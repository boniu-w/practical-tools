# 淘汰 WPF 清理清单

工作分支：`ava` · 编写日期：2026-10-30 · 状态：待执行（本次未删除任何 WPF 项目）

前提：Avalonia 平台验收完成，或明确接受提前删除。删除前先创建一个可回退的提交/标签，并备份旧数据库与保护密钥。

## 0. 最容易漏的三件事

- [ ] `PracticalTools.Avalonia/PracticalTools.Avalonia.csproj` 的 `<ApplicationIcon>` 仍指向 `../PracticalTools/Assets/app_icon.ico`。必须先复制图标并改路径，否则删除 WPF 项目后 Avalonia 直接构建失败。
- [ ] 不要删除 Core 的 DPAPI 旧数据路径：`LegacyDpapiSecretProtector`、`CryptoHelper`、`System.Security.Cryptography.ProtectedData` 包以及 `Migration/LegacyDatabaseReader` 仍用于在 Windows 上读取旧 WPF 数据库和创建迁移包。
- [ ] 不要顺带重命名 Core 的 `PracticalTools.*` 命名空间。它跟 WPF 项目无关，重命名只会扩大风险。

## 1. 删除前的前置动作

### 1.1 图标与资源

- [ ] 将 `PracticalTools/Assets/app_icon.ico` 复制到 `PracticalTools.Avalonia/Assets/app_icon.ico`。
- [ ] 修改 `PracticalTools.Avalonia/PracticalTools.Avalonia.csproj` 的 `<ApplicationIcon>` 指向新路径。
- [ ] 用 `dotnet publish ... -r win-x64` 检查生成的 exe 图标正常。
- [ ] 决定是否保留窗口/托盘图标。WPF 的 `PracticalTools/MainWindow.xaml.cs` 使用 `Assets/icons8-cancer-48.png`；Avalonia 当前没有对应托盘图标，若需要应先移植。
- [ ] 检查 `PracticalTools/Assets` 其余图标是否还有产品价值；没有则随项目删除。

### 1.2 README 截图

- [ ] 当前 `Readme.md` 顶部两张图 `img\屏幕截图 2026-02-26 105053.png`、`img\屏幕截图 2026-02-26 105209.png` 都是旧 WPF 界面。
- [ ] 用 Avalonia 截图替换（建议：首页、重复扫描、密钥库、解密预览各一张），或删除图片段落。
- [ ] 更新图片文件名与 Markdown 路径；确认不再引用 `img/` 下的 WPF 截图。

### 1.3 测试与功能取舍

- [ ] 盘点 `TestProject` 中只存在于旧版的用例：`PgpTest.TestSetKeyExpiration`、`TestGetKeyFingerprints`、`CalculateRemainingLife`、`FileUnitTest`、`DecryptionPreviewTest`。与 `PracticalTools.Core.Tests` 对照后，把仍未覆盖的场景迁到 Core/Avalonia 测试。
- [ ] `TestProject/DecryptionPreviewUiTest.cs` 依赖 WPF + WebView2，Avalonia 已有替代预览测试，可直接随项目删除。
- [ ] 明确接受以下 WPF 专属能力随项目移除（当前计划已列为暂缓或无 UI）：
  - Excel 导入/导出辅助方法
  - 签名/验签界面
  - 已有密钥有效期/口令修改界面
  - WebView2 预览实现
  - WPF 的文件分类预设与全盘枚举入口
- [ ] 若其中某项仍要保留，先移植到 Avalonia/Core，再执行删除。

### 1.4 旧数据兼容确认

- [ ] 确认 Avalonia 的旧库/迁移包导入仍可用：在 Windows 下用一份旧库副本走完“导出迁移包 → 预览 → 导入”。
- [ ] 保留 `.zip.gpg` 文件夹归档格式、迁移包格式和旧归档互读能力；这些不随 WPF 删除。
- [ ] 更新 `docs/data-migration.md`：说明旧 WPF 入口已淘汰，但旧安装/旧库仍可按该文档备份后迁移。

## 2. 移除解决方案中的项目

- [ ] 从 `PracticalTools.sln` 移除 `PracticalTools` 与 `TestProject`（含各自的 ProjectConfigurationPlatforms GUID 配置块）。可用：
  ```powershell
  dotnet sln PracticalTools.sln remove PracticalTools/PracticalTools.csproj TestProject/TestProject.csproj
  ```
- [ ] 确认 `dotnet sln PracticalTools.sln list` 只剩 `PracticalTools.Core`、`PracticalTools.Avalonia`、两个测试项目。
- [ ] 删除目录 `PracticalTools/` 和 `TestProject/`。
- [ ] 执行 `git rm` 清理仍被跟踪的杂项：
  - `PracticalTools/PracticalTools.csproj.user`
  - `PracticalTools/Temp/MainWindow.xaml.temp`
  - `PracticalTools/Temp/Temp.txt`
  - `PracticalTools/ReadMe.md`
  - `TestProject/shengChengQianMing.md`（如无保留价值）
- [ ] `PracticalTools.Avalonia.NativeSmokeTests` 不在 `.sln` 中，不受影响；如需要可单独构建。

## 3. 清理引用与文档

### 3.1 构建与脚本

