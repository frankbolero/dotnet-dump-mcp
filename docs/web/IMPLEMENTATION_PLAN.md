# Implementation plan

Status: **in progress** — Phase 0 complete and accepted. Phase 2 complete, measured and tested; all
three exit-criterion checks met, awaiting sign-off. Phases 1, 3–7 outstanding.

Eight phases. Phases 0, 1 and 2 run in parallel; the rest are sequential. Each phase has an exit
criterion that is a demonstrable fact, not a feeling, and the phases that could invalidate later work
carry an explicit measurement.

Read [`README.md` §3](README.md) first for why the ordering is what it is.

## Phase 0 — Core contract ✅ complete

**Track A. No web code. Ships CLI-visible value on its own.**

All seven tasks merged to `feature/web-interface`. Suite at 549 passed / 38 skipped / 0 failed on
`net8.0`/`net9.0`/`net10.0`, `dotnet format --verify-no-changes` clean.

| Task | Status | Detail |
| :--- | :--- | :--- |
| 0.1 | ✅ | `FilterSpec` in `DotNetDump.Core/Models/`, plus `QueryParameters.Filter`. [`DATA_CONTRACT.md` §2.2](DATA_CONTRACT.md) |
| 0.2 | ✅ | Apply the filter in each analyzer between the cached computation and the sort, against the matrix in [`DATA_CONTRACT.md` §2.3](DATA_CONTRACT.md). Declare the honored set with `FilterField`; call `FilterSpec.EnsureSupported` first, so an unsupported filter costs nothing and fails identically cached or not. Includes capturing `Generation` on `HeapObjectItem` during the walk — `listobj` cannot honor the declared set without it. **Runs after 0.5**, see below. Landed as `DotNetDump.Core/Filtering/`: shared primitives plus one predicate per model, each declaring its honored set as a constant. |
| 0.3 | ✅ | `PagedResult<T>.TotalUnfiltered`. Bump the cache `SchemaVersion` — the payload shape changed, and 0.2's new `Generation` field changes it again. One bump covers both. The two per-analyzer `CacheSchemaVersion` constants were collapsed into one declaration first: `ThreadAnalyzer` keys the *shared* heap-exceptions entry with it, so divergent copies would have silently stopped sharing that entry. |
| 0.4 | ✅ | `FilterExpressionParser` in `DotNetDump.Cli` for the `--filter field~value` grammar; wire `--filter` (repeatable) into every list command. Keep `listobj --type` as a distinct **walk-time scope**, *not* an alias for `--filter 'type~'` — aliasing it is a performance regression wearing a rename ([`DATA_CONTRACT.md` §2.4](DATA_CONTRACT.md)). |
| 0.5 | ✅ | Convert the remaining `IEnumerable<T>` analyzer methods to `PagedResult<T>`: `GetGCHandles`, `GetThreads`, `GetDetailedStacks`, `GetThreadStates`, `GetModules`, `VerifyHeap`. Update `JsonFormatter` to use the real totals instead of `FromItemsOnly`. |
| 0.6 | ✅ | Move `DumpResolver` and `SessionFile` from `DotNetDump.Cli` to `DotNetDump.Core`. Pure relocation; the CLI keeps behaving identically. Landed in `Core/Utilities/`, with a CLI-side facade mapping the new Core exceptions back to `CliUsageException`/`DumpLoadException` so the CLI's exception contract is unchanged. |
| 0.7 | ✅ | `IProgress<WalkProgress>` on the five walk-scale methods. The CLI passes `null` — and needed no edits at all, since the parameter is optional. |

**Ordering inside the phase: 0.1 → 0.5 → 0.2 → 0.3 → 0.4 → 0.6 → 0.7.** 0.5 comes before 0.2
because five of the ten filterable analyzer methods still return bare `IEnumerable<T>`. Filtering
them first means writing the filter twice — once against a pre-sliced sequence and again after the
conversion — and the second write is where a filter silently stops being applied. Convert to a
uniform `PagedResult<T>` surface, then filter it once.

`GetModules` belongs in 0.5 because §2.3 gives it a filter set; `VerifyHeap` belongs there for
pagination alone, since it honors no filters.

