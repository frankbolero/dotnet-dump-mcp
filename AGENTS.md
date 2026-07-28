# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Summary

**dotnet-dump-mcp-server** is a Model Context Protocol (MCP) server that wraps Microsoft.Diagnostics.Runtime (ClrMD) to enable AI agents to analyze .NET memory dumps. It provides tools for heap analysis, thread inspection, exception tracing, and module/metadata investigation. The server runs in Docker (recommended for architecture mismatches) or natively with .NET SDK 8+.

## Build, Test, and Development Commands

### Core Development
```bash
# Build the solution
dotnet build

# Run the MCP server locally (requires DUMP_PATH to be set)
export DUMP_PATH="/path/to/dump.core"
dotnet run --project src/DotNetDump.Server/DotNetDump.Server.csproj --framework net9.0

# Run all tests (xUnit)
dotnet test

# Run a single test by name
dotnet test --filter "MethodName"

# Code formatting (applies formatting in-place)
dotnet format

# Code formatting with verify (checks without modifying)
dotnet format --verify-no-changes

# Run coverage analysis
./run-coverage.sh
```

### Docker
```bash
# Build Docker image
docker build -t dotnet-dump-mcp-server .

# Run with a dump mounted
docker run --rm -i \
  -v "/path/to/dumps:/dumps" \
  -e DUMP_PATH=/dumps/target.core \
  dotnet-dump-mcp-server
```

### MCP Inspector (Interactive Testing)
```bash
# Launch the MCP Inspector web UI to test tools directly
export DUMP_PATH="/path/to/sample.core"
npx @modelcontextprotocol/inspector \
  dotnet run --project src/DotNetDump.Server/DotNetDump.Server.csproj --framework net9.0
```

## High-Level Architecture

The solution follows a clean three-layer architecture:

### 1. **DotNetDump.Core** (Class Library)
Reusable analysis engine with no MCP dependencies:
- **`DumpContext`** (`IDumpContext`): Lazy-loading wrapper around ClrMD's `DataTarget` and `ClrRuntime`. Manages lifecycle and symbol resolution via environment variables (`DOTNETDUMP_SYMBOL_PATHS`, `DOTNETDUMP_SYMBOL_CACHE`).
- **Analyzers** (`Analyzers/`):
  - `HeapAnalyzer`: Heap stats, object listing, GC roots, heap segments, object inspection, GC handles, heap verification.
  - `ThreadAnalyzer`: Managed thread enumeration, stack traces (grouped or per-thread), thread pool stats, exception printing.
  - `ModuleAnalyzer`: Loaded assemblies, module details, MethodTable/MethodDesc lookup.
  - `MetadataAnalyzer`: EEClass, MethodDesc, and MethodTable information display.
- **Models** (`Models/`): Strongly-typed result objects (e.g., `HeapStatItem`, `ThreadInfo`, `ObjectDetails`). No formatting logic; data only.
- **Formatting** (`Formatting/`): Markdown table generation for LLM-friendly output. Handles pagination (`Skip`/`Take`) and sorting.

### 2. **DotNetDump.Server** (Console App, Exe)
MCP server host:
- **`Program.cs`**: Sets up `IHost` with dependency injection, configures MCP server over Stdio transport, loads initial dump from `DUMP_PATH` environment variable if provided.
- **`DumpAnalyzerTools.cs`**: Registers MCP tool definitions with `[McpServerTool]` attributes. Each tool maps to a method that calls the appropriate analyzer and returns Markdown.

### 3. **DotNetDump.Tests** (xUnit)
Integration tests validating analyzer behavior against real dumps:
- **`IntegrationTests.cs`**: Loads sample dumps, calls analyzers, asserts correctness of results.

### Key Design Patterns

1. **Lazy Loading (`DumpContext`)**: Server starts without a dump; agents can call `load_dump` to load one dynamically. Enables containerized execution with flexible dump selection.

2. **Offline-First Symbol Resolution**: No symbol server contact by default. DAC is fetched by `entrypoint.sh` (Docker) before the server starts. Agents can opt in via `DOTNETDUMP_SYMBOL_PATHS` if real-time fetching is needed.

