using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>ModuleAnalyzer.GetModules</c> (<c>clrmodules</c>). Per DATA_CONTRACT.md
/// &#0167;2.3, <c>Size</c> here means the module's image size, and <c>Module</c>/<c>Text</c> both
/// match <see cref="ModuleInfo.Name"/> — <see cref="ModuleInfo"/> carries no separate assembly-name
/// field to distinguish them.
/// </summary>
public static class ModuleInfoFilter {
	public const FilterField Honored = FilterField.Module | FilterField.Size | FilterField.Text;

	public static bool Matches(ModuleInfo item, FilterSpec spec) {
		if (spec.Module != null && !FilterText.ContainsSubstring(item.Name, spec.Module))
			return false;

		if (!FilterRange.InRange(item.Size, spec.MinSize, spec.MaxSize))
			return false;

		if (spec.Text != null && !FilterText.ContainsSubstring(item.Name, spec.Text))
			return false;

		return true;
	}
}