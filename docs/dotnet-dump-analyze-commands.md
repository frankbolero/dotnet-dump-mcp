# Documentation for the output from the dotnet dump analyze commands

While this project uses ClrMD (nuget package Microsoft.Diagnostics.Runtime), we have made an effort to show how the command output looks when using the standard `dotnet dump analyze` CLI command.

## Common Commands

### Stack Analysis
*   `clrstack`: Displays managed call stacks for all threads or a specific thread.
*   `eestack`: Examines thread call stacks.
*   `dumpstack`: Helps examine thread call stacks.

### Heap Analysis
*   `dumpheap`: Analyzes managed heap objects, identifies memory leaks, and traces object references.
*   `gcroot`: Displays information about references (or roots) to a specified object.
*   `verifyheap`: Verifies the integrity of the managed heap.
*   `eeheap`: Displays information about the managed heap.
*   `dumpobj`: Displays details about a managed object.
*   `gchandles`: Displays statistics about garbage collector handles.
*   `syncblk`: Displays information about synchronization blocks.

### Thread Analysis
*   `clrthreads`: Lists all managed threads in the process.
*   `threadpool`: Investigates thread pool usage.
*   `threadstate`: Shows the state of each thread.

### Module and Assembly Analysis
*   `clrmodules`: Lists the managed modules in the process.
*   `dumpmodule`: Examines loaded modules.
*   `dumpassembly`: Examines loaded assemblies.
*   `name2ee`: Displays the `MethodTable` and `MethodDesc` structures for a specified method.
*   `ip2md`: Displays the `MethodDesc` structure at a specified address.

### Metadata Analysis
*   `dumpclass`: Digs into .NET type metadata.
*   `dumpmd`: Displays details about method descriptors.
*   `dumpmt`: Displays details about method tables.

### Exception Analysis
*   `pe`: Helps enumerate and inspect exceptions that have occurred.
*   `printexception`: Prints information about an exception object.

### Other Useful Commands
*   `help`: Lists available commands or provides detailed help on a specific command.
*   `quit` or `exit`: Exits the interactive session.
*   `sos`: Loads the SOS debugging extension for detailed analysis.
