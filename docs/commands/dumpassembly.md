# dumpassembly

Examines loaded assemblies.

## Usage
```
dumpassembly <address>
```

## ClrMD Implementation
ClrMD treats modules as the primary unit. Use `ClrModule` to access assembly details.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
// Assuming address is related to a module or assembly
ulong address = 0x7FF...; 

// Find module covering this address or containing this assembly
ClrModule module = runtime.EnumerateModules().FirstOrDefault(m => m.AssemblyId == address || m.ImageBase == address);
if (module != null)
{
    Console.WriteLine($"Assembly: {module.AssemblyName}");
}
```

**Link:** [ClrModule.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrModule.cs)