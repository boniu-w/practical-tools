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
- `PracticalTools.Avalonia`：Avalonia 11.3 迁移预览，支持密钥库、文件加解密、RSA 密钥生成及导入导出。
- `PracticalTools.Core.Tests`：可跨平台运行的 GPG 往返与文件保护测试。
- `PracticalTools.Avalonia.Tests`：激活窗口与主窗口的无界面自动化测试。

```powershell
dotnet run --project PracticalTools.Avalonia/PracticalTools.Avalonia.csproj
dotnet test PracticalTools.Core.Tests/PracticalTools.Core.Tests.csproj -c Release
dotnet test PracticalTools.Avalonia.Tests/PracticalTools.Avalonia.Tests.csproj -c Release
```

生成页支持 RSA 2048/3072/4096 位与有效天数（0 为永久），生成成功后自动保存到密钥库，重启后可继续使用。密钥库可导入一个 ASCII armored 公钥或私钥环、查看指纹与有效期、编辑显示名称和说明、分别导出公私钥，并在确认后删除。导入私钥须提供正确口令，会自动提取对应公钥；重复指纹拒绝覆盖，但已有公钥可升级为公私钥记录。

加解密页可选择库中密钥，也可继续手工导入/粘贴；解密下拉列表仅显示含私钥的记录。库中私钥与口令按需读取，不填入界面。新生成或导入后可切换页签或点击“刷新密钥”。输出文件须使用新文件名；操作失败会清理临时输出。

已迁移启动流程：首次运行输入原版激活码，之后从 SQLCipher 加密数据库读取激活记录；同一用户数据目录只允许运行一个 Avalonia 实例。数据库和日志位于系统当前用户的 LocalApplicationData 下的 `PracticalTools/Avalonia`，激活窗口和首页均显示实际路径。删除或更换发布目录不会清除激活记录。

新版本使用独立数据库，不自动复制或覆盖原 WPF 安装目录中的旧库。旧版 DPAPI 密文只能在原 Windows 用户环境中恢复；目前继续通过 ASCII 密钥文件导入，不直接复用旧库。激活规则沿用原版，首次启动需重新输入激活码。

密钥库的口令存储使用 AES-256-GCM，保护密钥位于用户数据目录的 `secret-protection.key`，原 WPF 继续使用 DPAPI。请退出应用后成套备份数据库和密钥文件；缺失保护密钥时无法恢复已有口令。格式、权限及恢复规则见[口令保护说明](docs/secret-protection.md)。

尚未迁移：旧 Windows 数据库导入、重复文件扫描、文件夹加解密、解密预览。密钥库暂只编辑名称和说明，尚无私钥口令修改或已有密钥有效期修改功能。预览版不是完整正式版。

macOS 发布目录目前为命令行可执行文件及运行依赖，尚未制作 `.app`、签名或公证。在 Mac 上可执行：

```sh
chmod +x ./publish-mac-arm64/PracticalTools.Avalonia
./publish-mac-arm64/PracticalTools.Avalonia
```

macOS/Linux 的实际界面和系统文件选择器仍需在对应系统验收。后续迁移状态见 [迁移记录](docs/avalonia-migration.md)。

