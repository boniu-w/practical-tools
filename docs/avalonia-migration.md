# Avalonia 迁移记录

## 2026-10-28：第一步

工作分支：`ava`。采用并行入口逐步迁移，以免尚未迁移的页面阻塞跨平台构建。

已完成：

- 将 `GpgHelper` 移至 `PracticalTools.Core/GPG`，保持命名空间和公开 API，原 WPF 项目通过项目引用继续使用。
- 新建 `PracticalTools.Avalonia`，使用 `net8.0`、Avalonia 11.3.21、Fluent 主题和异步 StorageProvider 文件选择器。
- 首页、单文件加密、单文件解密、RSA 3072 密钥生成及分别导出公私钥。
- 后台执行 GPG 运算，界面显示处理中状态和错误信息。
- 新增 `GpgFileOperations`：拒绝覆盖和同路径输出，使用目标目录临时文件，成功后才发布输出；Unix 临时文件和导出密钥权限为当前用户读写。
- 新增独立跨平台核心测试，覆盖二进制往返、同路径/覆盖保护、错误口令后的清理。

当前预览版没有接入数据库与激活服务，不作为完整正式版。原 WPF 的数据库、激活逻辑和功能保持原入口使用。

验证结果：

- Avalonia Release 编译通过；macOS ARM64 自包含发布通过，输出至 `publish-mac-arm64`。
- 原 WPF Release 编译通过（保留原有 118 条警告）。
- 核心测试 3/3 通过。受限环境中的测试宿主未返回，改为沙箱外运行后完成；NuGet 漏洞信息获取出现 NU1900 警告。
- 尚未在 macOS/Linux 启动应用或验收界面；发布成功仅验证编译和运行依赖还原，不代表系统实测通过。

下一步建议按依赖顺序推进：

1. 抽取不依赖 WPF 的模型和数据库服务，确定用户数据目录以及旧 Windows 数据迁移策略。
2. 迁移激活窗口、DI、日志与单实例生命周期。
3. 迁移密钥库与 DataGrid，明确 DPAPI 旧数据可恢复性后实现跨平台敏感数据保护。
4. 迁移扫描、文件夹操作和解密预览，逐步拆分原 MainViewModel。
5. 在 macOS 和 Linux 实测，制作 macOS `.app` 并处理签名、公证。

Avalonia 生命周期与文件选择器实现参考：[生命周期](https://docs.avaloniaui.net/docs/fundamentals/application-lifetimes)、[StorageProvider](https://docs.avaloniaui.net/docs/services/storage/storage-provider)。
