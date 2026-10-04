# Mi Buds Control 0.1.7

Windows 上控制 REDMI Buds 6 Pro 国行耳机的第三方应用。

## 本版变化

- 修复 0.1.6 初始化查询超时导致的连接问题。
- 佩戴检测使用耳机运行信息读取；贴合度检查区分启停与结果指令。
- 关闭窗口时可选择完全退出或缩小到托盘，托盘图标跟随系统主题。
- 整理源码、音频提取工具、公开打包和 Windows 自动构建。
- 补齐程序文件版本，加入共同编辑 `123SSR-PNG` 署名。

## 下载

- `MiBudsControl-0.1.7-windows-x64.zip`：完整解压后运行 `MiBudsControl.exe`。
- `MiBudsControl-0.1.7-source.zip`：对应完整源码和构建说明。
- `SHA256SUMS.txt`：下载文件的 SHA-256。

适配范围：Windows 11 x64、REDMI Buds 6 Pro 国行、耳机固件 1.1.9.9。最新连接修正、贴合度检查效果、空间音频听感及其他设置仍需实机确认，设备重命名和固件升级尚未实现。

下载包不含官方 APK 和贴合度检测音频。贴合度检查需按包内 `fit-audio.md` 从自己的小米耳机 1.38.0 APK 提取音频；其他功能不需要该文件。当前程序未做 Authenticode 代码签名。

许可证：**AGPL-3.0-or-later**，附中文阅读译文、正式英文条文及第三方声明。

**ccsy.qn制作** · 共同编辑：[123SSR-PNG](https://github.com/123SSR-PNG)。本项目与小米官方没有隶属关系。
