# Gemini Context: dotnet-dump-mcp-server

## Project Overview
**dotnet-dump-mcp-server** is a Model Context Protocol (MCP) server designed to assist users and AI agents in analyzing .NET memory dumps. It wraps ClrMD functionalities to provide programmatic access to dump analysis via the MCP standard, enabling agents to perform deep diagnostics on crash dumps.

## Technology Stack
- **Framework:** .NET 8 / 9 / 10 (Multi-targeted)
- **Key Libraries:**
  - `Microsoft.Diagnostics.Runtime` (ClrMD) - Version `3.1.512801`
  - `ModelContextProtocol` - Version `0.5.0-preview.1`
  - `Microsoft.Extensions.Hosting` - Version `9.0.0` / `10.0.1`

## Architecture
The project follows a clean separation of concerns:
1.  **DotNetDump.Core**: A reusable class library containing:
    -   `DumpContext`: Manages the `ClrRuntime` and `DataTarget` state (Lazy Loading support).
    -   `Analyzers`: Specialized logic for Heap, Threads, and Modules (e.g., `HeapAnalyzer`, `ThreadAnalyzer`).
    -   `Models`: Strongly-typed data structures for analysis results.
    -   `Formatting`: Logic to convert models into Markdown tables for Agent consumption.
2.  **DotNetDump.Server**: The MCP Server host (Console App).
    -   Handles Stdio transport.
    -   Registers `DumpAnalyzerTools` as MCP tools.
    -   Manages the application lifecycle via `IHost`.
3.  **Dockerization**: The solution is containerized to solve the "Architecture Mismatch" problem, allowing analysis of Linux/AMD64 dumps on ARM64 machines via `dotnet-symbol` and Docker emulation.

## Directory Structure
- `src/`: Source code.
  - `DotNetDump.Core/`: Core analysis logic.
  - `DotNetDump.Server/`: MCP Server entry point.
- `tests/`: Test suite.
  - `DotNetDump.Tests/`: Integration tests validating analyzers against sample dumps.
- `docs/`: Documentation.
  - `dotnet-dump-analyze-commands.md`: Reference for standard commands.
  - `OUTPUT_STRATEGY.md`: Design document for data formatting.
  - `PLAN.md`: Development roadmap and status.
  - `PRODUCT_ARCHITECTURE.md`: High-level architectural decisions.

## Building and Running

### Docker (Recommended)
Build the image:
```bash
docker build -t dotnet-dump-mcp-server .
```

Run the server (mounting dumps):
```bash
docker run --rm -i \
  -v "/path/to/dumps:/dumps" \
  -e DUMP_PATH=/dumps/target.core \
  dotnet-dump-mcp-server
```

### Local (Native)
Requires .NET SDK 9.0.
```bash
export DUMP_PATH="/path/to/dump.core"
dotnet run --project src/DotNetDump.Server/DotNetDump.Server.csproj --framework net9.0
```

### Testing
```bash
dotnet test
```

## Supported MCP Tools
The server exposes the following tools to the AI Agent:
- `load_dump`: Dynamically loads a dump file for analysis.
- `dump_heap`: Statistical summary of the managed heap.
- `list_objects`: Lists objects (supports pagination and filtering).
- `dump_obj`: Inspects fields and values of a specific object.
- `gc_root`: Finds garbage collection roots for an object.
- `ee_heap`: Lists managed heap segments.
- `clr_threads`: Lists managed threads and their states.
- `clr_stack`: Dumps stack traces, grouped by call site.
- `clr_modules`: Lists loaded modules.
- `thread_pool`: Shows ThreadPool statistics.
- `sync_blk`: Lists synchronization blocks (locks).

## Development Conventions
- **Style:** Standard C# .NET coding conventions.
- **Architecture:** "Lazy Loading" pattern for DumpContext to allow server startup without a dump.
- **Output:** All tools return Markdown-formatted strings to ensure readability for LLMs.