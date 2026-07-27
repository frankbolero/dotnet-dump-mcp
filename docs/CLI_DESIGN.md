# CLI Design Spec

Status: **proposed**, not implemented.

This document specifies a command-line front end for `DotNetDump.Core`, intended to become the
primary way both humans and AI agents drive dump analysis. The MCP server remains in the repo as a
thin optional adapter over the same Core.

## 1. Motivation

The MCP server works, but its delivery model is a poor fit for the workload:

* **One process per client.** Stdio transport spawns a server per connected agent, alive for the
  whole session. A developer with five agents open has five .NET processes holding dump state,
  when at most one is analyzing anything.
* **Persistence buys less than it appears — and the caching that would matter is broken anyway.**
  Exactly one analyzer memoizes a heap walk (`HeapAnalyzer._cachedStats`, `HeapAnalyzer.cs:15`),
  and it almost certainly never survives a tool call: `HeapAnalyzer` is registered `AddTransient`
  (`Program.cs:22`) and `DumpAnalyzerTools` is constructed per invocation by
  `WithToolsFromAssembly`. It is also never invalidated by `load_dump`, so if that instance *were*
  long-lived it would serve stats from a previous dump. Dead or wrong, either way. What genuinely
  survives across tool calls is `DataTarget.LoadDump` + DAC init plus ClrMD's incremental type
  cache — a bounded, one-off cost, not the cost of the analysis. The expensive part, the heap
  walk, is recomputed every time. §6 specifies a cache that fixes this properly, on disk, shared
  by both front ends.
* **Always-on context cost.** 25 tool definitions with full parameter descriptions are injected
  into every request of every connected agent, whether or not that agent will touch a dump.
* **Results cannot be filtered.** An MCP tool result lands in the agent's context whole. A
  `dumpheap` on a real dump can produce thousands of type rows; the `limit`/`offset` parameters
  exist precisely because of this. Shell output can be piped through `grep`/`jq`/`head` before it
  costs a single token.
* **The design is already single-tenant.** `IDumpContext` is a singleton and `Load()` unloads the
  previous dump (`DumpContext.cs:31`). One dump, one session, one consumer — which is exactly the
  CLI model, paid for with a persistent process.

A CLI inverts all five: the process exits when the command finishes, costs nothing when idle,
composes with the shell, and is described to the agent on demand by a skill rather than
permanently by a tool manifest.

### 1.1 Prerequisite measurement

Before implementing, measure per-invocation startup on a representative dump (5–20 GB):

```bash
time dotnet run -c Release --project src/exploration/DotNetDumpExplorer -- /path/to/dump.core dumpheap
```

Run twice and compare — the second run has a warm page cache. The number that matters is
`DataTarget.LoadDump` + `CreateRuntime`, since a CLI pays it per command.

* **≲ 3 s** — ship the CLI as specified. The tradeoff is clearly worth it.
* **≫ 3 s** — the CLI surface below is unchanged, but adopt the persistent-container delivery in
  §8.2 so the cost is paid once per dump rather than once per command. Note that §6 reduces the
  weight of this decision considerably: with a warm cache, most repeat commands never reach the
  runtime at all.

## 2. Naming and layout

| Item | Value |
| :--- | :--- |
| Binary | `dndump` |
| Project | `src/DotNetDump.Cli/DotNetDump.Cli.csproj` |
| Target frameworks | `net8.0;net9.0;net10.0` (matches the rest of the solution) |
| Packaging | `dotnet tool` (`PackAsTool`), plus the existing Docker image |

`dndump` is deliberately *not* `dotnet-dump-analyze` — Microsoft's `dotnet-dump analyze` already
owns that name and confusing the two would be actively harmful. The name is short because agents
type it in every command and it appears in every transcript.

The CLI project references `DotNetDump.Core` only. It must not reference `DotNetDump.Server` or
`ModelContextProtocol`.

## 3. Invocation model

```
dndump [global-options] <command> [arguments] [command-options]
```

### 3.1 Selecting the dump

