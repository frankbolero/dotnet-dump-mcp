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
}