# Data contract

Status: **proposed**.

What `DotNetDump.Core` must expose for the web interface to exist. Everything here is backend work
that is testable through the `dndump` CLI before a single line of web code is written — that is
deliberate, and it is why this is Phase 0.

Companion documents: [`SERVER.md`](SERVER.md) (how these are served),
[`DESIGN_BRIEF.md`](DESIGN_BRIEF.md) (how they are rendered).

## 1. What already exists

Not to be rebuilt. The web interface consumes these as they are:

| Component | Location | Notes |
| :--- | :--- | :--- |
| Analyzers | `DotNetDump.Core/Analyzers/` | `HeapAnalyzer`, `ThreadAnalyzer`, `ModuleAnalyzer`, `MetadataAnalyzer`, `SessionAnalyzer`. |
| Models | `DotNetDump.Core/Models/` | `HeapStatItem`, `HeapObjectItem`, `ObjectDetails`, `GCRootSearchInfo`, `ThreadInfo`, … |
| Pagination | `Models/PagedResult.cs` | `Items`, `TotalAvailable`, `Offset`, `Limit`, derived `HasMore`. |
| Query parameters | `Models/QueryParameters.cs` | `Limit`, `Offset`, `SortBy`, `SortDirection`. |
| Result cache | `DotNetDump.Core/Caching/` | `IAnalysisCache` with `Null`/`Memory`/`FileSystem`/`Tiered` providers, keyed on `DumpIdentity`. |
| JSON envelope | `Formatting/JsonFormatter.cs` | `{ data, pagination }`, camelCase, explicit `[JsonPropertyName]`, 16-char uppercase hex addresses. |

Five analyzer methods currently return `PagedResult<T>`
(`HeapAnalyzer.GetHeapStatistics`, `GetObjects`, `GetSyncBlocks`, `GetHeapExceptions`, and
`ThreadAnalyzer.GetThreadExceptions`). The rest return `IEnumerable<T>` already sliced, which is why
`JsonFormatter.PaginationInfo.FromItemsOnly` exists and cannot report `hasMore` honestly. §5 closes
that gap.

## 2. Filtering

### 2.1 Where it sits in the pipeline

```
cached walk  →  filter  →  sort  →  page  →  PagedResult<T>
   (§6.2)        (new)     (existing)  (existing)
```

The ordering is the whole design. Filtering happens **after** the cached computation and **before**
pagination, which yields three properties:

1. **Filters are free.** They never trigger a heap walk. One cached `dumpheap` entry serves every
   filter the user types, the same way it already serves every sort and page
   ([`../CLI_DESIGN.md` §6.2](../CLI_DESIGN.md)).
2. **The cache key must exclude the filter**, exactly as it already excludes
   `limit`/`offset`/`sort`/`order`/`format`. Extend that rule rather than adding a case to it.
3. **`TotalAvailable` is the post-filter count.** "1,284 of 1,502 types match" is the number a user
   needs; the pre-filter total is reported separately (§2.5).

### 2.2 The type

```csharp
namespace DotNetDump.Core.Models;

/// <summary>
/// A filter applied to an analyzer result after computation and before pagination.
/// Every field is optional; set fields are ANDed.
/// </summary>
public sealed class FilterSpec {
    public string? TypeName { get; init; }          // case-insensitive substring
    public string? TypeNameRegex { get; init; }     // anchored nowhere; caller supplies anchors
    public string? Module { get; init; }            // case-insensitive substring of module/assembly name
    public ulong? MinSize { get; init; }            // bytes, inclusive
    public ulong? MaxSize { get; init; }            // bytes, inclusive
    public int? MinCount { get; init; }             // instance count, inclusive
    public int? MaxCount { get; init; }
    public GenerationFilter? Generation { get; init; }   // Gen0 | Gen1 | Gen2 | Loh | Poh | Frozen
    public int? ManagedThreadId { get; init; }
    public uint? OSThreadId { get; init; }
    public bool? HasException { get; init; }
    public string? Text { get; init; }              // case-insensitive substring across the view's text columns

    public static readonly FilterSpec None = new();
    public bool IsEmpty => /* all fields null */;
}
```

`QueryParameters` gains one property:

```csharp
public FilterSpec Filter { get; set; } = FilterSpec.None;
```

That is the entire signature change. Existing callers compile unchanged and get `FilterSpec.None`.

### 2.3 Unsupported fields are an error

Each analyzer method declares which `FilterSpec` fields it honors. Passing one it does not honor
throws `UnsupportedFilterException` — it is **not** silently ignored.

