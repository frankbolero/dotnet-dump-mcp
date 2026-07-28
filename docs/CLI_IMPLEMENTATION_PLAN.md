# CLI Implementation Plan

Execution plan for [CLI_DESIGN.md](CLI_DESIGN.md).

**Status: all phases complete** (commits `ca7274a`, `f05b5c2`, `32e0d79`, `c41b490`, `aed4895`,
`cd40502`, `95d53f6`, `222d44d`, `95dc447`, plus the Phase 10 integration commit). Both §0 gates
resolved. Phase 10's review found and fixed a real cross-phase gap — the CLI never actually used
the disk cache Phases 1 and 4 built — see that phase's outcome below.

The work is broken into phases sized for delegation to subagents. Model assignments are chosen to
keep token cost down: mechanical work with an established pattern goes to Haiku 4.5, work requiring
design judgement goes to Sonnet 5, and architectural decisions plus review gates stay with Opus.

## 0. Prerequisites

### 0.1 Decision gate: startup measurement — **RESOLVED, native delivery**

Measured 2026-07-27 against `core_20251212_112511`, a 6.3 GB Mach-O arm64 core, using a Release
build of `DotNetDumpExplorer` invoked directly (not via `dotnet run`, which adds ~450 ms of MSBuild
evaluation and is not representative of a shipped tool).

| Path | Wall time |
| :--- | :--- |
| `LoadDump` + `CreateRuntime` + exit (cold) | 0.77 s |
| Same + full `dumpheap -stat` heap walk (warm) | 0.57 s |

Per-command init is **well under the 3 s threshold**, so:

* **Native per-command delivery (§8.1) is the primary path.** No persistent container required.
* **Phase 7 is descoped** to keeping Docker working for architecture mismatch, plus the DAC cache
  volume. The `docker exec` wrapper becomes optional, not the default.

Caveat on the heap-walk figure — see §0.2. This dump's managed heap is 26.7 MB and holds only 22,733
objects, so its walk is effectively free and that number does **not** generalize.

### 0.2 Decision gate: is the cache justified? — **RESOLVED, build tier 1**

Three dumps measured 2026-07-27, all Mach-O arm64 cores:

| | `…112511` | `…205808` | `…211646` |
| :--- | ---: | ---: | ---: |
| File size | 6.3 GB | 8.6 GB | 9.0 GB |
| Managed heap | 26.7 MB | 2,296.3 MB | 2,858.0 MB |
| **Objects** | **22,733** | **86,216** | **10,187,201** |
| Types | 1,449 | 1,404 | 1,511 |
| `eeheap` committed | 26.9 MB / 25 seg | 2,296.7 MB / 33 seg | 2,876.6 MB / 33 seg |
| Init only (cold) | 0.77 s | 0.62 s | 0.55 s |
| Init + walk (cold) | 0.57 s | 0.41 s | **2.62 s** |
| Init + walk (warm, ×2) | — | — | **1.81 s / 1.77 s** |
| DAC | mismatched, forced | matched | matched |

Objects total against `eeheap` committed on all three, so every walk is correct. The last two loaded
without the `ignoreMismatch` fallback.

**Heap size is the wrong metric; object count is the right one.** Dump 2 has an 86× larger heap than
dump 1 and still walks in under half a second — its 2.24 GB sits in only 21,403 `System.Byte[]`
instances averaging ~112 KB. `EnumerateObjects` walks segments linearly, reading headers and skipping
object bodies, so cost tracks object count, not bytes. Dump 3 has a similar heap size to dump 2 but
**118× the objects**, and that is where time appears.

**Derived rate: ~8M objects/sec warm** (10.19M objects, ~1.25 s of walk after subtracting ~0.55 s
init). Use this to predict any dump's walk cost from its object count:

| Objects | Walk (warm) |
| ---: | ---: |
| 10 M | ~1.3 s |
| 50 M | ~6 s |
| 100 M | ~13 s |

