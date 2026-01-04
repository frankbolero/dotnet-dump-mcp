# syncblk

Displays information about synchronization blocks (thin locks and `System.Threading.Monitor` instances). Useful for diagnosing deadlocks and contention issues.

## Usage
```
syncblk [-all]
```

## Example
```
> syncblk -all
```

## ClrMD Implementation
Iterate over `ClrHeap.SyncBlocks`.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (var syncBlock in runtime.Heap.SyncBlocks)
{
    Console.WriteLine($"Object: {syncBlock.Object:X}, Thread: {syncBlock.OwningThread?.OSThreadId:X}");
}
```

**Link:** [ClrHeap.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrHeap.cs)