Every analysis command needs a dump. Resolution order, first match wins:

1. `--dump <path>` on the command line.
2. `DNDUMP_PATH` environment variable.
3. A session file, `.dndump/session.json`, searched from the current directory upward.

The session file exists because agent shells do not persist environment variables between
commands — each Bash invocation is a fresh shell. Writing the path once keeps subsequent commands
short:

```bash
dndump use /dumps/prod-oom.core     # writes .dndump/session.json
dndump dumpheap --top 20            # no --dump needed
```

`.dndump/` should be added to `.gitignore`. The session file records the dump path, the resolved
DAC path, and the timestamp; it holds no analysis state and is safe to delete at any time.

### 3.2 Global options

| Option | Default | Meaning |
| :--- | :--- | :--- |
| `--dump <path>` | — | Dump file to analyze. |
| `--dac <path>` | auto | Explicit DAC path; overrides detection. |
| `--format <md\|json\|tsv>` | `md` | Output format. |
| `--limit <n>` | `50` | Max rows (list commands). |
| `--offset <n>` | `0` | Rows to skip (list commands). |
| `--sort <field>` | per command | Sort field; valid values documented per command. |
| `--order <asc\|desc>` | per command | Sort direction. |
| `--quiet` | off | Suppress the informational header on stderr. |
| `--version`, `--help` | — | Standard. |

`--top <n>` is accepted as an alias for `--limit <n>` on the summary commands, because that is how
people actually describe the operation ("top 20 types by size").

### 3.3 Output formats

* **`md`** (default) — the existing `MarkdownFormatter` output. Preserves current behavior and is
  the most readable for an LLM reading results directly.
* **`json`** — the Core model objects serialized directly. This is the format that makes the CLI
  worth building: `dndump dumpheap --format json | jq '.[] | select(.TotalSize > 1e9)'` filters on
  the machine instead of in the context window.
* **`tsv`** — tab-separated, no header decoration. For `grep`/`awk`/`cut` pipelines where markdown
  table padding gets in the way.

All three render from the same strongly-typed models. `MarkdownFormatter` stays as-is; `json` and
`tsv` are new sibling formatters in `DotNetDump.Core/Formatting/`.

### 3.4 Streams and exit codes

This is a correctness fix, not just an ergonomic one. `DumpAnalyzerTools.ExecuteSafe` currently
catches every exception and returns `"Error: {message}"` as a normal result string
(`DumpAnalyzerTools.cs:313`). In a shell that is a silent failure: a piped `grep` matches nothing
and the exit code is still `0`.

The CLI must instead:

* Write **results to stdout** and **diagnostics/errors to stderr**.
* Return meaningful exit codes:

| Code | Meaning |
| :--- | :--- |
| `0` | Success. |
| `1` | Analysis error (bad address, type not found, corrupt structure). |
| `2` | Usage error (unknown command, bad option, missing argument). |
| `3` | Dump could not be loaded (missing file, no CLR found, DAC mismatch). |

## 4. Command surface

Commands keep their SOS names. Developers analyzing dumps already know `dumpheap` and `clrstack`
from WinDbg/SOS, and the existing `docs/commands/*.md` reference set stays valid.

### 4.1 Session and orientation

| Command | Arguments | Notes |
| :--- | :--- | :--- |
| `use <path>` | dump path | Writes `.dndump/session.json`. Validates the dump opens. |
| `info` | — | Runtime version, architecture, OS, DAC path and whether it matched, heap size, segment and thread counts. The natural first command; orients before any expensive walk. |
| `commands` | — | Lists commands with one-line descriptions. Cheap for an agent to consult. |

`info` is new — it has no MCP tool equivalent today, but a CLI needs a cheap "what am I looking at"
entry point, and it doubles as the diagnostic when DAC resolution goes wrong.

### 4.2 Heap

