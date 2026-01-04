# eestack

Examines thread call stacks. Displays the merged threads stack, similar to the "Parallel Stacks" panel in Visual Studio.

## Usage
```
eestack
```

## Example
```
> eestack
```

## ClrMD Implementation
`eestack` essentially aggregates the output of `clrstack`. You can implement this by collecting stack traces from `ClrThread.EnumerateStackTrace()` and grouping them.

```csharp
using Microsoft.Diagnostics.Runtime;

// ... (Setup DataTarget and ClrRuntime)

// Conceptually:
var stacks = runtime.Threads.Select(t => t.EnumerateStackTrace().ToList());
// Logic to group and merge common stack frames would follow here.
```

**Link:** [ClrThread.cs](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrThread.cs)