# dotnet-dump-mcp-server

**Analyze .NET memory dumps with the help of your AI Assistant.**

This project wraps the powerful `Microsoft.Diagnostics.Runtime` (ClrMD) library to analyze .NET
memory dumps, and ships it two ways: **`dndump`**, a command-line tool you (or an AI agent with
shell access) run directly, and a [Model Context Protocol (MCP)](https://modelcontextprotocol.io/)
server for MCP clients (like Claude Desktop or Cursor) that can't invoke a shell. Both are thin
front ends over the same analysis engine — pick whichever fits how you work.

---

## 🧰 CLI Quick Start (`dndump`)

`dndump` runs one dump analysis command at a time from your shell, prints results to stdout, and
exits — no persistent process, no context cost until you actually invoke it. This is the
recommended way to use this project, including for AI agents that have shell access (see
`docs/CLI_DESIGN.md` for the full design and rationale). If your MCP client can't run shell
commands, skip to [MCP Server Quick Start](#-mcp-server-quick-start) below instead.

### Run it (no install)

From a clone of this repo, on a host whose OS/architecture matches your dump's:

```bash
dotnet run --project src/DotNetDump.Cli/DotNetDump.Cli.csproj --framework net9.0 -- \
  use /path/to/your/dump.core
dotnet run --project src/DotNetDump.Cli/DotNetDump.Cli.csproj --framework net9.0 -- \
  info
```

### Install it as a tool (recommended for repeated use)

`dndump` isn't published to nuget.org yet, so install it from a local build:

```bash
dotnet pack src/DotNetDump.Cli/DotNetDump.Cli.csproj -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg dndump
```

Once installed, every example below works as shown (`dndump ...` instead of the longer
`dotnet run --project ...` form).

### Try it

```bash
# Orient: pick a dump, then see what you're looking at
dndump use /path/to/your/dump.core
dndump info

# Top heap consumers
dndump dumpheap --top 20

# Machine-readable output for piping through jq/grep
dndump dumpheap --format json --limit 5000 \
  | jq -r '.data[] | select(.totalSize > 1073741824) | "\(.totalSize)\t\(.typeName)"'

# Full command list, with one-line descriptions
dndump commands
```

Every command supports `--format md|json|tsv` (default `md`), `--limit`/`--offset` for paging, and
`--sort`/`--order` where a sort makes sense. `dndump <command> --help` shows a command's exact
options. See `docs/CLI_DESIGN.md` §4 for the full command reference (arguments, options, sort
fields) and §5 for worked triage examples (OOM/leak, deadlock, unhandled exception).

### Selecting a dump without repeating `--dump`

`dndump use <path>` writes `.dndump/session.json` in the current directory (already in this repo's
own `.gitignore`, and worth adding to yours if you're using `dndump` elsewhere) so later commands
in the same directory tree don't need `--dump` at all. Resolution order: `--dump <path>` flag, then
`DNDUMP_PATH` environment variable, then the session file, searched from the current directory
upward.

### Running the CLI via Docker

Useful for architecture mismatch — e.g. analyzing a Linux ARM64 dump on a Mac, or vice versa. The
image (built below) carries `dndump` alongside the MCP server, plus a DAC cache volume so the
first analysis command fetches the DAC and every later one reuses it instead of hitting the
network again:

```bash
docker build -t dotnet-dump-mcp-server .

docker run --rm -i \
  -v "/path/to/your/dumps:/dumps" \
  -v dndump-symcache:/symcache \
  -e DUMP_PATH=/dumps/your_dump.core \
  dotnet-dump-mcp-server
```

Running one container per `dndump` command would pay container startup and the DAC fetch every
time, so instead keep one long-lived container per dump-analysis session and `docker exec` into
it:

```bash
docker run -d --name dndump-session \
  -v "/path/to/dumps:/dumps" -v dndump-symcache:/symcache \
  --entrypoint sleep dotnet-dump-mcp-server infinity

docker exec dndump-session dndump --dump /dumps/prod-oom.core dumpheap --top 20
```

(`--entrypoint sleep ... infinity` is required — the image's default
`ENTRYPOINT ["./entrypoint.sh"]` would otherwise swallow a bare `sleep infinity` argument as input
to `entrypoint.sh` instead of running it.)

`scripts/dndump-docker` wraps both steps into one idempotent script, so the agent-facing commands
look identical to running `dndump` natively:

```bash
./scripts/dndump-docker use /dumps/prod-oom.core
./scripts/dndump-docker info
./scripts/dndump-docker dumpheap --top 20
```

It creates `dndump-session` on first use, reuses (or restarts) it on subsequent calls, and reads
`DNDUMP_IMAGE`, `DNDUMP_CONTAINER_NAME`, `DNDUMP_DUMPS_DIR`, `DNDUMP_SYMCACHE_VOLUME` and
`DNDUMP_PLATFORM` (for `--platform linux/amd64`-style cross-architecture runs) for configuration —
see the script header for details.

---

## 🚀 MCP Server Quick Start

### Option 1: Using Docker (Recommended)
This is the easiest way to run the server, especially if you are on a Mac analyzing Linux dumps (fixes architecture mismatches).

1.  **Build the image**:
    ```bash
    docker build -t dotnet-dump-mcp-server .
    ```

2.  **Run the server**:
    Mount the folder containing your dump files to `/dumps` inside the container.
    ```bash
    docker run --rm -i \
      -v "/path/to/your/dumps:/dumps" \
      -e DUMP_PATH=/dumps/your_dump.core \
      dotnet-dump-mcp-server
    ```

    > **Tip**: The `DUMP_PATH` variable is optional. If omitted, the server will start without a dump, and your AI agent can use the `load_dump` tool to select one later.

    > **Note for Mac users with Linux dumps**: Add `--platform linux/amd64` to the run command if you are analyzing an x64 Linux dump on Apple Silicon.

### Option 2: Running Locally

If you have the .NET SDK 8, 9 or 10 installed and your OS matches the dump's OS (e.g., Windows dump on Windows, Linux on Linux).

**MacOS / Linux**:
```bash
# Set the dump path (optional) and run
export DUMP_PATH="/path/to/your/dump.core"
dotnet run --project src/DotNetDump.Server/DotNetDump.Server.csproj --framework net9.0
```

**Windows (PowerShell)**:
```powershell
$env:DUMP_PATH = "C:\path\to\your\dump.dmp"
dotnet run --project src\DotNetDump.Server\DotNetDump.Server.csproj --framework net9.0
```

---

## ✨ Features

Every capability below is available both as an MCP tool (name shown here) and as a `dndump` CLI
command (same analysis, usually a shorter name — e.g. `dump_heap` is `dndump dumpheap`). Run
`dndump commands` for the full CLI list, or see `docs/CLI_DESIGN.md` §4 for the exact mapping.

*   **Heap Analysis**:
    *   `dump_heap`: Get a statistical summary of the managed heap (top objects by size/count).
    *   `list_objects`: List specific objects with filtering and pagination.
    *   `ee_heap`: View internal CLR heap segments.
*   **Object Inspection**:
    *   `dump_obj`: detailed view of an object's fields and values.
    *   `gc_root`: Find why an object is being kept in memory (reference chains).
    *   `gchandles`: List Garbage Collector handles.
*   **Thread & Stack Analysis**:
    *   `clr_threads`: List all managed threads and their states (Live, Dead, etc.).
    *   `clr_stack`: Get stack traces, optionally grouped by unique frames.
    *   `thread_pool`: View CLR ThreadPool status (completion ports, workers).
*   **System & Modules**:
    *   `clr_modules`: List loaded assemblies and modules.
    *   `sync_blk`: Analyze synchronization blocks to find locked objects (deadlock detection).

---

## 🤖 Agent Configuration

Add this server to your MCP client configuration (e.g., `claude_desktop_config.json` or Cursor Settings).

### Docker Config (Example)

Note that we set a timeout to 10 minutes (600000 ms) due to that some requests might take a large amount of time (dump_heap as an example).
```json
{
  "mcpServers": {
    "dotnet-dump": {
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-v", "/Users/yourname/dumps:/dumps",
        "-e", "DUMP_PATH=/dumps/crash.core",
        "dotnet-dump-mcp-server"
      ],
      "timeout": 600000
    }
  }
}
```

### Local Config (Example)
```json
{
  "mcpServers": {
    "dotnet-dump": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/dotnet-dump-mcp-server/src/DotNetDump.Server/DotNetDump.Server.csproj",
        "--framework", "net9.0"
      ],
      "env": {
        "DUMP_PATH": "/path/to/your/dump.core"
      }
    }
  }
}
```

---

## 🔣 Symbol Resolution

The server ships with ClrMD 4.x. **Dump loading is offline by default** — no symbol server is
contacted. In the Docker image, `entrypoint.sh` already fetches the matching DAC with
`dotnet-symbol --debugging` before the server starts, so the common path needs no network
access at all.

If you need ClrMD to fetch binaries itself, opt in with these environment variables:

| Variable | Description |
| :--- | :--- |
| `DOTNETDUMP_SYMBOL_PATHS` | Semicolon-separated symbol server URLs. Unset (the default) keeps dump loading fully offline. |
| `DOTNETDUMP_SYMBOL_CACHE` | Directory for downloaded symbols. Defaults to `/dumps/.symcache`. The Docker image sets this to `/symcache` (see below) so it survives container restarts even when it isn't on the dumps mount. |

```json
"env": {
  "DUMP_PATH": "/dumps/crash.core",
  "DOTNETDUMP_SYMBOL_PATHS": "https://msdl.microsoft.com/download/symbols"
}
```

> **Upgrading from an older build?** ClrMD 4 no longer reads the `_NT_SYMBOL_PATH`
> environment variable. If you previously relied on it, switch to `DOTNETDUMP_SYMBOL_PATHS`.
> Enabling a symbol server means dump loading may block on network I/O, which is worth
> keeping in mind alongside the 10-minute client timeout noted above.

---

## 🛠️ Development

To contribute or test the server locally, you can use the MCP Inspector.

1.  **Clone the repo**:
    ```bash
    git clone https://github.com/yourusername/dotnet-dump-mcp-server.git
    cd dotnet-dump-mcp-server
    ```

2.  **Run with MCP Inspector**:
    This launches a web UI to interact with the server directly, allowing you to call tools and see the output.
    
    ```bash
    export DUMP_PATH="/path/to/sample.core"
    npx @modelcontextprotocol/inspector \
      dotnet run --project src/DotNetDump.Server/DotNetDump.Server.csproj --framework net9.0
    ```

### Project Structure
*   `src/DotNetDump.Core`: The analysis logic (ClrMD wrappers). No MCP or CLI dependencies.
*   `src/DotNetDump.Cli`: The `dndump` CLI front end.
*   `src/DotNetDump.Server`: The MCP Server front end.
*   `src/DotNetDump.Tests`: Integration and unit tests for both front ends.

### Code Style
*   Run `dotnet format` before committing to ensure code style consistency.