**The decisive finding is that the walk is CPU-bound, not I/O-bound.** Warm runs held at 103% CPU
with essentially no I/O wait, and repeated runs were stable at 1.81 s / 1.77 s. The OS page cache
flattens the cold→warm transition (2.62 s → 1.81 s) and then stops helping. There is a hard floor
that no amount of file caching removes, because the cost is ClrMD CPU work. **Only a result cache
eliminates it.**

Compounding this: a triage session hits four of the five walk sites (`dumpheap`, `listobj`,
`syncblk`, heap-exception scanning), so the per-session cost is roughly 4× the per-walk figure, paid
again by every agent and every re-run.

**Decision: build tier 1.** Phases 1, 4 and 8 are ungated. At 10 M objects the saving is ~1.8 s per
repeat — modest but real; at the 50–100 M objects a production leak dump can reach, it is 6–13 s per
walk and decisive. Tier 1 is also the cheap half of §6: small JSON entries, no bespoke binary format.

**Tier 2 (object index) stays deferred** per §12.4. At ~24 bytes/object it would be 240 MB for dump 3
alone and GB-scale for the dumps that motivate it, to save a walk now measured at ~1.3 s. Revisit
only if `listobj`/`gcroot` prove painful in real use.

Caveat: all three dumps are synthetic or semi-synthetic (dump 3 is `HeapNode*` × 30,000 each plus
3.0 M strings and 3.0 M byte arrays). They establish the *rate*, which transfers; they do not
establish what object counts production dumps actually reach.

### 0.3 Standing instructions for every subagent

Every task brief must include:

* **Verify your baseline before touching anything.** Agent worktrees are branched from `main`, which
  is *far* behind the working branch — `docs/overall-checkup` carries ~1,000 lines of Core changes
  that `main` does not have. Run `git log --oneline -1` and, if you are not on a descendant of
  `docs/overall-checkup`, `git merge --ff-only docs/overall-checkup` before starting. **This is not
  hypothetical:** Phase 2 was silently built against `main`, mirrored an obsolete `MarkdownFormatter`
  (16 KB vs the real 24 KB) and had to be redone in full. Phase 1 caught the same problem and
  fast-forwarded itself.
* **Scope `dotnet format` with `--include`.** `.editorconfig` specifies `end_of_line = crlf` while
  the committed blobs are LF, so an unscoped full-solution `dotnet format` rewrites line endings in
  every file in the repo and buries your actual change in hundreds of files of churn. Format only the
  files you touched, and revert incidental line-ending changes elsewhere.
* Read `AGENTS.md` and `docs/CLI_DESIGN.md` first. The spec is the contract; do not redesign it.
* Respect the layering: analysis logic in Core, formatting in `Core/Formatting/`, argument handling
  in the CLI project. The CLI project must not reference `DotNetDump.Server` or
  `ModelContextProtocol`.
* Multi-target `net8.0;net9.0;net10.0`. No APIs unavailable on net8.0.
* Nullable reference types are enabled; keep the existing tab-indented, brace-on-same-line style.
* Before reporting: `dotnet build`, `dotnet test`, and `dotnet format`.
* Report what you did **not** do, and any place the spec was ambiguous. Do not invent scope.

Writing the spec first is what makes this delegation cheap — each subagent starts cold, and
`CLI_DESIGN.md` is the shared context that saves re-deriving the design in every brief.

## 1. Phase table

Both gates in §0 are resolved: native per-command delivery, and tier 1 caching is in scope.

