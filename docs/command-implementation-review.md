# Command implementation review

**Date:** 2026-07-26
**Scope:** every command documented in [`docs/commands/`](./commands/) versus what the server
actually returns, plus the new-capability opportunities in
[`update-analysis/api-diff.md`](./update-analysis/api-diff.md).
**Companion documents:** [`PLAN.md`](./PLAN.md) (claims all commands ✅),
[`update-analysis/clrmd-4-upgrade-plan.md`](./update-analysis/clrmd-4-upgrade-plan.md).

---

## 0. Headline

`PLAN.md`'s status tables mark every row ✅ — all 24 of them. Measured against a real dump, of the
23 tools those rows cover, **7 are genuinely correct, 10
are degraded, and 6 return wrong or empty output for the case they exist to handle.** Every tool
returns *something*, which is why a green checklist was plausible — but "returns a table" and
"returns the right table" came apart in six places.

The two that matter most:

- **`gcroot` returns an empty table for essentially every object a user would ask about.** It
  matches only roots that point *directly* at the target address. Confirmed on a dump where the
  target was reachable in two hops: the tool returned zero rows; a 12-line BFS found the path in
  4 ms.
- **`printexception` found 0 of the 5 exception objects present in the dump**, because it only
  reads `ClrThread.CurrentException` — null once an exception is caught, which is the normal
  state of a collected dump.

