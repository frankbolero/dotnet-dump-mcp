using System.Net;
using System.Text.Json;

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

	/// <summary>
	/// The object tree rejects a malformed seed before it reaches an analyzer, so unlike its other
	/// routing behaviour this <i>is</i> reachable without a dump. A missing or unparseable address is a
	/// client error (400), distinct from an unknown tree name (404) -- the two are easy to conflate,
	/// and conflating them turns "you typed the address wrong" into "that feature does not exist".
	/// </summary>
	[Theory]
	[InlineData("/trees/object")]
	[InlineData("/trees/object/nonsense")]
	[InlineData("/trees/object/10-zz")]
	public async Task ObjectTreeWithoutAUsableAddress_Is400(string path) {
		var (status, body) = await Send(path);

		Assert.Equal(HttpStatusCode.BadRequest, status);
		Assert.Contains("requires an address", body, StringComparison.Ordinal);
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

	/// <summary>
	/// The thread&#8594;frames tree (5.2, DATA_CONTRACT.md &#0167;4.4) -- unlike <c>heap</c>, this tree
	/// is fully computed up front (<c>ThreadFramesTreeBuilder</c>'s own doc comment), so a single
	/// request already carries every root and every frame; there is no lazy-expand round trip to test
	/// the way <see cref="ExpandingANamespaceNode_ReturnsBareListItemsForItsChildren"/> does for
	/// <c>heap</c>.
	/// </summary>
	[SkippableFact]
	public async Task BrowserNavigation_ToThreadsTree_PutsTheTreeInsideTheShell() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		string body = await client.GetStringAsync($"{_url}/trees/threads");

		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-threads\"", body, StringComparison.Ordinal);
		Assert.Contains("dn-tree", body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task HtmxRequest_ToThreadsTree_ReturnsTheTreeAlone() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/trees/threads");
		request.Headers.Add("HX-Request", "true");

		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
		Assert.DoesNotContain("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-threads\"", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// The whole tree arrives in one response -- proves the "fully computed up front" shape against
	/// real data, not just <see cref="ThreadFramesTreeBuilderTests"/>'s synthetic fixtures. At least
	/// one root must render as a real, expandable <c>&lt;details&gt;</c> node with its frames already
	/// present in the same markup (no <c>hx-get</c> on it at all -- unlike <c>heap</c>'s nodes, which
	/// always carry one).
	/// </summary>
	[SkippableFact]
	public async Task ThreadsTree_RendersEveryThreadsFramesInTheSameResponse_WithNoFurtherFetch() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/trees/threads");
		request.Headers.Add("HX-Request", "true");

		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("dn-tree__item", body, StringComparison.Ordinal);
		Assert.DoesNotContain("/trees/threads/", body, StringComparison.Ordinal);
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

	// ---- object (5.4) -------------------------------------------------------------------------

	/// <summary>
	/// A real object address that has at least one reference field, plus the page rendered for it.
	/// <para>
	/// Found by type rather than by taking the first thing <c>listobj</c> returns: heap order puts
	/// <c>System.Byte[]</c> and <c>System.String</c> first in enormous quantity, and none of them
	/// reference anything, so "first object on the heap" finds an empty tree on a real dump. Asking
	/// <c>dumpheap</c> which types are present and preferring a non-<c>System</c> one gets to an
	/// object with fields in a couple of round trips, without hard-coding a type this particular
	/// fixture happens to contain.
	/// </para>
	/// </summary>
	private async Task<(string Address, string Page)> FindAnObjectWithReferences(HttpClient client) {
		using var stats = JsonDocument.Parse(await client.GetStringAsync($"{_url}/api/dumpheap?limit=200"));
		var types = stats.RootElement.GetProperty("data").EnumerateArray()
			.Select(item => item.GetProperty("typeName").GetString() ?? "")
			.Where(name => name.Length > 0 && name != "Free" && !name.EndsWith("[]", StringComparison.Ordinal))
			.OrderBy(name => name.StartsWith("System.", StringComparison.Ordinal) ? 1 : 0)
			.Take(12)
			.ToList();

		Assert.NotEmpty(types);

		foreach (string type in types) {
			using var objects = JsonDocument.Parse(
				await client.GetStringAsync($"{_url}/api/listobj?type={Uri.EscapeDataString(type)}&limit=3"));

			foreach (var item in objects.RootElement.GetProperty("data").EnumerateArray()) {
				string address = item.GetProperty("address").GetString() ?? "";
				string page = await client.GetStringAsync($"{_url}/trees/object/{address}");
				if (page.Contains("hx-get=\"/trees/object/", StringComparison.Ordinal)) {
					return (address, page);
				}
			}
		}

		Assert.Fail($"None of the objects sampled across {types.Count} types had a reference field to expand.");
		return default;
	}

	[SkippableFact]
	public async Task BrowserNavigation_ToObjectTree_PutsTheTreeAndItsBreadcrumbInsideTheShell() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		var (address, page) = await FindAnObjectWithReferences(client);

		Assert.Contains("<html", page, StringComparison.Ordinal);
		Assert.Contains("id=\"v-object\"", page, StringComparison.Ordinal);
		Assert.Contains("dn-breadcrumb", page, StringComparison.Ordinal);
		// The seed is the only crumb at depth 1, so it is the current one and carries no link.
		Assert.Contains($"dn-breadcrumb__current dn-addr\" aria-current=\"page\">{address}", page, StringComparison.Ordinal);
	}

	/// <summary>Every <c>hx-get</c> a rendered object page offers, in document order.</summary>
	private static List<string> ExpansionUrls(string page) =>
		[.. System.Text.RegularExpressions.Regex.Matches(page, "hx-get=\"(/trees/object/[^\"]+)\"")
			.Select(m => m.Groups[1].Value)
			.Distinct()];

	/// <summary>
	/// The lazy-expand round trip against real data, exactly as 5.1's namespace test does it: pull a
	/// node's own hx-get out of the rendered page and request it. Proves the node id this builder
	/// encodes (a path of addresses) survives the URL and comes back as the bare <c>&lt;li&gt;</c>s
	/// <c>_TreeNodes.cshtml</c> promises, rather than a second copy of the breadcrumb and wrapper.
	/// </summary>
	[SkippableFact]
	public async Task ExpandingAnObjectNode_ReturnsBareListItemsForItsChildren() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		var (_, page) = await FindAnObjectWithReferences(client);

		var urls = ExpansionUrls(page);
		Assert.NotEmpty(urls);

		bool sawRealChildren = false;
		foreach (string childUrl in urls) {
			// A child's id is its parent's path plus one address, so every expansion URL is strictly
			// longer than the seed: this is the visited set travelling in the URL.
			Assert.Contains("-", childUrl["/trees/object/".Length..], StringComparison.Ordinal);

			using var request = new HttpRequestMessage(HttpMethod.Get, _url + childUrl);
			request.Headers.Add("HX-Request", "true");
			using var response = await client.SendAsync(request);
			string body = await response.Content.ReadAsStringAsync();

			// Every disclosure the tree renders must be openable. A referent the analyzer cannot read
			// is a node saying so, never a failed request -- the tree offers a disclosure before it
			// knows whether the referent is readable, so "unreadable" has to be an ordinary outcome.
			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
			Assert.DoesNotContain("dn-breadcrumb", body, StringComparison.Ordinal);

			// An empty body is a legitimate answer: HasChildren is optimistic, because knowing whether
			// a referent has reference fields of its own means reading it, which is the walk this tree
			// exists to avoid. A referent that turns out to have none swaps in nothing. What must never
			// appear is a wrapper -- the response is swapped straight into the <ul> the <details>
			// already has, so bare <li>s or nothing at all.
			if (body.Trim().Length > 0) {
				Assert.StartsWith("<li", body.TrimStart(), StringComparison.Ordinal);
			}

			sawRealChildren |= body.Contains("hx-get=\"/trees/object/", StringComparison.Ordinal);
		}

		Assert.True(sawRealChildren, "Expected at least one child to expand into further expandable nodes.");
	}

	/// <summary>
	/// Following a breadcrumb link is a plain navigation, so it renders the whole shell again -- the
	/// property DATA_CONTRACT.md &#0167;4.5 relies on when it says the address bar and the back button
	/// are this tree's entire history mechanism.
	/// </summary>
	[SkippableFact]
	public async Task ADeepPathRendersACrumbPerAncestor() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		var (_, page) = await FindAnObjectWithReferences(client);

		int hrefIndex = page.IndexOf("hx-get=\"/trees/object/", StringComparison.Ordinal);
		int start = hrefIndex + "hx-get=\"".Length;
		string childUrl = page[start..page.IndexOf('"', start)];

		// Same URL, no HX-Request header: a navigation, not an expansion.
		string deep = await client.GetStringAsync(_url + childUrl);

		Assert.Contains("<html", deep, StringComparison.Ordinal);
		Assert.Contains("dn-breadcrumb__link", deep, StringComparison.Ordinal);
		Assert.Contains("dn-breadcrumb__current", deep, StringComparison.Ordinal);
	}

	/// <summary>The tree's only entry point: <c>dumpobj</c>'s own page, since no top-level navigation
	/// can offer an address the user does not have yet (TreeCatalog.cs).</summary>
	[SkippableFact]
	public async Task TheDumpObjPageLinksToTheObjectTree() {
		Skip.IfNot(File.Exists(DumpPath), $"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		var (address, _) = await FindAnObjectWithReferences(client);

		string page = await client.GetStringAsync($"{_url}/views/dumpobj/{address}");

		Assert.Contains($"href=\"/trees/object/{address}\"", page, StringComparison.Ordinal);
	}
}