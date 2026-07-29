# Design brief — the Claude Design instruction prompt

Status: **proposed**.

This document is written to be handed to Claude Design more or less verbatim. Sections 1–3 give it
the domain understanding it cannot infer; sections 4–8 are the constraints and the deliverable list.

The output is a component library of plain HTML/CSS files, synced into this repo with
`/design-sync` and used **as the Razor templates** for the fragments described in
[`SERVER.md` §5](SERVER.md). That is why the markup constraints in §4 are not negotiable: these
files are the shipping implementation, not a mockup of it.

---

## 1. What this product is

A local, single-user web interface for analyzing **.NET memory dumps** — the multi-gigabyte files a
crashed or hung .NET process leaves behind. It runs on `127.0.0.1`, opened from a terminal by a
developer who is debugging a production incident and closed when they are done.

It is a **data tool**, not a dashboard and not a consumer product. Everything on screen is evidence
in an investigation. Density beats whitespace, precision beats approximation, and every number is
either exact or explicitly labelled as an estimate.

The equivalent existing tools are WinDbg with the SOS extension, JetBrains dotMemory, and Visual
Studio's dump analysis window. Users of this interface already know those. Familiarity with that
vocabulary is an asset here, not jargon to be softened away.

## 2. Who uses it and what they are doing

One developer, mid-incident, usually on a deadline. They arrive with a hypothesis and need to confirm
or kill it fast. Three investigations account for nearly all use:

**Memory leak / out-of-memory.** "The service climbed to 6 GB and got OOM-killed." They look at what
occupies the heap, sorted by total size; pick the suspicious type; sample one instance; and ask what
is keeping it alive. That last question — the retention path from a GC root to the object — is the
answer they came for.

**Deadlock / hang.** "Requests stopped completing but the process is alive." They look at every
thread, find the ones blocked, look at what locks are held and by whom, and read the stacks of the
threads involved.

**Unhandled exception.** "It died and the log only has half a stack trace." They find the exception
objects, read the message and inner exceptions, and read the faulting thread's stack.

What they do constantly, in all three: **filter a large list down to the few rows that matter**, and
**follow a reference from one object to another**. The interface is good if those two actions are
frictionless, and bad if it makes them beautiful but slow.

### 2.1 Two facts about the data that must shape the design

**Scale is wildly uneven between views.** The type-summary view has roughly 1,500 rows. The
individual-object view of the same dump has **10.2 million**. A design that assumes "a table you can
scroll" is right for the first and catastrophically wrong for the second. Assume server-side
pagination everywhere, and never design a control that implies the whole dataset is present — no
"select all", no client-side total, no scrollbar whose thumb size means anything.

**The first view of a fresh dump takes seconds, not milliseconds.** Analyzing a cold dump means
walking millions of objects. Afterwards it is instant, permanently. This is a genuine multi-second
wait that needs a real pending state — elapsed time, progress against a known total, and a note that
it happens once. A spinner implies "almost there" and would be a lie. Design this state properly; it
is the first thing every user sees.

## 3. The data, with real values

Design against these. The widths and shapes here are what the components must survive.

### 3.1 Heap types (`dumpheap`) — the leak view, ~1,500 rows

| MethodTable | Count | Total size | Type |
| :--- | ---: | ---: | :--- |
| `00007F2C1A4B2E30` | 4,182,344 | 267,670,016 | `System.String` |
| `00007F2C1A4C0918` | 1,204,881 | 115,668,576 | `System.Byte[]` |
| `00007F2C1B2E4440` | 982,110 | 47,141,280 | `System.Collections.Generic.Dictionary<System.String,MyApp.Domain.CacheEntry>+Entry[]` |
| `00007F2C1C081A50` | 3 | 24 | `MyApp.Infrastructure.Telemetry.MetricsCollector` |

Note what this demands:

* **Addresses are 16 uppercase hex characters, no `0x` prefix.** They must be monospace, and they
  must be selectable and copyable as a unit — users paste them into other commands constantly.
