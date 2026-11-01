# 打包：把补丁文件、LiteLoader、插件塞进一个 exe。
# 输出：dist\QQNT-LiteLoader-Installer.exe
# 可选：-TestCopy 额外产出 dist\QQNT-Diag-Test.exe（asInvoker 清单，便于在无管理员权限的会话里验证 /diag）
#
# 设计要点：
#   * payload 不再整体删除：QQ.exe / dbghelp.dll / winmm.dll 的源路径不存在时，复用 payload 里已有的文件并给出明确提示；
#     只有「源不存在、payload 也没有」才报错。
#   * 插件 staging 用「工作区已跟踪 + 未忽略的新文件」（git ls-files --cached --others --exclude-standard），
#     不再依赖 git archive HEAD：新写但还没 commit 的文件也会打进包；.gitignore 里的 wallpaper.* 仍然排除。
#   * winmm 代理默认只在源码比 payload\winmm.dll 新、或 payload 缺失时才重新编译：
#     MSVC 每次编译的 PE 头 TimeDateStamp 都不一样，重编会让用户已经装好的 winmm 哈希对不上，
#     被诊断包误报成「不是安装器内置补丁」。需要强制重编时用 -RebuildWinmm。
param(
    [string]$QQExe = 'F:\download\Telegram Desktop\QQ.exe',
    [string]$Dbghelp = 'F:\download\Telegram Desktop\dbghelp_x64.dll',
    [string]$PluginRoot = 'D:\Agent project\qq-liteloader\liuli-plugin-work',
    [string]$PluginDirName = 'qqnt-liuli-theme',
    [string]$PluginSlug = 'liuli-dsh-theme',
    [switch]$TestCopy,
    [switch]$SkipWinmmBuild,
    [switch]$RebuildWinmm
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$payload = Join-Path $here 'payload'
$dist = Join-Path $here 'dist'
New-Item -ItemType Directory $payload, $dist -Force | Out-Null

function Get-PayloadFile {
    param([string]$SourcePath, [string]$PayloadName, [string]$What)
    $dst = Join-Path $payload $PayloadName
    if ($SourcePath -and (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        Copy-Item -LiteralPath $SourcePath -Destination $dst -Force
        Write-Host ("payload: {0} <- {1}" -f $PayloadName, $SourcePath)
        return
    }
    if (Test-Path -LiteralPath $dst -PathType Leaf) {
        Write-Warning ("{0} 源文件不存在（{1}），复用 payload 里已有的 {2}（{3} 字节）" -f $What, $SourcePath, $PayloadName, (Get-Item -LiteralPath $dst).Length)
        return
    }
    throw ("缺少 {0}：源路径 '{1}' 不存在，payload\{2} 也没有。请把源文件放好，或先准备 payload\{2}。" -f $What, $SourcePath, $PayloadName)
}

# --- 补丁文件 ---
Get-PayloadFile -SourcePath $QQExe -PayloadName 'QQ.exe' -What 'QQ 补丁 QQ.exe'
Get-PayloadFile -SourcePath $Dbghelp -PayloadName 'dbghelp.dll' -What 'dbghelp 补丁 dbghelp.dll'

# --- winmm 代理（让 QQEX.exe 内置浏览器也加载 dbghelp 补丁）---
# 默认不重编：MSVC 的 PE 头 TimeDateStamp 每次都变，重编会让「已装 winmm 哈希 == 安装器内置」永远对不上。
$winmmDst = Join-Path $payload 'winmm.dll'
$winmmBuilt = $false
$winmmBuildCmd = Join-Path $here 'winmm-proxy\build.cmd'
$winmmSrc = Join-Path $here 'winmm-proxy\winmm.c'
$needWinmmBuild = $false
if ($SkipWinmmBuild) {
    $needWinmmBuild = $false
}
elseif ($RebuildWinmm) {
    $needWinmmBuild = $true
}
elseif (-not (Test-Path -LiteralPath $winmmDst -PathType Leaf)) {
    $needWinmmBuild = $true
}
else {
    $dstTime = (Get-Item -LiteralPath $winmmDst).LastWriteTimeUtc
    foreach ($cand in @($winmmSrc, $winmmBuildCmd)) {
        if ((Test-Path -LiteralPath $cand -PathType Leaf) -and
            (Get-Item -LiteralPath $cand).LastWriteTimeUtc -gt $dstTime) {
            $needWinmmBuild = $true
            break
        }
    }
}
if ($needWinmmBuild -and (Test-Path -LiteralPath $winmmBuildCmd -PathType Leaf)) {
    $buildLog = Join-Path $here 'winmm-proxy\build.log'
    Push-Location (Join-Path $here 'winmm-proxy')
    try {
        cmd /c "build.cmd > `"$buildLog`" 2>&1"
        $winmmExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    $builtDll = Join-Path $here 'winmm-proxy\winmm.dll'
    if ($winmmExit -eq 0 -and (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
        Copy-Item -LiteralPath $builtDll -Destination $winmmDst -Force
        $winmmBuilt = $true
        Write-Host 'payload: winmm.dll <- winmm-proxy（本次编译，-RebuildWinmm 或源码有更新）'
    }
    else {
        Write-Warning ("winmm 代理编译失败（exit={0}，见 winmm-proxy\build.log）" -f $winmmExit)
    }
}
if (-not $winmmBuilt) {
    if (Test-Path -LiteralPath $winmmDst -PathType Leaf) {
        if (-not $needWinmmBuild) {
            Write-Host ("payload: winmm.dll 未重新编译（winmm.c / build.cmd 不比它新），复用 payload 里的（{0} 字节）" -f (Get-Item -LiteralPath $winmmDst).Length)
        }
        else {
            Write-Warning ("winmm.dll 复用 payload 里已有的文件（{0} 字节）；需要重新编译的话装好 VS C++ 工具再跑 -RebuildWinmm" -f (Get-Item -LiteralPath $winmmDst).Length)
        }
    }
    else {
        throw '缺少 winmm.dll：winmm-proxy 编译失败，且 payload 里没有现成的 winmm.dll。请装好 VS 2022 C++ 工具，或手工放一份 payload\winmm.dll。'
    }
}

# --- LiteLoader 本体（loader\ 里修过的 1.4.1：官方版 store.js 用 dirent.path，新版 Node 下是 undefined）---
Add-Type -AssemblyName System.IO.Compression.FileSystem
$loaderZip = Join-Path $payload 'LiteLoaderQQNT.zip'
if (Test-Path -LiteralPath $loaderZip) { Remove-Item -LiteralPath $loaderZip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $here 'loader'), $loaderZip)
Write-Host 'payload: LiteLoaderQQNT.zip 已重建'

# --- 插件：工作区里「已跟踪 + 未忽略的新文件」 ---
$stage = Join-Path $payload 'plugins'
$pluginsZip = Join-Path $payload 'plugins.zip'
$src = Join-Path $PluginRoot $PluginDirName
if (-not (Test-Path -LiteralPath (Join-Path $src '.git'))) {
    throw "插件目录不是 git 仓库或不存在: $src"
}
$rawList = & git -C $src ls-files -z --cached --others --exclude-standard
if ($LASTEXITCODE) { throw "git ls-files 失败: $src" }
$pluginFiles = @($rawList -split "`0" | Where-Object { $_ -ne '' })
if ($pluginFiles.Count -eq 0) { throw "插件目录里没有可打包的文件（git ls-files 返回空）: $src" }

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null
$copied = 0
$missingFiles = New-Object System.Collections.Generic.List[string]
foreach ($rel in $pluginFiles) {
    $relWin = $rel -replace '/', '\'
    $from = Join-Path $src $relWin
    if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { $missingFiles.Add($rel); continue }
    $to = Join-Path (Join-Path $stage $PluginSlug) $relWin
    $toDir = Split-Path -Parent $to
    if (-not (Test-Path -LiteralPath $toDir)) { New-Item -ItemType Directory $toDir -Force | Out-Null }
    Copy-Item -LiteralPath $from -Destination $to -Force
    $copied++
}
if ($missingFiles.Count -gt 0) {
    Write-Warning ("有 {0} 个已跟踪文件在工作区里被删除了，未打包：{1}" -f $missingFiles.Count, ($missingFiles -join ', '))
}
if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $stage $PluginSlug) 'manifest.json'))) {
    throw "插件 staging 后没有 $PluginSlug\manifest.json，检查 PluginDirName/PluginSlug 参数"
}
Write-Host ("payload: 插件 staging 完成（{0} 个文件；标题排除 wallpaper.* 等 .gitignore 文件）" -f $copied)
if (Test-Path -LiteralPath $pluginsZip) { Remove-Item -LiteralPath $pluginsZip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $pluginsZip)
Write-Host 'payload: plugins.zip 已重建'

