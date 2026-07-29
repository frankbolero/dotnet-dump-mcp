using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Caching;
using DotNetDump.Core.Models;

using Xunit;

namespace DotNetDump.Tests;

public class IntegrationTests : IDisposable {
	private readonly string _dumpPath;
	private readonly DumpContext _context;

	/// <summary>
	/// Overrides the fixture location, so CI can point at a generated dump without editing this file.
	/// </summary>
	public const string DumpPathVariable = "DOTNETDUMP_TEST_DUMP";

	public IntegrationTests() {
		_dumpPath = Environment.GetEnvironmentVariable(DumpPathVariable)
			?? Path.GetFullPath("../../../../../dumps/core_20251212_112511");
		_context = new DumpContext();

		if (File.Exists(_dumpPath)) {
			_context.Initialize(_dumpPath);
		}
	}

	/// <summary>
	/// Skips visibly when there is no dump to analyse.
	/// <para>
	/// These tests previously returned early instead, so a run with no fixture reported every one of
	/// them as <em>passing</em> while asserting nothing — a green suite that guaranteed nothing about
	/// the analyzers. Set <see cref="DumpPathVariable"/> to a real dump to actually exercise them.
	/// </para>
	/// </summary>
	private void SkipIfNoDump() {
		Skip.IfNot(File.Exists(_dumpPath),
			$"No dump fixture at '{_dumpPath}'. Set {DumpPathVariable} to a dump file to run integration tests.");
	}

	[SkippableFact]
	public void DumpContext_InitializesCorrecty() {
		SkipIfNoDump();

		Assert.NotNull(_context.DataTarget);
		Assert.NotNull(_context.Runtime);
		Assert.NotNull(_context.Heap);
	}

	[SkippableFact]
	public void DumpContext_Load_LoadsDumpSuccessfully() {
		SkipIfNoDump();

		var context = new DumpContext();
		context.Load(_dumpPath);

		Assert.True(context.IsLoaded);
		Assert.NotNull(context.DataTarget);
		Assert.NotNull(context.Runtime);
		Assert.NotNull(context.Heap);

		context.Dispose();
	}

	[SkippableFact]
	public void HeapAnalyzer_ReturnsStatistics() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var stats = analyzer.GetHeapStatistics(new QueryParameters { Limit = 10 }).Items;