* **Type names have no upper bound.** Generic types with nested generic arguments run past 200
  characters. They must not be truncated destructively (the distinguishing part of a generic type is
  often at the *end*), must not wrap the table into chaos, and must be fully readable somehow —
  middle-truncation with a full value on hover, or a wrapping cell in a fixed column, but decided
  deliberately.
* **Numbers span nine orders of magnitude** in the same column, right-aligned, thousands-separated.
  Sizes need a humanized form (`255.3 MB`) *and* access to the exact byte count — leak analysis is
  arithmetic, and "255.3 MB" cannot be subtracted from anything.

### 3.2 Objects (`listobj`) — 10.2 million rows

| Address | MethodTable | Size | Type |
| :--- | :--- | ---: | :--- |
| `00007F2C14A20918` | `00007F2C1A4B2E30` | 96 | `System.String` |
| `00007F2C14A20978` | `00007F2C1A4B2E30` | 1,024 | `System.String` |

Visually similar to §3.1 and behaviorally nothing like it. This is where server-side paging,
infinite scroll and the "you are looking at 50 of 10,238,441" affordance have to be honest.

### 3.3 Object detail (`dumpobj`)

```
Address       00007F2C14A20918
Type          MyApp.Domain.CacheEntry
Size          72 bytes
MethodTable   00007F2C1B2E4440

Offset  Type                    Name          Value
  0x08  System.String           _key          "tenant:8842:profile"
  0x10  System.Byte[]           _payload      00007F2C14A22100
  0x18  System.DateTime         _createdUtc   2026-07-27 21:16:44Z
  0x20  MyApp.Domain.CacheEntry _next         00007F2C14A20A40
  0x28  System.Int32            _hitCount     14
```

Reference fields (the ones whose value is an address) are **navigable** — clicking one opens that
object. Value fields are not. That distinction must be visible at a glance, because it is the
primary interaction in the whole interface.

**String values are shown, and they contain secrets.** Connection strings, bearer tokens, customer
PII. The design does not need to solve this, but it must not make it worse: no value previews in
list views, no values in tooltips that persist, nothing that puts a heap string somewhere the user
did not deliberately look.

### 3.4 Retention path (`gcroot`) — the tree that matters most

```
▾ [static var] MyApp.Infrastructure.CacheHost.s_instance    (pinned)
  ▾ 00007F2C0E110220  MyApp.Infrastructure.CacheHost                 40 B
    ▾ 00007F2C0E110300  Dictionary<String, CacheEntry>              80 B
      ▾ 00007F2C0E118880  Dictionary<String, CacheEntry>+Entry[]   4.1 MB
        • 00007F2C14A20918  MyApp.Domain.CacheEntry                 72 B   ← target
```

Roots come in kinds that matter to the reader: static variable, local variable on a thread's stack
(with the thread id), GC handle, finalizer queue. Some are **pinned**, which is itself a common cause
of leaks and deserves a visible marker.

**This view has a failure mode that must be designed for.** The search runs under a budget and can
give up before proving anything. When that happens the interface must say so unmistakably — "the
search was truncated after 2,000,000 nodes; this object may still be rooted" — with an action to
re-run without a limit. Rendering an empty truncated result as "not rooted, eligible for collection"
would state the opposite of the truth. Treat this as a first-class, prominent state, not a footnote.

### 3.5 Threads (`clrthreads`)

| Managed id | OS id | Alive | Exception |
| ---: | ---: | :--- | :--- |
| 1 | 24591 | yes | — |
| 14 | 24610 | yes | `System.OutOfMemoryException` |
| 22 | 24618 | no | — |

Expanding a thread reveals its stack frames:

```
▾ Thread 14  (OS 24610)  [OutOfMemoryException]
    MyApp.Api.Controllers.ReportController.Generate(ReportRequest)
    System.Linq.Enumerable+SelectListIterator`2.MoveNext()
    System.Collections.Generic.List`1.AddWithResize(T)
    System.Array.Resize[T](T[] ByRef, Int32)
```