This matters more than it looks. A user filtering `clrmodules` by `size>1gb` and getting the
unfiltered list back has been handed a wrong answer with no indication. In the CLI that surfaces as
exit code `2` (usage error); in the web UI the filter control for an unsupported field is simply not
rendered for that view, so the error is unreachable through the UI and exists to protect direct API
callers.

The matrix is keyed by **analyzer method**, not by view. A view is a presentation choice and can
change; the honored set is a property of the data the method returns, and two views backed by the
same method must not disagree about it. `FilterField` composites (`AnyTypeName`, `Size`, `Count`)
are the declaration form — see `Models/FilterField.cs`.

| Analyzer method | View | Honored | `Size` / `Text` refer to |
| :--- | :--- | :--- | :--- |
| `HeapAnalyzer.GetHeapStatistics` | `dumpheap` | `AnyTypeName`, `Size`, `Count`, `Text` | `TotalSize` (**aggregate**), `TypeName` |
| `HeapAnalyzer.GetObjects` | `listobj` | `AnyTypeName`, `Size`, `Generation`, `Text` | `Size` (**per instance**), `TypeName` |
| `HeapAnalyzer.GetGCHandles` | `gchandles` | `AnyTypeName`, `Text` | — , `TypeName` + `Kind` |
| `HeapAnalyzer.GetSyncBlocks` | `syncblk` | `TypeName`, `ManagedThreadId`, `Text` | — , `TypeName` |
| `HeapAnalyzer.GetHeapExceptions` | `printexception` (heap scan) | `AnyTypeName`, `Text` | — , `TypeName` + `Message` |
| `ThreadAnalyzer.GetThreadExceptions` | `printexception` (in flight) | `AnyTypeName`, `ManagedThreadId`, `OSThreadId`, `Text` | — , `TypeName` + `Message` |
| `ThreadAnalyzer.GetThreads` | `clrthreads` | `ManagedThreadId`, `OSThreadId`, `HasException`, `Text` | — , `ExceptionType` |
| `ThreadAnalyzer.GetThreadStates` | `threadstate` | `ManagedThreadId`, `OSThreadId`, `HasException`, `Text` | — , `ExceptionType` + `StateFlags` |
| `ThreadAnalyzer.GetDetailedStacks` | `dumpstack` | `ManagedThreadId`, `OSThreadId`, `HasException`, `Text` | — , frame `MethodName` |
| `ThreadAnalyzer.GetStackTraceGroups` | `clrstack`, `eestack` | `None` — see below | — |
| `ModuleAnalyzer.GetModules` | `clrmodules` | `Module`, `Size`, `Text` | `Size` (image), `Name` |
| everything else | `eeheap`, `threadpool`, `dumpobj`, `dumpmt`, `dumpmd`, `dumpclass`, `dumpassembly`, `name2ee`, `ip2md`, `info`, `verifyheap` | `None` | — |

`Text` is the case-insensitive substring match across the columns named in the last column. It is
what the web UI's single search box binds to.

Five corrections against the models as they actually stand, found while settling this matrix:

* **`clrstack` is not backed by `GetDetailedStacks`.** An earlier draft of this table listed
  `clrstack` alongside `dumpstack` on that row. It is not: `ClrStackCommand` and `EEStackCommand`
  both call `ThreadAnalyzer.GetStackTraceGroups`, a grouped summary that takes **no
  `QueryParameters` at all** — so there is nowhere to pass a `FilterSpec`, and it cannot even call
  `EnsureSupported`. `clrstack` and `eestack` are therefore not list views in the §3.2 sense and get
  no filter bar. Giving them one means first giving `GetStackTraceGroups` a `QueryParameters`
  overload, which is a Core change nobody has asked for; do not let a Phase 3 view force it in
  through the back door.

* **`Size` does not mean the same thing in two places.** On `dumpheap` it is the aggregate
  `TotalSize` of every instance of a type; on `listobj` it is one object's own size. `size>100mb`
  is a reasonable filter on both and means something different on each. The filter bar must label
  it per view — "total size" and "object size", never just "size".
* **`printexception` is two methods, and they honor different sets.** `GetThreadExceptions` returns
  `ThreadExceptionInfo`, which carries the owning thread. `GetHeapExceptions` returns bare
  `ExceptionDetails` for exception objects found by scanning the heap, and those have **no owning
  thread** — many are garbage from long-completed operations. `ManagedThreadId` is therefore
  unsupported on the heap-scan path rather than merely unpopulated.