| Command | Arguments | Options | Sort fields |
| :--- | :--- | :--- | :--- |
| `dumpheap` | — | `--limit`, `--offset`, `--sort`, `--order` | `TotalSize` (default), `Count`, `TypeName` |
| `listobj` | — | `--type <substring>`, `--limit`, `--offset`, `--sort`, `--order` | `Address` (default), `Size` |
| `dumpobj` | `<address>` | — | — |
| `gcroot` | `<address>` | `--max-paths <n>` (default 4), `--limit`, `--offset` | — |
| `eeheap` | — | — | — |
| `gchandles` | — | `--limit`, `--offset`, `--sort`, `--order` | `Address` (default), `Kind`, `TypeName` |
| `verifyheap` | — | — | — |
| `verifyobj` | `<address>` | — | — |

`listobj` is named for the existing `ListObjects` tool rather than SOS's `dumpheap` object mode,
because the two behaviors are separate commands here and both need names.

All `<address>` arguments accept hex with or without a `0x` prefix — `AddressParser.Parse` already
handles this and is shared unchanged.

### 4.3 Threads and stacks

| Command | Arguments | Options | Sort fields |
| :--- | :--- | :--- | :--- |
| `clrthreads` | — | `--limit`, `--offset`, `--sort`, `--order` | `ManagedThreadId` (default), `OSThreadId`, `Exception` |
| `threadstate` | — | `--limit`, `--offset`, `--sort`, `--order` | `ManagedThreadId` (default), `OSThreadId`, `LockCount` |
| `clrstack` | — | `--max-frames <n>` (default 20) | — |
| `eestack` | — | `--max-frames <n>` (default 30) | — |
| `dumpstack` | — | `--max-frames <n>` (default 100), `--limit`, `--offset`, `--sort`, `--order` | `ManagedThreadId` (default), `OSThreadId` |
| `threadpool` | — | — | — |
| `syncblk` | — | `--[no-]thin-locks` (default on), `--limit`, `--offset`, `--sort`, `--order` | `Address` (default), `Recursion`, `Waiting` |

### 4.4 Exceptions

| Command | Arguments | Options |
| :--- | :--- | :--- |
| `printexception` (alias `pe`) | `[address]` | `--[no-]heap-exceptions` (default on), `--all-threads`, `--limit`, `--offset`, `--sort`, `--order` |

`--all-threads` is the inverse of the current `onlyWithExceptions` parameter, which defaults to
`true`. Naming the flag for the *unusual* case is the right way round: the common request is
"show me the exceptions", not "show me every thread including the boring ones".

### 4.5 Modules and metadata

| Command | Arguments | Options | Sort fields |
| :--- | :--- | :--- | :--- |
| `clrmodules` | — | `--include-system`, `--limit`, `--offset`, `--sort`, `--order` | `Address` (default), `Size`, `Name` |
| `dumpmodule` | `<address>` | — | — |
| `dumpassembly` | `<address>` | — | — |
| `dumpmt` | `<address>` | — | — |
| `dumpmd` | `<address>` | — | — |
| `dumpclass` | `<address>` | — | MethodTable address; ClrMD does not expose EEClass separately. |
| `name2ee` | `<module> <type[.method]>` | — | — |
| `ip2md` | `<address>` | — | — |

### 4.6 Coverage

Every current MCP tool maps to exactly one command. `load_dump` becomes `use`; the rest keep their
names. `info` and `commands` are additions. Nothing is dropped.

## 5. Worked examples

The point of the CLI is that these are one line each and only the surviving rows cost context.

```bash
# Orient
dndump use /dumps/prod-oom.core && dndump info

# Top heap consumers
dndump dumpheap --top 20

# Only the type we suspect, without paging through the summary
dndump dumpheap --format tsv --limit 5000 | grep -i 'HttpClient'

# Everything over 1 GB
dndump dumpheap --format json --limit 5000 \
  | jq -r '.[] | select(.TotalSize > 1073741824) | "\(.TotalSize)\t\(.TypeName)"'

# Sample an instance and find what retains it
dndump listobj --type MyApp.CacheEntry --limit 1
dndump gcroot 0x7f2c14a20918 --max-paths 4

# Deadlock triage
dndump syncblk
dndump clrstack --max-frames 30

# Exceptions on the heap, not just in flight
dndump pe --limit 20
```

