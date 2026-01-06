#!/bin/bash
set -e

# Optional: DUMP_PATH can be passed as env var or first arg
DUMP_FILE=${DUMP_PATH:-$1}

if [ -n "$DUMP_FILE" ]; then
    if [ -f "$DUMP_FILE" ]; then
        echo "Pre-initializing environment for $DUMP_FILE..." >&2
        # Run dotnet-symbol to fetch DAC
        dotnet-symbol --dac-only "$DUMP_FILE" >&2 || echo "Warning: dotnet-symbol failed to fetch DAC." >&2
        export DUMP_PATH="$DUMP_FILE"
    else
        echo "Warning: Specified DUMP_PATH '$DUMP_FILE' not found. Starting server without pre-load." >&2
        unset DUMP_PATH
    fi
fi

# Start the MCP Server
exec dotnet DotNetDump.Server.dll