**Measurement (gates Phase 4).** ✅ **Taken 2026-07-29. Verdict: filters are free. Phase 4 may
proceed.** Reproduce with [`../../scripts/bench-filter.sh`](../../scripts/bench-filter.sh).

On a real dump (9.6 GB core, 1,976 heap-stat rows) with `type~Http` matching 172 rows (8.70%):

| | |
| :--- | ---: |
| Cold walk | 2289.0 ms |
| Warm, unfiltered | 192.1 ms (median of 15) |
| Warm, filtered | 197.9 ms (median of 15) |
| Delta | **5.8 ms (3.0%)** |
| Cold ÷ warm | 11.9× |

The failure this gates is the filter leaking into the cache key and forcing a re-walk, which would
have cost the cold number. It did not: the filtered path is 11.6× cheaper than a walk and within
3% of unfiltered. `FilterSpec` is genuinely excluded from the cache key and §2.1 holds in practice.

Two caveats on reading these numbers:

* **The 5.8 ms is not filter cost.** Substring-matching 1,976 rows is ~0.1 ms; you would need ~50×
  that to explain the gap. The likely cause is JIT — the filtered run is the only one that loads
  `FilterExpressionParser`, `TypeNameMatcher` and `HeapStatItemFilter`. That is a per-process cost
  in a CLI and a once-ever cost in a server.
* **Neither warm number transfers to Phase 6.** Both include ~190 ms of .NET startup plus dump open,
  which `serve` pays once at startup rather than per request. Phase 6's "cache-hit filter/sort/page
  < 50 ms" target is measured in-process against the Phase 2 baseline and must not be compared
  against these figures.

The original wording asked for a sub-millisecond delta. That is not resolvable at CLI granularity
and does not need to be: the decision this gates is whether filter-as-you-type is viable, and at 3%
of a process floor the server does not pay, it plainly is. If the row count grows by orders of
magnitude, re-take this in-process rather than through the CLI.

**Exit criterion.** ✅ **Met and accepted 2026-07-29.**

| Check | Result |
| :--- | :--- |
| `dumpheap --filter 'type~Http' --filter 'size>100mb'` | `total: 0`, `totalUnfiltered: 1976`, exit `0` — honest empty result on a warm cache, no re-walk |
| `clrmodules --filter 'gen=2'` | exit `2`: *"'clrmodules' does not support filtering on Generation. Supported: Module, MinSize, MaxSize, Text."* |

The specified `size>100mb` case returns zero rows on this dump, so composition was confirmed
separately: `type~Http` alone matches 172, `size>5kb` ANDed with it matches 2 — exactly the two
`Http` types whose aggregate exceeds 5,120 bytes. The zero is a true result, not a dropped filter.
`totalUnfiltered` stayed at 1,976 across every combination.

### Carried forward from Phase 0

Found while building it, each affecting a later phase. None blocks Phase 1 or 2.

| Finding | Bears on |
| :--- | :--- |
| **Negation has no `FilterSpec` representation.** The §2.4 grammar lists `!~` and `!=`, but every `FilterSpec` field is a positive match. The CLI parses them and rejects them with a usage error rather than silently dropping them. Whether the type grows negation is an open contract decision. | §2.4, and the Phase 4 filter bar if negation is ever exposed |
| **`listobj`'s cold walk got more expensive.** 0.2 captures `Generation` per object via `heap.GetSegmentByAddress(...)`, adding one call per object to a walk that covers millions. The segment fast path should keep it ~O(1), but this was never measured — the Phase 0 measurement covers *warm filter* cost, not cold walk cost. | Phase 6's startup warm (6.1) and pending-state timings (6.3) |
| **Three filterable methods never touch the cache.** `GetGCHandles`, `GetThreads` and `GetThreadStates` have no `GetOrCompute` call — pre-dating this work. "Filters are free" rests on the filter running over a *cached* result, so for these three each call re-enumerates. `gchandles` is walk-scale. | Phase 6, and any Phase 4 view backed by those three |
| **`WalkProgress.FractionComplete` may never reach 1.0.** `BytesSeen` sums live object sizes; `TotalBytes` sums segment lengths. If a segment's length includes committed-but-unallocated space, the fraction stalls short of 100%. The counts stay honest and `ReportFinal()` always emits the true totals — but a progress bar must treat walk completion, not the fraction, as its terminal signal. | 6.3, the pending-state progress panel |
| **Core writes to `Console.Error`.** `Core.Utilities.DumpResolver.ResolveAndLoad` prints `Dump: <path>` unless `quiet: true` — relocated verbatim from the CLI in 0.6. The web host must pass `quiet: true` or print a dump banner to its own stderr on every resolve. | 2.2, `dndump serve` |

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

