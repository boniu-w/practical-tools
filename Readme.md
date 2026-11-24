# 1. 启动

```shell
# 旧版
  # 直接运行（开发模式）
  dotnet run --project PracticalTools/PracticalTools.csproj

  # 或者先构建再运行
  dotnet build && dotnet run --project PracticalTools/PracticalTools.csproj
  
  # 只build windows版
  dotnet build .\PracticalTools.Avalonia -r win-x64

  # 发布 Release 版本（自包含单文件，无需安装 .NET）
  dotnet publish PracticalTools/PracticalTools.csproj -c Release -o ./publish

```



Avalonia 版

```bash
  # Windows（x64）
  dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r win-x64 --self-contained true -o ./publish-win-x64 -p:Version=0.7.0
     
  # macOS（Apple Silicon）
  dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -o ./publish-mac-arm64 -p:Version=0.7.0
     
  # Linux（x64）
  dotnet publish PracticalTools.Avalonia/PracticalTools.Avalonia.csproj -c Release -r linux-x64 --self-contained true -o ./publish-linux-x64 -p:Version=0.7.0
  
  # 执行这个脚本, 可以一次性发布三个版本的, 右键 使用powshell运行, 前提要安装 python 3.11 以上
  scripts/publish-release.ps1

```



mac版说明

```
  PracticalTools-0.7.0-osx-arm64-unsigned.zip
  - osx-arm64 表示只适用于 Apple Silicon（M1/M2/M3/M4 等），Intel Mac 无法运行。我确认了包里的主程序是 Mach-O 64-bit arm64 executable。
  - 解压后得到的是 PracticalTools.app 应用包，不是安装程序；把它拖进「应用程序」目录即可。
  - unsigned 表示未做 Apple 代码签名、也未公证（release.json 里 macos_signed 和 macos_notarized 都是 false）。首次打开会被 Gatekeeper 拦截，提示"无法验证开发者"或"应用已损坏"。
     
  首次运行前可在终端执行：
     
  xattr -dr com.apple.quarantine /Applications/PracticalTools.app

  然后正常双击打开；或者到「系统设置 → 隐私与安全性」里选择「仍要打开」。不要用 sudo spctl --master-disable 全局关闭 Gatekeeper，风险较大。

```



linux版说明

```
  PracticalTools-0.7.0-linux-x64.tar.gz
  Linux 包是自包含的（linux-x64，x86_64），不需要安装 .NET。整个目录包含原生库，请不要只拷贝可执行文件。
     
  tar -xzf PracticalTools-0.7.0-linux-x64.tar.gz
  cd PracticalTools-linux-x64
  ./PracticalTools.Avalonia
     
  tar 包里的可执行位已经是 755；如果拷贝后权限丢失：

  chmod +x PracticalTools.Avalonia

  依赖

  桌面版 Ubuntu 24.04 通常已自带 X11/OpenGL 等基础库。建议补装：
  
  # 中文字体（否则界面中文可能显示为方块）
  sudo apt install fonts-noto-cjk

  # 音视频预览需要系统 LibVLC 3（Linux 发布包不包含）
  sudo apt install vlc

  # 如果文件选择对话框打不开
  sudo apt install xdg-desktop-portal xdg-desktop-portal-gtk

  不装 VLC 也可以正常使用加解密、密钥库、重复扫描、PDF/图片/文本预览，只有音视频预览会报错。PDFium、Skia、Magick、SQLCipher 等都已在发布包内。

  数据目录与激活

  - 默认数据目录：~/.local/share/PracticalTools/Avalonia/
    - sqlite-PracticalTools.db
    - secret-protection.key
    - logs/、app.lock
  - 自定义数据目录（必须是绝对路径）：

  ./PracticalTools.Avalonia --data-dir /home/你的用户名/PracticalToolsData

  - 首次启动需要用原版激活码激活。
  - 备份时必须把数据库和 secret-protection.key 成套保存，否则已保存的私钥口令无法解密。
  - 同一数据目录只允许运行一个实例；提示无法取得运行锁时，先关闭已运行的实例。

  已知限制

  - 目标平台标记为 Ubuntu 24.04 x64，release.json 中也注明尚未做运行时实机验证。
  - 仅支持 x86_64 Linux，ARM Linux（如树莓派）不能运行。
  - 需要图形桌面会话，纯 SSH/无 DISPLAY 的环境无法启动。
  - Wayland 下通过 XWayland 运行 X11 应用；视频预览使用 X11 原生窗口接口，若显示异常可改用 Xorg 会话登录。
  - 出现问题时先查看 ~/.local/share/PracticalTools/Avalonia/logs/ 下的日志。

```





