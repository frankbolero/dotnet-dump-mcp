using DotNetDump.Core.Caching;

namespace DotNetDump.Tests;

public class MemoryAnalysisCacheTests {
	private static CacheKey MakeKey(string operation = "heap-statistics") =>
		new(DumpIdentity.FromComponents("dump-a"), operation, "args", 1);

	[Fact]
	public void GetOrCompute_ComputesOnceAndCachesTheResult() {
		var cache = new MemoryAnalysisCache();
		var key = MakeKey();
		int calls = 0;

		string Compute() {
			calls++;
			return "value";
		}

		string first = cache.GetOrCompute(key, Compute);
		string second = cache.GetOrCompute(key, Compute);

		Assert.Equal("value", first);
		Assert.Equal("value", second);
		Assert.Equal(1, calls);
	}

	[Fact]
	public void GetOrCompute_DifferentKeys_ComputeIndependently() {
		var cache = new MemoryAnalysisCache();
		var keyA = MakeKey("heap-statistics");
		var keyB = MakeKey("gcroot");

		string a = cache.GetOrCompute(keyA, () => "a-value");
		string b = cache.GetOrCompute(keyB, () => "b-value");

		Assert.Equal("a-value", a);
		Assert.Equal("b-value", b);
	}

	[Fact]
	public void Invalidate_ForcesRecomputeOnNextCall() {
		var cache = new MemoryAnalysisCache();
		var key = MakeKey();
		int calls = 0;

		string Compute() {
			calls++;
			return "value-" + calls;
		}

		string first = cache.GetOrCompute(key, Compute);
		cache.Invalidate(key);
		string second = cache.GetOrCompute(key, Compute);

		Assert.Equal(2, calls);
		Assert.NotEqual(first, second);
	}

	[Fact]
	public void ClearDump_RemovesOnlyEntriesForThatDump() {
		var cache = new MemoryAnalysisCache();
		var dumpA = DumpIdentity.FromComponents("dump-a");
		var dumpB = DumpIdentity.FromComponents("dump-b");
		var keyA = new CacheKey(dumpA, "heap-statistics", "args", 1);
		var keyB = new CacheKey(dumpB, "heap-statistics", "args", 1);

		cache.GetOrCompute(keyA, () => "a");
		cache.GetOrCompute(keyB, () => "b");

		cache.ClearDump(dumpA);

		int callsA = 0, callsB = 0;
		cache.GetOrCompute(keyA, () => { callsA++; return "a2"; });
		cache.GetOrCompute(keyB, () => { callsB++; return "b2"; });

		Assert.Equal(1, callsA); // Cleared -- recomputed.
		Assert.Equal(0, callsB); // Untouched -- still cached.
	}

	[Fact]
	public async Task GetOrCompute_ConcurrentCallsForTheSameKey_ComputeExactlyOnce() {
		var cache = new MemoryAnalysisCache();
		var key = MakeKey();
		int calls = 0;
		var ready = new CountdownEvent(8);
		var go = new ManualResetEventSlim(false);

		string Compute() {
			Interlocked.Increment(ref calls);
			// Hold long enough that every other thread piles up waiting, so the test exercises
			// coalescing rather than winning a race by finishing before anyone else starts.
			Thread.Sleep(200);
			return "value";
		}

		var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
			ready.Signal();
			go.Wait();
			return cache.GetOrCompute(key, Compute);
		})).ToArray();

		ready.Wait(TimeSpan.FromSeconds(5));
		go.Set();
		string[] results = await Task.WhenAll(tasks);

		Assert.Equal(1, calls);
		Assert.All(results, r => Assert.Equal("value", r));
	}
}