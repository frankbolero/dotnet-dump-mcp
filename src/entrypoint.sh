#!/bin/bash
set -e

# Optional: DUMP_PATH can be passed as env var or first arg
DUMP_FILE=${DUMP_PATH:-$1}

# Where dotnet-symbol persists fetched DAC/DBI files across container runs. Mount a volume here
# (`-v dndump-symcache:/symcache`, docs/CLI_DESIGN.md §8.2) so the fetch is a no-op on every
# run/exec after the first. Overridable via DOTNETDUMP_SYMBOL_CACHE for parity with Core's own
# opt-in symbol-server cache variable (see DumpContext.SymbolCacheVariable); the Dockerfile sets
# that variable to /symcache by default for this image.
SYMBOL_CACHE_DIR=${DOTNETDUMP_SYMBOL_CACHE:-/symcache}

if [ -n "$DUMP_FILE" ]; then
    if [ -f "$DUMP_FILE" ]; then
        echo "Pre-initializing environment for $DUMP_FILE..." >&2
        # Run dotnet-symbol to fetch the DAC. NOTE: the installed dotnet-symbol no longer has a
        # --dac-only flag (verified against its real --help output, not memory) -- the closest
        # equivalent is --debugging, which also pulls DBI and SOS; a SOS lookup miss is expected
        # and harmless. --cache-directory persists downloads across runs/execs of this container.
        dotnet-symbol --debugging --cache-directory "$SYMBOL_CACHE_DIR" "$DUMP_FILE" >&2 || echo "Warning: dotnet-symbol failed to fetch DAC." >&2
        export DUMP_PATH="$DUMP_FILE"
    else
        echo "Warning: Specified DUMP_PATH '$DUMP_FILE' not found. Starting server without pre-load." >&2
        unset DUMP_PATH
    fi
fi

# Start the MCP Server
exec dotnet DotNetDump.Server.dll