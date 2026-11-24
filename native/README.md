# Build the Windows x64 cores

Use Visual Studio 2019 16.11 or newer with the Desktop development with C++ workload, CMake 3.20 or newer, and an x64 zlib 1.2.13 development library built for MSVC. Run from the source archive root in an x64 Visual Studio Developer PowerShell. Supply the zlib include directory and the MSVC static library path.

```powershell
cmake -S native -B native-build -A x64 -DGDS_GBA=ON -DZLIB_INCLUDE_DIR="<zlib include>" -DZLIB_LIBRARY="<zlibstatic.lib>"
cmake --build native-build --config Release
cmake -S native -B native-ds-build -A x64 -DGDS_DS=ON
cmake --build native-ds-build --config Release
./build.ps1
```

The build writes `stage/boksol.exe` and copies both core DLLs into `stage/engines`. The matching Microsoft Visual C++ x64 runtime is required on the target machine or alongside the core DLLs. This source archive contains the engine code used by the build.

- VBA-M 2.2.3, commit `48ed03b02701b1cd72cbededeb07d9bc18bcee03`, https://github.com/visualboyadvance-m/visualboyadvance-m
- melonDS 1.1, commit `b86390e4428bf38ce4c1ce0e9ca446d6d25955e8`, https://github.com/melonDS-emu/melonDS
- ENet 1.3.18, commit `2662c0de09e36f2a2030ccc2c528a3e4c9e8138a`, https://github.com/lsalzman/enet
- zlib 1.2.13, https://github.com/madler/zlib/tree/v1.2.13

## Original multiplayer entry points

The following locations were checked against the ROM revisions in `src/Games.cs` using Capstone and scoped Ghidra decompilation. The addresses are specific to those revisions.

| Game ID | Observed hardware access | Startup in boksol |
| --- | --- | --- |
| U32J | Thumb at `0x08237AE0` writes the outgoing halfword to `0x0400012A`, then sets bit 7 in `SIOCNT`. | Restore the cable waiting checkpoint. Player 1 confirms the match, then both peers confirm the battle screen. |
| U33J | Thumb at `0x082226DC` performs the same send/start sequence. | Restore the cable waiting checkpoint. Player 1 opens course selection before both players configure their bikes. |
| ALKJ | ARM7 at `0x0238121C` writes to Wi-Fi mode/reset (`0x04808004`). ARM7 at `0x02386BD8` reads and checks the IPC FIFO status register. | Restore role-specific wireless checkpoints and enter the original wireless lobby. The host then sets the rules, and each player chooses their character and equipment. |

Ghidra confirmed that the GBA routines at `0x08238578` and `0x08222CA0` set the serial start bit. The DS routine beginning at `0x02386B9C` checks FIFO status before sending a packed word and returns separate results for FIFO error and full states. These routines identify the hardware boundaries used by the emulator cores.

`src/BattleLaunch.cs` coordinates the emulator entry points. Both peers must agree to use a checkpoint before either copies it into a session. Checkpoints are bound to the exact ROM hash and battery contents because they contain already-loaded profile state. When one side uses a different battery, both start with their selected batteries and use the original link menu.
