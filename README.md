# 文件夹主题工坊 / Folder Theme Studio

当前源码：**0.1.0-beta.24-preview.8 时间示例与精选图标试用**。增加专注/休息/每日HH:mm提醒，AI前置提示词先询问需求，图标设计页内置五款精选图片。原生注入尚未实现。详见[试用说明](docs/releases/v0.1.0-beta.24-preview.8.md)。

<p align="center">
  <img src="assets/brand/raster/folder-theme-studio-256.png" width="160" alt="Folder Theme Studio logo">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-v0.1.0--beta.23-1769e0" alt="Version v0.1.0-beta.23">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078d4" alt="Windows 10/11 x64">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-5c6bc0" alt="Apache-2.0 license"></a>
</p>

<p align="center">
  为普通 Windows 文件夹设计统一、可持续、可恢复的个性化图标。<br>
  Design consistent, persistent, and recoverable icons for ordinary Windows folders.
</p>

---

## 简体中文

Folder Theme Studio 是一款面向 Windows 10/11 x64 的开源文件夹图标定制工具。它只处理普通文件夹图标，不替换文件资源管理器、不修改文件类型图标，也不会把系统特殊文件夹当作普通目录处理。

你可以通过可视化调色板设计内置文件夹样式，也可以导入自己的图片作为完整图标。应用前，软件会先计算方案并展示允许与跳过的项目；应用后可通过备份恢复。对于需要长期统一风格的目录，还能启用递归监控，让之后新建的文件夹自动继承所选样式。

### 功能亮点

- **二维可视化调色板**：直接选择色相、饱和度和亮度，并分别调整渐变起始色、渐变结束色、描边和发光颜色。
- **配色、材质与贴纸**：12 组苹果风格配色，五种文件夹材质，四种可拖动定位的贴纸。
- **模拟桌面**：切换背景和图标尺寸，预览实际桌面排列效果。
- **图片图标导入**：支持 PNG、JPG、JPEG、BMP、ICO；自动保持比例、居中并补充透明边距。
- **无需保存即可使用**：刚完成的样式编辑或刚导入的图片会立即成为当前图标，保存个人主题仅用于以后复用。
- **多尺寸实时预览**：同步检查 16、32、64、256 px 效果。
- **两种应用模式**：全局模式统一普通文件夹映射；兼容模式只处理明确选择的目录。
- **已有样式可再次修改**：仅合并 `Desktop.ini` 中的图标字段，保留其他设置、注释、编码和换行格式。
- **递归持久监控**：为指定根目录保持样式，新建文件夹自动继承当前图标。
- **悬浮分类卡片**：为桌面或资源管理器中的真实文件夹添加分类和备注；卡片固定在文件夹旁，支持双色渐变强调色与统一透明度。
- **备份与恢复**：应用前记录原有配置；符合恢复条件时精确还原原始 `Desktop.ini` 内容和属性。
- **后台运行**：关闭主窗口默认收起到系统托盘；支持登录 Windows 后自动在后台启动并恢复监控。
- **中英双语界面**：简体中文与 English 可在设置中切换。
- **原生圆角窗口**：主窗口、设置和教程在受支持的 Windows 版本上使用原生圆角，同时保留系统标题栏与窗口操作。

### Beta 23 更新

- 灵动岛改为透明单行、原生点击穿透，悬停精简摘要；从托盘编辑位置。
- DIY 插件支持一键安装、独立启停、权限授权及故障隔离；内置专注示例与可展开 AI 提示词。
- 事件 Hook 开放音乐、剪贴板、通知数据；无插件时停止插件轮询，媒体元数据按变化缓存。
- 移除双层桌面并保留升级恢复。见 [Beta 23 更新说明](docs/releases/v0.1.0-beta.23.md) 与 [插件 SDK](plugins-sdk/README.zh-CN.md)。

### Beta 20 更新

- 灵动岛按活动自动切换：新消息优先，其后为剪贴板预览、播放中的歌词或歌名，金句只在空闲时兜底。
- 消息每秒独立刷新，提醒结束恢复当前活动；暂停或失效的媒体会话不再遮挡金句。
- 歌词需开启对应模块；在线歌词保留原联网开关。QQ 等需发布 Windows 系统通知并获准读取，设置显示实际授权状态。

### Beta 19 更新

- 功能独立开关：主题编辑、自动监控、悬停卡片、快捷备注、灵动岛和软件内默认背景分别启停，并保存偏好。
- 关闭功能停止对应监听与采样，保留已生成图标、主题和备注；快捷备注关闭后仍正确抑制原生单击、双击和拖拽引发的悬停效果。
- 灵动岛歌词可单独关闭，CPU 与内存分别按开关采样。
- 单行灵动岛新增一言金句（30 秒轮换）、剪贴板优先预览（10 秒）、物理弹簧伸缩和可更换的淡色渐隐图片背景，宽度最长约 8 cm。

