using System;

namespace DotNetDump.Core.Caching;

/// <summary>
/// No-op cache: always computes, never stores. This is the default so existing behavior and
/// tests are unchanged until caching is opted into at composition time, and it is what
/// <c>--no-cache</c> selects on the CLI (CLI_DESIGN.md &#0167;6.4, &#0167;6.5).
/// </summary>
public sealed class NullAnalysisCache : IAnalysisCache {
	/// <summary>Shared instance -- the type carries no state, so there is no reason to allocate more than one.</summary>
	public static readonly NullAnalysisCache Instance = new();

	public T GetOrCompute<T>(CacheKey key, Func<T> compute) where T : class => compute();

	public void Invalidate(CacheKey key) { }

	public void ClearDump(DumpIdentity dump) { }
}