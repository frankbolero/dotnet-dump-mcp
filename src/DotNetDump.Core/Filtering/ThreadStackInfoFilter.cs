using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>ThreadAnalyzer.GetDetailedStacks</c> (<c>dumpstack</c> — <em>not</em>
/// <c>clrstack</c>, which calls <c>GetStackTraceGroups</c> and honors no filters). Per
/// DATA_CONTRACT.md &#0167;2.3, <c>Text</c> matches frame <see cref="StackFrameInfo.MethodName"/> —
/// which requires the stack to already be walked, unlike the other three honored fields.
/// </summary>
/// <remarks>
/// Split into two entry points on purpose. <c>GetDetailedStacks</c> deliberately walks a thread's
/// stack only for the page it is about to return — see its doc comment — because the walk is by far
/// the most expensive part and a hung process can carry thousands of threads.
/// <see cref="MatchesThread"/> covers the three fields cheap enough to test straight off a
/// <c>ClrThread</c>, before any walk, so that optimization survives filtering by those fields.
/// <see cref="MatchesFrameText"/> covers <c>Text</c>, which only the caller can apply once frames
/// exist. <see cref="Matches"/> combines both against a fully materialized
/// <see cref="ThreadStackInfo"/>, which is what the unit tests exercise without needing a dump.
/// </remarks>
public static class ThreadStackInfoFilter {
	public const FilterField Honored =
		FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.HasException | FilterField.Text;

	/// <summary>The three fields testable before any stack walk.</summary>
	public static bool MatchesThread(int managedThreadId, uint osThreadId, string? exceptionType, FilterSpec spec) {
		if (spec.ManagedThreadId.HasValue && managedThreadId != spec.ManagedThreadId.Value)
			return false;

		if (spec.OSThreadId.HasValue && osThreadId != spec.OSThreadId.Value)
			return false;

		if (spec.HasException.HasValue && (exceptionType != null) != spec.HasException.Value)
			return false;

		return true;
	}

	/// <summary>The <c>Text</c> field: frame method name, once frames have been walked.</summary>
	public static bool MatchesFrameText(IReadOnlyList<StackFrameInfo> frames, string text) =>
		frames.Any(f => FilterText.ContainsSubstring(f.MethodName, text));

	/// <summary>Every honored field against a fully materialized item.</summary>
	public static bool Matches(ThreadStackInfo item, FilterSpec spec) {
		if (!MatchesThread(item.ManagedThreadId, item.OSThreadId, item.ExceptionType, spec))
			return false;

		if (spec.Text != null && !MatchesFrameText(item.Frames, spec.Text))
			return false;

		return true;
	}
}