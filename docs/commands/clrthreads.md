# clrthreads

Lists all managed threads in the process.

## Usage
```
clrthreads
```

## ClrMD Implementation
You can enumerate all managed threads using the `ClrRuntime.Threads` property.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrThread thread in runtime.Threads)
{
    Console.WriteLine($"OS Thread ID: {thread.OSThreadId:X}, Managed ID: {thread.ManagedThreadId}, Alive: {thread.IsAlive}");
}
```

**Link:** [ClrRuntime.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrRuntime.cs)