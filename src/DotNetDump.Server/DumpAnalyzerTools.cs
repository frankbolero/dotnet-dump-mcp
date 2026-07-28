using System.ComponentModel;

using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

using ModelContextProtocol.Server;

namespace DotNetDump.Server;

[McpServerToolType]
public class DumpAnalyzerTools {
	private readonly IDumpContext _dumpContext;
	private readonly HeapAnalyzer _heapAnalyzer;
	private readonly ThreadAnalyzer _threadAnalyzer;
	private readonly ModuleAnalyzer _moduleAnalyzer;
	private readonly MetadataAnalyzer _metadataAnalyzer;

	public DumpAnalyzerTools(IDumpContext dumpContext, HeapAnalyzer heapAnalyzer, ThreadAnalyzer threadAnalyzer, ModuleAnalyzer moduleAnalyzer, MetadataAnalyzer metadataAnalyzer) {
		_dumpContext = dumpContext;
		_heapAnalyzer = heapAnalyzer;
		_threadAnalyzer = threadAnalyzer;
		_moduleAnalyzer = moduleAnalyzer;
		_metadataAnalyzer = metadataAnalyzer;
	}

	[McpServerTool, Description("Loads a memory dump file for analysis. Must be called before other tools.")]
	public string LoadDump([Description("The absolute path to the .dmp or .core file")] string path) {
		try {
			_dumpContext.Load(path);
			return $"Successfully loaded dump: {path}";
		} catch (Exception ex) {
			return $"Error loading dump: {ex.Message}";
		}
	}