## 6. Caching

A per-invocation CLI cannot keep ClrMD's in-memory state alive between commands. It can, however,
persist *derived results* to disk — something the current server does not do at all. The net
effect is that the CLI is faster than today's MCP server on any repeated query, not slower.

### 6.1 Dumps are immutable

This is the property that makes the whole design tractable. A core dump never changes, so any
result derived from it is valid forever. There is no content-invalidation problem, only an
identity problem. The cache key is:

```
hash( dump identity + DAC identity + operation + normalized arguments + schema version )
```

* **Dump identity** — `size + mtime + inode`, or size plus a hash of sampled head/tail regions.
  Never hash the whole file; on a 20 GB dump that costs more than the analysis it protects.
* **DAC identity** — included deliberately. A mismatched DAC produces wrong answers, and a bad
  first run must not poison results once the correct DAC is in place.
* **Schema version** — so a change to a Core model shape cannot serve a stale payload.

### 6.2 Cache the model, not the rendering

Normalized arguments **exclude** `--limit`, `--offset`, `--sort`, `--order` and `--format`. Those
are applied *after* the cache lookup, to the materialized model.

One cached heap-statistics entry therefore serves every pagination, sort and format variant of
`dumpheap`. An agent exploring the same data ten different ways pays for the walk once. This is
the same trick `_cachedStats` already plays in-process, generalized to disk and made correct.

### 6.3 What is worth caching

Only walk-scale work. There are five full-heap enumerations in Core today —
`HeapAnalyzer.cs:31,59,320,408` and `ThreadAnalyzer.cs:291` — and they are the entire target list.

| Tier | Contents | Size | Format |
| :--- | :--- | :--- | :--- |
| 1 | Heap statistics; GC root candidates from `EnumerateRoots()`; per-target `gcroot` results | KB–MB | JSON |
| 2 | Object index: `address → (MethodTable, size)`, serving `listobj`, `dumpheap` and part of `gcroot` from one walk | ~24 bytes/object; GB-scale on a large heap | Compact binary, memory-mapped on read |

Tier 1 is the prize and should ship first: a few MB of output derived from a walk that can take
minutes. Tier 2 is phase 2, built only if measurement shows repeated heap walks still dominate.

**Do not cache cheap operations.** `dumpobj <address>` is a single object read — a cache lookup
would cost more than recomputing it. Same for `eeheap`, `threadpool`, `clrmodules`, `dumpmt` and
the other metadata commands. Adding cache plumbing there is pure overhead.

### 6.4 The pluggable interface

The cache is an abstraction in `DotNetDump.Core/Caching/`, not a concrete store. Analyzers depend
on the interface and know nothing about where results live.

```csharp
namespace DotNetDump.Core.Caching;

/// Identity of the dump and DAC a result was derived from.
public readonly record struct DumpIdentity(string Fingerprint);

public readonly record struct CacheKey(
    DumpIdentity Dump,
    string Operation,        // e.g. "heap-statistics"
    string ArgumentsHash,    // normalized; excludes limit/offset/sort/order/format
    int SchemaVersion);

public interface IAnalysisCache {
    /// Returns the cached value, or computes it, stores it and returns it.
    /// Implementations must be safe under concurrent access from multiple processes.
    T GetOrCompute<T>(CacheKey key, Func<T> compute) where T : class;

    void Invalidate(CacheKey key);
    void ClearDump(DumpIdentity dump);
}

/// Payload encoding, separated so tier 1 (JSON) and tier 2 (binary) can differ.
public interface ICacheSerializer {
    void Write<T>(Stream destination, T value);
    T? Read<T>(Stream source);
}
```

Shipped providers:

