---
name: dndump
description: Analyze a .NET memory dump (core file, .dmp) with the dndump CLI from the shell. Use whenever the task involves investigating a .NET/CLR crash dump, OOM, memory leak, hang, deadlock, or unhandled exception, or when a dump file path or dndump/SOS-style command (dumpheap, gcroot, clrstack, syncblk, pe, ...) is mentioned. Covers dump selection, the command surface, triage workflows for OOM/leak, deadlock/hang, and unhandled-exception investigations, and the Docker fallback for when a local dndump install can't open the dump (architecture mismatch, missing install, missing DAC).
---

# dndump — .NET dump analysis CLI

`dndump` is a per-invocation CLI over ClrMD (`docs/CLI_DESIGN.md`). It replaces the MCP server for
any environment with shell access: each command starts, prints results, and exits — no persistent
process, no tool-manifest context cost. Prefer it over the MCP server whenever a shell is available.

**Filter in the shell, not in your head.** A `dumpheap` on a real dump can be thousands of rows.
Never dump a full unfiltered result into context to eyeball it — use `--format json`/`tsv` with
`jq`/`grep`/`awk`, or `--limit`, so only the rows you actually need cost tokens. See "Piping" below.

## Selecting a dump

```bash
dndump use /path/to/dump.core   # writes .dndump/session.json; validates the dump opens
dndump info                     # runtime, arch, DAC match, heap/segment/thread counts — always run this first
```

After `use`, every later command in the same directory tree needs no `--dump`. Resolution order:
`--dump <path>` flag → `DNDUMP_PATH` env var → `.dndump/session.json` (searched upward). Each shell
invocation is a fresh process with no inherited env, which is exactly why `use` exists — set it
once, not per command.

If `dndump` isn't on `PATH` (not installed as a global tool), fall back to
`dotnet run --project src/DotNetDump.Cli/DotNetDump.Cli.csproj --framework net9.0 -- <args>` — but
if that also fails to load the dump, see "Docker fallback" below before concluding the dump is
unreadable.

## Global options

| Option | Default | Notes |
| :--- | :--- | :--- |
| `--format md\|json\|tsv` | `md` | `json` is a stable API contract (envelope `{data, pagination}`, camelCase, 16-hex addresses) — safe to `jq` against. `tsv` is header + tab-separated rows for `grep`/`awk`/`cut`. |
| `--limit <n>` / `--offset <n>` | `50` / `0` | Paging on every list command. `dumpheap` also accepts `--top <n>` as a friendlier alias for `--limit` (that command only). |
| `--sort <field>` / `--order asc\|desc` | per command | Valid fields differ per command — run `dndump <command> --help` if unsure. |
| `--quiet` | off | Suppresses the informational header on stderr; doesn't affect stdout data. |
| `--dump <path>`, `--dac <path>` | — | Override dump/DAC resolution for one call. |

**Exit codes are meaningful — check them, don't grep for "Error":** `0` success, `1` analysis error
(bad address, type not found), `2` usage error (bad args), `3` dump/DAC load failure. Results go to
stdout, diagnostics to stderr, so a piped `grep`/`jq` failure and a real command failure are always
distinguishable.

## Command surface

Run `dndump commands` for the live list (the CLI's own `commands` help text lags behind — trust
`dndump --help` and `dndump <command> --help` over it). Condensed reference (see
`docs/CLI_DESIGN.md` §4 for full option/sort-field tables):

**Session** — `use <path>`, `info`, `commands`

**Heap** — `dumpheap` (stats, `--sort TotalSize|Count|TypeName`), `listobj --type <substring>`
(instances, `--sort Address|Size`), `dumpobj <addr>`, `gcroot <addr>` (`--max-paths` default 4,
`--max-nodes` default budgeted / `0` = unlimited but memory-heavy — use `0` when a truncated search
reported "not conclusive"), `eeheap`, `gchandles`, `verifyheap`, `verifyobj <addr>`

**Threads/stacks** — `clrthreads`, `threadstate`, `clrstack --max-frames` (default 20),
`eestack --max-frames` (default 30), `dumpstack --max-frames` (default 100), `threadpool`,
`syncblk --no-thin-locks` (thin locks included by default; excluding them skips a full heap walk)

**Exceptions** — `printexception` / `pe [address]` — omit the address to list exceptions across
threads and the heap (`--no-heap-exceptions` to heap-scan-skip, `--all-threads` to include threads
without one); give an address to inspect a single exception object.

**Modules/metadata** — `clrmodules`, `dumpmodule <addr>`, `dumpassembly <addr>`,
`dumpmt <addr>`, `dumpmd <addr>`, `dumpclass <addr>` (a MethodTable address — ClrMD has no separate
EEClass), `name2ee <module> <type[.method]>`, `ip2md <addr>`

All `<address>` arguments take hex with or without `0x`.

## Triage workflows

These are judgement calls a tool manifest can't express — the sequence matters more than any one
command.

