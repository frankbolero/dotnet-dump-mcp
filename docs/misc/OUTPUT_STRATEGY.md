# Output Strategy for Token Efficiency & Usability

## Core Principles

To ensure the AI agent remains within context window limits while providing actionable insights, all commands will adhere to the following output principles:

1.  **Summarization by Default**: Commands that typically return large datasets (`dumpheap`, `clrstack`) will return statistical summaries or grouped data by default, rather than raw lists.
2.  **Strict Pagination**: All list-based outputs will default to a maximum of 50 items. Users/Agents must explicitly request `offset` or `limit` to see more.
3.  **Markdown Formatting**: Outputs will use Markdown tables and code blocks. This balances machine readability (structure) with human readability (presentation).
4.  **Truncation Indicators**: Output must clearly indicate when data is hidden (e.g., `... 1500 more items (use --offset 50 to view)`).
5.  **Flexible Sorting**: Data tables must support sorting to allow agents to prioritize outliers (e.g., largest objects, most frequent exceptions).

## Command-Specific Strategies

### 1. Stack Analysis (`clrstack`, `eestack`)
*   **Challenge**: High thread counts often lead to repetitive stack traces, consuming massive tokens.
*   **Strategy**: **Stack Grouping**.
    *   Group threads that share the exact same call stack.
    *   **Sortable By**: `ThreadCount` (descending default).
    *   Output format:
        ```markdown
        ### Group 1 (25 Threads)
        **Threads:** 1, 5, 12, ...
        **Stack:**
        - `System.Threading.ManualResetEventSlim.Wait(...)`
        - `Microsoft.AspNetCore.Server.Kestrel...`
        ```
*   **Limit**: Show top 20 frames only. Collapsed frames indicated by `...`.

### 2. Heap Analysis (`dumpheap`)
*   **Challenge**: The managed heap can contain millions of objects.
*   **Strategy**:
    *   **Default**: Equivalent to `-stat` (statistical summary). Group by Type.
    *   **Sortable By**: `TotalSize` (default), `Count`, `TypeName`.
    *   **Direction**: `Descending` (default for Size/Count), `Ascending`.
    *   **Detailed View**: Only permitted when filtering by `Type` or `MethodTable` or explicit paging.
    *   **Table Format**:
        | Count | Total Size | Type |
        |-------|------------|------|
        | 500   | 24,000     | System.String |

### 3. Object Inspection (`dumpobj`)
*   **Challenge**: Objects can have deep reference graphs and large collections.
*   **Strategy**: **Shallow & Windowed**.
    *   **Fields**: List all primitive fields.
    *   **References**: Show address and Type name only. Do not recurse automatically.
    *   **Collections**: For Arrays/Lists, show `Count` and the first 10 items.
    *   **Strings**: Truncate values > 200 chars.

### 4. Thread List (`clrthreads`)
*   **Challenge**: Hundreds of threads.
*   **Strategy**: Compact Table.
    *   **Sortable By**: `OSThreadId`, `ManagedThreadId`, `Exception` (group threads with exceptions to top).
    *   **Table Format**:
        | ID (OS) | ID (Mgd) | State | Exception |
        |---------|----------|-------|-----------|
        | 1234    | 1        | Alive | (none)    |
        | 5678    | 2        | Dead  | System.TimeoutException |

### 5. Modules (`clrmodules`)
*   **Challenge**: Hundreds of loaded modules.
*   **Strategy**:
    *   **Sortable By**: `Size`, `Name`, `Address`.
    *   Filter system modules by default (hide `System.*`, `Microsoft.*` unless `--all` is passed).
    *   Focus on User Code.

## Implementation Guide for MCP Server
*   The MCP tool should accept the following standard arguments for list-returning tools:
    *   `limit` (int, default 50)
    *   `offset` (int, default 0)
    *   `sort_by` (string, field name)
    *   `sort_direction` (enum: `asc`, `desc`)
*   The return type should be `text/markdown`.