| Provider | Purpose |
| :--- | :--- |
| `NullAnalysisCache` | No-op; always computes. **The default**, so existing behavior and tests are unchanged until caching is opted into. Also what `--no-cache` selects. |
| `MemoryAnalysisCache` | Process-local dictionary. Serves the MCP server within a session, and makes analyzer tests fast without touching disk. |
| `FileSystemAnalysisCache` | The real one. Directory per dump identity, JSON payloads via `JsonCacheSerializer`. |
| `TieredAnalysisCache` | Composes providers in order — memory, then disk, then compute — promoting hits upward. |

`ICacheSerializer` is a separate seam because tier 1 and tier 2 have incompatible needs: JSON is
fine for a few thousand type rows and catastrophic for a 50-million-entry object index.

Analyzers take `IAnalysisCache` by constructor injection, matching the existing
`HeapAnalyzer(IDumpContext)` pattern, and default to `NullAnalysisCache` when unregistered. No
decorator layer — that would require per-analyzer interfaces that do not exist today and would buy
nothing.

`DumpIdentity` is computed once per load and exposed on `IDumpContext`, which is the only component
that knows both the dump path and the resolved DAC.

### 6.5 Storage, concurrency and lifecycle

* **Location** — an XDG-style user cache directory by default; `DNDUMP_CACHE` overrides. In Docker,
  point it at a mounted volume so it survives container restarts and is visible from the host.
* **Atomicity** — write to a temp file and `rename` into place. Readers never observe a partial
  entry.
* **Duplicate work** — two agents running `dumpheap` against a cold cache would otherwise both
  perform the same multi-minute walk. Take an advisory lock on the key so the second waits for the
  first and then reads its result.
* **Management** — `dndump cache list|clear|prune`, with an LRU size cap. Tier 2 entries are
  GB-scale and cannot be allowed to accumulate silently.
* **Escape hatches** — `--no-cache` (bypass entirely) and `--refresh` (recompute and overwrite),
  both of which are needed the first time a cached result is suspected of being wrong.

`.dndump/` and the cache directory both belong in `.gitignore`.

### 6.6 Sharing with the MCP server

Because the cache sits in Core beneath both front ends, the server gets it for free by registering
a provider in `Program.cs`. This is a strict improvement to the server:

* Repeat `dump_heap` becomes instant instead of re-walking, fixing the `_cachedStats` defect
  properly — a disk cache keyed on dump identity cannot go stale across `load_dump` the way the
  in-process field does.
* **Results are shared across processes.** In the five-agent scenario each server process has its
  own useless in-memory state today; with a shared disk cache they reuse one computed heap walk.
  This does not fix the idle-process count, but it removes the redundant computation, which is the
  part that actually pins the CPU.
* **Mixed usage composes.** A CLI run warms the cache for a subsequent MCP session and vice versa,
  so adopting the CLI is not an all-or-nothing switch.

Two honest limits: the disk cache cannot preserve ClrMD's in-memory type cache, so per-invocation
runtime init is still paid (§1.1); and it does nothing about the number of idle processes on the
MCP path.

## 7. Implementation notes

* **Argument parsing** — use `System.CommandLine`. Verify the current published version and its API
  shape before writing against it; the package went through a long beta with breaking changes
  between previews. If the API is still unstable at implementation time, a hand-rolled parser is
  acceptable — the surface is regular enough (verb + optional address + shared options) that this
  is a contained amount of code.
* **No logic in the CLI project.** Commands parse arguments, call an analyzer, hand the model to a
  formatter, and set an exit code. All analysis stays in Core. This is the same discipline the
  server layer already follows.
* **Errors propagate.** Do not port `ExecuteSafe`. Let analyzer exceptions reach a single top-level
  handler that maps exception type to exit code and writes the message to stderr.
* **Formatters are shared.** `json` and `tsv` go in `DotNetDump.Core/Formatting/` next to
  `MarkdownFormatter`, so the MCP server can offer them too if it ever wants to.
* **Caching is opt-in at composition.** Analyzers default to `NullAnalysisCache`; the CLI and the
  server each choose a provider at startup. Nothing in Core assumes a cache exists.
