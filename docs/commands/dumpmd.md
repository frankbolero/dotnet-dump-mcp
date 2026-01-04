# dumpmd

Displays details about method descriptors (`MethodDesc`).

## Usage
```
dumpmd <address>
```

## ClrMD Implementation
If you have a MethodDesc address, currently ClrMD provides less direct "lookup by MethodDesc" than lookup by IP or Type. However, `ClrMethod` wraps this concept.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
// Often obtained via IP or Type.Methods
foreach (ClrModule module in runtime.EnumerateModules())
{
    foreach (ClrType type in module.EnumerateTypes())
    {
         foreach (ClrMethod method in type.Methods)
         {
             // method.MethodDesc is the handle
             Console.WriteLine($"{method.MethodDesc:X} - {method.Name}");
         }
    }
}
```

**Link:** [ClrMethod.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrMethod.cs)