# --- 编译 ---
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc: $csc" }
$srcFile = Join-Path $here 'src\Installer.cs'
$refs = @(
    '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll',
    '/r:System.Management.dll'
)
$resources = @(
    "/resource:$payload\QQ.exe,QQ.exe",
    "/resource:$payload\dbghelp.dll,dbghelp.dll",
    "/resource:$payload\winmm.dll,winmm.dll",
    "/resource:$payload\LiteLoaderQQNT.zip,LiteLoaderQQNT.zip",
    "/resource:$payload\plugins.zip,plugins.zip"
)

$out = Join-Path $dist 'QQNT-LiteLoader-Installer.exe'
$manifest = Join-Path $here 'src\app.manifest'
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
    "/out:$out", "/win32manifest:$manifest") + $refs + $resources + @($srcFile)
& $csc $cscArgs
if ($LASTEXITCODE) { throw 'csc 编译失败' }
Get-Item -LiteralPath $out | Select-Object FullName, Length

# --- 可选：无管理员权限也能跑的测试副本（asInvoker 清单）---
if ($TestCopy) {
    $testManifest = Join-Path $dist 'app.asInvoker.manifest'
    $manifestXml = @'
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="LiteLoaderInstallerTest"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
      <supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}" />
      <supportedOS Id="{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}" />
      <supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
    </windowsSettings>
  </application>
</assembly>
'@
    Set-Content -LiteralPath $testManifest -Value $manifestXml -Encoding utf8
    $testOut = Join-Path $dist 'QQNT-Diag-Test.exe'
    $testArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
        "/out:$testOut", "/win32manifest:$testManifest") + $refs + $resources + @($srcFile)
    & $csc $testArgs
    if ($LASTEXITCODE) { throw 'csc 编译测试副本失败' }
    Get-Item -LiteralPath $testOut | Select-Object FullName, Length
}
