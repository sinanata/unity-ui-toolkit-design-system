# UIR staged-updater staging patch (Unity 6000.5 WebGL/WebGPU players only)

> **Fixed upstream. 🎉** We found this bug, traced it to one line in Unity's renderer, and
> [reported it on Unity Discussions](https://discussions.unity.com/t/webgl-6000-5-2f1-ui-toolkit-staged-updater-under-sizes-its-staging-buffer-copybufferranges-reads-out-of-bounds-on-large-single-frame-restyles/1731377)
> with this patch attached. Unity's UI Toolkit team picked it up as
> [**UUM-151098**](https://issuetracker.unity.com/issues/24430/webgl-crash-when-consolidated-range-is-larger-than-the-sum-of-individual-ranges)
> and shipped the fix in **Unity 6000.6.2f1** and **6000.7.0b1**. On those editors, and every
> editor after them, you need nothing from this folder: a WebGL player turns world-space materials
> on by itself. Unity 6000.5 is marked Won't Fix ("6.5 support ended before we could backport
> it"), so on 6000.5 this patch is still the fix.

## Which editors need it

| Editor | Staged updater | Status | What to do |
| --- | --- | --- | --- |
| 6000.0 to 6000.4 | not present (UIR uploads geometry another way) | never affected | nothing, and materials need 6000.5+ anyway |
| **6000.5.x** | present, defective | Won't Fix upstream | apply this patch on the WebGL build machine |
| 6000.6.0f1, 6000.6.1f1 | present, defective | fixed in the next patch release | update the editor to 6000.6.2f1 or later |
| **6000.6.2f1+**, **6000.7.0b1+** | present, **fixed** (UUM-151098) | fixed upstream | nothing; the script and the patcher both refuse |

`DsFxManager.AllowWorldSpacePanels` follows the same table at runtime (see
`DsUirStagingFix.IsFixedIn`), so a project built on a fixed editor needs no flag, no define and no
patch.

## The engine defect

`UnityEngine.UIElements.UIR.GpuUpdaterStaged<T>.CompleteUpdate`, the UI Toolkit geometry updater
used on platforms **without mapped GPU buffers** (the WebGL and WebGPU players; editor and native
players use a different, unaffected path), sizes its per-frame staging buffer from a data set's
dirty count **before** it finishes computing the ranges it will actually copy:

```csharp
var staging = FindOrAllocateBuffer(m_AvailableStagingBuffers,
                  (int)dataSet.totalDirtyCount);   // sized here...
PrepareCopyRanges(dataSet, staging);               // ...then grows:
//   dataSet.ConsolidateRanges()  -> a >=90%-dense range set is replaced by the
//                                   whole [min,max) span, gaps included (<= +11.1%)
//   AlignIndexRange(...)         -> every index range grows outward to even (<= +2 each)
```

Staging buffers come in padded tiers, `{8192, 65536}` vertices and `{8192, 262144}` indices, and a
request **above the last tier is allocated exactly**. So any one-frame burst landing within ~11%
below a tier, or above the last tier, makes the updater read past its own staging buffer:

```
GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=590904 srcSize=589824)   <- 8192-vertex tier (72 B stride)
GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=17352  srcSize=16384)    <- 8192-index tier (2 B stride)
```

In a WebGL player the stray read eventually surfaces as a fatal wasm `bounds` exception, and the
skipped copies leave panels rendering **stale geometry** (a restyled UI keeps its old look). Any
full-subtree restyle with reallocation churn, such as applying or reverting a material theme or a
mass style swap, produces bursts in the danger windows. No host-side pacing can fully avoid them,
because the windows exist at *every* tier boundary.

## Unity's fix (6000.6.2f1+)

Unity's fix is the one we suggested in the report, done properly inside the engine. In the
6000.6.7f1 WebGL module, `CompleteUpdate` now consolidates every dirty data set **first**, and only
then sizes each staging request through a new `GetRequiredStagingLength`: the exact consolidated
count for vertex buffers, and for index buffers the exact sum of the even-aligned ranges (or
`count + 2 per range` when there are more than 16 ranges, which bounds the alignment growth).
`PrepareCopyRanges` no longer consolidates behind the sizing's back, and it now asserts
`usedCount <= capacity`.

That is better than this patch, not just equivalent: the patch pads every request to `2r+64`
because it cannot see the ranges, while Unity's sizing is exact, so staging memory stays where it
was.

### How the fix was verified (2026-10-21)

- **Decompiled diff.** `GpuUpdaterStaged` from the 6000.5.2f1 and 6000.6.7f1 WebGL modules,
  side by side: the sizing moved behind consolidation, `GetRequiredStagingLength` is new, and the
  capacity assert is new. `DataSet` itself (`AddDirtyRange`, `ConsolidateRanges`) is unchanged.
- **Behavioural replay.** The sizing, consolidation, alignment and staging-reuse code of both
  versions, lifted from the decompiled modules and driven by a harness whose `CopyBufferRanges`
  stand-in applies GfxDevice's bounds check. The 6000.5.2f1 logic reproduces **both errors above
  to the byte**; the 6000.6.7f1 logic handles the same inputs cleanly.
- **Fuzzing.** 12 million frames (3 seeds, 2 million frames each, vertex and index), about 121
  million copy ranges per version, biased toward the danger windows:

  | Engine logic | Out-of-bounds reads | Peak staging memory |
  | --- | ---: | --- |
  | 6000.5.2f1 | 2,571,357 | baseline |
  | 6000.5.2f1 + this patch (`2r+64`) | 0 | about 1.9x |
  | **6000.6.7f1** | **0** (and 0 failed asserts) | baseline, within 4% |

- **This tool, against every shape of the module.** The patcher still produces a byte-identical
  module on 6000.5.2f1, reports "not needed" on 6000.6.7f1 and "not applicable" on 6000.3.8f1.

## The patch (6000.5.x only)

One IL edit, `r -> 2r + 64` at the single call site:

```csharp
FindOrAllocateBuffer(m_AvailableStagingBuffers, (int)(totalDirtyCount + totalDirtyCount + 64));
```

That covers every possible post-sizing growth (consolidation up to 1.112x, index alignment up to
1.34x for real 6-index quad ranges) with margin. Cost: transient staging buffers at most twice as
large; they top out around a megabyte in real UIs.

The patcher (`Program.cs`) rewrites the assembly with Unity's own bundled `Unity.Cecil`, verifies
there is exactly one call site, and is idempotent. It exits `3` without writing anything when the
module already carries Unity's fix, and `4` when the module has no staged updater at all.

## Apply / restore

```powershell
.\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1                # patch the default 6000.5.2f1 install
.\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1 -UnityEditor "C:\Program Files\Unity\Hub\Editor\6000.5.x"
.\Tools\UirStagingPatch\Apply-UirStagingPatch.ps1 -Restore      # put the original back
```

The script reads the editor version first. On a fixed editor (6000.6.2f1+, 6000.7.0b1+) or one
older than 6000.5 it says so and exits without building anything or asking for elevation; on
6000.6.0f1 or 6000.6.1f1 it tells you to update instead. On 6000.5 it builds the patcher and pops
one UAC prompt (the target lives in Program Files). The original is kept beside the target as
`UnityEngine.UIElementsModule.dll.orig-pre-uir-patch`. The patch changes only the WebGL playback
engine's managed module, never the editor's own runtime, and takes effect on the **next WebGL
build**. Re-apply after upgrading or reinstalling a 6000.5 editor.

`BuildCli.BuildWebGL` handles both worlds. On a fixed editor it logs that Unity's fix is present
and builds as is. On 6000.5 it detects the patch (backup present and differing from the live
module) and injects the per-build define `DS_UIR_STAGING_PATCHED`, which flips
`DsFxManager.AllowWorldSpacePanels` to default **on** in that player; an unpatched 6000.5 build
keeps the safe gate and the showcase's `?worldfx=1` escape hatch.

## Provenance

- 2026-07-17: diagnosed from the decompiled module after `?worldfx=1` world-space material testing
  kept crashing the showcase (see `docs/MATERIALS.md` and CHANGELOG 1.5.0), patched here, and
  reported upstream with the root cause and a suggested fix.
- 2026-08-31: Unity confirmed it on the thread: "this is being fixed as part of case UUM-151098".
- Fixed in 6000.6.2f1 and 6000.7.0b1; 6000.5 Won't Fix.
- 2026-10-21: fix verified against 6000.6.7f1 as described above; this folder reworded to the
  version window it still serves.

Thank you to Unity's UI Toolkit team for taking a community report from root cause to shipped fix.
