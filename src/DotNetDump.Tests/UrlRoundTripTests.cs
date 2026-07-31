using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

using Xunit;

namespace DotNetDump.Tests;

/// <summary>
/// Task 4.6: URL state round-trip. Every existing test that exercises a filtered/sorted URL
/// (<see cref="FilterPreservationTests"/>, <see cref="InfiniteScrollTests"/>,
/// <see cref="OutOfBandCountTests"/>) sends <c>HX-Request: true</c> and only ever checks the bare
/// fragment. None of them prove the thing this task's exit criterion actually needs: that a fresh,
/// full-page <c>GET</c> with no <c>HX-Request</c> header at all -- exactly what happens when a user
/// pastes a URL into a new tab, or when a browser restores history on back/forward -- reconstructs
/// the identical visible state: the filter bar's inputs pre-populated with the right values, the
/// right chips present, the right sort header showing the right <c>aria-sort</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this is the right proxy for "the back button undoes a filter change."</strong> A real
/// back/forward click is client-side browser/htmx behavior this suite has no way to drive -- there is
/// no browser automation in this project. But a back/forward navigation is, from the server's point
/// of view, indistinguishable from "request the previous URL again": the browser (or htmx's history
/// cache, via <c>HX-History-Restore-Request</c> -- see <c>DumpRoutes.WantsFragment</c>'s remarks)
/// re-issues a plain <c>GET</c> for the query string that was on the address bar before the filter
/// change happened. So the credible server-side property is exactly what these tests assert: two
/// different query strings, each requested fresh with no <c>HX-Request</c> header, independently and
/// deterministically produce two different, correct rendered states. If that always holds, back and
/// forward are correct by construction, because pressing them cannot produce a request this suite
/// has not already covered.
/// </para>
/// <para>
/// <c>RenderShell</c>/<c>BuildFragment</c> read <c>Context.Request.Query</c> the same way regardless
/// of whether <c>HX-Request</c> is present, and <c>FilterBar.Build</c>/<c>SortHeader.For</c> are the
/// same calls the htmx-fragment path already exercises -- so this was expected to already work. These
/// tests are what turns "expected to work" into "verified", per the risk register's warning about
/// exactly this kind of silent gap.
/// </para>
/// <para>
/// Needs a real dump and skips without one: set <c>DOTNETDUMP_TEST_DUMP</c>. Shares
/// <see cref="FilterPreservationHostFixture"/> with the other Phase 4 test classes rather than
/// loading its own 9+ GB dump a second time.
/// </para>
/// </remarks>
[Collection(FilterPreservationCollection.Name)]
public sealed class UrlRoundTripTests(FilterPreservationHostFixture fixture) {
	private async Task<(HttpStatusCode Status, string Body)> GetFullPage(string path) {
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Url + path);
		// Deliberately no HX-Request header at all -- a genuine fresh load, not an htmx swap
		// mid-session and not a history restore either. This is the one request shape none of
		// FilterPreservationTests/InfiniteScrollTests/OutOfBandCountTests send.
		using var response = await client.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	/// <summary>
	/// The smallest <c>&lt;...&gt;</c> tag containing <paramref name="needle"/>, order-independent of
	/// attribute placement. Mirrors <c>FilterPreservationTests.TagContaining</c>.
	/// </summary>
	private static string TagContaining(string html, string needle) {
		foreach (Match tag in Regex.Matches(html, "<[^>]+>", RegexOptions.Singleline)) {
			if (tag.Value.Contains(needle, StringComparison.Ordinal)) {
				return tag.Value;
			}
		}

		Assert.Fail($"No tag in the response contains '{needle}'. Response was:\n{html}");
		return string.Empty; // unreachable
	}

