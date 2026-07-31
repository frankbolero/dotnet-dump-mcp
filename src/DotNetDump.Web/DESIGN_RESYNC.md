# Design resync procedure

How a change made in Claude Design reaches this application, and what a designer may change without
a C# edit. Task 3.1 established this; task 3.5 is its final form, written once all 25 views existed
(3.3's eight list views, 3.4's seventeen detail views) so the shape inventory below is complete
rather than a guess extrapolated from `dumpheap` alone.

Verified mechanically against the current tree, not just asserted: `grep -rn 'style='
Views/` matches only inside `@* ... *@` doc comments that describe the rule in prose (19 hits, zero
of them an actual attribute), and `grep -c 'https\?://'` across every view and the layout is `0`.

The property being protected is the Phase 3 exit criterion: **re-running `/design-sync` after a
purely visual change must require no C# edits.** If it does, the templates got too clever.

## What arrived, and why there is a translation layer at all

`/design-sync` writes `design-sync/`: seven component pages (`*.dc.html`) plus a design-system
directory (`_ds/nocturne-*/`). Of the seven, this application currently consumes three:
`Shell.dc.html` (the app shell, 3.2), `Data.dc.html` (the list-view table, 3.1/3.3) and
`Detail.dc.html` (the detail-view shapes, 3.4). `Filtering.dc.html` bears on Phase 4,
`Trees.dc.html` on Phase 5, and `Status.dc.html` on Phase 6 — none of them ported yet, so a resync
touching only those three pages has nothing in this codebase to update. `Canvas.dc.html` is the
design system's own demo scaffold and was never a source for anything here.

The component pages **do not use** the design system's stylesheet. Across all seven there are 586
inline `style=` attributes and zero `class=` attributes; every value is computed in JavaScript from
a `themeTokens(mode)` function in `Shell.dc.html`. That function carries roles
`_ds/styles.css` does not have — `surfaceAlt`, `accentSoft`, `accentText`, and the whole
`warn`/`danger`/`ok` set — and it carries the **light palette**, which `_ds/styles.css` lacks
entirely.

So `themeTokens()` is the source of truth for this application's look, and
`wwwroot/css/dndump.css` is its extraction. `_ds/styles.css` is deliberately not linked: two token
systems, one of which nothing consumes, drift silently.

## The layers

| Layer | File | Who edits it |
| :--- | :--- | :--- |
| Tokens | `wwwroot/css/dndump.css`, the `:root` blocks | Copied verbatim from `themeTokens()` |
| Components | `wwwroot/css/dndump.css`, the rules below the tokens | Lifted from the pages' inline styles |
| Structure | `Views/**/*.cshtml` | Only when the *markup* changes, not the styling |
| Data | `Rendering/ViewModels.cs`, `Rendering/Display.cs` | Only when the data shown changes |

## The four fragment shapes

Not visible until 3.4 wired all seventeen detail views: a fragment is one of four shapes, and which
one a new view gets is a mechanical decision, not a per-view judgment call.

