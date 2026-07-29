# `DotNetDump.Web` — host architecture

Status: **proposed**.

The web host: an ASP.NET Core minimal-API application that renders HTML fragments from
`DotNetDump.Core` analyzer results and swaps them into a page with htmx. Launched by
`dndump serve`, bound to loopback, one dump per process.

Companion documents: [`DATA_CONTRACT.md`](DATA_CONTRACT.md) (what it serves),
[`DESIGN_BRIEF.md`](DESIGN_BRIEF.md) (what the fragments look like).

## 1. Project shape

| Item | Value |
| :--- | :--- |
| Project | `src/DotNetDump.Web/DotNetDump.Web.csproj` |
| SDK | `Microsoft.NET.Sdk.Web` |
| Target frameworks | `net8.0;net9.0;net10.0` (matches the solution) |
| References | `DotNetDump.Core` only |
| Rendering | Razor Pages / Razor components rendered to string — **no** Blazor interactivity, no SignalR |
| Client dependency | htmx, vendored as a single file under `wwwroot/lib/` and served locally |

`DotNetDump.Cli` references `DotNetDump.Web` so `dndump serve` can start it in-process. The
dependency runs one way only: `Web` must never reference `Cli`, and neither references
`DotNetDump.Server` or `ModelContextProtocol`.

### 1.1 Vendoring htmx

htmx ships from a CDN by default. Vendor it instead: this is a tool that reads memory dumps
containing secrets, and it must not make outbound requests. `wwwroot/lib/htmx.min.js` is committed,
version-pinned, and referenced with an integrity hash. No CDN, no bundler, no Node.

### 1.2 `dndump serve`

```
dndump serve [--dump <path>] [--port <n>] [--no-open] [--no-warm]
```

Dump resolution uses the shared `DumpResolver` moved to Core
([`DATA_CONTRACT.md` §5](DATA_CONTRACT.md)) — the same `--dump` → `DNDUMP_PATH` →
`.dndump/session.json` precedence as every other command, so `dndump use X && dndump serve` works
the way a user would assume.

Default port `5111`, `--port 0` for an ephemeral one. On start it prints the URL to stderr and opens
a browser unless `--no-open`. The dump loads eagerly during startup: a `serve` that comes up and
then fails on the first request is worse than one that refuses to start.

## 2. Routes

Three families, one shared handler pipeline.

| Route | Returns | Purpose |
| :--- | :--- | :--- |
| `GET /` | Full HTML page | App shell: navigation, dump header, first view. |
| `GET /views/{view}` | HTML **fragment** | The view's table/detail body. Every filter, sort and page change hits this. |
| `GET /views/{view}/rows` | HTML fragment (`<tr>`s only) | Infinite-scroll append. Same query string, different swap target. |
| `GET /trees/{tree}/{id?}` | HTML fragment (`<li>` subtree) | Lazy tree expansion ([`DATA_CONTRACT.md` §4](DATA_CONTRACT.md)). |
| `GET /api/{view}` | JSON | The existing `JsonFormatter` envelope. |
| `GET /status/{jobId}` | HTML fragment or JSON | Pending-state polling (§4). |
| `GET /health` | `200` | Liveness, for the browser-open race and for scripts. |

**The JSON routes are not an afterthought.** They serve the same handler, the same
`QueryParameters`, and the same `FilterSpec` binding as the HTML routes — only the final formatter
differs. That keeps the API contract from drifting away from what the UI actually exercises, and it
means an agent can `curl` the running server with the vocabulary it already knows from the CLI.

### 2.1 Query-string binding

One binder, `ViewRequestBinder`, turns the query string from
[`DATA_CONTRACT.md` §3.2](DATA_CONTRACT.md) into `(FilterSpec, QueryParameters)`. It is shared by
every route and every view. A filter field a view does not honor is a `400` on the JSON route and is
simply not rendered as a control on the HTML route.

`limit` is clamped server-side (default 50, max 500). It arrives from the URL, so it arrives from the
user, so it is untrusted.

## 3. Concurrency: the analysis queue

**`ClrRuntime` and `ClrHeap` are not thread-safe.** ClrMD makes no concurrency guarantee, and the
DAC underneath it is a single-threaded COM-style interface. Every consumer of Core so far has been
accidentally safe: the CLI runs one command per process, and the MCP server's stdio transport
serializes tool calls. A web server is concurrent by default — two browser tabs, or one tab with a
filter box firing while a tree expands, is enough to corrupt state or crash the DAC.

So all analyzer access goes through a single worker:

```csharp
public interface IAnalysisQueue {
    /// <summary>Runs work on the single analysis thread. Cancellation abandons the wait, not the work.</summary>
    Task<T> Enqueue<T>(Func<CancellationToken, T> work, string label, CancellationToken ct);
}
```

