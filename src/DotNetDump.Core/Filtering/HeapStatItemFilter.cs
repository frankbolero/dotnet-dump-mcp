using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>HeapAnalyzer.GetHeapStatistics</c> (<c>dumpheap</c>). Per the DATA_CONTRACT.md
/// &#0167;2.3 matrix, <c>Size</c> here means the aggregate <see cref="HeapStatItem.TotalSize"/> —
/// every instance of the type summed — not a single object's size, and <c>Text</c> matches
/// <see cref="HeapStatItem.TypeName"/> only.
/// </summary>
public static class HeapStatItemFilter {
	/// <summary>The exact set this method honors — what <see cref="FilterSpec.EnsureSupported"/> is called with.</summary>
	public const FilterField Honored = FilterField.AnyTypeName | FilterField.Size | FilterField.Count | FilterField.Text;

	public static bool Matches(HeapStatItem item, FilterSpec spec, TypeNameMatcher typeName) {
		if (!typeName.Matches(item.TypeName))
			return false;

		ulong totalSize = FilterRange.ClampToUlong(item.TotalSize);
		if (!FilterRange.InRange(totalSize, spec.MinSize, spec.MaxSize))
			return false;

		if (!FilterRange.InRange(item.Count, spec.MinCount, spec.MaxCount))
			return false;

		if (spec.Text != null && !FilterText.ContainsSubstring(item.TypeName, spec.Text))
			return false;

		return true;
	}
}