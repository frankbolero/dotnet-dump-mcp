# htmx 4 upgrade assessment

Status: **informational**. No upgrade is scheduled; this exists so the decision, when made, isn't
made blind.

`DotNetDump.Web` vendors **htmx 2.0.9** (`Rendering/Assets.cs`, `wwwroot/lib/htmx.min.js`,
`VENDORING.md`), pinned by SRI hash and asserted every test run by `HtmxIntegrityTests`. htmx 4 is
in beta (`4.0.0-beta6`, released 2026-07-23) with a stated Summer-2026 GM target, so a stable release
could land within this project's normal maintenance window. This document audits every `hx-*`
attribute and every server response actually used in this codebase against htmx 4's documented
breaking changes, rather than reciting the general migration guide. Two things break here; the rest
either don't apply or already avoid the trap.

## 1. What breaks

### 1.1 Error responses will start swapping into fragments — HIGH risk

htmx 2 does not swap 4xx/5xx responses into the target by default; htmx 4 swaps everything except
`204`/`304`. This codebase returns plain-text, non-fragment-shaped error bodies on purpose:

* `TryBind` (`Routes/DumpRoutes.cs:743-753`) returns `400` with `Results.Text(ex.Message, "text/plain; charset=utf-8", ...)` for a rejected filter or malformed query.
* `TryRequireAddress` (`Routes/DumpRoutes.cs:759-770`) returns the same `400`/plain-text shape for a missing or malformed detail-view address.
* `NotWiredYet` (`Routes/DumpRoutes.cs:806-809`) returns `501` with a plain-text "not yet wired" message for a view the nav links to but no handler serves.

Every fragment response, by contrast, is wrapped in `<div id="v-{view}">…</div>` — the id every
later `hx-target="#v-{view}"` swap depends on (`DumpHeap.cshtml:36`, and the same shape in every
other `Views/Fragments/*.cshtml`). Under htmx 4's default, a `400` or `501` reply would swap its bare
`text/plain` body into that target, **destroying the `#v-{view}` element itself**. Every subsequent
sort, filter, or scroll action targeting that id would then find nothing to swap into, and there
would be no visible error — just a dead view. Today, under htmx 2, the same response is silently
ignored by the swap logic and the last-good fragment stays on screen (arguably its own quieter bug,
but not a destructive one).

Mitigation, in order of preference:
1. Set `htmx.config.noSwap = [204, 304, '4xx', '5xx']` (or load the `htmx-2-compat` extension) to
   restore htmx 2's behavior outright — cheapest, but punts on ever using the new default.
2. Give error responses the same `<div id="v-{view}">` shape as a real fragment, so a bad swap
   degrades to an in-place error message instead of erasing the target. This is more work but is the
   only option that lets `hx-status:400="target:#v-{view}"`-style per-status handling (new in v4)
   render something useful.

Either way this needs to be decided *before* the vendored file is swapped — it is the one change in
this list with a destructive failure mode, not just a cosmetic one.

### 1.2 Default request timeout drops from unlimited to 60s — MEDIUM risk

htmx 2's default `timeout` is `0` (no timeout); htmx 4's `defaultTimeout` is `60000`. Nothing in this
codebase currently sets a client-side timeout (confirmed by grep — no `htmx.config` calls, no
`hx-timeout` in any fragment), so today every request effectively waits as long as the analysis queue
takes. `AnalysisQueue` serializes all analyzer calls onto one worker (`SERVER.md §3`) specifically
because `ClrRuntime`/`ClrHeap` aren't thread-safe — a large heap walk queued behind other work, or a
slow `dumpheap`/`gcroot`-style query against a genuinely large dump, is exactly the kind of request
this tool exists to run and that could plausibly clear 60 seconds. The MCP server side of this
project already documents a 600000ms (10-minute) client timeout recommendation for comparable
symbol-loading work — the same order-of-magnitude concern applies here.

Mitigation: set `htmx.config.defaultTimeout = 0` explicitly (or a generous fixed value) rather than
inheriting the new default, and treat this as a required config addition on upgrade, not an
oversight to catch later.

## 2. What doesn't break (verified, not assumed)

