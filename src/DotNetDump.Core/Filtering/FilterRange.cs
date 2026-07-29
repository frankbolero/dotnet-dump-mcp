namespace DotNetDump.Core.Filtering;

/// <summary>
/// Inclusive-range matching, shared by every per-model filter predicate that honors
/// <c>Size</c> or <c>Count</c> (DATA_CONTRACT.md &#0167;2.3).
/// </summary>
public static class FilterRange {
	/// <summary>Inclusive range test: <paramref name="value"/> must be &gt;= <paramref name="min"/> and
	/// &lt;= <paramref name="max"/> wherever either bound is set.</summary>
	public static bool InRange(ulong value, ulong? min, ulong? max) =>
		(!min.HasValue || value >= min.Value) && (!max.HasValue || value <= max.Value);

	/// <summary>Inclusive range test over instance counts.</summary>
	public static bool InRange(int value, int? min, int? max) =>
		(!min.HasValue || value >= min.Value) && (!max.HasValue || value <= max.Value);

	/// <summary>
	/// Converts a size stored as <see cref="long"/> (e.g. <c>HeapStatItem.TotalSize</c>) to the
	/// <see cref="ulong"/> that <see cref="Models.FilterSpec.MinSize"/>/<see cref="Models.FilterSpec.MaxSize"/>
	/// use. A size is a sum of object sizes and cannot legitimately be negative; a negative value —
	/// which would itself indicate corruption elsewhere — clamps to zero rather than throwing out of
	/// a filter predicate or silently wrapping via an unchecked cast.
	/// </summary>
	public static ulong ClampToUlong(long value) => value < 0 ? 0UL : (ulong)value;
}