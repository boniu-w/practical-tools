# 实用工具 — WPF → Avalonia 跨平台迁移计划书

> **目标**：将现有 WPF (.NET 8 Windows) 桌面程序迁移到 Avalonia UI，实现 Windows + Linux (CentOS) 跨平台运行。

---

## 一、现状分析

### 1.1 代码规模

| 类别 | 文件数 | 行数 | 改造程度 |
|---|---|---|---|
| XAML 页面 | 13 | ~1,500 | **完全重写** |
| XAML 代码后置 | 13 | ~2,000 | **大量重写** |
| ViewModel (MainViewModel.cs) | 1 | ~1,020 | **部分修改** |
| 纯 C# 业务/服务/工具类 | ~30 | ~5,400 | **少量修改** |
| **总计** | **~57** | **~8,900** | |
### 1.2 当前技术栈

| 组件 | 版本/类型 | 跨平台兼容 |
|---|---|---|
| TargetFramework | net8.0-windows | 否 |
| UI 框架 | WPF + MaterialDesignThemes | 否 |
| OutputType | WinExe | 否 |
| 运行时标识 | win-x64 | 否 |
| DPAPI 加密 | ProtectedData | 否 (Windows only) |
| ORM | EF Core 8 + SQLite SQLCipher | 是 |
| GPG 加密 | BouncyCastle.Cryptography | 是 |
| Excel 操作 | EPPlus | 是 |
| 日志 | Serilog | 是 |

### 1.3 功能模块清单

| 模块 | 涉及文件 | WPF 耦合度 |
|---|---|---|
| 重复文件扫描 | MainWindow.xaml + .cs, DuplicateFilesService | 高 |
| GPG 加密/解密 | EncryptionPage, DencryptionPage | 高 |
| GPG 密钥对生成 | GenerateKeyPair | 中 |
| GPG 密钥管理表格 | GpgFileDataGrid, GpgDetailWindow, GpgEditWindow | 高 |
| GPG 导入 | ImportGpgWindow | 中 |
| 文件管理（分页） | MainWindow.xaml (已注释) | 高 |
| 黑名单管理 | MainWindow.xaml (已注释) | 中 |
| 软件激活 | ActivationWindow, ActivationService | 中 |
| 首页导航 | HomePage | 低 |
---

## 二、目标技术栈

### 2.1 改造后的 .csproj 核心配置



### 2.2 NuGet 包变更

**移除 (WPF 专属):**

| 移除包 | 原因 |
|---|---|
| MaterialDesignThemes | WPF only |
| MaterialDesignColors | WPF only |
| Microsoft.Xaml.Behaviors.Wpf | WPF only |
| System.Security.Cryptography.ProtectedData | Windows DPAPI only |

**新增 (跨平台):**

| 新增包 | 用途 |
|---|---|
| Avalonia (>= 11.x) | 跨平台 UI 框架 |
| Avalonia.Desktop | 桌面平台支持 |
| Avalonia.Themes.Fluent | Fluent 主题 (或 Semi.Avalonia) |
| Avalonia.Controls.DataGrid | DataGrid 控件 |

**保留不变 (已跨平台兼容):**

- BouncyCastle.Cryptography
- EPPlus
- Microsoft.EntityFrameworkCore.Sqlite.Core
- SQLitePCLRaw.bundle_e_sqlcipher
- Microsoft.Extensions.Configuration.Json
- Serilog / Serilog.Sinks.Console / Serilog.Sinks.File
---

## 三、分阶段改造计划

### 第一阶段：框架搭建 + 样板页面 (1-2 天)

**当前会话目标：产出可编译运行的框架 + 2-3 个样板页面**

#### 任务清单

