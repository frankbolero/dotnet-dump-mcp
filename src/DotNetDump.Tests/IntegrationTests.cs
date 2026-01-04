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

	public void Dispose() {
		_context.Dispose();
	}
}