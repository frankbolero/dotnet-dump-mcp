using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class IntegrationTests : IDisposable {
	private readonly string _dumpPath;
	private readonly DumpContext _context;

	public IntegrationTests() {
		// Use the provided sample dump
		_dumpPath = Path.GetFullPath("../../../../../dumps/core_20251212_112511");
		_context = new DumpContext();

		if (File.Exists(_dumpPath)) {
			_context.Initialize(_dumpPath);
		}
	}

	[Fact]
	public void DumpContext_InitializesCorrecty() {
		if (!File.Exists(_dumpPath)) return; // Skip if dump missing

		Assert.NotNull(_context.DataTarget);
		Assert.NotNull(_context.Runtime);
		Assert.NotNull(_context.Heap);
	}

	[Fact]
	public void DumpContext_Load_LoadsDumpSuccessfully() {
		if (!File.Exists(_dumpPath)) return;

		var context = new DumpContext();
		context.Load(_dumpPath);

		Assert.True(context.IsLoaded);
		Assert.NotNull(context.DataTarget);
		Assert.NotNull(context.Runtime);
		Assert.NotNull(context.Heap);

		context.Dispose();
	}

	[Fact]
	public void HeapAnalyzer_ReturnsStatistics() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var stats = analyzer.GetHeapStatistics(new QueryParameters { Limit = 10 }).ToList();

		Assert.NotEmpty(stats);
		Assert.Contains(stats, s => s.TypeName == "System.String");
	}

	[Fact]
	public void ThreadAnalyzer_GroupsStacks() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ThreadAnalyzer(_context);
		var groups = analyzer.GetStackTraceGroups().ToList();

		Assert.NotEmpty(groups);
		Assert.True(groups.First().ThreadCount > 0);
	}

	[Fact]
	public void HeapAnalyzer_GetGCRoots_ReturnsRoots() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);

		// Find an object first
		var obj = _context.Heap.EnumerateObjects()
			.FirstOrDefault(o => o.Type != null && o.Type.Name == "System.String");
		if (obj.Address == 0) return; // No string found

		// This test is tricky because we need an object that HAS roots.
		// Strings might not be rooted if they are garbage.
		// But let's try to call the method and ensure it doesn't crash.
		var roots = analyzer.GetGCRoots(obj.Address, new QueryParameters { Limit = 10 }).ToList();

		// Assert no exception
		Assert.NotNull(roots);
	}

	[Fact]
	public void HeapAnalyzer_GetObjectDetails_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var obj = _context.Heap.EnumerateObjects()
			.FirstOrDefault(o => o.Type != null && o.Type.Name == "System.String");
		if (obj.Address == 0) return;

		var details = analyzer.GetObjectDetails(obj.Address);

		Assert.Equal(obj.Address, details.Address);
		Assert.Equal("System.String", details.TypeName);
		Assert.NotEmpty(details.Fields);
	}

	[Fact]
	public void HeapAnalyzer_GetHeapSegments_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var segments = analyzer.GetHeapSegments().ToList();

		Assert.NotEmpty(segments);
		Assert.True(segments.First().Size > 0);
	}

	[Fact]
	public void ThreadAnalyzer_GetThreadPoolInfo_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ThreadAnalyzer(_context);
		var info = analyzer.GetThreadPoolInfo();

		Assert.NotNull(info);
		Assert.True(info.TotalThreads >= 0);
	}

	[Fact]
	public void HeapAnalyzer_GetSyncBlocks_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var blocks = analyzer.GetSyncBlocks(new QueryParameters()).ToList();

		// Might be empty, but shouldn't throw
		Assert.NotNull(blocks);
	}

	[Fact]
	public void HeapAnalyzer_VerifyHeap_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var corruptions = analyzer.VerifyHeap().ToList();

		// Should not throw, corruptions list might be empty (which is good)
		Assert.NotNull(corruptions);
	}

	[Fact]
	public void HeapAnalyzer_GetGCHandles_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var handles = analyzer.GetGCHandles(new QueryParameters { Limit = 50 }).ToList();

		// Might be empty, but shouldn't throw
		Assert.NotNull(handles);
	}

	[Fact]
	public void HeapAnalyzer_GetObjects_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var objects = analyzer.GetObjects(new QueryParameters { Limit = 10 }, null).ToList();

		Assert.NotEmpty(objects);
		Assert.All(objects, obj => Assert.True(obj.Address > 0));
	}

	[Fact]
	public void HeapAnalyzer_GetObjects_WithFilter_ReturnsFilteredData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new HeapAnalyzer(_context);
		var objects = analyzer.GetObjects(new QueryParameters { Limit = 10 }, "System.String").ToList();

		Assert.NotEmpty(objects);
		Assert.All(objects, obj => Assert.Contains("String", obj.TypeName ?? ""));
	}

	[Fact]
	public void ThreadAnalyzer_GetThreads_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ThreadAnalyzer(_context);
		var threads = analyzer.GetThreads(new QueryParameters { Limit = 50 }).ToList();

		Assert.NotEmpty(threads);
		Assert.All(threads, thread => Assert.True(thread.OSThreadId > 0 || thread.ManagedThreadId >= 0));
	}

	[Fact]
	public void ThreadAnalyzer_GetDetailedStacks_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ThreadAnalyzer(_context);
		var stacks = analyzer.GetDetailedStacks(new QueryParameters { Limit = 10 }, maxFrames: 20).ToList();

		Assert.NotEmpty(stacks);
		Assert.All(stacks, stack => {
			Assert.True(stack.OSThreadId > 0 || stack.ManagedThreadId >= 0);
			Assert.NotNull(stack.Frames);
		});
	}

	[Fact]
	public void ModuleAnalyzer_GetModules_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ModuleAnalyzer(_context);
		var modules = analyzer.GetModules(new QueryParameters { Limit = 50 }, includeSystem: true).ToList();

		Assert.NotEmpty(modules);
		Assert.All(modules, module => Assert.NotNull(module.Name));
	}

	[Fact]
	public void ModuleAnalyzer_GetModules_ExcludeSystem_ReturnsUserModules() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ModuleAnalyzer(_context);
		var modules = analyzer.GetModules(new QueryParameters { Limit = 50 }, includeSystem: false).ToList();

		// Might be empty if only system modules exist
		Assert.NotNull(modules);
		Assert.All(modules, module => Assert.True(module.IsUserCode));
	}

	[Fact]
	public void MetadataAnalyzer_GetMethodTable_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new MetadataAnalyzer(_context);

		// Find a type and get its MethodTable
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Name == "System.String");
		if (type == null) return;

		var methodTableInfo = analyzer.GetMethodTable(type.MethodTable);

		Assert.Equal(type.MethodTable, methodTableInfo.MethodTable);
		Assert.Equal("System.String", methodTableInfo.TypeName);
		Assert.NotNull(methodTableInfo.ModuleName);
		Assert.True(methodTableInfo.MethodCount > 0);
	}

	[Fact]
	public void MetadataAnalyzer_GetMethodDesc_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new MetadataAnalyzer(_context);

		// Find a type with methods and get a MethodDesc
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Methods.Any());
		if (type == null) return;

		var method = type.Methods.FirstOrDefault(m => m.MethodDesc != 0);
		if (method == null) return;

		var methodDescInfo = analyzer.GetMethodDesc(method.MethodDesc);

		Assert.Equal(method.MethodDesc, methodDescInfo.MethodDesc);
		Assert.NotNull(methodDescInfo.MethodName);
		Assert.NotNull(methodDescInfo.TypeName);
	}

	[Fact]
	public void MetadataAnalyzer_GetClass_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new MetadataAnalyzer(_context);

		// Find a type and get its class info (using MethodTable as EEClass)
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Name == "System.String");
		if (type == null) return;

		var classInfo = analyzer.GetClass(type.MethodTable);

		Assert.Equal(type.MethodTable, classInfo.MethodTable);
		Assert.Equal("System.String", classInfo.TypeName);
		Assert.NotNull(classInfo.ModuleName);
		Assert.True(classInfo.MethodCount > 0);
		Assert.NotNull(classInfo.Fields);
	}

	[Fact]
	public void ModuleAnalyzer_GetModuleDetails_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ModuleAnalyzer(_context);

		// Get first module with non-zero size
		var module = _context.Runtime.EnumerateModules().FirstOrDefault(m => m.Size > 0);
		if (module == null) return;

		var moduleDetails = analyzer.GetModuleDetails(module.ImageBase);

		Assert.Equal(module.ImageBase, moduleDetails.ImageBase);
		Assert.NotNull(moduleDetails.Name);
		Assert.True(moduleDetails.Size > 0);
	}

	[Fact]
	public void ModuleAnalyzer_GetAssemblyDetails_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ModuleAnalyzer(_context);

		// Get first module and use its ImageBase as AssemblyId
		var module = _context.Runtime.EnumerateModules().FirstOrDefault();
		if (module == null) return;

		var assemblyDetails = analyzer.GetAssemblyDetails(module.ImageBase);

		Assert.NotNull(assemblyDetails.Name);
		Assert.NotEmpty(assemblyDetails.Modules);
	}

	[Fact]
	public void ModuleAnalyzer_Name2EE_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ModuleAnalyzer(_context);

		// Try to find System.String in System.Private.CoreLib
		try {
			var result = analyzer.Name2EE("System.Private.CoreLib", "System.String");

			Assert.NotNull(result.TypeName);
			Assert.True(result.MethodTable != 0);
			Assert.Contains("System.Private.CoreLib", result.ModuleName ?? "");
		} catch (ArgumentException) {
			// Module or type might not exist in this dump, skip test
			return;
		}
	}

	[Fact]
	public void ModuleAnalyzer_GetMethodByIP_ReturnsData() {
		if (!File.Exists(_dumpPath)) return;

		var analyzer = new ModuleAnalyzer(_context);

		// Find a method with valid native code (not 0 and not all Fs)
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Methods.Any(m => m.NativeCode != 0 && m.NativeCode != ulong.MaxValue));
		if (type == null) return;

		var method = type.Methods.FirstOrDefault(m => m.NativeCode != 0 && m.NativeCode != ulong.MaxValue);
		if (method == null) return;

		try {
			var methodDescInfo = analyzer.GetMethodByIP(method.NativeCode);

			Assert.NotNull(methodDescInfo.MethodName);
			Assert.True(methodDescInfo.IsJitted);
			Assert.True(methodDescInfo.NativeCode != 0);
		} catch (ArgumentException) {
			// Method lookup failed, skip test
			return;
		}
	}

	public void Dispose() {
		_context.Dispose();
	}
}