using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Caching;
using DotNetDump.Web;

using Xunit;

namespace DotNetDump.Tests;

/// <summary>
/// <c>/views/name2ee/{module}/{type}</c> and its <c>/api</c> mirror -- the one Phase 3.4 detail
/// view that gets a dedicated two-segment route rather than the generic
/// <c>/views/{view}/{address?}</c> every other detail view shares, because it takes two required
/// strings rather than one optional address (IMPLEMENTATION_PLAN.md Phase 3.4).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the generic route, <c>name2ee</c> has no "unwired view" bypass to isolate the routing
/// decision from the analyzer the way <see cref="ViewRoutingTests"/> does with <c>gcroot</c> --
/// every request here calls <see cref="ModuleAnalyzer.Name2EE"/> for real, so there is no way to
/// exercise the whole-page-vs-fragment branch against <see cref="NoDumpContext"/> the way that
/// class does: the analyzer throws before either branch produces a response. This class instead
/// follows <see cref="WiredViewRoutingTests"/>'s pattern and needs a real dump, skipping without
/// one -- set <see cref="IntegrationTests.DumpPathVariable"/> to run it.
/// </para>
/// <para>
/// What it adds over the generic routing tests: proof that a request with two trailing segments
/// actually reaches the dedicated route (rather than the generic one, which cannot match two
/// segments against <c>{address?}</c>), carried all the way through a real analyzer call, in both
/// the whole-page and htmx-fragment shapes.
/// </para>
/// </remarks>
public sealed class Name2EERoutingTests : IAsyncLifetime {
	// Present in essentially every modern .NET dump -- the same pair IntegrationTests'
	// ModuleAnalyzer_Name2EE_ReturnsData uses, for the same reason: not guaranteed, so a lookup
	// failure here means "not in this particular dump," not "the feature is broken."
	private const string Module = "System.Private.CoreLib";
	private const string Type = "System.String";

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

	/// <summary>
	/// <c>System.Private.CoreLib</c> / <c>System.String</c> is not guaranteed to resolve in every
	/// fixture, exactly as <see cref="IntegrationTests.ModuleAnalyzer_Name2EE_ReturnsData"/> treats
	/// it. Checked directly against the same loaded context before trusting the HTTP round trip to
	/// tell the difference between "not wired" and "not present in this dump."
	/// </summary>
	private bool ModuleAndTypePresent() {
		try {
			new ModuleAnalyzer(_context!).Name2EE(Module, Type);
			return true;
		} catch (ArgumentException) {
			return false;
		}
	}

	[SkippableFact]
	public async Task BrowserNavigation_PutsTheCardInsideTheShell() {
		Skip.IfNot(File.Exists(DumpPath),
			$"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");
		Skip.IfNot(ModuleAndTypePresent(), $"'{Module}' / '{Type}' not present in this dump.");

		using var client = new HttpClient();
		string body = await client.GetStringAsync($"{_url}/views/name2ee/{Module}/{Type}");

		Assert.Contains("<html", body, StringComparison.Ordinal);
		Assert.Contains("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-name2ee\"", body, StringComparison.Ordinal);
		Assert.Contains(Type, body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task HtmxRequest_ReturnsTheCardAlone() {
		Skip.IfNot(File.Exists(DumpPath),
			$"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");
		Skip.IfNot(ModuleAndTypePresent(), $"'{Module}' / '{Type}' not present in this dump.");

		using var client = new HttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/views/name2ee/{Module}/{Type}");
		request.Headers.Add("HX-Request", "true");

		using var response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.DoesNotContain("<html", body, StringComparison.Ordinal);
		Assert.DoesNotContain("dn-nav", body, StringComparison.Ordinal);
		Assert.Contains("id=\"v-name2ee\"", body, StringComparison.Ordinal);
	}

	[SkippableFact]
	public async Task JsonRoute_ReturnsTheSameLookup() {
		Skip.IfNot(File.Exists(DumpPath),
			$"No dump fixture. Set {IntegrationTests.DumpPathVariable} to a dump file to run this.");
		Skip.IfNot(ModuleAndTypePresent(), $"'{Module}' / '{Type}' not present in this dump.");

		using var client = new HttpClient();
		string body = await client.GetStringAsync($"{_url}/api/name2ee/{Module}/{Type}");

		Assert.Contains("\"typeName\"", body, StringComparison.OrdinalIgnoreCase);
		Assert.Contains(Type, body, StringComparison.Ordinal);
	}
}