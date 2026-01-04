# name2ee

Displays the `MethodTable` and `MethodDesc` structures for a specified method or type name.

## Usage
```
name2ee <module_name> <type_name>
```

## Example
```
> name2ee System.Private.CoreLib!System.String
```

## ClrMD Implementation
Locate the module and then the type by name.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
string moduleName = "System.Private.CoreLib";
string typeName = "System.String";

ClrModule module = runtime.EnumerateModules().FirstOrDefault(m => m.Name.Contains(moduleName));
if (module != null)
{
    ClrType type = module.GetTypeByName(typeName);
    if (type != null)
    {
        Console.WriteLine($"MethodTable: {type.MethodTable:X}");
    }
}
```

**Link:** [ClrModule.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrModule.cs)