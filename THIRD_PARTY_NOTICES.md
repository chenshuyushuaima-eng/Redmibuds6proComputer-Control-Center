# 协议参考与许可证

认证算法 `tools/BudsAuthentication.cs` 改写自：

- [RedmiBudsBar / Authentication.swift](https://github.com/ChristianVeneko/redmi-buds-bar/blob/main/Sources/BudsProtocol/Authentication.swift)，AGPL-3.0-or-later。
- [Gadgetbridge](https://codeberg.org/Freeyourgadget/Gadgetbridge)，Copyright (C) 2024 Jonathan Gobbo and the Gadgetbridge contributors，AGPL-3.0。

本项目的桌面应用、认证算法和诊断程序按 GNU Affero General Public License version 3 or later 提供。完整许可证见 LICENSE。协议帧、读写指令、设置定义和均衡器编码参考 RedmiBudsBar 的 Message.swift、Commands.swift、Models.swift、Parsers.swift、Equalizer.swift 与 Session.swift，以及 Gadgetbridge RedmiBuds5ProProtocol.java。桌面 WPF 界面、Windows 连接管理、应答检查及回读由本项目实现。

本项目为第三方个人项目，与小米官方没有隶属关系。

## 用户提供的托盘图标

`assets/tray-dark.ico` 由用户提供的黑色线条图案转换，对应原始 PNG 为 `assets/icon-source/black-lines.png`；`assets/tray-white.ico` 由白色线条图案转换，对应 `assets/icon-source/white-lines.png`。转换时保留透明背景、裁去图案之外的留白并生成多个图标尺寸，未生成新的图案。按用户确认，浅色系统任务栏使用黑线，深色系统任务栏使用白线。此处的代码许可证不代表对用户提供图案的权利作出额外授权。

主目录 `LICENSE` 提供完整的非官方中文译文，原有 AGPL 第三版英文条文完整保存在 `LICENSE.en`；正式授权以该英文原文为准。

## 用户提供的检测音频

本地开发使用的 `assets/fitness_detect.wav`（程序文件夹中的 `fitness_detect.wav`）从用户提供的 `com.mi.earphone` 1.38.0 APK 的 `raw/fitness_detect`，实际路径 `res/Pir.wav` 提取，用于这台耳机的个人贴合度检查。SHA-256：`57ace2d825a41ed4ce87c5b9d58202305442a283221239ba66c2c4bf5456a615`。公开源码和 Windows 包均不含该音频，也不包含 APK；仅提供本地提取工具 `tools/extract-fit-audio.ps1`。

该音频属于原应用资源，不属于本项目 AGPL 代码的授权范围，本项目没有授予其再分发许可。由使用者从自己的官方安装包提取，使用方法见音频准备说明。公开打包脚本始终排除该资源。Windows 播放时仅将 24 位 PCM 转为 16 位 PCM，不改变采样率、声道或增益。
