# Implementation plan

Status: **in progress** — Phase 0 complete and accepted. Phase 1 delivered, with three deviations
recorded below. Phase 2 complete, measured, tested, and signed off. Phase 3's five tasks (3.1–3.5)
are all complete and its restated exit criterion mechanically verified, awaiting sign-off. Phase 4's
six tasks (filter bar, chips, sortable headers, infinite scroll, OOB row count, URL round-trip) are
all complete and verified against a real dump; its exit criterion's 10.2M-object scale claim
specifically is not yet measured (see Phase 4's own notes). Phase 5's four trees are all complete,
merged, and verified against the real dump — see Phase 5's own notes on what "keyboard navigable"
means for a native `<details>`-based tree and what has and has not been checked. Phase 6 outstanding.
Phase 7's task 7.1 (Docker reachability for `serve`) is done, see its own notes on what is and is
not verified; 7.2–7.5 outstanding.

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

## Phase 1 — Design ✅ delivered

**Track B. Calendar time, no code dependency. Start on day one.**

| Task | Status | Detail |
| :--- | :--- | :--- |
| 1.1 | ✅ | Review and adjust [`DESIGN_BRIEF.md`](DESIGN_BRIEF.md) — especially the §3 sample data, which should be replaced with rows from an actual dump you have. |
| 1.2 | ✅ | Create the Claude Design project; build the component library against the brief. Landed as **Nocturne**: a dark-first, compact interface on a near-neutral blue-grey ground with a single blurple accent. |
| 1.3 | ✅ | Iterate on the four highest-risk components first: **data table**, **tree view**, **pending/progress panel**, **truncation banner**. All four are present. |
| 1.4 | ✅ | `/design-sync` into `design-sync/` — seven component pages (`Shell`, `Data`, `Detail`, `Filtering`, `Trees`, `Status`, `Canvas`) plus a design-system directory. |

**Exit criterion.** ⚠️ **Met on substance; three deviations, all resolved in Phase 3 rather than by
re-running the design.**

| Check | Result |
| :--- | :--- |
| Every [`DESIGN_BRIEF.md` §5](DESIGN_BRIEF.md) component exists | ✅ All six groups delivered |
| Previews with real sample data | ✅ Real type names, real magnitudes |
| Works in light and dark | ✅ Both palettes exist — as a `themeTokens(mode)` function in `Shell.dc.html`, not in the stylesheet, which is why a first look for `prefers-color-scheme` found nothing |
| The long-`Dictionary` type name does not break the layout or hide its tail | ✅ Handled, though by a fixed 38/14 character split in JavaScript. Phase 3.1 replaced that with a responsive CSS split — see below |

### What the sync actually produced, and why Phase 3 needed a translation layer

The plan assumed synced files could be used as templates: [`README.md` §1](README.md) says
"Claude Design emits plain HTML/CSS, so synced files are used as templates rather than ported into
components — and re-ported on every resync." **That assumption did not hold.**

Across the seven component pages there are **586 inline `style=` attributes and zero `class=`
attributes**. Every value is computed in JavaScript from `themeTokens(mode)`. The design system
directory *does* ship a class-based stylesheet, but no component page uses it, and it lacks roles
the components rely on — `surfaceAlt`, `accentSoft`, `accentText`, the whole `warn`/`danger`/`ok`
set — as well as the light palette entirely.

`themeTokens()` is therefore the real source of truth, and 3.1 extracted it into
`wwwroot/css/dndump.css`. The design system's own stylesheet is deliberately **not** linked: two
token systems where nothing consumes one of them drift silently.

The consequence for the Phase 3 exit criterion is real and should be read plainly: a **token** change
resyncs into the `:root` blocks and nothing else, but a **structural or component-level** redesign
requires re-extraction. That is weaker than "no C# edits ever", and it is a property of what the
design tool emits rather than of how the templates were written. The procedure is
[`../../src/DotNetDump.Web/DESIGN_RESYNC.md`](../../src/DotNetDump.Web/DESIGN_RESYNC.md).

### Deviations from the design, made deliberately in 3.1

Each is recorded in `DESIGN_RESYNC.md` so a resync does not silently restore it.

| Deviation | Why |
| :--- | :--- |
| **Fonts vendored locally** | Every component page loads Inter and JetBrains Mono from `fonts.googleapis.com`. [`SERVER.md` §6](SERVER.md) forbids outbound requests of any kind from a tool that renders heap strings containing connection strings, tokens and PII. Both are now local latin-subset variable faces, 80 KB total. **Not negotiable, not a judgement call.** |
| **Dark is the default; light is opt-in** | Departs from [`DESIGN_BRIEF.md` §6](DESIGN_BRIEF.md), which asked for `prefers-color-scheme` with a `data-theme` override. Nocturne is dark-first — its readme opens "a quiet, compact dark interface", its guidance is written throughout in terms of a dark ground, and its own `Shell.dc.html` boots dark with a manual toggle. Following the OS preference put a developer on a light machine in front of the face the product was not composed in, and it read as wrong without being nameable. Two lines revert it. |
| **Middle truncation is responsive, not fixed** | The design cuts type names at a fixed 38/14 character split, which cuts short names on a wide screen and long ones anyway on a narrow one. `MiddleTruncated` splits off a fixed-length tail and CSS flexes the head, so the visible cut follows the column width. The tail is never cut — for a .NET type name the tail is what distinguishes it. |

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

**Exit criterion.** ✅ **All three met 2026-07-30. Signed off 2026-07-31.**

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

Depends on 1 and 2. All five tasks (3.1–3.5) complete. Exit criterion restated and mechanically
verified below; awaiting sign-off.

| Task | Status | Detail |
| :--- | :--- | :--- |
| 3.1 | ✅ | Convert the synced components into Razor views taking view models. "Structure and classes stay byte-identical" was not achievable — there are no classes to keep, see Phase 1 above — so the pattern is instead: extract to `wwwroot/css/dndump.css`, and **no `.cshtml` may carry a `style=` attribute**. Demonstrated on the data table, the highest-risk component. |
| 3.2 | ✅ | App shell, navigation across the six view groups, dump header bar. Collapsible nav (pure-CSS checkbox), theme toggle (one inline script, needed to set `data-theme` before first paint), and per-view `Command`/`Description` on `ViewDescriptor` for all 25 views. `DumpInfoService` memoizes `SessionAnalyzer.GetInfo` behind one `Enqueue` so the header does not re-enter the queue per request. |
| 3.3 | ✅ | Every list view on the data table component. All eight wired: `dumpheap` (3.1), `clrmodules`, `clrthreads`, `listobj`, `gchandles`, `syncblk`, `threadstate`, `printexception`. The last seven landed as five delegated branches (two Sonnet, proving the pattern generalizes; three Haiku, copy-and-adapt, partitioned as listobj alone / gchandles+syncblk / threadstate+printexception), each in its own worktree per the working agreement. Every merge but one was a trivial additive conflict in `DumpRoutes.cs`'s two switch statements — different agents' cases landing adjacent — resolved by keeping both sides. `ViewRoutingTests.UnwiredView` moved from `gchandles` to `info` once every list view was wired. |
| 3.4 | ✅ | Every detail view. **Seventeen, not the eleven this table used to say** — the original list was taken from [`DATA_CONTRACT.md` §3.1](DATA_CONTRACT.md)'s navigation grouping, which omits `dumpobj`, `gcroot`, `verifyobj`, `verifyheap`, `dumpstack`, `clrstack` and `eestack`. Full set: `info`, `eeheap`, `verifyheap`, `verifyobj`, `dumpobj`, `gcroot`, `dumpstack`, `clrstack`, `eestack`, `threadpool`, `dumpmodule`, `dumpassembly`, `dumpmt`, `dumpmd`, `dumpclass`, `name2ee`, `ip2md`. Sixteen wired; `gcroot` deferred to 5.3 as planned. The lead built `info` and `dumpobj` first as the two reference `DetailModel<T>` shapes (plain key-value, and address-identity card + table), then fanned the remaining fourteen out to six agents: three Sonnet for the shapes with no precedent yet (`verifyobj`+`dumpclass`; `dumpstack`+`verifyheap`, both `ViewKind.Detail` despite being `PagedResult`-backed; `name2ee`'s dedicated two-path-segment route), three Haiku copying the established shapes. Two of six agents' worktrees tracked a stale git ancestor despite the actual file content being current and correct — caught by diffing against the real tip before merging, and hand-applied instead of merged where a normal merge would have let git's algorithm compare against the wrong shared history. One agent's Razor file had a genuine `@{ }`-inside-an-already-open-code-block bug (`RZ1010`) caught by the lead's own build, not the agent's report of a clean one. Verified end to end against a real 9.6 GB dump: full suite at 722/0/0 with `DOTNETDUMP_TEST_DUMP` set, plus a manual `dndump serve` + `curl` pass over all sixteen wired views and `gcroot`'s continued 501. |
| 3.5 | ✅ | Resync procedure at [`../../src/DotNetDump.Web/DESIGN_RESYNC.md`](../../src/DotNetDump.Web/DESIGN_RESYNC.md) brought to final form now that all 25 views exist. Added the four-fragment-shape inventory (table, key-value, identity-card, card-list) that only became visible once 3.4 wired all seventeen detail views, the `{address?}`/`name2ee` routing convention, and which of the seven synced component pages this codebase actually consumes (`Shell`, `Data`, `Detail`; `Filtering`/`Trees`/`Status` await Phases 4–6). Re-verified mechanically against the current tree rather than re-asserted: `grep` for `style=` across `Views/` matches only prose inside doc comments (0 real attributes), and `https?://` matches nowhere. |

