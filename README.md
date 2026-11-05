# QQNT LiteLoader 一键安装器

一个 exe，双击后点「一键安装」：

1. 关闭 QQ
2. 备份原版 `package.json`（每个 `versions\*\resources\app` 一份，只在第一次安装时备份）
3. 在 QQ 根目录放 `winmm.dll`（代理，负责把 hook 带进 QQEX.exe 等子进程）、
   `dbghelp.dll`（运行时 hook）和 `hook.ini`（`mode = active` / `match = skipfullreads:1`）
4. 把 LiteLoaderQQNT 1.4.1 装到 `%APPDATA%\LiteLoaderQQNT`
5. 可选：装上「琉璃」插件
6. 每个 `versions\*\resources\app` 里加两个文件：`liuli_loader.js`（引导器）和
   `package.patched.json`（从原版 `package.json` 生成，只把 `main` 换成 `./liuli_loader.js`）
7. 重新打开 QQ

**一个被腾讯签名的文件都不改**（`application.asar` / `wrapper.node` / `major.node` / `package.json`），
**也不替换 `QQ.exe`**：hook 只在 QQ 读 `package.json` 的 `main` 那一次把内容换成 `package.patched.json`，
前面那次完整性校验放行原版。签名清单照常通过，所以主窗口、频道、腾讯文档（exApp）都不会再弹「文件已损坏」。

「还原」按钮把安装器加的这些文件删掉（`winmm.dll` / `dbghelp.dll` / `hook.ini` /
`liuli_loader.js` / `package.patched.json`）。以前用旧安装器装过的（`QQ.exe` 被降级、
`package.json` 的 `main` 被改成 `./app_launcher/LiteLoader.js`、多出 `app_launcher` 目录），
安装和还原时都会自动用备份改回原版。插件和数据留着不删。

## 已验证版本与首次启动

