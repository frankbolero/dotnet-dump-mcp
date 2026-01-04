# dumpmodule

Examines loaded modules.

## Usage
```
dumpmodule <address>
```

## ClrMD Implementation
Find the module by address and inspect its properties.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
ulong moduleAddress = 0x7FF...;

ClrModule module = runtime.EnumerateModules().FirstOrDefault(m => m.ImageBase == moduleAddress);
if (module != null)
{
    Console.WriteLine($"Module: {module.Name}");
    Console.WriteLine($"AssemblyId: {module.AssemblyId:X}");
    Console.WriteLine($"MetadataAddress: {module.MetadataAddress:X}");
}
```

**Link:** [ClrModule.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrModule.cs)