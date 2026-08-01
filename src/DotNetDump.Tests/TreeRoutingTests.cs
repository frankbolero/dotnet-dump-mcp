using System.Net;

using DotNetDump.Core;
using DotNetDump.Core.Caching;
using DotNetDump.Web;

using Xunit;

namespace DotNetDump.Tests;

/// <summary>
/// <c>GET /trees/{tree}/{seed?}</c> (Phase 5, SERVER.md &#0167;2). Only the routing shape that does not
/// need an analyzer -- an unknown tree name -- is reachable against <see cref="NoDumpContext"/>: a
/// known name reaches its builder's own <c>queue.Enqueue</c> call before either branch of
/// <c>WantsFragment</c> can answer, exactly the constraint <see cref="Name2EERoutingTests"/> already
/// documents for <c>name2ee</c>. The wired path is covered by <see cref="WiredTreeRoutingTests"/>.
/// </summary>
[Collection(WebSecurityCollection.Name)]
public sealed class TreeRoutingTests(LoopbackServerFixture server) {
	private async Task<(HttpStatusCode Status, string Body)> Send(string path, params (string Name, string Value)[] headers) {
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + path);
		foreach (var (name, value) in headers) {
			request.Headers.Add(name, value);
		}

		using var response = await client.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task UnknownTree_Is404() {
		var (status, body) = await Send("/trees/nosuchtree");

		Assert.Equal(HttpStatusCode.NotFound, status);
		Assert.Contains("nosuchtree", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UnknownTreeWithSeed_Is404() {
		var (status, _) = await Send("/trees/nosuchtree/some-id");

		Assert.Equal(HttpStatusCode.NotFound, status);
	}

	// gcroot's own 400s (5.3) reach no analyzer either: its address and budget are validated before
	// anything is enqueued, which is what makes them testable against this dumpless fixture.

	[Fact]
	public async Task GCRootWithoutAnAddress_Is400() {
		var (status, body) = await Send("/trees/gcroot");

		// Not a 404: 'gcroot' is a real tree, and saying "no such thing" about a route that exists
		// would send a reader looking for a spelling mistake instead of supplying the address.
		Assert.Equal(HttpStatusCode.BadRequest, status);
		Assert.Contains("requires an address", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GCRootWithAMalformedAddress_Is400() {
		var (status, body) = await Send("/trees/gcroot/nothexatall");

		Assert.Equal(HttpStatusCode.BadRequest, status);
		Assert.Contains("hex", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GCRootWithANegativeBudget_Is400() {
		var (status, body) = await Send("/trees/gcroot/7FF6A1B02000?maxNodes=-1");

		Assert.Equal(HttpStatusCode.BadRequest, status);
		Assert.Contains("maxNodes", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GCRootWithAnUnlimitedBudget_IsNotRejected() {
		// 0 means unlimited and is the value the truncation banner's own link uses, so rejecting it
		// as "not a sensible number" would break the one remedy the banner offers. Reaches the
		// analyzer and fails there for want of a dump; what matters is that it got past validation.
		var (status, _) = await Send("/trees/gcroot/7FF6A1B02000?maxNodes=0");

		Assert.NotEqual(HttpStatusCode.BadRequest, status);
	}
}

/// <summary>
/// The same route, exercised against a real dump. Skips without one, following
/// <see cref="IntegrationTests"/>' own convention -- set <see cref="IntegrationTests.DumpPathVariable"/>
/// to a dump file to run these.
/// </summary>
public sealed class WiredTreeRoutingTests : IAsyncLifetime {
	private static string DumpPath =>
		Environment.GetEnvironmentVariable(IntegrationTests.DumpPathVariable) ?? string.Empty;

	private DumpContext? _context;
	private Microsoft.AspNetCore.Builder.WebApplication? _app;
	private string _url = string.Empty;

	public async Task InitializeAsync() {
		if (!File.Exists(DumpPath)) {
			return;
		}

		_context = new DumpContext();
		_context.Load(DumpPath);

		var options = new DumpWebHostOptions {
			DumpPath = DumpPath,
			Context = _context,
			Cache = new MemoryAnalysisCache(),
			Port = 0,
		};

		_app = DumpWebHost.Build(options);
		await _app.StartAsync();
		_url = DumpWebHost.ResolveUrl(_app);
	}

	public async Task DisposeAsync() {
		if (_app is not null) {
			await _app.StopAsync();
			await _app.DisposeAsync();
		}
	}

	[SkippableFact]
	public async Task BrowserNavigation_ToHeapTree_PutsTheTreeInsideTheShell() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		string body = await client.GetStringAsync($"{_url}/trees/heap");

		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-heap\"", body, StringComparison.Ordinal);
		Assert.Contains("dn-tree", body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task HtmxRequest_ToHeapTree_ReturnsTheTreeAlone() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/trees/heap");
		request.Headers.Add("HX-Request", "true");

		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
		Assert.DoesNotContain("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-heap\"", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// The lazy-expand round trip: fetch the root, pick a real namespace's node id out of the
	/// rendered markup, then request its children exactly as the <c>&lt;details&gt;</c> element's own
	/// <c>hx-get</c> would. Proves <c>_TreeNodes.cshtml</c>'s "bare &lt;li&gt;s, no wrapper" contract
	/// against real data, not just the synthetic fixtures <c>NamespaceRollupBuilderTests</c> covers.
	/// </summary>
	[SkippableFact]
	public async Task ExpandingANamespaceNode_ReturnsBareListItemsForItsChildren() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		string root = await client.GetStringAsync($"{_url}/trees/heap");

		int hrefIndex = root.IndexOf("hx-get=\"/trees/heap/", StringComparison.Ordinal);
		Assert.True(hrefIndex >= 0, "Expected at least one expandable namespace node in the root page.");
		int start = hrefIndex + "hx-get=\"".Length;
		int end = root.IndexOf('"', start);
		string childUrl = root[start..end];

		using var request = new HttpRequestMessage(HttpMethod.Get, _url + childUrl);
		request.Headers.Add("HX-Request", "true");
		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
		// Bare <li>s, not wrapped in a <ul> of their own -- the response is meant to be swapped
		// straight into the <ul> the <details> element already has (hx-swap="innerHTML"), and a
		// second nested <ul> around the whole batch would double-wrap it. A *child* node's own empty
		// disclosure placeholder legitimately has its own <ul>, so the check is only that the
		// response's own first element is an <li>, not that "<ul" never appears anywhere in it.
		Assert.StartsWith("<li", body.TrimStart(), StringComparison.Ordinal);
		Assert.Contains("dn-tree__item", body, StringComparison.Ordinal);
	}

	// ---- gcroot (5.3) --------------------------------------------------------------------------
	//
	// Addresses are taken from the server's own JSON routes rather than hard-coded or read off the
	// ClrRuntime from the test thread: hard-coded addresses rot with the fixture, and touching the
	// runtime directly while the host is serving is the exact unsynchronized ClrMD access SERVER.md
	// §3 exists to prevent. Going through /api puts every read on the one analysis thread.

	/// <summary>An object held by a strong GC handle -- reliably rooted, and rooted by something the
	/// search finds immediately, so this stays fast on a 10M-object heap.</summary>
	private static async Task<string?> FindStronglyHeldObject(HttpClient client, string url) {
		using var document = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(url + "/api/gchandles?limit=500"));
		foreach (var handle in document.RootElement.GetProperty("data").EnumerateArray()) {
			if (handle.GetProperty("isStrong").GetBoolean()) {
				return handle.GetProperty("object").GetString();
			}
		}

		return null;
	}

	[SkippableFact]
	public async Task GCRootTree_ForAStronglyHeldObject_RendersTheChainRootFirst() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		string? address = await FindStronglyHeldObject(client, _url);
		Skip.If(address is null, "No strong GC handle in this dump to trace a retention path from.");

		string body = await client.GetStringAsync($"{_url}/trees/gcroot/{address}");

		Assert.Contains("id=\"v-gcroot\"", body, StringComparison.Ordinal);
		Assert.Contains("dn-tree__item", body, StringComparison.Ordinal);
		// The root of the chain is badged with what makes it a root, per DATA_CONTRACT.md §4.3.
		Assert.Contains("StrongHandle", body, StringComparison.Ordinal);
		// A found path is a found path: nothing here may hedge it as unrooted.
		Assert.DoesNotContain("eligible for collection", body, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The defect of docs/GCROOT_TRUNCATION.md, asserted against a real heap rather than a fixture.
	/// </summary>
	/// <remarks>
	/// This dump holds ~10.2M objects and the default budget is 2,000,000, so a search for an
	/// ordinary object truncates -- which is the whole point: on exactly the large dumps this command
	/// exists for, the old code path reported "unrooted, eligible for collection" as fact. The tree
	/// must call the result inconclusive and must not use those words anywhere.
	/// </remarks>
	[SkippableFact]
	public async Task GCRootTree_WhenTheSearchIsTruncated_SaysInconclusiveAndNeverEligibleForCollection() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

		using var objects = System.Text.Json.JsonDocument.Parse(
			await client.GetStringAsync($"{_url}/api/listobj?limit=1&type=System.String"));
		var rows = objects.RootElement.GetProperty("data").EnumerateArray().ToList();
		Skip.If(rows.Count == 0, "No System.String instances in this dump to search from.");

		string address = rows[0].GetProperty("address").GetString()!;

		// A deliberately tiny budget guarantees truncation regardless of which object came back
		// first, so this test asserts the truncated *rendering* rather than a property of the heap.
		string body = await client.GetStringAsync($"{_url}/trees/gcroot/{address}?maxNodes=1");

		// A budget of 1 truncates every search except one whose target is itself a root object --
		// found before the first node is visited, and so a conclusive answer with nothing to warn
		// about. Nothing to assert about truncation in that case; it is not a failure.
		Skip.If(!body.Contains("dn-tree-banner", StringComparison.Ordinal) && body.Contains("dn-note", StringComparison.Ordinal),
			"This object is itself a GC root, so the search completed before the budget mattered.");

		Assert.Contains("dn-tree-banner", body, StringComparison.Ordinal);
		Assert.Contains("Inconclusive", body, StringComparison.Ordinal);
		Assert.DoesNotContain("eligible for collection", body, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("unrooted", body, StringComparison.OrdinalIgnoreCase);
		// And the banner offers the way out, or it is only half an answer.
		Assert.Contains("maxNodes=0", body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task BrowserNavigation_ToGCRootTree_PutsTheTreeInsideTheShell() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		string? address = await FindStronglyHeldObject(client, _url);
		Skip.If(address is null, "No strong GC handle in this dump to trace a retention path from.");

		string body = await client.GetStringAsync($"{_url}/trees/gcroot/{address}");

		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
		// gcroot has no TreeCatalog entry (it needs an address, so it is not nav-reachable); the page
		// borrows the header of the ViewCatalog entry the nav link comes from.
		Assert.Contains("Retention paths", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-gcroot\"", body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task HtmxRequest_ToGCRootTree_ReturnsTheTreeAlone() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		string? address = await FindStronglyHeldObject(client, _url);
		Skip.If(address is null, "No strong GC handle in this dump to trace a retention path from.");

		using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/trees/gcroot/{address}");
		request.Headers.Add("HX-Request", "true");

		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-gcroot\"", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// The tree is computed whole up front (DATA_CONTRACT.md &#0167;4.3), so unlike the namespace rollup
	/// it must arrive fully nested -- no empty child list waiting on an <c>hx-get</c> that this tree
	/// never issues.
	/// </summary>
	[SkippableFact]
	public async Task GCRootTree_IsNestedUpFront_NotLazy() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		string? address = await FindStronglyHeldObject(client, _url);
		Skip.If(address is null, "No strong GC handle in this dump to trace a retention path from.");

		using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/trees/gcroot/{address}");
		request.Headers.Add("HX-Request", "true");
		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.DoesNotContain("hx-get=\"/trees/gcroot/", body, StringComparison.Ordinal);
		Assert.DoesNotContain("hx-trigger=\"toggle once\"", body, StringComparison.Ordinal);
	}
}