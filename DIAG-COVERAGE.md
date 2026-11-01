# 诊断包覆盖矩阵（2026-11 版）

> 目标：用户来反馈「QQ 能正常进，但每次启动都弹报错」「换了壁纸图片，背景不显示」时，
> 只看一个 zip 就能判定原因，不需要来回问。
>
> 本文是**实现契约**：安装器、插件主进程、插件渲染进程、验证脚本都按这里的路径/字段/布局实现。
> 任何一项采集失败都不允许让整包失败：失败项写进 `report.txt` 的「采集失败」小节，其余照常打包。

## 一、症状 → 证据链

### S1：每次启动弹报错（不影响使用）

| 可能原因 | 需要的证据 | 采集方 |
|---|---|---|
| QQ 原生弹窗（安全组件/文件校验/更新器/内核） | 弹窗标题 + 正文（Win32 窗口枚举，`#32770` 对话框 + 子控件文本） | 安装器 |
| 双开/多主进程抢同一份数据 | QQ 主进程数、PID/父 PID/命令行/启动时间、`nt_msg.db` 占用者（Restart Manager） | 安装器 |
| LiteLoader 插件 main 加载抛错（只打在 QQ 控制台，不落盘） | 安装器内置 LiteLoader 加 tee 日志：`liteLoader.log` 里每次启动的 banner、每个插件 loaded/失败栈、LiteLoader 自己的弹窗正文；`uncaughtExceptionMonitor` 记录主进程未捕获异常（不改变崩溃行为） | 安装器（loader） |
| 渲染进程注入失败/JS 报错 | 每次启动的渲染进程错误环形缓冲（onerror/unhandledrejection/console.error），转发到 `session.log` | 插件渲染进程 |
| 打补丁的文件被 QQ 更新覆盖 | `QQ.exe`/`dbghelp.dll`/`winmm.dll` 是否存在、大小、版本、SHA256、是否为安装器内置补丁；`versions` 版本号与运行中进程 exe 路径是否一致 | 安装器 |
| `package.json` / `app_launcher` 被改坏/没改到 | 每个 `app` 的 `main` 字段、`app_launcher/LiteLoader.js` 是否存在且内容正确、是否有未打补丁的 app 目录 | 安装器 |
| LiteLoader 本体被覆盖成官方版（`dirent.path` 崩溃） | 本体版本、`src/main/store.js` 是否含 `LiteLoader.path.plugins` 修复、`src/main/runtime.js` 是否有 `unregisterPlugin` | 安装器 |
| 杀软/受控文件夹访问拦截 | Defender CFA 状态、拦截事件 1123/1124/1127/1128、Application 错误事件 | 安装器 |
| 日志里的真实错误 | `errors-digest.txt`：按模式聚合 QQ/插件日志中的错误行（次数/首末时间/样例行） | 安装器 + 插件 |

### S3：界面渲染异常 / 主题不生效（设置页控件像没套样式）

| 可能原因 | 需要的证据 | 采集方 |
|---|---|---|
| 某一份 CSS 资源 404/损坏，启动注入在 `FILES` 循环里抛错（`data-liuli-theme=failed`，面板仍是原生控件） | 逐个 `FILES` fetch 状态/字节数/是否真的进了 `<style>`、启动注入的异常消息、`data-liuli-theme` 取值、`#liuli-dsh-theme` 是否存在/长度/`sheet.cssRules.length` | 插件渲染进程 |
| CSS 注入成功但解析失败/被裁剪 | style 标签长度、规则条数、关键变量（`--ll-brand/--ll-acrylic/--ll-material-blur`）计算值是否为空、`.liuli-btn`/`.liuli-embedded` 的计算样式（border-radius/background/font） | 插件渲染进程 |
| 主题只在部分窗口生效（主窗口正常、设置窗口黑底原生控件） | 每个窗口独立快照：`liuliTheme`、style 长度、规则数、变量值、面板控件计算样式、viewport | 插件渲染进程 + 主进程 |
| 插件文件缺失/被截断/旧版与新版代码混装（如缺 `lists.css`、`markdown.css`） | 已装插件每个文件与安装器 `plugins.zip` 的清单/大小/SHA256 对比；manifest 版本；renderer 哈希 | 安装器 |
| 设置页入口（`onSettingWindowCreated`）执行了但样式没生效 | 面板是否构建（`#liuli-embedded` 存在/子节点数）、`liuli-*` class 元素数、渲染进程报错环形缓冲 | 插件渲染进程 |
| 用户截图里能直接看到的样子 | 设置窗口截图（不含聊天，**默认采集**）；主窗口截图需显式勾选 | 插件主进程 |
| 其它插件注入失败连累/顺序问题 | 所有插件 manifest/injects/禁用状态、LiteLoader 版本、启动错误 session.log | 安装器 + 插件 |

