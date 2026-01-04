# clrstack

Displays managed call stacks for all threads or a specific thread.

## Usage
```
clrstack [options]
```

## Switches
*   `-a`: Shows arguments to the managed function.
*   `-l`: Shows information on local variables in a frame.
*   `-p`: Shows arguments to the managed function.
*   `-n`: Disables line numbers in the output.
*   `-f`: Displays native frames intermixing them with managed frames (may not work in all `dotnet-dump` contexts).
*   `-r`: Dumps the registers for each stack frame.
*   `-all`: Dumps all managed threads' stacks.

## Example Output
See [clrstack-example.md](../clrstack-example.md)

## ClrMD Implementation
To implement similar functionality using ClrMD, you can iterate over `ClrRuntime.Threads` and call `EnumerateStackTrace()` on each thread.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrThread thread in runtime.Threads)
{
    Console.WriteLine($"Thread {thread.OSThreadId:X}:");
    foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
    {
        Console.WriteLine($"  {frame}");
    }
}
```

**Link:** [ClrThread.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrThread.cs)