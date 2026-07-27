using DotNetDump.Core.Caching;

namespace DotNetDump.Tests;

public class TieredAnalysisCacheTests : IDisposable {
	private readonly List<string> _roots = new();

	public void Dispose() {
		foreach (string root in _roots) {
			try {
				if (Directory.Exists(root))
					Directory.Delete(root, recursive: true);
			} catch (IOException) { }
		}
	}

	private FileSystemAnalysisCache NewDiskCache() {
		string root = Path.Combine(Path.GetTempPath(), "dndump-tiered-tests-" + Guid.NewGuid().ToString("N"));
		_roots.Add(root);
		return new FileSystemAnalysisCache(root);
	}

	private static CacheKey MakeKey() => new(DumpIdentity.FromComponents("dump-a"), "heap-statistics", "args", 1);

	[Fact]
	public void GetOrCompute_ComputesOnceAndServesFromMemoryAfterward() {
		var memory = new MemoryAnalysisCache();
		var tiered = new TieredAnalysisCache(memory, NewDiskCache());
		var key = MakeKey();
		int calls = 0;

		string Compute() {
			calls++;
			return "value";
		}

		string first = tiered.GetOrCompute(key, Compute);
		string second = tiered.GetOrCompute(key, Compute);

		Assert.Equal("value", first);
		Assert.Equal("value", second);
		Assert.Equal(1, calls);
	}

	[Fact]
	public void ADiskHit_IsPromotedIntoTheMemoryTier() {
		var diskCache = NewDiskCache();
		var key = MakeKey();

		// Warm the disk tier directly, bypassing memory entirely -- as a prior process would have.
		diskCache.GetOrCompute(key, () => "from-disk");

		var memory = new MemoryAnalysisCache();
		var tiered = new TieredAnalysisCache(memory, diskCache);

		string value = tiered.GetOrCompute(key, () => "should-not-be-called");
		Assert.Equal("from-disk", value);

		// The memory tier must now hold the promoted value directly -- asking it alone, with a
		// compute delegate that would fail the test if invoked, must still return the disk value.
		string fromMemoryAlone = memory.GetOrCompute<string>(key, () => throw new InvalidOperationException("should not recompute"));
		Assert.Equal("from-disk", fromMemoryAlone);
	}

	[Fact]
	public void Invalidate_ClearsEveryTier() {
		var memory = new MemoryAnalysisCache();
		var tiered = new TieredAnalysisCache(memory, NewDiskCache());
		var key = MakeKey();

		tiered.GetOrCompute(key, () => "value");
		tiered.Invalidate(key);

		int calls = 0;
		tiered.GetOrCompute(key, () => { calls++; return "recomputed"; });

		Assert.Equal(1, calls);
	}

	[Fact]
	public void ClearDump_ClearsEveryTier() {
		var memory = new MemoryAnalysisCache();
		var tiered = new TieredAnalysisCache(memory, NewDiskCache());
		var key = MakeKey();

		tiered.GetOrCompute(key, () => "value");
		tiered.ClearDump(key.Dump);

		int calls = 0;
		tiered.GetOrCompute(key, () => { calls++; return "recomputed"; });

		Assert.Equal(1, calls);
	}

	[Fact]
	public void Constructor_RequiresAtLeastOneTier() {
		Assert.Throws<ArgumentException>(() => new TieredAnalysisCache());
	}
}