$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskTools = Join-Path $taskRoot '.tools'
$taskDownloads = Join-Path $taskTools 'downloads'
New-Item -ItemType Directory -Path $taskDownloads -Force | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $taskTools 'dotnet\dotnet.exe'))) {
    $taskSdkZip = Join-Path $taskDownloads 'dotnet-sdk-10.0.401-win-x64.zip'
    Invoke-WebRequest -Uri 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip' -OutFile $taskSdkZip
    $taskHash = (Get-FileHash -LiteralPath $taskSdkZip -Algorithm SHA512).Hash
    if ($taskHash -ne '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430') { throw 'SDK 校验失败。' }
    Expand-Archive -LiteralPath $taskSdkZip -DestinationPath (Join-Path $taskTools 'dotnet') -Force
}
if (!(Test-Path -LiteralPath (Join-Path $taskTools 'mpvnet-release\libmpv-2.dll'))) {
    $taskNativeZip = Join-Path $taskDownloads 'mpvnet-portable-x64.zip'
    Invoke-WebRequest -Uri 'https://github.com/mpvnet-player/mpv.net/releases/download/v7.1.2.0/mpv.net-v7.1.2.0-portable-x64.zip' -OutFile $taskNativeZip
    Expand-Archive -LiteralPath $taskNativeZip -DestinationPath (Join-Path $taskTools 'mpvnet-release') -Force
}
Write-Output '构建依赖已准备。运行 scripts/build-portable.ps1。'
