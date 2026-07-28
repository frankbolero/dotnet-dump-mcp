#!/bin/bash
set -e

# Optional: DUMP_PATH can be passed as env var, matching the server image's own entrypoint.sh
# convention (so a `docker run` one-liner for either image looks the same).
DUMP_FILE=${DUMP_PATH:-}

# Where dotnet-symbol persists fetched DAC/DBI files across container runs. Mount a volume here
# (`-v dndump-symcache:/symcache`) so the fetch is a no-op on every run/exec after the first.
# Overridable via DOTNETDUMP_SYMBOL_CACHE for parity with Core's own opt-in symbol-server cache
# variable (see DumpContext.SymbolCacheVariable); the Dockerfile sets that variable to /symcache
# by default for this image.
SYMBOL_CACHE_DIR=${DOTNETDUMP_SYMBOL_CACHE:-/symcache}

if [ -n "$DUMP_FILE" ]; then
    if [ -f "$DUMP_FILE" ]; then
        echo "Pre-initializing environment for $DUMP_FILE..." >&2
        # Run dotnet-symbol to fetch the DAC. NOTE: the installed dotnet-symbol no longer has a
        # --dac-only flag (verified against its real --help output, not memory) -- the closest
        # equivalent is --debugging, which also pulls DBI and SOS; a SOS lookup miss is expected
        # and harmless. --cache-directory persists downloads across runs/execs of this container.
        # dotnet-symbol writes the fetched DAC next to the dump itself (not into
        # --cache-directory, which only caches build-id lookups), always as libmscordaccore.so
        # since this image -- and therefore the DAC it fetches -- is always Linux regardless of
        # host OS/CPU.
        dotnet-symbol --debugging --cache-directory "$SYMBOL_CACHE_DIR" "$DUMP_FILE" >&2 || echo "Warning: dotnet-symbol failed to fetch DAC." >&2

        # ClrMD's no-argument DAC resolution does NOT auto-discover a manually-fetched DAC sitting
        # next to the dump -- it must be told the exact path, or dump loading fails offline with
        # "Could not find matching DAC for this runtime... symbol server download of the DAC is
        # disabled for this platform." So resolve it explicitly here rather than leaving that to
        # every later `dndump use`/`--dump` invocation.
        DAC_CANDIDATE="$(dirname "$DUMP_FILE")/libmscordaccore.so"
        if [ -f "$DAC_CANDIDATE" ]; then
            dndump use "$DUMP_FILE" --dac "$DAC_CANDIDATE" >&2 || echo "Warning: dndump use failed to validate $DUMP_FILE." >&2
        else
            dndump use "$DUMP_FILE" >&2 || echo "Warning: dndump use failed to validate $DUMP_FILE." >&2
        fi
    else
        echo "Warning: Specified DUMP_PATH '$DUMP_FILE' not found." >&2
    fi
fi

# Run whatever dndump command was passed as the container command, e.g.:
#   docker run --rm -v /dumps:/dumps -e DUMP_PATH=/dumps/x.core dndump-cli dumpheap --top 20
# Default to `info` so a bare `docker run` against a pre-loaded dump still shows something useful.
if [ "$#" -eq 0 ]; then
    exec dndump info
else
    exec dndump "$@"
fi