	[McpServerTool, Description("Analyzes managed heap objects and returns a statistical summary by type, including each type's MethodTable.")]
	public string DumpHeap(
		[Description("Field to sort by (Count, TotalSize, TypeName)")] string? sortBy = "TotalSize",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Desc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var stats = _heapAnalyzer.GetHeapStatistics(parameters);
			return MarkdownFormatter.FormatHeapStatistics(stats.Items);
		});
	}

	[McpServerTool, Description("Lists managed objects on the heap, optionally filtered by type name.")]
	public string ListObjects(
		[Description("Partial type name to filter by")] string? typeFilter = null,
		[Description("Field to sort by (Address, Size)")] string? sortBy = "Address",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var objects = _heapAnalyzer.GetObjects(parameters, typeFilter);
			return MarkdownFormatter.FormatHeapObjects(objects.Items);
		});
	}

	[McpServerTool, Description("Lists all managed threads in the process.")]
	public string ClrThreads(
		[Description("Field to sort by (OSThreadId, ManagedThreadId, Exception)")] string? sortBy = "ManagedThreadId",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var threads = _threadAnalyzer.GetThreads(parameters);
			return MarkdownFormatter.FormatThreads(threads);
		});
	}

	[McpServerTool, Description("Displays managed call stacks grouped by identical stacks.")]
	public string ClrStack([Description("Maximum number of frames per thread")] int maxFrames = 20) {
		return ExecuteSafe(() => {
			var groups = _threadAnalyzer.GetStackTraceGroups(maxFrames);
			return MarkdownFormatter.FormatStackGroups(groups);
		});
	}

	[McpServerTool, Description("Displays merged thread stacks grouped by common call patterns (similar to Visual Studio Parallel Stacks).")]
	public string EeStack([Description("Maximum number of frames per thread")] int maxFrames = 30) {
		return ExecuteSafe(() => {
			var groups = _threadAnalyzer.GetStackTraceGroups(maxFrames);
			return MarkdownFormatter.FormatStackGroups(groups);
		});
	}

	[McpServerTool, Description("Displays detailed stack traces for all threads including frame types, addresses, and declaring types.")]
	public string DumpStack(
		[Description("Field to sort by (ManagedThreadId, OSThreadId)")] string? sortBy = "ManagedThreadId",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
		[Description("Maximum number of frames per thread")] int maxFrames = 100,
		[Description("Number of threads to return")] int limit = 50,
		[Description("Number of threads to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var stacks = _threadAnalyzer.GetDetailedStacks(parameters, maxFrames);
			return MarkdownFormatter.FormatDetailedStacks(stacks);
		});
	}

	[McpServerTool, Description("Lists the managed modules in the process.")]
	public string ClrModules(
		[Description("Include runtime/framework modules from the shared framework directory")] bool includeSystem = false,
		[Description("Field to sort by (Size, Name, Address)")] string? sortBy = "Address",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var modules = _moduleAnalyzer.GetModules(parameters, includeSystem);
			return MarkdownFormatter.FormatModules(modules);
		});
	}

	[McpServerTool, Description("Finds why an object is still alive by tracing reference chains from GC roots to it. Returns the full retention path, not just direct roots. The result always states whether the search completed or was truncated by the node budget — a truncated result with no paths is inconclusive, not proof the object is unrooted.")]
	public string GcRoot(
		[Description("The hex address of the object to find roots for (0x prefix optional)")] string address,
		[Description("Maximum number of distinct retention paths to return")] int maxPaths = 4,
		[Description("Traversal budget in nodes visited per search pass. 0 means unlimited, which is the only way to get a conclusive answer but is not free: memory scales with nodes visited, roughly 40 bytes each (~4 GB at 100,000,000). Defaults to the DNDUMP_GCROOT_MAX_NODES environment variable, else 2,000,000.")] int? maxNodes = null,
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			ulong objAddr = AddressParser.Parse(address);
			var parameters = CreateParameters(null, null, limit, offset);
			var result = _heapAnalyzer.GetGCRoots(objAddr, parameters, maxPaths, maxNodes);
			return MarkdownFormatter.FormatGCRootPaths(result);
		});
	}

	[McpServerTool, Description("Inspects a specific object, listing its fields and values.")]
	public string DumpObj([Description("The hex address of the object to inspect (0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong objAddr = AddressParser.Parse(address);
			var details = _heapAnalyzer.GetObjectDetails(objAddr);
			return MarkdownFormatter.FormatObjectDetails(details);
		});
	}

	[McpServerTool, Description("Displays the managed heap segments, including generation, committed and reserved bytes, GC flavour and DATAS state.")]
	public string EeHeap() {
		return ExecuteSafe(() => {
			var summary = _heapAnalyzer.GetHeapSegments();
			return MarkdownFormatter.FormatHeapSegments(summary);
		});
	}

	[McpServerTool, Description("Displays held monitors. Includes thin locks (uncontended locks that allocate no sync block) by default.")]
	public string SyncBlk(
		[Description("Include thin locks; requires a full heap walk")] bool includeThinLocks = true,
		[Description("Field to sort by (Recursion, Waiting, Address)")] string? sortBy = "Address",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Desc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var blocks = _heapAnalyzer.GetSyncBlocks(parameters, includeThinLocks);
			return MarkdownFormatter.FormatSyncBlocks(blocks.Items);
		});
	}

	[McpServerTool, Description("Displays information about the CLR ThreadPool, including CPU utilization and completion ports.")]
	public string ThreadPool() {
		return ExecuteSafe(() => {
			var info = _threadAnalyzer.GetThreadPoolInfo();
			return MarkdownFormatter.FormatThreadPool(info);
		});
	}

	[McpServerTool, Description("Lists all GC handles in the process, including handle strength, ref count and dependent targets.")]
	public string GcHandles(
		[Description("Field to sort by (Address, Kind, TypeName)")] string? sortBy = "Address",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var handles = _heapAnalyzer.GetGCHandles(parameters);
			return MarkdownFormatter.FormatGCHandles(handles);
		});
	}

	[McpServerTool, Description("Verifies the integrity of the managed heap and reports any corruption found, with its kind and offset.")]
	public string VerifyHeap() {
		return ExecuteSafe(() => {
			var corruptions = _heapAnalyzer.VerifyHeap();
			return MarkdownFormatter.FormatHeapVerification(corruptions);
		});
	}

	[McpServerTool, Description("Verifies a single object at the given address and reports any corruption found.")]
	public string VerifyObj([Description("The hex address of the object to verify (0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong objAddr = AddressParser.Parse(address);
			var corruptions = _heapAnalyzer.VerifyObject(objAddr);
			return MarkdownFormatter.FormatHeapVerification(corruptions);
		});
	}

	[McpServerTool, Description("Displays information about a MethodTable structure, including real type flags and implemented interfaces.")]
	public string DumpMt([Description("The hex address of the MethodTable (0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong mt = AddressParser.Parse(address);
			var info = _metadataAnalyzer.GetMethodTable(mt);
			return MarkdownFormatter.FormatMethodTable(info);
		});
	}

	[McpServerTool, Description("Displays information about a MethodDesc structure.")]
	public string DumpMd([Description("The hex address of the MethodDesc (0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong md = AddressParser.Parse(address);
			var info = _metadataAnalyzer.GetMethodDesc(md);
			return MarkdownFormatter.FormatMethodDesc(info);
		});
	}

	[McpServerTool, Description("Displays type metadata: fields with offsets, static field values, and methods. Takes a MethodTable address (ClrMD does not expose EEClass separately).")]
	public string DumpClass([Description("The hex address of the MethodTable (0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong methodTable = AddressParser.Parse(address);
			var info = _metadataAnalyzer.GetClass(methodTable);
			return MarkdownFormatter.FormatClass(info);
		});
	}

	[McpServerTool, Description("Displays detailed information about a loaded module.")]
	public string DumpModule([Description("The hex address of the module (ImageBase or MetadataAddress; 0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong moduleAddr = AddressParser.Parse(address);
			var info = _moduleAnalyzer.GetModuleDetails(moduleAddr);
			return MarkdownFormatter.FormatModuleDetails(info);
		});
	}

	[McpServerTool, Description("Displays information about a loaded assembly. Accepts the runtime Assembly address or a module ImageBase.")]
	public string DumpAssembly([Description("The hex address of the assembly, or a module ImageBase (0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong assemblyAddress = AddressParser.Parse(address);
			var info = _moduleAnalyzer.GetAssemblyDetails(assemblyAddress);
			return MarkdownFormatter.FormatAssemblyDetails(info);
		});
	}

	[McpServerTool, Description("Finds MethodTable and MethodDesc for a type or method by name.")]
	public string Name2Ee(
		[Description("The module name (e.g., 'System.Private.CoreLib' or 'MyApp')")] string moduleName,
		[Description("The type name, optionally followed by .MethodName (e.g., 'System.String' or 'MyNamespace.MyClass.MyMethod')")] string typeName) {
		return ExecuteSafe(() => {
			var info = _moduleAnalyzer.Name2EE(moduleName, typeName);
			return MarkdownFormatter.FormatName2EE(info);
		});
	}

	[McpServerTool, Description("Gets the MethodDesc for the method at the specified instruction pointer.")]
	public string Ip2Md([Description("The hex address of the instruction pointer (0x prefix optional)")] string address) {
		return ExecuteSafe(() => {
			ulong ip = AddressParser.Parse(address);
			var info = _moduleAnalyzer.GetMethodByIP(ip);
			return MarkdownFormatter.FormatMethodDesc(info);
		});
	}

	[McpServerTool, Description("Displays detailed thread state including GC mode, apartment state, lock count and runtime flags.")]
	public string ThreadState(
		[Description("Field to sort by (ManagedThreadId, OSThreadId, LockCount)")] string? sortBy = "ManagedThreadId",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var states = _threadAnalyzer.GetThreadStates(parameters);
			return MarkdownFormatter.FormatThreadStates(states);
		});
	}

	[McpServerTool, Description("Displays exceptions with messages, HResults, stack traces and inner exceptions. Finds exceptions in flight on a thread AND exceptions reachable on the heap (which is where already-caught exceptions live), or one specific exception by address.")]
	public string PrintException(
		[Description("Optional hex address of a specific exception object to print (0x prefix optional)")] string? address = null,
		[Description("Also scan the heap for exception objects not in flight on any thread; requires a full heap walk")] bool includeHeapExceptions = true,
		[Description("Only include threads that have an exception in flight")] bool onlyWithExceptions = true,
		[Description("Field to sort by (ManagedThreadId, OSThreadId)")] string? sortBy = "ManagedThreadId",
		[Description("Sort direction (Asc, Desc)")] string? sortDirection = "Asc",
		[Description("Number of items to return")] int limit = 50,
		[Description("Number of items to skip")] int offset = 0) {
		return ExecuteSafe(() => {
			if (!string.IsNullOrWhiteSpace(address)) {
				ulong exceptionAddress = AddressParser.Parse(address);
				var single = _threadAnalyzer.GetExceptionByAddress(exceptionAddress);
				return MarkdownFormatter.FormatThreadExceptions(new[] { single });
			}

			var parameters = CreateParameters(sortBy, sortDirection, limit, offset);
			var exceptions = _threadAnalyzer.GetThreadExceptions(parameters, onlyWithExceptions, includeHeapExceptions);
			return MarkdownFormatter.FormatThreadExceptions(exceptions.Items);
		});
	}

	private QueryParameters CreateParameters(string? sortBy, string? sortDirection, int limit, int offset) {
		return new QueryParameters {
			SortBy = sortBy,
			SortDirection = sortDirection?.ToLower() == "asc" ? SortDirection.Asc : SortDirection.Desc,
			Limit = limit,
			Offset = offset
		};
	}

	private string ExecuteSafe(Func<string> action) {
		try {
			return action();
		} catch (Exception ex) {
			return $"Error: {ex.Message}";
		}
	}
}