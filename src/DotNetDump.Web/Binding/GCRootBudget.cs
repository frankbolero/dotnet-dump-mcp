using System.Globalization;

using Microsoft.AspNetCore.Http;

namespace DotNetDump.Web.Binding;

/// <summary>
/// The one query parameter <c>gcroot</c> takes that no other view does: its traversal budget.
/// </summary>
/// <remarks>
/// <para>
/// Read here rather than in <see cref="ViewRequestBinder"/> because it is not one of
/// DATA_CONTRACT.md &#0167;3.2's filter/sort/page parameters -- it belongs to a single command, and the
/// shared binder deliberately knows only the vocabulary every view shares. It does sit in the same
/// folder and follow the same rules: the value arrives from the address bar, so it is untrusted, and
/// a value this server will not act on is a <c>400</c> that names the reason instead of a silent
/// fallback to the default.
/// </para>
/// <para>
/// Shared by <c>TreeRoutes</c> and <c>DumpRoutes</c>'s JSON arm so the two cannot drift. A truncation
/// warning that an API caller can see but not act on would be half the fix: docs/GCROOT_TRUNCATION.md
/// asks for the budget to be the caller's choice, and SERVER.md &#0167;2 asks the JSON route to bind what
/// the HTML route binds.
/// </para>
/// </remarks>
internal static class GCRootBudget {
	public const string Parameter = "maxNodes";

	/// <summary>Matches <c>HeapAnalyzer.GetGCRoots</c>'s own default, restated where the routes pass
	/// it explicitly alongside a budget.</summary>
	public const int DefaultMaxPaths = 4;

	/// <summary>
	/// Reads the optional budget override. <see langword="null"/> means the parameter was absent and
	/// the analyzer should apply its own precedence (explicit argument, then
	/// <c>DNDUMP_GCROOT_MAX_NODES</c>, then the built-in default) -- which is why absent is not the
	/// same as the default value here. <c>0</c> is meaningful and valid: unlimited.
	/// </summary>
	public static bool TryRead(IQueryCollection query, out int? maxNodes, out string error) {
		maxNodes = null;
		error = "";

		string? raw = query[Parameter].LastOrDefault();
		if (string.IsNullOrWhiteSpace(raw)) {
			return true;
		}

		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0) {
			error = $"'{Parameter}' must be a whole number of at least 0, where 0 means an unlimited traversal budget.";
			return false;
		}

		maxNodes = value;
		return true;
	}
}