- [ ] 修改 PracticalTools.csproj (TFM, SDK, 包引用)
- [ ] 创建 App.axaml (Avalonia 应用资源定义)
- [ ] 重写 App.axaml.cs (生命周期: OnFrameworkInitializationCompleted)
- [ ] DI 容器、激活检查、单实例互斥体迁移
- [ ] 安装 Avalonia 主题包
- [ ] 创建 Styles/CommonStyle.axaml
- [ ] 创建 MainWindow.axaml 主窗口框架 (TabControl 左侧导航)
- [ ] 改造 GpgUniversal.axaml (GPG 功能容器)
- [ ] 改造 EncryptionPage.axaml + .axaml.cs (样板：加密页面)
- [ ] 改造 DencryptionPage.axaml + .axaml.cs (样板：解密页面)

#### MaterialDesign 到 Avalonia 样式映射

| WPF (MaterialDesignInXaml) | Avalonia 替代方案 |
|---|---|
| MaterialDesignFlatButton | Button |
| MaterialDesignRaisedButton | Button Classes=accent |
| MaterialDesignCheckBox | CheckBox |
| MaterialDesignOutlinedTextBox | TextBox |
| MaterialDesignFloatingHintTextBox | TextBox Watermark=... |
| MaterialDesignIconButton | Button + PathIcon |
| PackIcon Kind=... | PathIcon (内嵌矢量图标) |
| HintAssist.Hint=... | TextBox.Watermark |
| ButtonProgressAssist | ProgressBar + 自定义模板 |
| ColorZoneAssist | 自定义 Panel / Border |

#### App.axaml.cs 生命周期差异

---

### 第二阶段：全部页面迁移 (3-5 天)

| 序号 | 文件 | 复杂度 | 说明 |
|---|---|---|---|
| 1 | GenerateKeyPair.axaml | 中 | 密钥对生成表单 |
| 2 | GpgFileDataGrid.axaml | 高 | 密钥 DataGrid (核心) |
| 3 | GpgDetailWindow.axaml | 低 | 密钥详情弹窗 |
| 4 | GpgEditWindow.axaml | 低 | 密钥编辑弹窗 |
| 5 | ImportGpgWindow.axaml | 中 | 导入公钥弹窗 |
| 6 | GpgTabItemHeader.axaml | 低 | 页签标题控件 |
| 7 | Message.axaml | 低 | 消息提示条 |
| 8 | HomePage.axaml | 低 | 首页 |
| 9 | ActivationWindow.axaml | 中 | 激活码窗口 |
| 10 | MainWindow.axaml | 高 | 主窗口 + 重复文件扫描 |

#### XAML 语法差异速查表

| WPF | Avalonia | 备注 |
|---|---|---|
| xmlns=http://...presentation | xmlns=https://github.com/avaloniaui | 根命名空间 |
| PasswordBox | TextBox PasswordChar=* | 无独立 PasswordBox |
| RelativeSource AncestorType | parent[Window] | 相对绑定语法不同 |
| HintAssist.Hint | Watermark | 提示文字属性 |
| Visibility.Collapsed | IsVisible=False | 布尔替代枚举 |
| DataGrid | DataGrid | 需要单独安装 NuGet 包 |
| BooleanToVisibilityConverter | BoolConverters | 内置转换器 |
| RoutedEventArgs | RoutedEventArgs | 命名空间不同 |
| System.Windows.Media.Colors | Avalonia.Media.Colors | 命名空间不同 |
| SolidColorBrush | SolidColorBrush | 命名空间不同 |

#### DataGrid 关键差异

- Avalonia DataGrid 通过 NuGet 安装: Avalonia.Controls.DataGrid
- Star 宽度语法不完全兼容, 推荐用固定像素宽度
- 选中行样式通过 DataGrid.Resources 设置, 但属性名可能不同
- CellEditEnding 事件在 Avalonia 中为 CellEditEnded
---

### 第三阶段：ViewModel 与 Windows API 替换 (1-2 天)

#### MainViewModel.cs 修改点

