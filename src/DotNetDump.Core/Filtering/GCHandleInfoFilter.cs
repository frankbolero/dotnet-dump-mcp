using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>HeapAnalyzer.GetGCHandles</c> (<c>gchandles</c>). Per the DATA_CONTRACT.md
/// &#0167;2.3 matrix, <c>Text</c> matches <see cref="GCHandleInfo.TypeName"/> or
/// <see cref="GCHandleInfo.Kind"/>. <c>Size</c> is deliberately not honored here — see the "gchandles
/// could honor Size but does not" correction in &#0167;2.3: a handle's size is its target's size, and a
/// caller filtering by size almost certainly means <c>listobj</c>.
/// </summary>
public static class GCHandleInfoFilter {
	public const FilterField Honored = FilterField.AnyTypeName | FilterField.Text;

	public static bool Matches(GCHandleInfo item, FilterSpec spec, TypeNameMatcher typeName) {
		if (!typeName.Matches(item.TypeName))
			return false;

		if (spec.Text != null && !FilterText.ContainsInAny(spec.Text, item.TypeName, item.Kind))
			return false;

		return true;
	}
}