using DotNetDump.Core.Models;

namespace DotNetDump.Core;

/// <summary>
/// Thrown when a <see cref="FilterSpec"/> carries a field the target analyzer method does not honor.
/// </summary>
/// <remarks>
/// Deliberately not a silent no-op. A user who filters <c>clrmodules</c> by <c>size&gt;1gb</c> and
/// receives the unfiltered list back has been handed a wrong answer with nothing to indicate it
/// (DATA_CONTRACT.md §2.3). The CLI surfaces this as a usage error; the web UI does not render
/// controls for fields a view does not honor, so it exists there to protect direct API callers.
/// </remarks>
public sealed class UnsupportedFilterException : Exception {
	/// <summary>The fields that were rejected — set on the spec, absent from the honored set.</summary>
	public FilterField UnsupportedFields { get; }

	/// <summary>The fields the target does honor, for an actionable message.</summary>
	public FilterField SupportedFields { get; }

	public UnsupportedFilterException(string target, FilterField unsupported, FilterField supported)
		: base(BuildMessage(target, unsupported, supported)) {
		UnsupportedFields = unsupported;
		SupportedFields = supported;
	}

	private static string BuildMessage(string target, FilterField unsupported, FilterField supported) {
		string rejected = Describe(unsupported);
		return supported == FilterField.None
			? $"'{target}' does not support filtering. Remove the filter on {rejected}."
			: $"'{target}' does not support filtering on {rejected}. Supported: {Describe(supported)}.";
	}

	/// <summary>
	/// Names the individual flags, so a composite like <see cref="FilterField.Size"/> reads as its
	/// parts rather than as an alias the user never typed.
	/// </summary>
	private static string Describe(FilterField fields) {
		var names = new List<string>();
		foreach (FilterField candidate in Enum.GetValues<FilterField>()) {
			if (candidate == FilterField.None) continue;
			// Skip the composite aliases; only single-bit flags are user-facing field names.
			if ((candidate & (candidate - 1)) != 0) continue;
			if (fields.HasFlag(candidate)) names.Add(candidate.ToString());
		}

		return names.Count == 0 ? "(none)" : string.Join(", ", names);
	}
}