### S2：换了壁纸图片，背景不显示

| 可能原因 | 需要的证据 | 采集方 |
|---|---|---|
| 选图后写入 localStorage 失败（配额/异常，现在被 `catch {}` 吞掉） | 壁纸操作审计：选图文件名/大小/类型 → 压缩后 dataURL 长度 → `setItem` 结果/异常名与消息 → 广播 → 目标窗口收到时间 → 最终是否应用（保留最近 20 条，落 `session.log`） | 插件渲染进程 |
| dataURL 过大 / 图片解码失败 | `src` 长度、MIME、`Image.decode()` 结果、naturalWidth/Height、预估内存体积、localStorage 各键占用、配额探测结果 | 插件渲染进程 |
| 写进去了但主窗口没收到（两个 webContents / 分区不同） | 每个窗口的 `origin`、`session` 是否与主窗口同一个、`localStorage` 键值与字节数、`storage` 事件计数/最近时间、壁纸 revision 键是否一致 | 插件主进程 + 渲染进程 |
| 写进去了但 CSS 没生效 | `#app` 是否存在/尺寸/position、`#app::before` 的 `content/background-image/background-size/background-position/z-index`、`#liuli-wallpaper-style` 是否存在/长度/是否含当前 `src`、`#liuli-dsh-theme` 长度、`--ll-brand/--ll-scrim-rgb/--ll-wallpaper-dim` 计算值 | 插件渲染进程 |
| 主题 CSS 没加载全（local:// 资源缺失/版本旧） | `FILES` 逐个 fetch 状态、`data-liuli-theme` 是 `dsh` 还是 `failed`、插件版本 vs 安装器内置版本、renderer 文件哈希 | 插件渲染进程 + 安装器 |
| 默认壁纸文件缺失（安装器版不带 `wallpaper.css`） | 插件 `assets/` 文件清单（`wallpaper.css`/`wallpaper.jpg` 是否存在、大小）、取色源（`liuliSource`） | 安装器 + 插件 |

> 注意：安装器**故意不发布**默认壁纸（`assets/wallpaper.css|jpg` 在插件 .gitignore 里），
> 缺这两个文件属正常状态，**不得**作为故障/`[!]` 提示；用户选过图之后才应该有壁纸。

| 用户装的是旧版插件 | 已装插件 `manifest.version`、`renderer/index.js` 哈希 vs 安装器 `plugins.zip` 内哈希、插件目录 mtime | 安装器 |
| 背后的真实原因（二期） | 在设置页点「导出诊断包」拿到的就是上面这些；含截图时可直接看到主窗口画面 | 插件 |

## 二、安装器诊断包（`Installer.cs`）

输出：桌面 `QQ诊断包-<yyyyMMdd-HHmmss>.zip`；桌面不可用（OneDrive 重定向/不存在/只读）时依次回退
`%USERPROFILE%\Desktop` → `%USERPROFILE%\Documents` → `%TEMP%` → `%APPDATA%\LiteLoaderQQNT\diag`，
并在 UI/日志里写明最终路径。

zip 布局（顶层必须是这些名字，便于人肉打开）：

```
environment.txt        人读总报告（原有 + 新增小节 + 顶部「自动体检」结论清单）
errors-digest.txt      日志错误聚合（新增）
README.txt             包内说明 + 隐私声明 + 怎么提交（新增）
QQ-log/…               原有
QQ-arks/…              原有
QQ-crashpad/…          原有
Tencent-Logs/…         原有
LiteLoader/…           原有（顶层 .log/.txt，含新增 liteLoader.log）
 installer.log           安装器日志（GUI / headless 都写，带时间戳、1MB 轮转）
LiteLoader-data/…      新增：data 下各插件的小日志（.log/.txt，≤2MB/个，30 天内）
plugin-diag/<name>.zip  新增：插件设置页导出的诊断包（取 data/liuli-dsh-theme/diag 里最新一个）。
                       内容由插件导出策略决定：默认含设置页截图，勾选「包含聊天窗口截图」时还可能含聊天画面；
                       安装器只是原样带上，自身不截图
```