* **`Generation` on `listobj` requires a model change.** `HeapObjectItem` has no generation field
  today. Honoring the field means capturing the generation during the walk and bumping the cache
  schema — which 0.3 does anyway, so it costs one field, not one migration. Without it the
  generation selector in [`DESIGN_BRIEF.md` §5](DESIGN_BRIEF.md) has nothing to bind to.
* **`gchandles` could honor `Size` but does not.** `GCHandleInfo.Size` exists, so the filter would
  be free. It is left out because a handle's "size" is the size of its target object, and a user
  filtering handles by size almost certainly means the target — which is what `listobj` is for.

### 2.4 The CLI grammar

The web UI sends structured query parameters (§3.2), but the same filter needs a shell-friendly form
so Phase 0 is testable and the CLI gains the feature independently.

```
--filter <field><op><value>      repeatable; repeats are ANDed
```

| Element | Values |
| :--- | :--- |
| field | `type`, `module`, `size`, `count`, `gen`, `thread`, `osthread`, `exception`, `text` |
| op | `~` contains · `!~` not-contains · `=` equals · `!=` · `>` `>=` `<` `<=` |
| value | bare or quoted string; `/regex/` for `type`; integer; byte size with unit (`512`, `4kb`, `1mb`, `2gb`) |

```bash
dndump dumpheap --filter 'type~Http' --filter 'size>100mb'
dndump listobj  --filter 'type=/^MyApp\.Cache/' --filter 'gen=2'
dndump clrthreads --filter 'exception=true'
```

#### `listobj --type` is not an alias for `--filter 'type~'`

An earlier draft of this document said it was. It is not, and making it one would be a large
performance regression wearing a rename.

`--type` is a **scope**: it narrows the heap walk itself, is part of the cache key, and produces a
cache entry containing only the matching objects (`HeapAnalyzer.GetObjects`). `--filter 'type~'` is
a **filter**: it runs over the full cached object list and is excluded from the cache key, per
§2.1. Aliasing the first to the second would make `listobj --type Foo` walk and cache all 10.2M
objects to return the same few hundred rows.

Both must exist, because each is right for a different consumer:

| | `--type` (scope) | `--filter 'type~'` (filter) |
| :--- | :--- | :--- |
| Cost, cold | Walks once, narrowed | Walks once, everything |
| Cost, warm, changing the value | Another full walk | Free |
| `TotalUnfiltered` | The narrowed walk — not the heap | The whole heap |
| Right for | One-shot CLI and agent use | Filter-as-you-type in the web UI |

The web UI uses the filter exclusively; that is what makes "1,284 of 10,238,441" honest and what
[`IMPLEMENTATION_PLAN.md` Phase 4](IMPLEMENTATION_PLAN.md)'s exit criterion requires. The CLI keeps
`--type` because walking 10.2M objects to print 50 rows is the wrong trade for a process that exits
immediately afterwards.

They compose: given both, the scope applies at walk time and the filter after, ANDed. The CLI must
not silently rewrite one into the other in either direction — help text names the difference.

Parsing lives in the CLI (`FilterExpressionParser`); the parsed `FilterSpec` is what crosses into
Core. The web host never parses this grammar — it binds query parameters directly.

### 2.5 Reporting the filter honestly

`PagedResult<T>` gains one field:

```csharp
/// <summary>Rows before <see cref="FilterSpec"/> was applied. Equals TotalAvailable when unfiltered.</summary>
public int TotalUnfiltered { get; }
```

Without it the UI can say "1,284 rows" but not "1,284 of 1,502" — and cannot distinguish "this dump
has few types" from "your filter is too narrow". It is one integer and it is the difference between
a filter box that guides and one that misleads.

## 3. View contracts

### 3.1 One view per CLI command

The web interface adds no analysis. Every view maps to exactly one existing command, so the command
surface in [`../CLI_DESIGN.md` §4](../CLI_DESIGN.md) is the view inventory and needs no separate
list. Views are grouped for navigation as: **Overview** (`info`), **Heap** (`dumpheap`, `listobj`,
`eeheap`, `gchandles`, `verifyheap`), **Threads** (`clrthreads`, `threadstate`, `clrstack`,
`dumpstack`, `eestack`, `syncblk`, `threadpool`), **Exceptions** (`printexception`), **Modules**
(`clrmodules`, `dumpmodule`, `dumpassembly`), **Metadata** (`dumpmt`, `dumpmd`, `dumpclass`,
`name2ee`, `ip2md`).

### 3.2 Request shape

Every list view accepts the same query string, so one filter/sort/page component serves all of them:

