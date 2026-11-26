// SPDX-License-Identifier: MPL-2.0

#include <algorithm>
#include <cstring>
#include <memory>
#include <string>
#include <vector>
#include "Args.h"
#include "LAN.h"
#include "NDS.h"
#include "Platform.h"
#include "Savestate.h"

#include "export.h"
using namespace melonDS;

LAN* gdsLan = nullptr;
std::string gdsBatteryPath;
bool gdsStopped = false;

namespace {
std::unique_ptr<NDS> console;
std::unique_ptr<LAN> lan;

std::vector<u8> readFile(const char* path, u64 maxSize) {
    using namespace Platform;
    if (!path || !*path) return {};
    auto* file = OpenFile(path, FileMode::Read);
    if (!file) return {};
    const auto size = FileLength(file);
    std::vector<u8> data;
    if (size > 0 && size <= maxSize) {
        data.resize(static_cast<size_t>(size));
        if (FileRead(data.data(), 1, size, file) != size) data.clear();
    }
    CloseFile(file);
    return data;
}
}

GDS void gds_close() {
    if (console) console->Stop(Platform::StopReason::External);
    console.reset();
    gdsLan = nullptr;
    if (lan) lan->EndSession();
    lan.reset();
}

GDS int gds_open(const char* path, const char* save, const char* state, int, int role) {
    gds_close();
    auto rom = readFile(path, 512 * 1024 * 1024);
    if (rom.empty()) return 0;
    gdsStopped = false;
    gdsBatteryPath = save;
    auto backup = readFile(save, 1024 * 1024);
    NDSCart::NDSCartArgs cartArgs;
    if (!backup.empty()) {
        cartArgs.SRAMLength = static_cast<u32>(backup.size());
        cartArgs.SRAM = std::make_unique<u8[]>(backup.size());
        std::copy(backup.begin(), backup.end(), cartArgs.SRAM.get());
    }
    auto game = NDSCart::ParseROM(rom.data(), static_cast<u32>(rom.size()), nullptr, std::move(cartArgs));
    if (!game) return 0;
    NDSArgs config;
    if (role) {
        config.Firmware.GetHeader().MacAddr = {0, 9, 0xbf, 0x31, 0x22, static_cast<u8>(role)};
        config.Firmware.UpdateChecksums();
    }
    console = std::make_unique<NDS>(std::move(config));
    console->SetNDSCart(std::move(game));
    console->Reset();
    console->SetupDirectBoot("boktaids");
    console->Start();
    if (state && *state) {
        auto checkpoint = readFile(state, 64 * 1024 * 1024);
        if (checkpoint.empty()) { gds_close(); return -1; }
        Savestate file(checkpoint.data(), checkpoint.size(), false);
        if (file.Error || !console->DoSavestate(&file) || file.Error) { gds_close(); return -1; }
    }
    return 1;
}

GDS int gds_link(int host, const char* address, int port) {
    if (host < 0 || host > 3 || !address || port < 1 || port > 65535) return -1;
    bool relayed = std::strcmp(address, "127.0.0.1") == 0 && (host || port != 7064);
    lan = std::make_unique<LAN>(relayed);
    const auto linkPort = static_cast<u16>(port);
    bool started = host
        ? lan->StartHost("GDS-1P", host + 1, linkPort)
        : lan->StartClient("GDS-2P", address, linkPort);
    if (!started) { lan.reset(); return -1; }
    gdsLan = lan.get();
    return 0;
}

GDS int gds_poll_link() {
    if (!lan) return -1;
    lan->Process();
    const auto players = lan->GetPlayerList();
    if (players.size() != static_cast<size_t>(lan->GetMaxPlayers())) return 0;
    if (std::count_if(players.begin(), players.end(), [](const auto& player) {
            return player.Status == LAN::Player_Client;
        }) != lan->GetMaxPlayers() - 1) return 0;
    lan->Process(); // Flush the completed roster before the host waits on the control channel.
    return 1;
}

GDS int gds_step(uint32_t keys, int x, int y, uint32_t* pixels) {
    if (!console || gdsStopped) return -1;
    if (lan) lan->Process();
    console->SetKeyMask(~keys);
    if (x >= 0 && y >= 0) console->TouchScreen(static_cast<u16>(x), static_cast<u16>(y));
    else console->ReleaseScreen();
    console->RunFrame();
    const int front = console->GPU.FrontBuffer;
    for (int screen = 0; screen < 2; screen++) {
        const auto* screenPixels = console->GPU.Framebuffer[front][screen].get();
        if (!screenPixels) return 0;
        std::copy_n(screenPixels, 256 * 192, pixels + screen * 256 * 192);
    }
    return 1;
}

GDS int gds_audio(int16_t* samples) {
    return console ? console->SPU.ReadOutput(samples, 2048) * 2 : 0;
}
