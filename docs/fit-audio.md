# 贴合度检测音频准备

公开源码和 Windows 下载包不含官方检测音频。应用的其他功能可以直接使用；贴合度检查需要先将 `fitness_detect.wav` 放到 `MiBudsControl.exe` 同目录。

## 从自己的官方 APK 提取

当前提取工具适配小米耳机 Android 应用 `com.mi.earphone` **1.38.0** 的 APK：资源 `raw/fitness_detect` 对应压缩包中的 `res/Pir.wav`。其他应用版本可能改变资源路径。

源码和 Windows 下载包都附带 `tools/extract-fit-audio.ps1`。完整解压后，在项目或程序根目录打开 Windows PowerShell：

```powershell
# 将音频放到当前项目或程序根目录，与 EXE 放在一起
powershell -NoProfile -ExecutionPolicy Bypass -File tools\extract-fit-audio.ps1 -ApkPath 'C:\path\base.apk' -OutputDirectory '.'
```

将示例 APK 路径替换为自己的文件。`-OutputDirectory` 的相对路径基于工具所在的项目／程序根目录，也可以使用 EXE 所在文件夹的绝对路径。

开发时可以先提取到 `assets/`，随后进行本地构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\extract-fit-audio.ps1 -ApkPath 'C:\path\base.apk'
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -OutputDirectory dist\0.1.7
```

或直接写入已有的本地构建目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\extract-fit-audio.ps1 -ApkPath 'C:\path\base.apk' -OutputDirectory 'dist\0.1.7'
```

已有同名音频时工具默认拒绝覆盖；确需替换可添加 `-Force`。工具只读取 ZIP 中指定资源并检查 WAV 文件头，不安装或执行 APK。

## 开始检查

准备好音频后，在“更多设置”确认输出设备为 REDMI Buds 6 Pro，佩戴双耳、暂停其他音频，在安静环境点击“开始检查”。程序收到耳机就绪通知后播放音频，并等待左右耳结果。

若提示找不到 `res/Pir.wav`，请核对 APK 版本；不要用普通歌曲代替检测音频。没有耳机音频输出、没有检测资源或设备无响应时，程序无法完成检查。

## 来源与分发

该音频是官方应用资源，**不在本项目代码的 AGPL 授权范围内**。本项目不授予其再分发许可，公开包仅提供提取工具。不要将自己的 APK 或提取后的 WAV 上传到项目仓库或 Releases。

已知 1.38.0 资源的 SHA-256：`57ace2d825a41ed4ce87c5b9d58202305442a283221239ba66c2c4bf5456a615`。原音频为立体声、44100 Hz、24 位 PCM；应用在 Windows 播放时转换为 16 位 PCM，不改变采样率、声道或增益。

`tools/package.ps1` 始终使用公开构建，不打包本地检测音频。来源与许可详见 `THIRD_PARTY_NOTICES.md`。
