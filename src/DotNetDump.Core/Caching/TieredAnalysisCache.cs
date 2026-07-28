using System;
using System.Collections.Generic;

namespace DotNetDump.Core.Caching;

/// <summary>
/// Composes cache providers in order -- typically memory, then disk, then compute -- promoting
/// hits upward (CLI_DESIGN.md &#0167;6.4). A disk hit is transparently written into the memory
/// tier the next time the same process asks for it, without the caller doing anything special.
/// </summary>
/// <remarks>
/// Promotion works by nesting each tier's <c>compute</c> delegate inside the next: tier 0 is
/// asked to get-or-compute, and its "compute" is "ask tier 1 to get-or-compute", and so on until
/// the last tier's "compute" is the caller's real <paramref name="compute"/> delegate. A value
/// found at tier <em>N</em> therefore flows back up through every outer tier's
/// <see cref="IAnalysisCache.GetOrCompute{T}"/>, which stores it exactly as if it had computed
/// that value itself.
/// </remarks>
public sealed class TieredAnalysisCache : IAnalysisCache {
	private readonly IReadOnlyList<IAnalysisCache> _tiers;

	public TieredAnalysisCache(params IAnalysisCache[] tiers) {
		if (tiers == null || tiers.Length == 0)
			throw new ArgumentException("At least one tier is required.", nameof(tiers));

		_tiers = tiers;
	}

	public T GetOrCompute<T>(CacheKey key, Func<T> compute) where T : class => GetOrCompute(0, key, compute);

	private T GetOrCompute<T>(int tierIndex, CacheKey key, Func<T> compute) where T : class {
		if (tierIndex == _tiers.Count - 1)
			return _tiers[tierIndex].GetOrCompute(key, compute);

		return _tiers[tierIndex].GetOrCompute(key, () => GetOrCompute(tierIndex + 1, key, compute));
	}

	public void Invalidate(CacheKey key) {
		foreach (IAnalysisCache tier in _tiers)
			tier.Invalidate(key);
	}

	public void ClearDump(DumpIdentity dump) {
		foreach (IAnalysisCache tier in _tiers)
			tier.ClearDump(dump);
	}
}