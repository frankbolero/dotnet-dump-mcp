# Development Plan: dotnet-dump-mcp-server

This document outlines the steps to transition from the `DotNetDumpExplorer` prototype to a production-ready, containerized MCP Server.

## Phase 1: Foundation & Core Logic (The "Core" Project)

**Goal:** Create a reusable, testable library that manages the `ClrMD` state and implements the analysis logic defined in `OUTPUT_STRATEGY.md`.

1.  **Scaffolding**:
    *   Create `dotnet-dump-mcp-server.sln`.
    *   Create project `DotNetDump.Core` (Class Library).
    *   Create project `DotNetDump.Server` (Console App).
    *   Create project `DotNetDump.Tests` (xUnit).

2.  **State Management (`IDumpContext`)**:
    *   Implement a service that holds the `DataTarget` and `ClrRuntime` singleton.
    *   Ensure it implements `IDisposable` to release file locks.
    *   **DAC Handling**: Port the logic from `DotNetDumpExplorer` to automatically find or download the DAC into this context initialization.

3.  **Analyzers Implementation**:
    *   Create specialized analyzer classes in `Core`:
        *   `ThreadAnalyzer`: Implements `clrthreads`, `clrstack`, `eestack`.
        *   `HeapAnalyzer`: Implements `dumpheap`, `dumpobj`, `eeheap`.
        *   `ModuleAnalyzer`: Implements `clrmodules`, `name2ee`.
    *   **Output Models**: Define strongly-typed classes for results (e.g., `HeapStatItem`, `ThreadInfo`) to decouple logic from string formatting.

4.  **Formatting Layer**:
    *   Implement the **Output Strategy**:
        *   Pagination logic (`Take`, `Skip`).
        *   Sorting logic (`OrderBy`).
        *   Markdown table generators.

## Phase 2: MCP Server Integration (The "Server" Project)

**Goal:** Wrap the Core logic in an MCP-compliant server that runs over Stdio.

1.  **Setup**:
    *   Install `ModelContextProtocol` NuGet package.
    *   Configure the server capability (Resources? Tools? Prompts? **Tools** is the primary fit).

2.  **Tool Definitions**:
    *   Define `Tool` schemas for every command documented in `docs/commands/*.md`.
    *   Parameters: `limit`, `offset`, `sort_by`, `sort_direction`, plus command-specifics (e.g., `min_size` for `dumpheap`).

3.  **Request Handling**:
    *   Implement the Request Handler loop.
    *   Map `CallToolAsync` requests to the appropriate `Analyzer` method in `Core`.
    *   Handle exceptions gracefully (return user-friendly error messages, not stack traces).

## Phase 3: Containerization & Runtime

**Goal:** Solve the "Architecture Mismatch" problem using Docker.

1.  **Entrypoint Script (`entrypoint.sh`)**:
    *   Write a Bash script that:
        1.  Checks if the dump file exists.
        2.  Runs `dotnet-symbol --dac-only` if necessary.
        3.  Starts the `DotNetDump.Server` process, passing the dump path.

2.  **Dockerfile**:
    *   Refine the prototype Dockerfile.
    *   Ensure `dotnet-symbol` is on the PATH.
    *   Optimize for size (Multi-stage build).

## Phase 4: Verification

1.  **Integration Tests**:
    *   Use `DotNetDump.Tests` to load the sample dump (`dumps/core_...`).
    *   Assert that `HeapAnalyzer` returns correct counts.

2.  **End-to-End Test**:
    *   Build the Docker image.
    *   Run it locally against the sample dump.
    *   Connect an MCP Client (e.g., a simple Python script or a generic MCP inspector) to verify JSON-RPC communication.

## Execution Order

1.  **Scaffold Solution** (Immediate).
2.  **Port Core Logic** (High Priority).
3.  **Build Server Wrapper**.
4.  **Dockerize**.
