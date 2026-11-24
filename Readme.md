# 1. 启动

```shell

  # 直接运行（开发模式）
  dotnet run --project PracticalTools/PracticalTools.csproj

  # 或者先构建再运行
  dotnet build && dotnet run --project PracticalTools/PracticalTools.csproj
  
  # 只build windows版
  dotnet build .\PracticalTools.Avalonia -r win-x64

  # 发布 Release 版本（自包含单文件，无需安装 .NET）
  dotnet publish PracticalTools/PracticalTools.csproj -c Release -o ./publish

# 发布 Avalonia 迁移预览版（Apple Silicon）
dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -o ./publish-mac-arm64

# 一次性发布3个平台的版本, 在主文件夹里执行脚本
 .\scripts\publish-release.ps1

```


