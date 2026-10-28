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

目前采用两个桌面入口共用核心的渐进迁移方式：

- `PracticalTools`：原 WPF 完整版，仍仅支持 Windows。
- `PracticalTools.Core`：`net8.0` 共享 GPG 核心，保留原命名空间。
- `PracticalTools.Avalonia`：Avalonia 11.3 迁移预览，支持文件加密、解密和 RSA 密钥对生成、导出。
- `PracticalTools.Core.Tests`：可跨平台运行的 GPG 往返与文件保护测试。

```powershell
dotnet run --project PracticalTools.Avalonia/PracticalTools.Avalonia.csproj
dotnet test PracticalTools.Core.Tests/PracticalTools.Core.Tests.csproj -c Release
```

预览版从 ASCII armored 密钥文件导入或粘贴密钥，不读取原 Windows 数据库。生成的密钥只在当前会话内存中保留，关闭前需要分别导出公钥、私钥。输出文件须使用新文件名，避免误覆盖；操作失败时会清理临时输出。

尚未迁移：密钥库 CRUD、数据库与 DPAPI 数据保护、激活检查、单实例管理、重复文件扫描、文件夹加解密、解密预览。预览版不是完整正式版。

macOS 发布目录目前为命令行可执行文件及运行依赖，尚未制作 `.app`、签名或公证。在 Mac 上可执行：

```sh
chmod +x ./publish-mac-arm64/PracticalTools.Avalonia
./publish-mac-arm64/PracticalTools.Avalonia
```

macOS/Linux 的实际界面和系统文件选择器仍需在对应系统验收。后续迁移状态见 [迁移记录](docs/avalonia-migration.md)。

