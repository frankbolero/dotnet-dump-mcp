# Defect: `gcroot` reports objects as unrooted when its search was truncated

**Status: open, not fixed.** Affects `DotNetDump.Core` and therefore both the MCP server and the
planned CLI.

## Summary

`gcroot` searches forward from GC roots with a fixed budget of 2,000,000 visited nodes. When that
budget is exhausted the search stops and the result is indistinguishable from "this object has no
retention path" — and the Markdown output then asserts, without hedging, that the object is unrooted
and eligible for collection.

On a heap larger than ~2M objects that assertion can be flatly false. It is the worst possible
failure mode for this command: `gcroot` exists to answer *"why is this object still alive"*, and a
confident "nothing is holding it" sends the investigation in exactly the wrong direction. It fails
silently, and it fails more often the larger the heap — i.e. precisely on the leak dumps the tool
exists for.

## The defect chain

The root cause is that one `null` carries two opposite meanings.

**1. The budget** — `src/DotNetDump.Core/Utilities/RootPathFinder.cs:33`

```csharp
public const int DefaultMaxNodesVisited = 2_000_000;
```

**2. Exhausting it returns `null`** — `RootPathFinder.cs:109-112`

```csharp
int visited = 0;
while (queue.Count > 0) {
    if (++visited > maxNodesVisited)
        return null;          // "I gave up"
```

**3. Searching the entire graph and finding nothing also returns `null`** — `RootPathFinder.cs:141`

```csharp
    }
    return null;              // "there is genuinely no path"
}
```

`FindOnePath` is declared `RootPath?` (`RootPathFinder.cs:81`), so from here the two outcomes are
structurally indistinguishable. **This is the actual defect; everything below is a consequence.**

**4. The caller collapses them** — `RootPathFinder.cs:67-76`

```csharp
for (int pass = 0; pass < maxPaths; pass++) {
    var found = FindOnePath(target, rootList, successors, banned, maxNodesVisited);
    if (found is null)
        break;                // identical handling for both cases
```

**5. `HeapAnalyzer` has nothing left to propagate** — `src/DotNetDump.Core/Analyzers/HeapAnalyzer.cs:116-121`

`FindPaths` returns `IReadOnlyList<RootPath>`; an empty list is the only signal available, and
`GetGCRoots` passes it through as `IEnumerable<GCRootPathInfo>`.

**6. The formatter turns absence of evidence into a positive claim** — `src/DotNetDump.Core/Formatting/MarkdownFormatter.cs:90-95`

```csharp
if (list.Count == 0) {
    sb.AppendLine($"**No GC root path found to `{Addr(targetAddress)}`.**");
    sb.AppendLine();
    sb.AppendLine("The object is not reachable from any GC root, which means it is unrooted and " +
        "eligible for collection (or was already collected and the address is stale).");
```

This does not merely omit a caveat — it states the opposite conclusion as fact.

## Evidence

Measured against `core_20260727_211646` (see [CLI_IMPLEMENTATION_PLAN.md](CLI_IMPLEMENTATION_PLAN.md)
§0.2): **10,187,201 objects**. A 2,000,000-node budget covers **under 20%** of that graph.

Note also that `maxPaths` defaults to 4 and each pass gets a *fresh* budget
(`RootPathFinder.cs:68`, inside the loop). A later pass bailing after earlier ones succeeded yields a
**partial** result that is presented as complete — so the defect is not limited to the empty case.

## The contract this violates

Microsoft's [SOS documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/sos-debugging-extension)
for `GCRoot`, the command this implements:

> The **GCRoot** command examines the **entire managed heap** and the handle table for handles within
> other objects and handles on the stack. Each stack is then searched for pointers to objects, and
> the finalizer queue is also searched.

SOS is explicit about the one imprecision it does have — *"This command does not determine whether a
stack root is valid or is discarded"* — and silent about any traversal bound, because it has none.

## Fix

Two parts. The first is required for correctness; the second is what makes the bound acceptable.

### Part 1 — Never report a truncated search as a complete one

Change `FindOnePath` to distinguish exhaustion from completion — a small result struct, or an
`out bool budgetExhausted`. Thread it through `FindPaths` → `GetGCRoots` → `GCRootPathInfo` → all
three formatters.

Every `gcroot` result should report the nodes visited and whether the traversal completed. When
truncated, the "no paths" message must say the result is **inconclusive**, not that the object is
unrooted.

This part is worth doing on its own even if nothing else changes.

### Part 2 — Make the budget a setting, including unlimited

The bound should be the caller's choice, not a hard-coded constant.

| Surface | Form |
| :--- | :--- |
| CLI | `--max-nodes <n>` on `gcroot`; **`0` means unlimited** |
| MCP tool | `maxNodes` parameter on `GcRoot` |
| Default | `DNDUMP_GCROOT_MAX_NODES` environment variable, else the built-in default |

Two design decisions to settle when implementing:

- **Per-pass or total?** The budget is currently per-pass, so `--max-paths 4 --max-nodes 2000000`
  permits up to 8M visits. Whichever is chosen, document it — the current behaviour is undocumented
  and surprising.
- **Should the default change?** Keeping 2,000,000 is defensible once truncation is always reported,
  since an unbounded default could hang on a large heap. Scaling the default with object count is the
  alternative. Either way the default must be documented and overridable.

### Unlimited is not free — say so

The budget is not only a time bound, it is implicitly a **memory** bound.
`FindOnePath` keeps `parent`, a `Dictionary<ulong, ulong>` holding every visited node
(`RootPathFinder.cs:89`), so peak memory scales with nodes visited — roughly 40 bytes per entry
including overhead:

| Nodes visited | Approximate peak |
| ---: | ---: |
| 2 M (current default) | ~80 MB |
| 10 M | ~400 MB |
| 100 M | ~4 GB |

Unlimited is a legitimate choice and should be available, but it must be an *informed* one. The help
text and docs should state the cost, and a truncation report should suggest `--max-nodes 0` as the
way to get a conclusive answer rather than leaving the user to guess.

The only way to make the question exact *without* an unbounded forward search is to search backwards
from the target using a reverse-reference index — see [CLI_DESIGN.md](CLI_DESIGN.md) §11.2. That is a
much larger piece of work and is not proposed here.

## Tests

- Budget exhaustion on a graph with a known-reachable target reports truncated, not "no paths".
- A completed search reporting no paths is distinguishable from the above.
- A partial result (some passes succeeded, a later one exhausted) is flagged as partial.
- `maxNodes = 0` completes a search that the default budget truncates.
- Per-pass versus total budget semantics match whatever is documented.

`RootPathFinder` is expressed over an abstract successor function specifically so it can be tested
against a hand-built graph without a dump (`RootPathFinder.cs:26-29`), so all of these are cheap unit
tests. See `src/DotNetDump.Tests/RootPathFinderTests.cs`.

## References

- [SOS debugging extension for .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/sos-debugging-extension) — the `GCRoot` contract
- [CLI_DESIGN.md](CLI_DESIGN.md) §11.2 — reverse-reference index, the exact-answer alternative
- [CLI_DESIGN.md](CLI_DESIGN.md) §11.4 — where this defect is summarised in the design spec
- [commands/gcroot.md](commands/gcroot.md) — command reference
