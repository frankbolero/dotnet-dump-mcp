# sos

Loads the SOS debugging extension for detailed analysis.

## Usage
```
sos
```

## ClrMD Implementation
This command is specific to the debugger (WinDbg/dotnet-dump). ClrMD is effectively the "programmatic SOS". There is no direct "load sos" API because ClrMD *is* the library that provides access to the same data structures SOS uses.

**Link:** [Microsoft.Diagnostics.Runtime](https://github.com/microsoft/clrmd)