| WPF 引用 | 替换为 |
|---|---|
| using System.Windows.Media; | using Avalonia.Media; |
| using System.Windows.Controls; | using Avalonia.Controls; |
| using Microsoft.Win32; (SaveFileDialog) | IStorageProvider |
| MessageBox.Show() | Avalonia 对话框 API |
| Application.Current.MainWindow | DI 注入 TopLevel 或直接引用 |
| Mouse.OverrideCursor / Cursors.Wait | 自定义 Loading 遮罩层 |
| System.Windows.Media.SolidColorBrush | Avalonia.Media.SolidColorBrush |
| System.Windows.Media.Colors | Avalonia.Media.Colors |

#### 文件对话框同步改异步



#### Windows 专属 API 替换方案

**A. DPAPI (ProtectedData) -> AES-256-GCM**

- 新建 util/CrossPlatformDataProtection.cs
- 首次运行生成随机 AES 密钥, 存储到 ~/.practical-tools/dpapi-key.bin
- 提供 Protect() / Unprotect() 两个静态方法
- 密钥文件设为 600 权限 (仅当前用户可读写)

**B. DriveInfo.GetDrives() -> 跨平台适配**



**C. DispatcherTimer**

- System.Windows.Threading.DispatcherTimer -> Avalonia.Threading.DispatcherTimer

**D. 回收站**

- Linux 无回收站概念 -> 改为直接删除 + 二次确认对话框
- Windows 保留原有回收站逻辑
---

### 第四阶段：数据保护方案实现 (0.5 天)

新建 util/CrossPlatformDataProtection.cs:

- AES-256-GCM 加密, 替代 Windows DPAPI
- 密钥存储在用户目录: ~/.practical-tools/dpapi-key.bin
- 首次运行自动生成随机密钥
- 接口保持与 DPAPI 一致: Protect(byte[]) / Unprotect(byte[])



---

### 第五阶段：编译与发布配置 (0.5 天)



**Linux 部署注意事项:**

- 数据库路径改为用户目录: ~/.practical-tools/sqlite-PracticalTools.db
- (单文件发布后 BaseDirectory 可能指向临时解压目录, 不适合持久化数据)
- CentOS 需安装: libicu, fontconfig
- 运行前赋予执行权限: chmod +x ./实用工具
---

### 第六阶段：测试与调试 (3-5 天)

#### 功能测试清单

| 功能 | Windows | CentOS |
|---|---|---|
| 软件启动 | [ ] | [ ] |
| 激活码验证 | [ ] | [ ] |
| 重复文件扫描 | [ ] | [ ] |
| 文件删除 | [ ] | [ ] |
| GPG 密钥对生成 | [ ] | [ ] |
| GPG 文件加密 | [ ] | [ ] |
| GPG 文件解密 | [ ] | [ ] |
| GPG 密钥导入/导出 | [ ] | [ ] |
| 密钥管理 CRUD | [ ] | [ ] |
| Excel 导入/导出 | [ ] | [ ] |
| 单实例运行 | [ ] | [ ] |
| 日志记录 | [ ] | [ ] |
| 数据库加密 (SQLCipher) | [ ] | [ ] |

#### 已知风险点

| 风险 | 影响 | 缓解措施 |
|---|---|---|
| Avalonia DataGrid 功能不如 WPF 完备 | 表格展示降级 | 简化列操作, 砍掉部分花哨功能 |
| Material 主题差异大 | 视觉还原不完美 | 优先保证功能, 后续微调样式 |
| Linux 文件系统结构不同 | 全盘扫描需适配 | 限制扫描范围到用户目录 |
| Linux 无回收站 API | 删除操作降级 | 改为直接删除 + 确认对话框 |
| 单文件发布后 BaseDirectory 变化 | 数据库路径问题 | 数据库存储到用户目录 |
| GPG (BouncyCastle) Linux 兼容性 | 加解密可能异常 | 提前在 Linux 上测试核心加解密 |
---

## 四、工期汇总

| 阶段 | 内容 | 工时 |
|---|---|---|
| 第一阶段 | 框架搭建 + 样板页面 | 1-2 天 |
| 第二阶段 | 全部页面 XAML + code-behind 迁移 | 3-5 天 |
| 第三阶段 | ViewModel + Windows API 替换 | 1-2 天 |
| 第四阶段 | 数据保护方案 (DPAPI -> AES) | 0.5 天 |
| 第五阶段 | 编译与发布配置 | 0.5 天 |
| 第六阶段 | 测试与调试 | 3-5 天 |
| **合计** | | **10-15 天 (2-3 周)** |