3. **Markdown Output**: All tools return Markdown-formatted strings (tables, lists) for readability by LLMs. No JSON-heavy output; human-readable for AI interpretation.

4. **Pagination & Sorting**: Core models support `Skip`, `Take`, and `OrderBy` to handle large result sets efficiently without loading everything into memory at once.

5. **Minimal Dependencies**: Core depends only on `Microsoft.Diagnostics.Runtime` (ClrMD). Server adds `ModelContextProtocol` and `Microsoft.Extensions.Hosting`. Keeps the library reusable.

## Key Files and Patterns

### Dump Loading Flow
1. Optional: Set `DUMP_PATH` environment variable (used on startup).
2. Optional: Set `DOTNETDUMP_SYMBOL_PATHS` for symbol server opt-in (semicolon-separated URLs).
3. `DumpContext.Load(dumpPath)` opens the dump with ClrMD, auto-detects runtime version.
4. DAC handling: If running in Docker, `entrypoint.sh` runs `dotnet-symbol --dac-only` to fetch the matching DAC before the server starts.

### Adding a New Tool
1. Add an analyzer method to the appropriate `Analyzer` class (e.g., `HeapAnalyzer`) returning a strongly-typed model.
2. Add a `[McpServerTool]` method in `DumpAnalyzerTools.cs` that wraps the analyzer call, formats output as Markdown, and returns a string.
3. Add integration test in `DotNetDump.Tests/IntegrationTests.cs`.
4. Run `dotnet format` before committing.

### Multi-Targeting
Projects target `net8.0;net9.0;net10.0`. Ensure any new code works across all targets (no breaking APIs between versions).

## Code Style and Conventions

- Use `dotnet format` for automatic style enforcement (configured via `.editorconfig`).
- Nullable reference types enabled (`<Nullable>enable</Nullable>`).
- Implicit `using` statements enabled.
- Analyzer methods return strongly-typed models; formatting happens separately.
- No string building inside analyzers; use the `Formatting/` layer for output generation.
- Environment variable names are constants (e.g., `DumpContext.SymbolPathsVariable`).

## Symbol Resolution and DAC

By default, dump loading is **offline** (no network contact). The Docker entrypoint (`entrypoint.sh`) runs `dotnet-symbol --dac-only` to pre-fetch the matching DAC into the container before the server starts.

To enable symbol server contact at runtime (useful for local testing with incomplete symbol caches):
```bash
export DOTNETDUMP_SYMBOL_PATHS="https://msdl.microsoft.com/download/symbols"
export DOTNETDUMP_SYMBOL_CACHE="/tmp/symcache"
dotnet run --project src/DotNetDump.Server/DotNetDump.Server.csproj --framework net9.0
```

Enabling symbol servers means dump loading may block on network I/O, so be aware when setting timeouts in MCP client configs (the recommended timeout is 600000 ms / 10 minutes).

## Testing

- Test framework: **xUnit** with built-in Fact/Theory attributes.
- Coverage tool: **coverlet** (configured in test project; run via `./run-coverage.sh`).
- Integration tests load real sample dumps (referenced in `DotNetDump.Tests/IntegrationTests.cs`).
- Tests are not run in Docker by default; run locally for faster feedback.

## Docker and Architecture Mismatch

The primary use case for Docker is analyzing Linux ARM64 dumps on Mac ARM64 machines, or vice versa. The Dockerfile uses a multi-stage build:
1. Restore and build in the SDK image.
2. Runtime stage includes `dotnet-symbol` to fetch DACs.
3. `entrypoint.sh` is the entry point; it fetches the DAC if needed and starts the server.

## Important Conventions

- **Environment Variables**: All configurable paths/behaviors use environment variables (avoid hardcoding paths).
- **Strongly-Typed Models**: Analyzers return models, not strings. Formatting is separate.
- **No Side Effects in Analyzers**: Analyzer methods should be pure; state changes happen in `DumpContext` only.
- **Error Messages**: Analyzer methods throw meaningful exceptions (file not found, invalid object address, etc.); the server layer converts them to user-friendly MCP error responses.