- [ ] `.gitignore` 删除已失效的 `PracticalTools/bin`、`PracticalTools/obj`、`TestProject1/bin`、`TestProject1/obj`；保留通用 `*/bin`、`*/obj`。
- [ ] 全仓库确认没有脚本引用 WPF 项目：
  ```bash
  grep -rn "PracticalTools/PracticalTools.csproj\|TestProject\|net8.0-windows\|UseWPF" \
    --include="*.sln" --include="*.csproj" --include="*.ps1" --include="*.py" --include="*.md" .
  ```
- [ ] `scripts/publish-release.ps1`、`scripts/package-release.py` 只发布 Avalonia，无需改动；删除后重跑一次确认。
- [ ] `git-push.ps1` 与项目无关，无需改动。

### 3.2 当前文档

- [ ] `Readme.md`：
  - 删除旧 WPF 的运行/构建/发布命令（`dotnet run --project PracticalTools/...`、`dotnet publish PracticalTools/...`）。
  - 项目列表移除 `PracticalTools`。
  - 保留 Avalonia 启动、`--data-dir`、发布打包命令。
  - 替换旧截图（见 1.2）。
- [ ] `迁移到Avalonia跨平台改造计划书.md`：
  - 项目表移除 `PracticalTools` 与 `TestProject`。
  - 第 1 节改为“Avalonia 单入口 + Core”，删除“两个桌面入口”描述。
  - 第 5 节删除 WPF 保留/淘汰说明，改为“WPF 已移除”。
  - 第 6 节删除 `dotnet build PracticalTools/PracticalTools.csproj` 和 WPF Release 构建行。
  - 删除“`PracticalTools/PracticalTools.csproj` 仍是 WPF 项目，不能发布 macOS”的段落。
  - 标题可改为“Avalonia 跨平台改造计划”，或保留原标题作为历史。
- [ ] `docs/avalonia-migration.md`：新增一条“WPF 项目已移除”记录，保留此前历史记录不回改。
- [ ] `docs/查看加密文件.md`：内容描述的是 WPF WebView2 入口，已过时；删除，或改写为指向 `docs/decryption-preview.md`。
- [ ] `docs/decryption-preview.md`、`docs/folder-crypto.md`、`docs/duplicate-scanning.md`、`docs/remaining-tools.md`、`docs/secret-protection.md`：保留与旧格式/旧数据兼容有关的说明，更新“原 WPF 保持原实现/原 WPF 继续使用”等当前时态措辞。
- [ ] 删除 `PracticalTools/ReadMe.md`（随项目目录删除）。

## 4. 本地产物清理

- [ ] 删除 `publish/`（旧 WPF 发布目录，当前约 183 MB，已被 `.gitignore` 忽略）。
- [ ] 删除 `PracticalTools/bin`、`PracticalTools/obj`、`TestProject/bin`、`TestProject/obj`。
- [ ] 可删除本地缓存 `PracticalTools.sln.DotSettings.user`、`.vs/`、`.idea/`（均不参与构建）。
- [ ] 保留 Avalonia 的 `publish-win-x64/`、`publish-mac-arm64/`、`publish-linux-x64/` 和 `publish-release-*`。

## 5. 必须保留的 Core 兼容代码

删除 WPF 项目**不等于**删除旧数据兼容：

- [ ] `PracticalTools.Core/Security/LegacyDpapiSecretProtector.cs`
- [ ] `PracticalTools.Core/util/CryptoHelper.cs`
- [ ] `PracticalTools.Core/Migration/LegacyDatabaseReader.cs`
- [ ] `PracticalTools.Core/Migration/MigrationPackageService.cs`
- [ ] `PracticalTools.Core/Sqlite/MyDbContext.cs` 中默认 DPAPI 兼容路径
- [ ] `PracticalTools.Core/PracticalTools.Core.csproj` 的 `System.Security.Cryptography.ProtectedData` 包
- [ ] 相应测试：`PracticalTools.Core.Tests/SecretProtectionTests.cs` 中的 DPAPI 用例

只有在决定**不再支持从旧 WPF 数据库导入**时，才单独评估删除上述路径及迁移文档。

## 6. 删除后的验证

```powershell
dotnet build PracticalTools.sln -c Release
dotnet test PracticalTools.Core.Tests/PracticalTools.Core.Tests.csproj -c Release
dotnet test PracticalTools.Avalonia.Tests/PracticalTools.Avalonia.Tests.csproj -c Release
./scripts/publish-release.ps1 -Version 0.7.0
```

- [ ] 解决方案构建通过，且 Avalonia 图标正常。
- [ ] Core 测试仍为 130 通过 / 1 跳过；Avalonia 33/33。
- [ ] 三平台发布打包成功，校验和一致。
- [ ] 启动 Avalonia：首次激活、`--data-dir`、密钥库、加解密、预览各做一次冒烟。
- [ ] 全仓库搜索确认只剩历史文档中的 WPF 字样，没有构建/运行引用。

## 7. 建议的执行顺序

1. 提交 1.1–1.4 的前置改动（图标、截图、测试取舍、旧数据验证）。
2. 单独提交“从解决方案移除 WPF/TestProject + 删除目录”。
3. 单独提交文档清理与 `.gitignore` 调整。
4. 执行第 6 节验证；失败可用第 0 步的标签回退。
