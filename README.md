# Mi Buds Control

在 Windows 上控制 REDMI Buds 6 Pro 国行耳机的第三方桌面应用，使用 C#、WPF 和 Windows 原生蓝牙 API。当前版本 **0.1.7**。

**适配范围：Windows 11 x64、REDMI Buds 6 Pro 国行、固件 1.1.9.9。** 协议参考小米耳机 Android 应用 `com.mi.earphone` 1.38.0；其他型号、地区和固件尚未确认。

## 功能

- 左右耳及充电盒电量；降噪、通透、关闭三种模式。
- 降噪强度连续滑块（设备内部 0–19），通透、人声增强、环境增强三种通透设置。
- 自适应降噪、个性化降噪、自适应听感。
- 左右耳单击、双击、三击、长按、滑动，以及长按模式循环。
- 音效预设、10 段自定义均衡器。
- 空间音频、头部追踪、经典／音乐／视频／游戏／有声书场景。
- 佩戴检测、双设备连接、自动接听电话开关。
- 耳机贴合度检查、左右耳结果、取消与超时处理；需要用户本地准备检测音频。
- 查找左右耳或双耳；托盘快捷菜单、自动重连、记住设备、可选开机启动。
- 关闭窗口时可选择完全退出或缩小到托盘；托盘图标跟随 Windows 系统深浅主题。

“已接入”表示已有协议和界面实现。连接、认证和配置读取已有实机记录；最新连接修正、贴合度检查效果、空间音频听感及其他设置仍需使用新版确认。**设备重命名和固件升级尚未实现**，当前未覆盖手机应用的全部功能。详细范围见 [功能清单](docs/screenshot-features.md)。

## 下载与使用

1. 从本仓库的 Releases 下载 `MiBudsControl-0.1.7-windows-x64.zip`，完整解压到可写文件夹。
2. 在 Windows 蓝牙设置中配对并连接耳机，退出手机上的“小米耳机”应用。
3. 若旧版仍在运行，先从系统托盘完全退出旧版，再双击 `MiBudsControl.exe`。
4. 顶部选择耳机并连接，在各页面调整设置。

公开下载包不包含官方 APK 或 `fitness_detect.wav`。其他功能不需要该音频；贴合度检查请先按 [音频准备说明](docs/fit-audio.md) 从自己的官方 APK 提取，并将 WAV 放到 EXE 同目录。

完整操作、连接问题和退出方式见 [使用说明](docs/usage.md)。

## 构建

在项目根目录使用 Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -OutputDirectory dist\0.1.7
```

构建使用系统 .NET Framework 4.8 编译器和 WinRT 元数据，无需额外 .NET SDK。也提供 `src/MiBudsControl.csproj`，可使用带 .NET Framework 4.8 开发工具的 Visual Studio 打开。项目使用 C# 5 兼容语法。

本地构建会在 `assets/fitness_detect.wav` 存在时复制它；没有音频也能编译。生成公开发布文件：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1
```

输出位于 `release/`：

- `MiBudsControl-0.1.7-source.zip`：完整源码及构建说明。
- `MiBudsControl-0.1.7-windows-x64.zip`：可运行程序、许可证、使用说明及音频提取工具。
- `SHA256SUMS.txt`：两个 ZIP 的 SHA-256。

打包脚本按明确文件清单收集源码，并使用 `-PublicRelease` 编译；即使本地有检测音频，也不会将其加入公开包。GitHub Actions 在 `main` 分支推送、拉取请求或手动触发时编译并上传构建产物。

## 项目结构

| 路径 | 内容 |
| --- | --- |
| `src/` | WPF 界面、连接管理、协议、偏好设置、音频播放 |
| `tools/` | 认证、WinRT 等待、构建辅助、音频提取、图标转换、只读诊断源码 |
| `assets/` | 托盘图标及用于转换的原始 PNG |
| `docs/` | 使用说明、协议依据、适配范围和 GitHub 发布指南 |
| `.github/workflows/` | Windows 编译与打包工作流 |
| `VERSION` | 打包使用的当前版本号 |

`dist/`、`release/`、本地历史存档和个人分析资料不提交到仓库。整理后的发布目录应作为仓库根目录上传，具体步骤见 [GitHub 发布指南](docs/github-publishing.md)。

## 文档与参与

- [版本记录](CHANGELOG.md)
- [贡献说明](CONTRIBUTING.md)
- [作者与共同编辑](AUTHORS.md)
- [发布检查记录](docs/publication-review.md)
- [强度调节协议](docs/noise-strength.md)
- [空间音频协议](docs/spatial-audio.md)
- [佩戴检测与贴合度检查](docs/wearing-fit.md)
- [早期实机可行性记录](docs/feasibility.md)

应用日志及偏好位于 `%LOCALAPPDATA%\MiBudsControl`。反馈问题时附上 Windows、耳机固件和应用版本，以及问题发生步骤；分享日志前请隐去设备地址及其他个人信息。

## 许可证与署名

代码使用 **AGPL-3.0-or-later**。完整中文阅读译文见 [LICENSE](LICENSE)，正式英文原文见 [LICENSE.en](LICENSE.en)，许可效力以英文原文为准。协议参考、图标和官方音频的来源与范围见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

本项目与小米官方没有隶属关系。

**ccsy.qn制作**

共同编辑：[123SSR-PNG](https://github.com/123SSR-PNG)。完整署名见 [AUTHORS.md](AUTHORS.md)。