| htmx 4 change | Status here | Evidence |
| :--- | :--- | :--- |
| Attribute inheritance becomes explicit (`:inherited` needed) | **Not used** — every `hx-get` element sets its own `hx-target`/`hx-include`/`hx-push-url` inline | `DumpHeap.cshtml:44,48`, `_FilterBar.cshtml:31-50`, and the tally: 31×`hx-get`, 30×`hx-target`, 24×`hx-include` — near 1:1, i.e. explicit per element, not inherited from a container |
| `hx-swap="innerHTML show:#other:top"` combined-modifier syntax removed | **Not used** — only bare `outerHTML` appears anywhere | Every `_*Rows.cshtml` infinite-scroll row, e.g. `_DumpHeapRows.cshtml:50` |
| Attribute renames (`hx-disable`→`hx-ignore`, `hx-vars`→`hx-vals`, `hx-prompt` moved to extension, `hx-disinherit`/`hx-inherit` dropped) | **None of these attributes appear anywhere in the project** | grep across `Views/` |
| Extension API rewritten (`defineExtension`→`registerExtension`) | **No extensions loaded** — no `hx-ext` anywhere, no `htmx.defineExtension` calls | grep across `Views/`, `wwwroot/`, `*.cs` |
| `htmx:*` event renames (`htmx:beforeRequest`→`htmx:before:request`, etc.) | **No JS listens to any htmx event** — the only inline script is the theme toggle, which never touches `htmx.*` | `_Layout.cshtml:25-41`; grep for `htmx.` / `htmx:` across the project turns up only the `HtmxPath`/`HtmxVersion` constants and a code-comment reference to vendored internals |
| `revealed`/`intersect` trigger semantics | **Already on the IntersectionObserver-backed `intersect` trigger**, deliberately, after a debugged incident where `revealed`'s window-scroll heuristic missed a non-window-scrolling container | `_DumpHeapRows.cshtml:14-22`; confirmed `intersect` is retained in v4 and gains `root`/`rootMargin` options rather than being altered |
| OOB (`hx-swap-oob`) swap-ordering change (main content now swaps *before* OOB elements, reversed from v2) | **No dependency between the two** — the one OOB swap in this app (`AppendCountOob`, `Routes/DumpRoutes.cs:795-798`) updates `#view-count`, which is unrelated to and never read by the `#v-{view}` swap it rides alongside | `Routes/DumpRoutes.cs:795-798` |

## 3. Neutral, but worth a second look at upgrade time

* **XHR → fetch() internals.** The core request mechanism changes, which is why the event names
  above change too. This project makes no outbound requests by design (`SERVER.md §1.1, §6`) and all
  htmx traffic is same-origin loopback, so this has no bearing on the "no network egress" guarantee —
  fetch() is just as same-origin-bound as XHR was. The only actual exposure is that any *future*
  custom JS added against htmx events must target v4's `htmx:<phase>:<system>` naming; there's
  nothing to migrate today because nothing hooks events yet.
* **History handling: sessionStorage DOM snapshots → live re-fetch on back navigation.** htmx 2
  caches rendered DOM client-side for fast back/forward; htmx 4 just re-requests the page. For a tool
  whose entire premise is that heap strings can carry secrets and PII, *not* caching rendered dump
  content in browser storage is arguably a posture improvement, not a regression — but it does mean
  back-navigation after a slow analysis re-pays that cost. Given `hx-push-url="true"` is already used
  throughout the sort/filter/infinite-scroll paths (`DumpHeap.cshtml:44,48`), this is worth a manual
  spot-check on upgrade, not a code change.
* **The `htmx.org@4.0.0-beta*` `upgrade-check` CLI.** htmx's own migration tooling is an `npx`
  package. `VENDORING.md` and `SERVER.md §1.1` are explicit that this project uses "no CDN, no
  bundler, no Node" as a hard constraint on what ships — that constraint is about the *served*
  artifact, not the developer's machine, so running the checker locally during the upgrade PR is
  fine; just don't let anything it touches (a `package.json`, a `node_modules/`) end up committed.

## 4. Recommendation

Wait for a stable `4.0.0` — betas have already renamed at least one event between `beta5` and
`beta6` (`htmx:swap:finally` → `htmx:finally:swap`), so pinning to a beta now means re-verifying this
whole document against the next one. When it ships:

1. Decide and implement the §1.1 error-swap mitigation *first* — it's the only change here with a
   destructive failure mode, and it should be exercised against the existing `400`/`501` paths in
   `DumpRoutes.cs` before anything else changes.
2. Add the explicit `defaultTimeout` override from §1.2 in the same change.
3. Follow `VENDORING.md`'s existing update procedure (re-fetch, re-hash, update
   `Assets.HtmxVersion`/`Assets.HtmxIntegrity`, let `HtmxIntegrityTests` confirm the hash) — that
   mechanism itself needs no changes for this upgrade.
4. Re-run the §2 grep sweep (`hx-disable`, `hx-vars`, `hx-prompt`, `hx-ext`, `hx-inherit`,
   `hx-disinherit`, combined `hx-swap` modifiers) against whatever's in `Views/` at upgrade time, in
   case a phase shipped between now and then introduced one.