Frames are long, monospace, and their meaningful part is at the **end** (the method name), which is
the opposite of type names. A handful of threads out of hundreds are interesting; the rest are
identical idle worker threads and should be collapsible or de-emphasized as a group.

### 3.6 Heap composition tree (namespace rollup)

The ~1,500 types of §3.1 folded into namespaces with sizes rolled up:

```
▾ System                                          3,891,204 objects   412.8 MB
  ▸ System.Collections.Generic                      982,140 objects    47.2 MB
  ▸ System.Text                                      44,201 objects     8.1 MB
▾ MyApp                                             1,204,551 objects   1.9 GB
  ▾ MyApp.Domain                                    1,198,220 objects   1.9 GB
    • MyApp.Domain.CacheEntry                       1,180,004 objects   1.8 GB
```

The point of this view is that a user sees "MyApp.Domain is 1.9 GB" in one glance without reading a
sorted table. Proportion should be visible in the row itself — a size bar in the cell, not a separate
chart.

## 4. Technical constraints on the markup

These come from how the files are used and are not stylistic preferences.

1. **Plain HTML and CSS.** No React, no Vue, no build step, no CSS framework, no preprocessor. Hand
   JavaScript only where genuinely required, and say why.
2. **Every component is server-renderable as an isolated fragment.** The server sends a piece of
   HTML that replaces an element in the page. A component must therefore be valid and correct when
   returned on its own — no styling that depends on a parent that will not be re-sent, no ids
   generated by script.
3. **Interaction is declarative via htmx attributes.** Use `hx-get`, `hx-target`, `hx-swap`,
   `hx-trigger`, `hx-include`, `hx-push-url` as shown in [`SERVER.md` §5](SERVER.md). Leave the URLs
   as obvious placeholders; the server side fills them in.
4. **Style these htmx states explicitly**, because they are how the UI communicates that it is
   working: `.htmx-request` (an in-flight request originating from this element),
   `.htmx-swapping`, `.htmx-added` (freshly appended rows — a brief highlight, not an animation that
   delays reading), and a disabled/`aria-busy` treatment for controls awaiting a response.
5. **Classes, not inline styles, and no scoped-CSS mechanism.** One stylesheet plus per-component
   classes with a consistent prefix. Design tokens as CSS custom properties on `:root`.
6. **Light and dark themes**, via `prefers-color-scheme` with a `data-theme` attribute override. Both
   must be legible for long sessions; developers reading dumps at 2am are the actual users.
7. **Keyboard reachable.** Filter focus, row navigation, tree expand/collapse, and following a
   reference all need keyboard paths. This is a tool people live in for an hour at a time.
8. **Accessible tables and trees.** Real `<table>` semantics with `<th scope>`; trees as
   `role="tree"`/`role="treeitem"` with `aria-expanded` and `aria-level`. Sort state via
   `aria-sort`.

## 5. Components to deliver

Grouped as they should appear in the Design System pane.

### Group: Shell
* **App shell** — left navigation of view groups (Overview, Heap, Threads, Exceptions, Modules,
  Metadata), main content region, no footer chrome. Collapsible navigation.
* **Dump header bar** — always visible: dump filename, .NET runtime version, architecture, OS,
  process id, total heap size, object count, and a cache-state indicator ("analyzed" vs "computing").
* **View header** — view title, one-line description of what the underlying command does, result
  count ("1,284 of 1,502 types"), and the actions for the view.

### Group: Data
* **Data table** — dense, monospace for addresses and sizes, right-aligned numerics, zebra-free
  (rules instead), sticky header. Sortable column headers with three states (unsorted, asc, desc).
  Row hover and a distinct selected state. Must survive both a 4-column and a 9-column variant.
* **Cell treatments** — address cell (monospace, copyable, click-to-navigate), type-name cell
  (long-value strategy), size cell (humanized + exact), count cell, proportion bar cell.
* **Infinite-scroll sentinel row** — the loading row appended at the end of a page, plus its
  end-of-data state ("all 1,502 rows shown").
* **Empty state** — distinguish three different things clearly: no data in the dump, no rows matched
  the filter (with a clear-filter action), and this view is not applicable to this dump.

