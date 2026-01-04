# gchandles

Displays statistics about garbage collector handles.

## Usage
```
gchandles
```

## ClrMD Implementation
Enumerate handles via `ClrRuntime.EnumerateHandles()`.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrHandle handle in runtime.EnumerateHandles())
{
    Console.WriteLine($"Handle: {handle.Address:X}, Object: {handle.Object:X}, Type: {handle.HandleKind}");
}
```

**Link:** [ClrRuntime.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrRuntime.cs)