# 2. PracticalTools.Avalonia 主要功能

本地优先的文件与密钥工具箱。加密、解密、预览、扫描均在本机完成，数据不会上传网络。

### 文件工具

- **文件加密 / 文件解密**：使用 GPG 公钥加密单个文件、使用私钥解密；可选用密钥库中的密钥，或粘贴/导入 ASCII 密钥。输出必须选择新文件名，不覆盖已有文件。
- **文件夹加密 / 文件夹解密**：把整个文件夹打包为 ZIP 后再用 GPG 加密（`.zip.gpg`），保留相对目录结构和空目录；拒绝覆盖、合并，也拒绝把输出写进源目录内部。
- **解密预览**：在私有临时工作区解密并校验完整性，展示前删除磁盘明文；内置查看器，不启动浏览器或外部播放器。支持：
  - 文本：TXT、MD、CSV、JSON、XML、HTML、LOG、YAML、INI、CS、SQL
  - 图片：PNG、JPEG、BMP、GIF（动画）、ICO、WebP、AVIF（首帧）
  - Office：DOCX 正文；XLSX 按工作表查看单元格内容
  - PDF：分页浏览，50%–200% 缩放
  - 音视频：MP4/M4V/MOV/M4A、MKV/WebM、AVI/WMV、WAV/MP3/FLAC/OGG/OGV/AAC，支持播放、暂停、停止、跳转和音量
- **文件整理**：按后缀把当前目录的直接子文件移入同名子目录。先预览完整源/目标路径再确认；不递归、不覆盖已有文件，失败项保留在原处。

### 密钥与安全

- **生成密钥**：在本机生成 RSA GPG 密钥对，可设置密钥位数和有效天数（0 为永久），生成后自动保存到密钥库。
- **密钥库**：统一管理本机 GPG 密钥，支持导入密钥、导出公钥/私钥、修改名称与说明、删除记录；列表和详情不显示私钥与口令，私钥口令使用本地 AES-256-GCM 加密保存。
- **数据迁移**：支持从旧版 WPF 数据库只读导出，或导出当前新版数据，生成口令加密的 `.ptmigration` 迁移包；导入前可验证和预览，重复数据跳过且不覆盖目标。

### 扫描与记录

- **重复扫描**：先按文件大小筛选，再用 SHA-256 流式比较内容；支持多个目录、扩展名过滤、进度显示和取消。结果可多选，按组保留一份后可移入 Windows 回收站或经确认永久删除；扫描结果自动保存最近 500 条。
- **文件记录**：添加本地文件并记录 MD5 与元数据，支持搜索、类别编辑和删除记录；删除只影响数据库，不删除磁盘文件。
- **黑名单**：人工维护平台、作者、作品、日期、原因、类型、备注、信用代码等信息，支持搜索和增删改查；仅作备注，不参与扫描过滤或自动删除。

### 数据与平台

- 数据保存在本机 SQLite 数据库；私钥口令依赖同目录的 `secret-protection.key`，备份或恢复时必须把数据库与该密钥文件成套保存。
- 支持 Windows x64、macOS（Apple Silicon）和 Linux x64 自包含发布，目标机器无需安装 .NET。
- 应用为单实例运行；首次启动需要使用原版激活码激活。
- Linux 上音视频预览需要系统提供 LibVLC 3 及对应解码插件。
- 更多实现细节、限制和平台验收情况见 `docs/` 目录。



