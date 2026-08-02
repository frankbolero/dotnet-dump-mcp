using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

using Xunit;

namespace DotNetDump.Tests;

/// <summary>
/// <c>GET /views/{view}/rows</c> (task 4.4): the infinite-scroll sentinel's own target.
/// </summary>
/// <remarks>
/// <para>
/// Needs a real dump and skips without one: set <c>DOTNETDUMP_TEST_DUMP</c>. Shares
/// <see cref="FilterPreservationHostFixture"/> with <see cref="FilterPreservationTests"/> rather
/// than loading its own 9+ GB dump a second time in the suite -- nothing here mutates it either, so
/// there is no reason to pay for a second cold walk.
/// </para>
/// <para>
/// <see cref="RowsRoute_PreservesActiveFilterAndSort_AcrossThePageBoundary"/> is this task's answer
/// to IMPLEMENTATION_PLAN.md's risk register entry ("a sort or page action silently drops the active
/// filter"), specifically for the form the plan text does not spell out: the sentinel's own
/// <c>hx-get</c> has to bake in <c>type</c>/<c>sort</c>/<c>order</c> explicitly, because there is no
/// <c>hx-include="closest form"</c> and no hidden field for one to read even if there were. If that
/// wiring ever regressed to relying on one, this test would come back either unfiltered (wrong type)
/// or unsorted (wrong order) instead of both holding across the page boundary.
/// </para>
/// </remarks>
[Collection(FilterPreservationCollection.Name)]
public sealed class InfiniteScrollTests(FilterPreservationHostFixture fixture) {
	private async Task<(HttpStatusCode Status, string Body)> Get(string path) {
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Url + path);
		request.Headers.Add("HX-Request", "true");
		using var response = await client.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	/// <summary>
	/// Every MethodTable address cell's text, in document order -- one per row. Matches both the
	/// plain <c>&lt;span&gt;</c> an address with nothing to link to falls back to and the
	/// <c>&lt;a&gt;</c> <c>Display.AddrLink</c> renders for a non-zero one -- dumpheap's MethodTable
	/// is never zero, but the pattern should not silently start matching nothing if that ever changed.
	/// </summary>
	private static List<string> MethodTableAddresses(string html) =>
		Regex.Matches(html, "dn-td--mono\"><(?:span|a) class=\"dn-addr\"[^>]*>([^<]+)<")
			.Select(m => m.Groups[1].Value)
			.ToList();

	[SkippableFact]
	public async Task FirstPage_CarriesASentinel_PointingAtTheNextOffsetAndLimit() {
		fixture.SkipIfUnavailable();

		var (status, body) = await Get("/views/dumpheap?limit=5");
		Assert.Equal(HttpStatusCode.OK, status);

		// SERVER.md §5.1's markup, verbatim: intersect once/outerHTML/this, not the sort/filter
		// controls' own hx-target="#v-{view}". Not "revealed" -- see _DumpHeapRows.cshtml's doc
		// comment: this app's tables scroll inside .dn-view-pad, not the window, and "revealed" is
		// driven entirely by window-level scroll/resize, so it never fired from real scrolling here.
		// Not "afterend" either -- that inserts the response as a new sibling without removing the
		// sentinel itself, leaving a permanent "Loading…" row behind after every page; outerHTML
		// replaces the sentinel with the response (new rows plus, if HasMore, one fresh sentinel).
		Assert.Contains("hx-trigger=\"intersect once\"", body, StringComparison.Ordinal);
		Assert.Contains("hx-swap=\"outerHTML\"", body, StringComparison.Ordinal);
		Assert.Contains("hx-target=\"this\"", body, StringComparison.Ordinal);

		var match = Regex.Match(body, "hx-get=\"(/views/dumpheap/rows\\?[^\"]*)\"");
		Assert.True(match.Success, "No sentinel hx-get found in: " + body);
		Assert.Contains("offset=5", match.Groups[1].Value, StringComparison.Ordinal);
		Assert.Contains("limit=5", match.Groups[1].Value, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task RowsRoute_ReturnsTheNextPage_DisjointFromTheFirst() {
		fixture.SkipIfUnavailable();

		var (_, firstPage) = await Get("/views/dumpheap?limit=5&sort=typename&order=asc");
		var firstAddresses = MethodTableAddresses(firstPage);
		Assert.Equal(5, firstAddresses.Count);

		var (status, secondPage) = await Get("/views/dumpheap/rows?limit=5&offset=5&sort=typename&order=asc");
		Assert.Equal(HttpStatusCode.OK, status);

		// Rows-only: none of the table scaffolding the full fragment carries is repeated.
		Assert.DoesNotContain("<table", secondPage, StringComparison.Ordinal);
		Assert.DoesNotContain("<thead", secondPage, StringComparison.Ordinal);
		Assert.DoesNotContain("id=\"v-dumpheap\"", secondPage, StringComparison.Ordinal);

		var secondAddresses = MethodTableAddresses(secondPage);
		Assert.Equal(5, secondAddresses.Count);
		Assert.Empty(firstAddresses.Intersect(secondAddresses));
	}

	[SkippableFact]
	public async Task RowsRoute_PreservesActiveFilterAndSort_AcrossThePageBoundary() {
		fixture.SkipIfUnavailable();

		var (status, body) = await Get("/views/dumpheap/rows?type=Http&sort=count&order=asc&limit=500&offset=0");
		Assert.Equal(HttpStatusCode.OK, status);

		var typeCells = Regex.Matches(body, "dn-td--type\"[^>]*>.*?</td>", RegexOptions.Singleline);
		Assert.NotEmpty(typeCells);
		foreach (Match cell in typeCells) {
			Assert.Contains("Http", cell.Value, StringComparison.Ordinal);
		}

		var counts = Regex.Matches(body, "dn-td--num\">([\\d,]+)</td>")
			.Select(m => long.Parse(m.Groups[1].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture))
			.ToList();
		Assert.True(counts.Count >= 2, "Expected at least two rows to check ordering against, got " + counts.Count);
		for (int i = 1; i < counts.Count; i++) {
			Assert.True(counts[i] >= counts[i - 1],
				$"Row {i} (count={counts[i]}) is less than row {i - 1} (count={counts[i - 1]}) under order=asc.");
		}
	}

	[SkippableFact]
	public async Task RowsRoute_EmitsNoSentinel_WhenOffsetIsPastTheEnd() {
		fixture.SkipIfUnavailable();

		// An offset no real heap-stat row count will ever reach, so HasMore is false regardless of
		// this dump's actual type count -- deterministic without depending on dump content.
		var (status, body) = await Get("/views/dumpheap/rows?offset=999999999&limit=5");
		Assert.Equal(HttpStatusCode.OK, status);

		Assert.DoesNotContain("hx-trigger=\"intersect once\"", body, StringComparison.Ordinal);
		Assert.DoesNotContain("dn-tr", body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task RowsRoute_Is400_ForADetailView() {
		fixture.SkipIfUnavailable();

		// dumpstack is ViewKind.Detail despite being filterable/ListModel-bound (IMPLEMENTATION_PLAN.md's
		// Phase 4 scope note) -- it never gets a rows partial, so the route must refuse it rather than
		// 404 (it exists) or 500 (a missing partial would throw).
		var (status, _) = await Get("/views/dumpstack/rows");
		Assert.Equal(HttpStatusCode.BadRequest, status);
	}

	[SkippableFact]
	public async Task RowsRoute_Is404_ForAnUnknownView() {
		fixture.SkipIfUnavailable();

		var (status, _) = await Get("/views/nosuchview/rows");
		Assert.Equal(HttpStatusCode.NotFound, status);
	}
}