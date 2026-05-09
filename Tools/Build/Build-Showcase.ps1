#Requires -Version 5.1
<#
.SYNOPSIS
  WebGL showcase build entry point. Forwards to the cross-platform
  orchestrator installed as a submodule at Tools/.orchestrator.

.DESCRIPTION
  All hardening (live progress, lockfile cleanup, Burst-AOT cache retry,
  process-tree kill on Ctrl+C, JSON build report, optional Serve/Deploy)
  lives in the orchestrator. This file only nails down the three
  project-specific values: title, Unity batchmode method, live URL.

  See `Tools/.orchestrator/Tools/Build/Build-WebGL.ps1` for parameters
  and https://github.com/sinanata/unity-cross-platform-local-build-orchestrator
  for the full submodule pattern.

.EXAMPLE
  .\Tools\Build\Build-Showcase.ps1                # build only
  .\Tools\Build\Build-Showcase.ps1 -Serve         # build + npx serve smoke test
  .\Tools\Build\Build-Showcase.ps1 -Deploy        # build + force-push to gh-pages
  .\Tools\Build\Build-Showcase.ps1 -Deploy -Yes   # CI-style (no prompts)
  .\Tools\Build\Build-Showcase.ps1 -ClearCache    # nuke Library/BurstCache + Bee + Temp
  .\Tools\Build\Build-Showcase.ps1 -DryRun        # print every command, execute nothing
#>

$ErrorActionPreference = "Stop"
$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ConfigPath = Join-Path $PSScriptRoot "config.local.json"
$Orch       = Join-Path $RepoRoot "Tools\.orchestrator\Tools\Build\Build-WebGL.ps1"

if (-not (Test-Path $Orch)) {
    Write-Host "Orchestrator submodule missing at: $Orch" -ForegroundColor Red
    Write-Host "Fix: git submodule update --init --recursive"   -ForegroundColor Yellow
    exit 1
}

& $Orch -Title       "UI Toolkit Design System - Showcase Build" `
        -UnityMethod "UIDocumentDesignSystem.BuildTools.BuildCli.BuildWebGL" `
        -LiveUrl     "https://sinanata.github.io/unity-ui-document-design-system/" `
        -RepoRoot    $RepoRoot `
        -ConfigPath  $ConfigPath `
        @args
exit $LASTEXITCODE