Both are the flagship reason someone opens a dump at all ("what is keeping this alive?", "what
threw?"). Neither is a ClrMD limitation; the APIs to do it properly were present in 3.x and are
still present in 4.x.

Separately, the ClrMD 4 upgrade unlocked **five genuinely new capabilities** worth building on,
and the existing upgrade plan's single named "best functional win"
(`ClrHeap.EnumerateAdditionalRoots()`) turns out to be **already covered** by
`EnumerateRoots()` — adopting it as written would double-count roots. See [§5](#5-clrmd-4-what-is-actually-new-and-worth-using).

---

## 1. How this was verified

Nothing here is read off the source alone; every claim below was executed.

| Method | What it established |
|---|---|
| A purpose-built .NET 10 target app + `dotnet-dump collect --type Full` (6.0 GB) | A dump containing, by construction: a deep reference chain behind a static, a held monitor, background + threadpool + finalizer threads, a caught nested exception, a distinctive deep stack |
| A harness referencing `DotNetDump.Core` directly, driving all 20 analyzer entry points and the `MarkdownFormatter` | The exact Markdown an MCP client would receive |
| Raw ClrMD calls against the same dump, side by side with the analyzer output | Ground truth — what the data *is* versus what the tool reports |
| `MetadataLoadContext` reflection over ClrMD **3.1.512801 and 4.0.732401** | Which members are new in v4 versus present all along (the upgrade plan attributes several gaps to ClrMD that were never ClrMD's fault) |
| `CommandAttribute` reflection over `Microsoft.Diagnostics.ExtensionCommands.dll` from the installed `dotnet-dump` 9.0.652701 | The real managed command set of `dotnet dump analyze`: **60 commands**, mostly undocumented here |

The target app was shaped to make failures visible rather than to be representative: e.g. `Leaf`
objects sit behind `static Roots.StaticHolder → Holder → List<Leaf> → Leaf[] → Leaf`, so a
direct-match `gcroot` must fail and a transitive one must succeed.

Reproduction detail worth keeping: on this dump **no `StaticVar` root points at a user object at
all**. The one `StaticVar` root points at the `System.Object[]` that *stores* the statics. So even
an object held directly by a static field is only reachable transitively — direct-match `gcroot`
cannot work for user objects even in the simplest case.

---

## 2. Scorecard

Legend: ✅ correct · ⚠️ works but degraded/incomplete · ❌ wrong or empty for its primary use case

| # | Documented command | Tool | Verdict | One-line finding |
|---|---|---|:---:|---|
| 1 | [`gcroot`](./commands/gcroot.md) | `GcRoot` | ❌ | Direct-match only; empty for transitively-held objects, i.e. nearly always. Also double-counts stack roots when it does match |
| 2 | [`printexception`](./commands/printexception.md) | `PrintException` | ❌ | Only `CurrentException`; found 0 of 5 exceptions in the dump. Documented `pe [<address>]` parameter does not exist |
| 3 | [`threadstate`](./commands/threadstate.md) | `ThreadState` | ❌ | GC Mode always `Unknown`, Locks always `-1`, and Background/GC/Aborted/ThreadPool/Apartment are hardcoded constants |
| 4 | [`dumpmd`](./commands/dumpmd.md) | `DumpMd` | ❌ | Throws for any method whose declaring type has no live instance (verified: `Program.Main`). `GetMethodByHandle` resolves it in 0 ms |
| 5 | [`eeheap`](./commands/eeheap.md) | `EeHeap` | ❌ | Gen column is `-1` for every segment on .NET 5+. `-loader` not implemented at all |
| 6 | [`dumpassembly`](./commands/dumpassembly.md) | `DumpAssembly` | ❌ | Expects `ImageBase` while documented and described as an assembly id; the real `AssemblyAddress` throws |
| 7 | [`dumpstack`](./commands/dumpstack.md) | `DumpStack` | ⚠️ | Frames render as `<absolute-dll-path>!BareMethodName` — declaring type lost, path noise added |
| 8 | [`eestack`](./commands/eestack.md) | `EeStack` | ⚠️ | Byte-identical to `ClrStack` (same call, different default `maxFrames`). No merged/parallel-stacks view |
| 9 | [`syncblk`](./commands/syncblk.md) | `SyncBlk` | ⚠️ | Empty table while a monitor *was* held — it was a thin lock. Thin locks invisible; `-all` missing |
| 10 | [`gchandles`](./commands/gchandles.md) | `GcHandles` | ⚠️ | No statistics mode (SOS's default), no kind filter; drops RefCount / IsStrong / Dependent / AppDomain |
| 11 | [`threadpool`](./commands/threadpool.md) | `ThreadPool` | ⚠️ | Six fields only; drops CPU utilisation, completion-port counts, retired threads, hill-climbing log |
| 12 | [`clrmodules`](./commands/clrmodules.md) | `ClrModules` | ⚠️ | "System module" test is a substring match on the full **path**, so any assembly named `Microsoft.*`/`System.*` is hidden by default |
| 13 | [`dumpmodule`](./commands/dumpmodule.md) | `DumpModule` | ⚠️ | `AssemblyId` is fabricated (= ImageBase), `IsFileLayout` hardcoded false, type count is a 10k-object heap *sample* (reported ~4, actual 6) |
| 14 | [`name2ee`](./commands/name2ee.md) | `Name2Ee` | ⚠️ | The documented `module!Type.Method` form throws; and any namespaced type emits a spurious `Method:` line |
| 15 | [`dumpclass`](./commands/dumpclass.md) | `DumpClass` | ⚠️ | Parameter is described as an EEClass address but is used as a MethodTable; static field values never read |
| 16 | [`dumpmt`](./commands/dumpmt.md) | `DumpMt` | ⚠️ | Interface/Abstract/Sealed hardcoded `false`; the Interfaces section is dead formatter code |
| 17 | [`verifyheap`](./commands/verifyheap.md) | `VerifyHeap` | ⚠️ | Correct pass/fail, but drops `ObjectCorruptionKind` and reports `Offset` as a hardcoded `0` |
| 18 | [`dumpheap`](./commands/dumpheap.md) | `DumpHeap` | ✅ | Correct. No MethodTable column (blocks pivoting to `dumpmt`); no `-min`/`-max`/`-mt`/`-strings` |
| 19 | [`dumpobj`](./commands/dumpobj.md) | `DumpObj` | ✅ | Good — strings, arrays, per-field values all correct |
| 20 | [`ip2md`](./commands/ip2md.md) | `Ip2Md` | ✅ | Correct and complete, including signature and JIT state |
| 21 | [`clrstack`](./commands/clrstack.md) | `ClrStack` | ✅ | Grouping is genuinely good output. No thread filter, no `-a`/`-l`/`-p`/`-r` |
| 22 | [`clrthreads`](./commands/clrthreads.md) | `ClrThreads` | ✅ | Correct as far as it goes; no thread name, GC mode, or lock count |
| 23 | *(extra, not a SOS command)* | `ListObjects` | ✅ | Correct; substring type filter only |
| 24 | [`help`](./commands/help.md), [`sos`](./commands/sos.md) | — | n/a | Debugger meta-commands. Correctly absent — MCP tool discovery replaces `help`, and ClrMD *is* SOS |

**7 ✅ · 10 ⚠️ · 6 ❌ · 2 n/a**

---

## 3. The six broken commands, with evidence

### 3.1 `gcroot` — empty for any object not pointed at by a root ❌

`HeapAnalyzer.GetGCRoots` (`HeapAnalyzer.cs:76`) filters roots by `root.Object.Address ==
targetAddress`. `gcroot` exists to answer "what chain keeps this alive"; a chain of length > 1 is
the normal case.

Measured, on an object reachable as `List<Leaf> → Leaf[] → Leaf`:

```
gcroot(Leaf)   -> 0 rows
gcroot(Holder) -> 0 rows          (Holder is held by a static field)

ground truth: Leaf   heapRoots=0 stackRoots=0 additionalRoots=0
              Holder heapRoots=0 stackRoots=0 additionalRoots=0
```

A reverse BFS over `ClrObject.EnumerateReferences()` from all roots found it immediately:

```
BFS over 1083 objects in 4 ms; found Leaf=True
PATH (root -> target):
  13A611F50 (System.Collections.Generic.List<Leaf>)  <-- ROOT[Stack]@16D24DBE8
  13A630CA8 (Leaf[])
  13A611F10 (Leaf)
```

Two further defects in the same method:

- **Stack roots are double-counted.** `EnumerateRoots()` already includes them — verified:
  `EnumerateRoots total=114, per-thread stack roots=81, of those already in EnumerateRoots=81`.
  The second loop over `thread.EnumerateStackRoots()` (`HeapAnalyzer.cs:96`) re-adds all 81.
- **`RootName` is always null** except for stack roots, so the formatter's Name column is blank.

**Fix:** replace with a bounded reverse BFS over `EnumerateReferences`, return the *path*, and
delete the second loop. Root-kind labelling gets better for free in v4 — see §5.

### 3.2 `printexception` — cannot see exceptions that are not in flight ❌

`ThreadAnalyzer.GetThreadExceptions` reads only `ClrThread.CurrentException`. In the test dump the
exception had been caught and logged — the overwhelmingly common shape of a real dump:

```
threads with CurrentException = 0

exception objects on the heap = 5:
  13A665AE0 System.ApplicationException: "outer-boom" inner=System.InvalidOperationException hresult=80131600
  13A665A20 System.InvalidOperationException: "inner-boom"                                   hresult=80131509
  13A6080A8 System.OutOfMemoryException      13A608120 System.StackOverflowException
  13A608198 System.ExecutionEngineException
```

The tool printed `**No exception on this thread.**` nine times. `docs/commands/printexception.md`
documents `pe [<address>]`; the tool has **no address parameter**. Note the nested exception
walking, `HResult`, and inner-exception formatting are all already implemented and correct — only
the *discovery* path is wrong.

**Fix:** add an optional `address` parameter (`heap.GetObject(addr).AsException()`), and a
heap-scan mode filtering on `ClrType.IsException` — that second mode is the `dumpexceptions`
extension command, ~15 lines given the existing `BuildExceptionDetails`.

### 3.3 `threadstate` — fabricated columns ❌

Every interesting column is a constant. `ThreadAnalyzer.cs:139-147` blames ClrMD v3 in comments;
reflection over **both** 3.1.512801 and 4.0.732401 shows all the data was always there.

Tool output vs. ground truth, same threads:

| | Tool says | Actually available |
|---|---|---|
| GC Mode | `Unknown` | `ClrThread.GCMode` → `Preemptive` |
| Locks | `-1` for every thread | `LockCount` is `0xFFFFFFFF` = *unknown*; `(int)` cast turns it into a plausible-looking `-1` |
| Background / ThreadPool / Aborted / Unstarted / Apartment / IsGC | hardcoded | `ClrThread.State` (`ClrThreadState` flags: `TS_Background`, `TS_TPWorkerThread`, `TS_CompletionPortThread`, `TS_Aborted`, `TS_AbortRequested`, `TS_AbortInitiated`, `TS_Unstarted`, `TS_Dead`, `TS_InSTA`, `TS_InMTA`, `TS_GCSuspendPending`, `TS_DebugSuspendPending`, …) and `ClrThread.IsGc` |

Ground truth distinguished them cleanly — background threads `State=33690144`, threadpool threads
`State=50467360`, finalizer `State=135712`. Only the `Finalizer` flag is real today.

`LockCount` deserves its own note: `0xFFFFFFFF` means "not available", so the honest rendering is
`-` or `unknown`, not `-1`.

### 3.4 `dumpmd` — throws for most real inputs ❌

`MetadataAnalyzer.GetMethodDesc` (`MetadataAnalyzer.cs:48`) scans
`foreach module → foreach heap object → foreach method`, matching on `MethodDesc`. A method is
findable only if its declaring type has a **live instance on the heap** — excluding static
classes, entry points, most controllers and services.

```
Program.Main MethodDesc = 10AF0F810
runtime.GetMethodByHandle          -> Program.Main in 0 ms
MetadataAnalyzer.GetMethodDesc     -> THREW after 44 ms: "No method found for MethodDesc 10AF0F810"
```

44 ms on a tiny heap; the cost is `O(modules × heap objects × methods per type)` and scales with
the dump. `ClrRuntime.GetMethodByHandle(ulong)` — **present in 3.1.512801 and 4.0.732401** — is
the correct one-line implementation.

### 3.5 `eeheap` — the Gen column is always `-1` ❌

`HeapAnalyzer.GetHeapSegments` maps `GCSegmentKind` to a generation number, but the full enum is
`Generation0, Generation1, Generation2, Large, Pinned, Frozen, Ephemeral`, and a modern
regions-based GC reports the last two. Measured on .NET 10:

```
109294008-109297050  Kind=Frozen     -> reported "Gen -1"  | G0len=0      G1len=0  G2len=12360
13A608000-13A674268  Kind=Ephemeral  -> reported "Gen -1"  | G0len=442936 G1len=24 G2len=24
14A608000-14A608018  Kind=Large      -> reported "Gen -1"  (labelled LOH correctly)
152608000-15260A010  Kind=Pinned     -> reported "Gen -1"  (labelled POH correctly)
```

So on any .NET 5+ dump the Gen column is pure noise, and the two most interesting segments are
labelled `Gen -1`. Meanwhile each `ClrSegment` carries `Generation0/1/2` as `MemoryRange`s,
plus `CommittedMemory`, `ReservedMemory` and `SubHeap` — none surfaced. Committed vs. reserved is
usually the number the user actually wants (`committed=540672` vs `reserved=267878400` on the
ephemeral segment here).

`docs/commands/eeheap.md` documents `eeheap [-gc] [-loader]`. Only `-gc` exists. The loader side is
available: `ClrRuntime.EnumerateClrNativeHeaps()` (66 entries),
`ClrAppDomain.EnumerateLoaderAllocatorHeaps()`, `ClrRuntime.EnumerateJitManagers()` (1),
`ClrModule.EnumerateThunkHeap()`.

### 3.6 `dumpassembly` — rejects real assembly addresses ❌

The tool parameter is described as "The hex address of the assembly (AssemblyId)", and
`ModuleAnalyzer.cs:91` comments that `AssemblyId` is unavailable in ClrMD — but
`ClrModule.AssemblyAddress` exists in **both** 3.x and 4.x.

```
real ClrModule.AssemblyAddress = C475702A0
GetAssemblyDetails(C475702A0) -> THREW: "No module found with address C475702A0"
GetAssemblyDetails(102FF8000) -> works   (ImageBase, i.e. not what the description asks for)
```

Any agent that obtains an assembly address from SOS output, or from `dumpmodule`'s own real
assembly address, gets an error. Match on `AssemblyAddress` (accepting `ImageBase` too, for
compatibility) and report the real value.

---

## 4. Cross-cutting issues

These affect every tool and are cheap to fix.

### 4.1 Hex addresses with an `0x` prefix are rejected

All eight address-taking tools (`GcRoot`, `DumpObj`, `DumpMt`, `DumpMd`, `DumpClass`,
`DumpModule`, `DumpAssembly`, `Ip2Md`) use
`ulong.TryParse(address, NumberStyles.HexNumber, …)`, which does not accept `0x`:

```
OK    "13A611F10"          OK    "000000013A611F10"      OK    "13a611f10"
FAIL  "0x13A611F10"        FAIL  "00000001`3A611F10"     (WinDbg backtick form)
```

An LLM copying an address out of prose will write `0x…` more often than not, and the failure
surfaces as the unhelpful `Error: Invalid address format`. A shared `ParseAddress` helper that
strips `0x`/`0X`, backticks and whitespace fixes all eight call sites at once.

### 4.2 The heap-statistics cache never survives a call

`HeapAnalyzer._cachedStats` is built on first use — but `HeapAnalyzer` is registered
`AddTransient` (`Program.cs:22`) and `DumpAnalyzerTools` is not registered at all, so the MCP SDK
activates a fresh instance per tool invocation. The cache therefore has **no effect across
calls**: `DumpHeap(offset: 50)` re-enumerates the entire heap from scratch, as does every
subsequent page. Given that heap enumeration is the reason the README recommends a 10-minute
client timeout, paging is the exact scenario where this hurts.

Fix: hold the cache on the singleton `IDumpContext` (invalidated in `Unload`/`Load`), or register
the analyzers as singletons. Either way, invalidation must be wired to dump load — a transient
lifetime is currently the only thing preventing a stale-cache bug after `load_dump`.

### 4.3 Formatter issues

| Location | Issue |
|---|---|
| `FormatHeapStatistics` | `HeapStatItem.MethodTable` is populated but never rendered — the agent cannot pivot from a `dumpheap` row to `dumpmt`/`ListObjects` without a second lookup |
| `FormatModules` | `{item.Size:X}` — bare hex (`1A00`) in a column next to decimal-with-commas sizes elsewhere, and with no `0x` marker |
| `FormatDetailedStacks` | `{ModuleName}!{MethodName}` where `MethodName` is `ClrMethod.Name` only. Renders `/usr/local/share/dotnet/…/System.Private.CoreLib.dll!Sleep` instead of `System.Threading.Thread.Sleep(Int32)` — more tokens, less information. `ClrStackFrame.ToString()` (used by `ClrStack`) is already correct; and `ClrStackFrame.FrameName` exists for runtime frames |
| `FormatDetailedStacks` | `### Thread {threadNum}` is a row counter, so the header reads `Thread 1: Managed ID 10` |
| `FormatMethodTable` | The `Interfaces` block is unreachable — `MethodTableInfo.Interfaces` is never populated, though `ClrType.EnumerateInterfaces()` exists |
| `FormatHeapVerification` | `Offset` column is always `0` (hardcoded in the model) and `ObjectCorruptionKind` — a 15-value enum naming the exact defect — is discarded into `ToString()` |
| `FormatGCRoots` | Name column blank for all non-stack roots |

### 4.4 `IsSystemModule` hides first-party code

`ModuleAnalyzer.cs:49` classifies a module as "system" if its **full path** contains `System.` or
`Microsoft.`. So `Microsoft.MyCompany.Api.dll` — or any assembly under a directory containing
`Microsoft.`— is hidden from `clrmodules` by default. Match on the assembly's simple name, and
prefer the runtime directory as the signal.

### 4.5 Tests assert nothing

All 24 tests in `IntegrationTests.cs` open with `if (!File.Exists(_dumpPath)) return;` against
`../../../../../dumps/core_20251212_112511`, which does not exist in the repo. They pass without
executing. Every ❌ in §3 would have been caught by one assertion against a committed fixture —
`gcroot` returning a non-empty result for a known-rooted object, for instance. This is step 7 of
the upgrade plan's sequencing table and is still open; it is the highest-leverage item in this
document, because it is what makes the rest stay fixed.

### 4.6 Documentation drift

- `docs/commands/clrstack.md:20` links to `../clrstack-example.md`, which does not exist.
- `PLAN.md`'s status table marks all 23 commands ✅ with notes like "Detailed thread state with GC
  mode and flags" for a tool that reports `Unknown`. It should be corrected against §2, or it
  will keep being cited as evidence of completeness.
- The command docs are written as ClrMD *sketches* ("Assuming address is …", `0x7FF...`) rather
  than as a description of what this server does. They are useful as implementation notes but do
  not document the tools, their real parameter names, or their limits. Worth splitting: keep the
  SOS/ClrMD reference, and add a per-tool contract doc generated from the `[Description]`
  attributes.

---

## 5. ClrMD 4: what is actually new, and worth using

First, a correction to `clrmd-4-upgrade-plan.md` §4.4, which names
`ClrHeap.EnumerateAdditionalRoots()` as "the best single functional win available in v4":

```
EnumerateRoots total=114
EnumerateAdditionalRoots=21, of those already in EnumerateRoots=21
```

`EnumerateRoots()` already includes every additional root, so adding a second enumeration would
duplicate all 21 — the same defect §3.1 already has with stack roots. The real win in v4 for
`gcroot` is different and smaller: **`ClrRootKind` gained `StaticVar` and `ThreadStaticVar`**, so
static and thread-static roots can now be *labelled* rather than lumped in. Confirmed in the
root histogram: `StrongHandle=17, PinnedHandle=1, Stack=81, StaticVar=1, ThreadStaticVar=20`.

Also worth recording: the upgrade plan's §4.5 list of "fabricated fields" is correct but
incomplete. It flags `ThreadAnalyzer` and `MetadataAnalyzer`; it misses `ModuleAnalyzer` —
`AssemblyId` (§3.6), `IsFileLayout` (`ClrModule.Layout` exists: `Flat` on the test module), and
the sampled `TypeCount` (`EnumerateTypeDefToMethodTableMap()` gives the exact answer). None of
those three are ClrMD limitations either.

### Genuinely new in 4.0.732401 and worth building on

Verified new by reflecting over both packages:

| New API | What it enables here | Value |
|---|---|---|
| `ClrObject.ReadField<T>(ClrInstanceField)` and the `ReadObjectField`/`ReadStringField`/`ReadValueTypeField` overloads | `HeapAnalyzer.ReadPrimitiveValue` converts a `ClrInstanceField` it already holds into a *name string* and makes ClrMD look it up again, per field per object — and returns `null` outright for unnamed fields. The new overloads remove both the round-trip and that failure mode | **High** — pure win, `dumpobj` is a hot path |
| `ClrRootKind.StaticVar` / `ThreadStaticVar` | Root labelling in a rewritten `gcroot` | Medium |
| `ClrHeap.DynamicAdaptationMode` | Whether DATAS is on — essential context for reading heap sizes on .NET 9+. `null` on the test dump (off) | Medium, and cheap: one line in `eeheap` |
| `ClrThread.AllocationContext` | Per-thread allocation context; the missing half of an allocation-rate story. Non-empty for 8 of 9 threads here | Medium |
| `ClrModule.EnumerateTypesWithStaticFields()` | Direct answer to "which statics could be holding this?" — the companion to a fixed `gcroot`. 4 types on the test module | Medium |
| `ClrRuntime.StressLog` / `TryGetStressLog(out …)` + the `StressLogs` namespace | Backs a `dumplog`-style tool; the only way to see runtime-internal event history in a dump | Medium, new tool |
| `DataReaders.Implementation.IMemoryRegionReader`, `MemoryRegion`, `ModuleSegment`, `IProcessInfoProvider`, `IThreadInfoReader` | Native memory-region enumeration — what a `maddress`-style "where did the non-managed memory go" tool needs. Previously not expressible | Medium, new tool, more work |
| `DataTargetLimits` | Parse-time bounds for corrupt/hostile dumps. Only matters if untrusted dumps are in scope; defaults are sane | Low |
| `IDumpInfoProvider.IsCreatedByDotNetRuntime` | Backs `crashinfo` | Low |
| `ClrException.EnumerateExceptionStackTrace()` | Streaming form of `StackTrace`; marginal, `BuildExceptionDetails` walks it all anyway | Low |

**`DataTargetOptions.UseLockFreeMemoryMapReader` should stay off.** It is not thread-safe and
`DumpContext` shares one `ClrRuntime` process-wide. The upgrade plan flags this correctly;
repeating it here because a faster heap walk is tempting for exactly the paging problem in §4.2,
and it is the wrong fix for it.

### Not new, but wrongly assumed unavailable

Present in **3.1.512801 and 4.0.732401** alike, and each one is currently worked around with a
hardcoded constant or a heap scan:

`ClrRuntime.GetMethodByHandle` · `ClrType.TypeAttributes` · `ClrType.EnumerateInterfaces` ·
`ClrType.IsFinalizable` / `ContainsPointers` / `ComponentSize` · `ClrThread.GCMode` / `IsGc` /
`State` · `ClrModule.AssemblyAddress` / `Layout` / `EnumerateTypeDefToMethodTableMap` ·
`ClrHeap.SubHeaps` / `EnumerateFinalizableObjects` / `EnumerateFinalizerRoots` /
`FullyVerifyObject` / `IsObjectCorrupted` / `GetSegmentByAddress` /
`FindNextObjectOnSegment` · `ClrSegment.CommittedMemory` / `ReservedMemory` / `Generation0..2` ·
`ClrObject.GetThinLock` / `EnumerateReferences` · `ClrHandle.ReferenceCount` / `IsStrong` /
`Dependent` · `ClrRuntime.EnumerateClrNativeHeaps` / `EnumerateJitManagers` / `AppDomains` ·
`ClrStackFrame.FrameName` · `ObjectCorruption.Kind` / `Offset` · `ClrStaticField.Read*`.

---

## 6. Commands that exist in `dotnet dump analyze` but not here

`docs/commands/` documents 22 commands, all of which are **native SOS** commands. The managed
extension command set — 60 commands, enumerated from
`Microsoft.Diagnostics.ExtensionCommands.dll` in dotnet-dump 9.0.652701 — is almost entirely
absent from both the docs and the server:

```
analyzeoom  assemblies  crashinfo  d/da/db/dc/dd/dp/dq/du/dw  dumpasync
dumpconcurrentdictionary  dumpconcurrentqueue  dumpexceptions  dumpgen  dumpheap  dumphttp
dumpobjgcrefs  dumprequests  dumpruntimetypes  dumpstackobjects  eeheap  ephrefs  ephtoloh
finalizequeue  findpointersin  gcheapstat  gcroot  gctonative  gcwhere  help  listnearobj
loadsymbols  logclose  logging  logopen  maddress  modules  notreachableinrange  objsize
parallelstacks  pathto  registers  runtimes  setclrpath  setsymbolserver  sizestats  sos
sosflush  sosstatus  taskstate  threadpool  threadpoolqueue  threads  timerinfo
traverseheap  verifyheap  verifyobj
```

Of these, only `dumpheap`, `eeheap`, `gcroot`, `threadpool`, `verifyheap` and `assemblies`
(as `clrmodules`) have any counterpart here. Ranked by diagnostic value per unit of work:

**Tier 1 — high value, small and well-supported by ClrMD**

| Command | Implementation | Why |
|---|---|---|
| `dumpexceptions` | `EnumerateObjects().Where(o => o.Type.IsException)` + existing `BuildExceptionDetails` | Fixes §3.2 outright. Found all 5 exceptions in the test dump |
| `finalizequeue` | `EnumerateFinalizableObjects()` / `EnumerateFinalizerRoots()` | 46 finalizable objects here; a first-line answer for leak and shutdown-hang analysis |
| `dso` / `dumpstackobjects` | `thread.EnumerateStackRoots()` grouped per thread | "What is this thread holding?" — already half-written inside `GetGCRoots` |
| `gcwhere` | `heap.GetSegmentByAddress(addr)` + `segment.GetGeneration(addr)` | One call; tells you which generation and segment an address lives in |
| `verifyobj` | `heap.FullyVerifyObject(addr, out …)` / `IsObjectCorrupted` | Both APIs already exist and are unused |
| `gcheapstat` | Sum the per-segment `Generation0/1/2` ranges | This is what `eeheap`'s Gen column is failing to be (§3.5) |
| `objsize` / `pathto` | The same BFS that fixes `gcroot` | Retained size and "path from A to B" fall out of §3.1's fix for free |

**Tier 2 — high value, more work**

- `dumpasync` — the single most-requested modern command (async state machines, hung
  `await`s in ASP.NET). Requires walking state-machine objects and continuation chains; no ClrMD
  helper, so this is real work but disproportionately valuable for server dumps.
- `threadpoolqueue`, `timerinfo`, `taskstate` — need field-level walks of internal
  `ThreadPool`/`Timer` structures.
- `maddress`, `gctonative` — native memory attribution; newly expressible via the v4
  `IMemoryRegionReader` / `MemoryRegion` types.
- `dumplog` — via the new `ClrRuntime.TryGetStressLog`.
- `listnearobj`, `dumpgen`, `sizestats`, `ephrefs`, `dumpruntimetypes` — each small; grouped
  here only because they are niche.

**Tier 3 — deliberately skip**

`d`/`db`/`dc`/`dq`/`du`/`dw`, `registers`, `logging`/`logopen`/`logclose`, `setclrpath`,
`setsymbolserver`, `loadsymbols`, `runtimes`, `sosflush`, `sosstatus`, `help`, `sos`. These are
debugger-session plumbing or raw memory dumps; an MCP tool surface is the wrong shape for them,
and symbol/DAC configuration is already handled by `entrypoint.sh` and the
`DOTNETDUMP_SYMBOL_*` environment variables.

Also worth noting: real `dotnet-dump` has `parallelstacks` (`pstacks`) as its own command. That is
what `eestack` is currently mis-implemented as (§2 row 8) — so the honest move is to make
`EeStack` a genuine merged-stack tree, or drop it and rename `ClrStack`'s grouping behaviour.

---

## 7. Recommended order of work

Grouped so that each block leaves the tree in a coherent state.

**Block 1 — stop shipping wrong answers** (small, mechanical, high impact)

1. Rewrite `GcRoot` as a bounded reverse BFS returning paths; delete the duplicate stack-root
   loop (§3.1).
2. `DumpMd` → `ClrRuntime.GetMethodByHandle` (§3.4).
3. `PrintException` → add an `address` parameter and a heap-scan mode (§3.2); this is also
   `dumpexceptions`.
4. `ThreadState` → real `GCMode`, `IsGc`, and flags decoded from `ClrThread.State`; render
   `LockCount == 0xFFFFFFFF` as unknown (§3.3).
5. `EeHeap` → handle `Frozen`/`Ephemeral`, and report committed/reserved plus the per-segment
   generation ranges (§3.5).
6. `DumpAssembly` → match on `ClrModule.AssemblyAddress` (§3.6).
7. Shared `ParseAddress` helper accepting `0x`, backticks and whitespace (§4.1).

**Block 2 — make the fixes stick**

8. Commit a small dump fixture (or generate one in CI) and replace the silent
   `if (!File.Exists) return;` with `Assert.Skip` (§4.5). Add one assertion per ❌ above.
9. Correct `PLAN.md`'s status table; fix the dead `clrstack-example.md` link (§4.6).

**Block 3 — output quality**

10. MethodTable column in `dumpheap`; real declaring type in `DumpStack` frames; real
    `ObjectCorruptionKind`/`Offset` in `VerifyHeap`; populate `dumpmt`'s interfaces and
    `TypeAttributes` flags; drop `dumpclass`'s bogus field counting and read static values;
    fix `IsSystemModule`; fix `dumpmodule`'s type count and `IsFileLayout`; fix `name2ee`'s
    method form (§4.3, §4.4, §2).
11. Move the heap-stats cache onto `IDumpContext` with load-time invalidation (§4.2).
12. Adopt the `ClrInstanceField` read overloads in `ReadPrimitiveValue` (§5).

**Block 4 — new capability**

13. Tier 1 commands from §6 — `finalizequeue`, `dso`, `gcwhere`, `verifyobj`, `gcheapstat`,
    then `objsize`/`pathto` on the BFS from step 1.
14. `DynamicAdaptationMode` and `AllocationContext` into the heap/thread tools; a real
    `parallelstacks` for `EeStack`.
15. Then, as separate pieces of work: `dumpasync`, `maddress`, `dumplog`.

Blocks 1 and 2 together are the difference between a tool surface that looks complete and one
that is. Nothing in Block 1 needs an API that ClrMD 4 introduced — six of the seven items were
fixable before the upgrade too.
