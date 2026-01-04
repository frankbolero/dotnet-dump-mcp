# threadstate

Shows the state of each thread.

## Usage
```
threadstate
```

## ClrMD Implementation
The `ClrThread` object contains properties describing the thread's state.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrThread thread in runtime.Threads)
{
    Console.WriteLine($"Thread {thread.OSThreadId:X} State: {thread.GCMode} / IsBackground: {thread.IsBackground}");
}
```

**Link:** [ClrThread.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrThread.cs)