**Measurement (gates Phase 6).** ✅ **Taken 2026-07-30. Verdict: every Phase 6 target is already met
by the naive implementation. 6.4's cache-hit fast path is not needed for speed.** Reproduce with
[`../../scripts/bench-serve.sh`](../../scripts/bench-serve.sh).

Same dump as Phase 0 (9.0 GiB core, 1,976 heap-stat rows), Release `net9.0`, isolated
`DNDUMP_CACHE`, medians of 15, `--no-warm` throughout so the numbers describe the request path
rather than 6.1's background warm.

| | Cold cache | Warm cache |
| :--- | ---: | ---: |
| Process launch → listening | 1081.9 ms | 1063.9 ms |
| Process launch → first byte of `GET /` | 3316.8 ms | 1139.9 ms |
| **Implied cost of the step itself** | **2234.9 ms** (the walk) | **76.0 ms** (first render) |

Against a process that is already up and warm:

| Request | Median |
| :--- | ---: |
| `GET /views/dumpheap` | 0.6 ms |
| `GET /views/dumpheap?type=Http` | 0.8 ms |
| `GET /views/dumpheap?type=Http&sort=count&order=asc` | 0.7 ms |
| `GET /api/dumpheap` | 0.6 ms |
| `GET /health` — the HTTP floor | 0.4 ms |

Four things follow, and they change what Phase 6 is for:

* **The §4.3 targets are met with ~70× of headroom.** "Cache-hit filter/sort/page < 50 ms" is
  0.7 ms. Subtracting the 0.4 ms `/health` floor leaves ~0.2–0.4 ms of actual analysis plus
  fragment rendering, so most of the number is localhost round trip. The **decision point** below
  ("if cache-hit filter/sort exceeds ~200 ms the bottleneck is fragment rendering") is decisively
  not triggered, and the tier-2 object index stays off the table.
* **Filtering and sorting are free again, in-process this time.** Filtered costs 0.2 ms more than
  unfiltered and filtered-plus-sorted costs less than filtered — i.e. the difference is inside the
  noise. This is the in-process confirmation Phase 0 said it could not give at CLI granularity.
* **6.4's cache-hit fast path is no longer a performance task.** Serving a cached result off the
  request thread cannot improve on 0.6 ms. Its remaining value is that a cache hit should not queue
  behind a cold walk — a *latency-under-contention* property, not a throughput one. That is worth
  keeping, but it should be built and justified as such, and it is no longer urgent.
* **Startup is dominated by the host, not the dump.** Listening at ~1.07 s is the same cold and
  warm, so it is not the walk and not the cache; Phase 0 measured ~190 ms for .NET startup plus
  dump open in the CLI, which leaves roughly 0.9 s of ASP.NET Core host construction (MVC, the
  Razor view engine, DI). If startup ever needs to come down, that is where it is, and
  `AddMvcCore().AddRazorViewEngine()` in place of `AddControllersWithViews()` is the first thing to
  try. Not urgent: it is paid once per `serve`, not once per request.

The 76 ms warm first-render, against 0.6 ms steady state, is JIT plus first-touch of the Razor
pipeline and the cache deserializer. That is precisely the cost 6.1's startup warm exists to move
off the user's first request, and it is now a measured 76 ms rather than a guess.

**Exit criterion.** ✅ **All three met 2026-07-30. Awaiting sign-off.**

