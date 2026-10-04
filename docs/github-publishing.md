# GitHub 发布指南

## 上传源码

整理后的 `github发布` 文件夹就是仓库根目录。进入该目录，上传其中的 `src/`、`tools/`、`assets/`、`docs/`、`.github/`，以及根目录说明、许可证、版本号和构建脚本。

使用 Git 推送时，在此目录运行：

```powershell
gh auth login --hostname github.com --git-protocol https --web --scopes user:email
git init -b main
git add .
git commit -m "Prepare Mi Buds Control 0.1.7 for publication"
gh repo create MiBudsControl --public --source . --remote origin --push
```

首次提交前配置仓库的 Git 用户名和邮箱。Git 提交邮箱与 GitHub 登录身份分别核对，确认使用项目维护者指定的帐号。帐号关联邮箱通过已登录的 GitHub API 核对，凭据不写入项目文件；公开提交可使用 GitHub 提供的 `noreply` 邮箱。

若 `MiBudsControl` 仓库已经存在，先确认所有者和仓库内容，再连接实际远端，避免误用其他仓库。当前完整应用使用 **AGPL-3.0-or-later**，仓库创建时保留项目已有许可证。

`.gitignore` 会排除 `release/`、`dist/`、历史存档、APK、官方音频和个人日志。网页上传不使用 `.gitignore` 过滤文件：请上传上述源码目录及根文件，**不要把 `release/` 整个文件夹一起上传到源码仓库**。使用 Git 可同时保留 `.github` 等隐藏文件。

## 创建 Release

当前可发布文件位于 `github发布/release/`：

| 文件 | 用途 |
| --- | --- |
| `MiBudsControl-0.1.7-windows-x64.zip` | Windows 用户下载，解压运行 |
| `MiBudsControl-0.1.7-source.zip` | 与该二进制包对应的完整源码 |
| `SHA256SUMS.txt` | 两个下载包的 SHA-256 |

推送源码后，在 GitHub 创建 `v0.1.7` 标签和 Release，附上以上三个文件。保留对应源码、中文 LICENSE、英文 LICENSE.en 和第三方声明。

也可在仓库根目录使用 GitHub CLI：

```powershell
gh release create v0.1.7 release\MiBudsControl-0.1.7-windows-x64.zip release\MiBudsControl-0.1.7-source.zip release\SHA256SUMS.txt --target main --title "Mi Buds Control 0.1.7" --notes-file docs\release-0.1.7.md
```

建议在发布说明中注明：

- 适配 Windows 11 x64、REDMI Buds 6 Pro 国行、固件 1.1.9.9。
- 0.1.7 修复 0.1.6 初始化查询导致的连接问题。
- 最新连接修正、贴合度检查效果和空间音频听感仍需使用新版确认。
- 下载包不含官方检测音频；贴合度检查需从自己的小米耳机 1.38.0 APK 提取。包内附提取工具和说明。

历史版本留在本地原项目的 `dist/`，0.1.4 源码基线留在 `history/0.1.4-source/`。这些原始存档不放入新仓库或当前公开包，其中的官方音频和旧资料也不随本次发布。

## 共同编辑与协作者

`123SSR-PNG` 已列入 [AUTHORS.md](../AUTHORS.md)、README 及源码和 Windows 下载包。创建仓库后，在 GitHub 的 **Settings → Collaborators → Add people** 中邀请 `123SSR-PNG`，授予写入权限。

使用 CLI 时将下面的 `YOUR_ACCOUNT` 替换为实际仓库所有者：

```powershell
gh api --method PUT repos/YOUR_ACCOUNT/MiBudsControl/collaborators/123SSR-PNG -f permission=push
```

邀请发出后需要对方接受；文档署名不代表邀请已经发送或权限已经生效。

## 许可证和代码签名

认证代码明确移植自 AGPL 项目，本次经项目维护者确认按 **AGPL-3.0-or-later** 发布。以后若要使用 MIT 或 Apache 2.0，需要取得相关上游授权，或替换受限代码并重新核对来源；仅修改许可证文件不满足这个要求。

当前 EXE 和 PowerShell 脚本没有 Authenticode 签名。SHA-256 用于校验下载内容，不是发布者证书。证书检查结果见 [发布检查记录](publication-review.md)；签名私钥和 GitHub 凭据不提交到仓库。

## 重新打包

在源码根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1
```

版本号读取 `VERSION`。发布新版本前同步更新 `VERSION`、`src/AssemblyInfo.cs`、`src/app.manifest`、说明和版本记录；Windows 自动构建工作流只生成产物，不自动创建 Release。
