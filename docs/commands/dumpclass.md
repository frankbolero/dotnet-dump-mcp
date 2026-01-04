# dumpclass

Digs into .NET type metadata. Displays information about an `EEClass` structure.

## Usage
```
dumpclass <address>
```

## ClrMD Implementation
`EEClass` is less exposed in public ClrMD APIs than `MethodTable` (`ClrType`). Usually inspecting `ClrType` gives the necessary info.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
ulong mt = 0x7FF...; // Assuming input is MethodTable, or we find it via object

ClrType type = runtime.GetTypeByMethodTable(mt);
if (type != null)
{
    Console.WriteLine($"Type: {type.Name}");
    Console.WriteLine($"BaseSize: {type.BaseSize}");
    // EEClass address might be internal or accessed via specific ClrMD versions/helpers
}
```

**Link:** [ClrType.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrType.cs)