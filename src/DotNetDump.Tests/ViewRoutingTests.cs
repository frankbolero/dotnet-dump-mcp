using System.Net;

using DotNetDump.Web.Catalog;

namespace DotNetDump.Tests;

/// <summary>
/// That <c>/views/{view}</c> serves a whole page to a browser and a bare fragment to htmx.
/// </summary>
/// <remarks>
/// <para>
/// The route is two things at once, told apart by the <c>HX-Request</c> header. Serving the
/// fragment to both is a defect that presents as a styling problem: the navigation links point
/// here, so every view rendered as an unstyled table with no shell, no stylesheet and no way back.
/// </para>
/// <para>
/// It is a correctness requirement rather than a cosmetic one. DATA_CONTRACT.md &#0167;3.2 makes the
/// query string the view state and <c>hx-push-url</c> writes it to the address bar, so
/// <c>/views/dumpheap?type=Http</c> has to be a page a user can paste, bookmark and share — which
/// is what Phase 4.6's round-trip criterion will require.
/// </para>
/// <para>
/// <strong>What these tests can and cannot reach.</strong> They run against
/// <see cref="LoopbackServerFixture"/>, whose <see cref="NoDumpContext"/> has no runtime, so any
/// view that actually calls an analyzer throws — correctly — and never reaches a rendered page. The
/// page-versus-fragment branch is therefore exercised through a view with no handler, which does
/// not touch the analyzer and so isolates the routing decision from the analysis. The wired path
/// carrying real rows through the same branch is covered by
/// <see cref="WiredViewRoutingTests"/>, which needs a dump and skips without one.
/// </para>
/// </remarks>
[Collection(WebSecurityCollection.Name)]
public sealed class ViewRoutingTests(LoopbackServerFixture server) {
	/// <summary>
	/// A catalog view with no handler yet, so nothing here reaches an analyzer. Every list view is
	/// wired (task 3.3), and 3.4 is progressively wiring every detail view too, which made this
	/// constant move twice already (<c>gchandles</c>, then <c>info</c>) as those views landed.
	/// <c>gcroot</c> is deliberately excluded from 3.4 — it is a tree and belongs to Phase 5.3 — so
	/// unlike every other remaining name here, it will not need to move again until then.
	/// </summary>
	private const string UnwiredView = "gcroot";

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
	public async Task BrowserNavigation_ReturnsAWholePage() {
		var (status, body) = await Send("/views/" + UnwiredView);

		// 501 is honest -- the view is in the catalog and the navigation links to it, but it has no
		// handler yet. The page around it is what lets a reader leave without pressing Back.
		Assert.Equal(HttpStatusCode.NotImplemented, status);
		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dndump.css", body, StringComparison.Ordinal);
		// The navigation specifically: without it, a reader who follows a link cannot follow another.
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task HtmxRequest_ReturnsABareFragment() {
		var (_, body) = await Send("/views/" + UnwiredView, ("HX-Request", "true"));

		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
		Assert.DoesNotContain("dn-nav", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task HistoryRestoreRequest_ReturnsAWholePage() {
		// htmx sets both headers when restoring from its own history cache, and on that request it
		// wants the whole document back rather than a fragment.
		var (_, body) = await Send(
			"/views/" + UnwiredView,
			("HX-Request", "true"),
			("HX-History-Restore-Request", "true"));

		Assert.Contains("<html", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UnknownView_Is404() {
		var (status, _) = await Send("/views/nosuchview");

		Assert.Equal(HttpStatusCode.NotFound, status);
	}

	[Fact]
	public async Task RejectedQueryString_Is400_AndNotWrappedInTheShell() {
		// A rejected query string is the client's error, not a view. Rendering the shell around it
		// would present a broken request as a working page.
		var (status, body) = await Send("/views/dumpheap?order=sideways");

		Assert.Equal(HttpStatusCode.BadRequest, status);
		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NoViewInTheCatalog_Is404() {
		// The invariant is precisely "the navigation never links to a 404". Statuses other than 404
		// are all legitimate here: 200 for a wired view, 501 for one still to come, 500 for a view
		// whose analyzer refuses because this fixture has no dump loaded.
		foreach (var view in ViewCatalog.All) {
			var (status, _) = await Send("/views/" + view.Name);

			Assert.True(
				status != HttpStatusCode.NotFound,
				$"'{view.Name}' is in the navigation but the route answered 404.");
		}
	}
}

/// <summary>
/// The same page-versus-fragment branch, but over a view that actually renders rows.
/// </summary>
/// <remarks>
/// Needs a real dump and skips without one, following <c>IntegrationTests</c>: set
/// <c>DOTNETDUMP_TEST_DUMP</c> to a dump file to run these. What they add over
/// <see cref="ViewRoutingTests"/> is that the *wired* path — the one a user actually navigates to,
/// and the one that shipped broken — puts its table inside the shell rather than on its own.
/// </remarks>
public sealed class WiredViewRoutingTests : IAsyncLifetime {
	private static string DumpPath =>
		Environment.GetEnvironmentVariable(IntegrationTests.DumpPathVariable) ?? string.Empty;

	private DotNetDump.Web.DumpWebHostOptions? _options;
	private Microsoft.AspNetCore.Builder.WebApplication? _app;
	private string _url = string.Empty;

	public async Task InitializeAsync() {
		if (!File.Exists(DumpPath)) {
			return;
		}

		var context = new DotNetDump.Core.DumpContext();
		context.Load(DumpPath);

		_options = new DotNetDump.Web.DumpWebHostOptions {
			DumpPath = DumpPath,
			Context = context,
			Cache = new DotNetDump.Core.Caching.MemoryAnalysisCache(),
			Port = 0,
		};

		_app = DotNetDump.Web.DumpWebHost.Build(_options);
		await _app.StartAsync();
		_url = DotNetDump.Web.DumpWebHost.ResolveUrl(_app);
	}

	public async Task DisposeAsync() {
		if (_app is not null) {
			await _app.StopAsync();
			await _app.DisposeAsync();
		}
	}

	[SkippableFact]
	public async Task BrowserNavigation_ToAWiredView_PutsTheTableInsideTheShell() {
		Skip.IfNot(File.Exists(DumpPath),
			$"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		string body = await client.GetStringAsync(_url + "/views/dumpheap?limit=5");

		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-dumpheap\"", body, StringComparison.Ordinal);
		Assert.Contains("dn-table", body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task HtmxRequest_ToAWiredView_ReturnsTheTableAlone() {
		Skip.IfNot(File.Exists(DumpPath),
			$"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");

		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, _url + "/views/dumpheap?limit=5");
		request.Headers.Add("HX-Request", "true");

		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
		// The swap target still has to be there, or htmx has nothing to put anywhere.
		Assert.Contains("id=\"v-dumpheap\"", body, StringComparison.Ordinal);
		Assert.Contains("dn-table", body, StringComparison.Ordinal);
	}
}