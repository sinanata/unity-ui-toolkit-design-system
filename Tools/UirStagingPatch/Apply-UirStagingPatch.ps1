# Applies the UIR staged-updater staging-buffer patch to a Unity editor install's
# WebGL playback engine. See README.md here for what the engine defect is.
#
# VERSION LIMITATION: Unity fixed this upstream as UUM-151098 in 6000.6.2f1 and
# 6000.7.0b1. Only Unity 6000.5.x needs this patch (6000.5 is Won't Fix upstream).
# On a fixed editor, or on one older than 6000.5 (no staged updater at all), this
# script says so and exits without touching anything or asking for elevation.
#
#   .\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1                 # default editor path
#   .\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1 -UnityEditor "C:\...\6000.5.2f1"
#   .\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1 -Restore       # put the original back
#
# Needs the .NET SDK (builds the one-file Cecil patcher on first use) and pops ONE
# UAC prompt for the copy into Program Files. A backup lands beside the target as
# UnityEngine.UIElementsModule.dll.orig-pre-uir-patch; -Restore copies it back.
param(
    [string]$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.5.2f1",
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$target  = Join-Path $UnityEditor "Editor\Data\PlaybackEngines\WebGLSupport\Managed\UnityEngine.UIElementsModule.dll"
$backup  = "$target.orig-pre-uir-patch"
$cecil   = Join-Path $UnityEditor "Editor\Data\Managed\Unity.Cecil.dll"
$issue   = "UUM-151098 (https://issuetracker.unity.com/issues/24430)"

# Editor version from Unity.exe's product version (e.g. 6000.5.2f1_eb73d3b415a1),
# falling back to the Hub folder name.
function Get-EditorVersion([string]$root) {
    $exe = Join-Path $root "Editor\Unity.exe"
    $text = $null
    if (Test-Path $exe) { $text = (Get-Item $exe).VersionInfo.ProductVersion }
    if (-not $text) { $text = Split-Path $root -Leaf }
    $m = [regex]::Match([string]$text, '(\d+)\.(\d+)\.(\d+)([a-z])(\d+)')
    if (-not $m.Success) { return $null }
    return [pscustomobject]@{
        Text  = $m.Value
        Major = [int]$m.Groups[1].Value
        Minor = [int]$m.Groups[2].Value
        Patch = [int]$m.Groups[3].Value
        Stage = $m.Groups[4].Value
    }
}

# Mirrors DesignSystem.Runtime.Fx.DsUirStagingFix.IsFixedIn.
function Test-HasUpstreamFix($v) {
    if ($v.Major -ne 6000) { return ($v.Major -gt 6000) }
    if ($v.Minor -ge 8) { return $true }
    if ($v.Minor -eq 7) { return (($v.Patch -gt 0) -or ($v.Stage -ne 'a')) }
    if ($v.Minor -eq 6) { return ($v.Patch -ge 2) }
    return $false
}

function Invoke-Elevated([string]$script) {
    $tmp = Join-Path $env:TEMP "uir-patch-elevated.ps1"
    Set-Content -Path $tmp -Value $script -Encoding utf8
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',$tmp
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

if ($Restore) {
    if (-not (Test-Path $backup)) { throw "No backup at $backup - nothing to restore." }
    Invoke-Elevated "Copy-Item '$backup' '$target' -Force"
    if ((Get-FileHash $target).Hash -eq (Get-FileHash $backup).Hash) { Write-Host "RESTORED original module." }
    else { throw "Restore did not land (UAC declined?)" }
    return
}

$ver = Get-EditorVersion $UnityEditor
if ($ver) {
    if (Test-HasUpstreamFix $ver) {
        Write-Host "Unity $($ver.Text) already carries Unity's own fix for this bug: $issue." -ForegroundColor Green
        Write-Host "Fixed upstream in 6000.6.2f1 and 6000.7.0b1. Nothing to patch; WebGL players built"
        Write-Host "with this editor turn world-space materials on by themselves."
        return
    }
    if ($ver.Major -eq 6000 -and $ver.Minor -lt 5) {
        Write-Host "Unity $($ver.Text) has no staged geometry updater (it first ships in 6000.5), so it"
        Write-Host "was never affected. Nothing to patch."
        return
    }
    if ($ver.Major -eq 6000 -and $ver.Minor -eq 6) {
        throw "Unity $($ver.Text) still has the bug but is not the 6000.5 build this patch targets. Update to 6000.6.2f1 or later, which carries Unity's fix ($issue)."
    }
} else {
    Write-Host "[WARN] Could not read the editor version under $UnityEditor; the patcher will check the module itself." -ForegroundColor Yellow
}

if (-not (Test-Path $target)) { throw "Not found: $target - wrong -UnityEditor path?" }

# The csproj pins Unity's own Cecil by HintPath; point it at this install.
$proj = Join-Path $here "uir-patcher.csproj"
(Get-Content $proj -Raw) -replace '<HintPath>.*Unity\.Cecil\.dll</HintPath>', "<HintPath>$cecil</HintPath>" |
    Set-Content $proj -Encoding utf8

$patched = Join-Path $env:TEMP "UnityEngine.UIElementsModule.PATCHED.dll"
Push-Location $here
try {
    dotnet run -- $target $patched
    $code = $LASTEXITCODE
} finally { Pop-Location }
if ($code -eq 3) { Write-Host "Nothing to patch: the module already carries Unity's fix ($issue)." -ForegroundColor Green; return }
if ($code -eq 4) { Write-Host "Nothing to patch: this module has no staged geometry updater (pre-6000.5)."; return }
if ($code -ne 0) { throw "patcher failed with exit $code" }

Invoke-Elevated "if (-not (Test-Path '$backup')) { Copy-Item '$target' '$backup' }; Copy-Item '$patched' '$target' -Force"

if ((Get-FileHash $target).Hash -eq (Get-FileHash $patched).Hash) {
    Write-Host "INSTALLED. Backup: $backup"
    Write-Host "Rebuild the WebGL player for it to take effect."
} else {
    throw "Install did not land (UAC declined?)"
}
