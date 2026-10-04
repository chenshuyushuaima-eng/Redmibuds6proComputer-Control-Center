# 贡献说明

## 开发环境

- Windows 11 x64，系统 .NET Framework 4.8 和 WinRT 元数据。
- Windows PowerShell；可选带 .NET Framework 4.8 开发工具的 Visual Studio。
- C# 5 兼容语法；构建入口为根目录 `build.ps1`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -OutputDirectory dist\dev
```

请使用独立输出目录，避免覆盖正在运行的应用或历史版本。检测音频是用户本地资源，缺少它不会影响编译。

## 协议和界面修改

- 在问题或拉取请求中注明耳机型号、地区、固件和参考手机应用版本。
- 协议字段需有来源依据，注明来源与许可，并区分静态分析结论和实机观察。
- 不将其他耳机型号的配置范围直接套用到 REDMI Buds 6 Pro；保留未知值，不以默认值覆盖未读取的设备状态。
- 写入设置需检查应答，可读设置应回读确认；兼顾断线、取消和错误提示。
- 文档只描述已实现内容，并明确尚待实机确认的部分。
- 不提交 APK、反编译输出、官方检测音频、设备地址、个人日志、生成的 EXE 或历史发布目录。
- 签名证书私钥、密钥库、访问令牌和本地 `.env` 文件保存在仓库之外；共同编辑名单见 [AUTHORS.md](AUTHORS.md)。

公开打包使用 `tools/package.ps1`。新增需要分发的源文件或资源时，同时更新其中的文件清单和第三方声明。

## 反馈问题

提供应用版本、Windows 版本、耳机固件、复现步骤和错误文字。日志位于 `%LOCALAPPDATA%\MiBudsControl`；公开分享前隐去设备地址和其他个人信息。不要提交官方 APK 或固件包。

贡献代码按项目的 AGPL-3.0-or-later 许可证提供；详见 [LICENSE](LICENSE)、[LICENSE.en](LICENSE.en) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
