#!/bin/bash
# Phase 0 measurement (docs/web/IMPLEMENTATION_PLAN.md, "Measurement (gates Phase 4)").
#
#   ./bench-filter.sh /path/to/dump.core                 # discovery mode: find a ~5% filter
#   ./bench-filter.sh /path/to/dump.core 'type~Http'      # measure mode: cold + warm timings
#
# Uses an isolated DNDUMP_CACHE so your real cache is never touched or cleared.

set -euo pipefail

DUMP="${1:?usage: $0 <dump-path> [filter-expression]}"
FILTER="${2:-}"
RUNS="${RUNS:-15}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DNDUMP="${DNDUMP:-$REPO_ROOT/src/DotNetDump.Cli/bin/Release/net9.0/dndump}"
export DNDUMP_CACHE="${DNDUMP_CACHE:-/tmp/dndump-bench-cache}"

[ -x "$DNDUMP" ] || { echo "Build first: (cd $REPO_ROOT/src && dotnet build DotNetDump.Cli/DotNetDump.Cli.csproj -c Release -f net9.0)"; exit 1; }
[ -f "$DUMP" ]   || { echo "No such dump: $DUMP"; exit 1; }

now_ms() { perl -MTime::HiRes=time -e 'printf "%.1f\n", time*1000'; }

# Median of stdin (one number per line).
median() { sort -n | awk '{a[NR]=$1} END {print (NR%2) ? a[(NR+1)/2] : (a[NR/2]+a[NR/2+1])/2}'; }

run_timed() { # args: extra dndump args...  -> prints elapsed ms
  local t0 t1
  t0=$(now_ms)
  "$DNDUMP" dumpheap --dump "$DUMP" --quiet --format json "$@" >/dev/null
  t1=$(now_ms)
  echo "$t1 - $t0" | bc
}

counts() { # args: extra dndump args... -> prints "total totalUnfiltered"
  "$DNDUMP" dumpheap --dump "$DUMP" --quiet --format json --limit 1 "$@" \
    | jq -r '.pagination | "\(.total) \(.totalUnfiltered)"'
}

echo "cache root : $DNDUMP_CACHE"
echo "dump       : $DUMP"
echo

# --- warm the cache once; everything below this line is a cache hit ------------
# Cleared by default so "cold" is genuinely cold. COLD=0 skips the clear when you
# are re-running discovery and do not want to pay the walk again.
if [ "${COLD:-1}" = "1" ]; then
  rm -rf "$DNDUMP_CACHE"
  echo "Cleared cache for a genuine cold walk (COLD=0 to skip)."
else
  echo "Reusing existing cache (COLD=0); the 'cold' number below is NOT cold."
fi
echo "Warming cache (cold walk, this is the slow one)..."
COLD_MS=$(run_timed --limit 50)
read -r TOTAL UNFILTERED <<<"$(counts)"
echo "cold walk  : ${COLD_MS} ms   (${UNFILTERED} type rows)"
echo

# --- discovery mode -----------------------------------------------------------
if [ -z "$FILTER" ]; then
  echo "Match rates for candidate filters (pick one near 5%):"
  printf '  %-34s %8s %8s\n' 'FILTER' 'ROWS' 'PCT'
  for f in 'type~System' 'type~Collections' 'type~Generic' 'type~Http' 'type~String' \
           'type~Dictionary' 'type~Task' 'type~Async' 'type~[]' 'type~Microsoft' 'type~Byte'; do
    read -r t u <<<"$(counts --filter "$f" 2>/dev/null || echo '0 0')"
    [ "$u" -gt 0 ] || continue
    pct=$(echo "scale=2; 100*$t/$u" | bc)
    printf '  %-34s %8s %7s%%\n' "$f" "$t" "$pct"
  done
  echo
  echo "Re-run with the one closest to 5%:  $0 $DUMP 'type~Xxx'"
  exit 0
fi

# --- measure mode -------------------------------------------------------------
read -r F_TOTAL F_UNFILTERED <<<"$(counts --filter "$FILTER")"
PCT=$(echo "scale=2; 100*$F_TOTAL/$F_UNFILTERED" | bc)
echo "filter     : $FILTER"
echo "matches    : $F_TOTAL of $F_UNFILTERED rows (${PCT}%)"
echo

echo "Timing $RUNS warm runs each..."
for i in $(seq "$RUNS"); do run_timed --limit 50;                     done | median | xargs printf 'warm unfiltered : %s ms (median)\n'
for i in $(seq "$RUNS"); do run_timed --limit 50 --filter "$FILTER";  done | median | xargs printf 'warm filtered   : %s ms (median)\n'

echo
echo "Interpretation:"
echo "  * cold (${COLD_MS} ms) vs warm tells you the cache is working."
echo "  * filtered vs unfiltered warm SHOULD be indistinguishable. A re-walk would"
echo "    cost roughly the cold number, so any delta near that is the failure case."
echo "  * Both warm numbers include ~200-400ms of .NET startup + dump open per"
echo "    process. The filter itself is sub-millisecond and CANNOT be resolved by"
echo "    this method -- see the note in the chat."