	/// <summary>
	/// The smallest <c>&lt;tagName ...&gt;...&lt;/tagName&gt;</c> block whose contents contain
	/// <paramref name="needle"/>. Mirrors <c>FilterPreservationTests.ElementContaining</c> -- used for
	/// <c>aria-sort</c>, which belongs on the <c>&lt;th&gt;</c> itself, one level up from the sort
	/// <c>&lt;a&gt;</c> whose <c>hx-get</c> carries the sort key.
	/// </summary>
	private static string ElementContaining(string html, string tagName, string needle) {
		foreach (Match element in Regex.Matches(html, $"<{tagName}\\b[^>]*>.*?</{tagName}>", RegexOptions.Singleline)) {
			if (element.Value.Contains(needle, StringComparison.Ordinal)) {
				return element.Value;
			}
		}

		Assert.Fail($"No <{tagName}> element in the response contains '{needle}'. Response was:\n{html}");
		return string.Empty; // unreachable
	}

	/// <summary>
	/// Whether a chip labeled exactly <paramref name="label"/> is present -- i.e. an
	/// <c>&lt;a class="dn-chip" ...&gt;</c> whose text starts with it (the chip's own <c>&times;</c>
	/// glyph follows in a nested <c>&lt;span&gt;</c>, per <c>_FilterBar.cshtml</c>).
	/// </summary>
	private static bool HasChip(string html, string label) =>
		Regex.IsMatch(html, "<a class=\"dn-chip\"[^>]*>\\s*" + Regex.Escape(label), RegexOptions.Singleline);

	[SkippableFact]
	public async Task FullPageLoad_ReconstructsFilterChipAndSortState_ForATextFilteredView() {
		fixture.SkipIfUnavailable();

		// dumpheap: a Text-kind filter control (type) plus a Range-kind neighbor field, exercising
		// the plain <input> code path for control values.
		var (status, body) = await GetFullPage("/views/dumpheap?type=Http&sort=count&order=asc&limit=25");
		Assert.Equal(HttpStatusCode.OK, status);

		// It's a whole page, not a bare fragment -- same shape ViewRoutingTests/WiredViewRoutingTests
		// already check for the unfiltered case.
		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-dumpheap\"", body, StringComparison.Ordinal);

		// The filter bar's own input carries the active value.
		string typeInput = TagContaining(body, "name=\"type\"");
		Assert.Contains("value=\"Http\"", typeInput, StringComparison.Ordinal);

		// The sort header shows the active sort and direction.
		string countHeader = ElementContaining(body, "th", "sort=count");
		Assert.Contains("aria-sort=\"ascending\"", countHeader, StringComparison.Ordinal);

		// The active filter is represented as a chip.
		Assert.True(HasChip(body, "type: Http"), "Expected a 'type: Http' chip in:\n" + body);
	}

	[SkippableFact]
	public async Task FullPageLoad_ReconstructsSelectFilterAndSortState_ForClrThreads() {
		fixture.SkipIfUnavailable();

		// clrthreads: FilterControlKind.Select (hasException) is a different code path from the
		// text/number/range inputs dumpheap exercises above -- a <select>'s selected <option> renders
		// only if FilterBar.Build read the same query string on this cold, full-page path as it does
		// on the htmx-fragment path the other Phase 4 tests already cover.
		var (status, body) = await GetFullPage("/views/clrthreads?hasException=true&sort=exception&order=asc&limit=25");
		Assert.Equal(HttpStatusCode.OK, status);

		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-clrthreads\"", body, StringComparison.Ordinal);

		// The "Yes" <option> is the one marked selected; every other <option> in that same <select>
		// carries no selected attribute at all (_FilterBar.cshtml's conditional Razor attribute omits
		// it entirely rather than emitting selected="false").
		string select = ElementContaining(body, "select", "name=\"hasException\"");
		Assert.Contains("<option value=\"true\" selected=\"selected\">Yes</option>", select, StringComparison.Ordinal);
		Assert.DoesNotContain("<option value=\"false\" selected=\"selected\">No</option>", select, StringComparison.Ordinal);
		Assert.DoesNotContain("<option value=\"\" selected=\"selected\">Any</option>", select, StringComparison.Ordinal);

		string exceptionHeader = ElementContaining(body, "th", "sort=exception");
		Assert.Contains("aria-sort=\"ascending\"", exceptionHeader, StringComparison.Ordinal);

		Assert.True(HasChip(body, "exception: yes"), "Expected an 'exception: yes' chip in:\n" + body);
	}
}