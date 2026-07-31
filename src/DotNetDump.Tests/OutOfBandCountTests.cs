using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

using Xunit;

namespace DotNetDump.Tests;

/// <summary>
/// Task 4.5: the view header's row count lives outside the swapped fragment
/// (<c>_Layout.cshtml</c>'s <c>#view-count</c>), so it needs an <c>hx-swap-oob="true"</c> element in
/// every response that would otherwise leave it stale -- the htmx-fragment path through
/// <c>RenderView</c> and the infinite-scroll continuation through <c>/views/{view}/rows</c>. Scope is
/// exactly those two paths and the count/summary text; the pagination-footer and cache-state-indicator
/// mentions in the same plan row are deliberately out of scope (see
/// <c>IMPLEMENTATION_PLAN.md</c>'s Phase 4.5 entry and the lead's scope note).
/// </summary>
/// <remarks>
/// Needs a real dump and skips without one: set <c>DOTNETDUMP_TEST_DUMP</c>. Shares
/// <see cref="FilterPreservationHostFixture"/> with <see cref="FilterPreservationTests"/> and
/// <see cref="InfiniteScrollTests"/> rather than loading its own 9+ GB dump a second time.
/// </remarks>
[Collection(FilterPreservationCollection.Name)]
public sealed class OutOfBandCountTests(FilterPreservationHostFixture fixture) {
	private async Task<string> Get(string path) {
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Url + path);
		request.Headers.Add("HX-Request", "true");
		using var response = await client.SendAsync(request);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		return await response.Content.ReadAsStringAsync();
	}

	/// <summary>The single out-of-band count element, asserting there is exactly one.</summary>
	private static string CountOobElement(string html) {
		var matches = Regex.Matches(html, "<div id=\"view-count\"[^>]*>([^<]*)</div>");
		Assert.True(matches.Count == 1,
			$"Expected exactly one #view-count OOB element, found {matches.Count}. Response was:\n{html}");
		return matches[0].Value;
	}

	[SkippableFact]
	public async Task FilteredFragment_CarriesCountOob_MatchingTheFilteredTotal() {
		fixture.SkipIfUnavailable();

		string body = await Get("/views/dumpheap?type=Http&limit=5");

		string oob = CountOobElement(body);
		Assert.Contains("hx-swap-oob=\"true\"", oob, StringComparison.Ordinal);
		Assert.Contains("class=\"dn-view-count\"", oob, StringComparison.Ordinal);

		// Asserting the exact number would duplicate the dump-content-dependent arithmetic
		// FilterPreservationTests already exercises; what's specific to this task is the shape --
		// ListModel<T>.CountSummary always produces "N rows" or "N of M rows" -- and that it landed
		// inside the OOB element rather than nowhere at all.
		string text = Regex.Match(oob, ">([^<]*)</div>").Groups[1].Value;
		Assert.Matches(new Regex(@"^[\d,]+( of [\d,]+)? rows$"), text);
	}

	[SkippableFact]
	public async Task SortedFragment_CarriesCountOob_WithId() {
		fixture.SkipIfUnavailable();

		string body = await Get("/views/dumpheap?sort=count&order=asc&limit=5");

		string oob = CountOobElement(body);
		Assert.Contains("id=\"view-count\"", oob, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task RowsRoute_CarriesTheSameCountOob_AsTheInitialFragment() {
		fixture.SkipIfUnavailable();

		string firstPage = await Get("/views/dumpheap?type=Http&limit=5");
		string firstOob = CountOobElement(firstPage);

		string secondPage = await Get("/views/dumpheap/rows?type=Http&limit=5&offset=5");
		string secondOob = CountOobElement(secondPage);

		// TotalAvailable/TotalUnfiltered describe the full filtered result, independent of
		// Offset/Limit (PagedResult.cs), so under a fixed filter the honest count reads identically
		// on every page -- the point of this test is that /rows carries the element at all (it did
		// not, before 4.5), not that the number changes with the scroll offset.
		Assert.Equal(firstOob, secondOob);

		// Rows-only response: none of the full fragment's scaffolding is repeated, and the OOB div is
		// appended after the row/sentinel markup, not nested inside it.
		Assert.DoesNotContain("<table", secondPage, StringComparison.Ordinal);
		Assert.DoesNotContain("id=\"v-dumpheap\"", secondPage, StringComparison.Ordinal);
		Assert.EndsWith("</div>", secondPage.TrimEnd(), StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task DetailFragment_CarriesNoCountOob() {
		fixture.SkipIfUnavailable();

		// 'info' is a ViewKind.Detail view -- BuildFragment passes CountSummary: null for it, so
		// there is nothing honest to swap in and AppendCountOob must append nothing.
		string body = await Get("/views/info");

		Assert.DoesNotContain("id=\"view-count\"", body, StringComparison.Ordinal);
		Assert.DoesNotContain("hx-swap-oob", body, StringComparison.Ordinal);
	}
}