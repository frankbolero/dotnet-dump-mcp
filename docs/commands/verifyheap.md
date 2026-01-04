# verifyheap

Verifies the integrity of the managed heap. Useful for identifying memory corruption.

## Usage
```
verifyheap
```

## ClrMD Implementation
Use the `ClrHeap.VerifyHeap()` method.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (var corruption in runtime.Heap.VerifyHeap())
{
    Console.WriteLine($"Corruption detected at {corruption.Address:X}: {corruption.Message}");
}
```

**Link:** [ClrHeap.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrHeap.cs)