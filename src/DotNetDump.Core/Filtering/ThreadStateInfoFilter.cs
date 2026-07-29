using System.Linq;

using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>ThreadAnalyzer.GetThreadStates</c> (<c>threadstate</c>). Per DATA_CONTRACT.md
/// &#0167;2.3, <c>Text</c> matches <see cref="ThreadStateInfo.ExceptionType"/> or any entry of
/// <see cref="ThreadStateInfo.StateFlags"/> — deliberately different from <c>clrthreads</c>, which
/// has no state flags to search.
/// </summary>
public static class ThreadStateInfoFilter {
	public const FilterField Honored =
		FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.HasException | FilterField.Text;

	public static bool Matches(ThreadStateInfo item, FilterSpec spec) {
		if (spec.ManagedThreadId.HasValue && item.ManagedThreadId != spec.ManagedThreadId.Value)
			return false;

		if (spec.OSThreadId.HasValue && item.OSThreadId != spec.OSThreadId.Value)
			return false;

		if (spec.HasException.HasValue && (item.ExceptionType != null) != spec.HasException.Value)
			return false;

		if (spec.Text != null) {
			bool matchesText = FilterText.ContainsSubstring(item.ExceptionType, spec.Text)
				|| item.StateFlags.Any(flag => FilterText.ContainsSubstring(flag, spec.Text));
			if (!matchesText)
				return false;
		}

		return true;
	}
}