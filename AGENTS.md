# Gemini Context: dotnet-dump-mcp-server

## Project Overview
**dotnet-dump-mcp-server** is a Model Context Protocol (MCP) server designed to assist users and AI agents in analyzing .NET memory dumps. It wraps ClrMD functionalities to provide programmatic access to dump analysis via the MCP standard.

## Technology Stack
- **Framework:** .NET 8 / 9 / 10
- **Key Libraries:**
  - `Microsoft.Diagnostics.Runtime` (ClrMD) - Version `3.1.512801`
  - `ModelContextProtocol` - Version `0.5.0-preview.1`
  - `Microsoft.Extensions.Hosting`

## Architecture
The project is structured to separate logic from transport:
1.  **Core DLL:** Contains the main logic for interacting with ClrMD and analyzing dumps. designed to be highly testable.
2.  **Console App (CLI):** Acts as the MCP Server host, handling Stdio transport and invoking the Core DLL.

## Directory Structure
- `src/`: Source code directory (currently scaffolding).
  - Intended to contain the Core DLL and the Console App projects.
- `tests/`: Test directory (currently scaffolding).
  - Intended for unit and integration tests.
- `docs/`: Documentation.
  - `dotnet-dump-analyze-commands.md`: Reference for command outputs.

## Building and Running (Standard .NET Workflows)
*Note: Source files are currently being initialized. These commands apply once the project solution and projects are created.*

### Build
```bash
dotnet build
```

### Run
To run the server (likely via `stdio`):
```bash
dotnet run --project src/[ConsoleAppProjectName]
```

### Test
```bash
dotnet test
```

## Development Conventions
- **Style:** Standard C# .NET coding conventions.
- **Testing:** High emphasis on testability for the Core DLL.
