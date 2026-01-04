# Documentation for the output from the dotnet dump analyze commands

While this project uses ClrMD (nuget package Microsoft.Diagnostics.Runtime), we have made an effort to show how the command output looks when using the standard `dotnet dump analyze` CLI command.

## Command Reference

### Stack Analysis
*   [`clrstack`](commands/clrstack.md): Displays managed call stacks for all threads or a specific thread.
*   [`eestack`](commands/eestack.md): Examines thread call stacks.
*   [`dumpstack`](commands/dumpstack.md): Helps examine thread call stacks.

### Heap Analysis
*   [`dumpheap`](commands/dumpheap.md): Analyzes managed heap objects, identifies memory leaks, and traces object references.
*   [`gcroot`](commands/gcroot.md): Displays information about references (or roots) to a specified object.
*   [`verifyheap`](commands/verifyheap.md): Verifies the integrity of the managed heap.
*   [`eeheap`](commands/eeheap.md): Displays information about the managed heap.
*   [`dumpobj`](commands/dumpobj.md): Displays details about a managed object.
*   [`gchandles`](commands/gchandles.md): Displays statistics about garbage collector handles.
*   [`syncblk`](commands/syncblk.md): Displays information about synchronization blocks.

### Thread Analysis
*   [`clrthreads`](commands/clrthreads.md): Lists all managed threads in the process.
*   [`threadpool`](commands/threadpool.md): Investigates thread pool usage.
*   [`threadstate`](commands/threadstate.md): Shows the state of each thread.

### Module and Assembly Analysis
*   [`clrmodules`](commands/clrmodules.md): Lists the managed modules in the process.
*   [`dumpmodule`](commands/dumpmodule.md): Examines loaded modules.
*   [`dumpassembly`](commands/dumpassembly.md): Examines loaded assemblies.
*   [`name2ee`](commands/name2ee.md): Displays the `MethodTable` and `MethodDesc` structures for a specified method.
*   [`ip2md`](commands/ip2md.md): Displays the `MethodDesc` structure at a specified address.

### Metadata Analysis
*   [`dumpclass`](commands/dumpclass.md): Digs into .NET type metadata.
*   [`dumpmd`](commands/dumpmd.md): Displays details about method descriptors.
*   [`dumpmt`](commands/dumpmt.md): Displays details about method tables.

### Exception Analysis
*   [`pe`](commands/printexception.md) / [`printexception`](commands/printexception.md): Helps enumerate and inspect exceptions that have occurred.

### Other Useful Commands
*   [`help`](commands/help.md): Lists available commands or provides detailed help on a specific command.
*   `quit` or `exit`: Exits the interactive session.
*   [`sos`](commands/sos.md): Loads the SOS debugging extension for detailed analysis.