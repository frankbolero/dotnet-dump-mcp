# Implementation plan

Status: **proposed**.

Eight phases. Phases 0, 1 and 2 run in parallel; the rest are sequential. Each phase has an exit
criterion that is a demonstrable fact, not a feeling, and the phases that could invalidate later work
carry an explicit measurement.

Read [`README.md` §3](README.md) first for why the ordering is what it is.

## Phase 0 — Core contract

**Track A. No web code. Ships CLI-visible value on its own.**

| Task | Detail |
| :--- | :--- |
| 0.1 | `FilterSpec` in `DotNetDump.Core/Models/`, plus `QueryParameters.Filter`. [`DATA_CONTRACT.md` §2.2](DATA_CONTRACT.md) |
| 0.2 | Apply the filter in each analyzer between the cached computation and the sort. Declare honored fields per method; throw `UnsupportedFilterException` otherwise. |
| 0.3 | `PagedResult<T>.TotalUnfiltered`. Bump the cache `SchemaVersion` — the payload shape changed. |
| 0.4 | `FilterExpressionParser` in `DotNetDump.Cli` for the `--filter field~value` grammar; wire `--filter` (repeatable) into every list command. Keep `listobj --type` as an alias. |
| 0.5 | Convert the remaining `IEnumerable<T>` analyzer methods to `PagedResult<T>`: `GetGCHandles`, `GetThreads`, `GetDetailedStacks`, `GetThreadStates`, `VerifyHeap`. Update `JsonFormatter` to use the real totals instead of `FromItemsOnly`. |
| 0.6 | Move `DumpResolver` and `SessionFile` from `DotNetDump.Cli` to `DotNetDump.Core`. Pure relocation; the CLI keeps behaving identically. |
| 0.7 | `IProgress<WalkProgress>` on the five walk-scale methods. The CLI passes `null`. |

**Measurement (gates Phase 4).** On a real dump with a warm cache, time `dumpheap` with a filter
that matches ~5% of rows versus unfiltered. Filtering happens after the cached walk, so the delta
should be sub-millisecond on ~1,500 rows. If it is not, the assumption that "filters are free" is
wrong and the web UI's filter-as-you-type interaction has to be reconsidered before it is built.

**Exit criterion.** `dndump dumpheap --filter 'type~Http' --filter 'size>100mb'` returns correct
filtered results with an honest `total` and `totalUnfiltered`, on a warm cache, without re-walking.
`dndump clrmodules --filter 'gen=2'` exits `2` with a clear message rather than silently ignoring it.

## Phase 1 — Design

**Track B. Calendar time, no code dependency. Start on day one.**

| Task | Detail |
| :--- | :--- |
| 1.1 | Review and adjust [`DESIGN_BRIEF.md`](DESIGN_BRIEF.md) — especially the §3 sample data, which should be replaced with rows from an actual dump you have. |
| 1.2 | Create the Claude Design project; build the component library against the brief. |
| 1.3 | Iterate on the four highest-risk components first: **data table** (must survive 200-character type names), **tree view** (four content variants from one component), **pending/progress panel**, **truncation banner**. |
| 1.4 | `/design-sync` the library into `src/DotNetDump.Web/wwwroot/` + `Views/`. |

**Exit criterion.** Every component in [`DESIGN_BRIEF.md` §5](DESIGN_BRIEF.md) exists, previews with
real sample data, works in light and dark, and carries a `@dsCard` marker. The data table renders
`System.Collections.Generic.Dictionary<System.String,MyApp.Domain.CacheEntry>+Entry[]` without
breaking the layout or hiding the distinguishing tail of the name.

## Phase 2 — Web host skeleton

**Track C. Placeholder markup that Phase 3 throws away. The point is the plumbing.**

| Task | Detail |
| :--- | :--- |
| 2.1 | `src/DotNetDump.Web/DotNetDump.Web.csproj` (`Microsoft.NET.Sdk.Web`, `net8.0;net9.0;net10.0`, references Core only). Add to the solution. |
| 2.2 | `dndump serve [--dump] [--port] [--no-open] [--no-warm]` in `DotNetDump.Cli`, starting the host in-process and resolving the dump through the shared `DumpResolver` from 0.6. |
| 2.3 | **`IAnalysisQueue`** — the single serialized analysis worker. [`SERVER.md` §3](SERVER.md). Not optional and not deferrable; every handler is written against it from the first one. |
| 2.4 | Security middleware: explicit `127.0.0.1` Kestrel binding, `ASPNETCORE_URLS` cleared at startup, `Host`-header validation, no CORS. |
| 2.5 | Vendor htmx into `wwwroot/lib/` with an integrity hash. No CDN reference anywhere. |
| 2.6 | `ViewRequestBinder`: query string → `(FilterSpec, QueryParameters)`, with clamping and unsupported-field rejection. |
| 2.7 | One route end to end — `GET /views/dumpheap` — in unstyled placeholder markup, plus `GET /api/dumpheap` through the existing `JsonFormatter`. |

