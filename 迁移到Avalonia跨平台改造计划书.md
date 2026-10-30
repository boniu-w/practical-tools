# 实用工具：WPF → Avalonia 跨平台迁移计划

更新日期：2026-10-30 · 工作分支：`ava` · 当前进度：前五步、重复扫描、文件夹加解密及基础解密预览已实现；XLSX 工作表及 PDF 分页预览已接入，下一项为音视频预览。

本文件维护当前架构、待办顺序与验收条件；[迁移记录](docs/avalonia-migration.md)保存各次实施与验证历史。两者出现差异时先核对代码与最近验证结果，再同步文档。历史测试通过不等于所有平台实机通过。

## 1. 目标与迁移方式

目标平台为 Windows、macOS（优先 Apple Silicon / `osx-arm64`）和 Linux。Linux 发行版与最低版本尚未确定；如需 CentOS，必须明确具体版本并验证 .NET、Avalonia 和原生依赖兼容性。

采用两个桌面入口共用 Core 的渐进迁移方式，当前没有将原 WPF 项目直接转换为 Avalonia：

| 项目 | 当前职责 | 状态 |
|---|---|---|
| `PracticalTools` | 原 WPF 完整版，`net8.0-windows` | 保留，仅 Windows |
| `PracticalTools.Core` | `net8.0`：GPG 核心、数据模型、EF 上下文、激活服务、存储基础设施 | 已抽取一部分，仍保留 Windows DPAPI 兼容路径 |
| `PracticalTools.Avalonia` | `net8.0`：新桌面入口、首页、GPG 操作页、激活窗口 | 迁移预览版 |
| `PracticalTools.Core.Tests` | GPG、存储、迁移、扫描与文件夹归档测试 | 108 项：107 通过、1 因权限跳过 |
| `PracticalTools.Avalonia.Tests` | 无界面窗口测试 | 最近验证 23/23 通过 |
| `TestProject` | 原 Windows 测试项目 | 保留，尚未整体跨平台迁移 |

当前实际选用 Avalonia 11.3.21、Fluent 主题、Inter 字体、异步 StorageProvider 文件选择器；密钥库使用兼容的 DataGrid 11.3.13（该包没有 11.3.21 版本）。没有安装 Semi.Avalonia。WPF 原有 MaterialDesign 包继续留在原项目中。

## 2. 当前功能状态

| 功能 | Avalonia 现状 | 还缺什么 |
|---|---|---|
| 应用入口、首页、导航 | 已实现 | 后续功能按需接入 |
| 文件加密、解密 | 共享核心，支持库中选钥与手工输入 | 各平台实机验收 |
| 密钥输入 | 单钥文件、旧库迁移包、新版迁移包均可导入；文件操作可手工粘贴 | 实机验收 |
| 密钥生成 | RSA 2048/3072/4096、有效天数、自动入库、公私钥导出 | 已有密钥的过期签名修改未迁移 |
| 生成密钥保存 | 自动持久化，重启后可选用，也可用迁移包转移 | 密钥轮换 |
| 输出文件保护 | 拒绝覆盖和同路径，成功后才提交临时输出 | 持续覆盖异常与平台差异 |
| 数据库初始化 | 用户目录中的 SQLCipher 数据库，支持旧数据显式导入 | 完整版本化升级 |
| 跨平台口令存储 | AES-GCM、本地保护密钥、转换器隔离、旧口令通过包迁移并重新加密 | 轮换未实现 |
| 激活 | 原规则、新库记录、激活窗口 | 原平台实机启动验收 |
| DI、日志、单实例 | 已接入启动与退出流程 | 跨进程及各平台验收 |
| 密钥库列表、编辑、详情、删除 | 已实现，编辑名称和说明、删除需确认 | 私钥口令修改等扩展能力 |
| 旧库与跨机器迁移 | 四类记录的加密包导出、预览、事务导入；重复跳过，不迁移激活 | 其他表/实际文件不迁移，平台实测待完成 |
| 重复文件扫描 | 多目录、内容判重、过滤/取消、智能选择、确认移除、只读记录 | 链接测试与原生回收站等平台验收未完成 |
| 文件管理、黑名单 | 原 WPF 入口保留 | 服务梳理与 Avalonia UI |
| 文件夹加解密 | ZIP + GPG、相对结构/空目录、新旧互读、进度/取消、私有暂存及安全解压 | 平台实测，完整文件系统元数据不在范围内 |
| 解密预览 | 文本、PNG/JPEG、DOCX 正文、XLSX 工作表内容、PDF 分页/缩放；原生容器、限制/取消及正常清理 | 音视频等格式；崩溃遗留清理 |
| Excel、签名、验签等其余能力 | 尚未完成 Avalonia 对应入口梳理 | 按原版实际调用清单逐项验收 |
| Mac 发布 | `osx-arm64` 自包含目录发布通过 | 实机运行、`.app`、签名、公证 |

