#!/bin/bash
set -e

DUMP_FILE=${DUMP_PATH:-$1}

if [ -z "$DUMP_FILE" ]; then
    echo "Error: No dump file specified. Set DUMP_PATH or pass as first argument." >&2
    exit 1
fi

if [ ! -f "$DUMP_FILE" ]; then
    echo "Error: Dump file $DUMP_FILE not found." >&2
    exit 1
fi

echo "Initializing analysis environment for $DUMP_FILE..." >&2

# Run dotnet-symbol to fetch DAC if needed. 
# --dac-only ensures we get the library needed by ClrMD.
# We run it in the background if possible or just before starting.
dotnet-symbol --dac-only "$DUMP_FILE" >&2 || echo "Warning: dotnet-symbol failed to fetch DAC. ClrMD might still find a local one." >&2

# Export the path so the app knows where the dump is
export DUMP_PATH="$DUMP_FILE"

# Start the MCP Server
exec dotnet DotNetDump.Server.dll