### Beta 18 更新

- 新默认背景与桌面预览，保留纯色选项，不改 Windows 壁纸。
- [60 秒宣传片与可编辑工程](docs/PROMO-FILM.zh-CN.md)，回顾 Beta 8 以来更新；简约介绍网站同步新版下载。


- 新消息队列、来源屏蔽与去重；拖动弹性、阴影和可选边缘吸附。


- 在线同步歌词：本地 LRC 优先、显式开启网络、多来源回退、后台缓存；右键可控制播放/暂停和上下一首。
- 可选的令牌保护本地消息接口；系统通知按 Windows 授权状态使用，权限边界见 [灵动岛指南](docs/DYNAMIC-ISLAND.zh-CN.md)。
- [GitHub 上传步骤](docs/GITHUB-UPLOAD.zh-CN.md) · [参考致谢](THIRD-PARTY-NOTICES.md)

- 灵动岛为高度 42 DIP（含阴影）的单行胶囊，宽度弹簧自适应、最长约 8 cm；一言金句每 30 秒更新，剪贴板优先展示 10 秒。支持淡色渐隐图片背景及独立开关，悬停查看详情。


- 新增内置桌面灵动岛，显示时间、性能、可选剪贴板、系统媒体信息及本地 LRC 同步歌词；支持拖动、透明度滚轮和模块设置。
- [灵动岛运行、开发与通知授权说明](docs/DYNAMIC-ISLAND.zh-CN.md)，包含完整源码清单。
- 创建时间、修改时间、内容数量与大小摘要合并到文件夹便签；识别目标后临时收起对应的 Windows 信息提示。
- 不修改系统全局提示设置；大目录和不可读内容明确标注部分统计。
- 新首页整合图标设计、活文件夹、自动样式和备份恢复入口。
- 内置 12 组苹果风格配色，新增更柔和的文件夹造型与柔光材质。
- 默认折叠高级参数，三步教程帮助完成样式选择、文件夹选择和应用。
- 桌面快捷备注改为精简窗口，自动绑定快捷键点击的文件夹；支持 Ctrl＋Enter 保存和 Esc 取消。
- 设置、规则、分类、教程及悬浮卡片采用统一的圆角、留白和控件样式。

完整变更参见 [v0.1.0-beta.18 发布说明](docs/releases/v0.1.0-beta.18.md)。

### 安装

1. 从项目 Release 下载 `FolderThemeStudio-v0.1.0-beta.18-setup.exe` 及同名 `.sha256` 文件。
2. 核对安装包 SHA-256。
3. 运行安装器。若系统缺少 x64 `.NET 8 Desktop Runtime`，安装器会联网下载并安装。

> 当前发布的是未签名 Beta 版本，Windows 可能显示 SmartScreen 提示。建议首次使用时先在一次性测试目录中通过兼容模式验证效果。

### 快速开始

1. 在“图标来源”中选择内置文件夹样式，或导入 PNG、JPG、JPEG、BMP、ICO 图片。
2. 调整颜色并检查 16、32、64、256 px 实时预览。
   当前编辑或刚导入的图标无需保存个人主题即可继续使用。
3. 选择全局模式，或在兼容模式中添加一个或多个明确根目录。
4. 点击“计算方案”，检查允许、跳过和失败项目后再应用。
5. 如需让新文件夹继续继承样式，启用递归监控；需要撤销时使用“恢复最近备份”。

### 应用模式

| 模式 | 适合场景 | 行为 |
| --- | --- | --- |
| 全局模式 | 希望统一当前用户普通文件夹的默认外观 | 修改普通文件夹图标映射，不扫描整个磁盘 |
| 兼容模式 | 只想修改项目、素材库或指定目录 | 仅处理明确添加的根目录；跳过特殊、受保护、网络、重解析或不可写路径 |
| 兼容模式 + 递归监控 | 希望目录中的新文件夹长期保持同一风格 | 监控所选根目录，新建文件夹自动应用已保存的图标 |

### 使用须知

- 软件只替换普通文件夹图标，不替换文件资源管理器本身。
- GIF 不受支持；动态图也不适合作为 Windows 文件夹图标。
- Windows 图标缓存可能不会立即刷新，可重新打开文件夹窗口或稍后查看。
- 托盘菜单可打开主窗口、暂停/恢复监控或彻底退出。
- 开机自启和关闭到托盘均可在“设置”中关闭。

### 推荐图标资源

