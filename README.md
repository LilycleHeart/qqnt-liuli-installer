# QQNT LiteLoader 一键安装器

一个 exe，双击后点「一键安装」：

1. 关闭 QQ
2. 备份原来的 `QQ.exe`、`dbghelp.dll`、`package.json`（只在第一次安装时备份）
3. 放入补丁 `QQ.exe` 和 `dbghelp.dll`
4. 把 LiteLoaderQQNT 1.4.1 装到 `%APPDATA%\LiteLoaderQQNT`
5. 可选：装上「琉璃」插件
6. 改 `versions\*\resources\app\package.json`，让 QQ 启动时加载 LiteLoader
7. 重新打开 QQ

「还原」按钮把备份的文件拷回去。插件和数据留着不删。

## 打包

```powershell
pwsh -File build.ps1
```

输出 `dist\QQNT-LiteLoader-Installer.exe`。用 Windows 自带的 .NET Framework 4 编译，用户电脑上不用另装运行库。

补丁文件路径在 `build.ps1` 顶部的参数里。`payload\` 和 `dist\` 不进 git。