**3.4's address-argument routing — decided.** `/views/{view}/{address?}`, an optional trailing route
segment, not a query parameter; `/api/{view}/{address?}` mirrors it for JSON parity (`SERVER.md`
§2). Three reasons: the trees in [`DATA_CONTRACT.md` §4.3/§4.5](DATA_CONTRACT.md) already route
addresses this way (`gcroot/{address}`, `object/{address}`), and a second convention for the same
kind of value would be its own bug; §3.2's "the query string *is* the view state" is a rule about
*list*-view filter/sort/page state composing under `hx-include`, not about a detail view's identity
— an address is not a filter dimension alongside `sort` and `limit`, it names which record the page
*is*; and it leaves `ViewRequestBinder` untouched, since neither `FilterSpec` nor `QueryParameters`
apply to a single-record view. The route and handler plumbing for the optional segment lands as
shared-file setup immediately before 3.4 fans out, per the working agreement below — not before,
since nothing consumes it yet and an unused code path is its own kind of bug.

**Correction to the count: eight views need it, not seven, and one needs a different shape entirely.**
The original list of seven missed `dumpassembly` and `ip2md` — both take a single hex address exactly
like the other six (`ModuleAnalyzer.GetAssemblyDetails(address)`, `ModuleAnalyzer.GetMethodByIP(address)`).
The address route covers eight: `dumpobj`, `verifyobj`, `dumpmt`, `dumpmd`, `dumpclass`, `dumpmodule`,
`dumpassembly`, `ip2md`. `gcroot` also takes an address but is out of scope for 3.4 — it is a tree
(5.3) with its own routed shape already in §4.3. `name2ee` takes two independent strings
(`<module> <type>`), not an address, and needs its own shape when 3.4 reaches it; this decision does
not cover it.

