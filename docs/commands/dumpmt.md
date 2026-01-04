# dumpmt

Displays details about method tables (`MethodTable`).

## Usage
```
dumpmt <address>
```

## ClrMD Implementation
Lookup type by MethodTable using `ClrRuntime.GetTypeByMethodTable()`.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
ulong mt = 0x7FF...;

ClrType type = runtime.GetTypeByMethodTable(mt);
if (type != null)
{
    Console.WriteLine($"Type: {type.Name}");
    Console.WriteLine($"Contains {type.Methods.Count()} methods");
}
```

**Link:** [ClrType.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrType.cs)