```
/views/dumpheap?type=Http&minSize=1048576&sort=totalSize&order=desc&offset=100&limit=50
```

| Parameter | Maps to |
| :--- | :--- |
| `type`, `typeRegex`, `module`, `text` | `FilterSpec` string fields |
| `minSize`, `maxSize`, `minCount`, `maxCount` | `FilterSpec` range fields |
| `gen`, `thread`, `osthread`, `hasException` | `FilterSpec` scalar fields |
| `sort`, `order` | `QueryParameters.SortBy`, `SortDirection` |
| `offset`, `limit` | `QueryParameters.Offset`, `Limit` |

The query string **is** the view state. It is what `hx-push-url` writes to the address bar, which
makes every view bookmarkable, shareable and back-button-correct for free — and identical between
the HTML route and the JSON route (`SERVER.md` §2).

### 3.3 Response envelope

The JSON route reuses `JsonFormatter` unchanged, with the envelope extended by the two new counts:

```json
{
  "data": [ … ],
  "pagination": { "total": 1284, "totalUnfiltered": 1502, "offset": 100, "limit": 50, "hasMore": true },
  "state": { "cached": true, "computedAt": "2026-07-28T09:12:04Z", "truncated": false }
}
```

`state` is new and is what makes the UI honest: `cached` drives the "instant vs. computing" affordance,
`truncated` surfaces budget exhaustion (§4.3) rather than letting an incomplete result read as complete.

## 4. Trees

Four trees, four different backend shapes, one wire contract.

### 4.1 The node type

```csharp
namespace DotNetDump.Core.Models;

public sealed class TreeNode {
    /// <summary>Opaque, URL-safe, and self-describing: it encodes everything needed to expand this node.</summary>
    public required string Id { get; init; }

    public required string Label { get; init; }        // primary text — type name, frame, namespace segment
    public string? Detail { get; init; }               // secondary text — size, count, address
    public required TreeNodeKind Kind { get; init; }   // Namespace | Type | Object | Field | Thread | Frame | Root | More | Cycle
    public required bool HasChildren { get; init; }
    public int? ChildCount { get; init; }              // null when unknown without computing
    public ulong? Address { get; init; }               // set where the node denotes a heap object
    public IReadOnlyList<TreeBadge> Badges { get; init; } = [];
}

public sealed record TreeBadge(string Label, TreeBadgeTone Tone);  // Neutral | Info | Warn | Danger
```

`Id` is opaque to the client and parsed only by the server. It encodes the tree kind, the path taken
to reach the node, and any state the expansion needs (§4.5). The client never constructs one; it only
echoes back what it was given.

### 4.2 Namespace / assembly rollup — `heap`

**Source.** The cached `PagedResult<HeapStatItem>` from `HeapAnalyzer.GetHeapStatistics`, with no
additional dump access whatsoever.

**Shape.** Split each `TypeName` on `.` and build a prefix tree; sum `Count` and `TotalSize` up the
tree. `System.Collections.Generic.Dictionary<TKey,TValue>` contributes to `System`,
`System.Collections`, `System.Collections.Generic`, and a leaf.

Generic arity is part of the leaf, never a namespace level — split on `.` only outside `<>`, or the
tree grows spurious levels from generic arguments.

**Fan-out cap.** Children are the top 50 by rolled-up size, plus a synthetic `Kind = More` node
("312 more, 4.1 MB") that expands to the next 50. Without this, `System` alone produces hundreds of
children.

**Why it is first.** It is pure grouping over data the cache already holds. It is the cheapest of the
four to build, it is instant on a warm cache, and it delivers the "see heap composition at a glance"
capability from [`../CLI_DESIGN.md` §10.5](../CLI_DESIGN.md).

### 4.3 gcroot retention paths — `gcroot/{address}`

**Source.** `HeapAnalyzer.GetGCRoots(address, parameters, maxPaths, maxNodesVisited)` returning
`GCRootSearchInfo`.

**Shape.** `GCRootSearchInfo.Paths` is a list of `GCRootPathInfo`, each an ordered
`List<GCRootPathNode>` running root-first to target-last. Rendered as a list of chains they repeat
their shared prefixes; merge them into a trie keyed on `GCRootPathNode.Address` so common ancestry
collapses into one branch that forks where the paths diverge.

Root nodes carry badges from `GCRootPathInfo`: `RootKind`, `RootName`, pinned, interior, and the
owning thread when `ManagedThreadId` is set.