> 以上为 1 人全职估算。实际可能因 Avalonia 的坑 (DataGrid, 对话框, 平台差异) 而浮动 20%-30%。

---

## 五、附录

### A. 文件映射表 (WPF -> Avalonia)

| 原 WPF 文件 | 新 Avalonia 文件 |
|---|---|
| App.xaml | App.axaml |
| App.xaml.cs | App.axaml.cs (重写) |
| MainWindow.xaml | MainWindow.axaml |
| MainWindow.xaml.cs | MainWindow.axaml.cs |
| Views/HomePage.xaml | Views/HomePage.axaml |
| Views/EncryptionPage.xaml | Views/EncryptionPage.axaml |
| Views/DencryptionPage.xaml | Views/DencryptionPage.axaml |
| Views/GenerateKeyPair.xaml | Views/GenerateKeyPair.axaml |
| Views/GpgUniversal.xaml | Views/GpgUniversal.axaml |
| Views/GpgFileDataGrid.xaml | Views/GpgFileDataGrid.axaml |
| Views/GpgDetailWindow.xaml | Views/GpgDetailWindow.axaml |
| Views/GpgEditWindow.xaml | Views/GpgEditWindow.axaml |
| Views/ImportGpgWindow.xaml | Views/ImportGpgWindow.axaml |
| Views/GpgTabItemHeader.xaml | Views/GpgTabItemHeader.axaml |
| Views/Message.xaml | Views/Message.axaml |
| Views/ActivationWindow.xaml | Views/ActivationWindow.axaml |
| ResourceDictionary/CommonStyle.xaml | Styles/CommonStyle.axaml |
| util/MyBooleanToVisibilityConverter.cs | 删除 (Avalonia 内置) |
### B. 可复用文件清单 (不改核心逻辑)

**Service 层 (完全复用):**
- GPG/GpgHelper.cs - GPG 加解密核心 (依赖 BouncyCastle, 跨平台)
- GPG/GpgFileService.cs - GPG 密钥 CRUD
- DuplicateFiles/Service/DuplicateFilesService.cs - 重复文件扫描
- FileManager/Service/MyFileService.cs - 文件管理
- Blacklist/Service/BlacklistService.cs - 黑名单管理
- Activation/Service/ActivationService.cs - 激活验证

**Model 层 (完全复用):**
- GPG/GpgFile.cs, GpgFileEntryWrapper.cs
- DuplicateFiles/Model/DuplicateFilesEntry.cs
- DuplicateFiles/Wrapper/DuplicateFilesEntryWrapper.cs
- FileManager/Model/MyFile.cs
- Blacklist/Model/BlacklistEntry.cs
- Activation/Model/ActivationRecord.cs
- Accounting/Model/Accounting.cs

**数据层 (完全复用):**
- Sqlite/MyDbContext.cs - EF Core 上下文
- Sqlite/DatabaseMigrationHelper.cs - 数据库迁移
- Sqlite/SQLiteDemo.cs - 数据库演示

**工具类 (完全复用):**
- Vo/PagedList.cs - 分页列表
- Common/RelayCommand.cs - ICommand 实现
- Common/ColumnHeaderAttribute.cs - 列头注解
- Constant/CommonConst.cs - 常量定义
- Constant/FileType.cs - 文件类型常量
- TheConst/FileCategoryHelper.cs - 文件分类
- TheConst/FileExtensionCategories.cs - 扩展名分类
- util/SnowflakeId.cs - 雪花 ID
- util/StringUtil.cs - 字符串工具
- util/DateTimeConverter.cs - 日期转换
- util/MathUtil.cs - 数学工具
- util/SecondsToDurationConverter.cs - 秒数转换 (IValueConverter)

