using System.Net;

using DotNetDump.Web.Catalog;

namespace DotNetDump.Tests;

/// <summary>
/// What <c>/views/{view}</c> answers before any analyzer is involved: unknown names, rejected query
/// strings, and the one view whose HTML surface lives somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This class used to test the whole-page-versus-fragment branch, and no longer can.</strong>
/// That branch is real and remains a correctness requirement — the route is two things at once, told
/// apart by the <c>HX-Request</c> header, and serving the fragment to both renders every view as an
/// unstyled table with no shell and no way back. It was testable here because these tests run
/// against <see cref="LoopbackServerFixture"/>, whose <see cref="NoDumpContext"/> has no runtime: any
/// view that reaches an analyzer throws before either branch produces a response, so the branch could
/// only be exercised through a view that reached no analyzer at all. That meant a view in the catalog
/// with no handler yet, and the constant naming it moved twice as Phase 3.3 and 3.4 wired views
/// (<c>gchandles</c>, then <c>info</c>) before settling on <c>gcroot</c>.
/// </para>
/// <para>
/// Phase 5.3 wired <c>gcroot</c> — as a redirect into <c>/trees/gcroot/{address}</c>, which returns
/// before <c>WantsFragment</c> is consulted and so cannot stand in for that branch either. With every
/// other view wired by 3.4, no substitute exists and none is coming: a name invented to keep this
/// test alive would prove the routing of a view nobody navigates to. <strong>The page-versus-fragment
/// branch is therefore covered only by <see cref="WiredViewRoutingTests"/> and
/// <see cref="WiredTreeRoutingTests"/>, both of which need a real dump and skip without one.</strong>
/// A dumpless run no longer covers it at all. That is a genuine loss of coverage, recorded here
/// rather than papered over.
/// </para>
/// <para>
/// What is left here is everything the routing layer decides without asking an analyzer anything,
/// which is still worth holding: a name that is not in the catalog is a <c>404</c>, a name that is
/// gets something other than one, a query string this server will not act on is a bare <c>400</c>
/// rather than a broken page, and <c>gcroot</c> forwards rather than rendering a second, duplicate
/// implementation of a tree.
/// </para>
/// </remarks>
[Collection(WebSecurityCollection.Name)]
public sealed class ViewRoutingTests(LoopbackServerFixture server) {
	private async Task<(HttpStatusCode Status, string Body)> Send(string path, params (string Name, string Value)[] headers) {
		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + path);
		foreach (var (name, value) in headers) {
			request.Headers.Add(name, value);
		}

		using var response = await client.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	/// <summary>Redirects are not followed: the response under test *is* the redirect, and a client
	/// that chased it would reach the tree route's analyzer and fail for want of a dump.</summary>
	private async Task<(HttpStatusCode Status, string? Location)> SendWithoutFollowing(string path, params (string Name, string Value)[] headers) {
		using var handler = new HttpClientHandler { AllowAutoRedirect = false };
		using var client = new HttpClient(handler);
		using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + path);
		foreach (var (name, value) in headers) {
			request.Headers.Add(name, value);
		}

		using var response = await client.SendAsync(request);
		return (response.StatusCode, response.Headers.Location?.ToString());
	}

	// gcroot (5.3): a real CLI command with a real ViewCatalog entry, whose HTML surface is a tree.
	// It forwards instead of growing a second implementation of that tree here -- and a second place
	// for docs/GCROOT_TRUNCATION.md's "an empty result is not evidence" rule to be got wrong.

	[Fact]
	public async Task GCRootView_RedirectsToTheTree() {
		var (status, location) = await SendWithoutFollowing("/views/gcroot/7FF6A1B02000");

		Assert.Equal(HttpStatusCode.Found, status);
		Assert.Equal("/trees/gcroot/7FF6A1B02000", location);
	}

	[Fact]
	public async Task GCRootView_WithoutAnAddress_RedirectsToTheBareTree() {
		// The nav link has no address to offer. The tree route answers 400 saying so, which beats
		// this route inventing an address or answering 404 for a view that exists.
		var (status, location) = await SendWithoutFollowing("/views/gcroot");

		Assert.Equal(HttpStatusCode.Found, status);
		Assert.Equal("/trees/gcroot", location);
	}

	[Fact]
	public async Task GCRootView_CarriesTheQueryStringThrough() {
		// maxNodes is the one remedy offered for a truncated search; dropping it at the redirect
		// would silently give back the budget the caller just asked to lift.
		var (_, location) = await SendWithoutFollowing("/views/gcroot/7FF6A1B02000?maxNodes=0");

		Assert.Equal("/trees/gcroot/7FF6A1B02000?maxNodes=0", location);
	}

	[Fact]
	public async Task GCRootView_RedirectsForHtmxToo() {
		// htmx follows a redirect transparently, so the fragment branch is the tree route's to make.
		// This route must not try to answer the fragment itself.
		var (status, location) = await SendWithoutFollowing("/views/gcroot/7FF6A1B02000", ("HX-Request", "true"));

		Assert.Equal(HttpStatusCode.Found, status);
		Assert.Equal("/trees/gcroot/7FF6A1B02000", location);
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
		// are all legitimate here: 200 for a wired view, 500 for a view whose analyzer refuses
		// because this fixture has no dump loaded, 400 for gcroot (followed to its tree, which says
		// it needs an address). 501 was on this list until 5.3 wired the last unwired view.
		foreach (var view in ViewCatalog.All) {
			var (status, _) = await Send("/views/" + view.Name);

			Assert.True(
				status != HttpStatusCode.NotFound,
				$"'{view.Name}' is in the navigation but the route answered 404.");
		}
	}
}

/// <summary>
/// The whole-page-versus-fragment branch, over a view that actually renders rows.
/// </summary>
/// <remarks>
/// Needs a real dump and skips without one, following <c>IntegrationTests</c>: set
/// <c>DOTNETDUMP_TEST_DUMP</c> to a dump file to run these. Since Phase 5.3 wired the last view that
/// reached no analyzer, these two tests and <see cref="WiredTreeRoutingTests"/>' equivalents are the
/// <em>only</em> coverage of that branch — see <see cref="ViewRoutingTests"/>' own remarks. They
/// exercise the path a user actually navigates to, and the one that shipped broken: the table goes
/// inside the shell, not on its own.
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