`environment.txt` 新增小节（每节都要 try/catch，失败写「采集失败: …」）：

1. `---------- 自动体检（先看这里）----------`
   - 逐条 `[ ]/[!]/[OK]` 结论，命中问题的用 `<<==` 标出，涵盖：
     双主进程、运行中的 QQ 不在安装目录、补丁文件缺失/不是内置补丁、`package.json main` 未指向
     `app_launcher/LiteLoader.js`、`app_launcher` 缺失、LiteLoader 本体不是修补版、插件 manifest
     带 BOM/JSON 解析失败/被禁用/版本落后、聊天记录在可移动盘/网络盘、Defender CFA 开启、
     QQ 有原生对话框窗口、日志里出现已知错误模式、插件导出包缺失/过期。
2. `---------- 已安装插件（版本 / 是否被禁用 / 文件是否完整）----------`
   - 现有 manifest 内容保留；新增：装到的版本、安装器内置版本、renderer/main/preload 文件是否存在、
     大小、SHA256、mtime、是否与内置一致、是否存在 `xxx_<时间戳>` 残留目录。
   - **逐文件完整性**：把安装器内置 `plugins.zip` 的条目清单与已装目录对比，输出每个文件
     「缺失 / 大小不同 / 与内置不一致 / 未在内置清单里」；重点标出 `assets/*.css`。
     这一类问题会直接造成「UI 渲染异常、主题不生效」，必须在自动体检里单列一条。
3. `---------- LiteLoader 配置与插件数据 ----------`
   - `data/LiteLoader/config.json`（不存在时写默认值说明：`enable_plugins=true, disabled_plugins=[]`）
     的 `enable_plugins/disabled_plugins/deleting_plugins/installing_plugins`；
   - 每个插件 data 目录：`config.json`（脱敏、≤4KB）、其它文件数量/总大小、`.log/.txt` 尾部 80 行；
   - 插件 `diag/` 里导出的诊断包（文件名、大小、时间）。
4. `---------- QQ 应用打补丁状态 ----------`
   - 每个 `versions/*/resources/app`：`main` 字段、`app_launcher/LiteLoader.js` 存在与内容、
     `package.json` 是否 BOM/能否解析、是否被 QQ 更新器重置；
   - `QQ.exe` 文件版本 vs `versions` 目录版本、`versions/config.json`；
   - 运行中的 QQ.exe 路径是否等于安装目录（从进程节复用）。
5. `---------- LiteLoader 本体 ----------`
   - `package.json` 版本、`src/main/store.js` 是否仍有 `dirent.path`（应无）、是否含
     `LiteLoader.path.plugins` 修复、`src/main/runtime.js` 是否有 `unregisterPlugin`；
   - `src/` 关键文件大小/mtime（不打包源码）。
6. `---------- 显示器 / 显卡 / 会话（壁纸与渲染异常）----------`
   - 显示器数量、每屏分辨率/缩放、主屏工作区、是否远程会话（`SystemInformation.TerminalServerSession`）、
     DPI、WMI `Win32_VideoController`（名称/驱动版本）、物理内存。
7. `---------- QQ 窗口与对话框（启动报错取证）----------`
   - 枚举 `QQ.exe`/`QQEX.exe` 进程的所有顶层窗口：PID、窗口类、标题、是否可见、矩形；
   - 对 `#32770` 对话框额外枚举子控件文本（最多 30 个、每个 500 字符），拼成「对话框正文」。
8. `---------- QQ 本地存储（壁纸设置所在）----------`
   - `%APPDATA%\QQ\Local Storage\leveldb` 的文件数/总大小/最新 mtime（**只列元数据，不复制内容**）；
   - `%APPDATA%\QQ\Preferences`、`Local State` 大小/mtime；
   - 提示：要壁纸细节请用插件设置页导出。
9. `---------- 最近启动时间线 ----------`（新增，便于对齐「每次启动」）
   - 合并：最新 `installer.log` 行、`liteLoader.log` 最新行、`plugin.log`/`session.log` 尾部、
     `QQ\log\app_launcher-*.log` 最新一段、`versions` 目录 mtime、`QQ.exe` mtime，按时间倒序最多 40 行。