已采用的页面映射：

| 原功能/入口 | 当前 Avalonia 实现 |
|---|---|
| `App.xaml` / `App.xaml.cs` | `PracticalTools.Avalonia/App.axaml` / `.axaml.cs` |
| 主窗口、首页、GPG 页签容器 | `PracticalTools.Avalonia/MainWindow.axaml` |
| `EncryptionPage` + `DencryptionPage` | 共用 `Views/FileCryptoPage.axaml`，通过 `Decrypt` 区分模式 |
| `GenerateKeyPair` | `Views/GenerateKeyPage.axaml` |
| `ActivationWindow` | `Views/ActivationWindow.axaml` |
| 密钥管理表格及详情/元数据编辑 | `Views/KeyLibraryPage.axaml` |
| 密钥文件导入 | `Views/KeyImportWindow.axaml` |
| 旧数据库迁移/跨机器迁移 | `Views/MigrationPage.axaml` |
| 重复文件扫描与记录 | `Views/DuplicateScanPage.axaml`，新共享 Scanning 服务，原 WPF 服务不变 |
| `EncryptionFolderPage` + `DencryptionFolderPage` | 共用 `Views/FolderCryptoPage.axaml`，通过 `Decrypt` 区分模式；共享 FolderCryptoService |

不要求逐个复制原 WPF 文件。新页面按职责组织；原 MainViewModel 尚未拆分，新功能不继续依赖这个 WPF ViewModel。

## 3. 已完成里程碑

### 第一步：跨平台入口与 GPG 基础功能

- [x] 建立独立 Avalonia 项目和 `net8.0` Core。
- [x] 移动 `GpgHelper`，两个入口共用原命名空间和公开 API。
- [x] 首页、单文件加密/解密、密钥生成与导出。
- [x] 后台运算、状态提示、输出文件保护。
- [x] GPG 核心测试与 Mac ARM64 发布验证。

### 第二步：共享数据层与应用生命周期

- [x] 抽取上下文、相关模型、GPG 数据服务和激活服务。
- [x] 用户数据目录、SQLCipher 初始化及缺失激活表的补建。
- [x] DI、首次激活与激活记录持久化。
- [x] 日志、实例文件锁、退出时资源释放。
- [x] 非 Windows 访问非空旧 DPAPI 口令时明确报错。
- [x] 存储集成测试和激活/主窗口无界面测试。

上述完成状态指代码实现与列明的验证，不能解读为三个操作系统均已验收。

## 4. 后续实施顺序

### 第三步：跨平台口令保护（已完成）

`MyDbContext` 的口令转换器已支持注入保护器：Avalonia 使用本地 AES-GCM，原 WPF 默认仍使用 DPAPI。存储层已验收，第四步的密钥库页面已接入。实施与备份规则见[口令保护说明](docs/secret-protection.md)。

- [x] 定义可注入的数据保护接口，保留原 WPF DPAPI 兼容行为。
- [x] 实施带 `pt-aesgcm:v1:` 版本的 AES-256-GCM，明确本地密钥文件的生成、备份和恢复方式。
- [x] 每次用密码学随机源生成新 nonce；篡改、错误/缺失密钥、未知格式明确失败，不退回明文。
- [x] 密钥放入 AppDataPaths，Unix 创建权限 `0600`，Windows 创建时使用当前用户独立 ACL；记录恢复时的权限规则。
- [x] EF 模型缓存按保护器实例隔离，服务容器内使用单例保护器。

验收通过：保护往返、重启恢复、篡改/错误或缺失密钥失败、原始数据库列为密文、目录隔离、并发初始化、成套备份恢复及 Windows DPAPI 兼容。nonce 使用随机方案，唯一性是概率保障；详见保护说明。Unix/macOS 实机验证仍属第七步。