**OOM / memory leak**
```bash
dndump info                                    # confirm heap size, object/segment counts
dndump dumpheap --top 20                       # biggest types by total size
dndump listobj --type MyApp.SuspectType --limit 1   # grab one sample instance's address
dndump gcroot 0x<address>                      # what's retaining it
dndump dumpobj 0x<retaining-collection-addr>   # inspect the actual retaining object's fields
```
If `gcroot` reports the search was truncated rather than conclusive (large heap, default node
budget exhausted), rerun with `--max-nodes 0` before concluding an object is genuinely unrooted —
otherwise "no path found" and "gave up looking" are indistinguishable.

**Deadlock / hang**
```bash
dndump clrthreads              # thread states — look for Blocked/Wait
dndump syncblk                 # which locks are held, by whom, who's waiting
dndump clrstack --max-frames 30   # full stacks to see what each thread is doing
dndump threadstate             # cross-reference lock counts against the blocked thread ids
```

**Unhandled exception**
```bash
dndump pe --limit 20           # exceptions in flight and on the heap
dndump clrstack --max-frames 30   # stack of the faulting thread
dndump dumpobj 0x<exception-address>   # inspect the exception object (message, inner exception, etc.)
```

## Piping — filter before it reaches your context

```bash
# Only the type suspected, out of thousands of rows
dndump dumpheap --format tsv --limit 5000 | grep -i 'HttpClient'

# Everything over 1 GB, machine-parsed
dndump dumpheap --format json --limit 5000 \
  | jq -r '.data[] | select(.totalSize > 1073741824) | "\(.totalSize)\t\(.typeName)"'
```

Default to `--format tsv` or `--format json` plus a shell filter whenever a result could be large
(`dumpheap`, `listobj`, `clrthreads`, `gchandles`, `syncblk`, `pe` with no address) — reach for plain
`md` output only once a result is already known to be small, or after filtering it down.

## Docker fallback — when local dndump can't open the dump

Reach for Docker whenever a native `dndump`/`dotnet run` attempt fails for any of these reasons:

- **Architecture/OS mismatch** — e.g. a Linux dump analyzed on macOS or Windows, or an ARM64 dump
  on an x64 host (or vice versa). ClrMD generally needs a host whose OS/arch matches the dump's.
- **No local .NET SDK/runtime**, or the wrong major version installed for `dotnet run` to work.
- **DAC not found locally** and no reachable local install of the exact matching runtime.

### 1. Build the image

The Dockerfile lives at `src/Dockerfile`, but its `COPY`/paths (`src/DotNetDump.Server/...`,
`src/entrypoint.sh`) assume the build **context** is the repo root — not `src/`. Always build from
the repo root with `-f` pointing at the Dockerfile, keeping `.` as context:

```bash
docker build -f src/Dockerfile -t dotnet-dump-mcp-server .
```

(Building with context `./src/` or from inside `src/` breaks those paths and fails with
`MSB1009: Project file does not exist`.)

### 2. Prefer the persistent-container wrapper over one-shot `docker run`

A one-shot `docker run --rm -i ... dotnet-dump-mcp-server` pays container startup and a DAC fetch
on every single command. Instead, use `scripts/dndump-docker`, which keeps one long-lived container
per analysis session and `docker exec`s into it — same command surface as native `dndump`:

```bash
./scripts/dndump-docker use /dumps/prod-oom.core
./scripts/dndump-docker info
./scripts/dndump-docker dumpheap --top 20
```

It creates/reuses a container named `dndump-session` (override via `DNDUMP_CONTAINER_NAME`), mounts
`DNDUMP_DUMPS_DIR` (default: current directory) at `/dumps`, and persists the DAC cache in a named
volume (`DNDUMP_SYMCACHE_VOLUME`, default `dndump-symcache`) so the DAC fetch only happens once per
dump. Set `DNDUMP_PLATFORM=linux/amd64` (or `linux/arm64`) for cross-architecture runs on Docker
Desktop/Apple Silicon.

On `use`, the wrapper automatically runs `dotnet-symbol` to fetch the matching DAC next to the dump
and passes `--dac` explicitly — this matters because ClrMD's no-argument DAC resolution does **not**
auto-discover a manually-fetched DAC sitting next to the dump; it must be told the path.

### 3. If you're calling `docker exec`/`docker run` directly instead of the wrapper

You'll hit: `Could not find matching DAC for this runtime. Note that symbol server download of the
DAC is disabled for this platform.` Fix it by fetching the DAC yourself and passing `--dac`
explicitly — the parameterless resolution won't find it on its own:

```bash
docker exec <container> dotnet-symbol --debugging --cache-directory /symcache /dumps/<dump-file>
docker exec <container> dndump use /dumps/<dump-file> --dac /dumps/libmscordaccore.so
```

(`libmscordaccore.so` is always the right filename here — the image, and therefore the DAC
`dotnet-symbol` fetches for it, is always Linux, regardless of the host OS/CPU.)

### 4. Benchmarking

`scripts/dndump-benchmark.sh` times the costliest commands (`dumpheap`, `listobj`, `gcroot`,
`syncblk`, `gchandles`, `printexception`) cold (cache cleared) vs. warm (cached) against whatever
dump is currently selected — useful for judging how "safe" a dump is to explore interactively
before running a long triage session against it.