| # | Deliverable | Model | Depends on | Parallel with |
| :-- | :--- | :--- | :--- | :--- |
| 1 | Cache abstraction + providers, tier 1 only, incl. `DumpIdentity` on `IDumpContext` (§6.4) | Sonnet 5 | — | 2 |
| 2 | JSON + TSV formatters (§3.3, §10.3) | Haiku 4.5 | — | 1 |
| ~~3~~ | ~~`DumpIdentity` on `IDumpContext`~~ — folded into Phase 1 | — | — | — |
| 3b | Fix `gcroot` truncation reporting; make the node budget settable | Sonnet 5 | — | 1, 2, 4 |
| 4 | Wire cache into analyzers; delete `_cachedStats` (§6.3) | Sonnet 5 | 1 | 5 |
| 5 | CLI scaffold: project, global options, dump resolution, exit codes (§2, §3) | Sonnet 5 | 2 | 4 |
| 6 | The 25 command wirings (§4) | Haiku 4.5 | 5 | 7 |
| 7 | Docker: DAC cache volume; keep arch-mismatch path working (§8.2) | Sonnet 5 | — | 6 |
| 8 | MCP server registers a cache provider (§6.6) | Haiku 4.5 | 1, 4 | 6, 7 |
| 9 | Skill authoring (§8.3) | Sonnet 5 | 6 | — |
| 10 | Integration review, README, `PLAN.md` update | Opus (me) | all | — |

Phases 1 and 2 have no dependencies and launch together. They touch disjoint directories
(`Core/Caching/` and `Core/Formatting/`) but must run in **separate git worktrees** regardless —
concurrent `dotnet build`/`dotnet test` in one working tree collides over `obj/` and `bin/`.

Phase 3 was folded into Phase 1: it added `DumpIdentity` to `IDumpContext`, but Phase 1 defines that
type, so running them concurrently would have produced either a duplicate definition or a build
failure. It is a few lines and belongs with its owner.

Phase 6 is the largest by volume but the most mechanical, which is why it is deliberately isolated
behind Phase 5 — once one command is wired, the remaining 24 are pattern-matching.

Phase 7 is reduced from the original plan: §0.1 showed sub-second init, so the persistent-container
wrapper is optional rather than the primary path. What remains is the DAC cache volume and keeping
the architecture-mismatch path working.

## 2. Phase briefs

### Phase 1 — Cache abstraction and providers · Sonnet 5

Create `src/DotNetDump.Core/Caching/` implementing §6.4 exactly: `DumpIdentity`, `CacheKey`,
`IAnalysisCache`, `ICacheSerializer`, and the four providers (`Null`, `Memory`, `FileSystem`,
`Tiered`) plus `JsonCacheSerializer`.

Requirements beyond the interface sketch:

* `FileSystemAnalysisCache` writes temp-file-then-rename; readers never see partial entries.
* Advisory locking on the key so concurrent processes do not duplicate a multi-minute walk.
* Cache root from `DNDUMP_CACHE`, else an XDG-style user cache directory. Directory per
  `DumpIdentity`.
* `ClearDump` and LRU pruning support, since tier 2 entries will be GB-scale later.

Tests: key composition and stability, hit/miss, tier promotion, atomicity under concurrent writers,
`NullAnalysisCache` always computing. No dump file required — these are pure unit tests.

Out of scope: tier 2 binary index (§12.4 is unresolved), and any analyzer changes.

### Phase 2 — JSON and TSV formatters · Haiku 4.5

Add `JsonFormatter` and `TsvFormatter` to `src/DotNetDump.Core/Formatting/`, mirroring the public
surface of the existing `MarkdownFormatter` method-for-method. The models and the method list already
exist; serialize the same inputs in a different shape. `MarkdownFormatter` is not to be modified.
TSV emits a header row then tab-separated values, no padding or alignment.

**Build the JSON output as an API contract, not a `jq` convenience** (§10.3). A web front end is a
recorded future direction and would consume this as its API, so:

* Stable, explicit field names — do not rely on incidental property-name defaults that a rename
  would silently change.
* A consistent envelope across every command, rather than a bare array in some and an object in
  others.
* Pagination metadata travelling *with* the rows — total available, offset, limit, and whether more
  remains. A short page must be distinguishable from the end of the data.

This is the same amount of work as the naive version and much cheaper than retrofitting it later.
TSV stays deliberately dumb: rows only, for `grep`/`awk`.

**Outcome — first attempt discarded, must be redone.** Two independent problems:

