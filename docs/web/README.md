# `dndump serve` — web interface

Status: **proposed**, not implemented.

This directory specifies a local web interface for interactive dump analysis: a third consumer of
`DotNetDump.Core` alongside the `dndump` CLI and the MCP server. It replaces the deliberately
under-specified sketch in [`../CLI_DESIGN.md` §10](../CLI_DESIGN.md), which recorded the idea and
the constraints but took no position on any of the decisions below.

Those decisions are now taken. §10's constraints still apply and are inherited, not restated.

## 1. Settled decisions

| Decision | Choice | Why |
| :--- | :--- | :--- |
| Rendering model | ASP.NET Core minimal APIs + **htmx**, server-rendered HTML fragments | Claude Design emits plain HTML/CSS, so synced files are used as templates rather than ported into components — and re-ported on every resync. Paging/sort/filter logic stays in the C# that already implements it correctly. |
| Host | New **`DotNetDump.Web`** project, launched by `dndump serve` | Keeps the ASP.NET Core dependency graph out of the startup cost that every other `dndump` command pays ([`../CLI_DESIGN.md` §1.1](../CLI_DESIGN.md)). |
| Speed strategy | Perceived speed on the existing **tier-1 cache** | Background cache warm at startup, honest pending states for cold walks, ClrMD kept warm in the long-lived process. No new storage substrate; tier 2 and the CSR graph (§11) stay unbuilt. |
| Filtering | **Structured filter layer in Core**, applied server-side | One filter grammar shared by the web UI and a new CLI `--filter`. Applied after the cached walk and before pagination, so filters cost nothing. |
| Trees | All four: namespace rollup, thread→frames, gcroot paths, object reference navigator | See [`DATA_CONTRACT.md` §4](DATA_CONTRACT.md). |
| Sessions | **One dump per server process** | Matches today's singleton `IDumpContext` exactly. No lifecycle redesign. |
| Access | **Loopback only, no auth** | Heap strings contain connection strings, tokens and PII. Bind `127.0.0.1`, never `0.0.0.0`, plus `Host`-header validation against DNS rebinding. |
| Concurrency | Single serialized analysis worker | `ClrRuntime`/`ClrHeap` are **not thread-safe**. This is a correctness requirement, not a tuning choice — see [`SERVER.md` §3](SERVER.md). |

## 2. The documents

| Document | Contents | Primary reader |
| :--- | :--- | :--- |
| [`DESIGN_BRIEF.md`](DESIGN_BRIEF.md) | The instruction prompt for Claude Design: what the product is, the component inventory, real sample data to design against, and the constraints htmx imposes on the markup. | Claude Design |
| [`DATA_CONTRACT.md`](DATA_CONTRACT.md) | Filter grammar, view models, tree node shapes, lazy-expansion protocol, pagination and truncation semantics. | Whoever implements Core changes |
| [`SERVER.md`](SERVER.md) | `DotNetDump.Web` architecture: routes, fragment conventions, the analysis queue, cache warm-up, security posture. | Whoever implements the host |
| [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) | Phased build with per-phase exit criteria and the measurements that gate later phases. | Whoever builds it |

## 3. Sequencing

You asked what order to do this in. The short answer is **not** design-first, and not
design → data → server either.

```
Track A ── Phase 0: Core contract (filters, shared dump resolution, view models) ──┐
                                                                                   │
Track B ── Phase 1: Design brief → Claude Design → component library ──────────────┼──→ Phase 3 → 4 → 5 → 6 → 7
                                                                                   │
Track C ── Phase 2: Web host skeleton (`dndump serve`, analysis queue, one view) ──┘
```

| Phase | Work | Depends on |
| ---: | :--- | :--- |
| 0 | Structured filter layer in Core; move `DumpResolver`/`SessionFile` from CLI to Core; `PagedResult<T>` on the remaining analyzer methods. Ships CLI-visible value (`--filter`) with zero web code. | — |
| 1 | Write the design brief, build the component library in Claude Design. | — |
| 2 | `DotNetDump.Web` skeleton: `dndump serve`, loopback binding, the serialized analysis queue, one real view in placeholder markup. | — |
| 3 | `/design-sync` the library into the repo; replace placeholder markup with real components. | 1, 2 |
| 4 | Interaction: filter bar, sortable headers, infinite scroll, URL-encoded view state. | 0, 3 |
| 5 | The four trees, cheapest first. | 4 |
| 6 | Perceived speed: startup cache warm, progress reporting, pending-state UI. | 4 |
| 7 | Packaging: Docker, tests, README, skill update. | 5, 6 |

### 3.1 Why the three tracks run in parallel

Phases 0, 1 and 2 have no dependency on each other and each de-risks something different:

* **Phase 0 first, not last.** The obvious ordering puts data structures after the design, but a
  design drawn against imagined data produces components that cannot be filled. The Core models
  already exist and are documented, so the design brief can be written today — what does *not* yet
  exist is filtering, which is the single feature you asked to have "in all commands". Building it
  first means it is exercised through the CLI, with real dumps and real timings, before any web
  code depends on it. If server-side filtering turns out to be expensive, that is much better
  discovered in Phase 0 than in Phase 4.
* **Phase 1 is calendar time, not engineering time.** Design iteration is the long pole and has no
  code dependency. Starting it on day one is free.
* **Phase 2 proves the risky plumbing early** — dump resolution in a long-lived process, the
  serialized analysis worker, loopback binding — using placeholder markup that Phase 3 throws away.

### 3.2 The one thing that must not be deferred

The serialized analysis queue belongs in **Phase 2**, not Phase 6. `ClrRuntime` and `ClrHeap` are
not thread-safe, and a web server is concurrent by default. Every command the CLI runs today is one
command in one process on one thread; that assumption disappears the moment two browser tabs exist.
Retrofitting serialization onto handlers that already call analyzers directly means rewriting every
handler. See [`SERVER.md` §3](SERVER.md).

## 4. Out of scope

Recorded so the boundary is explicit rather than discovered:

* **Tier-2 object index and the CSR graph** ([`../CLI_DESIGN.md` §6.3, §11.2](../CLI_DESIGN.md)).
  Retained size and dominator trees are not on this plan. Phase 6 measures whether they are needed.
* **Multiple resident dumps** and side-by-side diffing. One dump per process.
* **Remote access.** No `--bind`, no auth, no TLS. If dumps live on another machine, run `serve`
  there and use an SSH tunnel.
* **Mutating the dump or the session from the UI.** The web interface is read-only; changing dumps
  means restarting `serve`.
* **Replacing the CLI or the MCP server.** Both stay. This is a third front end over the same Core.
