using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>HeapAnalyzer.GetSyncBlocks</c> (<c>syncblk</c>). Per the DATA_CONTRACT.md
/// &#0167;2.3 matrix this method honors plain <see cref="FilterField.TypeName"/> only — not the
/// <see cref="FilterField.AnyTypeName"/> composite <c>dumpheap</c>/<c>listobj</c> use, so
/// <see cref="Models.FilterSpec.TypeNameRegex"/> is rejected here rather than silently ignored. No
/// <see cref="TypeNameMatcher"/> is needed as a result: the plain-substring case does not need a
/// compiled regex. <c>Text</c> matches <see cref="SyncBlockInfo.TypeName"/> only.
/// </summary>
public static class SyncBlockInfoFilter {
	public const FilterField Honored = FilterField.TypeName | FilterField.ManagedThreadId | FilterField.Text;

	public static bool Matches(SyncBlockInfo item, FilterSpec spec) {
		if (spec.TypeName != null && !FilterText.ContainsSubstring(item.TypeName, spec.TypeName))
			return false;

		if (spec.ManagedThreadId.HasValue && item.ManagedThreadId != spec.ManagedThreadId.Value)
			return false;

		if (spec.Text != null && !FilterText.ContainsSubstring(item.TypeName, spec.Text))
			return false;

		return true;
	}
}