# hook · QQNT package.json 重定向 hook（源码）

这个目录存放安装器 `payload\dbghelp.dll` 的**完整源码**，之前它在仓库之外
（`..\probe\b2\`），改过一次却没有版本记录（issue #3 / #39 那次修复就丢了追溯能力）。

## 产物

| 文件 | 说明 |
|---|---|
| `dbghelp.dll` | **生效版**：按 `hook.ini` 的条件改写 `package.json` 读取 |
| `dbghelp.probe.dll` | **探针版**：只记日志、永不改写，用于在真机上采集调用序列 |

两个都从同一个 `hook.c` 编出来，唯一区别是 `/DLIULI_PROBE=1`。**打包时必须用生效版**，
`build.ps1` 会比对两者哈希，误打包探针版会直接报错。

## 编译

```cmd
cd installer\hook
build.cmd
```

需要 Visual Studio 2022 的 `vcvars64.bat`（脚本里写死了 Community 版路径，装的是其它版本请自行改）。
产物落在本目录：`dbghelp.dll` / `dbghelp.probe.dll`。

然后回到仓库根目录打包：

```powershell
pwsh -File build.ps1
```

`build.ps1` 会优先取 `hook\dbghelp.dll`；没有时才回退到旧的 `..\probe\b2\dbghelp.dll`。

## 判定条件

`hook.ini` 的 `match` 决定「哪一次读取要换成 `package.patched.json`」：

| match | 语义 |
|---|---|
| `verifier` | **当前默认**。按原始 `dwDesiredAccess` 识别校验读：校验读（`0x80000000`）恒放行原版，入口读（`0x00120089`）等一律改写。不设次数上限。 |
| `skipfullreads:N` | 旧判据（回滚用）。前 N 次「全文读」放行、第 N+1 次起改写。**它假设「第 1 次全文读一定是校验读」，实测该假设不成立**（见下），保留仅为兼容旧 `hook.ini`。 |
| `auto` | `skipfullreads:1` 的别名。 |

### 为什么不再用 skipfullreads

`skipfullreads:1` 把一个「顺序依赖」当成了「顺序确定」：它认为 QQ 启动时第 1 次读
`package.json` 一定是完整性校验，第 2 次才是入口解析。但四份真实诊断包显示校验读
**不保证**先发生——缺了它，入口读就落到 `fr=1` 被无条件放行，表现为
「安装成功、hook 日志 rewrite 成功、插件从未加载」的静默失效。

`verifier` 改为看「这次读取的调用特征」，不再依赖顺序，因此在四种实测环境下都能稳定注入。

## 残留风险

- `verifier` 依赖「校验读用未展开的 `0x80000000`、入口读用展开后的 `0x00120089`」这一观察。
  目前四份诊断包 100% 可分，但**未来 QQ 版本若改变访问掩码会误判**（入口读被当成校验读放行）。
  更稳的做法是按调用点身份（AOB 定位 `QQNT.dll+0x5314C1x` 校验栈）判定，见 `hook.c` 第 9a 节注释。
- hook 装配在 `DllMain` 起的 worker 线程里，装配完成前发生的读取不可见——任何判定条件都补不了，
  只能靠安装器的装后自检（`liteLoader.log` 是否出现）兜底。

## 文件

| 文件 | 说明 |
|---|---|
| `hook.c` | 主体（MinHook + CreateFileW detour + 条件求值 + 日志） |
| `build.cmd` | MSVC 构建脚本 |
| `hook.ini.sample` | 配置样例与字段说明 |
| `minhook/` | MinHook 依赖（MIT） |
