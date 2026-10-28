# 1. 主体功能

## 1. 功能1: 扫描重复文件

![屏幕截图 2026-02-26 105053](.\img\屏幕截图 2026-02-26 105053.png)



## 2. 功能二: 加密, 解密文件

![屏幕截图 2026-02-26 105053](.\img\屏幕截图 2026-02-26 105209.png)





# 2. 启动

```shell

  # 直接运行（开发模式）
  dotnet run --project PracticalTools/PracticalTools.csproj

  # 或者先构建再运行
  dotnet build && dotnet run --project PracticalTools/PracticalTools.csproj

  # 发布 Release 版本（自包含单文件，无需安装 .NET）
  dotnet publish PracticalTools/PracticalTools.csproj -c Release -o ./publish

# 发布 Avalonia 迁移预览版（Apple Silicon）
dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -o ./publish-mac-arm64

```

## Avalonia 跨平台迁移（ava 分支）

当前路线与待办见[迁移计划](迁移到Avalonia跨平台改造计划书.md)，各次验证结果见[迁移记录](docs/avalonia-migration.md)。目前采用两个桌面入口共用核心的渐进迁移方式：

- `PracticalTools`：原 WPF 完整版，仍仅支持 Windows。
- `PracticalTools.Core`：`net8.0` 共享 GPG 核心、数据模型、数据库上下文与激活服务，保留原命名空间。
- `PracticalTools.Avalonia`：Avalonia 11.3 迁移预览，支持文件加密、解密和 RSA 密钥对生成、导出。
- `PracticalTools.Core.Tests`：可跨平台运行的 GPG 往返与文件保护测试。
- `PracticalTools.Avalonia.Tests`：激活窗口与主窗口的无界面自动化测试。

```powershell
dotnet run --project PracticalTools.Avalonia/PracticalTools.Avalonia.csproj
dotnet test PracticalTools.Core.Tests/PracticalTools.Core.Tests.csproj -c Release
dotnet test PracticalTools.Avalonia.Tests/PracticalTools.Avalonia.Tests.csproj -c Release
```

预览版从 ASCII armored 密钥文件导入或粘贴密钥，不读取原 Windows 数据库。生成的密钥只在当前会话内存中保留，关闭前需要分别导出公钥、私钥。输出文件须使用新文件名，避免误覆盖；操作失败时会清理临时输出。

已迁移启动流程：首次运行输入原版激活码，之后从 SQLCipher 加密数据库读取激活记录；同一用户数据目录只允许运行一个 Avalonia 实例。数据库和日志位于系统当前用户的 LocalApplicationData 下的 `PracticalTools/Avalonia`，激活窗口和首页均显示实际路径。删除或更换发布目录不会清除激活记录。

新版本使用独立数据库，不自动复制或覆盖原 WPF 安装目录中的旧库。旧版 DPAPI 密文只能在原 Windows 用户环境中恢复；目前继续通过 ASCII 密钥文件导入，不直接复用旧库。激活规则沿用原版，首次启动需重新输入激活码。

跨平台口令存储基础已接入数据库：Avalonia 使用 AES-256-GCM，保护密钥位于用户数据目录的 `secret-protection.key`，原 WPF 继续使用 DPAPI。请退出应用后成套备份数据库和密钥文件；缺失保护密钥时无法恢复已有口令。格式、权限及恢复规则见[口令保护说明](docs/secret-protection.md)。密钥库页面尚未接入，此项不会改变当前生成页面的手动导出流程。

尚未迁移：密钥库 CRUD、旧库导入、重复文件扫描、文件夹加解密、解密预览。预览版不是完整正式版。

macOS 发布目录目前为命令行可执行文件及运行依赖，尚未制作 `.app`、签名或公证。在 Mac 上可执行：

```sh
chmod +x ./publish-mac-arm64/PracticalTools.Avalonia
./publish-mac-arm64/PracticalTools.Avalonia
```

macOS/Linux 的实际界面和系统文件选择器仍需在对应系统验收。后续迁移状态见 [迁移记录](docs/avalonia-migration.md)。

