---
name: dndump
description: Analyze a .NET memory dump (core file, .dmp) with the dndump CLI from the shell. Use whenever the task involves investigating a .NET/CLR crash dump, OOM, memory leak, hang, deadlock, or unhandled exception, or when a dump file path or dndump/SOS-style command (dumpheap, gcroot, clrstack, syncblk, pe, ...) is mentioned. Covers dump selection, the command surface, triage workflows for OOM/leak, deadlock/hang, and unhandled-exception investigations, and the Docker fallback for when a local dndump install cannot open the dump (architecture mismatch, missing install, missing DAC).
user-invocable: false
---

# dndump — .NET dump analysis CLI

`dndump` is a per-invocation CLI over ClrMD. Each command starts, prints results, and exits — no
persistent process, no tool-manifest context cost. Prefer it over any MCP-server equivalent
whenever a shell is available.

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

If `dndump` fails to load the dump locally (wrong OS/architecture for the dump, no local .NET
runtime matching it, or no matching DAC available), see "Docker fallback" below before concluding
the dump is unreadable.

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

Run `dndump commands` for the live list, and `dndump <command> --help` for a command's exact
options and sort fields (trust `--help` over any static docs, which can lag behind).

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

Reach for this whenever a native `dndump` attempt fails for any of these reasons:

- **Architecture/OS mismatch** — e.g. a Linux dump analyzed on macOS or Windows, or an ARM64 dump
  on an x64 host (or vice versa). ClrMD generally needs a host whose OS/arch matches the dump's.
- **No local .NET runtime matching the dump's version**, or no `dndump` install at all on this
  machine.
- **DAC not found locally** and no matching runtime installed to source one from.

A prebuilt, multi-arch image (`linux/amd64` and `linux/arm64`) that bundles `dndump` alongside
`dotnet-symbol` (for fetching the DAC) is published at `ghcr.io/frankbolero/dotnet-dump-mcp` — no
source checkout needed:

```bash
docker pull ghcr.io/frankbolero/dotnet-dump-mcp:latest
```

### One-shot command

Fine for a single command; pays container startup and a DAC fetch every time:

```bash
docker run --rm -i \
  -v "/path/to/your/dumps:/dumps" \
  -e DUMP_PATH=/dumps/your_dump.core \
  ghcr.io/frankbolero/dotnet-dump-mcp:latest
```

Add `--platform linux/amd64` (or `linux/arm64`) if the dump's architecture doesn't match the host's
default (e.g. analyzing an x64 Linux dump on Apple Silicon).

### Persistent session (preferred for a multi-command triage session)

Keep one long-lived container and `docker exec` into it, so container startup and the DAC fetch are
each paid once, not per command:

```bash
docker run -d --name dndump-session \
  -v "/path/to/dumps:/dumps" \
  -v dndump-symcache:/symcache \
  --entrypoint sleep \
  ghcr.io/frankbolero/dotnet-dump-mcp:latest infinity
```

`--entrypoint sleep ... infinity` is required — the image's normal entrypoint would otherwise try
(and fail) to treat `sleep`/`infinity` as a dump path. But that also means its normal DAC
auto-prefetch (which only runs as part of that entrypoint, keyed off a `DUMP_PATH` env var) is
skipped here, so fetch the DAC yourself before using the dump, and pass it explicitly — the CLI's
default (no-argument) DAC resolution does **not** automatically discover a manually-fetched DAC
sitting next to the dump, it must be told the exact path:

```bash
docker exec dndump-session dotnet-symbol --debugging --cache-directory /symcache /dumps/your_dump.core
docker exec dndump-session dndump use /dumps/your_dump.core --dac /dumps/libmscordaccore.so
docker exec dndump-session dndump dumpheap --top 20
docker exec dndump-session dndump info
```

(`libmscordaccore.so` is always the right filename here — the image, and therefore the DAC
`dotnet-symbol` fetches for it, is always Linux regardless of the host OS/CPU. `-v
dndump-symcache:/symcache` persists what `dotnet-symbol` fetches across container restarts so this
is a no-op after the first run for a given dump.)

If you hit `Could not find matching DAC for this runtime. Note that symbol server download of the
DAC is disabled for this platform.` after `use`, it means the DAC fetch above either didn't run or
wasn't passed via `--dac` — rerun both commands.

### Benchmarking a dump before a long session

The heap/thread-walk commands (`dumpheap`, `listobj`, `gcroot`, `syncblk`, `gchandles`,
`printexception`) are the expensive ones. A quick cold-vs-warm timing check before committing to a
long triage session:

```bash
time docker exec dndump-session dndump dumpheap --format json --limit 100000 >/dev/null
time docker exec dndump-session dndump dumpheap --format json --limit 100000 >/dev/null  # warm (cached)
```