`errors-digest.txt` 规则：

- 扫描本次已收集的文本日志（`QQ-log`、`Tencent-Logs`、`LiteLoader`、`LiteLoader-data`、`QQ-arks`）；
- 模式（大小写不敏感）：`error`、`exception`、`failed`、`fail `、`无法`、`失败`、`错误`、`异常`、
  `cannot find`、`ENOENT`、`EACCES`、`EPERM`、`已损坏`、`文件校验`、`版本不支持`、`not supported`、
  `LiteLoader`、`找不到`、`拒绝访问`、`0x800`、`crash`；
- 每个文件最多贡献 200 条、单行保存前 400 字符、最多 600 条输出；
- 输出按「文件 → 命中次数 → 首/末时间（能从行里解析 `yyyy-MM-dd HH:mm:ss` 或 `[yyyy/M/d H:mm:ss` 就写，
  解析不到写文件 mtime）→ 最多 3 条样例行」组织；
- 文件头部写清：这是**自动筛选**，不保证是根因；紧接着写「自动体检」里最相关的 3 条提示。

其它要求：

- `README.txt` 写明：包含什么、不包含什么（聊天记录库内容/表情缓存/插件源码全文/截图）、怎么提交、如何用插件设置页导出更细的包。
- 隐私口径（必须与实现一致）：
  - `errors-digest.txt` 的样例行、`LiteLoader/installer.log`、`LiteLoader/liteLoader.log`、
    `LiteLoader-data/**` 下的文本日志：落盘前脱敏（`uin=`/`uid=`/`u_xxx`/邮箱/长 base64/dataURL/`sk-`/`Bearer`）。
  - `QQ-log/`、`QQ-arks/`、`Tencent-Logs/`、`QQ-crashpad/` 的 QQ 原始日志：**按原样收集**（脱敏会破坏排查），
    README/契约/报告必须明确「可能含本机路径/账号 ID/token 片段，公开提交前请自行确认」，
    自动体检给一条 `[ ]` 提示。
  - 无法按 UTF-8 解码或二进制文件跳过脱敏，并在报告里注明「未脱敏（非文本）」。
- 体积截断：`EnforceSizeCap` 必须在 `FinishChecks` 之前执行（或 FinishChecks 读取 cap 结果），
  这样自动体检才能给出 `[!] 诊断包超过上限，已按「日志→插件包→其它」截断（实际上限 X MB）`；
  文案里的上限用实际值（`LIULI_DIAG_CAP_MB` 可覆盖），不得硬编码 30MB。
- 采集体积上限约 30MB；超了按「日志→插件包→其它」顺序截断并在报告里注明。
- 全部路径处理支持中文/空格/长路径；日志文件用 `FileShare.ReadWrite|Delete` 读。
- 保持 C# 5 语法（csc v4.0.30319 编译），不得引入 NuGet/新引用。
- 版本号 `1.0.9.0`；`/diag` 无界面模式必须同样产出新内容（已有 `BuildDiag()` 路径共用）。

## 三、插件诊断包（琉璃 `liuli-dsh-theme`）

### 3.0 目录与约定

- 主进程新增 `main/diag.js`；`main/index.js` 里注册 IPC 并启动 session 日志。
- 渲染进程新增 `renderer/diag.js`；`renderer/index.js` 顶部安装错误捕获、暴露快照函数、
  设置页加「诊断」小节。
- 插件的诊断日志目录：`<LiteLoader.path.data>/liuli-dsh-theme/`（即 `data/liuli-dsh-theme/`）
  - `session.log`：启动/错误流水，**有界**（单文件 ≤256KB，超出轮转为 `session.1.log`，最多 2 个历史）
  - `diag/`：导出的 zip，最多保留 3 个，文件名 `琉璃诊断包-<yyyyMMdd-HHmmss>.zip`
- 导出时**主输出**：桌面（`app.getPath("desktop")`），失败回退 `documents` → `temp` → `diag/`；
  成功后**再复制一份到 `diag/`**（供安装器自动带上）。
- zip 内路径只用 ASCII，内容可以是中文：