**Truncation is a first-class node, not a footnote.** `GCRootSearchInfo.Truncated` means the search
exhausted its node budget rather than proving anything. An empty `Paths` with `Truncated = true` must
never render as "unrooted, eligible for collection" — that is the exact defect
[`../GCROOT_TRUNCATION.md`](../GCROOT_TRUNCATION.md) exists to prevent. The tree renders a `Danger`
banner stating the budget and offering a re-run at a higher `maxNodes` (`0` = unlimited), and
`state.truncated` in the envelope carries the same fact to API callers.

**Fully computed up front.** Unlike the other three, this tree arrives whole from one analyzer call;
lazy expansion is a rendering choice, not a fetching one.

### 4.4 Thread → frames — `threads`

**Source.** `ThreadAnalyzer.GetThreads` for the roots; `ThreadAnalyzer.GetDetailedStacks(parameters,
maxFrames)` for the children.

**Shape.** Two levels: threads as roots (labelled by managed thread id, badged with OS thread id,
alive/dead, and exception type when `ThreadInfo.ExceptionType` is set), frames as children.

**Locals are deferred, and may never arrive.** ClrMD does not expose frame locals in a form that
survives without matching PDBs — `ClrStackFrame` gives the method and IL offset, and mapping that to
named locals needs symbol data the offline-first design does not fetch
(`AGENTS.md`, "Symbol Resolution and DAC"). Specced as an optional third level, gated on a
measurement against a real dump in Phase 5. If it does not work, the tree is two levels and that is
a complete feature — do not block on it.

### 4.5 Object reference navigator — `object/{address}`

**Source.** `HeapAnalyzer.GetObjectDetails(address)` → `ObjectDetails.Fields`, filtered to
`IsReference == true && Address != 0`.

**Shape.** Each qualifying `ObjectField` becomes a child node: `Label` = field name, `Detail` = field
type + address, `Address` = the referent. Expanding it calls `GetObjectDetails` on the referent. Each
expansion is one cheap single-object read, never a walk — this tree stays fast on a cold cache, which
none of the others do.

**Cycles must be handled, and object graphs are full of them.** The node `Id` carries the addresses
already visited on the path from the root. When an expansion would revisit one, the child is emitted
as `Kind = Cycle` with `HasChildren = false` and a badge naming the ancestor it points back to.
Without this the tree is infinitely deep and a user holding the expand key will hang the server.

**Depth cap.** 64 levels, after which nodes render as `HasChildren = false` with a `Warn` badge.
A practical guard against pathological linked lists, not a semantic limit.

**History** is the breadcrumb: the node `Id` already encodes the path, so the address bar plus the
back button provide navigation history with no client-side state machine.

## 5. Supporting changes in Core

Small, independently useful, all Phase 0.

| Change | Why |
| :--- | :--- |
| `PagedResult<T>` on the remaining collection methods — `GetGCHandles`, `GetThreads`, `GetDetailedStacks`, `GetThreadStates`, `VerifyHeap` | They currently return pre-sliced `IEnumerable<T>`, so `hasMore` cannot be reported (see the `FromItemsOnly` remarks in `Formatting/JsonFormatter.cs`). Infinite scroll needs to know whether more rows exist. |
| Move `DumpResolver` and `SessionFile` from `DotNetDump.Cli` to `DotNetDump.Core` | The web host needs the identical `--dump` → `DNDUMP_PATH` → `.dndump/session.json` precedence. Two implementations of that chain would drift, and the drift would be silent. Both are file IO and JSON; neither adds a dependency to Core. |
| `IProgress<WalkProgress>` on the five walk-scale analyzer methods | Cold walks take seconds. Without progress the UI can only show a spinner, which [`../CLI_DESIGN.md` §10.4](../CLI_DESIGN.md) explicitly rules out as dishonest. `WalkProgress` carries objects walked and bytes seen; the CLI ignores it. |
| `SchemaVersion` bump in the cache key | `FilterSpec` does not change any cached payload shape, but `TotalUnfiltered` on `PagedResult<T>` does. |

## 6. What this contract deliberately does not add

* **No new analysis.** Every view and every tree is a rendering of an existing analyzer result.
* **No retained size, no dominators, no reverse edges.** Those need the CSR graph
  ([`../CLI_DESIGN.md` §11.2](../CLI_DESIGN.md)) and are out of scope.
* **No cross-dump comparison.** One dump per process.
* **No filter pushdown into the walk.** Filtering after the cached result is what makes filters free;
  pushing predicates into the ClrMD enumeration would make each distinct filter its own cache entry
  and its own heap walk. That trade is only worth revisiting if a dump appears whose *unfiltered*
  result does not fit in memory.