| Check | Result |
| :--- | :--- |
| `dndump use X && dndump serve` shows real heap statistics | ✅ Verified against the 9.0 GiB core through both `--dump` and the `.dndump/session.json` path; `GET /` renders 1,976 type rows, and `/api/dumpheap?type=Http` reports `total: 172, totalUnfiltered: 1976` — the same 172 Phase 0 measured |
| `curl -H 'Host: evil.example' http://127.0.0.1:5111/` is rejected | ✅ `400`, and covered by `WebSecurityTests` against a real Kestrel socket. Also rejected: a loopback name on the wrong port, `localhost` with no port, an absent `Host`, `127.0.0.1.evil.example`, and `localtest.me` (a real hostname resolving to 127.0.0.1 — rebinding without the attacker controlling DNS at request time) |
| Two concurrent requests are provably serialized (test, not inspection) | ✅ `AnalysisQueueTests.ConcurrentSubmissions_NeverOverlapOnTheWorker`: 24 submitters released from a common start line, proven twice over — an occupancy counter whose peak must equal 1, and recorded enter/exit intervals that must not intersect after sorting. Mutation-verified: replacing `item.Run` with `ThreadPool.QueueUserWorkItem` fails 9 of 18 tests including this one |

**Test suite.** 120 tests added across four suites, **669 passed / 0 failed / 38 skipped** on each of `net8.0`, `net9.0`, `net10.0` (the 38 skips are the pre-existing sample-dump integration tests). `dotnet format --verify-no-changes` clean. No agent modified a line of implementation — the whole diff since the measurement commit is four new test files.

| Suite | Tests | Notes |
| :--- | ---: | :--- |
| `AnalysisQueueTests` | 18 | Serialization, FIFO, thread affinity, cancellation, re-entrancy, failure isolation, shutdown, depth |
| `WebSecurityTests` (+ decision table, + binding env) | 52 | All eight controls mutation-verified able to fail |
| `ViewRequestBinderTests` | 46 | Query-string contract and honored-field matrix |
| `HtmxIntegrityTests` | 4 | SRI hash recomputed from the vendored file |

Two tests are honest about being weaker than they look, and say so in their own doc comments rather than reading as coverage they do not provide:

* **`HostHeader_Absent_IsRejected` does not exercise our middleware.** Kestrel rejects an absent `Host` with its own `400` before `LoopbackHostMiddleware` runs, so it stays green even with `IsLoopbackHost` replaced by `return true`. It is a valid end-to-end property; the middleware's own absent-`Host` branch is covered by `LoopbackHostDecisionTests`, which does go red under that mutation.
* **`AbandonedJobThatThrows_ProducesNoUnobservedTaskException` is GC- and finalizer-dependent.** It cannot fail spuriously — only an exception carrying that run's unique sentinel is counted — but it can pass spuriously if the faulted task is not finalized inside the collection loop. Mutation-verified to fail when the `OnlyOnFaulted` continuation is deleted from `AwaitAbandonable`.

The environment-clearing in `DumpWebHost.Build` needed a test of its own: deleting it leaves every address-inspecting test green, because the explicit `kestrel.Listen` overrides `urls` regardless. That is what makes it a genuine second lock and what made it invisible. `Build_ClearsTheBindingEnvironmentVariables` observes the lock rather than the door, and was itself mutation-verified.

**Known gaps, deliberate.** No IPv6 non-loopback reachability assertion (IPv4 only). No coverage of `/views/{view}/rows`, `/trees/{tree}/{id?}` or `/status/{jobId}` — not implemented at this phase. `Dispose`'s 10-second `ShutdownGrace` timeout path is untested, since exercising it would put a 10-second stall in the suite.

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

## Delegation

Most of this plan is well-specified enough to hand to a subagent. What follows is the division and
the model for each task. The organising principle: **an agent gets a task when the task has an
oracle** — a compiler, a test, or a grammar in [`DATA_CONTRACT.md`](DATA_CONTRACT.md) that says
unambiguously whether the work is right. Tasks whose correctness is a judgment call about what the
rest of the system will have to live with stay with the lead.

### Working agreement

Applies to every delegated task without exception.

* **Worktree per agent**, branched from `feature/web-interface`, named `web/<task>-<slug>` (e.g.
  `web/0.4-filter-parser`). No agent commits to `feature/web-interface`; the lead merges.
