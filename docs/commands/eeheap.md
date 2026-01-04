# eeheap

Displays information about the managed heap, including memory usage for each generation (Gen 0, Gen 1, Gen 2, and Large Object Heap).

## Usage
```
eeheap [-gc] [-loader]
```

## Example
```
> eeheap -gc
```

## ClrMD Implementation
Inspect `ClrHeap.Segments` to see how the heap is laid out in memory.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrSegment segment in runtime.Heap.Segments)
{
    Console.WriteLine($"Segment: {segment.Start:X} - {segment.End:X}, Gen: {segment.Generation}, Size: {segment.Length}");
}
```

**Link:** [ClrSegment.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrSegment.cs)