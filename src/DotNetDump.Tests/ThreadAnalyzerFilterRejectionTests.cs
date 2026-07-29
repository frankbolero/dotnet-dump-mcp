using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// EnsureSupported rejection tests for <see cref="ThreadAnalyzer"/>, against
/// <see cref="NoDumpContext"/> -- see the class remarks on
/// <see cref="HeapAnalyzerFilterRejectionTests"/> for why a thrown
/// <see cref="UnsupportedFilterException"/> here proves the ordering in DATA_CONTRACT.md §2.1.
/// </summary>
public class ThreadAnalyzerFilterRejectionTests {
	private readonly ThreadAnalyzer _analyzer = new(new NoDumpContext());

	[Fact]
	public void GetThreads_RejectsTypeName_ThreadInfoHasNoTypeNameColumn() {
		var parameters = new QueryParameters { Filter = new FilterSpec { TypeName = "Foo" } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetThreads(parameters));
	}

	[Fact]
	public void GetThreadStates_RejectsMinSize_UnhonoredOnThreadstate() {
		var parameters = new QueryParameters { Filter = new FilterSpec { MinSize = 1 } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetThreadStates(parameters));
	}

	[Fact]
	public void GetDetailedStacks_RejectsModule_UnhonoredOnDumpstack() {
		var parameters = new QueryParameters { Filter = new FilterSpec { Module = "Foo.dll" } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetDetailedStacks(parameters));
	}

	[Fact]
	public void GetThreadExceptions_RejectsGeneration_UnhonoredOnPrintexception() {
		var parameters = new QueryParameters { Filter = new FilterSpec { Generation = GenerationFilter.Gen0 } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetThreadExceptions(parameters));
	}

	[Fact]
	public void UnfilteredCalls_StillReachTheNoDumpGuard_ProvingTheRejectionIsWhatSkippedIt() {
		var parameters = new QueryParameters();
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetThreads(parameters));
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetThreadStates(parameters));
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetDetailedStacks(parameters));
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetThreadExceptions(parameters));
	}
}