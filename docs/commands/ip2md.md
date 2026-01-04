# ip2md

Displays the `MethodDesc` structure at a specified address (Instruction Pointer).

## Usage
```
ip2md <address>
```

## ClrMD Implementation
Use `ClrRuntime.GetMethodByInstructionPointer()`.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)
ulong ip = 0x7FF...;

ClrMethod method = runtime.GetMethodByInstructionPointer(ip);
if (method != null)
{
    Console.WriteLine($"Method: {method.Signature}");
    Console.WriteLine($"MethodDesc: {method.MethodDesc:X}");
}
```

**Link:** [ClrRuntime.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrRuntime.cs)