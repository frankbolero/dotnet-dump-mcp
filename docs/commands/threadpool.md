# threadpool

Investigates thread pool usage.

## Usage
```
threadpool
```

## ClrMD Implementation
Access thread pool details via `ClrRuntime.ThreadPool`.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

ClrThreadPool pool = runtime.ThreadPool;
Console.WriteLine($"Total Threads: {pool.TotalThreads}");
Console.WriteLine($"Idle Threads: {pool.IdleThreads}");
Console.WriteLine($"Running Threads: {pool.RunningThreads}");
```

**Link:** [ClrThreadPool.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrThreadPool.cs)