# clrmodules

Lists the managed modules in the process.

## Usage
```
clrmodules
```

## ClrMD Implementation
Enumerate modules via `ClrRuntime.EnumerateModules()`.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrModule module in runtime.EnumerateModules())
{
    Console.WriteLine($"Module: {module.Name}, Address: {module.ImageBase:X}, Size: {module.Size}");
}
```

**Link:** [ClrRuntime.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrRuntime.cs)