using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>HeapAnalyzer.GetObjects</c> (<c>listobj</c>). Per the DATA_CONTRACT.md &#0167;2.3
/// matrix, <c>Size</c> here means one object's own <see cref="HeapObjectItem.Size"/> — not the
/// aggregate <c>dumpheap</c> uses — and <c>Text</c> matches <see cref="HeapObjectItem.TypeName"/>
/// only. <c>Generation</c> requires <see cref="HeapObjectItem.Generation"/>, captured during the walk
/// in <c>HeapAnalyzer.ComputeObjects</c>.
/// </summary>
public static class HeapObjectItemFilter {
	public const FilterField Honored = FilterField.AnyTypeName | FilterField.Size | FilterField.Generation | FilterField.Text;

	public static bool Matches(HeapObjectItem item, FilterSpec spec, TypeNameMatcher typeName) {
		if (!typeName.Matches(item.TypeName))
			return false;

		if (!FilterRange.InRange(item.Size, spec.MinSize, spec.MaxSize))
			return false;

		if (spec.Generation.HasValue && item.Generation != spec.Generation.Value)
			return false;

		if (spec.Text != null && !FilterText.ContainsSubstring(item.TypeName, spec.Text))
			return false;

		return true;
	}
}