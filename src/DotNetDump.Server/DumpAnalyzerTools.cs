using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DotNetDump.Server
{
    [McpServerToolType]
    public class DumpAnalyzerTools
    {
        private readonly HeapAnalyzer _heapAnalyzer;
        private readonly ThreadAnalyzer _threadAnalyzer;
        private readonly ModuleAnalyzer _moduleAnalyzer;

        public DumpAnalyzerTools(HeapAnalyzer heapAnalyzer, ThreadAnalyzer threadAnalyzer, ModuleAnalyzer moduleAnalyzer)
        {
            _heapAnalyzer = heapAnalyzer;
            _threadAnalyzer = threadAnalyzer;
            _moduleAnalyzer = moduleAnalyzer;
        }

        [McpServerTool, Description("Analyzes managed heap objects and returns statistical summary.")]
        public string DumpHeap(
            [Description("Field to sort by (Count, TotalSize, TypeName)")] string? sortBy = "TotalSize",
            [Description("Sort direction (Asc, Desc)")] string? sortDirection = "Desc",
            [Description("Number of items to return")] int limit = 50,
            [Description("Number of items to skip")] int offset = 0)
        {
            var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
            var stats = _heapAnalyzer.GetHeapStatistics(parameters);
            return MarkdownFormatter.FormatHeapStatistics(stats);
        }

        [McpServerTool, Description("Lists managed objects on the heap, optionally filtered by type name.")]
        public string ListObjects(
            [Description("Partial type name to filter by")] string? typeFilter = null,
            [Description("Field to sort by (Address, Size)")] string? sortBy = "Address",
            [Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
            [Description("Number of items to return")] int limit = 50,
            [Description("Number of items to skip")] int offset = 0)
        {
            var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
            var objects = _heapAnalyzer.GetObjects(parameters, typeFilter);
            return MarkdownFormatter.FormatHeapObjects(objects);
        }

        [McpServerTool, Description("Lists all managed threads in the process.")]
        public string ClrThreads(
            [Description("Field to sort by (OSThreadId, ManagedThreadId, Exception)")] string? sortBy = "ManagedThreadId",
            [Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
            [Description("Number of items to return")] int limit = 50,
            [Description("Number of items to skip")] int offset = 0)
        {
            var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
            var threads = _threadAnalyzer.GetThreads(parameters);
            return MarkdownFormatter.FormatThreads(threads);
        }

        [McpServerTool, Description("Displays managed call stacks grouped by identical stacks.")]
        public string ClrStack([Description("Maximum number of frames per thread")] int maxFrames = 20)
        {
            var groups = _threadAnalyzer.GetStackTraceGroups(maxFrames);
            return MarkdownFormatter.FormatStackGroups(groups);
        }

        [McpServerTool, Description("Lists the managed modules in the process.")]
        public string ClrModules(
            [Description("Include system modules (System.*, Microsoft.*)")] bool includeSystem = false,
            [Description("Field to sort by (Size, Name, Address)")] string? sortBy = "Address",
            [Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
            [Description("Number of items to return")] int limit = 50,
            [Description("Number of items to skip")] int offset = 0)
        {
            var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
            var modules = _moduleAnalyzer.GetModules(parameters, includeSystem);
            return MarkdownFormatter.FormatModules(modules);
        }

        private QueryParameters CreateParameters(string? sortBy, string? sortDirection, int limit, int offset)
        {
            return new QueryParameters
            {
                SortBy = sortBy,
                SortDirection = sortDirection?.ToLower() == "asc" ? SortDirection.Asc : SortDirection.Desc,
                Limit = limit,
                Offset = offset
            };
        }
    }
}