1. **Wrong baseline (fatal).** The worktree was branched from `main` and never fast-forwarded, so the
   agent mirrored a `MarkdownFormatter` of 16 KB against the working branch's 24 KB, on model shapes
   ~1,000 lines out of date. The output references none of the current fields — `GCHandleInfo` alone
   has gained `IsStrong`, `ReferenceCount`, `DependentTarget`, `AppDomainName` and a whole
   `GCHandleStatItem` companion class, none of which appear. Not patchable: the mirror target itself
   was wrong. See the baseline instruction in §0.3.
2. **Contradictory brief (mine).** The brief asked the formatters to mirror `MarkdownFormatter`'s
   signatures *and* to emit total/offset/limit/hasMore. Those conflict — analyzers slice with
   `Skip().Take()` and return a bare `IEnumerable<T>`, so the pre-pagination total is discarded before
   any formatter is reached. The agent shipped `{ "itemCount": n }`, the only thing its inputs
   allowed. Phase 4 now owns this via `PagedResult<T>`.

The redo should keep the envelope shape (`{ data, pagination }`, camelCase, nulls omitted, 16-hex
addresses) — that part was right — and emit `pagination` with the fields Phase 4 will populate.

### Phase 3 — Dump identity · Haiku 4.5

Add `DumpIdentity Identity { get; }` to `IDumpContext`, computed once in `DumpContext.Load` from
dump `size + mtime + inode` plus the resolved DAC path/build id. Must not read the whole file.
Throws or returns a sentinel when no dump is loaded, consistent with the existing `IsLoaded`
pattern.

### Phase 3b — `gcroot` truncation reporting and settable budget · Sonnet 5 — **done, `32e0d79`**

Full write-up, defect chain and proposed fix: **[GCROOT_TRUNCATION.md](GCROOT_TRUNCATION.md)**.

A correctness bug, not CLI work — `gcroot` reports a truncated search as "the object is unrooted and
eligible for collection", which on a heap above ~2M objects can be flatly false. Independent of every
other phase; can run at any time.

Two parts:

1. **Never report a truncated search as complete.** `FindOnePath` returns `null` both when it
   exhausts its budget (`RootPathFinder.cs:111`) and when it legitimately finds nothing
   (`RootPathFinder.cs:141`). Distinguish them, thread the distinction through `FindPaths` →
   `GetGCRoots` → `GCRootPathInfo` → all three formatters, and report nodes visited plus completion
   status on every result. Note `maxPaths` gives each pass a fresh budget, so *partial* results are
   possible too and must be flagged.
2. **Make the budget settable**, `--max-nodes <n>` with `0` meaning unlimited, plus a `maxNodes` MCP
   tool parameter and a `DNDUMP_GCROOT_MAX_NODES` default. Settle and document whether the budget is
   per-pass or total — the current per-pass behaviour is undocumented and surprising.

Unlimited must be available but is not free: `parent` retains every visited node, so peak memory is
roughly 40 bytes × nodes visited (~4 GB at 100M). Say so in the help text, and have the truncation
message point at `--max-nodes 0` as the way to get a conclusive answer.

`RootPathFinder` is expressed over an abstract successor function so it can be tested against a
hand-built graph with no dump — see `src/DotNetDump.Tests/RootPathFinderTests.cs`. All the tests
listed in the write-up are cheap unit tests.

### Phase 4 — Wire the cache into analyzers, and split compute from pagination · Sonnet 5 — **done, `32e0d79`**

**Scope expanded after Phase 2.** Two requirements turn out to be the same refactor:

1. §6.2 requires caching the *unpaginated* model so one entry serves every `limit`/`offset`/`sort`
   variant. Analyzers currently do walk → sort → page inside a single method
   (`HeapAnalyzer.cs:54,74,141,316`, `ThreadAnalyzer.cs:43,140,204,258`, `ModuleAnalyzer.cs:47`), so
   the cache cannot sit around the full result until computation and pagination are separated.
