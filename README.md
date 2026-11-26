# 我太 boksol

boksol 是《续・我们的太阳》《新・我们的太阳》和《我们的太阳 DS》的 Windows 局域网对战客户端。它提供 ROM 槽位、六位联机码、自定义键位，以及内嵌对战、观战和录像。

配对和设置流程参考了 [彩斗 Saito](https://github.com/masuexe/saito)。boksol 使用随源码附带的 VBA-M 和 melonDS 封装来运行原作与联机流程。

## 使用

运行 `boksol.exe`，在「设置 → 游戏与汉化」导入自己的 GBA 或 DS ROM。同一局域网的两位玩家选择同一游戏版本和联机码后开始配对。存档选择、赛前操作和键位见 [使用说明](说明.txt)。

Windows 便携版内置三作的初始档。匹配版本的两款 GBA 游戏另带只含地区与时钟配置的新档记录，地区为日本东京，时钟从启动时的电脑本地时间开始。

## 从源码构建

需要 Windows x64、Visual Studio 的 .NET 桌面开发与 C++ 桌面开发组件、CMake 3.20 或更新版本和 x64 zlib 开发库。在 Visual Studio 的 x64 开发者 PowerShell 中，按 [原生核心构建说明](native/README.md) 编译两个核心，再在项目根目录执行 `./build.ps1`。程序输出至 `stage/boksol.exe`。运行程序需要 Microsoft Visual C++ x64 运行库。

| 路径 | 内容 |
| --- | --- |
| `src/` | C# WinForms 客户端、联机配对、对战启动与录像 |
| `native/` | VBA-M 与 melonDS 的原生 DLL 封装 |
| `vbam/`、`melonds/`、`native-deps/enet/` | 随源码提供的上游实现与授权文件 |
| `build.ps1`、`package.py` | 编译桌面程序和生成发布包 |

## 打包

使用 Python 3.9 或更新版本。已有独立 Git 元数据的上游目录还需要可用的 `git` 命令来读取跟踪文件；从源码 ZIP 解压后可直接再次打包。

```powershell
python package.py 0.5.4 --source
```

`outputs/boksol-0.5.4-github-source.zip` 可解压到一个空目录作为 GitHub 工作树。源码包包含构建脚本、说明和本地使用的上游源码快照。游戏 ROM、本地数据、游戏存档、模拟器快照、插图和编译产物保留在各自的本地目录。源码构建会使用客户端自带的备用品牌图案。

本地构建可在 `src/assets/` 放入以下可用的自备预置数据。使用匹配的 ROM 与存档内容时，配套快照可直接恢复对战入口。

| 作品 | 存档 | 可选对战快照 |
| --- | --- | --- |
| 续・我们的太阳 | `boktai2.sav` | `boktai2.start.sgm` |
| 新・我们的太阳 | `boktai3.sav` | `boktai3.start.sgm` |
| 我们的太阳 DS | `boktaids.sav` | `boktaids.host.start.ml1`、`boktaids.guest.start.ml1` |

如已有本地品牌资源，将 `solar.png` 和 `solar.ico` 放入 `src/`。`./build.ps1` 会编入存在的资源。源码构建也可以在应用内导入个人对战档。

编译完成并将 Visual C++ x64 运行库的 `msvcp140.dll`、`vcruntime140.dll` 和 `vcruntime140_1.dll` 放入 `stage/engines/` 后，可以运行 `python package.py 0.5.4` 生成 Windows 便携包。

## License / 开源协议

boksol 自有源代码采用 Mozilla Public License 2.0（MPL-2.0）。分发受 MPL-2.0 约束的文件或相应可执行程序时，按协议提供对应源代码。[许可证全文](LICENSE)

boksol's original source code is licensed under the Mozilla Public License 2.0 (MPL-2.0). [Full license text](LICENSE)

VBA-M、melonDS 和 ENet 沿用各自的上游许可证。[VBA-M](vbam/doc/License.txt) · [melonDS](melonds/LICENSE) · [ENet](native-deps/enet/LICENSE)

分发包含模拟器内核的构建时，适用相应的上游条款。MPL-2.0 第 3.3 节规定了与 GPL 许可代码组合分发的条件。版本和其他依赖见 [原生核心构建说明](native/README.md)。

游戏、品牌图案、游戏存档、模拟器快照和本地化资源中涉及的第三方权利归各自的权利人。
