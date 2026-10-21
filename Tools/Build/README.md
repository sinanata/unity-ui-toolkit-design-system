# Showcase build orchestrator

WebGL build + optional `npx serve` smoke test + optional `gh-pages`
force-push, all from one PowerShell command on Windows.

The hardening (lockfile cleanup, Burst-AOT retry, process-tree kill,
JSON build report, deploy worktree) lives in a separate cross-platform
build orchestrator installed here as a git submodule:

> [`unity-cross-platform-local-build-orchestrator`](https://github.com/sinanata/unity-cross-platform-local-build-orchestrator) at `Tools/.orchestrator/`

`Tools/Build/Build-Showcase.ps1` is a 12-line shim that forwards to the
submodule with this repo's title, batchmode method, and live URL.

## First-run setup

```powershell
git clone --recurse-submodules https://github.com/sinanata/unity-ui-toolkit-design-system
cd unity-ui-toolkit-design-system
copy Tools\Build\config.example.json Tools\Build\config.local.json
# Edit unity.windowsEditorPath if Unity isn't in C:\Program Files\Unity\Hub\Editor\6000.3.8f1\
```

If you forgot `--recurse-submodules`, run `git submodule update --init --recursive` after the fact.

## Daily usage

```powershell
.\Tools\Build\Build-Showcase.ps1                # build only
.\Tools\Build\Build-Showcase.ps1 -Serve         # build + serve at http://localhost:3000
.\Tools\Build\Build-Showcase.ps1 -Deploy        # build + force-push to gh-pages (single-commit)
.\Tools\Build\Build-Showcase.ps1 -Deploy -Yes   # CI-style (no prompts)
.\Tools\Build\Build-Showcase.ps1 -ClearCache    # nuke Library/BurstCache + Bee + Temp before build
.\Tools\Build\Build-Showcase.ps1 -DryRun        # print every command, execute nothing
```

Close the Unity editor before running — batchmode and a live editor session fight for the same licence seat.

## What runs

1. **Preflight** — config + Unity exe exist, stale `Temp/UnityLockfile` removed.
2. **Optional cache clear** — wipes `Library/BurstCache`, `Library/Bee`, `Library/ScriptAssemblies`, `Temp`.
3. **WebGL build** — `Unity -batchmode -executeMethod UIDocumentDesignSystem.BuildTools.BuildCli.BuildWebGL`. Live phase progress from Unity's `DisplayProgressbar:` log markers. JSON build report at `Tools/Build/output/report-*.json`.
4. **Optional `-Serve`** — `npx serve build/WebGL` on the configured port.
5. **Optional `-Deploy`** — orphan worktree → copy artefacts → force-push to `gh-pages` → remove worktree. Branch stays at one commit so the public repo doesn't accumulate ~10 MB per deploy.

Hardening (defensive checks, Burst-AOT cache auto-retry, native-crash labelling, deploy worktree cleanup) — see [the orchestrator's README](https://github.com/sinanata/unity-cross-platform-local-build-orchestrator) for the full list.

### World-space materials need an engine patch on this machine

GPU materials on the world-space gallery (the `Blueprint (shader)` theme in world mode) need a WebGL engine without the staged-updater buffer overrun. Unity fixed it upstream from our report as [UUM-151098](https://issuetracker.unity.com/issues/24430/webgl-crash-when-consolidated-range-is-larger-than-the-sum-of-individual-ranges) in **6000.6.2f1** and **6000.7.0b1**, and this repo builds with 6000.6.7f1, so nothing extra is needed: `BuildCli.BuildWebGL` logs that the fix is present and the player turns world-space materials on by itself. Only a **6000.5** build machine (Won't Fix upstream) needs the one-instruction patch in [`Tools/UirStagingPatch`](../UirStagingPatch/README.md); there `BuildCli` auto-detects it (backup present and differing from the live module) and injects the `DS_UIR_STAGING_PATCHED` define. An unpatched 6000.5 machine still builds fine; world-space materials just stay off (`DsFxManager.AllowWorldSpacePanels = false`) and everything else is unaffected. Re-apply the patch after upgrading or reinstalling a 6000.5 editor.

## GitHub Pages — one-time setup

After the first `-Deploy` push:

1. https://github.com/sinanata/unity-ui-toolkit-design-system → **Settings** → **Pages**.
2. Source: **Deploy from a branch**.
3. Branch: **`gh-pages`**, folder: **`/ (root)`**.
4. Save. URL: https://sinanata.github.io/unity-ui-toolkit-design-system/

## Files in this folder

```
Tools/Build/
├── Build-Showcase.ps1        ← shim, forwards to orchestrator with our project values
├── config.example.json       ← copy to config.local.json (gitignored)
├── README.md                 ← you are here
└── output/                   ← Unity logs + JSON build reports (gitignored)

Tools/.orchestrator/          ← submodule — the heavy lifting
└── Tools/Build/
    ├── Build-WebGL.ps1       ← parameter-driven WebGL flow
    └── Deploy-GhPages.ps1    ← single-commit gh-pages force-push via worktree
```

`BuildCli.cs` lives at `Assets/Editor/BuildCli.cs` (project-local: scene path + WebGL template name + namespace).

## Extending

- **Different Unity version** — change `unity.windowsEditorPath` in `config.local.json`.
- **Different output dir** — change `paths.buildDir` (absolute or repo-relative).
- **Different deploy target** — change `deploy.remoteName` / `deploy.branchName`.
- **Different orchestrator pin** — `cd Tools/.orchestrator && git checkout <sha>` and commit the submodule update.