```
report.txt                人读总报告（结论清单 + 各采集项）
meta.json                 机器可读（schema=1、时间、版本、路径、采集项、检查项、错误列表）
environment.json          主进程侧环境/进程/版本
snapshots/window-<id>.json  每个渲染窗口的快照（渲染进程返回的 JSON）
screenshots/window-<id>.png 可选（用户勾选「包含截图」时才采；可能含聊天画面，报告里要警告）
wallpaper/preview.jpg     可选（与截图同一个勾选项；壁纸缩略图，宽 ≤480）
logs/session.log          本插件启动/错误流水
logs/session.1.log        可选
logs/plugin.log           原有日志
logs/installer.log        `LiteLoaderQQNT/installer.log` 尾部（GUI/headless 都写，带时间戳）
logs/liteLoader.log       LiteLoader 本体主进程 tee 日志（banner / 插件 loaded / 失败栈 / [dialog] 弹窗正文）
logs/qq/<name>.log        `%APPDATA%/QQ/log` 最新 8 个文件的尾部（每个 ≤120KB，二进制跳过）
logs/liteloader-data/<slug>.log  其它插件 data 下的小日志尾部（每个 ≤64KB，最多 10 个）
report.txt 顶部必须写：schema 版本、生成时间、插件版本、LiteLoader 版本、QQ 版本、采集范围与隐私声明。
```

### 3.1 IPC 契约（preload ⇄ main）

`preload/index.js` 的 `liuli_api` 新增（保持现有 `selfUid`/`openExternal` 不变）：

```js
diagExport: payload => ipcRenderer.invoke("LiteLoader.liuli.diagExport", payload) // => { ok, path, copiedTo, error, size }
diagLog: entry => ipcRenderer.send("LiteLoader.liuli.diagLog", entry)            // 单向；主进程写 session.log
diagOpenPath: path => ipcRenderer.invoke("LiteLoader.liuli.diagOpenPath", path)  // => bool
```

`diagLog` 条目：`{ t: ISO 字符串, level: "error"|"warn"|"info", scope, msg, stack?, extra? }`，
单条序列化后 ≤4KB，主进程再包一层截断（≤8KB）。

`payload`（渲染进程 → 主进程）：

```js
{
  schema: 1,
  generatedAt: ISO,
  opts: { screenshots: boolean, wallpaperPreview: boolean },
  self: { href, hash, scope, ua, viewport, dpr, visibility, origin },
  errorCount: number,
  windowsKnown: number,     // 渲染进程可见的窗口数（只作参考）
  snapshot: { …见 3.2… }    // 当前窗口的完整快照
}
```

主进程不信任 payload：所有字段逐项校验/截断；异常也照常打包并在 report 里记录。

### 3.2 渲染进程快照字段（`window.__liuliDiagSnapshot()` 同步版，`collectDiagnostics()` 异步增强版）

必须有：

- `schema`, `generatedAt`
- `window`: `{ href, origin, hash, scope dataset, visibility, ua, language, online, viewport:{w,h,dpr,zoom}, screen:{w,h,availW,availH}, colorScheme, reducedMotion }`
- `boot`: `{ startedAt, readyAt, readyMs, failed:boolean, failedMsg }`（由 index.js 记录）
- `liteLoader`: `{ present, versions, path, plugins: [{slug,name,version,disabled,incompatible,path}] }`
- `theme`:
  ```js
  {
    dataset,                       // body.dataset 快照
    failed: true|false,            // data-liuli-theme === "failed"
    failedReason,                  // 启动注入 catch 到的消息（index.js 记录）
    bootOk: true|false,
    styleTags: [{id,len}],
    themeStyle: { present, len, ruleCount, ruleReadError, sheetReadable },
    wallpaperStyle: { present, len, hasUrl },
    files: [{name, fetchOk, status, bytes, chars, optional, inStyle, markerOk}],   // 异步版逐文件检查
    // 页面 <style> 字符数 vs 磁盘 CSS 字符数之和：以 checks 里的 theme-stale 结论呈现（不单独建字段）
    cssVars: { "--ll-brand": "...", "--ll-acrylic": "...", "--ll-material-blur": "...", missing: [...] },
    panel: { embeddedFound, childCount, styledControls: [{selector, found, borderRadius, bg, fontFamily}],
             panelBg, panelColor },
    checks: [...]
  }
  ```
  - `chars` 必须是**字符数**（`text.length`），不是 UTF-8 字节数：CSS 注释里有中文，字节数会明显大于
    `<style>.textContent.length`，用字节数对比会误报「样式被截断」。
  - 完整性校验：`expectedChars = Σ files[].chars + (成功文件数 - 1)`，与 `themeStyle.len` 比较，
    差超过 max(2000, 2%) 时给 `warn`：
    「样式内容与磁盘 CSS 总长度不一致（多半是插件更新后没重启 QQ）」（check id = `theme-stale`）。
    marker 判定抓不到截断，这条能。
  - `inStyle` 判定：fetch 到的文件内容取「第一段非注释的 40 字符」作为 marker，检查
    `#liuli-dsh-theme.textContent` 是否包含它；`markerOk` 为更宽松的二次判定（去空白后包含）。
  - `optional`：`assets/wallpaper.css` 是安装器版故意不带的本地默认壁纸。**主进程派生「主题样式未生效」
    结论时必须跳过 optional 条目**，否则每个用户都会看到 `CSS 资源加载失败：wallpaper.css` 的假警报
    （真实快照实测过，必须防回归）。
  - `ruleCount`：`document.getElementById("liuli-dsh-theme").sheet.cssRules.length`，抛异常时记
    `ruleReadError`（有些环境拿不到 cssRules，这本身就是证据）。
