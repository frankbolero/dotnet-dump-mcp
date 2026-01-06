# Product Architecture: Containerized DotNet Dump MCP Server

## The Core Challenge: Architecture Mismatch
Memory dump analysis is architecture-sensitive.
*   **Scenario**: A user on an Apple Silicon (ARM64) MacBook wants to analyze a dump from an Azure Kubernetes Service (AMD64 Linux) pod.
*   **The Problem**: `ClrMD` (and the underlying OS debug APIs) generally requires the host process to match the target dump's architecture (pointer size, endianness). Loading an AMD64 dump into an ARM64 process is fraught with instability or impossible.
*   **The Dependency**: Analysis requires the **DAC** (Data Access Component), a library (`libmscordaccore.so`) matching the *exact* version and architecture of the runtime that crashed.

## The Solution: Containerized Abstraction

We will treat the MCP Server not as a local CLI tool, but as a **Containerized Workload**.

### 1. The Container Image
We will publish a multi-arch Docker image, but typically users will force a specific architecture based on their *dump*, not their *machine*.

*   **Base Image**: `mcr.microsoft.com/dotnet/sdk:9.0` (Contains necessary build tools and `dotnet-symbol`).
*   **Components**:
    1.  **The MCP Server (Our C# App)**: Compiled as `AnyCpu` (portable).
    2.  **`dotnet-symbol`**: A CLI tool pre-installed to fetch DACs automatically.
    3.  **Entrypoint Script**: A shell script that initializes the environment before starting the MCP server.

### 2. Stateful Analysis Lifecycle (The "Load Once" Model)
Memory dumps are heavy. A 10GB dump can take 30+ seconds to index. A "one-shot" CLI approach (load -> answer -> exit) is too slow for an interactive chat.

**The Server Architecture:**
1.  **Startup**: The container starts. It expects the dump path as an environment variable or argument.
2.  **Initialization**:
    *   It verifies the dump.
    *   It downloads the required DAC using `dotnet-symbol` if missing.
    *   It initializes `DataTarget` and `ClrRuntime`.
    *   **Crucially**: It builds the Heap Index (`runtime.Heap`) immediately and keeps this object alive in RAM.
3.  **The Loop (MCP Protocol)**:
    *   The process listens on `StdIn` for JSON-RPC messages (Model Context Protocol).
    *   When the Agent asks for `dumpheap`, the server queries the *already loaded* `ClrHeap` instance.
    *   Response is instantaneous.
4.  **Shutdown**: The container runs until the Agent terminates the connection.

### 3. Execution Flow (The "Rosetta" Trick)

When an ARM64 Mac user analyzes an AMD64 dump:

1.  **User/Agent Command**:
    ```bash
    docker run --platform linux/amd64 --rm -i \
      -v /path/to/dump:/dump \
      ghcr.io/my-org/dotnet-dump-mcp \
      --dump-path /dump/core.dmp
    ```
2.  **Docker Host**: Emulates AMD64 instructions on the ARM64 CPU.
3.  **Inside Container**:
    *   OS: Linux AMD64.
    *   Process: .NET 9 (AMD64).
    *   Dump: Linux AMD64.
    *   **State**: The process stays running, holding the 10GB dump in memory.
4.  **ClrMD**: Sees a native match. It downloads the matching Linux AMD64 DAC from Microsoft's symbol servers and successfully analyzes the dump.

### 4. Symbol & DAC Management
To make this a "Product" that "Just Works":
*   The application must detect missing DACs.
*   It should shell out to `dotnet-symbol --dac-only <dump_path>` inside the container to fetch the required library if `ClrMD` fails to load the runtime initially.
*   This ensures that even if the dump is from an obscure patch version of .NET 8, the container can self-heal.

## Integration with MCP Clients (Cursor/Windsurf/Claude)

MCP Clients usually run a command (stdio). To use the Docker approach, the configuration in the AI editor would look like this:

```json
{
  "mcpServers": {
    "dotnet-dump": {
      "command": "docker",
      "args": [
        "run",
        "--rm",
        "-i", // Interactive (Keep Stdin open for MCP)
        "--platform", "linux/amd64", // Force Architecture match
        "-v", "${workspaceFolder}/dumps:/dumps",
        "dotnet-dump-mcp-server",
        "/dumps/target.dmp"
      ]
    }
  }
}
```

## Summary
*   **Build**: Standard `dotnet build`.
*   **Run**: Inside Docker or locally when CPU architecture and platform matches dump.
*   **Cross-Platform**: Solved via Docker Desktop's QEMU/Rosetta emulation.
