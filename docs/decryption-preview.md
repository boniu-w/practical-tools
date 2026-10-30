# Avalonia 解密预览（文本、图片、Office 内容、PDF、音视频）

2026-10-30，`ava` 分支。原 WPF 的 WebView2 预览保持原状；新入口使用 Avalonia 文本/图片控件、PDFium 和 LibVLC 视频窗口，不启动浏览器或外部播放器。

## 使用与范围

“解密预览”页选择密钥库中的私钥记录和加密文件，再点击“查看加密文件”。已过期私钥仍可解密旧文件。密钥口令按需读取，不填回界面。

- 文本：txt、md、csv、json、xml、html/htm、log、yaml/yml、ini、cs、sql。支持 BOM、UTF-8 和 GB18030 回退；含二进制控制字符时拒绝。HTML/XML 仅作为文字显示，不运行脚本或加载链接。
- 图片：PNG/JPEG/BMP/GIF/ICO/WebP/AVIF；静态图最多 1600 万源像素、显示解码宽度最多 1600；多帧 GIF 自动循环播放（画布 ≤ 1600 万像素、帧数 ≤ 300、总像素 ≤ 6400 万），AVIF 仅显示首帧；不提供原图编辑。
- DOCX：只抽取正文文字、段落和表格内文字，不保留分页、图片、样式或表格布局。不加载外部资源，不执行宏。禁止 DTD/XML 外部实体，XML 字符上限 400 万，ZIP 中央目录预检最多 10000 条目。
- XLSX：可切换工作表，按“单元格地址 + 原始值”显示内容，支持共享/内联字符串、富文本的文字、布尔值、错误值和稀疏单元格；空表、隐藏表有标注。支持 Transitional/Strict SpreadsheetML。
- PDF：静态页面图像、上一页/下一页、页码及 50%–200% 缩放，不提供文本选择、搜索或表单编辑。
- 音视频：MP4/M4V/MOV/M4A、MKV/WebM、AVI/WMV、WAV/MP3/FLAC/OGG/OGV/AAC（ADTS），具体编码能否播放取决于原生解码器；提供播放、暂停、停止、跳转和音量。
- 动画 WebP/动画 AVIF、旧 DOC/XLS 尚未迁移；AVIF 与动画 GIF 按上述方式预览。预览剩余平台测试继续保留，下一主项为剩余工具入口盘点及文件管理/黑名单。

### 音视频播放与平台依赖

选择加密媒体并查看后，需手动点击“播放 / 继续”；不会自动发声。支持暂停、停止、结束后重播；进度条显示实际播放位置，拖动上方百分比滑块后点击“跳转到所选位置”。媒体完成初始化并可定位时才启用跳转。音量范围 0–100，默认 50。

媒体继续受 64 MiB 明文上限约束，较大视频需通过“文件解密”保存。扩展名必须受支持，文件头需匹配对应容器；例如 MOV 当前接受 ftyp/moov 起始布局，其他布局可能被拒绝，AAC 必须是 ADTS 帧（拒绝 ADIF），WMV 必须是 ASF 容器。匹配文件头不保证整个文件完整，解码失败会显示错误。不会把 M3U/网络地址作为媒体入口，解析器按格式显式指定。

LibVLC 从内存回调读取，磁盘明文在显示播放页前已经删除。播放器持有独立字节副本，直至停止并释放所有输入回调，之后关闭流、清零副本。关闭预览、改输入或离开页面会停止播放，解绑原生视频窗口，释放媒体和引擎；没有后台继续播放。原生 Stop/Dispose 可能耗时，未实现硬超时或独立进程隔离。

使用 LibVLCSharp.Avalonia 3.10.2、VideoLAN.LibVLC.Windows 3.0.24、VideoLAN.LibVLC.Mac 3.1.3.2（包版本）。忽略用户 VLC 配置，禁用 Lua、字幕自动搜索、网络元数据、媒体库及硬件解码。不提供流媒体、字幕加载或外部播放列表入口；这些设置不是操作系统网络/文件访问沙箱，复杂原生媒体解析也没有解码内存硬上限。

Windows x64 与 Mac ARM64 发布目录含完整 `libvlc/<RID>` 原生库和插件层级，不能只复制主 DLL/dylib。Linux 需由选定发行版提供兼容 LibVLC 3、解码与音视频输出插件；视频宿主使用 X11 窗口接口，Wayland 环境需另行确认 XWayland 路径。Mac/Linux 和真实视频窗口/声音设备均未实机验收。Windows 可执行文件通过项目中的 `app.manifest` 声明 supportedOS（Win7–Win11）；Avalonia 的 `NativeControlHost` 依赖该清单创建视频子窗口，缺失时 `VideoView` 挂载会抛出 `Unable to create child window for native control host` 并终止进程，构建/发布时不要移除 `ApplicationManifest` 设置。