**3.3's `listobj` — no separate walk-time scope in the web UI, deliberately.**
`HeapAnalyzer.GetObjects` takes a `typeFilter` that narrows the walk itself and sits in the cache
key, distinct from `parameters.Filter`'s post-walk filter — the distinction 0.4 protects for the
CLI's `--filter` grammar precisely so `--type` is not aliased into it. The web contract in §3.2 has
exactly one `type` parameter, shared across every list view and mapped to `FilterSpec.TypeName`.
Rather than inventing a second `type`-shaped control for `listobj` alone, the web view passes
`typeFilter: null` and always does the full walk, letting `type` filter post-walk like everywhere
else. That trades away the walk-time pruning that makes a first-ever `listobj?type=Http` cheap on a
huge heap; it is deferred, not silently dropped, because Phase 4's 10.2M-object exit criterion is
exactly where it would start to matter. If the full walk proves intolerable there, that is a
Phase 4/6-style decision point, taken with a real measurement, not a 3.3 one.

3.3 has no routing question and is proceeding independently of the above.

**Exit criterion.** All 25 commands are reachable and render correctly styled output. Re-running
`/design-sync` after a purely visual change in Claude Design requires no C# edits — if it does, 3.1
made the templates too clever and needs correcting before Phase 4 builds on them.

⚠️ **The second half of this criterion needs restating before it can be judged.** As written it
assumes the design tool emits reusable templates; it emits inline-styled previews (Phase 1 above).
The property actually worth holding is the one `DESIGN_RESYNC.md` states: **a token change requires
no C# edit, and no `.cshtml` may carry a `style=` attribute.** Both are mechanically checkable —
`curl -s http://127.0.0.1:5111/ | grep -c 'style='` must print `0`. The stronger reading, that any
visual change is C#-free, is not achievable with what this design tool produces, and recording it as
met would be false.

