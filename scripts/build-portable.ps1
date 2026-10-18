param([switch]$SkipPublish, [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSdk = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $taskSdk)) { throw '请先准备 .tools/dotnet SDK（详见 docs/modern-player.md）。' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$taskOutput = if ($OutputDirectory) { [System.IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory)) } else { Join-Path $taskRoot 'dist\FrameFlow' }
if (!$SkipPublish) {
    & $taskSdk publish (Join-Path $taskRoot 'src\MpvNet.Windows\MpvNet.Windows.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -m:1 -nr:false -p:UseSharedCompilation=false -o $taskOutput --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw '播放器编译失败。' }
}
$taskNativeRoot = Join-Path $taskRoot '.tools\mpvnet-release'
foreach ($taskNative in @('libmpv-2.dll','MediaInfo.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $taskNativeRoot $taskNative))) { throw "缺少原版公开发布组件 $taskNative。" }
    Copy-Item -LiteralPath (Join-Path $taskNativeRoot $taskNative) -Destination $taskOutput -Force
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'License.txt') -Destination (Join-Path $taskOutput 'License.txt') -Force
if (Test-Path -LiteralPath (Join-Path $taskNativeRoot 'Locale')) { Copy-Item -LiteralPath (Join-Path $taskNativeRoot 'Locale') -Destination $taskOutput -Recurse -Force }
$taskConfig = Join-Path $taskOutput 'portable_config'
New-Item -ItemType Directory -Path $taskConfig -Force | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $taskConfig 'mpv.conf'))) {
@'
osc=no
border=no
title-bar=no
keepaspect-window=no
hwdec=auto-safe
save-position-on-quit=no
resume-playback=yes
watch-later-options=start
keep-open=yes
autocreate-playlist=no
input-ipc-server=\\.\pipe\FrameFlow
'@ | Set-Content -LiteralPath (Join-Path $taskConfig 'mpv.conf') -Encoding utf8
}
# Frontend defaults already live in code. Do not ship a fresh frameflow.conf:
# it would shadow an imported legacy mpvnet.conf before one-time migration.
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\modern-player.md') -Destination (Join-Path $taskOutput '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\third-party.md') -Destination (Join-Path $taskOutput '第三方组件.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\open-source-notices.md') -Destination (Join-Path $taskOutput '开源声明.md') -Force
$taskLicenseOutput = Join-Path $taskOutput 'licenses'
New-Item -ItemType Directory -Path $taskLicenseOutput -Force | Out-Null
foreach ($taskLicenseFile in @('NGettext-LICENSE.txt', 'XamlBehaviors-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('docs\licenses\' + $taskLicenseFile)) -Destination $taskLicenseOutput -Force
}
foreach ($taskLicenseFile in @('mpv-Copyright.txt', 'mpv-GPL-2.txt', 'FFmpeg-LICENSE.md', 'FFmpeg-GPL-2.txt', 'MediaInfo-LICENSE.html', 'HandyControl-LICENSE.txt', 'NGettext.Wpf-LICENSE.txt', 'GPL-3.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('docs\licenses\' + $taskLicenseFile)) -Destination $taskLicenseOutput -Force
}
$taskDependencyLicenses = @{
    'CommunityToolkit-LICENSE.md' = 'communitytoolkit.mvvm\8.4.0\License.md'
    'CommunityToolkit-NOTICES.txt' = 'communitytoolkit.mvvm\8.4.0\ThirdPartyNotices.txt'
    'Dotnet-LICENSE.txt' = 'microsoft.netcore.app.runtime.win-x64\10.0.12\LICENSE.TXT'
    'Dotnet-NOTICES.txt' = 'microsoft.netcore.app.runtime.win-x64\10.0.12\THIRD-PARTY-NOTICES.TXT'
    'WindowsDesktop-LICENSE.txt' = 'microsoft.windowsdesktop.app.runtime.win-x64\10.0.12\LICENSE'
}
foreach ($taskLicense in $taskDependencyLicenses.GetEnumerator()) {
    $taskLicensePath = Join-Path $env:NUGET_PACKAGES $taskLicense.Value
    if (Test-Path -LiteralPath $taskLicensePath) { Copy-Item -LiteralPath $taskLicensePath -Destination (Join-Path $taskLicenseOutput $taskLicense.Key) -Force }
}
Write-Output "便携版已生成：$taskOutput\FrameFlow.exe"