图片静态解码使用 SkiaSharp；AVIF 使用 Magick.NET-Q8-AnyCPU 14.17.2（Apache-2.0，随包 ImageMagick/libheif/aom 等，完整声明见 `ThirdParty/Magick.NET`），只读首帧并设置内存/磁盘/尺寸/线程上限；多帧 GIF 使用 Avalonia.Labs.Gif 11.3.1（MIT，见 `ThirdParty/Avalonia.Labs.Gif`），交控件前用 SkiaSharp 校验帧数、画布和总像素。发布目录因此增加约 25–30 MB 原生库（Windows x64 由 225 MB 增至 251 MB，Mac ARM64 由 203 MB 增至 234 MB）。

本次原生冒烟测试在真实 Win32 窗口中验证 AVI/WMV 的 VideoView/D3D11 输出、WAV/AAC 默认音频输出（静音）播放及卸载释放，各两轮；可见窗口模式用间隔截图确认 GIF 红色/蓝色帧交替、视频画面显示和音频预览界面。它们仍不证明真实扬声器音量或所有列明编码已通过。已知原生窗口的裁剪/层叠限制属于平台验收项。技术依据：[LibVLCSharp](https://github.com/videolan/libvlcsharp)、[Avalonia VideoView 说明](https://github.com/videolan/libvlcsharp/blob/3.x/src/LibVLCSharp.Avalonia/README.md)、[Magick.NET](https://github.com/dlemstra/Magick.NET)、[Avalonia.Labs.Gif](https://github.com/AvaloniaUI/Avalonia.Labs)。许可和上游源码信息在随包的 `ThirdParty/` 中。

### PDF 渲染与生命周期

使用非 V8 的 `bblanchon.PDFium.Win32/macOS/Linux` 158.0.8125 原生包，仅加载页面并栅格化。没有浏览器、JavaScript 引擎、表单动作或外部链接回调。显示静态注释；动态表单、富媒体不在本次范围。带额外 PDF 打开口令的文件暂不支持，提示用文件解密后另行查看。

最多 2000 页；缩放按 25% 步长，100% 对应 96 DPI。单页渲染最多 1600 万像素、单边 8192 像素；超限拒绝该次渲染。当前只保留一张显示位图，不预渲染所有页面。初始 100% 也超限的文档需用文件解密后查看。

磁盘暂存仍在 Core 返回前删除。页面持有 PDF 明文字节以便翻页；每次后台渲染自有一份副本，避免关闭清零与原生读取竞争。关闭时清零页面缓冲并释放显示位图；后台结束时关闭 PDFium 文档/页面/位图并清零任务副本。PDFium 调用进程内串行；初始化状态保持到进程退出。

取消会在原生调用前后检查，迟到结果不会重新显示，但不能抢占正在执行的原生解析/渲染。没有硬超时、原生内存硬限制或进程隔离；复杂文档可能延迟取消，原生崩溃会影响应用。普通页数/像素限制不是对任意 PDF 的完整资源隔离。删除与清零不保证系统交换文件、原生内存副本安全擦除。

Windows 原生渲染已由像素测试验证；Mac ARM64 发布目录已包含 `libpdfium.dylib`，Mac/Linux 实机字体与原生库依赖仍待验收。原生包来源为 [pdfium-binaries](https://github.com/bblanchon/pdfium-binaries)，适配依据 [PDFium 公共渲染 API](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdfview.h)。上游许可与第三方声明随 `ThirdParty/PDFium` 一起发布。

### XLSX 展示与限制

公式只显示包内缓存结果并标注“未重算”，缓存可能过时；没有缓存则显示“公式无缓存值”。不执行宏、公式或外部数据连接，不跟随超链接。数字按存储原始值显示，日期序列、百分比和货币不套用显示格式；不还原合并单元格、样式、分页、图表或图片。图表工作表会显示不支持提示。此功能用于快速查看内容，不能替代 Excel 的完整显示或计算。

最多预览前 20 张工作表，每表范围 A1:CV2000（前 2000 行、100 列）；整个工作簿最多 50000 个单元格、20 万字符。稀疏单元格保留真实坐标，不按巨大 dimension 声明分配二维数组。范围或展示容量超限会明确提示部分内容未显示，输出不代表完整工作簿。

ZIP 中央目录最多 10000 条目；每个读取的 XML 部件最多 800 万实际展开字节、400 万 XML 字符，累计读取部件最多 3200 万字节；共享字符串最多 100000 项。读取/结构限制超限时拒绝本次预览，不输出错误索引对应的值。禁止 DTD 和外部实体，必需的工作簿、工作表和共享字符串部件只能指向包内路径；路径越界、重复条目、重复关系 ID/单元格地址及损坏索引会报错。无关的外部链接关系不会访问。

只读取受限 ZIP/XML，不新增 Office 库依赖。格式行为参考 Microsoft 的 [SpreadsheetML 工作表关系](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/working-with-sheets)及[单元格值与共享字符串](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.cellvalue)。

原文件名优先使用 PGP literal 元数据，忽略其中的目录部分；即使密文被改名，仍能按原扩展名选择预览类型。文件名永远不用于输出路径。

## 数据生命周期

单次解密明文最大 64 MiB；文本只显示前 20 万字符并提示截断。要求 PGP 完整性保护，校验完成前不会展示任何内容。旧版不带完整性保护的数据不允许在此入口预览。

暂存使用应用数据目录下随机私有工作区，复用 `PrivateWorkspace` 的 Windows 当前用户 ACL / Unix 0700 目录。明文使用固定 `payload` 名称，整个工作区在内容返回页面前删除，不在预览期间持续保存明文文件。

关闭预览、修改路径/密钥和离开页面会取消任务并清空显示、释放位图。只读文本框禁用撤销历史；异步结果返回后再次检查取消，避免已关闭的内容重新出现。图片字节缓冲在使用后清零。

正常失败和取消会清理工作区；删除失败会报错，不展示成功状态。删除不是安全擦除，.NET 字符串和系统交换文件不保证清零；用户主动复制的文本不自动清除。强杀/断电可能留下私有工作区；Avalonia 启动时在取得单实例锁后扫描应用数据目录，删除符合 `.practical-tools-folder-<32位GUID>` 的遗留工作区，失败项跳过并记录日志，不扫描其他会话目录或任意输出目录。退出正在解密时属于这一限制；已展示的内容在返回页面前已删除磁盘副本。

## 验证

Core 新增 10 项：改名密文、原文件名、HTML 被动文本、截断、图片缓冲清零、错误口令、篡改、大小上限、取消、格式拒绝、DOCX 及 DTD 拒绝（含参数化用例）。无界面 UI 新增 4 项：选钥往返、关闭/改路径/卸载清空、取消、有效和无效图片。

XLSX 阶段另新增 Core 16 项、UI 1 项：标准/Strict、多类型内容、公式缓存、关系解析、稀疏地址、隐藏/空工作表、范围与字符限制、非法结构、XML 超限，以及切表、关闭和卸载后的全部内容清空。最新完整基线：Core 106 通过 / 1 跳过，共 107；UI 16/16 通过。

PDF 阶段新增 Core 1 项、原生渲染 6 项、UI 1 项，覆盖分页像素、缩放、错误/超限、取消/并发、自有缓冲、翻页关闭与卸载清理。最新完整基线：Core 107 通过 / 1 跳过，共 108；Avalonia 测试 23/23 通过（含原生测试）。上段 XLSX 基线属于该阶段历史结果。

音视频阶段新增 Core 7 项、原生播放 3 项和 UI 1 项，最新完整基线：Core 114 通过 / 1 跳过，共 115；Avalonia 测试 27/27 通过。上面的 XLSX/PDF 数量为相应历史阶段结果。

图片/音视频格式补齐阶段新增 Core 8 项、Avalonia 3 项和原生冒烟 4 组：AAC/WMV 头校验与路由、AVIF/GIF 图片路由、遗留工作区清理、AAC 播放、WMV 帧解码、BMP/GIF/ICO/WebP/AVIF 预览、动画 GIF 路由、AVI/WMV/WAV/AAC 双轮原生播放。最新完整基线：Core 122 通过 / 1 跳过，共 123；Avalonia 测试 30/30；原生冒烟通过。Windows x64 发布 251 MB、Mac ARM64 发布 234 MB（均含 Magick.NET 原生库）。

### 平台实机验收（未完成）

- Windows：实际听 WAV/AAC 声音、看 AVI/WMV/MP4 画面，确认 GIF 动画循环；自动测试使用静音输出。
- macOS ARM64：运行 `publish-mac-arm64`，验证文件选择器、AVIF/GIF、视频窗口与声音、关闭/切页清理及 `Magick.Native-Q8-arm64` 原生库。
- Linux：明确发行版和最低版本，安装兼容 LibVLC 3 及解码/输出插件，验证 X11 视频宿主；Wayland 需确认 XWayland。

原生图片实现参考 [Avalonia Bitmap.DecodeToWidth](https://docs.avaloniaui.net/api/avalonia/media/imaging/bitmap)。无界面测试和跨平台发布不等同于 Mac/Linux 实机验收，字体、真实文件选择器和图片渲染仍待平台测试。