| QQ 版本 | 状态 | 说明 |
|---|---|---|
| 9.9.35-52892 | ✅ 已验证可用 | 实测见 [qqnt-liuli-theme#38](https://github.com/LilycleHeart/qqnt-liuli-theme/issues/38) |
| 9.9.36-53644 | ✅ 已验证可用 | 同机完整重启后，LiteLoader 与琉璃均成功加载；该版本的 `isPureShell=true` 不影响现有方案生效 |
| 其它版本 | 未验证 | 安装后会验证注入是否真正生效 |

`isPureShell` 仅作为诊断信息收集，不能单独用来判断兼容性，也不会阻止安装。
首次安装后如果设置里没有「琉璃」，请先**完全退出 QQ 再重新打开**。同机日志显示，首次启动与重启后的
`package.json` 读取顺序可能不同，`skipfullreads:1` 有机会在首次启动时放错入口读取；不能仅凭 hook 日志
有 `rewrite` 就认定 LiteLoader 已加载。不要直接修改磁盘上的 `package.json`，否则会触发 QQ 完整性校验。

安装完成并拉起 QQ 后，安装器每 2 秒检查一次、最多等待 30 秒。只有本轮启动新建或更新了
`%APPDATA%\LiteLoaderQQNT\liteLoader.log`，或琉璃插件的 `data\liuli-dsh-theme\plugin.log`，才判定注入生效。
超时会在 GUI 明确警告并建议先完全重启 QQ；无界面 `/install` 返回退出码 2。
若重启后仍未生效，可运行「还原」，或导出诊断包反馈。`liuli-hook.log` 只证明 hook 正在活动，
**不作为注入成功信号**。

## 导出诊断包（用户来报问题时）

右上角「导出诊断包」→ 桌面生成 `QQ诊断包-<时间>.zip`。

桌面的写入位置按下面顺序挑，**第一个能写的就用**，最终路径会写在 UI 日志、`installer.log` 和包内
`environment.txt` 的「环境」小节里：

1. 桌面（Known Folder，跟随 OneDrive 重定向）
2. `%USERPROFILE%\Desktop`
3. `%USERPROFILE%\Documents`
4. `%TEMP%`
5. `%APPDATA%\LiteLoaderQQNT\diag`

生成完会问一句要不要打开 GitHub 提 issue：选「是」直接跳到新建 issue 页面，
标题和正文模板（含安装器版本、QQ 版本、系统、文档目录）都预填好了，
用户只要写一句现象、把 zip 拖进输入框，GitHub 会自动上传附件。
选「否」就打开文件所在位置，自己留着看。

无界面也可以：`QQNT-LiteLoader-Installer.exe /diag`（会打印输出目录和 issue 链接）

### 包里有什么

```
environment.txt        人读总报告（顶部是「自动体检」结论清单，下面每节都有原始证据）
errors-digest.txt      日志错误聚合：按关键字筛出可疑行，按文件给命中次数/首末时间/样例行
README.txt             包内说明 + 隐私声明 + 怎么提交
QQ-log/                %APPDATA%\QQ\log 全部文本日志
QQ-arks/               %APPDATA%\QQ\arks 近 7 天
QQ-crashpad/           QQ 崩溃报告里的文本
Tencent-Logs/          %APPDATA%\Tencent\Logs 里和 QQ 有关的日志
LiteLoader/            %APPDATA%\LiteLoaderQQNT 顶层 .log/.txt（installer.log 等）
LiteLoader-data/       各插件 data 目录下的小日志（.log/.txt，≤2MB/个，30 天内）
plugin-diag/<name>.zip 插件设置页导出的最新诊断包（可选；内容由插件导出策略决定，
                       默认含设置页截图，勾选后可能含聊天截图；安装器自身不截图）
```

`environment.txt` 的小节：

- **自动体检（先看这里）**：逐条 `[OK]` / `[!]` 结论，命中问题的行尾带 `<<==`；
  并写明三类症状的因果与对照表（S1 启动弹报错 / S2 壁纸不显示 / S3 主题不生效）
- **环境**：系统/用户/管理员/路径/输出目录回退说明
- **已安装插件（版本 / 是否被禁用 / 文件是否完整）**：manifest 原文、版本 vs 安装器内置版本、
  `injects` 文件存在性、逐文件完整性（缺失 / 大小不同 / 内容与内置不一致 / 未在内置清单里，
  另列「内容一致（仅换行符不同）N 个」），并单独列 `assets/*.css` 对照——缺样式文件就是
  「设置页像没套样式、主题不生效」。文本类扩展名（`.css/.js/.json/.md/.svg/.map` 等）统一
  **去掉 `\r` 后**算 SHA256/长度再比，CRLF 与 LF 的历史差异不会误报；二进制仍按原始字节比。
- **LiteLoader 配置与插件数据**：`data/LiteLoader/config.json` 的启用/禁用/删除/安装中列表，
  每个插件 data 的 config（脱敏 ≤4KB）、文件数/总大小、日志尾部 80 行、`diag/` 导出包清单
- **LiteLoader 启动日志**：`%APPDATA%\LiteLoaderQQNT\liteLoader.log`（安装器内置本体里的
  `src/main/logger.js` 写的 tee 日志）会被收进包的 `LiteLoader/` 目录；自动体检会检查
  `[Main] … 加载失败`、`[dialog]`（LiteLoader 自己弹的报错正文）、`[fatal]`（主进程未捕获异常），
  命中就提示「看 LiteLoader/liteLoader.log 的 [Main]/[dialog] 行」，时间线也会并进去
- **QQ 应用打补丁状态**：按新方案的落点逐条查——QQ 根目录的 `winmm.dll` / `dbghelp.dll` /
  `hook.ini`（完整内容、是否 `mode=active` 且含 `skipfullreads`）、`liuli-hook.log` 尾部 80 行、
  `QQ.exe` 的腾讯数字签名（有没有被降级版换掉）、每个 `versions/*/resources/app` 的 `isPureShell`、
  `liuli_loader.js`、`package.patched.json`（`main` 是否 `./liuli_loader.js`），以及
  **原版 `package.json` 是否仍是原版**（`main` 还是 `./application.asar/app_launcher/index.js`；
  被旧安装器改成 `./app_launcher/LiteLoader.js` 会被单独标出来）、有没有旧方案遗留的 `app_launcher` 目录；
  还包含 `versions` 目录列表与配置、运行中 QQ 是否就是安装目录这份、QQEX.exe 辅助进程存在性。
  补丁文件「内置补丁」判断保留精确哈希，但**大小相同、哈希不同**时只作为中性提示
  （可能是较早的安装器构建，例如 winmm 的 PE 时间戳每次构建都不同；重跑安装器可对齐，不影响功能）
- **LiteLoader 本体**：版本、`store.js` 是否还有 `dirent.path`、是否含 `LiteLoader.path.plugins`、
  `runtime.js` 是否有 `unregisterPlugin`、关键文件大小/mtime
- **显示器 / 显卡 / 会话**：显示器数量/分辨率/工作区/主屏、DPI、远程会话、WMI 显卡与驱动、内存
- **QQ 窗口与对话框（启动报错取证）**：QQ.exe/QQEX.exe 的所有顶层窗口（PID/类/标题/可见/矩形），
  `#32770` 对话框额外枚举子控件文本（最多 30 个、每个 500 字符），拼成「对话框正文」
- **QQ 本地存储**：`%APPDATA%\QQ\Local Storage\leveldb`、`Preferences`、`Local State` 的**元数据**
  （文件数/大小/时间，不读内容）
- **最近启动时间线**：installer.log、插件 session/plugin 日志、QQ app_launcher 日志、
  `versions` 与 `QQ.exe` 修改时间合并，按时间倒序（便于对齐「每次启动都弹」）
- 原有小节：QQ 安装/进程（双主进程、管理员令牌）、聊天记录目录（盘类型/ACL/占用者）、磁盘、
  Defender 受控文件夹访问与拦截事件、Application 错误事件、安装器日志

体积上限约 30MB；超限时按「日志 → 插件包 → 其它」顺序截断，并在报告末尾注明截断了什么。

### 安装器自己的日志 `installer.log`

`%APPDATA%\LiteLoaderQQNT\installer.log` 现在 **GUI 和无界面都会写**，每行带时间戳与来源标记：

```
2026-11-01 07:51:18 [GUI] 安装器界面已启动（GUI 日志写入 installer.log，带时间戳）
2026-11-01 07:51:30 [GUI] 开始收集诊断包（只读，不含聊天记录）
2026-11-01 07:51:41 [HEADLESS] 诊断包: C:\...\QQ诊断包-....zip
```

- GUI：每次「Log」既显示在窗口里，也 best-effort 追加写文件；写失败不影响界面。
- 无界面（`/install`、`/restore`、`/diag`）：同样带 `[HEADLESS]` 前缀；`/diag` 用追加模式，不清空历史。
- 单文件超过 1MB 自动轮转为 `installer.1.log`（只留 1 个历史）。
- 诊断包「安装器日志（尾部 60 行）」小节能直接看到这些行，`installer.log` 本身也会被收进包的
  `LiteLoader/` 目录。

### 隐私边界与脱敏

**已脱敏（落盘前处理）**

- `LiteLoader/installer.log`（安装器自己写）、`LiteLoader/liteLoader.log`（内置本体的 tee 日志）、
  `LiteLoader-data/**` 下的 `.log/.txt` 文本日志，以及 `errors-digest.txt` 的样例行。
- 规则：`uin=数字`、`uid=…`、`u_xxx`、邮箱、`sk-`、`Bearer`、长 base64/`data:image` → `***`。
- 无法按 UTF-8 解码或带二进制特征的日志跳过脱敏，报告里会注明「未脱敏（非文本）」。

**按原样收集（未脱敏）**

- `QQ-log/`、`QQ-arks/`、`Tencent-Logs/`、`QQ-crashpad/` 的 QQ 原始日志。脱敏会破坏排查，
  所以保留原始字节；它们可能含**本机路径、账号 ID、token 片段**，**公开提交前请自行确认**。
  自动体检里有一条 `[ ]` 提示说明这一点。

**不收集**

- `nt_msg.db` 等聊天记录库内容、表情/图片/语音缓存、插件源码全文（只给哈希/大小/时间）、
  QQ Local Storage 的实际内容（只列元数据）。安装器自身不截图。

**`plugin-diag/<name>.zip` 是插件自己导出的包，安装器只是原样带上**

- 内容由插件导出策略决定：默认包含**设置页截图**；如果导出时勾选了「包含聊天窗口截图」，
  里面还可能有**聊天画面**。提交前请确认是否要包含这些截图。
- `liteLoader.log` 只含 LiteLoader 自身事件（启动 banner、插件 load/loaded/加载失败、
  自己的错误对话框正文、未捕获异常），不含聊天内容。

### 想更细的壁纸/渲染证据

打开 QQ → 设置 → 琉璃 → 最下面的「诊断」小节 → 勾选需要的内容 →「导出诊断包」。
那个包会带每个渲染窗口的快照（localStorage 键、`#app::before` 计算值、壁纸操作审计、
渲染进程错误环形缓冲）；勾选「包含界面截图」时可能含当前聊天画面，提交前请自己确认。
导出成功后，安装器下次生成的诊断包会自动带上 `plugin-diag/` 里的最新一份。

## 排查覆盖矩阵

症状 → 证据链 → 采集方的完整对照见 [`DIAG-COVERAGE.md`](DIAG-COVERAGE.md)：

- S1 每次启动弹报错：QQ 原生对话框、双主进程、QQ 根目录的 `winmm.dll` / `dbghelp.dll` / `hook.ini`
  被覆盖或被杀软删掉、`package.patched.json` / `liuli_loader.js` 缺失、原版 `package.json` 被改坏、本体被换、
  Defender、日志错误聚合
- S2 换壁纸背景不显示：显示器/显卡/远程会话、QQ Local Storage 元数据、插件导出包（壁纸细节）。
  注意 `assets/wallpaper.css` 缺失**属正常**：安装器版故意不发布默认壁纸（插件 `.gitignore` 排除了
  `wallpaper.css/jpg`），用户在设置里选图后才显示壁纸；自动体检里这条是 `[ ]` 说明，不是故障
- S3 界面渲染异常 / 主题不生效：插件逐文件完整性（重点 `assets/*.css`）、版本对比、manifest/injects

## 调试用环境变量

安装器和诊断包都认这几个（不设就是默认行为）：

| 变量 | 作用 |
|---|---|
| `LIULI_QQ_ROOT` | 手工指定 QQ 安装目录（诊断/非常规安装时用） |
| `LITELOADERQQNT_PROFILE` | 自定义 LiteLoader profile（与 LiteLoader 自己认的变量一致） |
| `LIULI_DIAG_WINDOW_PROCS` | 额外枚举指定进程名（分号分隔）的窗口，仅用于自测窗口枚举 |
| `LIULI_DIAG_CAP_MB` | 临时把诊断包体积上限改小（MB），用于验证截断逻辑；默认 30 |

## 打包

```powershell
pwsh -File build.ps1                 # 产出 dist\QQNT-LiteLoader-Installer.exe（requireAdministrator）
pwsh -File build.ps1 -TestCopy       # 另外产出 dist\QQNT-Diag-Test.exe（asInvoker，可在无管理员会话里跑 /diag）
pwsh -File build.ps1 -RebuildWinmm   # 强制重新编译 winmm 代理（默认只在源码更新 / payload 缺失时才编）
```

- payload 文件路径在 `build.ps1` 顶部的参数里：`dbghelp.dll` 来自
  `probe\b2\dbghelp.dll`（**生效版** hook；同目录的 `dbghelp.probe.dll` 只记日志、不换源，
  build.ps1 会比对哈希，误打成探针版会直接报错），`liuli_loader.js` 来自 `probe\b2\liuli_loader.js`。
  **不再打包降级版 QQ.exe**（新方案不替换 QQ.exe）；`payload\` 不再整体删除，源路径不存在时
  复用 `payload\` 里已有的 `dbghelp.dll` / `winmm.dll` / `liuli_loader.js` 并给出提示，两边都没有才报错。
- `hook.ini` 不进 payload：内容是 `Installer.cs` 里内联写的（`mode = active` / `match = skipfullreads:1` / `log =` 指向 `%APPDATA%\LiteLoaderQQNT\liuli-hook.log` / `sp =` 由「防检测增强」选项决定，默认 `off`），
  安装时落到 QQ 根目录（`dbghelp.dll` 优先读自身同目录的 `hook.ini`；`C:\liuli-b2` 只是调试回退，安装器不再往那儿写）。
- **winmm 默认不重编**：`winmm-proxy\winmm.c` / `build.cmd` 不比 `payload\winmm.dll` 新、且 payload 里有时，
  直接复用。MSVC 每次编译的 PE 头 `TimeDateStamp` 都不同，重编会让用户已装好的 winmm 哈希永远对不上，
  被诊断包误报成「不是安装器内置补丁」。确实需要重编时用 `-RebuildWinmm`。
- `LiteLoaderQQNT.zip`（来自 `loader\`）和 `plugins.zip` 每次重新生成。
- 插件 staging 用「工作区里已跟踪 + 未忽略的新文件」
  （`git ls-files --cached --others --exclude-standard`），所以新写但还没 commit 的文件也会打进包；
  `.gitignore` 里的 `assets/wallpaper.css` / `assets/wallpaper.jpg` 仍然排除（那是本地壁纸，不发布）；
  插件仓库外的测试脚本（如 `tools/test-plugin-diag.cjs`）不会被带进包。
- 用 Windows 自带的 .NET Framework 4 编译，用户电脑上不用另装运行库；`payload\` 和 `dist\` 不进 git。

自测工具在 `dist\diag-selftest\`（不参与打包）：`diag-msgbox.exe`（弹一个标题「诊断测试弹窗」的
MessageBox）、`winenum.exe`（独立的窗口/对话框枚举，验证跨进程能抓到标题+正文）、
`pe-manifest.exe`（读 PE 的 `RT_MANIFEST`，验证两个 exe 的 requireAdministrator/asInvoker）。
