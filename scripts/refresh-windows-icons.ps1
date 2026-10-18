[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [string]$IconPath = '',
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$taskPlayer = [IO.Path]::GetFullPath($PlayerPath)
if (!(Test-Path -LiteralPath $taskPlayer -PathType Leaf) -or
    [IO.Path]::GetFileName($taskPlayer) -notin @('FrameFlow.exe','mpvnet.exe')) {
    throw 'PlayerPath must point to an existing FrameFlow.exe (or legacy mpvnet.exe).'
}
$taskIcon = $taskPlayer
if ($IconPath) {
    $taskIcon = [IO.Path]::GetFullPath($IconPath)
    if (!(Test-Path -LiteralPath $taskIcon -PathType Leaf) -or
        [IO.Path]::GetExtension($taskIcon) -ine '.ico') {
        throw 'IconPath must point to an existing ICO file.'
    }
}

# Only update the icon for the already registered copy. Do not change the
# default player, open command, UserChoice, or other applications' keys.
$taskApplication = 'Software\Classes\Applications\' + [IO.Path]::GetFileName($taskPlayer)
$taskCommandKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskApplication + '\shell\open\command')
if (!$taskCommandKey) { throw 'This player has no per-user application registration.' }
try { $taskCommand = [string]$taskCommandKey.GetValue('') }
finally { $taskCommandKey.Dispose() }
if ($taskCommand -notmatch '^\s*"([^"]+)"(?:\s|$)' -or
    [IO.Path]::GetFullPath($Matches[1]) -ine $taskPlayer) {
    throw 'The registered player open command does not match PlayerPath. No changes made.'
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class MpvNetIconRefreshShell {
    [DllImport("shlwapi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
    private static extern int AssocQueryStringW(uint flags, uint kind, string association, string extra, StringBuilder value, ref uint length);
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr first, IntPtr second);
    public static string Query(uint kind) {
        uint length=4096;
        var value=new StringBuilder((int)length);
        int result=AssocQueryStringW(0,kind,".mp4",null,value,ref length);
        return result==0 ? value.ToString() : "HRESULT:0x" + unchecked((uint)result).ToString("X8");
    }
    public static void Refresh() {
        // SHCNE_ASSOCCHANGED; SHCNF_IDLIST | SHCNF_FLUSH.
        SHChangeNotify(0x08000000,0x1000,IntPtr.Zero,IntPtr.Zero);
    }
}
'@

$taskIconKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskApplication + '\DefaultIcon')
$taskHadIconKey = $null -ne $taskIconKey
$taskOldIcon = $null
if ($taskIconKey) {
    try { $taskOldIcon = $taskIconKey.GetValue('') }
    finally { $taskIconKey.Dispose() }
}
$taskAppKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskApplication)
try {
    $taskHadOverlay = $taskAppKey.GetValueNames() -contains 'TypeOverlay'
    $taskOldOverlay = $taskAppKey.GetValue('TypeOverlay')
}
finally { $taskAppKey.Dispose() }
$taskChoicePath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mp4\UserChoice'
$taskChoiceBefore = Get-ItemProperty -LiteralPath $taskChoicePath -ErrorAction SilentlyContinue
$taskReport = [ordered]@{
    Timestamp = [DateTimeOffset]::Now.ToString('o')
    PlayerPath = $taskPlayer
    PlayerVersion = (Get-Item -LiteralPath $taskPlayer).VersionInfo.FileVersion
    IconResourceFile = $taskIcon
    RegistryKey = 'HKCU\' + $taskApplication + '\DefaultIcon'
    IconKeyExistedBefore = $taskHadIconKey
    DefaultIconBefore = $taskOldIcon
    DefaultIconAfter = '"' + $taskIcon + '",0'
    ThumbnailOverlayExistedBefore = $taskHadOverlay
    ThumbnailOverlayBefore = $taskOldOverlay
    ThumbnailOverlayAfter = if ($IconPath) { '"' + $taskIcon + '",0' } else { $taskOldOverlay }
    Mp4ExecutableBefore = [MpvNetIconRefreshShell]::Query(2)
    Mp4IconBefore = [MpvNetIconRefreshShell]::Query(15)
    UserChoiceBefore = $taskChoiceBefore.ProgId
    Status = 'prepared'
}

if (!$PSCmdlet.ShouldProcess('HKCU\' + $taskApplication, 'Update player icon and thumbnail overlay, then notify Windows')) { return }
if (!$ReportPath) {
    $taskReportFolder = Join-Path (Split-Path $PSScriptRoot -Parent) 'test-results\icon-refresh-v19'
    $ReportPath = Join-Path $taskReportFolder ('refresh-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.json')
}
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Path (Split-Path $ReportPath -Parent) -Force | Out-Null
# Save the previous icon value before making the reversible change.
$taskReport | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$taskIconKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($taskApplication + '\DefaultIcon')
try { $taskIconKey.SetValue('', $taskReport.DefaultIconAfter, [Microsoft.Win32.RegistryValueKind]::String) }
finally { $taskIconKey.Dispose() }
if ($IconPath) {
    # Thumbnail badges otherwise fall back to the application icon, which
    # Windows may cache separately from a file type's DefaultIcon.
    $taskAppKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($taskApplication)
    try { $taskAppKey.SetValue('TypeOverlay', $taskReport.ThumbnailOverlayAfter, [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $taskAppKey.Dispose() }
}
[MpvNetIconRefreshShell]::Refresh()
$taskChoiceAfter = Get-ItemProperty -LiteralPath $taskChoicePath -ErrorAction SilentlyContinue
$taskReport.Mp4ExecutableAfter = [MpvNetIconRefreshShell]::Query(2)
$taskReport.Mp4IconAfter = [MpvNetIconRefreshShell]::Query(15)
$taskReport.UserChoiceAfter = $taskChoiceAfter.ProgId
$taskReport.UserChoiceUnchanged = $taskChoiceBefore.ProgId -ceq $taskChoiceAfter.ProgId -and
    $taskChoiceBefore.Hash -ceq $taskChoiceAfter.Hash
$taskReport.Status = 'refreshed'
$taskReport | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$taskReport.ReportPath = $ReportPath
$taskReport | ConvertTo-Json -Depth 3 -Compress