**需要小改的工具类:**
- util/CryptoHelper.cs - DPAPI 调用需替换为 CrossPlatformDataProtection
- util/FileUtil.cs - 路径处理需适配 Linux (主要确保用 Path.Combine)
### C. Avalonia 项目 .csproj 完整参考



---

---
  计划书能解决的
     
  ┌──────────────────────────────┬──────────────────────────────────────────────────────┐
  │             问题             │                         效果                         │
  ├──────────────────────────────┼──────────────────────────────────────────────────────┤
  │ 每次会话都要重新理解项目结构 │ ✅) 解决了 — 现状分析、模块清单写死了，不用重新读代码 │
  ├──────────────────────────────┼──────────────────────────────────────────────────────┤
  │ 每次都要查 XAML 语法差异     │ ✅  解决了 — 对照表写了，直接参考                     │
  ├──────────────────────────────┼──────────────────────────────────────────────────────┤
  │ 忘记文件映射关系             │ ✅  解决了 — 附录 A 有完整映射表                      │
  ├──────────────────────────────┼──────────────────────────────────────────────────────┤
  │ 忘记哪些 API 要替换          │ ✅  解决了 — 阶段三里列清楚了                         │
  ├──────────────────────────────┼──────────────────────────────────────────────────────┤
  │ 每次都要权衡技术选型         │ ✅  解决了 — 主题选 Semi.Avalonia，方案已定           │
  └──────────────────────────────┴──────────────────────────────────────────────────────┘

---
  仍然存在的硬伤

  ┌─────────────────────────────────────┬───────────────────────────────────────────────────────────────────────┐
  │                问题                 │                             为什么没解决                              │
  ├─────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
  │ 一阶段要改 8-10 个文件              │ 每个 XAML 文件 50-200 行，逐个重写，互相引用，消耗极快                │
  ├─────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
  │ App.axaml.cs 重写要参考原来的逻辑   │ 我必须同时持有原 App.xaml.cs (204 行) + 新写法 + DI 逻辑 → 上下文爆了 │
  ├─────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
  │ MainViewModel 有 1019 行 + WPF 引用 │ 读它、理解它、改它 → 一次就吃掉大量上下文                             │
  ├─────────────────────────────────────┼───────────────────────────────────────────────────────────────────────┤
  │ code-behind 和 XAML 强关联          │ 改 .axaml 时要同步看 .axaml.cs，双重消耗                              │
  └─────────────────────────────────────┴───────────────────────────────────────────────────────────────────────┘

---
  实际需要的额外策略

  如果真要用多会话完成，需要做的不只是"写计划书"，还需要：

    1. 把大文件拆碎

  现在的 MainViewModel.cs 1019 行太大了。如果在改造前先把它拆成职责单一的多个文件：
  MainViewModel.cs (1019 行)
  → 拆成:
    GpgViewModel.cs        (~300 行, 密钥管理)
    ScanViewModel.cs       (~200 行, 文件扫描)
    FileManageViewModel.cs (~200 行, 文件管理)
    ShellViewModel.cs      (~300 行, 导航/状态)
  这样每会话只改一个 200-300 行的 ViewModel，轻松很多。

    2. 先把 Service/Model 层独立出来

  业务逻辑已经不依赖 WPF，可以先建一个 class library 项目 PracticalTools.Core（net8.0），把 Service/Model/工具类迁过去，一次搞定。后续只改 UI 层，不用反复读业务代码。

    3. 会话间"接力"用文件状态传递

  每改完一个文件就跑 dotnet build。成功了 → 下一个会话直接从 git diff 看到哪些改了、哪些没改，不需要我从零回忆。

---
  一句话

  ▎ 计划书降低了认知负担（不用每次重新理解项目），但没有降低代码量（还是要写 8900 行等价的新代码）。真正对抗上下文限制的手段是拆文件 + 拆分层 + 以编译结果作为会话间交接物。

  要不要我先把拆分 ViewModel 和独立 Core 类库做了？这两个铺垫做完，后续每次会话只碰 2-3 个 UI 文件，上下文完全够用。