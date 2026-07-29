using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// EnsureSupported rejection test for <see cref="ModuleAnalyzer"/>, against
/// <see cref="NoDumpContext"/> -- see the class remarks on
/// <see cref="HeapAnalyzerFilterRejectionTests"/> for why a thrown
/// <see cref="UnsupportedFilterException"/> here proves the ordering in DATA_CONTRACT.md §2.1.
/// </summary>
public class ModuleAnalyzerFilterRejectionTests {
	private readonly ModuleAnalyzer _analyzer = new(new NoDumpContext());

	[Fact]
	public void GetModules_RejectsTypeName_UnhonoredOnClrmodules() {
		var parameters = new QueryParameters { Filter = new FilterSpec { TypeName = "Foo" } };
		Assert.Throws<UnsupportedFilterException>(() => _analyzer.GetModules(parameters));
	}

	[Fact]
	public void UnfilteredCall_StillReachesTheNoDumpGuard_ProvingTheRejectionIsWhatSkippedIt() {
		Assert.Throws<InvalidOperationException>(() => _analyzer.GetModules(new QueryParameters()));
	}
}