* **Atomic commits.** One logical change per commit. The solution builds and the full test suite
  passes at *every* commit, not just the last one. `dotnet format` before each. No "WIP" commits and
  no commit whose only purpose is fixing the one before it — rewrite that history before handing
  the branch back.
* **All three target frameworks.** `net8.0;net9.0;net10.0`. Code that compiles on one is not done.
* **One owner per file.** Where a phase fans out over near-identical items, the shared files —
  command registration, `_Layout`, navigation, DI wiring — are edited *once by the lead before
  fan-out*. Parallel agents editing a shared registration file produce merge conflicts that cost
  more than the parallelism saved.
* **Agents do not run measurements.** Every measurement here needs a real dump and a warm cache on
  the developer's machine. An agent reporting a timing it could not have produced is worse than no
  number.
* **The deliverable includes what was not done.** An agent hands back the branch plus an explicit
  list of anything it skipped, stubbed, or could not verify.

### Task assignment

| Task | Model | Rationale |
| :--- | :--- | :--- |
| 0.1 `FilterSpec` | **Lead / Opus** | The type every later phase binds to. Cheap to write, expensive to change once eight analyzers and a query binder depend on it. |
| 0.2 Filter application + honored fields | **Sonnet** | Repetitive across analyzers once 0.1 and 0.5 exist. The honored-field matrix is settled in [`DATA_CONTRACT.md` §2.3](DATA_CONTRACT.md) and is not the agent's to change; a field that cannot be honored is a finding to report back, not a row to quietly drop. |
| 0.3 `TotalUnfiltered` + `SchemaVersion` bump | **Haiku** | Mechanical, compiler-guided. |
| 0.4 `FilterExpressionParser` + `--filter` wiring | **Sonnet** | A pure parser against a written grammar, with unit tests as the oracle. The single best-shaped delegation in the plan. |
| 0.5 `IEnumerable<T>` → `PagedResult<T>` (5 methods) | **Sonnet** | Repetitive and compiler-guided, but `JsonFormatter` must stop using `FromItemsOnly` and start reporting real totals — that part is easy to miss. Sequenced before 0.2. |
| 0.6 Move `DumpResolver`/`SessionFile` to Core | **Haiku** | Pure relocation. The oracle is "the build passes and the CLI tests are untouched". |
| 0.7 `IProgress<WalkProgress>` on the walk methods | **Sonnet** | Threading a parameter through five methods without changing any existing behaviour. |
| Phase 1 (all) | **Not delegated** | Design iteration in Claude Design, plus taste calls a subagent has no basis for. |
| 2.1 `DotNetDump.Web.csproj` + solution entry | **Haiku** | Scaffolding. |
| 2.2 `dndump serve` | **Sonnet** | Ordinary command wiring on top of 0.6's shared resolver. |
| 2.3 **`IAnalysisQueue`** | **Lead / Opus** | The correctness spine of the whole server. Serialization, cancellation and job identity have to be right before any handler exists, and the failure mode — ClrMD corruption under concurrency — does not present as an obvious bug. |
| 2.4 Security middleware | **Opus** | Small, and the one thing in the project that cannot be subtly wrong. |
| 2.5 Vendor htmx | **Haiku** | Download, pin, hash. |
| 2.6 `ViewRequestBinder` | **Sonnet** | Spec'd in [`DATA_CONTRACT.md`](DATA_CONTRACT.md), fully unit-testable. |
| 2.7 First route end to end | **Sonnet** | Placeholder markup by definition; the plumbing is the point. |
| 3.1 Razor conversion pattern | **Lead / Opus** | Sets the template every later view copies. "Keep templates dumb" is precisely the constraint an agent will violate to make one view nicer. Do the first component yourself, then delegate against it. |
| 3.2 App shell + navigation | **Sonnet** | Shared files — do this before any 3.3/3.4 fan-out. |
| 3.3 Eight list views | **Sonnet**, then **Haiku** | First two by Sonnet to prove the pattern generalises; the remaining six are copy-and-adapt. Fan out at most three agents, partitioned by view. |
| 3.4 Eleven detail views | **Sonnet**, then **Haiku** | Same shape as 3.3. |
| 3.5 Resync procedure | **Sonnet** | Documenting a boundary the agent has just worked inside. |
| 4.1–4.3 Filter bar, chips, sortable headers | **Sonnet** | Well-specified htmx wiring. **The lead writes the filter-preservation test first** (see the risk register) and the agent makes it pass; an agent asked to test its own work here will write the test that passes. |
| 4.4 Infinite scroll | **Sonnet** | Sentinel protocol is fully specified. |
| 4.5 Out-of-band updates | **Sonnet** | |
| 4.6 URL state round-trip | **Sonnet** | Round-trip property is its own oracle. |
| 5.1 Namespace rollup | **Sonnet** | Pure grouping over cached data. The "split on `.` only outside `<>`" rule is fiddly and deserves a dedicated test set. |
| 5.2 Thread → frames | **Sonnet** | Note the explicit permission to drop locals — an agent will otherwise burn effort proving ClrMD can do something it cannot. |
| 5.3 gcroot retention paths | **Opus** | Truncation semantics carry the plan's most consequential correctness requirement, and the tempting shortcut is to ship the tree and defer the banner. |
| 5.4 Object reference navigator | **Opus** | Cycle detection, visited-set node identity and the depth cap against a routinely-cyclic graph. A **High** risk row. |
| 6.1 Startup cache warm | **Sonnet** | |
| 6.2 Progress plumbing | **Sonnet** | Consumes 0.7's interface. |
| 6.3 Pending-state protocol | **Sonnet** | |
| 6.4 Cache-hit fast path | **Opus** | Deliberately bypasses the queue. Whether that is safe for a given result type is exactly the reasoning 2.3 exists to protect. |
| 6.5 Cache-state indicator | **Haiku** | |
| 7.1 Docker | **Sonnet** | Loopback prefix in every emitted example — verify, don't assume. |
| 7.2 Security tests + cyclic-graph fixture | **Opus** | The last line of defence for two **High** risks. A weak test here is worse than no test, because it reads as coverage. |
| 7.2 Remaining tests | **Sonnet** | |
| 7.3 README section | **Sonnet** | |
| 7.4 `dndump` skill update | **Sonnet** | |
| 7.5 Rewrite `CLI_DESIGN.md` §10 | **Haiku** | Replacing a section with a pointer. |

