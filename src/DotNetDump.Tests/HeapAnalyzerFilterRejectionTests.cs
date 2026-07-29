using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// Exercises DATA_CONTRACT.md §2.1's ordering guarantee -- EnsureSupported runs before any cache
/// lookup or dump access -- without a dump. <see cref="NoDumpContext"/> throws
/// <see cref="InvalidOperationException"/> the moment any analyzer method tries to touch the heap or
/// runtime, so a passing <see cref="UnsupportedFilterException"/> here proves the rejection happened
/// first, not that the method simply degraded gracefully with nothing loaded.
/// </summary>
public class HeapAnalyzerFilterRejectionTests {
	private readonly HeapAnalyzer _analyzer = new(new NoDumpContext());

	[Fact]
	public void GetHeapStatistics_RejectsGeneration_UnhonoredOnDumpheap() {
		var parameters = new QueryParameters { Filter = new FilterSpec { Generation = GenerationFilter.Gen2 } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetHeapStatistics(parameters));
	}

	[Fact]
	public void GetObjects_RejectsMinCount_UnhonoredOnListobj() {
		var parameters = new QueryParameters { Filter = new FilterSpec { MinCount = 5 } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetObjects(parameters));
	}

	[Fact]
	public void GetGCHandles_RejectsMinSize_TargetIsForListobjNotGchandles() {
		// DATA_CONTRACT.md §2.3: gchandles could honor Size but deliberately does not -- a handle's
		// size is its target's size, which is what listobj is for.
		var parameters = new QueryParameters { Filter = new FilterSpec { MinSize = 1024 } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetGCHandles(parameters));
	}

	[Fact]
	public void GetSyncBlocks_RejectsTypeNameRegex_OnlyPlainTypeNameIsHonored() {
		var parameters = new QueryParameters { Filter = new FilterSpec { TypeNameRegex = "^Lock" } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetSyncBlocks(parameters));
	}

	[Fact]
	public void GetHeapExceptions_RejectsManagedThreadId_HeapScanHasNoOwningThread() {
		// DATA_CONTRACT.md §2.3: the heap-scan path carries no owning thread, so ManagedThreadId is
		// unsupported here even though the in-flight path (ThreadAnalyzer.GetThreadExceptions) honors it.
		var parameters = new QueryParameters { Filter = new FilterSpec { ManagedThreadId = 1 } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetHeapExceptions(parameters));
	}

	[Fact]
	public void GetGCRoots_RejectsAnyFilter_HonorsNone() {
		var parameters = new QueryParameters { Filter = new FilterSpec { TypeName = "Foo" } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetGCRoots(0x1000, parameters));
	}

	[Fact]
	public void VerifyHeap_RejectsAnyFilter_HonorsNone() {
		var parameters = new QueryParameters { Filter = new FilterSpec { TypeName = "Foo" } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.VerifyHeap(parameters));
	}

	[Fact]
	public void UnfilteredCalls_StillReachTheNoDumpGuard_ProvingTheRejectionIsWhatSkippedIt() {
		// Sanity check for the tests above: with FilterSpec.None, every one of these methods should
		// get past EnsureSupported and fail on the absent dump instead -- confirming the
		// UnsupportedFilterException assertions above are testing the right thing.
		var parameters = new QueryParameters();
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetHeapStatistics(parameters));
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetObjects(parameters));
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetSyncBlocks(parameters));
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetHeapExceptions(parameters));
		Assert.Throws<InvalidOperationException>(() => _analyzer.VerifyHeap(parameters));
	}
}