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
