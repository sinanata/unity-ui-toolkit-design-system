# UIR staged-updater staging patch (Unity 6000.5, WebGL/WebGPU players)

## The engine defect

`UnityEngine.UIElements.UIR.GpuUpdaterStaged<T>.CompleteUpdate` — the UI Toolkit geometry
updater used on platforms **without mapped GPU buffers** (the WebGL and WebGPU players;
editor and native players use a different, unaffected path) — sizes its per-frame staging
buffer from a data set's dirty count **before** it finishes computing the ranges it will
actually copy:

```csharp
var staging = FindOrAllocateBuffer(m_AvailableStagingBuffers,
                  (int)dataSet.totalDirtyCount);   // sized here...
PrepareCopyRanges(dataSet, staging);               // ...then grows:
//   dataSet.ConsolidateRanges()  -> a >=90%-dense range set is replaced by the
//                                   whole [min,max) span, gaps included (<= +11.1%)
//   AlignIndexRange(...)         -> every index range grows outward to even (<= +2 each)
```

Staging buffers come in padded tiers — `{8192, 65536}` vertices, `{8192, 262144}` indices —
and a request **above the last tier is allocated exactly**. So any one-frame burst landing
within ~11% below a tier, or above the last tier, makes the updater read past its own
staging buffer:

```
GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=590904 srcSize=589824)   ← 8192-vertex tier (72 B stride)
GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=17352  srcSize=16384)    ← 8192-index tier (2 B stride)
```

In a WebGL player the stray read eventually surfaces as a fatal wasm `bounds` exception,
and the skipped copies leave panels rendering **stale geometry** (a restyled UI keeps its
old look). Any full-subtree restyle with reallocation churn — applying or reverting a
material theme, mass style swaps — produces bursts in the danger windows. No host-side
pacing can fully avoid them, because the windows exist at *every* tier boundary.

## The patch

One IL edit, `r -> 2r + 64` at the single call site:

```csharp
FindOrAllocateBuffer(m_AvailableStagingBuffers, (int)(totalDirtyCount + totalDirtyCount + 64));
```

That covers every possible post-sizing growth (consolidation ≤ 1.112×, index alignment
≤ 1.34× for real 6-index quad ranges) with margin. Cost: transient staging buffers at most
twice as large — they top out around a megabyte.

The patcher (`Program.cs`) rewrites the assembly with Unity's own bundled `Unity.Cecil`,
verifies there is exactly one call site, and is idempotent.

## Apply / restore

```powershell
.\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1                # patch the default 6000.5.2f1 install
.\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1 -Restore      # put the original back
```

One UAC prompt per run (the target lives in Program Files). The original is kept beside the
target as `UnityEngine.UIElementsModule.dll.orig-pre-uir-patch`. The patch changes only the
WebGL playback engine's managed module — the editor's own runtime is untouched — and takes
effect on the **next WebGL build**. Re-apply after upgrading or reinstalling the editor.

`BuildCli.BuildWebGL` detects the patch (backup present and differing from the live module)
and injects the per-build define `DS_UIR_STAGING_PATCHED`, which flips
`DsFxManager.AllowWorldSpacePanels` to default **on** in that player: a patched build shows
world-space materials with no `?worldfx=1` flag, an unpatched build keeps the safe gate.

## Scope and provenance

- Verified against 6000.5.2f1. The patcher fails loudly (`expected exactly 1
  FindOrAllocateBuffer call`) if a future Unity changes the method, rather than guessing.
- Diagnosed 2026-07-17 from the decompiled module after `?worldfx=1` world-space material
  testing kept crashing the showcase (see `docs/MATERIALS.md`, world-space section, and
  CHANGELOG 1.5.0). Report upstream to Unity as: staging buffer sized from
  pre-consolidation dirty count in `GpuUpdaterStaged.CompleteUpdate`.
