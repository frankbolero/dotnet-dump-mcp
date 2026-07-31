using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;

using DotNetDump.Core;
using DotNetDump.Core.Caching;
using DotNetDump.Web;

using Xunit;

namespace DotNetDump.Tests;

/// <summary>
/// The oracle for Phase 4's named highest-risk bug (IMPLEMENTATION_PLAN.md's risk register): a sort
/// or page action silently dropping the active filter, which returns wrong data that looks right.
/// Written by the lead before 4.1&#8211;4.3 were implemented, per the plan's delegation working
/// agreement for those tasks &#8212; an agent asked to test its own htmx wiring here would write the
/// test that passes, not the one that catches the bug.
/// </summary>
/// <remarks>
/// Needs a real dump and skips without one: set <c>DOTNETDUMP_TEST_DUMP</c>. Uses a single shared
/// host across every fact in the class (<see cref="FilterPreservationHostFixture"/>) rather than
/// <c>WiredViewRoutingTests</c>' per-fact reload, since a 9+ GB dump load six times over would make
/// this file the slow one in the suite for no benefit &#8212; nothing here mutates the loaded dump.
/// </remarks>
[Collection(FilterPreservationCollection.Name)]
public sealed class FilterPreservationTests(FilterPreservationHostFixture fixture) {
	private async Task<string> GetFragment(string query) {
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Url + "/views/dumpheap" + query);
		request.Headers.Add("HX-Request", "true");
		using var response = await client.SendAsync(request);
		return await response.Content.ReadAsStringAsync();
	}

	/// <summary>
	/// Returns the smallest <c>&lt;...&gt;</c> tag in <paramref name="html"/> containing
	/// <paramref name="needle"/>, order-independent of attribute placement. Used instead of a full
	/// HTML parser (no such dependency exists in this test project) to assert two attributes belong
	/// to the *same* element without caring which one Razor emits first.
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

	[SkippableFact]
	public async Task SortHeader_CarriesHxInclude_SoClickingItResubmitsTheActiveFilter() {
		fixture.SkipIfUnavailable();

		string body = await GetFragment("?type=Http&limit=5");

		// "count" is unambiguous -- HeapAnalyzer.GetHeapStatistics has no dedicated sort string for
		// TotalSize, the default, so asserting against the Count header is the one that cannot be
		// satisfied by coincidence.
		string header = TagContaining(body, "sort=count");
		Assert.Contains("hx-include", header, StringComparison.Ordinal);

		// The filter bar must still show the active filter, or hx-include has nothing to resubmit
		// even if the attribute is present on the header.
		string filterInput = TagContaining(body, "name=\"type\"");
		Assert.Contains("value=\"Http\"", filterInput, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task SortedAndFiltered_ComposeCorrectly_AtTheServer() {
		fixture.SkipIfUnavailable();

		string body = await GetFragment("?type=Http&sort=count&order=asc&limit=500");

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
	public async Task ActiveFilterChip_RemovesOnlyItself_KeepingTheOtherFilter() {
		fixture.SkipIfUnavailable();

		string body = await GetFragment("?type=Http&minCount=1&limit=5");

		// One chip's own removal control must drop 'type' while keeping 'minCount', and the other
		// must do the reverse. Neither chip's href/hx-get may carry both -- that would be the
		// "clear all" control, not an individual removal.
		var chipTargets = Regex.Matches(body, "(?:hx-get|href)=\"([^\"]*dumpheap[^\"]*)\"")
			.Select(m => m.Groups[1].Value)
			.Where(url => url.Contains('?'))
			.ToList();

		Assert.Contains(chipTargets, url => url.Contains("minCount=1", StringComparison.Ordinal) && !url.Contains("type=", StringComparison.Ordinal));
		Assert.Contains(chipTargets, url => url.Contains("type=Http", StringComparison.Ordinal) && !url.Contains("minCount=", StringComparison.Ordinal));
	}

	[SkippableFact]
	public async Task ClearAll_RemovesEveryActiveFilter() {
		fixture.SkipIfUnavailable();

		string body = await GetFragment("?type=Http&minCount=1&limit=5");

		var targets = Regex.Matches(body, "(?:hx-get|href)=\"([^\"]*dumpheap[^\"]*)\"")
			.Select(m => m.Groups[1].Value)
			.ToList();

		Assert.Contains(targets, url => !url.Contains("type=", StringComparison.Ordinal) && !url.Contains("minCount=", StringComparison.Ordinal));
	}

	[SkippableFact]
	public async Task ActiveSortColumn_IsMarkedAscending_AndOthersDoNotClaimAnOrder() {
		fixture.SkipIfUnavailable();

		string body = await GetFragment("?sort=count&order=asc&limit=5");

		string countHeader = TagContaining(body, "sort=count");
		Assert.Contains("aria-sort=\"ascending\"", countHeader, StringComparison.Ordinal);

		string typeHeader = TagContaining(body, "sort=typename");
		Assert.DoesNotContain("aria-sort=\"ascending\"", typeHeader, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-sort=\"descending\"", typeHeader, StringComparison.Ordinal);
	}
}

/// <summary>One dump load shared by every <see cref="FilterPreservationTests"/> fact.</summary>
public sealed class FilterPreservationHostFixture : IAsyncLifetime {
	private static string DumpPath =>
		Environment.GetEnvironmentVariable(IntegrationTests.DumpPathVariable) ?? string.Empty;

	private Microsoft.AspNetCore.Builder.WebApplication? _app;

	public bool Available { get; private set; }
	public string Url { get; private set; } = string.Empty;

	public void SkipIfUnavailable() =>
		Skip.IfNot(Available,
			$"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

	public async Task InitializeAsync() {
		if (!File.Exists(DumpPath)) {
			return;
		}

		var context = new DumpContext();
		context.Load(DumpPath);

		var options = new DumpWebHostOptions {
			DumpPath = DumpPath,
			Context = context,
			Cache = new MemoryAnalysisCache(),
			Port = 0,
		};

		_app = DumpWebHost.Build(options);
		await _app.StartAsync();
		Url = DumpWebHost.ResolveUrl(_app);
		Available = true;
	}

	public async Task DisposeAsync() {
		if (_app is not null) {
			await _app.StopAsync();
			await _app.DisposeAsync();
		}
	}
}

[CollectionDefinition(Name)]
public sealed class FilterPreservationCollection : ICollectionFixture<FilterPreservationHostFixture> {
	public const string Name = "FilterPreservation";
}