### Group: Filtering
* **Filter bar** — a free-text search input plus a set of typed controls: type-name contains,
  size range (with byte units — `4kb`, `1mb`, `2gb`), count range, generation selector
  (gen0/gen1/gen2/LOH/POH), thread id, has-exception toggle. Different views expose different
  subsets, so the bar must compose from an arbitrary selection of these.
* **Active-filter chips** — each applied filter as a removable chip, plus clear-all. Users forget
  what they filtered by; the chips are what stop them misreading a filtered view as the whole truth.
* **Sort control** — for narrow layouts where header-click sorting is impractical.

### Group: Trees
* **Tree view** — expand/collapse affordance, connector lines, per-node badges, lazy-loading node
  state, and a "312 more…" node for capped fan-out. One component, four content variants:
  namespace rollup (§3.6), retention path (§3.4), thread→frames (§3.5), object references (§3.3).
* **Tree node variants** — namespace node (name + rolled-up count/size + proportion bar), object
  node (address + type + size), root node (root kind + name + pinned/thread badges), frame node
  (method signature), cycle node (a reference back to an ancestor — terminal, visually distinct),
  and truncation node.
* **Breadcrumb** — the path taken through the object graph, each hop clickable.

### Group: Detail
* **Object detail card** — the §3.3 layout: header fields, then the field table with reference fields
  visibly navigable and value fields not.
* **Exception detail** — type, message, HRESULT, stack trace, and nested inner exceptions to
  arbitrary depth.
* **Key–value block** — the general form used by `info`, `threadpool`, `eeheap` and the metadata
  views.

### Group: Status
* **Pending / progress panel** — the §2.1 multi-second state. Elapsed time, progress bar with a real
  denominator, what is running, and the "this happens once per dump" note.
* **Truncation banner** — the §3.4 case. Prominent, warning-toned, states what limit was hit and
  offers the re-run action. This is the most consequential component in the library; a user who
  misses it draws the wrong conclusion.
* **Error state** — a failed analysis with the message and a retry.
* **Badges** — neutral / info / warning / danger pills used for pinned, thread ids, root kinds,
  generation, exception types, alive/dead.

## 6. Visual direction

* **Dense and quiet.** Small type, tight rows, restrained color. Color carries meaning (warning,
  danger, selected, navigable) and is never decorative — a colorful row in this UI must always mean
  something.
* **Monospace for machine values** — addresses, sizes, offsets, method signatures. Proportional for
  prose. Never mix within a cell.
* **Alignment does the work.** Numeric right, text left, consistent column widths across views so the
  eye can move between them.
* **No charts requiring a library.** Proportion bars, sparkline-ish inline bars and stacked
  composition bars in pure CSS are welcome and useful. Anything needing d3 or Chart.js is out.
* **No animation that delays information.** Transitions under 150 ms, none on data appearing.

## 7. Explicitly not wanted

* Dashboard tiles, KPI cards, "at a glance" summaries that hide the numbers behind them.
* Marketing chrome: hero sections, illustrations, onboarding tours, empty-state artwork.
* Anything implying multi-user, sharing, saving, accounts, or persistence. The session ends when the
  process does.
* Controls implying the client holds the full dataset — client-side totals, select-all, export-all.
* Modal dialogs for anything in the investigation path. Modals break the reading flow and cannot be
  linked to.

## 8. Deliverable format

* One HTML file per component under a clear path (`components/<group>/<name>.html`), each
  self-contained enough to preview on its own.
* **First line of every file must be a card marker**, which is what builds the Design System pane
  index:
  ```html
  <!-- @dsCard group="Data" -->
  ```
  Use the group names from §5 verbatim.
* One shared `styles.css` with the design tokens as CSS custom properties, plus per-component styles.
* Populate every component with the **real sample data from §3**, not lorem ipsum. Placeholder data
  hides exactly the problems this brief is trying to surface — the 200-character generic type name,
  the nine-order-of-magnitude number column, the 10-million-row count.
