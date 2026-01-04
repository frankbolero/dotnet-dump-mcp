# Example Output for `clrstack`

The `clrstack` command displays the managed call stack for each managed thread in the process.

## Command Output Example

```text
> clrstack
OS Thread Id: 0x1234 (0)
        Child SP               IP Call Site
00007FFC12345678 00007FFB98765432 ConsoleApp.Program.MethodC(Int32)
00007FFC123456A0 00007FFB98765410 ConsoleApp.Program.MethodB(Int32)
00007FFC123456C8 00007FFB987653F0 ConsoleApp.Program.MethodA()
00007FFC123456F0 00007FFB987653D0 ConsoleApp.Program.Main(System.String[])
00007FFC12345718 00007FFB987653B0 [GCFrame: 00007FFC12345718]

OS Thread Id: 0x5678 (1)
        Child SP               IP Call Site
00007FFC87654321 00007FFB12345678 System.Threading.Thread.Sleep(Int32)
00007FFC87654349 00007FFB12345650 ConsoleApp.Worker.DoWork()
00007FFC87654371 00007FFB12345630 System.Threading.ThreadHelper.ThreadStart_Context(System.Object)
00007FFC87654399 00007FFB12345610 System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object, Boolean)
00007FFC876543C1 00007FFB123455F0 System.Threading.ThreadHelper.ThreadStart()
00007FFC876543E9 00007FFB123455D0 [GCFrame: 00007FFC876543E9]
```

## Field Descriptions

- **OS Thread Id**: Displays the operating system thread ID in hexadecimal, followed by the managed thread ID in parentheses.
- **Child SP**: The stack pointer for the current frame.
- **IP**: The instruction pointer, indicating the address of the next instruction to be executed.
- **Call Site**: The fully qualified name of the method being executed at that stack frame, including parameters.
- **[GCFrame]**: Special frames used by the runtime to track pointers for the garbage collector.