* **Tests.** Extend `DotNetDump.Tests` with parser tests (option binding, address forms, precedence
  of `--dump` over `DNDUMP_PATH` over session file), exit-code assertions, and cache-provider tests
  (key composition, hit/miss, concurrent writers, tier promotion). All are pure unit tests and need
  no dump.

## 8. Delivery

### 8.1 Native

```bash
dotnet tool install --global dndump
```

Works when the host OS and architecture match the dump's.

### 8.2 Docker

The container solves architecture mismatch (Linux ARM64 dumps on a Mac, and vice versa) and must
keep working. Naively running one container per command is a regression: it pays container startup
*plus* `entrypoint.sh` running `dotnet-symbol --dac-only` on every invocation.

Two required changes:

1. **Cache the DAC in a mounted volume** so the fetch is a no-op after the first command.
2. **Keep one long-lived container per dump** and `exec` into it per command:

   ```bash
   docker run -d --name dndump-session \
     -v "/path/to/dumps:/dumps" -v dndump-symcache:/symcache \
     dotnet-dump-mcp-server sleep infinity

   docker exec dndump-session dndump --dump /dumps/prod-oom.core dumpheap --top 20
   ```

This is the shape that answers the original complaint directly: **one process space per dump, not
one per agent.** The page cache is warm and shared, the DAC is fetched once, and no CLR runtime
sits idle per connected agent. A thin wrapper script should hide the `docker exec` prefix so the
agent-facing commands are identical to the native ones.

### 8.3 Skill

The CLI is invoked through a skill rather than a tool manifest, which is what removes the
always-on context cost. The skill should carry:

* The dump-selection convention (`use` first, then bare commands).
* The command table from §4, condensed.
* **Triage workflows** — the part a tool manifest structurally cannot express:
  * *OOM / leak*: `info` → `dumpheap --top 20` → `listobj --type X --limit 1` → `gcroot` → read
    the retaining collection with `dumpobj`.
  * *Deadlock / hang*: `clrthreads` → `syncblk` → `clrstack` → `threadstate` on the blocked ids.
  * *Unhandled exception*: `pe` → `clrstack` on the faulting thread → `dumpobj` on the exception.
* The piping idioms from §5, so the agent filters in the shell by default instead of paging
  through results in context.

## 9. Relationship to the MCP server

The server stays. It is already a thin adapter over Core and costs little to maintain. The
recommendation is that documentation presents the CLI as the default and the MCP server as the
option for clients that cannot invoke a shell, with the tradeoff stated plainly so users choose
knowingly.

Explicitly **not** recommended: converting the server to an HTTP/SSE shared daemon. It would fix
the process count but collide with the singleton `DumpContext` — two clients loading different
dumps would clobber each other, and fixing that means per-session contexts, which puts N dumps
back inside one process. Real work, no better outcome, and the tool-manifest context cost remains.

## 10. Future direction: web frontend

**Status: not planned work.** Recorded so that decisions taken now do not foreclose it, and so the
one thing it genuinely constrains today (§10.3) is visible to whoever implements the JSON formatter.

The idea: `dndump serve` launches a local web UI for interactive investigation of a loaded dump —
a third consumer of Core alongside the CLI and the MCP server.

### 10.1 Why it fits

* Core is already front-end agnostic. Analyzers return strongly-typed models and formatting is a
  separate layer, so a web front end is mostly assembly rather than new analysis work.
* **The cache economics favour it more than the CLI's.** §6 saves ~1.8 s per repeated walk across
  maybe four walks in a CLI triage session. Interactive browsing issues dozens of queries, and
  §6.2's "cache the model, sort and page afterwards" design is exactly what a UI wants — one cached
  heap-stat set backing all subsequent interaction.
* A long-lived server process also keeps ClrMD's **in-memory** type cache warm across queries. That
  is the one layer a per-invocation CLI must always discard, so this is the only delivery model that
  gets both halves of the caching story.

### 10.2 Deliberately unspecified

The interface is expected to be designed separately — as a component library authored in Claude
Design and synced into the repo via `/design-sync`. This spec therefore takes **no position** on:

