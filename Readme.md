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

# 发布 苹果版本
 dotnet publish PracticalTools/PracticalTools.csproj -c Release -r osx-arm64 --self-contained true -o
  ./publish-mac-arm64

```

