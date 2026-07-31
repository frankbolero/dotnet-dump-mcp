using System.Globalization;

using DotNetDump.Web.Catalog;

using Microsoft.AspNetCore.Http;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// Builds the infinite-scroll sentinel row's own <c>hx-get</c> target (task 4.4, SERVER.md &#0167;5.1
/// rule 3).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Deliberately not the <see cref="FilterBar"/>/<see cref="SortHeader"/> pattern.</strong>
/// Those two controls split state between their own <c>href</c> (the one value each is responsible
/// for changing) and <c>hx-include="closest form"</c> reading the rest live from the DOM, because
/// each fires from a request that is actively editing its own half of that split -- baking a stale
/// copy of the other half into the href would create two competing sources for it, and if the stale
/// one ever won that race a click would silently stop working. See <see cref="FilterBar"/>'s and
/// <see cref="SortHeader"/>'s own remarks for the full argument.
/// </para>
/// <para>
/// The sentinel is not in that situation. It fires from <c>hx-trigger="revealed"</c>, not a form
/// control, and there is no hidden field carrying <c>sort</c>/<c>order</c> for <c>hx-include</c> to
/// find even if it tried -- only the filter bar's own inputs live in the form. Any filter or sort
/// change swaps the whole <c>#v-{view}</c> fragment via <c>outerHTML</c>, which destroys the old
/// sentinel along with everything else in it, so a sentinel that is still on the page always
/// corresponds exactly to the filter+sort+offset state the *current* fragment was rendered with.
/// There is nothing live in the DOM for it to race against.
/// </para>
/// <para>
/// So <see cref="SentinelHref"/> bakes the complete next request into the sentinel's own
/// <c>hx-get</c>: every query parameter the current request carried (every active filter field,
/// <c>sort</c>, <c>order</c>, whatever else happens to be present), with only <c>offset</c> (the
/// next page) and <c>limit</c> (the page size actually in effect --
/// <see cref="Core.Models.PagedResult{T}.Limit"/>, which is the clamped value the binder used, not
/// necessarily whatever the query string said) overwritten. Getting this wrong -- e.g. relying on
/// <c>hx-include</c> here and letting <c>sort</c>/<c>order</c> silently drop when the user scrolls
/// past the first page of a sorted, filtered view -- is exactly the "looks fine, returns wrong data"
/// bug class IMPLEMENTATION_PLAN.md's risk register names for a sort or page action dropping the
/// active filter, just triggered by scrolling instead of clicking a sort header.
/// </para>
/// </remarks>
public static class InfiniteScroll {
	public static string SentinelHref(ViewDescriptor view, IQueryCollection query, int nextOffset, int limit) {
		var pairs = query.Keys
			.Where(key => !key.Equals("offset", StringComparison.OrdinalIgnoreCase) && !key.Equals("limit", StringComparison.OrdinalIgnoreCase))
			.Select(key => (Key: key, Value: QueryStrings.Raw(query, key)))
			.ToList();

		pairs.Add(("offset", nextOffset.ToString(CultureInfo.InvariantCulture)));
		pairs.Add(("limit", limit.ToString(CultureInfo.InvariantCulture)));

		return QueryStrings.BuildUrl(view.Name, pairs, segment: "rows");
	}
}