* One long-lived thread owns `IDumpContext` and every analyzer instance.
* Requests submit work and await the result. Order is FIFO.
* `label` is what the pending-state UI displays ("walking heap", "resolving roots").
* **Cancellation is cooperative and partial.** A browser navigating away cancels the *wait*; the walk
  itself continues to completion so its cache entry still lands. Abandoning a 12-second walk at
  second 11 because a user clicked elsewhere is the wrong trade.

### 3.1 The consequence: long walks block short requests

A cold `dumpheap` walk occupying the worker for seconds stalls a queued `dumpobj` that would take a
millisecond. That is the direct cost of the thread-safety constraint, and it is why §4 exists rather
than being a nicety.

Two mitigations, both cheap:

1. **Cache hits never enter the queue.** `IAnalysisCache` lookups are thread-safe and touch no ClrMD
   state, so a warm result is served straight from the request thread. After the startup warm (§4.1)
   this is the common path, and the queue is mostly idle.
2. **The queue is depth-aware.** Work submitted while a long job is running returns a pending
   fragment immediately rather than blocking the HTTP request for an unbounded time.

An alternative — a pool of `ClrRuntime` instances over the same dump — was considered and rejected:
it multiplies the DAC's memory footprint per instance and buys parallelism that a single-user local
tool has no use for.

## 4. Speed

The strategy is perceived speed over the existing tier-1 cache. No new storage substrate.

### 4.1 Warm at startup

When `serve` starts, it enqueues the walk-scale operations in usage order — heap statistics, then
heap exceptions, then sync blocks — before any request arrives. The user is still reading the
overview page while the expensive walk completes in the background.

This is where a long-lived process beats the CLI outright. The CLI pays dump load plus DAC init per
invocation and discards ClrMD's in-memory type cache every time; `serve` pays both once and keeps
both. Combined with the disk cache, the second and later views of a dump are instant, and a
previously analyzed dump is instant even on the first view.

`--no-warm` skips it, for when the user wants a specific cheap view of a large dump immediately.

### 4.2 Honest pending states

When a request needs work that is not cached and not yet complete, the handler returns a pending
fragment rather than holding the connection:

```html
<div id="v-dumpheap" hx-get="/status/7f3a" hx-trigger="every 1s" hx-swap="outerHTML">
  <p>Walking the heap — 4.2M of ~10.2M objects</p>
  <progress value="4200000" max="10200000"></progress>
  <p class="muted">Elapsed 3.1s. First walk on this dump; later views are instant.</p>
</div>
```

The rules, from [`../CLI_DESIGN.md` §10.4](../CLI_DESIGN.md):