2. §10.3 requires JSON pagination metadata — total available, offset, limit, whether more remains.
   Phase 2 could not deliver this: analyzers return a bare `IEnumerable<T>` after slicing, so the
   pre-pagination total is discarded before any formatter sees it. Phase 2 shipped a placeholder
   `{ "itemCount": n }`, which is derivable from the array and therefore carries no information.

Introduce `PagedResult<T>` in `Core/Models/` — items plus `TotalAvailable`, `Offset`, `Limit` — and
have the analyzers return it. The cache then wraps the unpaginated computation; pagination is applied
after the lookup; and `JsonFormatter` gets the fields it needs to fill the envelope properly.

`MarkdownFormatter` and the MCP server call sites need updating to match. `MarkdownFormatter`'s
rendering must not change — only how it receives the data.

Add `IAnalysisCache` constructor injection to `HeapAnalyzer` and `ThreadAnalyzer`, defaulting to
`NullAnalysisCache` so existing callers and tests are unaffected.

Wrap only the five walk-scale sites named in §6.3: `HeapAnalyzer.cs:31,59,320,408` and
`ThreadAnalyzer.cs:291`. Do not add caching to cheap operations.

Delete `_cachedStats` and replace it with a `GetOrCompute` call — it is currently either dead (per-
call instantiation) or stale (never invalidated by `load_dump`), and the cache key in §6.1 fixes
both.

Critical: the arguments hash must exclude `limit`, `offset`, `sort`, `order` and `format`, so one
entry serves every pagination and rendering variant (§6.2). Add a test proving that two calls
differing only in `--limit` produce one cache entry and one computation.

