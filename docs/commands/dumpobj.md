# dumpobj

Displays details about a managed object.

## Usage
```
dumpobj <address>
```

## Example
```
> dumpobj 000001D0F90F34E0
```

## ClrMD Implementation
Retrieve the `ClrObject` or its type and iterate through its fields.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
ulong address = 0x12345678; // Target address

ClrObject obj = runtime.Heap.GetObject(address);
if (!obj.IsNull)
{
    Console.WriteLine($"Type: {obj.Type.Name}");
    foreach (ClrField field in obj.Type.Fields)
    {
         // Read field values...
         Console.WriteLine($"Field: {field.Name}");
    }
}
```

**Link:** [ClrObject.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrObject.cs)