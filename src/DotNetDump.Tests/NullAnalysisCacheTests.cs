using DotNetDump.Core.Caching;

namespace DotNetDump.Tests;

public class NullAnalysisCacheTests {
	private static CacheKey MakeKey() => new(DumpIdentity.FromComponents("dump-a"), "heap-statistics", "args", 1);

	[Fact]
	public void GetOrCompute_AlwaysInvokesCompute() {
		var cache = NullAnalysisCache.Instance;
		var key = MakeKey();
		int calls = 0;

		string Compute() {
			calls++;
			return "value";
		}

		cache.GetOrCompute(key, Compute);
		cache.GetOrCompute(key, Compute);
		cache.GetOrCompute(key, Compute);

		Assert.Equal(3, calls);
	}

	[Fact]
	public void Invalidate_AndClearDump_AreHarmlessNoOps() {
		var cache = new NullAnalysisCache();
		var key = MakeKey();

		var invalidateEx = Record.Exception(() => cache.Invalidate(key));
		var clearDumpEx = Record.Exception(() => cache.ClearDump(key.Dump));

		Assert.Null(invalidateEx);
		Assert.Null(clearDumpEx);
	}
}