- `wallpaper`: 
  ```js
  {
    key: "liuli-dsh-wallpaper", present, rawLen, parseOk, parseError,
    format: "json"|"legacy-string"|"none", srcLen, mime, dataUrlPrefix,
    fit, area, areaValid, imageRatioCached, decodeOk, naturalWidth, naturalHeight,
    approxBytes, lastAppliedAt, lastApplyScope,
    applied: { pseudoBgContainsUrl, pseudoBgSize, pseudoBgPosition, pseudoContent, pseudoZIndex,
               appFound, appSize, appPosition, appBgAlpha },
    defaultWallpaper: { wallpaperCssPresent, wallpaperJpgPresent }  // 异步版 fetch 判定
  }
  ```
- `storage`: `{ totalChars, keyCount, keys: [{k, len, jsonOk, preview}], quotaProbe: [{bytes,ok,err}], revKey }`
  - 预览规则：壁纸键只给 `{k,len,dataUrl:true,mime}`；**只有 `liuli-*` 自己的键给值预览**（截断 120 字符），
    QQ 内核/其它插件的键只给 `{k,len,jsonOk,previewRedacted:true}`（值里可能带联系人/群 ID、草稿、登录态片段）；
    疑似敏感键（含 `token`/`pass`/`cookie`/`session`/`uid`/`device`/`guid`）只给 `{k,len}`。
  - `quotaProbe` 只在异步版做：依次试 0.5/1/2/4/8/12MB，写临时键→立即删除；记录结果。
- `events`: `{ storage: [{key, at}], lastWallpaperRev, wallpaperOps: [...] }`
  - `wallpaperOps`：最近 20 条换壁纸审计（见 3.3），来自 localStorage `liuli-dsh-wallpaper-audit`（≤4KB）。
- `errors`: `[{ t, level, msg, stack, scope }]` 最近 60 条（环形缓冲，单条 ≤2KB）
- `settings`: `window.__liuliSettingsCache`（截断）
- `dom`: `{ readyState, elementCount, appBeforeVisibleHint, overlays: [{selector, bgAlpha, zIndex} ×3] }`
  - `appBeforeVisibleHint`：`#app` 存在 + `::before` 有背景图 + `#app` 有尺寸 + 计算出的 `::before` 可见。
- `checks`: `[{ id, level: "ok"|"warn"|"bad", text }]`（直接给结论，中文，例如
  `壁纸已写入 localStorage（783KB）` / `主窗口未收到壁纸更新（storage 事件 0 次）` /
  `主题样式未注入` / `主题样式注入不完整：assets/lists.css 缺失` / `面板控件未套用样式（.liuli-btn 计算样式为默认值）`）

### 3.3 壁纸操作审计（关键）

在以下位置记录结构化事件（内存环形 + `localStorage["liuli-dsh-wallpaper-audit"]` 最多 20 条 + `diagLog` 落 session.log）：

