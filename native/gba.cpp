// SPDX-License-Identifier: MPL-2.0

#ifdef _WIN32
#include <windows.h>
#endif
#include <algorithm>
#include <chrono>
#include <cstring>
#include <string>
#include "core/base/system.h"
#include "core/gba/gba.h"
#include "core/gba/gbaFlash.h"
#include "core/gba/gbaGlobals.h"
#include "core/gba/gbaLink.h"
#include "core/gba/gbaRtc.h"
#include "core/gba/gbaSound.h"

#include "export.h"

namespace {
uint32_t buttons;
bool loaded;
bool frameReady;
std::string battery;
int audioCount;
int sunlight;
constexpr uint8_t darkness[] = {0xe8, 0xe0, 0xda, 0xd0, 0xc8, 0xc0, 0xb0, 0xa0, 0x88, 0x70, 0x50};
uint16_t audio[4096];

struct BufferedAudio final : SoundDriver {
    bool init(long) override { return true; }
    void pause() override {}
    void reset() override { audioCount = 0; }
    void resume() override {}
    void setThrottle(unsigned short) override {}
    void write(uint16_t* samples, int bytes) override {
        int count = std::min(bytes / 2, 4096 - audioCount);
        std::copy_n(samples, count, audio + audioCount);
        audioCount += count;
    }
};
}

int emulating;
CoreOptions coreOptions;
uint8_t systemColorMap8[0x10000];
uint16_t systemColorMap16[0x10000];
uint32_t systemColorMap32[0x10000];
uint16_t systemGbPalette[24];
int systemRedShift = 19, systemGreenShift = 11, systemBlueShift = 3;
int systemColorDepth = 32, systemVerbose, systemFrameSkip, systemSaveUpdateCounter, systemSpeed;
void (*dbgOutput)(const char*, uint32_t) = nullptr;
void (*dbgSignal)(int, int) = nullptr;
void log(const char*, ...) {}
void systemMessage(int, const char*, ...) {}
void systemScreenMessage(const char*) {}
void systemSetTitle(const char*) {}
bool systemPauseOnFrame() { return true; }
bool systemReadJoypads() { return true; }
uint32_t systemReadJoypad(int) { return buttons; }
uint32_t systemGetClock() {
#ifdef _WIN32
    return GetTickCount();
#else
    using namespace std::chrono;
    return static_cast<uint32_t>(duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count());
#endif
}
void systemGbPrint(uint8_t*, int, int, int, int) {}
void systemScreenCapture(int) {}
void systemDrawScreen() { frameReady = true; }
void systemSendScreen() {}
std::unique_ptr<SoundDriver> systemSoundInit() { return std::make_unique<BufferedAudio>(); }
void systemOnWriteDataToSoundBuffer(const uint16_t*, int) {}
void systemOnSoundShutdown() {}
void systemUpdateMotionSensor() {}
int systemGetSensorX() { return 0; }
int systemGetSensorY() { return 0; }
int systemGetSensorZ() { return 0; }
uint8_t systemGetSensorDarkness() { return darkness[sunlight]; }
void systemCartridgeRumble(bool) {}
void systemPossibleCartridgeRumble(bool) {}
void updateRumbleFrame() {}
bool systemCanChangeSoundQuality() { return true; }
void systemShowSpeed(int) {}
void system10Frames() {}
void systemFrame() {}
void systemGbBorderOn() {}

GDS void gds_close() {
    CloseLink();
    if (!loaded) return;
    if (systemSaveUpdateCounter != SYSTEM_SAVE_NOT_UPDATED) CPUWriteBatteryFile(battery.c_str());
    soundShutdown();
    CPUCleanUp();
    loaded = false;
}

GDS int gds_open(const char* path, const char* save, const char* state, int saveType, int) {
    gds_close();
    int size = CPULoadRom(path);
    if (!size) return 0;
    loaded = true;
    emulating = 1;
    buttons = 0;
    sunlight = 0;
    audioCount = 0;
    coreOptions = CoreOptions{};
    coreOptions.cheatsEnabled = 0;
    coreOptions.skipSaveGameCheats = 1;
    coreOptions.skipSaveGameBattery = 1;
    for (uint32_t i = 0; i < 0x10000; i++) {
        auto expand = [](uint32_t c) { return (c << 3) | (c >> 2); };
        systemColorMap32[i] = 0xff000000 | (expand(i & 31) << 16) |
            (expand((i >> 5) & 31) << 8) | expand((i >> 10) & 31);
    }
    if (saveType) coreOptions.saveType = coreOptions.cpuSaveType = saveType;
    else flashDetectSaveType(size);
    rtcEnable(true);
    soundInit();
    soundSetSampleRate(48000);
    CPUInit(nullptr, false);
    CPUReset();
    battery = save;
    CPUReadBatteryFile(save);
    if (state && *state && !CPUReadState(state)) {
        gds_close();
        return -1;
    }
    return 1;
}

GDS int gds_link(int host, const char* address, int port) {
    if (host < 0 || host > 3 || !address || port < 1 || port > 65535) return -1;
    IP_LINK_PORT = static_cast<uint16_t>(port);
    IP_LINK_RELAYED = std::strcmp(address, "127.0.0.1") == 0;
    EnableLinkServer(host != 0, host ? host : 1);
    EnableSpeedHacks(false);
    SetLinkTimeout(3500);
    if (host) IP_LINK_BIND_ADDRESS = address;
    else if (!SetLinkServerHost(address)) return -1;
    auto result = InitLink(LINK_CABLE_SOCKET);
    return result == LINK_OK ? 1 : result == LINK_NEEDS_UPDATE ? 0 : -1;
}

GDS int gds_poll_link() {
    char message[128]{};
    auto result = ConnectLinkUpdate(message, sizeof(message));
    return result == LINK_OK ? 1 : result == LINK_NEEDS_UPDATE ? 0 : -1;
}

GDS int gds_step(uint32_t keys, int light, int, uint32_t* pixels) {
    if (!loaded) return 0;
    buttons = keys;
    sunlight = std::clamp(light, 0, 10);
    frameReady = false;
    GBASystem.emuMain(GBASystem.emuCount);
    if (!frameReady) return 0;
    const auto* source = reinterpret_cast<uint32_t*>(g_pix) + 241;
    for (int y = 0; y < 160; y++)
        std::copy_n(source + y * 241, 240, pixels + y * 240);
    return 1;
}

GDS int gds_audio(uint16_t* samples) {
    std::copy_n(audio, audioCount, samples);
    int count = audioCount;
    audioCount = 0;
    return count;
}