### Never delegated

Every **Measurement** and **Decision point** block in this plan, the honored-field matrix in 0.2,
and the acceptance of any phase's exit criterion. Those are the points where the plan can change,
and an agent working inside a single task has no standing to change it.

## Risk register

| Risk | Likelihood | Mitigation |
| :--- | :--- | :--- |
| ClrMD thread-safety violated by a handler that bypasses the queue | Medium | The queue lands in Phase 2 before any real handler exists. Analyzers are not registered in DI as request-scoped services, so a handler cannot reach one except through the queue. |
| A delegated agent reports a measurement or an exit criterion it did not verify | Medium | Agents do not run measurements, and do not accept exit criteria. Both are the lead's, per the delegation working agreement. |
| A sort or page action silently drops the active filter | **High** | Per-view tests in Phase 4. This bug returns wrong data that looks right. |
| Truncated `gcroot` read as a conclusive answer | Medium | Truncation banner is inside task 5.3, not a follow-up. `state.truncated` in the JSON envelope so API callers see it too. |
| Object-reference tree expands forever on a cyclic graph | **High** — object graphs are routinely cyclic | Cycle detection and depth cap in 5.4, with a deliberately cyclic test fixture. |
| Design resync requires C# changes every time | Medium | Phase 3 exit criterion tests exactly this; keep templates dumb. |
| Docker example published without the `127.0.0.1:` prefix | Medium | One copy-pasted command undoes the entire security posture. Wrapper script emits the safe form; README shows only that form. |
| Long cold walk blocks all other requests | Medium | Inherent to the thread-safety constraint. Mitigated by cache-hit fast path (6.4) and startup warm (6.1), not eliminated. |
| Scope creep into retained size / dominators | Medium | Explicitly out of scope in [`README.md` §4](README.md). The trigger for revisiting is a feature decision, not a performance one ([`../CLI_DESIGN.md` §11.3](../CLI_DESIGN.md)). |
