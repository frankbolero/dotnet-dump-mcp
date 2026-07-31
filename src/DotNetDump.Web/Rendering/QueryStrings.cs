using Microsoft.AspNetCore.Http;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// The two query-string operations both <see cref="FilterBar"/> and <see cref="SortHeader"/> need:
/// reading a value the same "present-but-empty means unset" way <c>ViewRequestBinder.Text</c> does,
/// and building a <c>/views/{view}</c> URL from a set of name/value pairs. Kept here rather than
/// duplicated in each so the two components cannot quietly disagree about either.
/// </summary>
internal static class QueryStrings {
	/// <summary>
	/// The raw string a query parameter carries, or <c>null</c> if it is absent or blank. Mirrors
	/// <c>DotNetDump.Web.Binding.ViewRequestBinder.Text</c>'s semantics deliberately: a filter bar
	/// re-rendering the query string it was just handed must treat <c>?type=&amp;text=Http</c> the
	/// same way the binder does, or a round-trip through this page would show a control as "set" to
	/// an empty string the binder itself reads as unset.
	/// </summary>
	public static string? Raw(IQueryCollection query, string name) {
		if (!query.TryGetValue(name, out var values)) {
			return null;
		}

		string? value = values.Count > 0 ? values[^1] : null;
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	/// <summary>
	/// A <c>/views/{view}</c> URL carrying <paramref name="pairs"/> whose value is non-null. Order is
	/// preserved as given, so callers control it rather than this method guessing at one.
	/// </summary>
	public static string BuildUrl(string viewName, IEnumerable<(string Key, string? Value)> pairs) =>
		BuildUrl(viewName, pairs, segment: null);

	/// <summary>
	/// The same URL as <see cref="BuildUrl(string, IEnumerable{(string Key, string? Value)})"/>, with
	/// an extra path segment after the view name -- <c>/views/{view}/{segment}</c> -- for routes that
	/// sit alongside a view rather than being it, e.g. <c>InfiniteScroll.SentinelHref</c>'s
	/// <c>/views/{view}/rows</c>.
	/// </summary>
	public static string BuildUrl(string viewName, IEnumerable<(string Key, string? Value)> pairs, string? segment) {
		string basePath = segment is null ? $"/views/{viewName}" : $"/views/{viewName}/{segment}";
		var kept = pairs.Where(pair => pair.Value != null).ToList();
		if (kept.Count == 0) {
			return basePath;
		}

		string query = string.Join("&", kept.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
		return $"{basePath}?{query}";
	}
}