# 实用工具：WPF → Avalonia 跨平台迁移计划

更新日期：2026-10-28 · 工作分支：`ava` · 当前进度：第一、二步已实现，第三步待开始。

本文件维护当前架构、待办顺序与验收条件；[迁移记录](docs/avalonia-migration.md)保存各次实施与验证历史。两者出现差异时先核对代码与最近验证结果，再同步文档。历史测试通过不等于所有平台实机通过。

## 1. 目标与迁移方式

目标平台为 Windows、macOS（优先 Apple Silicon / `osx-arm64`）和 Linux。Linux 发行版与最低版本尚未确定；如需 CentOS，必须明确具体版本并验证 .NET、Avalonia 和原生依赖兼容性。

采用两个桌面入口共用 Core 的渐进迁移方式，当前没有将原 WPF 项目直接转换为 Avalonia：

| 项目 | 当前职责 | 状态 |
|---|---|---|
| `PracticalTools` | 原 WPF 完整版，`net8.0-windows` | 保留，仅 Windows |
| `PracticalTools.Core` | `net8.0`：GPG 核心、数据模型、EF 上下文、激活服务、存储基础设施 | 已抽取一部分，仍保留 Windows DPAPI 兼容路径 |
| `PracticalTools.Avalonia` | `net8.0`：新桌面入口、首页、GPG 操作页、激活窗口 | 迁移预览版 |
| `PracticalTools.Core.Tests` | GPG 与存储集成测试 | 最近验证 7/7 通过 |
| `PracticalTools.Avalonia.Tests` | 无界面窗口测试 | 最近验证 2/2 通过 |
| `TestProject` | 原 Windows 测试项目 | 保留，尚未整体跨平台迁移 |

当前实际选用 Avalonia 11.3.21、Fluent 主题、Inter 字体、异步 StorageProvider 文件选择器。没有安装 Semi.Avalonia 或 Avalonia DataGrid；DataGrid 待密钥库阶段引入。WPF 原有 MaterialDesign 包继续留在原项目中。

## 2. 当前功能状态

| 功能 | Avalonia 现状 | 还缺什么 |
|---|---|---|
| 应用入口、首页、导航 | 已实现 | 后续功能按需接入 |
| 文件加密、解密 | 已调用共享 GPG 核心 | 与持久化密钥库联动 |
| 密钥输入 | 可粘贴或读取 ASCII armored 密钥文件 | 尚非数据库导入 |
| 密钥生成 | RSA 3072、口令确认、公私钥分别导出 | 有效期等原版选项、保存至密钥库 |
| 生成密钥保存 | 当前会话内存，关闭前手动导出 | 持久化、未保存关闭提示 |
| 输出文件保护 | 拒绝覆盖和同路径，成功后才提交临时输出 | 持续覆盖异常与平台差异 |
| 数据库初始化 | 用户目录中的 SQLCipher 数据库 | 完整版本化升级、旧库导入 |
| 激活 | 原规则、新库记录、激活窗口 | 原平台实机启动验收 |
| DI、日志、单实例 | 已接入启动与退出流程 | 跨进程及各平台验收 |
| 密钥库列表、编辑、详情、删除 | 服务与模型已移入 Core，UI 未迁移 | 口令保护、UI、与操作页联动 |
| 重复文件扫描、文件管理、黑名单 | 原 WPF 入口保留 | 服务梳理与 Avalonia UI |
| 文件夹加解密、解密预览 | 原 WPF 入口保留 | 批量流程、预览容器、临时数据生命周期 |
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

### 第三步：跨平台口令保护（下一次改造起点）

必须先完成此步，再让 Avalonia 密钥库保存口令。当前 `MyDbContext` 的 `GpgFile.Passphrase` 转换器仍调用 DPAPI；在非 Windows 上不能直接保存或读取非空口令。

- [ ] 定义可注入的数据保护接口，区分原 WPF 兼容行为与 Avalonia 新写入格式。
- [ ] 实施带格式版本的认证加密方案（候选为 AES-256-GCM），明确密钥生成、存储、备份与恢复方式；原计划中的本地密钥文件仅是候选，尚未实现。
- [ ] 保证 nonce 唯一、密文篡改/错误密钥/密钥丢失时明确失败，不把解密失败的密文当明文使用。
- [ ] 若采用本地密钥文件，放入统一 AppDataPaths 数据目录，Unix 权限限制为当前用户读写，明确 Windows 权限与密钥随备份恢复规则。
- [ ] 处理 EF 模型缓存与保护服务作用域，防止不同数据目录复用错误密钥。

验收：保护往返、重启后恢复、篡改/错误密钥/缺失密钥失败、不泄漏明文、原 Windows DPAPI 路径兼容测试通过。

### 第四步：持久化密钥库与 GPG 页面联动

依赖第三步。

