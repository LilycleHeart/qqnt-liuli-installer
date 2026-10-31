# QQNT LiteLoader 一键安装器

一个 exe，双击后点「一键安装」：

1. 关闭 QQ
2. 备份原来的 `QQ.exe`、`dbghelp.dll`、`package.json`（只在第一次安装时备份）
3. 放入补丁 `QQ.exe`、`dbghelp.dll`，以及 `winmm.dll`（让 QQ 内置浏览器 QQEX.exe 也加载补丁，否则打开网页会弹"文件已损坏"）
4. 把 LiteLoaderQQNT 1.4.1 装到 `%APPDATA%\LiteLoaderQQNT`
5. 可选：装上「琉璃」插件
6. 改 `versions\*\resources\app\package.json`，让 QQ 启动时加载 LiteLoader
7. 重新打开 QQ

「还原」按钮把备份的文件拷回去。插件和数据留着不删。

## 导出诊断包（用户来报问题时）

右上角「导出诊断包」→ 桌面生成 `QQ诊断包-<时间>.zip`。

生成完会问一句要不要打开 GitHub 提 issue：选「是」直接跳到新建 issue 页面，
标题和正文模板（含安装器版本、QQ 版本、系统、文档目录）都预填好了，
用户只要写一句现象、把桌面上的 zip 拖进输入框，GitHub 会自动上传附件。
选「否」就打开桌面对应位置，自己留着看。

无界面也可以：`QQNT-LiteLoader-Installer.exe /diag`（会顺带打印 issue 链接）

包里是：

- **日志**：`%APPDATA%\QQ\log` 全部、`QQ\arks` 近 7 天、崩溃报告（只取文本）、
  `%APPDATA%\Tencent\Logs` 里和 QQ 有关的、`%APPDATA%\LiteLoaderQQNT` 自己的 `.log`
- **environment.txt**：QQ 版本与补丁文件哈希（比对是不是安装器内置的）、安装器日志末尾、
  QQ 进程列表（是否以管理员身份运行、有没有两个主进程）、
  聊天记录目录所在盘的类型/剩余空间/所有者/ACL/`nt_db` 文件清单、
  谁正占用 `nt_msg.db`（Restart Manager）、磁盘、
  Defender「受控文件夹访问」状态和拦截事件、和 QQ 相关的崩溃事件

**不会**收集：`nt_msg.db` 等聊天记录内容、表情缓存、插件源码、安装备份。

覆盖的场景：聊天记录在移动盘/网盘、磁盘满、目录被 ACL/只读搞坏、被杀软或
「受控文件夹访问」拦、双开抢库、安装器拉起 QQ 时带了管理员令牌（和之后普通方式启动不一致）。

## 打包

```powershell
pwsh -File build.ps1
```

输出 `dist\QQNT-LiteLoader-Installer.exe`。用 Windows 自带的 .NET Framework 4 编译，用户电脑上不用另装运行库。

补丁文件路径在 `build.ps1` 顶部的参数里。`payload\` 和 `dist\` 不进 git。
