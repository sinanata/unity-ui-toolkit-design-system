# Applies the UIR staged-updater staging-buffer patch to a Unity editor install's
# WebGL playback engine. See README.md here for what the engine defect is.
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

if (-not (Test-Path $target)) { throw "Not found: $target - wrong -UnityEditor path?" }

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

# The csproj pins Unity's own Cecil by HintPath; point it at this install.
$proj = Join-Path $here "uir-patcher.csproj"
(Get-Content $proj -Raw) -replace '<HintPath>.*Unity\.Cecil\.dll</HintPath>', "<HintPath>$cecil</HintPath>" |
    Set-Content $proj -Encoding utf8

$patched = Join-Path $env:TEMP "UnityEngine.UIElementsModule.PATCHED.dll"
Push-Location $here
try {
    dotnet run -- $target $patched
    if ($LASTEXITCODE -ne 0) { throw "patcher failed with exit $LASTEXITCODE" }
} finally { Pop-Location }

Invoke-Elevated "if (-not (Test-Path '$backup')) { Copy-Item '$target' '$backup' }; Copy-Item '$patched' '$target' -Force"

if ((Get-FileHash $target).Hash -eq (Get-FileHash $patched).Hash) {
    Write-Host "INSTALLED. Backup: $backup"
    Write-Host "Rebuild the WebGL player for it to take effect."
} else {
    throw "Install did not land (UAC declined?)"
}
