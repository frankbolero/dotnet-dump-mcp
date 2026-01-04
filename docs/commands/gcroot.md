# gcroot

Displays information about references (or roots) to a specified object. It helps in identifying what is keeping an object alive in the managed heap.

## Usage
```
gcroot <address>
```

## Example
```
> gcroot 000001D0F90F34E0
```

## ClrMD Implementation
Use `GCRoot` helper class or manually walk the heap references. Modern ClrMD provides `EnumerateGCRoots`.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
ulong targetAddress = 0x12345678;

// Note: GCRooting can be complex. 
// You often need to build a reference graph or use the GCRoot class if available in your version.
// A simple approach is finding roots pointing to it:
foreach (ClrRoot root in runtime.Heap.EnumerateGCRoots())
{
    if (root.Object == targetAddress) 
    {
       Console.WriteLine($"Root found: {root.Kind} at {root.Address:X}");
    }
}
```

**Link:** [GCRoot.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/GCRoot.cs)