### 第四步：持久化密钥库与 GPG 页面联动（已完成）

依赖第三步。

- [x] 新 KeyLibrary 使用独立作用域、串行写入、验证输入；修复原服务每次重新创建 ID 生成器的问题。
- [x] DataGrid 列表、指纹/有效期详情、名称和说明编辑、删除确认、公私钥导入导出；重复私钥不覆盖，公钥可升级为公私钥。
- [x] 生成后自动入库，重启重新加载；RSA 2048/3072/4096、有效天数可设。
- [x] 加密/解密可选库中密钥，保留手工文件输入；公钥记录不出现在解密列表。
- [x] 列表与详情不读取私钥和口令，按需使用，默认不在界面或日志展示。

验收通过：生成→保存→服务容器重建→选用密钥加解密；导入、编辑、删除、取消与导出保护有集成测试。编辑范围为名称和说明，现有密钥的口令及过期签名修改尚未迁移。

### 第五步：旧 Windows 数据迁移（已完成）

已交付已知四类数据的显式迁移，不等同于复制任意完整数据库。详见[迁移说明](docs/data-migration.md)，未映射表与实际文件不在包内。

- [x] 显式迁移页与预览/确认流程，旧库只读，目标重复项跳过不覆盖。
- [x] 在原 Windows 用户下严格解开 DPAPI 并验证私钥口令，通过加密包传输，在目标重新加密。
- [x] v1 包格式、至少 12 字符迁移口令、PBKDF2-SHA256 + AES-GCM；四类记录统一事务、失败回滚、重复指纹/同类 ID 跳过。
- [x] 完整文件头识别明文库/SQLCipher；严格区分旧明文、DPAPI 和新版 AES 格式，不调用原就地迁移助手。

已验证当前 Windows 账户下旧库主文件哈希不变、四类数据数量与密钥指纹一致、损坏 DPAPI 拒绝、错误迁移口令/篡改拒绝、预览后包变化拒绝、重复导入、事务回滚和激活保留。跨平台迁移包实现不依赖 DPAPI，Mac/Linux 实机导入仍待第七步。

### 第六步：剩余工具功能（进行中；下一项：音视频解密预览）

- [x] 新共享扫描服务与 Avalonia 页：多目录、扩展名、流式 SHA-256/MD5、大小预筛、进度、取消、结果及全路径移除确认；新增记录与只读历史。
- [x] 明确平台策略：Windows 原生回收站或显式永久删除；非 Windows 禁用尚未接入的回收站，仅提供明确确认的永久删除，不做失败自动降级。每组保留副本，失败行保留。
- [x] 文件夹 ZIP + GPG 整体归档加解密：结构/空目录、阶段进度与结果、新旧互读、拒绝覆盖、取消及私有暂存清理；ZIP/ZIP64 目录预检、路径和大小限制。
- [x] 基础解密预览：文本、PNG/JPEG、DOCX 正文；私有暂存、完整性及大小限制，正常取消/清理，关闭或切页释放显示内容。
- [x] XLSX 工作表内容预览：切换工作表、单元格地址与原始值、公式缓存标识；不重算公式或访问外部资源，限制 XML/条目/内容大小。
- [x] PDF 静态分页预览：PDFium 原生渲染、前后翻页、50%–200% 缩放、尺寸/页数限制、取消后丢弃结果及关闭释放。
- [ ] 继续预览：音视频等原有类型；崩溃/强制退出遗留清理。详见[预览说明](docs/decryption-preview.md)。
- [ ] 按原版实际入口盘点文件管理、黑名单、Excel、签名/验签及密钥有效期操作，确定哪些必须迁移、哪些暂缓。
- [ ] 新界面按功能拆分状态与服务，逐步消除对 WPF API、外部 `gpg.exe` 路径等假设的依赖。

验收：每项以原版实际行为建立可复现用例，功能和数据完整性通过后标记完成。

重复扫描子项的实现/测试范围见[扫描说明](docs/duplicate-scanning.md)。本次 Windows 账户无符号链接创建权限，链接用例跳过；Windows 原生回收站和 Mac/Linux 文件分支仍列入第七步，不因上述勾选视为平台实测完成。尚未迁移原版全盘枚举、系统类型预设与其他剩余工具。

