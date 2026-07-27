using DotNetDump.Core.Caching;

namespace DotNetDump.Tests;

public class FileSystemAnalysisCacheTests : IDisposable {
	private readonly string _root;

	public FileSystemAnalysisCacheTests() {
		_root = Path.Combine(Path.GetTempPath(), "dndump-cache-tests-" + Guid.NewGuid().ToString("N"));
	}

	public void Dispose() {
		try {
			if (Directory.Exists(_root))
				Directory.Delete(_root, recursive: true);
		} catch (IOException) { }
	}

	private FileSystemAnalysisCache NewCache() => new(_root);

	private static CacheKey MakeKey(string operation = "heap-statistics") =>
		new(DumpIdentity.FromComponents("dump-a"), operation, "args", 1);

	[Fact]
	public void GetOrCompute_MissesOnFirstCallAndHitsOnSubsequentCalls() {
		var cache = NewCache();
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
	public void GetOrCompute_PersistsAcrossSeparateCacheInstances() {
		var key = MakeKey();

		string first = NewCache().GetOrCompute(key, () => "persisted");

		// A brand-new instance pointed at the same root simulates a second process reading what
		// the first one wrote -- this is the property that makes the cache shared, not per-run.
		int calls = 0;
		string second = NewCache().GetOrCompute(key, () => { calls++; return "recomputed"; });

		Assert.Equal("persisted", first);
		Assert.Equal("persisted", second);
		Assert.Equal(0, calls);
	}

	[Fact]
	public void Invalidate_RemovesTheOnDiskEntry() {
		var cache = NewCache();
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
	public void ClearDump_RemovesEveryEntryForThatDumpButNotOthers() {
		var cache = NewCache();
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

		Assert.Equal(1, callsA);
		Assert.Equal(0, callsB);
	}

	[Fact]
	public void GetOrCompute_NeverLeavesATempOrLockFileBehindOnSuccess() {
		var cache = NewCache();
		var key = MakeKey();

		cache.GetOrCompute(key, () => "value");

		var leftovers = Directory.GetFiles(cache.Root, "*", SearchOption.AllDirectories)
			.Where(f => f.EndsWith(".tmp", StringComparison.Ordinal) || f.EndsWith(".lock", StringComparison.Ordinal))
			.ToList();

		Assert.Empty(leftovers);
	}

	[Fact]
	public async Task GetOrCompute_ConcurrentWriters_ComputeExactlyOnceAndAgreeOnTheResult() {
		// The scenario the advisory lock exists for (CLI_DESIGN.md &#0167;6.5): two writers racing
		// on a cold cache must not both perform the walk. One computes; the other waits on the
		// lock and then reads the first one's result.
		var cache = NewCache();
		var key = MakeKey();
		int calls = 0;
		var ready = new CountdownEvent(8);
		var go = new ManualResetEventSlim(false);

		string Compute() {
			Interlocked.Increment(ref calls);
			Thread.Sleep(200);
			return "computed-once";
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
		Assert.All(results, r => Assert.Equal("computed-once", r));
	}

	[Fact]
	public async Task GetOrCompute_ConcurrentWritersAcrossSeparateInstances_StillComputeOnce() {
		// Same as above, but with a distinct FileSystemAnalysisCache instance per "writer" and no
		// shared in-memory state -- the part of the guarantee that must hold across processes,
		// not just across threads of one process.
		var key = MakeKey();
		int calls = 0;
		var ready = new CountdownEvent(4);
		var go = new ManualResetEventSlim(false);

		string Compute() {
			Interlocked.Increment(ref calls);
			Thread.Sleep(200);
			return "computed-once";
		}

		var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => {
			var cache = NewCache();
			ready.Signal();
			go.Wait();
			return cache.GetOrCompute(key, Compute);
		})).ToArray();

		ready.Wait(TimeSpan.FromSeconds(5));
		go.Set();
		string[] results = await Task.WhenAll(tasks);

		Assert.Equal(1, calls);
		Assert.All(results, r => Assert.Equal("computed-once", r));
	}

	[Fact]
	public void ResolveDefaultRoot_HonorsTheCacheRootEnvironmentVariable() {
		string? previous = Environment.GetEnvironmentVariable(FileSystemAnalysisCache.CacheRootVariable);
		try {
			Environment.SetEnvironmentVariable(FileSystemAnalysisCache.CacheRootVariable, "/tmp/example-dndump-cache");
			Assert.Equal("/tmp/example-dndump-cache", FileSystemAnalysisCache.ResolveDefaultRoot());
		} finally {
			Environment.SetEnvironmentVariable(FileSystemAnalysisCache.CacheRootVariable, previous);
		}
	}

	[Fact]
	public void Prune_DeletesOldestEntriesUntilUnderTheSizeCap() {
		var cache = NewCache();

		for (int i = 0; i < 5; i++) {
			var key = new CacheKey(DumpIdentity.FromComponents("dump-a"), "heap-statistics", $"args-{i}", 1);
			cache.GetOrCompute(key, () => new string('x', 1000));
			Thread.Sleep(10); // ensure distinct LastWriteTimeUtc ordering
		}

		long before = Directory.GetFiles(cache.Root, "*.json", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
		Assert.True(before > 0);

		cache.Prune(maxTotalBytes: 0);

		var remaining = Directory.GetFiles(cache.Root, "*.json", SearchOption.AllDirectories);
		Assert.Empty(remaining);
	}
}