想导入现成的文件夹图标，可以访问第三方项目 [Folder11 Ico](https://github.com/icon11-community/Folder-Ico) 下载 `.ico` 文件，再使用本软件的图片图标导入功能。图标由该项目提供，使用前请查看其说明和授权条款。

### 文档

- [中文使用教程](docs/USER-GUIDE.zh-CN.md)
- [English User Guide](docs/USER-GUIDE.en-US.md)
- [Beta 18 发布说明](docs/releases/v0.1.0-beta.18.md)
- [双语项目宣传文章](docs/PROMOTION.zh-en.md)
- [人工测试清单](docs/manual-test-checklist.md)
- [安全策略](SECURITY.md)
- [参与贡献](CONTRIBUTING.md)

### 从源码构建

开发环境需要 Windows x64 和 .NET 8 SDK；生成安装器还需要 Inno Setup 6。

```powershell
dotnet restore FolderThemeStudio.sln
dotnet test FolderThemeStudio.sln -c Release
dotnet build src\FolderThemeStudio.App\FolderThemeStudio.App.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Package-Release.ps1 -Version v0.1.0-beta.18 -InnoCompilerPath "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

欢迎通过 Issue 提交可复现的问题，也欢迎先阅读 [参与贡献](CONTRIBUTING.md) 后改进界面、文档、兼容性或测试。涉及敏感问题时，请遵循 [安全策略](SECURITY.md)。

### 许可证

本项目基于 [Apache License 2.0](LICENSE) 开源。

---

## English

Folder Theme Studio is an open-source folder icon customization tool for Windows 10/11 x64. It targets ordinary folder icons only: it does not replace File Explorer, alter file-type icons, or treat protected system locations as ordinary folders.

You can build a folder style with a visual color palette or import an image as the complete icon. Before changing anything, the app calculates a plan and reports what is allowed or skipped; afterwards, its backup workflow can restore the previous state. For directories that need a lasting visual identity, recursive monitoring automatically applies the selected style to newly created folders.

### Highlights

- **Two-dimensional visual color palette**: pick hue, saturation, and brightness directly, then tune gradient start, gradient end, stroke, and glow independently.
- **Image icon import**: supports PNG, JPG, JPEG, BMP, and ICO, preserving aspect ratio while centering the image on transparent padding.
- **No save required for current use**: new style edits and imported images become the current icon immediately; saving a personal theme is only for later reuse.
- **Colors, materials, and stickers**: twelve Apple-inspired colors, five folder materials, and four draggable stickers.
- **Simulated desktop**: preview the icon against different backgrounds and at different desktop sizes.
- **Live multi-size preview**: inspect the result at 16, 32, 64, and 256 px.
- **Two application modes**: Global mode updates the ordinary-folder mapping; Compatible mode stays within explicitly selected roots.
- **Restyle existing custom folders**: only icon-related fields in `Desktop.ini` are merged, while unrelated settings, comments, encoding, and line endings are retained.
- **Persistent recursive monitoring**: keep a style attached to a selected root so new folders inherit its icon automatically.
- **Folder hover cards**: add a category and note to a real desktop or Explorer folder; the stationary card supports a two-color accent and shared opacity.
- **Backup and restore**: capture pre-existing configuration before applying and restore the original `Desktop.ini` bytes and attributes when recovery conditions are met.
- **Background operation**: closing the main window minimizes to the system tray by default; monitoring can resume automatically in the background after Windows sign-in.
- **Bilingual interface**: switch between Simplified Chinese and English in Settings.
- **Native rounded windows**: Main, Settings, and Tutorial use native rounded corners on supported Windows versions while retaining standard system window controls.

### What is new in Beta 23

- Transparent, single-line Desktop Island with native mouse pass-through, compact hover details and tray-based position editing.
- Installable DIY plugins with independent toggles, explicit data permissions and isolated workers. Includes a focus timer example and a collapsed AI prompt.
- Media, clipboard and notification hooks; no idle plugin polling. Media metadata is cached until native change events invalidate it.
- Removed desktop layers; upgrade recovery restores native icons. See [release notes](docs/releases/v0.1.0-beta.23.md) and [plugin SDK](plugins-sdk/README.zh-CN.md).

### What is new in Beta 20

- New messages take priority, followed by short clipboard previews and lyrics or track titles during active playback. Quotes fill idle time.
- Independent one-second notification checks remain responsive during slow media queries. Paused or invalid sessions no longer hide idle content.
- Lyrics and online lookup honor existing switches. QQ and similar apps must publish Windows notifications and notification access must be granted.

### What is new in Beta 18

- Opt-in online synchronized lyrics with local LRC priority, provider fallback, background caching, and media playback controls.
- Optional authenticated loopback message API; native notifications remain subject to Windows authorization.

- Desktop Island uses a single-line capsule (42 DIP including shadow), spring-driven adaptive width capped at about 8 cm, Hitokoto quotes every 30 seconds, and 10-second clipboard previews. Pale, fading image backgrounds and individual switches are available.


- Built-in Desktop Island: clock, performance, optional clipboard, system media and synchronized local LRC lyrics, with draggable position and wheel opacity controls.
- [Desktop Island development and notification setup guide](docs/DYNAMIC-ISLAND.zh-CN.md).
- Folder cards now include created/modified dates, direct content counts, and a recursive size summary, while dismissing the matching Windows info tip.
- Windows tooltip preferences stay unchanged. Large or unreadable directories are clearly marked as partially counted.
- A new Home brings Icon studio, Live folders, Automatic styles, and Backup & restore together.
- Twelve Apple-inspired colors and a new soft satin folder silhouette.
- Advanced controls are collapsed by default, with a three-step onboarding guide.
- A compact desktop note editor automatically selects the clicked folder. Ctrl + Enter saves; Esc cancels.
- Consistent spacing, rounded surfaces, and controls across Settings, rules, notes, tutorials, and hover cards.

See the [v0.1.0-beta.18 release notes](docs/releases/v0.1.0-beta.18.md) for the complete summary.

### Installation

1. Download `FolderThemeStudio-v0.1.0-beta.18-setup.exe` and its matching `.sha256` file from the project Release.
2. Verify the installer SHA-256.
3. Run the installer. If the x64 `.NET 8 Desktop Runtime` is missing, the installer downloads and installs it over the network.

> This is an unsigned Beta build, so Windows may display a SmartScreen warning. For a first run, use Compatible mode with a disposable test directory and verify the result before applying more broadly.

### Quick start

1. Choose a built-in folder style under Icon source, or import a PNG, JPG, JPEG, BMP, or ICO image.
2. Tune the colors and inspect the live 16, 32, 64, and 256 px previews.
   Current edits and newly imported icons can be used without saving a personal theme.
3. Choose Global mode, or add one or more explicit roots in Compatible mode.
4. Select Calculate plan, review allowed, skipped, and failed items, and then apply.
5. Enable recursive monitoring if future folders should inherit the style; use Restore latest backup when you need to undo the last application.

### Application modes

| Mode | Best for | Behavior |
| --- | --- | --- |
| Global | Giving ordinary folders a consistent default appearance for the current user | Updates the ordinary-folder icon mapping without scanning the whole drive |
| Compatible | Styling only a project, asset library, or selected directory | Processes explicit roots and skips special, protected, network, reparse-point, or unwritable paths |
| Compatible + recursive monitoring | Keeping future folders in a directory visually consistent | Watches selected roots and applies a persisted icon to newly created folders |

### Important notes

- The app changes ordinary folder icons only; it does not replace File Explorer.
- GIF is not supported, and animated images are not suitable as Windows folder icons.
- Windows icon caching can delay visible changes; reopen the folder window or check again later.
- The tray menu can open the main window, pause/resume monitoring, or exit completely.
- Startup after sign-in and close-to-tray behavior can both be disabled in Settings.

### Recommended icon resource

For ready-made folder icons, download `.ico` files from the third-party [Folder11 Ico](https://github.com/icon11-community/Folder-Ico) project and import them with this app's image icon import feature. Check the project's documentation and licensing terms before use.

### Documentation

- [中文使用教程](docs/USER-GUIDE.zh-CN.md)
- [English User Guide](docs/USER-GUIDE.en-US.md)
- [Beta 18 release notes](docs/releases/v0.1.0-beta.18.md)
- [Bilingual project announcement](docs/PROMOTION.zh-en.md)
- [Manual test checklist](docs/manual-test-checklist.md)
- [Security policy](SECURITY.md)
- [Contributing guide](CONTRIBUTING.md)

### Build from source

Development requires Windows x64 and the .NET 8 SDK. Inno Setup 6 is also required to create the installer.

```powershell
dotnet restore FolderThemeStudio.sln
dotnet test FolderThemeStudio.sln -c Release
dotnet build src\FolderThemeStudio.App\FolderThemeStudio.App.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Package-Release.ps1 -Version v0.1.0-beta.18 -InnoCompilerPath "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

Reproducible bug reports are welcome through Issues. Read the [contributing guide](CONTRIBUTING.md) before proposing interface, documentation, compatibility, or test improvements. For sensitive reports, follow the [security policy](SECURITY.md).

### License

Folder Theme Studio is released under the [Apache License 2.0](LICENSE).
