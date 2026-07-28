# ClrMD 3.1.512801 → 4.0.732401 upgrade plan

**Status:** ✅ **upgrade applied and verified.** See [§0](#0-what-was-done) for what shipped.
**Date:** 2026-07-26
**Companion document:** [`api-diff.md`](./api-diff.md) — full generated public API diff.

---

## 0. What was done

Steps 4.1, 4.2, 4.3 and the README update were applied. Sections 4.4 and 4.5 were
deliberately left out of scope and remain open tickets.

| File | Change |
|---|---|
| `src/DotNetDump.Core/DotNetDump.Core.csproj` | ClrMD `3.1.512801` → `4.0.732401` |
| `src/exploration/DotNetDumpExplorer/DotNetDumpExplorer.csproj` | ClrMD `3.1.512801` → `4.0.732401` |
| `src/DotNetDump.Core/DumpContext.cs` | Added `CreateOptions()`; `LoadDump` now takes explicit `DataTargetOptions`. Offline by default, opt in via `DOTNETDUMP_SYMBOL_PATHS` / `DOTNETDUMP_SYMBOL_CACHE` |
| `src/Dockerfile` | Publish framework `net9.0` → `net10.0` |
| `README.md` | New "Symbol Resolution" section; documents the env vars and the `_NT_SYMBOL_PATH` removal |

### Verification performed

A real dump was generated for this purpose (`dotnet-dump collect --type Full`, 6.0 GB, from
a .NET 10 target process allocating 50k strings + 50k `StringBuilder`s, with a held lock, a
background thread and a caught nested exception).

1. **Build** — all 4 projects, all 3 TFMs: 0 errors. Warning counts identical to the
   3.1.512801 baseline (4× `CS8600`, 48× `CS8602`, all pre-existing).
2. **Behavioural diff** — a harness exercised all 20 analyzer entry points across
   `HeapAnalyzer`, `ThreadAnalyzer`, `ModuleAnalyzer` and `MetadataAnalyzer` against the same
   dump on old-code/v3 and new-code/v4. Output was **byte-identical** apart from the load
   timing line. Zero exceptions on either version.
3. **Performance** — v4 is faster, not slower: dump load `1017 ms → 260 ms`, full analyzer
   sweep `1787 ms → 1165 ms`.
4. **Offline load** — confirmed the new default resolves the DAC locally with
   `SymbolPaths = []`, i.e. no symbol server contacted. This was the primary risk in §3.1.
5. **Symbol opt-in** — all four config permutations verified: unset → offline;
   `DOTNETDUMP_SYMBOL_PATHS` set → paths parsed and trimmed with cache at `/dumps/.symcache`;
   `DOTNETDUMP_SYMBOL_CACHE` set → honoured; whitespace-only → falls back to offline.
6. **Test suite** — `dotnet test` on net8.0, net9.0 and net10.0: **24/24 passed** on each,
   with the generated dump temporarily linked in as the fixture (see the caveat below).

### Caveat that still stands

The dump used for verification was generated ad hoc and is **not committed** — the temporary
fixture link was removed afterwards, so the repository is unchanged in that respect. That
means §5 is still open: on a clean checkout those 24 tests go back to passing without
asserting anything. The upgrade itself is verified; the *ongoing* regression safety is not.
Steps 5.1 and 5.2 remain worth doing.

Not re-verified, because it needs a Linux container build: the offline behaviour inside the
actual Docker image with egress blocked (§5.4). The mechanism is confirmed on macOS and the
code path is platform-independent, but the containerised run is untested.

---

## Headline finding

**The upgrade compiles with zero source changes.** Every project builds against
4.0.732401 on all three target frameworks with no new errors and no new warnings.

That makes this a *behavioural* migration, not a source migration. The work is not in
fixing compiler errors — there are none. It is in the four runtime behaviour changes in
[§3](#3-behavioural-changes-that-do-affect-us), the most important of which is that
**ClrMD 4 now reaches out to `https://msdl.microsoft.com/download/symbols` by default**,
where ClrMD 3 read `_NT_SYMBOL_PATH` instead.

---

## 1. How this was verified

The published [migration guide](https://github.com/microsoft/clrmd/blob/main/doc/Migrating4.md)
was the starting point, but it does not match the shipped 4.0.732401 package in several
places (see [§6](#6-where-the-official-migration-guide-is-wrong)). Everything below was
verified directly against the two assemblies rather than taken from the guide:

| Method | What it established |
|---|---|
| `MetadataLoadContext` reflection over both `lib/netstandard2.0` assemblies | The authoritative public API diff (`api-diff.md`) |
| Building all 4 projects against 4.0.732401 on net8.0/net9.0/net10.0 | Zero compile errors; warning counts identical to baseline |
| Running a probe that instantiates `DataTargetOptions` | Actual runtime defaults, including the msdl symbol path |
| Reading the `#US` (user string) metadata heap of both assemblies | `_NT_SYMBOL_PATH` present in v3, **absent** in v4 |
| Reading `project.assets.json` for the upgraded projects | The new transitive dependency closure |

Build result, all projects, all TFMs:

```
DotNetDump.Core        Build succeeded.   0 errors
DotNetDump.Server      Build succeeded.   0 errors
DotNetDump.Tests       Build succeeded.   0 errors
DotNetDumpExplorer     Build succeeded.   0 errors
```

Warning baseline is unchanged — 4× `CS8600` and 48× `CS8602` on both 3.1.512801 and
4.0.732401. Those are pre-existing nullable warnings in `IntegrationTests.cs` and the
explorer's `Program.cs`, unrelated to ClrMD.

> Also observed while testing: `src/dotnet-dump-mcp-server.sln` fails to load with
> `MSB5009` (a solution-folder nesting GUID that does not resolve). This is **pre-existing**
> and reproduces on the current `main` — it is not caused by the upgrade. Projects must be
> built individually until it is fixed. Worth a separate ticket.

---

## 2. API changes, and why none of them break us

Only **one type was removed** in the entire public surface:
`Microsoft.Diagnostics.Runtime.CustomDataTarget`. We never used it.

The changes that *would* be breaking, and our exposure to each:

| Breaking change | Used in this project? | Why we are unaffected |
|---|---|---|
| `CustomDataTarget` deleted | No | We only call `DataTarget.LoadDump(path)` |
| `DataTarget.LoadDump(string)` replaced by `LoadDump(string, DataTargetOptions? = null)` | Yes — `DumpContext.cs:32` | The new parameter is **optional**, so the existing single-argument call site still binds |
| `DataTarget.FileLocator` setter removed (now get-only) | No | We never set it |
| `DataTarget.SetSymbolPath(string)` deleted | No | We never called it |
| `IFileLocator.FindElfImage` / `FindMachOImage` removed | No | We do not implement `IFileLocator` |
| `new ClrObject(address, type)` constructor made internal | No | `HeapAnalyzer.cs:117` already uses the correct `heap.GetObject(address)` |
| `AttachToProcess` / `CreateSnapshotAndAttach` / `CreateFromDbgEng` gained an optional options parameter | No | We only load dumps from disk |

Everything else the codebase touches is **unchanged**: `ClrHeap.EnumerateObjects`,
`EnumerateRoots`, `EnumerateSyncBlocks`, `VerifyHeap`, `Segments`, `GetObject`;
`ClrRuntime.Threads`, `EnumerateModules`, `EnumerateHandles`, `GetTypeByMethodTable`,
`GetMethodByInstructionPointer`, `ThreadPool`; `ClrThread.EnumerateStackTrace`,
`EnumerateStackRoots`; `ClrType`, `ClrInstanceField`, `ClrModule`, `ClrMethod`,
`ClrException`, `ClrArray`, `SyncBlock`, `ObjectCorruption` — no member changes at all on
`ClrType`, and additions only on the rest.

`ClrInfo.CreateRuntime` is worth calling out specifically because the migration guide
claims a parameter was removed from it. It was not — the three overloads are **byte-for-byte
identical** across both versions:

```csharp
ClrRuntime CreateRuntime();
ClrRuntime CreateRuntime(string dacPath);
ClrRuntime CreateRuntime(string dacPath, bool ignoreMismatch);
```

So `DumpContext.cs:40` and `:48` (`clrInfo.CreateRuntime(dacPath, ignoreMismatch: true)`)
need no change.

---

## 3. Behavioural changes that DO affect us

These are the actual substance of this upgrade. None of them produce a compiler
diagnostic, which is exactly why they are dangerous.

### 3.1 Default symbol server — the big one

Probing `new DataTargetOptions()` on 4.0.732401 gives:

```
SymbolPaths       : [https://msdl.microsoft.com/download/symbols]
SymbolCachePath   : /var/folders/.../T/symbols     (i.e. Path.GetTempPath() + "symbols")
VerifyDacOnWindows: True
UseLockFreeMemoryMapReader: False
FileLocator       : Microsoft.Diagnostics.Runtime.Implementation.SymbolGroup
CacheOptions      : CacheTypes=True CacheFields=True CacheMethods=True MaxDumpCacheSize=4294967296
```

Two independent changes are bundled here:

1. **`_NT_SYMBOL_PATH` is no longer read.** Confirmed by reading the user-string heap of
   both assemblies: the literal `_NT_SYMBOL_PATH` exists in 3.1.512801 and is **gone** from
   4.0.732401. Any deployment that configured symbols via that environment variable is
   silently ignored on v4.
2. **msdl is now the built-in default.** A bare `DataTarget.LoadDump(path)` on v4 will
   attempt outbound HTTPS to `msdl.microsoft.com` when it needs a binary it cannot find
   locally.

Why this matters here specifically: this is a containerised MCP server. `src/entrypoint.sh`
already fetches the DAC out-of-band via `dotnet-symbol --dac-only` before the server starts,
so ClrMD's own symbol resolution is meant to be a fallback. On v4 that fallback becomes a
network call the container may not be able to make — and the README already notes that
requests can take long enough to need a 10-minute MCP client timeout. Adding a
silent, unbounded network round-trip to the dump-load path in an air-gapped or
egress-restricted container is a latency and hang risk, not just a correctness one.

The symbol cache also defaults into `Path.GetTempPath()`, which in a container is ephemeral
and re-downloaded on every restart.

**Recommendation:** make the behaviour explicit and configurable rather than inheriting the
new default. See [§4.2](#42-make-symbol-resolution-explicit).

### 3.2 New transitive dependency: Azure.Identity

ClrMD 4 takes a dependency on `Azure.Identity 1.21.0` (for the new
`DataTargetOptions.SymbolTokenCredential`, used to authenticate to Azure-hosted symbol
servers). The dependency closure of `DotNetDump.Core` grows from **1 direct package** to
**~20**:

| ClrMD 3.1.512801 | ClrMD 4.0.732401 |
|---|---|
| `Microsoft.Diagnostics.NETCore.Client` | `Microsoft.Diagnostics.NETCore.Client` |
| `System.Collections.Immutable` (netstandard only) | `Azure.Identity` → `Azure.Core`, `Microsoft.Identity.Client`, `Microsoft.Identity.Client.Extensions.Msal`, `Microsoft.IdentityModel.Abstractions`, `System.ClientModel`, `System.Memory.Data`, `System.Security.Cryptography.ProtectedData`, `Microsoft.Bcl.AsyncInterfaces`, and 7× `Microsoft.Extensions.*` |
| `System.Runtime.CompilerServices.Unsafe` | plus `System.Collections.Immutable`, `System.Text.Json`, `System.IO.Pipelines`, `System.Diagnostics.DiagnosticSource`, `System.Text.Encodings.Web` on net8.0/net9.0 |

Consequences: a larger published image, and a materially larger surface for CVE scanning
and Dependabot churn — MSAL and `System.Text.Json` are both frequent advisory sources. We
do not use `SymbolTokenCredential`, but the dependency is not optional.

### 3.3 net8.0 and net9.0 fall back to the netstandard2.0 asset

ClrMD 4 ships `lib/netstandard2.0` and `lib/net10.0` only — the `lib/net6.0` asset from v3
is gone. Confirmed in `project.assets.json`:

| Our TFM | ClrMD asset selected |
|---|---|
| net8.0 | `lib/netstandard2.0` |
| net9.0 | `lib/netstandard2.0` |
| net10.0 | `lib/net10.0` |

The netstandard2.0 path drags in the polyfill packages listed above and forgoes whatever
modern-runtime optimisations the net10.0 build carries.

This directly affects the shipped container: `src/Dockerfile:11` publishes with
`--framework net9.0`, so **the Docker image runs the netstandard2.0 build of ClrMD**. The
base image is already `mcr.microsoft.com/dotnet/sdk:10.0`, so switching the publish to
`net10.0` is nearly free and gets the better asset.

### 3.4 DAC signature verification (no impact on us, documented for completeness)

`DataTargetOptions.VerifyDacOnWindows` defaults to `true`. As the name says, it is
Windows-only; the container is Linux and local development here is macOS, so this is inert.
Recorded because the migration guide describes it inaccurately (see §6) and someone will
eventually read that guide and worry about it.

---

## 4. Recommended changes

Ordered by priority. Steps 4.1–4.3 are the upgrade; 4.4–4.5 are opportunities the analysis
surfaced that are worth doing while the file is open.

### 4.1 Bump the package (required)

Two files, mechanical:

- `src/DotNetDump.Core/DotNetDump.Core.csproj:10`
- `src/exploration/DotNetDumpExplorer/DotNetDumpExplorer.csproj:11`

```diff
-<PackageReference Include="Microsoft.Diagnostics.Runtime" Version="3.1.512801" />
+<PackageReference Include="Microsoft.Diagnostics.Runtime" Version="4.0.732401" />
```

No other source change is required to compile.

### 4.2 Make symbol resolution explicit (strongly recommended)

Do not let `DumpContext` inherit the new implicit-network default. Pass
`DataTargetOptions` explicitly so the behaviour is visible in code and controllable by
operators. Sketch for `DumpContext.Load` (`src/DotNetDump.Core/DumpContext.cs:32`):

```csharp
// Explicit options: ClrMD 4 defaults to contacting msdl.microsoft.com and no longer
// reads _NT_SYMBOL_PATH. Keep dump loading offline unless symbols are opted into.
var options = new DataTargetOptions();

string? symbolPaths = Environment.GetEnvironmentVariable("DOTNETDUMP_SYMBOL_PATHS");
if (string.IsNullOrWhiteSpace(symbolPaths)) {
    options.SymbolPaths = [];   // offline by default; entrypoint.sh already fetched the DAC
} else {
    options.SymbolPaths = symbolPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
    options.SymbolCachePath = Environment.GetEnvironmentVariable("DOTNETDUMP_SYMBOL_CACHE")
                              ?? "/dumps/.symcache";   // persistent, not container temp
}

_dataTarget = DataTarget.LoadDump(dumpPath, options);
```

Decide deliberately which default you want — **offline-by-default** (shown above; matches
the existing `entrypoint.sh` design where `dotnet-symbol` has already placed the DAC, and
avoids surprise egress) or **msdl-by-default with a persistent cache** (matches upstream,
better out-of-box experience on a workstation). Offline-by-default is the recommendation
for a server whose clients already need a 10-minute timeout. Whichever is chosen, set
`SymbolCachePath` to something persistent rather than the temp directory.

Then document the new env vars in `README.md`, and note there that `_NT_SYMBOL_PATH` no
longer has any effect.

### 4.3 Publish the container against net10.0 (recommended)

`src/Dockerfile:11`:

```diff
-RUN dotnet publish src/DotNetDump.Server/DotNetDump.Server.csproj -c Release -o /app/out --framework net9.0 ...
+RUN dotnet publish src/DotNetDump.Server/DotNetDump.Server.csproj -c Release -o /app/out --framework net10.0 ...
```

Gets the `lib/net10.0` ClrMD asset and drops the polyfill packages from the image. The
build stage already uses the .NET 10 SDK, so nothing else changes.

### 4.4 Optional: adopt new v4 APIs

Genuinely useful additions, none required:

- **`ClrHeap.EnumerateAdditionalRoots()`** — `HeapAnalyzer.GetGCRoots` currently walks
  `heap.EnumerateRoots()` plus per-thread `EnumerateStackRoots()`. Adding this closes a
  gap where a root that keeps an object alive is not reported, which is precisely the
  "why is this object still alive?" question the tool exists to answer. Best single
  functional win available in v4.
- **`ClrObject.ReadField<T>(ClrInstanceField)` / `ReadObjectField(ClrInstanceField)`** —
  new overloads taking the field object instead of its name.
  `HeapAnalyzer.ReadPrimitiveValue` (`HeapAnalyzer.cs:194`) currently converts a
  `ClrInstanceField` it already holds into a `string` name and forces ClrMD to look it up
  again, once per field per object. Passing the field directly removes that round-trip and
  removes the `string.IsNullOrEmpty(fieldName)` failure mode for unnamed fields.
- **`ClrException.EnumerateExceptionStackTrace()`** — streaming alternative to the
  `StackTrace` immutable array; marginal here since `BuildExceptionDetails` walks the
  whole trace anyway.
- **`DataTargetOptions.UseLockFreeMemoryMapReader`** — opt-in, faster sequential heap
  walks. Tempting for `dump_heap`, but it is **not thread-safe**. `DumpContext` holds one
  shared `ClrRuntime` for the whole MCP server process; if two tool calls can ever be
  serviced concurrently this will corrupt reads. Do not enable it without first confirming
  the MCP host serialises requests.
- **`DataTargetLimits`** — parse-time bounds for hostile or corrupt dumps. Defaults are
  `MaxThreads=20000`, `MaxModules=100000`, `MaxMinidumpStreams=10000`,
  `MaxStackFrames=8096`, `MaxAppDomains=10000`. Only relevant if untrusted dumps are in
  scope; the defaults are sane.
- **`ClrRuntime.StressLog` / `TryGetStressLog`**, **`ClrThread.AllocationContext`**,
  **`ClrModule.EnumerateTypesWithStaticFields()`** — new capabilities that could back new
  MCP tools. Out of scope for the upgrade itself.

### 4.5 Unrelated bugs this analysis turned up

These are **pre-existing and wrong on v3 too** — the upgrade neither causes nor fixes them.
Listing them because the code comments blame ClrMD for gaps that do not exist, so they will
otherwise never be revisited:

- `ThreadAnalyzer.cs:139` — `GcMode = "Unknown", // IsGCMode not available in ClrMD v3`.
  **`ClrThread.GCMode` exists in v3 and v4** and returns `GCMode.Cooperative` /
  `GCMode.Preemptive`.
- `ThreadAnalyzer.cs:143` — `IsGC = false, // Not available in ClrMD v3`.
  **`ClrThread.IsGc` exists in both versions.**
- `ThreadAnalyzer.cs:141,142,145,147` — `ApartmentState`, `IsThreadPoolThread`,
  `IsBackground`, `IsAborted` all hardcoded. **All four are derivable from
  `ClrThread.State`**, a `ClrThreadState` flags enum present in both versions, via
  `TS_InSTA`/`TS_InMTA`, `TS_TPWorkerThread`/`TS_CompletionPortThread`, `TS_Background`,
  and `TS_Aborted`/`TS_AbortRequested`/`TS_AbortInitiated`.
- `MetadataAnalyzer.cs:39-41` — `IsInterface`/`IsAbstract`/`IsSealed` hardcoded `false`
  with `// Not directly available in ClrMD`. **`ClrType.TypeAttributes` exists in both
  versions** and is a `System.Reflection.TypeAttributes` flags value carrying exactly
  these bits.

So `get_thread_states` and `get_methodtable` are currently returning fabricated constants
where real data is available. That is a correctness issue in tool output worth its own
ticket, independent of this upgrade.

---

## 5. Testing — read this before merging

**The existing test suite provides no regression safety for this change.**

`IntegrationTests.cs:13` points at `../../../../../dumps/core_20251212_112511`, and every
test opens with a variant of:

```csharp
if (!File.Exists(_dumpPath)) return;   // Skip if dump missing
```

That path **does not exist in this repository or working tree**. The dump is not committed
and is not fetched by any build step, so every integration test currently passes by
returning immediately without asserting anything. A green test run says nothing about
whether ClrMD 4 still reads dumps correctly.

Since the entire risk of this upgrade is behavioural, this has to be addressed for the
upgrade to be verifiable at all:

1. **Make the skip visible.** Replace the silent `return` with `Assert.Skip(...)`
   (xUnit 2.9 supports it) so the run reports "skipped" rather than "passed". Otherwise
   CI will keep reporting success for tests that never execute.
2. **Get a dump into CI.** Generate one at build time — a tiny console app plus
   `dotnet-dump collect` in a CI step is enough — or commit a small pre-captured dump as
   a test fixture. Without this, none of the analyzers are covered on any version.
3. **Capture a v3 baseline first.** Before bumping, run every analyzer against a real
   dump on 3.1.512801 and save the output. Re-run on 4.0.732401 and diff. This is the only
   thing that will actually catch a behavioural regression.
4. **Verify offline load explicitly.** Run the container with egress blocked and confirm
   `load_dump` still completes and does not hang. This is the single most likely
   user-visible failure mode of the upgrade (§3.1).
5. **Time a large `dump_heap` before and after.** The README's 10-minute client timeout
   exists because heap enumeration is already slow; confirm v4 has not made it worse.

---

## 6. Where the official migration guide is wrong

The guide at `doc/Migrating4.md` on `main` does not match the shipped 4.0.732401 package.
Verified discrepancies, so nobody re-derives them later:

| Guide says | Shipped 4.0.732401 actually has |
|---|---|
| `DataTargetOptions.VerifyDacSignature` | Property is named **`VerifyDacOnWindows`**. There is also an undocumented `DacSignatureVerificationOverride` (`Func<string, bool>`) |
| `ClrInfo.CreateRuntime()` lost its `verifySignature` parameter | **No such parameter ever existed in 3.1.512801.** All three overloads are identical in v3 and v4 |
| `DataTargetLimits.MaxFileDownloadSize` (16 MB default) | **Does not exist** on `DataTargetLimits` |
| `DataTargetLimits.SymbolTimeout` (180 s default) | **Does not exist** on `DataTargetLimits` |
| `DataTargetLimits.MaxMinidumpMemoryRanges` etc. | Present, but the guide omits `MaxStackFrames`, `MaxAppDomains`, `MaxPEExportNames`, `MaxPERelocations`, `MaxPEDebugDirectories`, `MaxElfAuxvEntries`, `MaxElfFileTableEntries`, `MaxElfGnuHashChainLength`, `MaxMachOSymbols`, `MaxMachOAsciiLength`, `MaxMachODylinkerSearchBytes` |
| `CacheOptions.UseOSMemoryFeatures` → `UseLockFreeMemoryMapReader` | `UseLockFreeMemoryMapReader` is on `DataTargetOptions`, not `CacheOptions` |
| `Command` / `CommandOptions` were removed | Neither type was public in 3.1.512801, so this is a no-op for consumers |
| — (not mentioned) | `DataTargetOptions.SymbolProvider` (`IClrSymbolProvider`) is new and undocumented |

Treat `api-diff.md` as the source of truth over the guide.

---

## 7. Suggested sequencing

| # | Step | Status |
|---|---|---|
| 1 | Capture analyzer output baseline on 3.1.512801 (§5.3) | ✅ done — ad hoc dump, see §0 |
| 2 | Bump the package in both csproj files (§4.1) | ✅ done |
| 3 | Add explicit `DataTargetOptions` to `DumpContext` (§4.2) | ✅ done — offline by default |
| 4 | Switch the Dockerfile publish to net10.0 (§4.3) | ✅ done |
| 5 | Diff analyzer output vs. baseline; heap-timing check (§5.3, §5.5) | ✅ done — identical output, v4 faster |
| 6 | Update README: symbol env vars, `_NT_SYMBOL_PATH` no longer honoured | ✅ done |
| 7 | Commit a permanent test fixture + `Assert.Skip` (§5.1–5.2) | ⬜ **open** — the tests still self-skip on a clean checkout |
| 8 | Offline check inside the real container with egress blocked (§5.4) | ⬜ open — needs a Linux image build |
| 9 | Separate tickets: `EnumerateAdditionalRoots` (§4.4), the fabricated thread/type fields (§4.5), the `MSB5009` solution file (§1) | ⬜ open |

The upgrade itself turned out to be cheap and is fully verified. What remains is the test
coverage that should exist regardless of this change — step 7 is the one worth prioritising,
since without it the next ClrMD bump gets no safety net either.
