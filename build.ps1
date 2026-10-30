# 打包：把补丁文件、LiteLoader、插件塞进一个 exe。
# 输出：dist\QQNT-LiteLoader-Installer.exe
param(
    [string]$QQExe = 'F:\download\Telegram Desktop\QQ.exe',
    [string]$Dbghelp = 'F:\download\Telegram Desktop\dbghelp_x64.dll',
    [string]$PluginRoot = 'D:\Agent project\qq-liteloader\liuli-plugin-work'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$payload = Join-Path $here 'payload'
$dist = Join-Path $here 'dist'
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $payload, $dist -Force | Out-Null

Copy-Item -LiteralPath $QQExe (Join-Path $payload 'QQ.exe')
Copy-Item -LiteralPath $Dbghelp (Join-Path $payload 'dbghelp.dll')
# LiteLoader 本体：用 loader\ 里修过的 1.4.1（官方版 store.js 用 dirent.path，新版 QQ 的 Node 里是 undefined）
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $here 'loader'), (Join-Path $payload 'LiteLoaderQQNT.zip'))

# 插件：只取 git 里跟踪的文件（不带壁纸等本地文件）
$stage = Join-Path $payload 'plugins'
foreach ($p in 'liuli-dsh-theme', 'qq-enhance') {
    $src = Join-Path $PluginRoot $p
    $tar = Join-Path $payload "$p.zip"
    git -C $src archive --format=zip --prefix="$p/" -o $tar HEAD
    if ($LASTEXITCODE) { throw "git archive 失败: $p" }
    Expand-Archive -LiteralPath $tar -DestinationPath $stage -Force
    Remove-Item $tar
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, (Join-Path $payload 'plugins.zip'))

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $dist 'QQNT-LiteLoader-Installer.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    "/out:$out" "/win32manifest:$here\src\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/resource:$payload\QQ.exe,QQ.exe" `
    "/resource:$payload\dbghelp.dll,dbghelp.dll" `
    "/resource:$payload\LiteLoaderQQNT.zip,LiteLoaderQQNT.zip" `
    "/resource:$payload\plugins.zip,plugins.zip" `
    "$here\src\Installer.cs"
if ($LASTEXITCODE) { throw 'csc 编译失败' }
Get-Item $out | Select-Object FullName, Length
