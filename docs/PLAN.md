# Development Plan: dotnet-dump-mcp-server

This document outlines the steps to transition from the `DotNetDumpExplorer` prototype to a production-ready, containerized MCP Server.

## Status Checklist

- [x] **Phase 1: Foundation & Core Logic**
    - [x] Scaffolding (Solution, Projects, NuGet)
    - [x] State Management (`IDumpContext`, `DumpContext`)
    - [x] Analyzers Implementation (`ThreadAnalyzer`, `HeapAnalyzer`, `ModuleAnalyzer`)
    - [x] Output Models (Strongly typed results)
    - [x] Formatting Layer (Markdown table generators)
- [x] **Phase 2: MCP Server Integration**
    - [x] Setup (`ModelContextProtocol` package)
    - [x] Tool Definitions (`McpServerTool` attributes)
    - [x] Request Handling (Stdio transport, Tool discovery)
- [x] **Phase 3: Containerization & Runtime**
    - [x] Entrypoint Script (`entrypoint.sh` with auto-DAC fetching)
    - [x] Dockerfile (Multi-stage build)
- [x] **Phase 4: Verification**
    - [x] Integration Tests (Passed against sample dump)
    - [x] Architecture Validation (Confirmed DAC fallback logic)

---

## Command Implementation Status

### Core Commands
| Command | Status | Tool Name | Notes |
| :--- | :---: | :--- | :--- |
| `load_dump` | ✅ | `LoadDump` | Loads dump context dynamically |

### Stack Analysis
| Command | Status | Tool Name | Notes |
| :--- | :---: | :--- | :--- |
| `clrstack` | ✅ | `ClrStack` | Grouped by call site |
| `eestack` | ❌ | | |
| `dumpstack` | ❌ | | |

### Heap Analysis
| Command | Status | Tool Name | Notes |
| :--- | :---: | :--- | :--- |
| `dumpheap` | ✅ | `DumpHeap` | Statistical summary |
| `list_objects` | ✅ | `ListObjects` | Detailed list (pagination) |
| `gcroot` | ✅ | `GcRoot` | Finds roots pointing to object |
| `verifyheap` | ❌ | | |
| `eeheap` | ✅ | `EeHeap` | List heap segments |
| `dumpobj` | ✅ | `DumpObj` | Shallow inspection |
| `gchandles` | ❌ | | |
| `syncblk` | ✅ | `SyncBlk` | List sync blocks (locks) |

### Thread Analysis
| Command | Status | Tool Name | Notes |
| :--- | :---: | :--- | :--- |
| `clrthreads` | ✅ | `ClrThreads` | Lists managed threads |
| `threadpool` | ✅ | `ThreadPool` | Basic thread pool stats |
| `threadstate` | ❌ | | Covered partially by `clrthreads` |

### Module and Assembly Analysis
| Command | Status | Tool Name | Notes |
| :--- | :---: | :--- | :--- |
| `clrmodules` | ✅ | `ClrModules` | Lists loaded modules |
| `dumpmodule` | ❌ | | |
| `dumpassembly` | ❌ | | |
| `name2ee` | ❌ | | |
| `ip2md` | ❌ | | |

### Metadata Analysis
| Command | Status | Tool Name | Notes |
| :--- | :---: | :--- | :--- |
| `dumpclass` | ❌ | | |
| `dumpmd` | ❌ | | |
| `dumpmt` | ❌ | | |

### Exception Analysis
| Command | Status | Tool Name | Notes |
| :--- | :---: | :--- | :--- |
| `pe` / `printexception` | ❌ | | |

---

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
