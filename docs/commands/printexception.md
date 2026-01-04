# printexception (pe)

Prints information about an exception object.

## Usage
```
printexception [<address>]
pe [<address>]
```

## ClrMD Implementation
Inspect `ClrThread.CurrentException` for the active exception on a thread, or cast a `ClrObject` to an exception type to read its message/stack trace.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrThread thread in runtime.Threads)
{
    if (thread.CurrentException != null)
    {
        Console.WriteLine($"Exception on Thread {thread.OSThreadId:X}: {thread.CurrentException.Type.Name}");
        Console.WriteLine($"Message: {thread.CurrentException.Message}");
        
        foreach (ClrStackFrame frame in thread.CurrentException.StackTrace)
        {
             Console.WriteLine($"  at {frame}");
        }
    }
}
```

**Link:** [ClrException.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrException.cs)