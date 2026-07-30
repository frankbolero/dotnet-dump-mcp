#!/bin/bash
# Phase 2 measurement (docs/web/IMPLEMENTATION_PLAN.md, "Measurement (gates Phase 6)").
#
#   ./bench-serve.sh /path/to/dump.core
#
# Produces the three numbers Phase 6 is measured against:
#
#   1. serve startup to first byte, cold cache
#   2. serve startup to first byte, warm cache
#   3. a single cached dumpheap render
#
# Deliberately mirrors bench-filter.sh: Release build, net9.0, an isolated
# DNDUMP_CACHE so your real cache is never touched, and medians rather than means.
#
# Runs with --no-warm throughout. The startup cache warm is Phase 6.1's subject, and
# leaving it on would mean the "cold" number timed the background warm job racing the
# request through the queue rather than the request path itself. This is the honest
# before-picture that 6.1 has to improve on.

set -euo pipefail

DUMP="${1:?usage: $0 <dump-path>}"
RUNS="${RUNS:-15}"
PORT="${PORT:-5199}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DNDUMP="${DNDUMP:-$REPO_ROOT/src/DotNetDump.Cli/bin/Release/net9.0/dndump}"
export DNDUMP_CACHE="${DNDUMP_CACHE:-/tmp/dndump-serve-bench-cache}"

[ -x "$DNDUMP" ] || { echo "Build first: (cd $REPO_ROOT/src && dotnet build DotNetDump.Cli/DotNetDump.Cli.csproj -c Release -f net9.0)"; exit 1; }
[ -f "$DUMP" ]   || { echo "No such dump: $DUMP"; exit 1; }

now_ms() { perl -MTime::HiRes=time -e 'printf "%.1f\n", time*1000'; }
median()  { sort -n | awk '{a[NR]=$1} END {print (NR%2) ? a[(NR+1)/2] : (a[NR/2]+a[NR/2+1])/2}'; }

SERVER_PID=""
stop_server() {
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  SERVER_PID=""
}
trap stop_server EXIT

# Starts serve and sets T_READY / T_FIRST (ms from process launch).
#
# Sets globals rather than printing them, and must never be called inside a command
# substitution: that runs the function in a subshell, where SERVER_PID is set on a copy
# of the environment and lost. The first version of this script did exactly that, so
# stop_server killed nothing, the second serve failed to bind, and curl was answered by
# the still-running first process -- reporting a 17 ms "cold start" of a 9 GB dump.
# The port guard below is what turns that class of mistake back into a loud failure.
T_READY=0
T_FIRST=0
time_startup() {
  local t0
  if lsof -ti "tcp:$PORT" >/dev/null 2>&1; then
    echo "Port $PORT is already in use; refusing to measure against someone else's server." >&2
    exit 1
  fi

  t0=$(now_ms)
  "$DNDUMP" serve --dump "$DUMP" --no-open --no-warm --quiet --port "$PORT" >/dev/null 2>&1 &
  SERVER_PID=$!

  # curl --retry-connrefused with no delay is what makes this precise: it spins on the
  # refused connection rather than sleeping, so the poll granularity does not land in
  # the number, and once the socket accepts the GET blocks through the cold walk.
  curl -s -o /dev/null --retry-connrefused --retry 500 --retry-delay 0 --retry-max-time 900 \
    "http://127.0.0.1:$PORT/health"
  T_READY=$(echo "$(now_ms) - $t0" | bc)

  curl -s -o /dev/null --max-time 900 "http://127.0.0.1:$PORT/"
  T_FIRST=$(echo "$(now_ms) - $t0" | bc)

  kill -0 "$SERVER_PID" 2>/dev/null || { echo "serve exited during measurement." >&2; exit 1; }
}

# Median wall time of RUNS requests to a path, against the already-running server.
time_requests() {
  local path="$1" i
  for ((i = 0; i < RUNS; i++)); do
    curl -s -o /dev/null -w '%{time_total}\n' "http://127.0.0.1:$PORT$path"
  done | awk '{printf "%.1f\n", $1 * 1000}' | median
}

echo "cache root : $DNDUMP_CACHE"
echo "dump       : $DUMP  ($(du -h "$DUMP" | cut -f1))"
echo "binary     : $DNDUMP"
echo "runs       : $RUNS"
echo

rm -rf "$DNDUMP_CACHE"
echo "=== 1. Cold cache (cleared) ==="
time_startup
ROWS=$(curl -s "http://127.0.0.1:$PORT/api/dumpheap?limit=1" | jq -r '.pagination.totalUnfiltered')
echo "startup to listening   : ${T_READY} ms"
echo "startup to first byte  : ${T_FIRST} ms   (${ROWS} type rows)"
echo

echo "=== 3. Cached render (same warm process) ==="
echo "GET /views/dumpheap                    : $(time_requests '/views/dumpheap') ms"
echo "GET /views/dumpheap  filtered          : $(time_requests '/views/dumpheap?type=Http') ms"
echo "GET /views/dumpheap  filtered + sorted : $(time_requests '/views/dumpheap?type=Http&sort=count&order=asc') ms"
echo "GET /api/dumpheap                      : $(time_requests '/api/dumpheap') ms"
echo "GET /health          (floor)           : $(time_requests '/health') ms"
echo
stop_server

echo "=== 2. Warm cache (same cache directory, fresh process) ==="
time_startup
echo "startup to listening   : ${T_READY} ms"
echo "startup to first byte  : ${T_FIRST} ms"
stop_server