* **Never a bare spinner.** A spinner implies imminence, and a cold walk on a large dump is seconds.
* **Show elapsed time and what is running** (the queue's `label`).
* **Show progress when the analyzer can report it** — the `IProgress<WalkProgress>` hook from
  [`DATA_CONTRACT.md` §5](DATA_CONTRACT.md). Object counts are known from the segment metadata, so
  the denominator is real, not invented.
* **Say that it happens once.** The single most useful thing the pending state can tell a first-time
  user is that this cost is not recurring.

Polling every second via `hx-trigger="every 1s"` is deliberately unsophisticated. SSE would be
tidier; it also adds a connection lifecycle to get wrong, for a page that already knows the work is
seconds long. Revisit only if it proves inadequate.

### 4.3 What "instant" means here

| Path | Expectation |
| :--- | :--- |
| Cache hit, filter/sort/page change | < 50 ms — no ClrMD access at all |
| Single-object read (`dumpobj`, object tree expand) | < 50 ms, cold or warm |
| Cold walk-scale view | Seconds. Pending state, progress, no lying |

Phase 6 measures these against a real dump. If cache-hit filter/sort exceeds ~200 ms on the
~1,500-row heap-stat set, the bottleneck is fragment rendering, not analysis, and the fix is
rendering — not the tier-2 index.

## 5. Fragments and htmx conventions

The conventions the design library must be built against ([`DESIGN_BRIEF.md` §4](DESIGN_BRIEF.md)).

### 5.1 Swap targets

Every view fragment has a stable id, `#v-{view}`, and swaps `outerHTML`. Every table body has
`#rows-{view}`.

```html
<!-- filter bar -->
<input name="text" placeholder="Filter…"
       hx-get="/views/dumpheap" hx-target="#v-dumpheap" hx-swap="outerHTML"
       hx-trigger="input changed delay:250ms, search"
       hx-include="closest form" hx-push-url="true">

<!-- sortable header -->
<th><a hx-get="/views/dumpheap?sort=totalSize&order=asc"
       hx-target="#v-dumpheap" hx-push-url="true">Total size ▾</a></th>

<!-- infinite scroll sentinel: last row of the current page -->
<tr hx-get="/views/dumpheap/rows?offset=50" hx-trigger="revealed"
    hx-swap="afterend" hx-target="this"><td colspan="4" class="loading">…</td></tr>
```

Three rules the markup must obey:

1. **`hx-push-url="true"` on every state change.** The query string is the view state
   ([`DATA_CONTRACT.md` §3.2](DATA_CONTRACT.md)), so back/forward and bookmarking work with no
   client-side state.
2. **`hx-include="closest form"`** on filter and sort controls, so a sort keeps the active filter and
   vice versa. Getting this wrong silently drops the user's filter — the most likely bug in the whole
   UI.
3. **The scroll sentinel is the last row and replaces itself.** `hx-swap="afterend"` with
   `hx-target="this"` appends the next page and the server emits a fresh sentinel — unless
   `HasMore` is false, in which case it emits none and the scrolling stops naturally.

### 5.2 Out-of-band updates

The result count, the pagination footer and the cache-state indicator live outside the swapped
table. They update via `hx-swap-oob="true"` on elements included in the same response, so one
round trip updates every part of the view.

### 5.3 Rendering

Fragments are Razor views rendered to string. Each takes a view model, never an analyzer, never
`IDumpContext` — handlers do the analysis, views do the markup. That is the same discipline the CLI
and MCP layers already follow, and it is what keeps the design library swappable.

## 6. Security

Dumps contain connection strings, tokens and PII in heap strings, and `dumpobj` renders string
values. The posture is the minimum that is actually safe, not the minimum that looks safe.

| Control | Implementation |
| :--- | :--- |
| **Loopback only** | Kestrel binds `http://127.0.0.1:{port}` explicitly. No `--bind`, no `0.0.0.0`, no `ASPNETCORE_URLS` honored — the env var is cleared at startup so it cannot widen the binding. |
| **Host-header validation** | Middleware rejects any request whose `Host` is not `localhost`/`127.0.0.1`/`[::1]` + the bound port. Without it, a page on the internet can point DNS at `127.0.0.1` and reach the server from the user's own browser. Loopback binding alone does not stop this. |
| **No CORS** | No CORS headers are emitted, so cross-origin JavaScript cannot read responses. |
| **No auth** | Correct given the above: anyone who can reach loopback already runs code on the machine. Adding a token would guard against other local users, which is not this tool's threat model. |
| **No outbound requests** | htmx vendored (§1.1), no CDN, no fonts, no telemetry. Symbol-server access stays opt-in through the existing `DOTNETDUMP_SYMBOL_PATHS`. |
| **No secrets in logs** | Request logging records the view, the filter *fields* and the timing — never rendered values, never object contents. A log file that quietly accumulates heap strings would undo everything above. |
| **Read-only** | No route mutates the dump, the session file, or the cache. Changing dumps means restarting `serve`. |

### 6.1 Docker

The CLI image gains a `serve` entry point. The port is published to loopback only:

```bash
docker run --rm -p 127.0.0.1:5111:5111 \
  -v "/path/to/dumps:/dumps" -v dndump-cache:/cache \
  -e DNDUMP_CACHE=/cache \
  dndump-web serve --dump /dumps/prod-oom.core --port 5111
```

`-p 5111:5111` without the `127.0.0.1:` prefix publishes on every interface and exposes heap
contents to the network. The wrapper script must emit the loopback form, and the README must show
only that form — this is the one place where a copy-pasted command can undo §6 entirely.

## 7. Testing

| Layer | Approach |
| :--- | :--- |
| Filter binding | Unit tests on `ViewRequestBinder`: query string → `(FilterSpec, QueryParameters)`, clamping, unsupported-field rejection. No dump needed. |
| Filter semantics | Unit tests on `FilterSpec` application over hand-built model lists. No dump needed. |
| Tree building | Unit tests on namespace rollup (generic arity, fan-out cap), gcroot trie merging, and object-tree cycle detection over synthetic data. **Cycle detection needs a deliberately cyclic fixture** — it will not appear by accident. |
| Analysis queue | Concurrent submissions observe serialized execution; cancellation abandons the wait but completes the work. |
| Routes | `WebApplicationFactory` against a fake `IDumpContext`, asserting fragment ids, swap attributes and OOB targets — the htmx contract is markup, so it is testable as markup. |
| Security | Host-header rejection, binding address, absence of CORS headers. These are cheap tests guarding the properties most likely to regress silently. |
| End to end | One integration test against a real sample dump: load, warm, render `dumpheap`, filter it, expand a tree. |
