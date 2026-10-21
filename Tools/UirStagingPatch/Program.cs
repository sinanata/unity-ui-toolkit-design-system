// Patches Unity 6000.5's staged UI-geometry updater so its staging buffer is
// sized with headroom for the growth the updater itself applies AFTER sizing.
//
// The defect (UnityEngine.UIElements.UIR.GpuUpdaterStaged<T>.CompleteUpdate):
//
//     StagingBufferInfo b = FindOrAllocateBuffer(m_AvailableStagingBuffers,
//                               (int)dataSet.totalDirtyCount);      // sized HERE
//     PrepareCopyRanges(dataSet, b);                                // grows AFTER:
//        - dataSet.ConsolidateRanges() replaces >=90%-dense ranges with the whole
//          [min,max) span, gap bytes included (up to +11.1%)
//        - AlignIndexRange grows every index range outward to even (+<=2/range)
//
// The supported staging tiers are {8192, 65536} vertices / {8192, 262144}
// indices, and a request above the last tier is allocated EXACTLY. So any
// one-frame burst within ~11% below a tier - or above the last tier - overruns
// the staging buffer and the GfxDevice logs
//     CopyBufferRanges: range reads out of bounds
// then reads whatever follows the buffer (observed as a fatal wasm "bounds"
// exception on WebGL). Only platforms without mapped GPU buffers run this code:
// the WebGL and WebGPU players.
//
// The patch injects `dup; add; ldc.i4.s 64; add` before the FindOrAllocateBuffer
// call, turning the requested length r into 2r+64 - comfortably above every
// possible post-sizing growth (consolidation <= 1.112r, alignment <= 1.34r in
// realistic 6-index quad ranges). Cost: transient staging buffers at most twice
// as large (they top out around a megabyte).
//
// Usage:  uir-patcher <input.dll> <output.dll>
// Idempotent: re-running on a patched assembly is a no-op.
//
// Version limitation: Unity fixed this upstream as UUM-151098 in 6000.6.2f1 and 6000.7.0b1
// (consolidate first, then size each staging request exactly). 6000.5 is Won't Fix, so this
// patch is for 6000.5.x only. Exit codes: 0 patched or already patched, 1 failure, 2 usage,
// 3 not needed (the module already carries Unity's fix), 4 not applicable (no staged updater,
// i.e. an editor before 6000.5).

using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: uir-patcher <input.dll> <output.dll>");
    return 2;
}

var input = args[0];
var output = args[1];

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input)));
var readerParams = new ReaderParameters { ReadWrite = false, InMemory = true, AssemblyResolver = resolver };
using var module = ModuleDefinition.ReadModule(input, readerParams);

// 6000.6.2f1+ ships Unity's own fix: a non-generic GpuUpdaterStaged whose CompleteUpdate
// consolidates every dirty data set first and sizes staging through GetRequiredStagingLength.
var fixedType = module.GetType("UnityEngine.UIElements.UIR.GpuUpdaterStaged");
if (fixedType != null && fixedType.Methods.Any(m => m.Name == "GetRequiredStagingLength"))
{
    Console.WriteLine("NOT NEEDED: this module already carries Unity's fix (UUM-151098, " +
                      "Unity 6000.6.2f1 and 6000.7.0b1). Nothing to patch.");
    return 3;
}

var type = module.GetType("UnityEngine.UIElements.UIR.GpuUpdaterStaged`1");
if (type == null)
{
    if (fixedType == null)
    {
        Console.WriteLine("NOT APPLICABLE: no staged geometry updater in " + input +
                          ". It first ships in Unity 6000.5; earlier editors were never affected.");
        return 4;
    }
    Console.Error.WriteLine("FAIL: this module has GpuUpdaterStaged without Unity's fix but not in the " +
                            "6000.5 shape this patch knows (6000.6.0f1 or 6000.6.1f1?). Update the editor " +
                            "to 6000.6.2f1 or later instead, which carries the fix (UUM-151098).");
    return 1;
}

var method = type.Methods.FirstOrDefault(m => m.Name == "CompleteUpdate");
if (method == null || !method.HasBody)
{
    Console.Error.WriteLine("FAIL: CompleteUpdate not found or has no body.");
    return 1;
}

var body = method.Body;
var il = body.GetILProcessor();

var callSites = body.Instructions
    .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
             && i.Operand is MethodReference mr
             && mr.Name == "FindOrAllocateBuffer")
    .ToList();

if (callSites.Count != 1)
{
    Console.Error.WriteLine($"FAIL: expected exactly 1 FindOrAllocateBuffer call, found {callSites.Count}. " +
                            "The engine code changed; re-derive the patch.");
    return 1;
}

var call = callSites[0];

// Idempotence: the four instructions we inject directly precede the call.
var prev = call.Previous;
if (prev != null && prev.OpCode == OpCodes.Add
    && prev.Previous is { } p2 && p2.OpCode == OpCodes.Ldc_I4_S && (sbyte)p2.Operand == 64
    && p2.Previous is { } p3 && p3.OpCode == OpCodes.Add
    && p3.Previous is { } p4 && p4.OpCode == OpCodes.Dup)
{
    Console.WriteLine("Already patched - nothing to do.");
    return 0;
}

// Stack on entry to the call: [..., this, list, requiredLength]. Rewrite the
// top of the stack: r -> 2r+64.
il.InsertBefore(call, il.Create(OpCodes.Dup));
il.InsertBefore(call, il.Create(OpCodes.Add));
il.InsertBefore(call, il.Create(OpCodes.Ldc_I4_S, (sbyte)64));
il.InsertBefore(call, il.Create(OpCodes.Add));
body.MaxStackSize += 2;

module.Write(output);
Console.WriteLine("PATCHED: staging request r -> 2r+64 in GpuUpdaterStaged`1.CompleteUpdate");
Console.WriteLine("  in : " + input);
Console.WriteLine("  out: " + output);
return 0;