| Shape | Bound to | Example | Chosen when |
| :--- | :--- | :--- | :--- |
| Table | `ListModel<T>` | `DumpHeap.cshtml` | The view is a filtered/sorted/paged collection of like rows. |
| Key-value | `DetailModel<T>` | `Info.cshtml` | One record, no address identity, no field list — `threadpool`, `eeheap`, `verifyheap`. |
| Identity card + table | `DetailModel<T>` | `DumpObj.cshtml` | One record *with* an address identity (`verifyobj`, `dumpmt`, `dumpmd`, `dumpclass`, `dumpmodule`, `dumpassembly`, `ip2md`) or a two-segment lookup identity (`name2ee`, which bypasses the address-routing switch entirely — see `Name2EE.cshtml`'s own comment). |
| Card list | `ListModel<T>` | `DumpStack.cshtml` | `ViewKind.Detail` in the catalog, but the underlying data is `PagedResult<T>` (`dumpstack`, `verifyheap` if it grows rows) — each *item* is itself a multi-row record, so it gets one `dn-card` per item inside the same `#rows-@Model.View.Name` id a table body would use, rather than being forced into `dn-table`'s flat rows. This is the shape to reach for if a future view is "paginated but each row is a small record," not a special case invented for stacks alone. |

Card-shaped and key-value-shaped views all still use the single `#v-@Model.View.Name` /
`#rows-@Model.View.Name` id pair — Phase 4's infinite scroll and Phase 4.5's out-of-band updates
key off that pair regardless of which of the four shapes fills it.

The `{address?}` route (`/views/{view}/{address?}`, mirrored at `/api/{view}/{address?}`) covers
the identity-card shape's eight address-taking views. `name2ee` is the one exception with its own
two-segment route (`/views/name2ee/{module}/{type}`) and its own handler path
(`RenderName2EEView`/`RenderName2EEJson`), documented in `DumpRoutes.cs` and in `Name2EE.cshtml`'s
own header comment. A future view needing more than one free-form identity argument copies that
precedent rather than overloading `{address?}`.

## Resyncing

1. Re-run `/design-sync`.
2. **Tokens.** Diff `themeTokens()` in `design-sync/Shell.dc.html` against the two `:root` blocks in
   `dndump.css`. A colour change is this step and nothing else.
3. **Components.** For each changed inline style, update the corresponding class. The class names
   are `dn-`-prefixed and follow the page they came from — `dn-table`/`dn-th`/`dn-td` from
   `Data.dc.html`, `dn-card`/`dn-kv` from `Detail.dc.html`, `dn-nav`/`dn-dumpbar` from
   `Shell.dc.html`.
4. **Structure.** Only if elements were added, removed or reordered.
5. **New view.** If the sync adds a command with no fragment yet, pick its shape from the table
   above before writing any markup — it is a lookup, not a design decision to redo per view.
6. Rebuild and check the discipline below.

## Rules a view must obey

These are what keep step 4 rare. All are checkable mechanically.

* **No `style=` attribute in any `.cshtml`, ever.** Styling is a class. Verify with
  `curl -s http://127.0.0.1:5111/ | grep -c 'style='` — it must be `0`.
* **No outbound request.** No CDN, no fonts, no telemetry (`SERVER.md` §6). The design pages'
  Google Fonts `<link>` is replaced by `@font-face` over vendored woff2 in `dndump.css`. Verify with
  `grep -c 'https\?://'` on the rendered page — it must be `0`.
* **No formatting logic in a template.** Byte humanization, thousands separators, address hex and
  middle truncation live in `Rendering/Display.cs`. A template renders values; it does not compute
  them.
* **No conditional beyond presence/absence.** `@if (Items.Count == 0)` is fine. A branch that picks
  between two layouts means two fragments.
* **The htmx contract is markup** (`SERVER.md` §5.1) and is derived from the view name, never
  hardcoded: `id="v-@Model.View.Name"` on the fragment root, `id="rows-@Model.View.Name"` on the
  table body.

## Things the extraction changed on purpose

Recorded so a resync does not "restore" them.

* **Fonts are local.** See above. Non-negotiable.
* **Light theme is CSS, not JavaScript, and dark is the default.** The design page toggles
  `state.theme` and recomputes inline styles; here both palettes are CSS custom properties.
  `DESIGN_BRIEF.md` §6 asked for `prefers-color-scheme` with a `data-theme` override, and that is
  deliberately **not** what this does. Nocturne is dark-first — its readme opens "a quiet, compact
  dark interface", its guidance is written throughout in terms of a dark ground, and its own
  `Shell.dc.html` boots dark with a manual toggle. Following the OS preference put a developer on a
  light machine in front of the face the product was not composed in, and it read as wrong without
  being nameable. Dark is now the default and light is opt-in via `data-theme="light"`.
* **Middle truncation is responsive.** `Shell.dc.html` truncates type names at a fixed 38/14
  character split in JavaScript, which cuts short names on a wide screen and long ones anyway on a
  narrow one. Here `MiddleTruncated` splits off a fixed-length tail and CSS flexes the head, so the
  visible cut follows the column width. The tail is never cut — for a .NET type name the tail is
  what distinguishes `Dictionary<String,CacheEntry>+Entry[]` from `Dictionary<String,CacheEntry>`.
* **Empty states distinguish filtered from genuinely empty.** The design page shows one empty state;
  `DESIGN_BRIEF.md` §5 asks for three. Telling a user "no data" when they have over-filtered sends
  them looking for a problem in the dump.
