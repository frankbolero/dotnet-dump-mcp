# Design resync procedure

How a change made in Claude Design reaches this application, and what a designer may change without
a C# edit. Task 3.1 established this; task 3.5 is where it gets its final form once every view
exists.

The property being protected is the Phase 3 exit criterion: **re-running `/design-sync` after a
purely visual change must require no C# edits.** If it does, the templates got too clever.

## What arrived, and why there is a translation layer at all

`/design-sync` writes `design-sync/`: seven component pages (`*.dc.html`) plus a design-system
directory (`_ds/nocturne-*/`).

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

## Resyncing

1. Re-run `/design-sync`.
2. **Tokens.** Diff `themeTokens()` in `design-sync/Shell.dc.html` against the two `:root` blocks in
   `dndump.css`. A colour change is this step and nothing else.
3. **Components.** For each changed inline style, update the corresponding class. The class names
   are `dn-`-prefixed and follow the page they came from.
4. **Structure.** Only if elements were added, removed or reordered.
5. Rebuild and check the discipline below.

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
* **Light theme is CSS, not JavaScript.** The design page toggles `state.theme` and recomputes
  inline styles. Here both palettes are CSS custom properties, defaulting to `prefers-color-scheme`
  with a `data-theme` attribute override, which is what `DESIGN_BRIEF.md` §6 asked for and what
  works without script.
* **Middle truncation is responsive.** `Shell.dc.html` truncates type names at a fixed 38/14
  character split in JavaScript, which cuts short names on a wide screen and long ones anyway on a
  narrow one. Here `MiddleTruncated` splits off a fixed-length tail and CSS flexes the head, so the
  visible cut follows the column width. The tail is never cut — for a .NET type name the tail is
  what distinguishes `Dictionary<String,CacheEntry>+Entry[]` from `Dictionary<String,CacheEntry>`.
* **Empty states distinguish filtered from genuinely empty.** The design page shows one empty state;
  `DESIGN_BRIEF.md` §5 asks for three. Telling a user "no data" when they have over-filtered sends
  them looking for a problem in the dump.
