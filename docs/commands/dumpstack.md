# dumpstack

Helps examine thread call stacks. While `clrstack` is more commonly used for managed call stacks, `dumpstack` can also be used to inspect stack frames, including native ones.

## Usage
```
dumpstack [options]
```

## Example
```
> dumpstack
```

## ClrMD Implementation
ClrMD primarily focuses on managed stack traces via `ClrThread.EnumerateStackTrace()`. For purely native stacks, ClrMD relies on the underlying `DataTarget` and platform-specific debugging APIs, but the core convenience method is for managed frames.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

foreach (ClrThread thread in runtime.Threads)
{
    foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
    {
         // frame.Kind can distinguish between ManagedMethod, Internal, etc.
         Console.WriteLine($"{frame.Kind} - {frame}");
    }
}
```

**Link:** [ClrStackFrame.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrStackFrame.cs)