1. 选图：`{action:"pick", file:{name,size,type}, maxW}`（只记文件名，不记路径）
2. 压缩/编码：`{action:"encode", ok, dataUrlLen, width, height, quality, ms, error}`
3. 写 localStorage：`{action:"persist", ok, error, key, valueLen}`
4. 应用：`{action:"apply", scope, styleLen, ratioKnown, fit, area}`
5. 其它窗口收到 storage：`{action:"storage", scope, key, hasSrc}`
6. 移除：`{action:"remove"}`
7. revision 轮询命中：`{action:"poll", fromRev, toRev, scope}`

### 3.4 主进程采集项（`main/diag.js`）

- 环境：`process.versions`、`app.getVersion()`、`app.getPath("appData"/"desktop"/"documents"/"temp"/"userData")`、
  `os.*`（platform/release/arch/cpus/mem/freemem/uptime/hostname）、时区、`process.execPath`、
  是否管理员（Windows 下 `process.getuid` 不可用，能拿到就写）、`LiteLoader.versions/path/os`。
- 插件与配置：
  - 所有插件：目录名、manifest（原文截断 2KB + JSON 解析结果 + BOM 判定）、`LiteLoader.plugins` 的
    disabled/incompatible、注入文件存在性/大小/mtime/SHA256（只对 liuli 自己算全文哈希，其它只算大小）、
    `data/<slug>/config.json`（≤4KB）、data 目录文件数/总大小。
  - `data/LiteLoader/config.json`（存在才写）。
- 安装器状态：`installer.log` 尾部 200 行；`installer-backup/original` 文件清单/哈希（只列元数据）；
  `LiteLoaderQQNT/package.json` 版本。
- QQ：`%APPDATA%/QQ/log` 最新 8 个文件尾部；`QQ/Crashpad/reports` 清单；`QQ/Local Storage/leveldb`
  元数据（只元数据）。
- 日志错误聚合：主进程也生成 `report.txt` 里的「日志错误摘要」小节（同安装器规则，但只扫它收集到的日志）。
- 窗口与截图：
  - `webContents.getAllWebContents()` 全量：id/type/url/title/destroyed/visible/`isLoading`/
    `getOSProcessId()`/`session` 是否与主窗口同一个（`===` 比较）/`getZoomFactor()`/`getUserAgent()`；
  - 对每个 URL 以 `app://` 开头且非 `screenshot.html`/`hiddenWindow.html`/`about:blank` 的窗口，
    **优先异步采集**：
    `typeof __liuliDiagCollect === "function" ? __liuliDiagCollect({files:true,quota:false,wallpaperDecode:false,defaultWallpaper:false}).then(JSON.stringify) : 同步快照`，
    限时 6s；失败/超时回退 `__liuliDiagSnapshot()` 同步版。快照文件里写 `mode: "async"|"sync"|"error"`。
    异步版才有 `theme.files`（逐文件 fetch/inStyle/optional/chars），report 的窗口级主题结论依赖它；
    同步回退时 `files=null`，按降级文案处理。结果存 `snapshots/window-<id>.json`（失败也存错误说明）；
  - 截图策略：**设置页窗口（URL 含 `#/setting`）默认采集**（不涉及聊天内容，正是「UI 渲染异常」最需要的证据）；
    主窗口/其它窗口只在 `payload.opts.screenshots === true` 时采集。`capturePage()` → PNG，单张 ≤4MB、
    总 ≤12MB，只截上面过滤后的窗口；截图失败写报告。截图文件名 `screenshots/window-<id>.png`，
    同目录写 `screenshots/index.json` 记录 id↔url↔caption。
- 输出与容错：zip 写入用纯 JS（`zlib.deflateRawSync` + 手写 ZIP：local header/central directory/CRC32/
  UTF-8 标志），不引入依赖；写完后 `shell.showItemInFolder(path)`；失败返回 `{ok:false,error}` 且
  session.log 记录；桌面写入失败自动回退，报告里写最终路径。
- 隐私：所有文本经 `sanitizeText()`（脱敏 `uin=`/`uid=`/`u_xxx`/邮箱/长 base64/`sk-`/`Bearer`，
  单行 ≤1000 字符）；不采集 `nt_msg.db`、表情图片、聊天内容；截图默认关闭。

### 3.5 设置页 UI（`renderer/index.js` 的 `buildPanelContent`）

在「恢复默认」之前加一个「诊断」小节（沿用现有 `liuli-*` 样式，必要时补 `settings-page.css`）：

