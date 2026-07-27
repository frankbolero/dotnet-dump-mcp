using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DotNetDump.Core.Caching;

/// <summary>
/// Process-local cache backed by a dictionary. Serves the MCP server within a session (each
/// tool call is a fresh analyzer instance, but the cache outlives them) and makes analyzer tests
/// fast without touching disk (CLI_DESIGN.md &#0167;6.4).
/// </summary>
/// <remarks>
/// Concurrent calls for the *same* key are coalesced onto a single <see cref="Lazy{T}"/> so the
/// underlying walk runs exactly once, mirroring the cross-process guarantee
/// <see cref="FileSystemAnalysisCache"/> makes with an advisory lock. If <c>compute</c> throws,
/// the entry is evicted so a later call gets a fresh attempt instead of a permanently poisoned
/// cache slot.
/// </remarks>
public sealed class MemoryAnalysisCache : IAnalysisCache {
	private readonly ConcurrentDictionary<CacheKey, Lazy<object>> _entries = new();

	public T GetOrCompute<T>(CacheKey key, Func<T> compute) where T : class {
		var lazy = _entries.GetOrAdd(key, static (_, factory) => new Lazy<object>(() => factory(), LazyThreadSafetyMode.ExecutionAndPublication), (Func<object>)compute);

		try {
			return (T)lazy.Value;
		} catch {
			// Do not let a transient failure permanently poison the slot for this key.
			_entries.TryRemove(new KeyValuePair<CacheKey, Lazy<object>>(key, lazy));
			throw;
		}
	}

	public void Invalidate(CacheKey key) => _entries.TryRemove(key, out _);

	public void ClearDump(DumpIdentity dump) {
		foreach (var key in _entries.Keys) {
			if (key.Dump.Equals(dump))
				_entries.TryRemove(key, out _);
		}
	}
}