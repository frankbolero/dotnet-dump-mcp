using DotNetDump.Core.Models;
using DotNetDump.Web.Catalog;

using Microsoft.AspNetCore.Http;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// A sortable <c>&lt;th&gt;</c>'s three pieces: where clicking it goes, its current
/// <c>aria-sort</c> state (IMPLEMENTATION_PLAN.md's three-state requirement -- unsorted,
/// ascending, descending), and a small glyph for sighted users who are not reading the attribute.
/// </summary>
public sealed record SortHeaderInfo(string Href, string AriaSort, string Indicator);

/// <summary>
/// Builds one sortable column header against the query string already on the request.
/// </summary>
/// <remarks>
/// <para>
/// Only call <see cref="For"/> for a column the backing analyzer has a dedicated <c>SortBy</c>
/// string for -- see each analyzer method's own sort switch (e.g.
/// <c>HeapAnalyzer.GetHeapStatistics</c> only recognizes <c>"count"</c> and <c>"typename"</c>;
/// anything else, including no value at all, falls back to its default column). A column with no
/// dedicated key does not get a sortable header, per DATA_CONTRACT.md &#0167;2.3 and
/// IMPLEMENTATION_PLAN.md's Phase 4.3 -- inventing a sort string the analyzer does not recognize
/// would silently no-op instead of sorting.
/// </para>
/// <para>
/// <see cref="SortHeaderInfo.Href"/> deliberately carries only <c>sort</c>, <c>order</c> and the
/// current <c>limit</c> (never the filter fields) -- the header's own <c>hx-include="closest
/// form"</c> is what resubmits whatever the filter bar currently holds, live from the DOM, so
/// baking a filter snapshot into the href here would create two competing sources for the same
/// query parameter on the one request that has both.
/// </para>
/// </remarks>
public static class SortHeader {
	public static SortHeaderInfo For(ViewDescriptor view, IQueryCollection query, string sortKey, SortDirection defaultDirection) {
		string? currentSort = QueryStrings.Raw(query, "sort");
		bool isActive = currentSort != null && currentSort.Equals(sortKey, StringComparison.OrdinalIgnoreCase);

		SortDirection currentDirection = isActive ? CurrentDirection(query) : defaultDirection;
		SortDirection nextDirection = isActive ? Flip(currentDirection) : defaultDirection;

		var pairs = new (string, string?)[] {
			("sort", sortKey),
			("order", nextDirection == SortDirection.Asc ? "asc" : "desc"),
			("limit", QueryStrings.Raw(query, "limit")),
		};

		string ariaSort = isActive ? (currentDirection == SortDirection.Asc ? "ascending" : "descending") : "none";
		string indicator = isActive ? (currentDirection == SortDirection.Asc ? " ▲" : " ▼") : string.Empty;

		return new SortHeaderInfo(QueryStrings.BuildUrl(view.Name, pairs), ariaSort, indicator);
	}

	/// <summary>
	/// The direction a currently-active sort is in, matching
	/// <c>ViewRequestBinder.Order</c>'s own default (absent or unrecognized reads as descending --
	/// though an unrecognized value would have already 400'd before a fragment ever rendered).
	/// </summary>
	private static SortDirection CurrentDirection(IQueryCollection query) {
		string? order = QueryStrings.Raw(query, "order");
		return order != null && order.Equals("asc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Asc : SortDirection.Desc;
	}

	private static SortDirection Flip(SortDirection direction) => direction == SortDirection.Asc ? SortDirection.Desc : SortDirection.Asc;
}