**Measurement (gates Phase 6).** Time `serve` startup to first byte on a cold cache and a warm one,
and time a single cached `dumpheap` render. This is the baseline every Phase 6 target is measured
against.

**Exit criterion.** `dndump use X && dndump serve` opens a browser showing real heap statistics from
a real dump. Two concurrent requests are provably serialized through the queue (test, not
inspection). `curl -H 'Host: evil.example' http://127.0.0.1:5111/` is rejected.

## Phase 3 — Wire the design in

Depends on 1 and 2.

| Task | Detail |
| :--- | :--- |
| 3.1 | Convert the synced HTML components into Razor views taking view models. Structure and classes stay byte-identical where possible; only data binding is introduced. |
| 3.2 | App shell, navigation across the six view groups, dump header bar. |
| 3.3 | Every list view rendering with the real data table component: `dumpheap`, `listobj`, `gchandles`, `clrthreads`, `threadstate`, `syncblk`, `printexception`, `clrmodules`. |
| 3.4 | Every detail view with the detail components: `dumpobj`, `info`, `eeheap`, `threadpool`, `dumpmt`, `dumpmd`, `dumpclass`, `dumpmodule`, `dumpassembly`, `name2ee`, `ip2md`. |
| 3.5 | Document the resync procedure: which files are copied verbatim, which are Razor-ified, and what a designer must not change without a code change. |

**Exit criterion.** All 25 commands are reachable and render correctly styled output. Re-running
`/design-sync` after a purely visual change in Claude Design requires no C# edits — if it does, 3.1
made the templates too clever and needs correcting before Phase 4 builds on them.

## Phase 4 — Interaction

Depends on 0 and 3. This is where the interface becomes usable rather than viewable.

| Task | Detail |
| :--- | :--- |
| 4.1 | Filter bar wired per view, exposing only the fields that view honors. `hx-trigger="input changed delay:250ms"`, `hx-include="closest form"`, `hx-push-url="true"`. |
| 4.2 | Active-filter chips with individual and clear-all removal. |
| 4.3 | Sortable headers with three-state `aria-sort`, preserving the active filter. |
| 4.4 | Infinite scroll: `/views/{view}/rows`, the sentinel row that replaces itself, and the end-of-data state driven by `HasMore`. |
| 4.5 | Out-of-band updates for result counts, pagination footer and cache-state indicator. |
| 4.6 | URL state round-trip: every filter, sort and page position survives copy-paste of the address bar, back and forward. |

**Exit criterion.** Filtering `listobj` on a 10.2M-object dump returns correct results with an honest
"N of 10,238,441" count and stays responsive while typing. Sorting a filtered view keeps the filter.
The back button undoes a filter change.

**Watch for.** The most likely bug in the whole project is a sort or page action dropping the active
filter because `hx-include` was omitted — silently returning unfiltered data that looks plausible.
Test it explicitly for every view.

## Phase 5 — Trees

Depends on 4. Ordered cheapest to most expensive; each is independently shippable.

| Order | Tree | Notes |
| ---: | :--- | :--- |
| 5.1 | **Namespace rollup** — [`DATA_CONTRACT.md` §4.2](DATA_CONTRACT.md) | Pure grouping over cached heap stats, no dump access. Split on `.` only outside `<>`. Fan-out cap with a `More` node. |
| 5.2 | **Thread → frames** — §4.4 | Two levels from existing analyzer output. Group identical idle worker stacks. Locals are a stretch goal: measure whether ClrMD exposes them usefully on a real dump, and drop them without ceremony if not. |
| 5.3 | **gcroot retention paths** — §4.3 | Merge `GCRootSearchInfo.Paths` into a trie on address. **Truncation banner is part of this task, not a follow-up** — see [`../GCROOT_TRUNCATION.md`](../GCROOT_TRUNCATION.md). |
| 5.4 | **Object reference navigator** — §4.5 | Lazy per-node `GetObjectDetails`. Cycle detection via visited-set in the node id, 64-level depth cap, breadcrumb history from the URL. |

