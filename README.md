# dotnet-dump-mcp-server

An MCP Server to help you and your AI friend analyze memorydumps.

## Technology

This project uses the following technologies

.NET 8/9/10

The following nuget packages are to be used
- Microsoft.Diagnostics.Runtime (version `3.1.512801`)
- ModelContextProtocol (prerelease - current version is `0.5.0-preview.1`)
- Microsoft.Extensions.Hosting

## Project structure

The project supplies a highly testable core DLL that provides the main entry to the ClrMD tools, and a simple Console App (CLI) that is the actual Stdio-trasport for the MCP Server.

## Repository structure

- Root
  - docs (documentation)
  - src (source code)
  - tests (unit tests and integration tests)
