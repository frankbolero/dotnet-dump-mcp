# dotnet-dump-mcp-server

An MCP Server to help you and your AI friend analyze .NET memory dumps.

## Technology

- **Framework**: .NET 8 / 9
- **Libraries**:
  - `Microsoft.Diagnostics.Runtime` (ClrMD) for dump analysis.
  - `ModelContextProtocol` for AI agent communication.
  - `Microsoft.Extensions.Hosting` for structured application lifecycle.

## Important

- Always use `dotnet format` to ensure code formatting.

## Usage

This server communicates via Stdio using the Model Context Protocol. It requires a path to a memory dump file to initialize.

### 1. Using Docker (Recommended for Cross-Platform)

Docker is the preferred method because it solves the "Architecture Mismatch" problem (e.g., analyzing an AMD64 Linux dump on an ARM64 Mac) and automatically handles the Data Access Component (DAC) fetching.

#### Build the image
```bash
docker build -t dotnet-dump-mcp-server .
```

#### Run the server
You must mount the directory containing your dump file and provide the path via the `DUMP_PATH` environment variable.

```bash
docker run --rm -i \
  -v "/path/to/your/dumps:/dumps" \
  -e DUMP_PATH=/dumps/your_dump.core \
  dotnet-dump-mcp-server
```

*Note: Use `--platform linux/amd64` if your dump was generated on an AMD64 Linux system and you are running on an ARM64 machine.*

---

### 2. Running Locally (Native)

Use this method if your local machine architecture matches the dump file's architecture.

#### Prerequisites
- .NET 8 or 9 SDK installed.
- `dotnet-symbol` tool (optional, but recommended for fetching DACs):
  ```bash
  dotnet tool install --global dotnet-symbol
  ```

#### Run the server
```bash
export DUMP_PATH="/path/to/your/dump.core"
dotnet run --project src/DotNetDump.Server/DotNetDump.Server.csproj --framework net9.0
```

---

### 3. Integration with AI Agents

To use this with an MCP-compatible agent (like Cursor, Windsurf, or Claude Desktop), add the following to your configuration:

#### Docker Configuration (Example)
```json
{
  "mcpServers": {
    "dotnet-dump": {
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-v", "/Users/path/to/dumps:/dumps",
        "-e", "DUMP_PATH=/dumps/my_app.core",
        "dotnet-dump-mcp-server"
      ]
    }
  }
}
```

#### Local Configuration (Example)
```json
{
  "mcpServers": {
    "dotnet-dump": {
      "command": "dotnet",
      "args": [
        "run",
        "--framework",
        "net9.0",
        "--project",
        "/Users/USERNAME/src/dotnet-dump-mpc-server/src/DotNetDump.Server/DotNetDump.Server.csproj"
      ]
    }
  }
}
```

## Commands Supported

The following tools are exposed to the AI Agent:
- `dump_heap`: Statistical summary of the managed heap.
- `list_objects`: Detailed list of objects (with filtering and pagination).
- `clr_threads`: List of all managed threads and their states.
- `clr_stack`: Stack traces grouped by identical call sites.
- `clr_modules`: List of loaded managed modules.
- `gc_root`: Finds garbage collection roots for a specific object.

## Project Structure

- `DotNetDump.Core`: Class library containing the analyzer logic and formatting.
- `DotNetDump.Server`: The MCP Server host handling transport and tool registration.
- `DotNetDump.Tests`: Integration tests against sample dumps.
