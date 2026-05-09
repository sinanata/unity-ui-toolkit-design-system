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
git clone --recurse-submodules https://github.com/sinanata/unity-ui-document-design-system
cd unity-ui-document-design-system
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

## GitHub Pages — one-time setup

After the first `-Deploy` push:

1. https://github.com/sinanata/unity-ui-document-design-system → **Settings** → **Pages**.
2. Source: **Deploy from a branch**.
3. Branch: **`gh-pages`**, folder: **`/ (root)`**.
4. Save. URL: https://sinanata.github.io/unity-ui-document-design-system/

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