文件夹子项见[归档说明](docs/folder-crypto.md)：整个目录打包后加密，不是逐文件批次；单项失败拒绝整次归档，不支持覆盖/合并或跳过失败项。只保留文件字节、相对结构与空目录，不承诺源 ACL、执行位和时间等完整元数据。正常取消会清理临时内容，崩溃遗留自动清理尚未实现。

### 第七步：平台实测与发布交付

- [ ] Windows：真实窗口、首次/再次启动、退出及重复启动。
- [ ] macOS ARM64：文件选择器、字体、SQLCipher/Skia 原生库、路径权限、单实例锁及 GPG 往返。
- [ ] Linux：明确发行版、架构和最低版本，再验证系统依赖、文件选择器、权限、锁和 GPG。
- [ ] 制作 Mac `.app`；确定分发方式后完成签名、公证与安装体验。
- [ ] 处理 AVLN3001 警告及其他本次迁移引入的问题，复查旧版兼容性。

不再沿用原计划“10–15 天”的固定估算。后续按上述里程碑及验收结果推进；密钥恢复、剩余功能范围和目标 Linux 版本明确后再估算工期。

## 5. 数据与兼容性约束

- Avalonia 数据根目录：系统 `LocalApplicationData/PracticalTools/Avalonia`，实际值由 `AppDataPaths` 计算，窗口内可查看。
- 数据库为 `sqlite-PracticalTools.db`，保护密钥为 `secret-protection.key`，日志在 `logs/`，实例锁为 `app.lock`；不依赖发布目录，替换发布文件不会清除数据。数据库和保护密钥必须配套备份。
- 新数据库独立于原 WPF 安装目录，首次启动需要原激活码；迁移包也不导入旧激活记录。
- 激活和密钥库均已接入数据库，生成密钥自动持久化；也可手工导出公私钥作为额外备份。
- 当前 Core 含平台专用兼容路径，不代表所有公开方法已跨平台。旧库导出需原 Windows 账户，其他平台使用加密迁移包导入，目标使用自己的保护密钥。
- 保留 WPF 入口及其依赖，达到功能对等和平台验收后再决定是否移除。

## 6. 验证基线与操作命令

最近一次验证（2026-10-30，第六步 PDF 分页预览完成时）：

| 检查 | 结果 | 边界 |
|---|---|---|
| Core 测试 | 107 通过 / 1 跳过，共 108 | 链接用例因 Windows 账户缺少权限跳过 |
| Avalonia 无界面窗口测试 | 23/23 通过 | 不替代真实平台交互测试和原生回收站 |
| WPF Release 构建 | 通过，116 条警告 | 未在本次执行完整旧版功能测试 |
| Avalonia Mac ARM64 自包含发布 | 通过 | 未在 Mac 实机启动验收 |
| Avalonia Windows x64 自包含发布 | 通过 | 可在原账户运行并创建旧库迁移包；原生界面流程待实机验收 |
| Avalonia 动态资源加载警告 | 三处 AVLN3001 | DI 构造的窗口加载有测试覆盖，警告仍待处理 |

运行与检查：

```powershell
dotnet run --project PracticalTools.Avalonia/PracticalTools.Avalonia.csproj
dotnet test PracticalTools.Core.Tests/PracticalTools.Core.Tests.csproj -c Release
dotnet test PracticalTools.Avalonia.Tests/PracticalTools.Avalonia.Tests.csproj -c Release
dotnet build PracticalTools/PracticalTools.csproj -c Release
dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -o ./publish-mac-arm64
dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r win-x64 --self-contained true -o ./publish-win-x64
```

在 Mac 上保留整个发布目录，并运行：

```sh
chmod +x ./publish-mac-arm64/PracticalTools.Avalonia
./publish-mac-arm64/PracticalTools.Avalonia
```

`PracticalTools/PracticalTools.csproj` 仍是 WPF 项目，不能用它发布 macOS。当前发布物是可执行文件及依赖目录，尚非 `.app`。

## 7. 文档维护规则

每完成一个里程碑，同步：

1. 本计划的状态、待办和下一步依赖。
2. `docs/avalonia-migration.md` 的实施记录、验证命令与结果、未验证的平台和遗留问题。
3. README 中对用户可见的能力、启动命令、数据路径和限制。

计划中仅已实现且有相应验证证据的内容打勾。设计候选与未来方案保留为待办，历史记录明确其所属阶段，避免多个“当前状态”互相冲突。