**Outcome — worktree isolation failed silently for both 3b and 4.** Both subagents ended up editing
directly in the primary checkout instead of their own isolated worktree, uncoordinated, and each
independently touched five shared files (`HeapAnalyzer.cs`, `JsonFormatter.cs`,
`DumpAnalyzerTools.cs`, `IntegrationTests.cs`, `JsonFormatterTests.cs`). This was caught only because
both agents' baseline check reported unexpected foreign changes appearing mid-task and stopped to ask
rather than pushing through — the standing instruction earned its keep here in a new way. Reviewing
the merged diff found the two changes genuinely complementary rather than conflicting (each agent's
edits, made against a live shared file, had implicitly accounted for the other's presence), so no
rework was needed; both landed in one commit, `32e0d79`. **Lesson for Phase 5, 6 and 7, which also run
as parallel subagents: explicitly verify each agent is in its own worktree directory (not the primary
checkout) before relying on isolation to prevent collisions** — the request for isolation does not
guarantee it took effect.

### Phase 5 — CLI scaffold · Sonnet 5 — **done, `c41b490` + `aed4895`**

Create `src/DotNetDump.Cli/` per §2, add it to the solution, and implement §3 in full: global
options, dump resolution precedence (`--dump` → `DNDUMP_PATH` → `.dndump/session.json` searched
upward), the three output formats, stdout/stderr separation, and the four exit codes.

Wire exactly one analysis command end to end — `dumpheap` — as the reference pattern Phase 6 will
replicate. Also implement `use`, `info` and `commands` (§4.1).

Do **not** port `ExecuteSafe` from `DumpAnalyzerTools.cs:313`. Analyzer exceptions propagate to a
single top-level handler mapping exception type to exit code, with the message on stderr.

On argument parsing: verify the currently published `System.CommandLine` version and API shape
before writing against it — the package went through a long beta with breaking changes between
previews. If it is still unstable, a hand-rolled parser is acceptable and preferable to churn;
report which you chose and why.

**Outcome.** Used **System.CommandLine 2.0.10** (verified current GA, not the unstable
`3.0.0-preview` series) — chosen by loading the assembly and probing its actual API rather than
trusting recollection. `info` required new Core plumbing with no prior MCP equivalent
(`SessionAnalyzer`, `DumpInfo`) plus a `FormatInfo` method appended to each formatter, without
touching any existing formatter method. Smoke-tested against a real sample dump.

This worktree branched before Phase 4 landed, so it was built against the pre-Phase-4
`HeapAnalyzer` API (bare `IEnumerable<T>`). Phase 4 merged first and changed those methods to
return `PagedResult<T>`; reconciling required one small addition, an `OutputFormatting.Render`
overload that takes a `PagedResult<T>` and hands `JsonFormatter` the full result (for real
pagination metadata) while Markdown/TSV still get just the page — committed separately as
`aed4895` and re-verified against the sample dump (`total`/`hasMore` render correctly). **Phase 6
should use this overload for any command backed by one of the five cached analyzer methods, and
the plain `Render<T>` overload for everything else.**

### Phase 6 — Command wirings · Haiku 4.5 — **done, `95d53f6`**

Implement the remaining 24 commands from §4.2–§4.5, following the `dumpheap` pattern established in
Phase 5. Each is: parse arguments, call the analyzer, hand the model to the selected formatter, set
the exit code. No logic in the CLI project.

The spec tables give exact option names, defaults and valid sort fields per command. Match them
literally, including the negatable flags (`--[no-]thin-locks`, `--[no-]heap-exceptions`) and
`--all-threads` as the inverse of `onlyWithExceptions`.

**Outcome.** 23 command files, one per remaining command, plus `RootCommandFactory` registration —
1,143 lines, additive only, no shared files touched (no collision with Phase 7 or 8, which ran in
parallel). Negatable flags implemented as a single presence-based `--no-x` option rather than a true
`--[no-]x` pair (System.CommandLine 2.0.10 has no built-in negation syntax), which is sufficient
since every negatable flag here defaults on. `gcroot` and `pe`'s single-address mode both correctly
use the plain `Render<T>` overload / a `PagedResult` wrap rather than the wrong dispatch shape.
Verified against the real sample dump post-merge: `eeheap`, `gchandles`, `syncblk --no-thin-locks`,
`pe`, `listobj`, `dumpmt`, `clrmodules`, and `gcroot` against a real address (both retention paths
render correctly) all work end to end in Markdown and JSON.

### Phase 7 — Docker · Sonnet 5 — **done, `222d44d`**

Per §8.2: mount a DAC/symbol cache volume so `dotnet-symbol --dac-only` runs once rather than per
command, and add a wrapper script for the persistent-container pattern (`docker run -d` once, then
`docker exec` per command) that hides the prefix so agent-facing commands match the native ones.
`entrypoint.sh` must keep working unchanged for the existing MCP server path.

**Outcome.** Real bug found: the installed `dotnet-symbol` has no `--dac-only` flag any more (verified
against its actual `--help`, not memory) — `entrypoint.sh`'s DAC fetch was silently failing into its
warning branch on every run, predating this phase. Replaced with `--debugging --cache-directory
/symcache`. The Dockerfile now also publishes `DotNetDump.Cli` into the image (into its own
`/app/cli` directory, so the two publishes' overlapping Core/ClrMD assemblies can't clobber each
other) — without this, the design doc's `docker exec ... dndump ...` example would have had nothing
to exec. `scripts/dndump-docker` wraps the persistent-container pattern idempotently, and works
around a real footgun: `ENTRYPOINT`'s exec form swallows a bare `sleep infinity` as arguments to
`entrypoint.sh` rather than running it, so the script uses `--entrypoint sleep` instead. Everything
was verified against a real `docker build`/`run`/`exec`, including a cold vs. warm DAC-cache timing
comparison (~7.5s vs ~0.19s).

Rebuilding from the fully-merged state (all 25 commands, not just the 1 that existed when this
phase's own worktree branched) surfaced a second, unrelated latent bug: `.dockerignore` had no rule
for the `core_*` sample dump files used for local smoke-testing (tens of GB across three dumps,
`.gitignore`-excluded but not `.dockerignore`-excluded), so a build run from a checkout that happens
to have them present sends them into the build context via `COPY . .` — confirmed as the actual
cause after an initial false "no regression" read of a stale/wrong image (a `docker build | tail`
pipeline had masked the real, failing exit code). Fixed in `362f42c`; reverified afterward with a
clean `--no-cache` build: all 25 commands resolve on `PATH`, `dotnet-symbol --debugging
--cache-directory` both verified as real flags, and the MCP server path starts and shuts down
cleanly, unaffected.

### Phase 8 — Server cache registration · Haiku 4.5 — **done, `cd40502`**

Register a `TieredAnalysisCache` (memory + filesystem) in `src/DotNetDump.Server/Program.cs` so the
MCP server shares the cache written by the CLI. Roughly a one-line DI change plus verification that
`load_dump` on a second dump serves correct results — the regression the old `_cachedStats` would
have caused.

**Outcome.** Exactly as scoped: 3 lines in `Program.cs`, plus a regression test proving `CacheKey`
values differ across `DumpIdentity`s (tested at the cache level, since no two-dump fixture exists to
drive it through the full MCP tool surface). This agent's harness-provided worktree was itself
mis-based (on old `main`, not `docs/overall-checkup`) — it caught this via the same self-verification
instruction, created its own correctly-based worktree, and proceeded without needing intervention.

### Phase 9 — Skill · Sonnet 5 — **done, `95dc447`**

Author the skill per §8.3: dump-selection convention, condensed command table, the three triage
workflows (OOM/leak, deadlock/hang, unhandled exception), and the piping idioms from §5 so the agent
filters in the shell by default. The workflows are the part that needs judgement — they are what a
tool manifest structurally cannot express.

**Outcome.** Written as a Claude Code skill at `.claude/skills/dndump/SKILL.md`
(`user-invocable: false`, auto-triggered by dump/crash/OOM/SOS-command mentions, matching the
pattern of this environment's other knowledge-type skills rather than an argument-taking command
like Phase-9-adjacent action skills). Command names, options and defaults were verified against the
actual `DotNetDump.Cli` source (`RootCommandFactory.cs`, `GlobalOptions.cs`, and the individual
`Commands/*.cs` files) rather than transcribed from §4, since Phase 6's outcome notes recorded a
deliberate deviation from the spec worth surfacing here too: `--top` is `dumpheap`-only, not a
general alias across "summary commands" as §3.2 could be read to imply, and negatable flags are a
single presence-based `--no-x` (e.g. `--no-thin-locks`, `--no-heap-exceptions`), not true
`--[no-]x` pairs. The skill states both of these accurately rather than the spec's more general
phrasing. Not covered: a top-level `dndump` project skill beyond this one file — no second skill or
command-file split was warranted since the brief describes a single cohesive reference document.

### Phase 10 — Integration · Opus — **done**

Cross-phase review, README restructuring to present the CLI as the default with the MCP server as
the option, and `docs/PLAN.md` updated to reflect the new shape.

**Outcome.** `dotnet build` and `dotnet test` clean across all three target frameworks (288 passed,
26 skipped — the skipped tests require a real sample dump and are unaffected by anything below).

**Real bug found and fixed: the CLI never used the cache.** Every one of the 25 command files
constructs its `HeapAnalyzer`/`ThreadAnalyzer` as `new HeapAnalyzer(context)` with no second
argument, which defaults to `NullAnalysisCache` (`HeapAnalyzer.cs:40`,
`ThreadAnalyzer.cs:21`). Phase 8's brief only covered registering a cache in the *server*; no phase
brief told the CLI project to do the same, and it fell through the gap between them. The practical
effect: none of the CLI's five cache-eligible walks (`heap-statistics`, `heap-objects`,
`sync-blocks`, and the two `heap-exceptions` call sites shared with `printexception`) ever hit disk,
so the CLI — the *primary* delivery path per §0.1 — re-walked the heap on every single invocation,
directly contradicting §6's stated thesis ("the CLI is faster than today's MCP server on any
repeated query, not slower"). Fixed by adding `AnalysisCacheProvider` (a single static
`FileSystemAnalysisCache` — no memory tier, since a per-invocation process never makes a second
`GetOrCompute` call for one to serve) and passing it into all 16 affected command files (the other 9
construct `ModuleAnalyzer`/`MetadataAnalyzer`, which don't accept a cache). Verified end to end
against a real sample dump: a cold `dumpheap --top 3` writes
`<cache>/<dump-identity>/heap-statistics--v1.json`, and a subsequent `dumpheap --sort Count --top 3`
(different sort and limit) reuses that entry rather than recomputing, exactly as §6.2 specifies.

**Also noted, not fixed:** `scripts/dndump-docker` mounts a volume for the DAC/symbol cache but not
for `DNDUMP_CACHE`, so the analysis-result cache still works during one persistent-container
session but does not survive the container being recreated. Lower severity than the bug above (the
session-scoped case is the documented common path) and left as a follow-up rather than expanding
Phase 7's already-closed scope. `--no-cache`/`--refresh` (§6.5 escape hatches) also remain
unimplemented on both front ends — real gaps, but net-new option surface rather than a wiring fix,
so left for whoever picks up tier 2 or the escape hatches specifically.

README: added a "Result caching" section under the CLI quick start explaining the disk cache,
`DNDUMP_CACHE`, and that it's shared with the MCP server — the design doc covered this but the
README, freshly written in commit `112b4da`, hadn't mentioned it at all. The rest of the README's
CLI-first restructuring was already in place from that commit and needed no further changes.

`docs/PLAN.md` (the pre-CLI MCP-server build log) gets a short pointer at the top to
`CLI_DESIGN.md`/`CLI_IMPLEMENTATION_PLAN.md` rather than a rewrite — it's a historical record of a
finished build, and restating it in CLI terms would falsify that history for no benefit.

## 3. Why these model assignments

**Haiku 4.5** takes Phases 2, 3, 6 and 8. Each has an existing pattern to copy and a spec table to
match literally — formatter methods mirroring `MarkdownFormatter`, a single computed property, 24
commands following a reference implementation, one DI registration. Phase 6 is the largest single
block of code in the plan and also the least design-sensitive, which is exactly the trade that makes
delegation to a cheaper model worthwhile.

**Sonnet 5** takes Phases 1, 4, 5, 7 and 9. These involve decisions the spec deliberately leaves
open: locking and atomicity strategy, where cache boundaries sit relative to pagination, the
argument-parsing library call, container lifecycle, and what a good triage workflow looks like.

**Opus** keeps Phase 10 and the §0.1 gate. Integration review is where cross-phase mistakes surface,
and the measurement decides a delivery model that is expensive to change later.

## 4. Risks

* **Phase 4 is the correctness-critical one.** Getting the argument hash wrong — including
  `limit`/`offset` in the key, or omitting the DAC identity — produces a cache that is either
  useless or silently wrong. The named test is not optional.
* **Phase 6 volume.** 24 near-identical commands is where a cheaper model is most likely to drift
  from the spec tables. Review the option names and defaults against §4 specifically, rather than
  trusting a green build.
* **`System.CommandLine` churn** could stall Phase 5. The hand-rolled fallback is explicitly
  sanctioned so this does not become a blocker.
* **The measured rate comes from synthetic dumps.** ~8M objects/sec transfers, but the object counts
  production dumps actually reach do not. If real dumps land nearer 100k objects than 10M, tier 1
  caching will have been built for a cost that never materialises. It is cheap enough to accept that
  risk; tier 2 is not, which is why it stays deferred.
* **Phase 1 should not over-build.** §6.4 specifies four providers, locking, atomic renames and LRU
  pruning. At tier-1 sizes (a few MB per entry) the pruning and tiering are speculative. Build the
  interface and all four providers as specified, but keep the eviction policy simple until there is
  evidence it matters.
