using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>ThreadAnalyzer.GetThreads</c> (<c>clrthreads</c>). Per DATA_CONTRACT.md
/// &#0167;2.3, <c>Text</c> matches <see cref="ThreadInfo.ExceptionType"/> only.
/// </summary>
public static class ThreadInfoFilter {
	public const FilterField Honored =
		FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.HasException | FilterField.Text;

	public static bool Matches(ThreadInfo item, FilterSpec spec) {
		if (spec.ManagedThreadId.HasValue && item.ManagedThreadId != spec.ManagedThreadId.Value)
			return false;

		if (spec.OSThreadId.HasValue && item.OSThreadId != spec.OSThreadId.Value)
			return false;

		if (spec.HasException.HasValue && (item.ExceptionType != null) != spec.HasException.Value)
			return false;

		if (spec.Text != null && !FilterText.ContainsSubstring(item.ExceptionType, spec.Text))
			return false;

		return true;
	}
}