```
诊断
[说明文字：把环境、插件状态、日志、UI/壁纸状态打成 zip（不含聊天记录；设置页截图默认包含）]
[ ] 包含聊天窗口截图（可能包含当前聊天画面）
[导出诊断包]  状态：就绪 / 正在收集… / 已导出到 C:\...  [打开文件夹]
最近错误：N 条（附「查看」展开环形错误列表）
```

- 导出期间按钮禁用、显示进度文案；成功 toast + 路径；失败显示错误原因并写 session.log。
- 设置窗口导出时，主进程要同时抓主窗口快照（不依赖用户在主窗口点）。
- 任何异常都要落到 UI 和 `session.log`，不能静默。

## 四、壁纸健壮性修复（与诊断同批，最小改动）

1. `saveWallpaperState` 不再吞异常：返回 `{ok, error}`；失败时 toast 明确提示（含异常名/消息），
   并写审计事件。
2. 选图压缩自适应：原图（≤2560 宽）优先，超预算再按 2560 → 1920 → 1440 → 1080 宽、质量 0.86→0.8
   逐档压，目标 dataURL ≤3.5MB；仍超限则在 toast 里说明，并允许保存（localStorage 写入失败再提示）。
3. 跨窗口同步兜底：新增 `liuli-dsh-wallpaper-rev`（毫秒字符串）随保存写入；主窗口每 1500ms 读一次
   这个小键，变化就重新读取壁纸状态并渲染（不重复解析大图：先比较 rev）。storage 事件路径保留。
4. 主窗口启动时若 `liuliSource` 没有变化但 `#app::before` 无背景，尝试重新 `renderWallpaper`。
5. 以上全部写审计事件，可在诊断包里看到。

## 五、验收标准

- 安装器：`build.ps1` 能在本机构建；`QQNT-LiteLoader-Installer.exe /diag` 产出 zip；用
  `Expand-Archive` 能解开；`environment.txt` 含新增全部小节；`errors-digest.txt`/`README.txt` 存在；
  不包含 `nt_msg.db`/表情/聊天内容；桌面不可用时回退有效（可手工改路径验证）。
- 安装器·文件对比口径：文本文件（.css/.js/.json/.md/.html/.svg/.txt 等）必须按「去掉 `\r` 后」的长度与
  SHA256 比较，纯换行符差异记为「一致（仅换行符不同）」，不得计入 `<<==` 问题项；二进制文件按原始字节。
- 安装器·补丁识别：`winmm.dll` 允许「大小相同、哈希不同（不同构建时间戳）」的中性描述，不判严重；
  build.ps1 默认只在 `winmm.c` 变化时重编 winmm，另有 `-RebuildWinmm` 强制开关。
- 安装器·启动报错取证：`liteLoader.log` 必须记录每次启动的 banner、每个插件的 loaded/失败（含 stack）、
  `store.js` 弹窗的 `[dialog] 标题 + 正文`；`uncaughtExceptionMonitor` 有记录但不改变默认崩溃行为；
  GUI 与 `/diag` 都要写带时间戳的 `installer.log` 并有 1MB 轮转。
- 插件主进程：用 mock electron/LiteLoader 的 Node 测试能跑通 `exportDiag()`，产出 zip 且
  `Expand-Archive` 可解、条目与 report 一致；无 electron 环境下模块 require 不崩（延迟取依赖）。
- 插件渲染进程：在真实 QQ 里用 ll-cdp 执行 `await import("local://profile/plugins/liuli-dsh-theme/renderer/diag.js")`
  拿到的快照包含上述关键字段，且能正确反映本机现状（壁纸已应用、`#app::before` 有背景图）；
  模拟一份 `fetchOk=false` / `inStyle=false` 的 files 数组能报出具体文件名；
  页面 `<style>` 与磁盘 CSS 字符数一致时不误报 `theme-stale`。
- 隐私：`errors-digest.txt` 样例行与 LiteLoader 侧文本日志（`installer.log`/`liteLoader.log`/`LiteLoader-data/**`）里的
  `uin=`/`uid=`/`u_xxx`/邮箱/token/长 base64 已脱敏；**QQ 原始日志（`QQ-log/` 等）按原样收集、未脱敏**，
  报告与 README 已明确提示「公开提交前请确认」。
- 文档：`installer/README.md` 与插件 `README.md` 写清两个导出入口、包内结构、隐私范围。
- 版本：安装器 `1.0.9.0`，插件 `1.3.0`。