* framework or rendering model (Blazor, SPA, server-rendered, htmx — all open);
* view inventory, layout, navigation or visual design;
* whether it ships inside the `dndump` binary or as a separate host;
* how much runs client-side versus server-side.

Those are design decisions, and pinning them here would only get in the way.

### 10.3 What the backend must provide — the part that constrains work now

One decision does need making early, because Phase 2 implements it: **`--format json` should be
built as an API contract, not merely a convenience for `jq` pipelines.** Concretely that means
stable field names, a consistent envelope, and pagination metadata (total, offset, limit, whether
more remains) travelling alongside the rows rather than being implied by their absence.

Retrofitting that later means designing the serialization layer twice. Doing it now costs nothing —
it is the same formatter either way.

It also settles §11.3 in the affirmative: with a web front end in view, the Core model shapes are a
public contract.

### 10.4 Constraints any design will have to respect

Recorded as constraints rather than requirements, so they inform a design without dictating one.

* **Data volume.** Heap statistics are ~1,500 rows and fine to render. Object lists are not — a
  measured dump holds 10.2 M objects. Server-side pagination and virtualized rendering are
  mandatory; "load it all and filter client-side" is not available at this scale.
* **Cold-start latency is seconds, not milliseconds.** A first walk costs roughly
  `object-count / 8M` seconds (§0.2 of the implementation plan). The cache makes repeats instant, but
  the first view of a cold dump genuinely takes time. That needs a real pending state, not a spinner
  that implies imminence.
* **Dumps contain secrets.** Connection strings, tokens and PII live in heap strings, and object
  inspection renders string values. Bind to `127.0.0.1` only — never `0.0.0.0` by default — and
  decide deliberately whether the Docker path exposes a port at all. This is much easier to get
  right at the start than to retrofit.
* **It reintroduces a long-lived process holding a dump** — the MCP server's resource profile with a
  different protocol. Acceptable because it is explicitly launched and closed rather than
  auto-spawned per agent and left idling, but it is a real cost, not a free addition.
* **The stack must be able to consume a plain HTML/CSS component library,** since that is what a
  Claude Design project produces. This is a mild argument against frameworks with strongly bespoke
  component models, but not a decision — noted so it is weighed rather than discovered.

### 10.5 Capabilities worth having

Listed as capabilities rather than screens, to leave the design open. These are the things the CLI
and the agent path structurally cannot do well:

* Navigating object references interactively, with history — `gcroot` output is hard to follow as
  text and natural as a tree.
* Rendering retention paths as graphs. `RootPathFinder` already computes node-disjoint paths; that
  is graph-shaped data currently flattened into a table.
* Seeing heap composition at a glance rather than reading a sorted table.
* Re-sorting and filtering without re-querying, which §6.2 already supports.

### 10.6 Sequencing

After Phases 1, 2 and 6 of the implementation plan. The command surface *is* the API surface, so
building this once those exist means consuming a settled contract rather than designing it twice.

## 11. Open questions

1. **Startup cost** — §1.1. Decides native-per-command vs. persistent-container delivery.
2. **Should `use` validate eagerly?** Opening the dump to verify catches a bad path immediately but
   makes `use` as slow as any other command. Leaning yes: a clear failure at session start beats a
   confusing one on the first real query.
3. **Do the Core models become a public contract?** — **yes, settled by §10.3.** Three pressures
   point the same way: `--format json` invites agents to depend on field names in `jq` filters, §6
   persists those same shapes to disk, and a future web front end would consume them as an API.
   `SchemaVersion` in the cache key handles the storage half safely; the other two are a real
   compatibility commitment, now accepted deliberately rather than drifted into.
4. **Is tier 2 worth building?** The object index is the difference between "repeat queries are
   fast" and "the first query on a dump is the only slow one". It is also GB-scale and needs a
   bespoke binary format. Defer until tier 1 is measured in real use.
5. **Binary name** — `dndump` is the proposal, not a decision.
