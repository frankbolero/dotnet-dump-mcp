using System;

namespace DotNetDump.Core.Caching;

/// <summary>
/// Persists derived analysis results (heap statistics, GC root candidates, and similar
/// walk-scale outputs) keyed by dump identity and operation, so a multi-second-to-multi-minute
/// heap walk is paid once rather than on every invocation that asks for it.
/// </summary>
/// <remarks>
/// Analyzers depend on this abstraction, not on a concrete store -- see CLI_DESIGN.md
/// &#0167;6.4. The default implementation is <see cref="NullAnalysisCache"/>, so existing
/// callers and tests see no behavior change until a cache provider is registered explicitly.
/// </remarks>
public interface IAnalysisCache {
	/// <summary>
	/// Returns the cached value for <paramref name="key"/>, or invokes <paramref name="compute"/>,
	/// stores its result and returns it. Implementations must be safe under concurrent access
	/// from multiple threads and, for on-disk providers, multiple processes.
	/// </summary>
	T GetOrCompute<T>(CacheKey key, Func<T> compute) where T : class;

	/// <summary>Removes a single cached entry, if present.</summary>
	void Invalidate(CacheKey key);

	/// <summary>Removes every cached entry associated with a dump identity.</summary>
	void ClearDump(DumpIdentity dump);
}