**Restated property verified 2026-07-31, at 3.5's final form of `DESIGN_RESYNC.md`.** `grep -rn
'style=' Views/` across all 25 wired views matches only prose inside `@* ... *@` doc comments — zero
real attributes — and `grep -c 'https\?://'` matches nowhere. The restated criterion holds; awaiting
sign-off alongside the rest of Phase 3.

**Suite as of 3.2.** 679 passed / 0 failed / 40 skipped on each of `net8.0`, `net9.0`, `net10.0`;
`dotnet format --verify-no-changes` clean. The two new skips are `WiredViewRoutingTests`, which need
a dump — run with `DOTNETDUMP_TEST_DUMP=<path>`; both pass against the 9.6 GB core.

**Unchanged at 3.3, then verified against a real dump.** 679/0/40 on all three frameworks after
wiring the remaining seven list views — the generic `ViewRoutingTests` (page-vs-fragment,
catalog-vs-404) already exercise a newly wired view automatically, so no new tests were needed for
that property. None of the delegated agents had `DOTNETDUMP_TEST_DUMP` available, so none of the
seven were checked against real row data at merge time. Run afterward with
`DOTNETDUMP_TEST_DUMP=core_20260727_211646` (9.6 GB core, checked into the repo root): the full
suite goes to 719/0/0 (the two dump-gated skips resolve and pass). Separately, `dndump serve`
against the same dump and a `curl` of all eight `/views/{view}` fragments and one `/api/{view}`
route confirmed real rows render correctly — including `type=String` filtering on `listobj` despite
its no-walk-time-scope decision above, and a full page (nav, shell) on direct navigation to
`threadstate`.

### Carried forward from Phase 3

| Finding | Bears on |
| :--- | :--- |
| **The navigation linked at the fragment route.** `/views/{view}` returns an HTML fragment, but the nav linked straight to it, so following any link produced a bare table with no `<html>`, stylesheet or navigation — for every view, not only the unwired ones. Fixed by branching on the `HX-Request` header: htmx gets the fragment, a browser gets the whole document. Found by using the product, not by a test. | 4.6's URL round-trip, which requires `/views/dumpheap?type=Http` to be pasteable |
| **A decorative header bar could take down every page.** `DumpInfoService` propagated `SessionAnalyzer.GetInfo`'s "No dump loaded" exception, and since the header renders on every route, one failure 500'd the whole UI — permanently, because `Lazy<Task<T>>` caches a faulted task as readily as a successful one. It now degrades to blank metadata. | Any future shell-wide data; the same trap applies to 6.5's cache-state indicator |
| **`ViewCatalog` is a second copy of the command surface, and it drifted immediately.** `verifyobj` was missing from the moment the catalog was written, and it failed silently — a missing view simply never appears in the navigation. `ViewCatalogCoverageTests` now makes the CLI the oracle in both directions. | Any future command; the catalog cannot be trusted to be complete by inspection |
| **`clrstack` and `eestack` still have no `QueryParameters` overload.** They are backed by `GetStackTraceGroups`, which takes none, so they get no filter bar and no pagination. This is settled in [`DATA_CONTRACT.md` §2.3](DATA_CONTRACT.md) and the catalog refuses the pressure, but 3.4 will feel it again. | 3.4, and 4.1's per-view filter bar |
| **`dumpstack` appears to under-walk stacks on at least one real dump.** Against the 9.6 GB core, every thread's `GetDetailedStacks` result had exactly one frame, and every one of those frames carried the identical instruction pointer and an identical `[(unknown)]` method name — found while smoke-testing 3.4's wiring, not introduced by it; the web view faithfully renders whatever `ThreadAnalyzer.GetDetailedStacks` returns, and the CLI would show the same thing against this dump. Not investigated further — it reads as a `ThreadAnalyzer`/ClrMD stack-walking limitation, out of scope for a view-wiring phase. | Any future work on `ThreadAnalyzer.GetDetailedStacks`; worth a real look before `dumpstack` is presented as trustworthy |

## Phase 4 — Interaction

Depends on 0 and 3. This is where the interface becomes usable rather than viewable. **All six
tasks complete. Exit criterion partly verified — see below.**

| Task | Status | Detail |
| :--- | :--- | :--- |
| 4.1 | ✅ | Filter bar wired per view, exposing only the fields that view honors. `hx-trigger="input changed delay:250ms"`, `hx-include="closest form"`, `hx-push-url="true"`. Data-driven over `ViewDescriptor.HonoredFilters` (`Rendering/FilterBar.cs`) rather than 8 hand-copied field lists, rendered through one shared `Views/Shared/_FilterBar.cshtml`. |
| 4.2 | ✅ | Active-filter chips with individual and clear-all removal. Each chip's own URL keeps every other active filter and drops only its own field; "clear all" drops every filter field but keeps `limit`. |
| 4.3 | ✅ | Sortable headers with three-state `aria-sort`, preserving the active filter. Only columns a view's analyzer has a dedicated `SortBy` string for get a sortable header — e.g. `dumpheap` sorts on `count`/`typename` only, `MethodTable`/`Total size` stay plain. |
| 4.4 | ✅ | Infinite scroll: `GET /views/{view}/rows` answers with the next page's `<tr>` rows plus a fresh sentinel, driven by `PagedResult.HasMore`. Each view's row loop factored into a shared `_{View}Rows.cshtml` partial so the initial page and the incremental route render identically rather than drifting apart. |
| 4.5 | ✅ | Out-of-band update for the view header's row count, on both the htmx-fragment path and the `/rows` continuation. **Narrower than the row's original wording — see below.** |
| 4.6 | ✅ | URL state round-trip. Turned out to be verification-only: every 4.1–4.3 control already carried `hx-push-url="true"` correctly (audited across all 8 views, not assumed), and the infinite-scroll sentinel correctly carries none. The actual gap was that no test had ever requested a filtered/sorted URL **without** `HX-Request` — i.e. a fresh full-page load, exactly what a pasted URL or a back/forward-restored history entry produces. `UrlRoundTripTests.cs` closes that gap for both a text-filter view (`dumpheap`) and a `<select>`-kind filter view (`clrthreads`), each reconstructing the correct filter value, chip and `aria-sort` state from a cold request. |

**4.6's proxy for "the back button undoes a filter change."** No browser automation exists in this
project, so the back button itself is untestable directly. The credible server-side property, stated
explicitly in the test's own doc comment: a back/forward navigation is, from the server's perspective,
indistinguishable from "request the previous URL again" (the browser or htmx's history cache re-issues
a plain `GET`, per `DumpRoutes.WantsFragment`'s `HX-History-Restore-Request` handling). If two
different query strings each independently and deterministically reconstruct correct state when
requested fresh — which `UrlRoundTripTests` now proves — back and forward are correct by construction,
because pressing them cannot produce a request this suite has not already covered.

**4.5's scope, corrected against what the codebase actually has.** The plan row and `SERVER.md` §5.2
both mention three things — result count, pagination footer, cache-state indicator — but only the
first is real work this task could do. `IAnalysisCache.GetOrCompute<T>` has no way to tell a caller a
cache hit from a fresh computation, let alone a timestamp, so a genuine cache-state indicator needs
that plumbing built first — squarely Phase 6's job (6.1 startup warm, 6.4 cache-hit fast path, 6.5
the indicator itself), not invented here as a fake/static stand-in. And no distinct "pagination
footer" element exists anywhere in the design: `DESIGN_BRIEF.md`'s Shell group explicitly specifies
"no footer chrome," and none of the seven `design-sync/*.dc.html` pages show one — the view header's
row count is the only element either mention could be describing, written before 4.1–4.4 existed to
give it a concrete home. Scoped down to that one element before delegating, and the agent's own
`grep` across the design pages confirmed the same conclusion independently.

**A wrinkle the agent found and the lead's own brief got wrong.** The count does not numerically
"climb" as more pages load via infinite scroll — `PagedResult.TotalAvailable`/`TotalUnfiltered`
describe the full filtered/unfiltered result, independent of `Offset`/`Limit`, so "172 of 1,976 rows"
reads identically on every page under a fixed filter. The actual bug 4.5 fixes is narrower than that:
the count was computed on *every* fragment request already (`ListModel<T>.CountSummary`) but silently
discarded on the htmx-fragment-only path and never present at all on `/rows`, so the header simply
froze at whatever a full page load last showed — including through a filter or sort change. Caught in
review: a doc comment the agent wrote still claimed the "keeps climbing" framing after the agent's
own test proved otherwise; corrected before merge.

**Verified.** Full suite: 737/0/0 with the 9.6 GB dump, 679/0/58 without, on
`net8.0`/`net9.0`/`net10.0`. `dotnet format --verify-no-changes` clean. Zero real `style=`
attributes. Manual `curl` confirmed the `hx-swap-oob="true"` count element appears with the correct
filtered total on both the fragment and `/rows` responses, and the plain (non-OOB) element renders
correctly on a full page load.

**4.4's own version of the risk register's named bug, correctly avoided.** The sentinel cannot reuse
4.1–4.3's `hx-include="closest form"` pattern — it fires from `hx-trigger="revealed"`, not a form
control, and there is no hidden field carrying `sort`/`order` for `hx-include` to find even if there
were. `Rendering/InfiniteScroll.SentinelHref` instead bakes the complete current request (every
active filter field, `sort`, `order`, the next `offset`, and the clamped `limit`) explicitly into its
own `hx-get`. Getting this wrong would have silently reset a sorted, filtered view's sort order the
moment a user scrolled past the first page — the same failure class the risk register names, aimed
at a parameter the register's own wording doesn't mention. Verified with a dedicated test
(`InfiniteScrollTests.RowsRoute_PreservesActiveFilterAndSort_AcrossThePageBoundary`) and a manual
`curl` walk across two page boundaries of a `type=Http&sort=count&order=asc` request.

**Verified.** Full suite: 733/0/0 with the 9.6 GB dump, 679/0/54 without, on
`net8.0`/`net9.0`/`net10.0`. `dotnet format --verify-no-changes` clean. Zero real `style=`
attributes. `dumpstack/rows` (a `ViewKind.Detail` view despite being filterable) returns `400`, not
`404` or `500`.

**4.1–4.3, delegated and reviewed.** Per the delegation table, the lead wrote
[`FilterPreservationTests.cs`](../../src/DotNetDump.Tests/FilterPreservationTests.cs) — the risk
register's named top bug, encoded as an executable oracle against the 9.6 GB dump fixture — *before*
handing the three tasks to a Sonnet subagent in an isolated worktree, so the agent implemented
against a fixed target rather than grading its own htmx wiring. Confirmed as a real oracle both
ways: 4 of 5 checks failed pre-implementation, 5 of 5 passed after.

Scope is exactly the 8 `ViewKind.List` views (`dumpheap`, `listobj`, `gchandles`, `clrthreads`,
`threadstate`, `syncblk`, `printexception`, `clrmodules`). `dumpstack` is deliberately excluded —
it is `ViewKind.Detail` despite being filterable (`ThreadStackInfoFilter.Honored`), and the `ViewKind`
enum's own doc comment reserves the filter bar, sortable headers and infinite scroll for `List`
views only.

**A trade-off the agent surfaced and the lead kept: sort is not preserved across a filter change.**
`SERVER.md` §5.1 asks for `hx-include="closest form"` "so a sort keeps the active filter and vice
versa." Only the first direction is implemented. Baking the *current* `sort`/`order` into a filter
control's or chip's own action URL would create two competing sources for the same query parameter
on the one request that has both — the control's own URL, and whatever `hx-include="closest form"`
reads live from the DOM — and if the stale one ever won that race, a sort header click would
silently stop changing the sort. That is the exact "silently wrong data" failure class this phase
exists to prevent, aimed at the opposite parameter than the one named in the risk register. The
cost is narrow and one-directional: typing a filter or removing one resets the view to its default
sort rather than preserving a previously chosen one; a sort click always keeps the active filter.
Revisiting this (e.g. threading the current sort through as a hidden form field the filter bar's own
`hx-include` would then pick up, rather than through each control's own href) is left for the lead
to pick up if it proves worth the complexity — not done speculatively here.

**Lead review before merge.** All 15 sortable headers across the 8 views had `aria-sort` on the
nested sort `<a>` rather than the enclosing `<th>` — functionally inert for the assertions written
against it, but wrong per WAI-ARIA's sortable-table pattern, which expects a screen reader to read
sort state from the columnheader itself. Moved in review, along with the corresponding fix to
`FilterPreservationTests.cs`'s own assertion shape (it originally looked for `aria-sort` on the same
tag as the sort link, which stopped being true once the two are different elements).

**Verified.** `FilterPreservationTests`: 5/5 against the 9.6 GB dump. Full suite: 727/0/0 with the
dump, 679/0/48 without, on `net8.0`/`net9.0`/`net10.0`. `dotnet format --verify-no-changes` clean.
Zero real `style=` attributes in any `.cshtml`. Manual `curl` smoke test against a live `dndump
serve` confirmed filter values, chips, clear-all, and sort headers (`aria-sort`, direction toggling)
all render and function correctly for a combined `?type=Http&sort=count&order=asc` request.

**Exit criterion.** Filtering `listobj` on a 10.2M-object dump returns correct results with an honest
"N of 10,238,441" count and stays responsive while typing. Sorting a filtered view keeps the filter.
The back button undoes a filter change.

⚠️ **Partly verified, not fully.** "Sorting a filtered view keeps the filter" is verified —
`FilterPreservationTests` and `InfiniteScrollTests` prove it directly, against the 9.6 GB fixture,
for every view this phase covers. "The back button undoes a filter change" is verified by the
server-side proxy argument above. **The specific 10.2M-object `listobj` scale claim is not measured
and should not be read as met.** Every test and manual check in 4.1–4.6 ran against the same 9.6 GB
core this whole plan has used elsewhere — real, but nowhere near 10.2M objects on `listobj`'s own
per-instance walk (as opposed to `dumpheap`'s aggregated ~1,976 type-stat rows). "Stays responsive
while typing" against a heap that large is exactly the kind of claim [`README.md` §3](README.md)
and this plan's own measurement discipline says must be taken with a real number, not assumed from a
smaller fixture. Phase 0's `HeapObjectItemFilter` and 0.2's per-object `Generation` capture were
measured at that CLI granularity (§0's "carried forward" findings), but the *web* path's filter-typing
latency at `listobj` scale has not been. Re-take this measurement against a dump with an object count
in that range before treating Phase 4 as fully signed off — if none is on hand, this is worth a note
to revisit rather than a blocking gate on Phase 5, which does not depend on it.

**Watch for.** The most likely bug in the whole project is a sort or page action dropping the active
filter because `hx-include` was omitted — silently returning unfiltered data that looks plausible.
Test it explicitly for every view. **Realized twice during this phase**, both caught before merge:
4.1–4.3's aria-sort placement (cosmetic/accessibility, not a data bug) and 4.4's sentinel needing to
bake in `sort`/`order` explicitly rather than relying on `hx-include` (a genuine instance of this
exact risk, aimed at a parameter the original wording didn't name).

## Phase 5 — Trees ✅ complete

Depends on 4. Ordered cheapest to most expensive; each is independently shippable.

| Order | Tree | Status | Notes |
| ---: | :--- | :--- | :--- |
| 5.1 | **Namespace rollup** — [`DATA_CONTRACT.md` §4.2](DATA_CONTRACT.md) | ✅ | Pure grouping over cached heap stats, no dump access. Split on `.` only outside `<>`. Fan-out cap with a `More` node that self-replaces (`Rendering/InfiniteScroll.cs`'s sentinel pattern, applied to a tree row instead of a table row). Built by the lead as the reference implementation, the same role 3.1's data table played for Phase 3 — the shared groundwork below and this task landed in one commit. |
| 5.2 | **Thread → frames** — §4.4 | ✅ | Two levels, fully computed up front (not lazy — see below). Idle-worker grouping by exact `{ModuleName}!{MethodName}` frame-sequence signature; a bucket of one stays its own node, a thread carrying an in-flight exception is never grouped. **Locals dropped, not deferred**: the ClrMD version this project references (4.0.732401) has no locals-by-name accessor at all, only IL metadata plumbing and an unnamed per-frame stack-root enumeration — a structural gap, not a per-dump measurement question. |
| 5.3 | **gcroot retention paths** — §4.3 | ✅ | Merges `GCRootSearchInfo.Paths` into a trie on address, fully computed up front. Truncation is a first-class `GCRootOutcome` (`Rooted` / `RootedPartial` / `Unrooted` / `Inconclusive`) computed once in `GCRootTreeBuilder` rather than inferred from an empty list per-renderer — the words "unrooted" and "eligible for collection" appear in exactly one arm of `GcRootTree.cshtml`'s switch. `GET /api/gcroot/{address}` wired too (`JsonFormatter.FormatGCRootPaths`, already existed). A `?maxNodes=` override (`GCRootBudget`, shared between the HTML and JSON routes) backs the truncation banner's "re-run unlimited" link. `/views/gcroot/{address}` now redirects (302) into the tree rather than duplicating it. |
| 5.4 | **Object reference navigator** — §4.5 | ✅ | Lazy per-node `GetObjectDetails`, one cheap single-object read per expansion — the one tree that stays fast on a cold cache. Cycle-safe by construction: `TreeNode.Id` is the whole chain of visited addresses from the tree's root, hex-joined by `-`, so a revisit is detected from the id alone with no server-side session state. 64-level depth cap. An **unreadable referent** — a real ClrMD failure mode this codebase hits (e.g. a primitive-element array `GetObjectDetails` throws on), not only a hypothetical — renders as a node saying so rather than failing the whole request. Breadcrumb reconstructs history from the node id's own address chain; `dumpobj` gained the tree's only entry point ("View references →"), since no top-level nav link can offer an address nobody has yet. |

### Shared groundwork (lead, landed with 5.1)

Mirrors 3.1/3.2's role for Phase 3: the pattern every fan-out task builds against, decided once
rather than four times. `TreeNode`/`TreeNodeKind`/`TreeBadge` in Core
(`DotNetDump.Core/Models/TreeNode.cs`, [`DATA_CONTRACT.md` §4.1](DATA_CONTRACT.md)); a new
`GET /trees/{tree}/{seed?}` route family (`Routes/TreeRoutes.cs`) mirroring `DumpRoutes.cs`'s
page-vs-fragment split rather than growing `ViewCatalog`/`DumpRoutes.cs` with a third `ViewKind`
that doesn't fit a tree's shape; `TreeCatalog` for the two nav-reachable trees (`heap`, `threads`);
and shared rendering (`_TreeRow.cshtml`, the lazy-only `_TreeNodes.cshtml`/`TreeNodesModel`, and the
tree CSS in `dndump.css`) that 5.1 and 5.4 (the two genuinely lazy trees) consume directly, while 5.2
and 5.3 (both "fully computed up front" per the contract) render their own nested structure straight
over `_TreeRow.cshtml` instead. `ShellModel` was generalized to carry flat `Title`/`Command`/
`Description` plus an optional current-tree name, so a tree page uses the same shell as a view page
without needing a `ViewDescriptor`.

**Expand/collapse is native `<details>`/`<summary>`, not custom JS or ARIA `role="tree"`.** A
deliberate deviation from the Claude Design mockup's `role="tree"`/`aria-level` markup, consistent
with the near-zero-JS stance the rest of this codebase already keeps (the theme toggle is still the
only inline `<script>`, and only because a flash-before-paint requirement leaves no CSS-only
option). `<details>` gives free, real keyboard operability — Tab focuses it, Enter/Space toggles —
at the cost of the full ARIA-tree pattern's arrow-key navigation between siblings, which would need
a roving-tabindex script this project has otherwise avoided everywhere. Not recorded in
`DESIGN_RESYNC.md` because there is no synced tree component page to resync against yet — Trees.dc.html
exists in `design-sync/` but 3.5 explicitly deferred consuming it to this phase.

### gcroot's route, decided during the shared-groundwork commit and completed in 5.3

`gcroot` stays in `ViewCatalog` (`ViewCatalogCoverageTests` needs it — it is a real
`dndump gcroot <address>` command) but is a tree, not a `ViewKind.Detail` record
(DATA_CONTRACT.md §4.3), so its real implementation lives at `/trees/gcroot/{address}` and
`/views/gcroot/{address}` redirects into it. This left `ViewRoutingTests`'s `UnwiredView` constant
(`"gcroot"`, unwired by design since Phase 3.4) without a substitute — every other catalog view was
already wired — so 5.3 repurposed that test class rather than inventing a fake unwired view, and
documented the resulting gap explicitly in its own remarks: **a dumpless test run no longer exercises
the whole-page-vs-fragment routing branch at all**; that branch's only coverage is now
`WiredViewRoutingTests` and `WiredTreeRoutingTests`, both of which need `DOTNETDUMP_TEST_DUMP` and
skip without it. Recorded here rather than left to be rediscovered.

### Delegation and merges

5.1 was built by the lead as the reference implementation. 5.2 (Sonnet), 5.3 (Opus) and 5.4 (Opus)
were delegated to worktree subagents per the plan's assignment table, briefed against 5.1's pattern.
5.3 and 5.4 were interrupted mid-run by the account's monthly spend limit — 5.3 had already committed
four complete, atomic commits and was one verification step from done; 5.4 had committed its
cycle-detection core (`ObjectReferenceTreeBuilder` + synthetic-fixture tests) with the web-wiring
layer still uncommitted. The lead reviewed both in full, finished 5.4's wiring directly (plus fixed
one doc-comment placement glitch found in review), and ran the build/format/test verification neither
agent finished. All three merges hit the same **additive conflict** shape 3.3 first established as
expected and low-risk — each task added one `case` to `TreeRoutes.cs`'s switch (and, for 5.3/5.4,
overlapping test-file regions and one identically-named `TreeFormat.Address` helper both agents wrote
independently) — resolved every time by keeping both sides.

**Verified.** Full suite 830/0/0 on `net8.0`/`net9.0`/`net10.0` with the 9.6 GB fixture dump,
`dotnet format --verify-no-changes` clean. Manual `curl` smoke tests against a live `dndump serve`
confirmed the namespace rollup's root load, lazy expand and more-node paging by hand; the remaining
three trees' dump-gated automated tests (which passed) cover the same properties for each — including
`GCRootTree_WhenTheSearchIsTruncated_SaysInconclusiveAndNeverEligibleForCollection`, run against the
same real ~10.2M-object heap the Phase 4 exit criterion cites, with a deliberately tiny budget to
force truncation.

**Exit criterion.** All four render, expand (lazily for `heap`/`object`; whole, with native collapse,
for `threads`/`gcroot` — see "Shared groundwork" above for why those two don't round-trip per node).
A deliberately cyclic object graph terminates in the navigator instead of expanding forever. An
intentionally budget-truncated `gcroot` shows the warning banner and never the words "eligible for
collection".

⚠️ **"Keyboard navigable" is met by construction, not by a browser check.** `<details>`/`<summary>`
are natively focusable and toggle on Enter/Space — true of every disclosure this phase renders,
mechanically, because nothing here overrides that behavior — but no browser-automation tooling exists
in this project (the same limitation 4.6 named for the back button), so no one has actually pressed
Tab and Enter against a running `dndump serve` and watched a tree expand. **Cycle termination** and
**the truncation wording rule** are not similarly caveated: both are asserted by dedicated unit tests
against hand-built synthetic fixtures (`ObjectReferenceTreeBuilderTests`: two-object cycles,
self-reference, long cycles, diamonds and shared-leaf siblings correctly *not* flagged, the depth cap;
`GCRootTreeBuilderTests`: all four `GCRootOutcome` states), the same evidentiary standard
`RootPathFinderTests` set for the layer below them — and the gcroot truncation wording is additionally
confirmed against the real 10.2M-object dump, not only a fixture.

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

| Task | Status | Detail |
| :--- | :--- | :--- |
| 7.1 | ✅ | Docker: `serve` already ran inside the CLI image (`DotNetDump.Cli` references `DotNetDump.Web`), but was unreachable — see below. Fixed via `DumpWebHostOptions.BindAnyInterface` / `dndump serve --container`, loopback-only port publishing hardcoded into `scripts/dndump-serve-docker` and every documented example ([`SERVER.md` §6.1](SERVER.md)). |
| 7.2 | | Tests per [`SERVER.md` §7](SERVER.md), including the security tests (binding, `Host` header, no CORS) and the cyclic-graph fixture. |
| 7.3 | | README section presenting `dndump serve` alongside the CLI and the MCP server, with the loopback constraint and the secrets rationale stated, not implied. |
| 7.4 | | Update the `dndump` skill so an agent knows `serve` exists, and knows it is for a human — an agent should keep using the CLI, which is cheaper for it in every respect. |
| 7.5 | | Rewrite [`../CLI_DESIGN.md` §10](../CLI_DESIGN.md) to point here instead of describing this as unplanned future work. |

### 7.1 — the finding, and what is and isn't verified

Kestrel's loopback bind (§6's `IPAddress.Loopback`) is correct for `serve` running directly on a
host, but backfires inside Docker: `-p hostPort:containerPort` forwarding delivers packets to the
container's *routable* interface, never its loopback, so a loopback-bound Kestrel is unreachable
from the host even with the port published. `SERVER.md` §6.1's original example did not account
for this and would have hung silently. The fix makes the bind an explicit opt-in
(`BindAnyInterface`/`--container`) rather than changing the default, and moves the actual
"unreachable off this machine" guarantee to the host-side `-p 127.0.0.1:<port>:<port>` publish —
`LoopbackHostMiddleware`'s `Host`-header check needed no change, since it already validates
independent of bind address (SERVER.md §6.1 explains why).

**Verified:** a real-socket test (`ContainerBindingTests` in `WebSecurityTests.cs`) proves, against
this machine's actual non-loopback interface (not skipped — this environment has one), both that
`--container` genuinely makes the server reachable off loopback and that the `Host`-header check
still rejects a non-loopback name when the connection arrives that way. Full suite 759/0/75 on
`net8.0`/`net9.0`/`net10.0` (unchanged skip count — no dump available in this session), the touched
files are `dotnet format --verify-no-changes` clean.

**Not verified:** the other half of the fix — that Docker's own `-p 127.0.0.1:<port>:<port>` NAT
actually forwards into a container bound this way — is reasoned from documented Docker networking
semantics, not smoke-tested against a live daemon; no working Docker/podman backend was available
in the session that built this. Run `./scripts/dndump-serve-docker <a real dump>` and load
`http://127.0.0.1:5111` in a browser once before treating this as fully closed.

**Unrelated, pre-existing:** `dotnet format --verify-no-changes` fails on the whole solution because
of a stray trailing final newline in `DotNetDump.Web/Rendering/TreeModels.cs` (`insert_final_newline
= false` in `src/.editorconfig`), introduced by Phase 5.4's merge, not by this work. Left as found
rather than fixed opportunistically in an unrelated change.

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
| 3.3 Eight list views | **Sonnet**, then **Haiku** | `dumpheap` landed with 3.1, so seven remain. First two by Sonnet to prove the pattern generalises; the rest are copy-and-adapt. Fan out at most three agents, partitioned by view. |
| 3.4 Seventeen detail views | **Sonnet**, then **Haiku** | Not eleven — see the Phase 3 table. **The lead settles the address-argument routing first**; it is one decision for seven views and an agent taking it per-view would produce seven answers. `gcroot` is a tree and belongs to 5.3, not here. |
| 3.5 Resync procedure | ~~Sonnet~~ **Lead** | Reassigned: 3.3 and 3.4 need the rules *before* they fan out, so it was written during 3.1 rather than after. An agent documenting a boundary it worked inside cannot also be the boundary the next agents are briefed against. |
| 4.1–4.3 Filter bar, chips, sortable headers | **Sonnet** | Well-specified htmx wiring. **The lead writes the filter-preservation test first** (see the risk register) and the agent makes it pass; an agent asked to test its own work here will write the test that passes. |
| 4.4 Infinite scroll | **Sonnet** | Sentinel protocol is fully specified. |
| 4.5 Out-of-band updates | **Sonnet** | |
| 4.6 URL state round-trip | **Sonnet** | Round-trip property is its own oracle. |
| 5.1 Namespace rollup | ~~Sonnet~~ **Lead** | Reassigned in execution: Phase 5 is the first phase to consume the `Trees` design component, so the lead built 5.1 as the reference implementation the other three copy the shape of, the same role 3.1 played for the data table. Pure grouping over cached data; the "split on `.` only outside `<>`" rule is fiddly and got a dedicated test set regardless. |
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
| Design resync requires C# changes every time | **Realised, partially** | The design tool emits inline-styled previews rather than reusable templates (Phase 1), so a *structural* redesign does require re-extraction. Reduced rather than eliminated: token changes are CSS-only, and "no `style=` in any `.cshtml`" is mechanically checkable. |
| A view exists in the navigation but is unreachable, or exists in the CLI and not the navigation | **Realised** (`verifyobj`) | `ViewCatalogCoverageTests` makes the CLI the oracle in both directions; `ViewRoutingTests` asserts no catalog entry answers 404. Both were written after the fact, because the failure is silent — a missing view simply never appears. |
| Docker example published without the `127.0.0.1:` prefix | Medium | One copy-pasted command undoes the entire security posture. Wrapper script emits the safe form; README shows only that form. |
| Long cold walk blocks all other requests | Medium | Inherent to the thread-safety constraint. Mitigated by cache-hit fast path (6.4) and startup warm (6.1), not eliminated. |
| Scope creep into retained size / dominators | Medium | Explicitly out of scope in [`README.md` §4](README.md). The trigger for revisiting is a feature decision, not a performance one ([`../CLI_DESIGN.md` §11.3](../CLI_DESIGN.md)). |