**Exit criterion.** All four render, expand lazily, and are keyboard navigable. A deliberately cyclic
object graph terminates in the navigator instead of expanding forever. An intentionally
budget-truncated `gcroot` shows the warning banner and never the words "eligible for collection".

## Phase 6 — Perceived speed

Depends on 4. Measured against the Phase 2 baseline.

| Task | Detail |
| :--- | :--- |
| 6.1 | Startup cache warm: enqueue heap statistics → heap exceptions → sync blocks at `serve` start, skippable with `--no-warm`. |
| 6.2 | Progress plumbing: `IProgress<WalkProgress>` from 0.7 through the queue to a per-job progress record. |
| 6.3 | Pending-state protocol: `/status/{jobId}`, `hx-trigger="every 1s"`, the progress panel with elapsed time, real denominator, running operation, and the "once per dump" note. |
| 6.4 | Cache-hit fast path: cached results served off the request thread without entering the queue. |
| 6.5 | Cache-state indicator in the dump header ("analyzed 4 minutes ago" vs "computing"). |

**Targets** ([`SERVER.md` §4.3](SERVER.md)): cache-hit filter/sort/page < 50 ms; single-object read
< 50 ms cold or warm; cold walk shows the pending state within 100 ms and never blocks an HTTP
request.

**Decision point.** If cache-hit filter/sort exceeds ~200 ms on the ~1,500-row heap-stat set, the
bottleneck is fragment rendering rather than analysis — fix the rendering. Only if *cold* walk cost
proves intolerable in real use does the tier-2 object index
([`../CLI_DESIGN.md` §6.3](../CLI_DESIGN.md)) come back onto the table, and that is a separate
decision taken with these measurements in hand.

## Phase 7 — Packaging

Depends on 5 and 6.

| Task | Detail |
| :--- | :--- |
| 7.1 | Docker: `serve` entry point on the CLI image, loopback-only port publishing in the wrapper script and in every documented example. |
| 7.2 | Tests per [`SERVER.md` §7](SERVER.md), including the security tests (binding, `Host` header, no CORS) and the cyclic-graph fixture. |
| 7.3 | README section presenting `dndump serve` alongside the CLI and the MCP server, with the loopback constraint and the secrets rationale stated, not implied. |
| 7.4 | Update the `dndump` skill so an agent knows `serve` exists, and knows it is for a human — an agent should keep using the CLI, which is cheaper for it in every respect. |
| 7.5 | Rewrite [`../CLI_DESIGN.md` §10](../CLI_DESIGN.md) to point here instead of describing this as unplanned future work. |

**Exit criterion.** A fresh clone can `dotnet tool install`, run `dndump serve` against a dump, and
investigate a leak end to end. `dotnet format --verify-no-changes` and the full test suite pass on
all three target frameworks.

## Risk register

| Risk | Likelihood | Mitigation |
| :--- | :--- | :--- |
| ClrMD thread-safety violated by a handler that bypasses the queue | Medium | The queue lands in Phase 2 before any real handler exists. Analyzers are not registered in DI as request-scoped services, so a handler cannot reach one except through the queue. |
| A sort or page action silently drops the active filter | **High** | Per-view tests in Phase 4. This bug returns wrong data that looks right. |
| Truncated `gcroot` read as a conclusive answer | Medium | Truncation banner is inside task 5.3, not a follow-up. `state.truncated` in the JSON envelope so API callers see it too. |
| Object-reference tree expands forever on a cyclic graph | **High** — object graphs are routinely cyclic | Cycle detection and depth cap in 5.4, with a deliberately cyclic test fixture. |
| Design resync requires C# changes every time | Medium | Phase 3 exit criterion tests exactly this; keep templates dumb. |
| Docker example published without the `127.0.0.1:` prefix | Medium | One copy-pasted command undoes the entire security posture. Wrapper script emits the safe form; README shows only that form. |
| Long cold walk blocks all other requests | Medium | Inherent to the thread-safety constraint. Mitigated by cache-hit fast path (6.4) and startup warm (6.1), not eliminated. |
| Scope creep into retained size / dominators | Medium | Explicitly out of scope in [`README.md` §4](README.md). The trigger for revisiting is a feature decision, not a performance one ([`../CLI_DESIGN.md` §11.3](../CLI_DESIGN.md)). |
