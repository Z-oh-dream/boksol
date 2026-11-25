# 我太 boksol

boksol 是《续・我们的太阳》《新・我们的太阳》和《我们的太阳 DS》的对战客户端，支持 Windows 和 Android。它提供 ROM 槽位、六位联机码、自定义键位，以及局域网对战、远程对战、观战和录像。

配对和设置流程参考了 [彩斗 Saito](https://github.com/masuexe/saito)。boksol 使用随源码附带的 VBA-M 和 melonDS 封装来运行原作与联机流程。

## 使用

运行 `boksol.exe`，在「设置 → 游戏与汉化」导入自己的 GBA 或 DS ROM。同一局域网的两位玩家选择同一游戏版本和联机码后开始配对。存档选择、赛前操作和键位见 [使用说明](说明.txt)。

在「设置 → 联机与录像」填写名字，配对时可看到房间名单。跨网络对战时，所有玩家保存同一个信令服务器地址，再用同一个联机码配对。信令地址默认留空，清空保存即可切回双人局域网发现。《续・我们的太阳》和《我们的太阳 DS》的远程房间可设为 2、3 或 4 人，房主先开始配对。《新・我们的太阳》为双人对战。服务端的运行与部署方法见 [服务端说明](server/README.md)。

Windows 便携版内置三作的初始档。匹配版本的两款 GBA 游戏另带只含地区与时钟配置的新档记录，地区为日本东京，时钟从启动时的电脑本地时间开始。

## Android

Android 版支持 Android 8.0 或更新版本的 ARM64 设备。打开应用后，点击「导入 ROM」多选自己的 GBA 和 DS ROM。对战玩家选择同一 ROM 版本并输入相同联机码。首页的「联机设置」提供名字、信令服务器地址和房间人数。游戏支持虚拟按键、DS 下屏触控、键盘和手柄。

在 Windows 上安装 .NET 10 的 `android` 工作负载、JDK 21、Android SDK 36、Build Tools 36.0.0、NDK 28.2.13676358、CMake 和 Ninja。设置 `ANDROID_HOME` 和 `JAVA_HOME` 后运行以下命令。脚本使用本地调试密钥签名，APK 输出至 `outputs/`。下文列出的预置存档和快照可放在 `src/assets/` 随 APK 编译。

```powershell
./android/build.ps1
```

## 从源码构建

需要 Windows x64、Visual Studio 的 .NET 桌面开发与 C++ 桌面开发组件、CMake 3.20 或更新版本和 x64 zlib 开发库。在 Visual Studio 的 x64 开发者 PowerShell 中，按 [原生核心构建说明](native/README.md) 编译两个核心，再在项目根目录执行 `./build.ps1`。程序输出至 `stage/boksol.exe`。运行程序需要 Microsoft Visual C++ x64 运行库。

| 路径 | 内容 |
| --- | --- |
| `src/` | C# WinForms 客户端、联机配对、对战启动与录像 |
| `native/` | VBA-M 与 melonDS 的原生 DLL 封装 |
| `server/` | 独立信令服务器和游戏流量中继 |
| `vbam/`、`melonds/`、`native-deps/enet/` | 随源码提供的上游实现与授权文件 |
| `build.ps1`、`package.py` | 编译桌面程序和生成发布包 |

## 打包

使用 Python 3.9 或更新版本。已有独立 Git 元数据的上游目录还需要可用的 `git` 命令来读取跟踪文件；从源码 ZIP 解压后可直接再次打包。

```powershell
python package.py 0.5.4 --source
python package.py 0.5.4 --server
```

`outputs/boksol-0.5.4-github-source.zip` 可解压到一个空目录作为 GitHub 工作树。源码包包含构建脚本、说明和本地使用的上游源码快照。游戏 ROM、本地数据、游戏存档、模拟器快照、插图和编译产物保留在各自的本地目录。源码构建会使用客户端自带的备用品牌图案。

`outputs/boksol-0.5.4-server-source.zip` 是可单独构建与部署的服务端源码包。

本地构建可在 `src/assets/` 放入以下可用的自备预置数据。使用匹配的 ROM 与存档内容时，配套快照可直接恢复对战入口。

| 作品 | 存档 | 可选对战快照 |
| --- | --- | --- |
| 续・我们的太阳 | `boktai2.sav` | `boktai2.start.sgm` |
| 新・我们的太阳 | `boktai3.sav` | `boktai3.start.sgm` |
| 我们的太阳 DS | `boktaids.sav` | `boktaids.host.start.ml1`、`boktaids.guest.start.ml1` |

如已有本地品牌资源，将 `solar.png` 和 `solar.ico` 放入 `src/`。`./build.ps1` 会编入存在的资源。源码构建也可以在应用内导入个人对战档。

编译完成并将 Visual C++ x64 运行库的 `msvcp140.dll`、`vcruntime140.dll` 和 `vcruntime140_1.dll` 放入 `stage/engines/` 后，可以运行 `python package.py 0.5.4` 生成 Windows 便携包。

## 上游项目和授权

引擎版本、上游提交和原生构建配置见 [原生核心构建说明](native/README.md)。VBA-M、melonDS 和 ENet 保留原有授权文件。boksol 自有代码的发布授权尚待项目维护者确定。
