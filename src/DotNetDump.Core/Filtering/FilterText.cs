namespace DotNetDump.Core.Filtering;

/// <summary>
/// Case-insensitive substring matching, shared by every per-model filter predicate
/// (DATA_CONTRACT.md &#0167;2.3). Callers only invoke these once the corresponding
/// <see cref="Models.FilterSpec"/> field is known to be set — these helpers take the needle as a
/// non-null <see cref="string"/> rather than encoding "not filtering" as a null needle themselves.
/// </summary>
public static class FilterText {
	/// <summary>True if <paramref name="haystack"/> contains <paramref name="needle"/>, ignoring case.
	/// A null haystack never matches — there is nothing there to find the needle in.</summary>
	public static bool ContainsSubstring(string? haystack, string needle) =>
		haystack != null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// True if <paramref name="needle"/> is found in any of <paramref name="columns"/> — the OR
	/// across text columns that the <c>Text</c> field uses (DATA_CONTRACT.md &#0167;2.3: "ORed across
	/// those columns"). The set of columns is specific to each analyzer method's matrix row.
	/// </summary>
	public static bool ContainsInAny(string needle, params string?[] columns) {
		foreach (string? column in columns) {
			if (ContainsSubstring(column, needle))
				return true;
		}

		return false;
	}
}