- [ ] 核对 `GpgFileService` 的作用域、ID 生成和输入验证，避免将“已移入 Core”视为“已适合全部平台”。
- [ ] 实现密钥列表、详情、编辑、删除确认、公私钥导入导出；需要表格时引入匹配版本 DataGrid 及主题。
- [ ] 保存生成的密钥，重启后重新加载；提供生成参数与有效期设置。
- [ ] 加密/解密页可选择库中密钥，保留文件导入路径。
- [ ] 保证界面默认隐藏口令与私钥，不在日志中写入敏感内容。

验收：生成→保存→重启→选用密钥完成加解密；导入、编辑、删除和取消操作均有集成测试。

### 第五步：旧 Windows 数据迁移

依赖第三步；与第四步接口协同设计，完整库导入在密钥库验收后交付。

- [ ] 提供显式导入入口，不自动覆盖当前库或改写旧库。
- [ ] 在原 Windows 用户环境中解开旧 DPAPI 口令，再写入新保护格式；复制数据库到 Mac 不代表口令已迁移。
- [ ] 设计跨机器迁移包的加密、口令和元数据版本；失败回滚、重复导入与冲突策略必须明确。
- [ ] 区分未加密 SQLite、SQLCipher 和不同口令格式，不盲目复用原就地迁移代码。

验收：旧库保留、数据数量与密钥指纹一致、失败不产生半成品、原 Windows 用户解密后可在新平台正常使用。

### 第六步：剩余工具功能

- [ ] 重复文件扫描：梳理服务，迁移目录选择、进度、取消、结果及删除确认。
- [ ] 明确各平台回收站策略；永久删除与移入回收站要在界面上准确区分，不能默认静默降级。
- [ ] 文件夹加解密：批量结果、冲突处理、取消及清理。
- [ ] 解密预览：跨平台预览容器，安全临时文件与退出清理。
- [ ] 按原版实际入口盘点文件管理、黑名单、Excel、签名/验签及密钥有效期操作，确定哪些必须迁移、哪些暂缓。
- [ ] 新界面按功能拆分状态与服务，逐步消除对 WPF API、外部 `gpg.exe` 路径等假设的依赖。

验收：每项以原版实际行为建立可复现用例，功能和数据完整性通过后标记完成。

### 第七步：平台实测与发布交付

- [ ] Windows：真实窗口、首次/再次启动、退出及重复启动。
- [ ] macOS ARM64：文件选择器、字体、SQLCipher/Skia 原生库、路径权限、单实例锁及 GPG 往返。
- [ ] Linux：明确发行版、架构和最低版本，再验证系统依赖、文件选择器、权限、锁和 GPG。
- [ ] 制作 Mac `.app`；确定分发方式后完成签名、公证与安装体验。
- [ ] 处理 AVLN3001 警告及其他本次迁移引入的问题，复查旧版兼容性。

不再沿用原计划“10–15 天”的固定估算。后续按上述里程碑及验收结果推进；密钥恢复、剩余功能范围和目标 Linux 版本明确后再估算工期。

## 5. 数据与兼容性约束

- Avalonia 数据根目录：系统 `LocalApplicationData/PracticalTools/Avalonia`，实际值由 `AppDataPaths` 计算，窗口内可查看。
- 数据库为 `sqlite-PracticalTools.db`，日志在 `logs/`，实例锁为 `app.lock`；不依赖发布目录，替换发布文件不会清除数据。
- 新数据库独立于原 WPF 安装目录，当前首次启动需要原激活码；不自动迁移旧激活记录。
- 当前只有激活流程接入数据库。生成密钥仍在会话内存中，关闭前需分别导出公钥和私钥。
- 当前 Core 含平台专用兼容路径，不代表所有公开方法已跨平台。完整密钥库跨平台读写仍依赖第三步。
- 保留 WPF 入口及其依赖，达到功能对等和平台验收后再决定是否移除。

## 6. 验证基线与操作命令

最近一次验证（2026-10-28，第二步完成时）：

| 检查 | 结果 | 边界 |
|---|---|---|
| Core 测试 | 7/7 通过 | 当前 Windows 环境执行 |
| Avalonia 无界面窗口测试 | 2/2 通过 | 不替代真实平台交互测试 |
| WPF Release 构建 | 通过，116 条警告 | 未在本次执行完整旧版功能测试 |
| Avalonia Mac ARM64 自包含发布 | 通过 | 未在 Mac 实机启动验收 |
| Avalonia 动态资源加载警告 | 两处 AVLN3001 | DI 构造的窗口加载有测试覆盖，警告仍待处理 |

运行与检查：

```powershell
dotnet run --project PracticalTools.Avalonia/PracticalTools.Avalonia.csproj
dotnet test PracticalTools.Core.Tests/PracticalTools.Core.Tests.csproj -c Release
dotnet test PracticalTools.Avalonia.Tests/PracticalTools.Avalonia.Tests.csproj -c Release
dotnet build PracticalTools/PracticalTools.csproj -c Release
dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -o ./publish-mac-arm64
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
