// SPDX-License-Identifier: MPL-2.0

#include <algorithm>
#include <memory>
#include <string>
#include <vector>
#include "Args.h"
#include "LAN.h"
#include "NDS.h"
#include "Platform.h"
#include "Savestate.h"

#define GDS extern "C" __declspec(dllexport)
using namespace melonDS;

LAN* gdsLan = nullptr;
std::string gdsBatteryPath;
bool gdsStopped = false;

namespace {
std::unique_ptr<NDS> console;
std::unique_ptr<LAN> link;

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
    if (link) link->EndSession();
    link.reset();
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

GDS int gds_link(int host, const char* address, int) {
    link = std::make_unique<LAN>();
    bool started = host ? link->StartHost("GDS-1P", 2) : link->StartClient("GDS-2P", address);
    if (!started) { link.reset(); return -1; }
    gdsLan = link.get();
    return 0;
}

GDS int gds_poll_link() {
    if (!link) return -1;
    link->Process();
    const auto players = link->GetPlayerList();
    if (players.size() != 2) return 0;
    if (std::none_of(players.begin(), players.end(),
                     [](const auto& player) { return player.Status == LAN::Player_Client; })) return 0;
    link->Process(); // Flush the completed roster before the host waits on the control channel.
    return 1;
}

GDS int gds_step(uint32_t keys, int x, int y, uint32_t* pixels) {
    if (!console || gdsStopped) return -1;
    if (link) link->Process();
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