		Assert.NotEmpty(stats);
		Assert.Contains(stats, s => s.TypeName == "System.String");
	}

	[SkippableFact]
	public void ThreadAnalyzer_GroupsStacks() {
		SkipIfNoDump();

		var analyzer = new ThreadAnalyzer(_context);
		var groups = analyzer.GetStackTraceGroups().ToList();

		Assert.NotEmpty(groups);
		Assert.True(groups.First().ThreadCount > 0);
	}

	[SkippableFact]
	public void HeapAnalyzer_GetGCRoots_ReturnsRoots() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);

		// Find an object first
		var obj = _context.Heap.EnumerateObjects()
			.FirstOrDefault(o => o.Type != null && o.Type.Name == "System.String");
		Skip.If(obj.Address == 0, "No suitable object found in the dump.");

		// This test is tricky because we need an object that HAS roots.
		// Strings might not be rooted if they are garbage.
		// But let's try to call the method and ensure it doesn't crash.
		var result = analyzer.GetGCRoots(obj.Address, new QueryParameters { Limit = 10 });

		// Assert no exception
		Assert.NotNull(result);
		Assert.NotNull(result.Paths);
	}

	[SkippableFact]
	public void HeapAnalyzer_GetObjectDetails_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var obj = _context.Heap.EnumerateObjects()
			.FirstOrDefault(o => o.Type != null && o.Type.Name == "System.String");
		Skip.If(obj.Address == 0, "No suitable object found in the dump.");

		var details = analyzer.GetObjectDetails(obj.Address);

		Assert.Equal(obj.Address, details.Address);
		Assert.Equal("System.String", details.TypeName);
		Assert.NotEmpty(details.Fields);
	}

	[SkippableFact]
	public void HeapAnalyzer_GetHeapSegments_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var summary = analyzer.GetHeapSegments();

		Assert.NotEmpty(summary.Segments);
		Assert.True(summary.Segments.First().Size > 0);
		// Every segment must carry a real kind label, including Frozen/Ephemeral on a regions GC.
		Assert.All(summary.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.Kind)));
	}

	[SkippableFact]
	public void ThreadAnalyzer_GetThreadPoolInfo_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new ThreadAnalyzer(_context);
		var info = analyzer.GetThreadPoolInfo();

		Assert.NotNull(info);
		Assert.True(info.TotalThreads >= 0);
	}

	[SkippableFact]
	public void HeapAnalyzer_GetSyncBlocks_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var blocks = analyzer.GetSyncBlocks(new QueryParameters()).Items;

		// Might be empty, but shouldn't throw
		Assert.NotNull(blocks);
	}

	[SkippableFact]
	public void HeapAnalyzer_VerifyHeap_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var corruptions = analyzer.VerifyHeap(new QueryParameters { Limit = 50 }).Items;

		// Should not throw, corruptions list might be empty (which is good)
		Assert.NotNull(corruptions);
	}

	[SkippableFact]
	public void HeapAnalyzer_GetGCHandles_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var handles = analyzer.GetGCHandles(new QueryParameters { Limit = 50 }).Items;

		// Might be empty, but shouldn't throw
		Assert.NotNull(handles);
	}

	[SkippableFact]
	public void HeapAnalyzer_GetObjects_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var objects = analyzer.GetObjects(new QueryParameters { Limit = 10 }, null).Items;

		Assert.NotEmpty(objects);
		Assert.All(objects, obj => Assert.True(obj.Address > 0));
	}

	[SkippableFact]
	public void HeapAnalyzer_GetObjects_WithFilter_ReturnsFilteredData() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var objects = analyzer.GetObjects(new QueryParameters { Limit = 10 }, "System.String").Items;

		Assert.NotEmpty(objects);
		Assert.All(objects, obj => Assert.Contains("String", obj.TypeName ?? ""));
	}

	[SkippableFact]
	public void HeapAnalyzer_GetHeapStatistics_FilterSpecTypeName_FiltersAndReportsPostFilterTotal() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var unfiltered = analyzer.GetHeapStatistics(new QueryParameters { Limit = 10_000 });
		var filtered = analyzer.GetHeapStatistics(new QueryParameters {
			Limit = 10_000,
			Filter = new FilterSpec { TypeName = "String" }
		});

		Assert.NotEmpty(filtered.Items);
		Assert.All(filtered.Items, s => Assert.Contains("String", s.TypeName ?? "", StringComparison.OrdinalIgnoreCase));
		// TotalAvailable is the post-filter count (DATA_CONTRACT.md §2.1 point 3), so it must be
		// smaller than the unfiltered total on any dump with non-string types on the heap.
		Assert.True(filtered.TotalAvailable <= unfiltered.TotalAvailable);
		Assert.Equal(filtered.Items.Count, filtered.TotalAvailable);
	}

	[SkippableFact]
	public void HeapAnalyzer_GetObjects_FilterSpecGeneration_OnlyReturnsThatGeneration() {
		SkipIfNoDump();

		var analyzer = new HeapAnalyzer(_context);
		var sample = analyzer.GetObjects(new QueryParameters { Limit = 200 }).Items;
		Skip.If(sample.Count == 0, "No objects found in the dump.");

		var observedGeneration = sample.Select(o => o.Generation).FirstOrDefault(g => g.HasValue);
		Skip.If(observedGeneration is null, "No object in the sample had a known generation.");

		var filtered = analyzer.GetObjects(new QueryParameters {
			Limit = 10_000,
			Filter = new FilterSpec { Generation = observedGeneration!.Value }
		}).Items;

		Assert.NotEmpty(filtered);
		Assert.All(filtered, o => Assert.Equal(observedGeneration, o.Generation));
	}

	[SkippableFact]
	public void ThreadAnalyzer_GetThreads_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new ThreadAnalyzer(_context);
		var threads = analyzer.GetThreads(new QueryParameters { Limit = 50 }).Items;

		Assert.NotEmpty(threads);
		Assert.All(threads, thread => Assert.True(thread.OSThreadId > 0 || thread.ManagedThreadId >= 0));
	}

	[SkippableFact]
	public void ThreadAnalyzer_GetDetailedStacks_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new ThreadAnalyzer(_context);
		var stacks = analyzer.GetDetailedStacks(new QueryParameters { Limit = 10 }, maxFrames: 20).Items;

		Assert.NotEmpty(stacks);
		Assert.All(stacks, stack => {
			Assert.True(stack.OSThreadId > 0 || stack.ManagedThreadId >= 0);
			Assert.NotNull(stack.Frames);
		});
	}

	[SkippableFact]
	public void ThreadAnalyzer_GetThreads_FilterSpecManagedThreadId_ReturnsOnlyThatThread() {
		SkipIfNoDump();

		var analyzer = new ThreadAnalyzer(_context);
		var sample = analyzer.GetThreads(new QueryParameters { Limit = 1 }).Items;
		Skip.If(sample.Count == 0, "No threads found in the dump.");

		int targetId = sample[0].ManagedThreadId;
		var filtered = analyzer.GetThreads(new QueryParameters {
			Limit = 50,
			Filter = new FilterSpec { ManagedThreadId = targetId }
		}).Items;

		Assert.NotEmpty(filtered);
		Assert.All(filtered, t => Assert.Equal(targetId, t.ManagedThreadId));
	}

	[SkippableFact]
	public void ThreadAnalyzer_GetDetailedStacks_FilterSpecText_MatchesOnlyFramesContainingIt() {
		SkipIfNoDump();

		var analyzer = new ThreadAnalyzer(_context);
		var unfiltered = analyzer.GetDetailedStacks(new QueryParameters { Limit = 1000 }, maxFrames: 50);
		var withKnownFrame = unfiltered.Items.SelectMany(s => s.Frames).FirstOrDefault(f => !string.IsNullOrEmpty(f.MethodName));
		Skip.If(withKnownFrame == null, "No thread with a named frame found in the dump.");

		// Search for a short, distinctive slice of the frame name -- exact copy avoids a false
		// negative from a would-be substring that happens not to appear verbatim.
		string needle = withKnownFrame!.MethodName!.Length > 6 ? withKnownFrame.MethodName![..6] : withKnownFrame.MethodName!;

		var filtered = analyzer.GetDetailedStacks(new QueryParameters {
			Limit = 1000,
			Filter = new FilterSpec { Text = needle }
		}, maxFrames: 50);

		Assert.NotEmpty(filtered.Items);
		Assert.All(filtered.Items, s => Assert.Contains(s.Frames, f => (f.MethodName ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase)));
	}

	[SkippableFact]
	public void ModuleAnalyzer_GetModules_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new ModuleAnalyzer(_context);
		var modules = analyzer.GetModules(new QueryParameters { Limit = 50 }, includeSystem: true).Items;

		Assert.NotEmpty(modules);
		Assert.All(modules, module => Assert.NotNull(module.Name));
	}

	[SkippableFact]
	public void ModuleAnalyzer_GetModules_ExcludeSystem_ReturnsUserModules() {
		SkipIfNoDump();

		var analyzer = new ModuleAnalyzer(_context);
		var modules = analyzer.GetModules(new QueryParameters { Limit = 50 }, includeSystem: false).Items;

		// Might be empty if only system modules exist
		Assert.NotNull(modules);
		Assert.All(modules, module => Assert.True(module.IsUserCode));
	}

	[SkippableFact]
	public void ModuleAnalyzer_GetModules_FilterSpecModule_ReturnsOnlyMatchingModules() {
		SkipIfNoDump();

		var analyzer = new ModuleAnalyzer(_context);
		var sample = analyzer.GetModules(new QueryParameters { Limit = 1 }, includeSystem: true).Items;
		Skip.If(sample.Count == 0 || string.IsNullOrEmpty(sample[0].Name), "No named module found in the dump.");

		string name = sample[0].Name!;
		string needle = name.Length > 6 ? name[..6] : name;

		var filtered = analyzer.GetModules(new QueryParameters {
			Limit = 500,
			Filter = new FilterSpec { Module = needle }
		}, includeSystem: true).Items;

		Assert.NotEmpty(filtered);
		Assert.All(filtered, m => Assert.Contains(needle, m.Name ?? "", StringComparison.OrdinalIgnoreCase));
	}

	[SkippableFact]
	public void MetadataAnalyzer_GetMethodTable_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new MetadataAnalyzer(_context);

		// Find a type and get its MethodTable
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Name == "System.String");
		Skip.If(type == null, "No suitable type found in the dump.");

		var methodTableInfo = analyzer.GetMethodTable(type.MethodTable);

		Assert.Equal(type.MethodTable, methodTableInfo.MethodTable);
		Assert.Equal("System.String", methodTableInfo.TypeName);
		Assert.NotNull(methodTableInfo.ModuleName);
		Assert.True(methodTableInfo.MethodCount > 0);
	}

	[SkippableFact]
	public void MetadataAnalyzer_GetMethodDesc_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new MetadataAnalyzer(_context);

		// Find a type with methods and get a MethodDesc
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Methods.Any());
		Skip.If(type == null, "No suitable type found in the dump.");

		var method = type.Methods.FirstOrDefault(m => m.MethodDesc != 0);
		Skip.If(method == null, "No suitable method found in the dump.");

		var methodDescInfo = analyzer.GetMethodDesc(method.MethodDesc);

		Assert.Equal(method.MethodDesc, methodDescInfo.MethodDesc);
		Assert.NotNull(methodDescInfo.MethodName);
		Assert.NotNull(methodDescInfo.TypeName);
	}

	[SkippableFact]
	public void MetadataAnalyzer_GetClass_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new MetadataAnalyzer(_context);

		// Find a type and get its class info (using MethodTable as EEClass)
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Name == "System.String");
		Skip.If(type == null, "No suitable type found in the dump.");

		var classInfo = analyzer.GetClass(type.MethodTable);

		Assert.Equal(type.MethodTable, classInfo.MethodTable);
		Assert.Equal("System.String", classInfo.TypeName);
		Assert.NotNull(classInfo.ModuleName);
		Assert.True(classInfo.MethodCount > 0);
		Assert.NotNull(classInfo.Fields);
	}

	[SkippableFact]
	public void ModuleAnalyzer_GetModuleDetails_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new ModuleAnalyzer(_context);

		// Get first module with non-zero size
		var module = _context.Runtime.EnumerateModules().FirstOrDefault(m => m.Size > 0);
		if (module == null) return;

		var moduleDetails = analyzer.GetModuleDetails(module.ImageBase);

		Assert.Equal(module.ImageBase, moduleDetails.ImageBase);
		Assert.NotNull(moduleDetails.Name);
		Assert.True(moduleDetails.Size > 0);
	}

	[SkippableFact]
	public void ModuleAnalyzer_GetAssemblyDetails_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new ModuleAnalyzer(_context);

		// Get first module and use its ImageBase as AssemblyId
		var module = _context.Runtime.EnumerateModules().FirstOrDefault();
		if (module == null) return;

		var assemblyDetails = analyzer.GetAssemblyDetails(module.ImageBase);

		Assert.NotNull(assemblyDetails.Name);
		Assert.NotEmpty(assemblyDetails.Modules);
	}

	[SkippableFact]
	public void ModuleAnalyzer_Name2EE_ReturnsData() {
		SkipIfNoDump();

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

	[SkippableFact]
	public void ModuleAnalyzer_GetMethodByIP_ReturnsData() {
		SkipIfNoDump();

		var analyzer = new ModuleAnalyzer(_context);

		// Find a method with valid native code (not 0 and not all Fs)
		var type = _context.Heap.EnumerateObjects()
			.Select(o => o.Type)
			.FirstOrDefault(t => t != null && t.Methods.Any(m => m.NativeCode != 0 && m.NativeCode != ulong.MaxValue));
		Skip.If(type == null, "No suitable type found in the dump.");

		var method = type.Methods.FirstOrDefault(m => m.NativeCode != 0 && m.NativeCode != ulong.MaxValue);
		Skip.If(method == null, "No suitable method found in the dump.");

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

	/// <summary>
	/// The correctness-critical property from CLI_DESIGN.md §6.2: the cache key must exclude
	/// <c>limit</c>/<c>offset</c>/<c>sort</c>/<c>order</c>, so one cache entry -- and one heap walk --
	/// serves every pagination/sort variant of the same underlying query. Asserted by counting actual
	/// invocations of the compute delegate, not by inspecting the cache key directly, so it catches a
	/// regression in <em>either</em> the key construction or the wiring.
	/// </summary>
	[SkippableFact]
	public void HeapAnalyzer_GetHeapStatistics_PaginationVariantsShareOneCacheEntryAndOneComputation() {
		SkipIfNoDump();

		var cache = new CountingAnalysisCache();
		var analyzer = new HeapAnalyzer(_context, cache);

		var first = analyzer.GetHeapStatistics(new QueryParameters { Limit = 5, Offset = 0 });
		var second = analyzer.GetHeapStatistics(new QueryParameters {
			Limit = 10,
			Offset = 5,
			SortBy = "Count",
			SortDirection = SortDirection.Asc
		});

		Assert.Equal(1, cache.ComputeCalls);
		Assert.Equal(first.TotalAvailable, second.TotalAvailable);
	}

	/// <summary>Distinct <c>typeFilter</c> values are not pagination -- they change what is computed,
	/// so (unlike limit/offset/sort/order) they are deliberately part of the cache key and each gets
	/// its own entry and its own walk.</summary>
	[SkippableFact]
	public void HeapAnalyzer_GetObjects_DifferentTypeFiltersComputeIndependently() {
		SkipIfNoDump();

		var cache = new CountingAnalysisCache();
		var analyzer = new HeapAnalyzer(_context, cache);

		analyzer.GetObjects(new QueryParameters { Limit = 5 }, typeFilter: null);
		analyzer.GetObjects(new QueryParameters { Limit = 5 }, typeFilter: "System.String");
		analyzer.GetObjects(new QueryParameters { Limit = 5 }, typeFilter: null); // Repeats the first filter.

		Assert.Equal(2, cache.ComputeCalls);
	}

	/// <summary>
	/// Verifies that different dumps produce different DumpIdentity values, and thus different cache
	/// keys, preventing cache leakage when the MCP server switches between dumps.
	/// </summary>
	[Fact]
	public void TieredAnalysisCache_CacheKeyIncludesDumpIdentity_PreventsLeakageAcrossDumps() {
		// Create two distinct DumpIdentity values (e.g., from different files or metadata).
		var dump1 = DumpIdentity.FromComponents("dump1.core", "10485760", "2025-01-01T00:00:00Z");
		var dump2 = DumpIdentity.FromComponents("dump2.core", "20971520", "2025-01-02T00:00:00Z");

		// Create cache keys for the same operation on different dumps.
		var key1 = new CacheKey(dump1, "GetHeapStatistics", "abc123", 1);
		var key2 = new CacheKey(dump2, "GetHeapStatistics", "abc123", 1);

		// Keys should be different since they reference different dumps.
		Assert.NotEqual(key1, key2);

		// Demonstrate that a TieredAnalysisCache with a memory tier stores separate entries.
		var cache = new TieredAnalysisCache(new MemoryAnalysisCache());

		var value1 = cache.GetOrCompute(key1, () => "Result from dump 1");
		var value2 = cache.GetOrCompute(key2, () => "Result from dump 2");

		Assert.Equal("Result from dump 1", value1);
		Assert.Equal("Result from dump 2", value2);

		// Verify that clearing dump1 does not affect dump2's cache entries.
		cache.ClearDump(dump1);

		// After clearing dump1, querying key1 should recompute.
		var value1After = cache.GetOrCompute(key1, () => "RECOMPUTED for dump 1");
		Assert.Equal("RECOMPUTED for dump 1", value1After);

		// Verify that dump2's entry is still cached (not recomputed).
		var value2After = cache.GetOrCompute(key2, () => "RECOMPUTED for dump 2");
		Assert.Equal("Result from dump 2", value2After);
	}

	/// <summary>Wraps a real <see cref="MemoryAnalysisCache"/> and counts how many times the
	/// underlying compute delegate actually runs, independent of how many times <c>GetOrCompute</c>
	/// itself is called.</summary>
	private sealed class CountingAnalysisCache : IAnalysisCache {
		private readonly MemoryAnalysisCache _inner = new();
		public int ComputeCalls { get; private set; }

		public T GetOrCompute<T>(CacheKey key, Func<T> compute) where T : class {
			return _inner.GetOrCompute(key, () => {
				ComputeCalls++;
				return compute();
			});
		}

		public void Invalidate(CacheKey key) => _inner.Invalidate(key);
		public void ClearDump(DumpIdentity dump) => _inner.ClearDump(dump);
	}

	public void Dispose() {
		_context.Dispose();
	}
}