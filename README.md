# dotnet-dump-mcp-server

**Analyze .NET memory dumps with the help of your AI Assistant.**

This project is a [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that wraps the powerful `Microsoft.Diagnostics.Runtime` (ClrMD) library. It allows AI agents (like Claude CLI, Gemini CLI, Zed Agent or Cursor) to inspect .NET core dumps, analyze the heap, check threads, and diagnose memory leaks or deadlocks directly.

---

## 🚀 Quick Start

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

The server exposes the following tools to your AI agent:

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
      ]
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
*   `src/DotNetDump.Core`: The analysis logic (ClrMD wrappers).
*   `src/DotNetDump.Server`: The MCP Server implementation.
*   `tests`: Integration tests.

### Code Style
*   Run `dotnet format` before committing to ensure code style consistency.
