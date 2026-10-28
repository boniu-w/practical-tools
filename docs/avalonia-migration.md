# Avalonia 迁移记录

当前架构、里程碑和后续验收条件统一维护在[迁移计划](../迁移到Avalonia跨平台改造计划书.md)。本文件保留实施历史；后面的“第一步”描述当时状态，不代表当前仍未接入数据库和激活。

## 2026-10-28：第二步（当前状态）

工作分支：`ava`。

已完成：

- 将 EF Core 数据上下文、相关模型、GPG 数据服务和激活服务移入共享 Core，原 WPF 项目继续通过项目引用使用原命名空间。
- Avalonia 数据独立保存在系统 LocalApplicationData 下的 `PracticalTools/Avalonia`，数据库、日志和实例锁不再依赖发布目录。Unix 新建数据目录权限为当前用户读写执行。
- 初始化 SQLCipher 数据库，首次创建表；兼容已有数据库缺少激活表的情况。
- 使用 DI 管理数据库作用域和激活服务，后台初始化和验证激活码，激活成功后进入主窗口，重启后直接读取激活记录。
- 文件句柄形式的单实例锁与应用退出时的 DI、日志和锁资源释放。
- 启动错误或重复实例显示可关闭的提示窗口；初始化失败不会进入主界面。
- 首页和激活窗口显示实际数据目录。

数据兼容策略：

- 不自动复制、修改或覆盖旧 WPF 数据库。新数据库中的激活记录与旧库独立，需要输入原激活码。
- 保留 Windows DPAPI 旧读写行为；非 Windows 调用非空旧口令的读写时抛出明确的不支持异常，不把旧密文当作明文返回。
- 此步仅持久化激活，尚未接入密钥库 UI。GPG 页面仍用 ASCII 密钥文件和会话内存；后续先完成跨平台口令保护与旧库导入，再接入密钥 CRUD。

验证结果：

- 核心测试 7/7 通过：包括 GPG 往返与文件保护、激活持久化、无效激活码、缺表兼容、单实例锁释放重取、特殊字符路径。
- Avalonia 无界面窗口测试 2/2 通过：真实 XAML 资源加载、激活失败和成功事件、注入路径的主窗口加载。
- 原 WPF Release 构建通过，116 条原有代码警告。
- macOS ARM64 自包含发布通过，已更新 `publish-mac-arm64`。Avalonia 的两个 DI 构造窗口仍有 AVLN3001 动态资源加载警告，实际构造加载已由窗口测试覆盖。
- 尚未在 macOS/Linux 实机验收界面、SQLCipher 原生运行库或文件锁行为。无界面测试不代替这些平台验收。

下一步顺序：第三步先完成跨平台口令保护，第四步接入持久化密钥库，第五步交付旧 Windows 数据迁移；后续阶段以迁移计划为准。

无界面测试参考 [Avalonia 官方 XUnit 文档](https://docs.avaloniaui.net/docs/testing/headless-xunit)。以下保留第一步的历史记录。

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
