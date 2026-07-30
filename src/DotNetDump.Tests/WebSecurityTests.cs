using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

using DotNetDump.Core.Caching;
using DotNetDump.Web;
using DotNetDump.Web.Security;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetDump.Tests;

/// <summary>
/// Groups every test that starts a real host. Parallelism is disabled because
/// <see cref="DumpWebHost.Build"/> clears process-wide binding environment variables and
/// <see cref="WebBindingEnvironmentTests"/> deliberately sets them — two hosts building at once
/// would interleave those writes and make the environment tests report on each other's state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebSecurityCollection : ICollectionFixture<LoopbackServerFixture> {
	public const string Name = "web-security";
}

/// <summary>
/// One real <c>dndump serve</c> host on an ephemeral loopback port, shared by the whole collection.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the real host rather than <c>WebApplicationFactory</c>/<c>TestServer</c>. The two
/// properties under test here — that the socket is bound to loopback and nothing else, and that
/// <see cref="LoopbackHostMiddleware"/> compares the <c>Host</c> header against
/// <c>Connection.LocalPort</c> — are properties of an actual socket. <c>TestServer</c> has no real
/// binding and reports <c>LocalPort</c> as <c>0</c>, so an in-memory host would evaluate the port
/// comparison against a number that never existed and report success for a check it never ran.
/// </para>
/// <para>
/// Port <c>0</c> so a developer's own <c>dndump serve</c> on the default 5111 neither collides with
/// these tests nor, worse, answers them.
/// </para>
/// </remarks>
public sealed class LoopbackServerFixture : IAsyncLifetime {
	private WebApplication? _app;

	/// <summary>The URL the host reports for itself, as <c>dndump serve</c> would print it.</summary>
	public string Url { get; private set; } = string.Empty;

	/// <summary>The ephemeral port the OS actually assigned.</summary>
	public int Port { get; private set; }

	/// <summary>Every address the server is listening on, not merely the first one printed.</summary>
	public IReadOnlyCollection<string> Addresses { get; private set; } = [];

	public async Task InitializeAsync() {
		_app = DumpWebHost.Build(new DumpWebHostOptions {
			DumpPath = "(test)",
			Context = new NoDumpContext(),
			// Memory only. The host's default is memory over a file-system tier, and these tests have
			// no business writing cache entries into the developer's real cache directory.
			Cache = new MemoryAnalysisCache(),
			Port = 0,
		});

		try {
			await _app.StartAsync();
		} catch {
			await _app.DisposeAsync();
			_app = null;
			throw;
		}

		Url = DumpWebHost.ResolveUrl(_app);
		Port = new Uri(Url).Port;
		Addresses = ServerAddresses(_app);
	}

	public async Task DisposeAsync() {
		if (_app is null) {
			return;
		}

		await _app.StopAsync();
		await _app.DisposeAsync();
		_app = null;
	}

	/// <summary>
	/// The full address set from the server feature. <see cref="DumpWebHost.ResolveUrl"/> returns only
	/// the first, which would hide a second, wider binding — exactly the failure worth catching.
	/// </summary>
	public static IReadOnlyCollection<string> ServerAddresses(WebApplication app) {
		var feature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
		return feature?.Addresses.ToArray() ?? [];
	}
}

/// <summary>
/// The security posture of SERVER.md &#0167;6, asserted against a running server.
/// </summary>
/// <remarks>
/// This server renders memory-dump contents, and heap strings hold connection strings, tokens and
/// PII. Each control below fails silently when it regresses — a widened binding still serves
/// localhost, a dropped <c>Host</c> check still serves the browser, a stray CORS header still looks
/// like a working page. Nothing here asserts a mere absence of exceptions; every test names the
/// value or header whose presence would be the leak.
/// </remarks>
[Collection(WebSecurityCollection.Name)]
public sealed class WebSecurityTests(LoopbackServerFixture server) {
	private readonly LoopbackServerFixture _server = server;

	// ---------------------------------------------------------------------------------------------
	// 1. Loopback-only binding.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	public void ResolvedUrl_IsIPv4Loopback() {
		Assert.StartsWith("http://127.0.0.1:", _server.Url, StringComparison.Ordinal);
	}

	[Fact]
	public void EveryListeningAddress_IsIPv4Loopback() {
		Assert.NotEmpty(_server.Addresses);
		Assert.All(_server.Addresses, address =>
			Assert.StartsWith("http://127.0.0.1:", address, StringComparison.Ordinal));
	}

	[SkippableFact]
	public async Task Server_IsNotReachable_OnANonLoopbackLocalAddress() {
		var addresses = NonLoopbackIPv4Addresses();
		Skip.If(addresses.Count == 0,
			"This machine has no non-loopback IPv4 address, so there is no interface to prove the " +
			"server is absent from. Skipped rather than passed: a vacuous pass here would read as " +
			"coverage of the binding.");

		foreach (var address in addresses) {
			Assert.False(
				await CanConnect(address, _server.Port),
				$"The server accepted a connection on {address}:{_server.Port}. It must bind 127.0.0.1 only " +
				"(SERVER.md §6) — a dump's heap contents were reachable from the network.");
		}
	}

	// ---------------------------------------------------------------------------------------------
	// 3. Host-header validation.
	// ---------------------------------------------------------------------------------------------

	[Theory]
	// A name the server has no business answering to.
	[InlineData("evil.example")]
	// The right port, the wrong name: this is the DNS-rebinding shape. The connection genuinely
	// arrives on loopback, so the binding does not stop it and only the Host check does.
	[InlineData("evil.example:{port}")]
	// A real, publicly resolvable hostname that points at 127.0.0.1 — rebinding without the attacker
	// even needing to control DNS at request time.
	[InlineData("localtest.me:{port}")]
	// A loopback name on a port this server is not listening on.
	[InlineData("localhost:{otherport}")]
	// No port at all, which means port 80 and therefore not this server.
	[InlineData("localhost")]
	[InlineData("127.0.0.1")]
	// Prefix trickery: a suffix match or a StartsWith would let this through.
	[InlineData("127.0.0.1.evil.example:{port}")]
	[InlineData("localhost.evil.example:{port}")]
	public async Task HostHeader_IsRejected(string template) {
		var response = await Send(RawRequest("GET", "/health", Resolve(template)));
		Assert.Equal(400, response.StatusCode);
	}

	[Fact]
	public async Task HostHeader_Absent_IsRejected() {
		// HttpClient will not send a request without a Host header, so this has to go out over a raw
		// socket. HTTP/1.1 requires the header; a malformed request is not grounds to relax the check.
		var response = await Send(RawRequest("GET", "/health", host: null));
		Assert.Equal(400, response.StatusCode);
	}

	[Theory]
	[InlineData("localhost:{port}")]
	[InlineData("127.0.0.1:{port}")]
	[InlineData("[::1]:{port}")]
	public async Task HostHeader_IsAccepted(string template) {
		var response = await Send(RawRequest("GET", "/health", Resolve(template)));

		// 200, not merely "not 400": if /health stopped answering, a not-400 assertion would keep
		// passing on a 404 and stop proving that a legitimate host is served at all.
		Assert.Equal(200, response.StatusCode);
	}

	// ---------------------------------------------------------------------------------------------
	// 4. The rejection does not echo the attacker's Host value.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	public async Task Rejection_DoesNotEchoTheHostValue() {
		const string Attacker = "evil.example";
		var response = await Send(RawRequest("GET", "/health", Attacker));

		Assert.Equal(400, response.StatusCode);
		Assert.DoesNotContain(Attacker, response.Body, StringComparison.OrdinalIgnoreCase);
		// Headers too: reflecting the value into a Location or a diagnostic header would be the same
		// injection point wearing a different hat.
		Assert.DoesNotContain(Attacker, response.Head, StringComparison.OrdinalIgnoreCase);
	}

	// ---------------------------------------------------------------------------------------------
	// 5. The Host check runs before static files.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	public async Task StaticFiles_AreServed_ToALoopbackHost() {
		// Guards the test below from passing vacuously. If the asset were missing from the output
		// directory, a rebound request for it would 404 and the "rejected, not served" assertion would
		// hold for entirely the wrong reason.
		var response = await Send(RawRequest("GET", "/lib/htmx.min.js", Resolve("localhost:{port}")));

		Assert.Equal(200, response.StatusCode);
		Assert.True(response.Body.Length > 1000,
			$"Expected the vendored htmx bundle, got {response.Body.Length} bytes.");
	}

	[Fact]
	public async Task StaticFiles_AreRejected_ForAReboundHost() {
		var response = await Send(RawRequest("GET", "/lib/htmx.min.js", "evil.example"));

		Assert.Equal(400, response.StatusCode);
		// The middleware must be ordered ahead of UseStaticFiles, not merely ahead of the routes.
		Assert.DoesNotContain("htmx", response.Body, StringComparison.OrdinalIgnoreCase);
	}

	// ---------------------------------------------------------------------------------------------
	// 6. No CORS headers.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	public async Task SuccessResponse_CarriesNoCorsHeaders() {
		var response = await Send(RawRequest(
			"GET", "/health", Resolve("localhost:{port}"), "Origin: https://evil.example"));

		Assert.Equal(200, response.StatusCode);
		AssertNoCorsHeaders(response);
	}

	[Fact]
	public async Task RejectionResponse_CarriesNoCorsHeaders() {
		var response = await Send(RawRequest(
			"GET", "/health", "evil.example", "Origin: https://evil.example"));

		Assert.Equal(400, response.StatusCode);
		AssertNoCorsHeaders(response);
	}

	[Fact]
	public async Task Preflight_CarriesNoCorsHeaders() {
		var response = await Send(RawRequest(
			"OPTIONS", "/views/dumpheap", Resolve("localhost:{port}"),
			"Origin: https://evil.example",
			"Access-Control-Request-Method: GET"));

		AssertNoCorsHeaders(response);
	}

	// ---------------------------------------------------------------------------------------------
	// 7. No Server header.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	public async Task NoServerHeader_OnSuccess() {
		var response = await Send(RawRequest("GET", "/health", Resolve("localhost:{port}")));

		Assert.Equal(200, response.StatusCode);
		Assert.DoesNotContain(response.HeaderNames, name => name.Equals("Server", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task NoServerHeader_OnRejection() {
		var response = await Send(RawRequest("GET", "/health", "evil.example"));

		Assert.Equal(400, response.StatusCode);
		Assert.DoesNotContain(response.HeaderNames, name => name.Equals("Server", StringComparison.OrdinalIgnoreCase));
	}

	// ---------------------------------------------------------------------------------------------
	// 8. Read-only surface.
	// ---------------------------------------------------------------------------------------------

	[Theory]
	[InlineData("POST", "/")]
	[InlineData("PUT", "/")]
	[InlineData("DELETE", "/")]
	[InlineData("POST", "/views/dumpheap")]
	[InlineData("PUT", "/views/dumpheap")]
	[InlineData("DELETE", "/views/dumpheap")]
	[InlineData("POST", "/api/dumpheap")]
	[InlineData("PUT", "/api/dumpheap")]
	[InlineData("DELETE", "/api/dumpheap")]
	public async Task MutatingMethods_DoNotSucceed(string method, string target) {
		var response = await Send(RawRequest(
			method, target, Resolve("localhost:{port}"), "Content-Length: 0"));

		Assert.True(response.StatusCode >= 400,
			$"{method} {target} answered {response.StatusCode}. SERVER.md §6 says the surface is " +
			"read-only: no route may accept a mutating method.");
	}

	// ---------------------------------------------------------------------------------------------
	// Helpers.
	// ---------------------------------------------------------------------------------------------

	/// <summary>
	/// Substitutes the ephemeral port into a Host template. <c>{otherport}</c> is a port this server
	/// is certainly not on, for the wrong-port cases.
	/// </summary>
	private string Resolve(string template) => template
		.Replace("{otherport}", (_server.Port + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
		.Replace("{port}", _server.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

	private Task<RawResponse> Send(string request) => RawHttp.Send(_server.Port, request);

	private static void AssertNoCorsHeaders(RawResponse response) {
		string[] offenders = response.HeaderNames
			.Where(name => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase))
			.ToArray();

		Assert.True(offenders.Length == 0,
			"SERVER.md §6 emits no CORS headers, so cross-origin JavaScript cannot read a dump. Found: " +
			string.Join(", ", offenders));
	}

	/// <summary>
	/// Builds an HTTP/1.1 request by hand. <c>Connection: close</c> so the server ends the response by
	/// closing, which is what lets the reader below stop without guessing at framing.
	/// </summary>
	private static string RawRequest(string method, string target, string? host, params string[] extraHeaders) {
		var request = new StringBuilder()
			.Append(method).Append(' ').Append(target).Append(" HTTP/1.1\r\n");

		if (host is not null) {
			request.Append("Host: ").Append(host).Append("\r\n");
		}

		foreach (string header in extraHeaders) {
			request.Append(header).Append("\r\n");
		}

		return request.Append("Connection: close\r\n\r\n").ToString();
	}

	/// <summary>
	/// Every non-loopback IPv4 address on an interface that is up — the addresses a widened binding
	/// would become reachable on.
	/// </summary>
	private static IReadOnlyList<IPAddress> NonLoopbackIPv4Addresses() =>
		NetworkInterface.GetAllNetworkInterfaces()
			.Where(nic => nic.OperationalStatus == OperationalStatus.Up)
			.Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
			.Select(unicast => unicast.Address)
			.Where(address => address.AddressFamily == AddressFamily.InterNetwork)
			.Where(address => !IPAddress.IsLoopback(address))
			.Distinct()
			.ToArray();

	/// <summary>
	/// Whether anything accepts a TCP connection at this address. A refusal and a timeout both mean
	/// "nothing listening"; only a completed connection means the binding is wider than loopback.
	/// </summary>
	internal static async Task<bool> CanConnect(IPAddress address, int port) {
		try {
			using var client = new TcpClient();
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
			await client.ConnectAsync(address, port, timeout.Token);
			return client.Connected;
		} catch (Exception ex) when (ex is SocketException or OperationCanceledException) {
			return false;
		}
	}
}

/// <summary>
/// That no inherited environment can move the binding off loopback (SERVER.md &#0167;6).
/// </summary>
/// <remarks>
/// A developer shell, a CI runner or a container image can all carry <c>ASPNETCORE_URLS</c>, and the
/// ASP.NET Core default is to honor it. Here it must lose to the explicit <c>Listen</c> call, because
/// the cost of it winning is a memory dump served on every network interface.
/// </remarks>
[Collection(WebSecurityCollection.Name)]
public sealed class WebBindingEnvironmentTests {
	[Fact]
	public async Task WideningEnvironment_DoesNotWidenTheAddresses() {
		await WithWideningEnvironment((app, url) => {
			Assert.StartsWith("http://127.0.0.1:", url, StringComparison.Ordinal);

			var addresses = LoopbackServerFixture.ServerAddresses(app);
			Assert.NotEmpty(addresses);
			Assert.All(addresses, address =>
				Assert.StartsWith("http://127.0.0.1:", address, StringComparison.Ordinal));

			return Task.CompletedTask;
		});
	}

	[SkippableFact]
	public async Task WideningEnvironment_LeavesTheServerUnreachableOffLoopback() {
		var addresses = NonLoopbackIPv4Addresses();
		Skip.If(addresses.Count == 0,
			"This machine has no non-loopback IPv4 address to prove the widened environment failed to " +
			"reach. Skipped rather than passed.");

		await WithWideningEnvironment(async (_, url) => {
			int port = new Uri(url).Port;
			foreach (var address in addresses) {
				Assert.False(
					await WebSecurityTests.CanConnect(address, port),
					$"ASPNETCORE_URLS widened the binding: the server answered on {address}:{port}.");
			}
		});
	}

	/// <summary>
	/// Runs <paramref name="body"/> against a host built with the binding variables set to a wildcard,
	/// then restores the environment. The restore is in a <c>finally</c> because
	/// <see cref="DumpWebHost.Build"/> clears these variables as part of its job — leaving them cleared
	/// would silently reconfigure every test that runs afterwards in this process.
	/// </summary>
	private static async Task WithWideningEnvironment(Func<WebApplication, string, Task> body) {
		string? savedUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
		string? savedPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
		WebApplication? app = null;

		try {
			Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://0.0.0.0:0");
			Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", "0");

			app = DumpWebHost.Build(new DumpWebHostOptions {
				DumpPath = "(test)",
				Context = new NoDumpContext(),
				Cache = new MemoryAnalysisCache(),
				Port = 0,
			});

			await app.StartAsync();
			await body(app, DumpWebHost.ResolveUrl(app));
		} finally {
			if (app is not null) {
				await app.StopAsync();
				await app.DisposeAsync();
			}

			Environment.SetEnvironmentVariable("ASPNETCORE_URLS", savedUrls);
			Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", savedPorts);
		}
	}

	private static IReadOnlyList<IPAddress> NonLoopbackIPv4Addresses() =>
		NetworkInterface.GetAllNetworkInterfaces()
			.Where(nic => nic.OperationalStatus == OperationalStatus.Up)
			.Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
			.Select(unicast => unicast.Address)
			.Where(address => address.AddressFamily == AddressFamily.InterNetwork)
			.Where(address => !IPAddress.IsLoopback(address))
			.Distinct()
			.ToArray();
}

/// <summary>
/// <see cref="LoopbackHostMiddleware.IsLoopbackHost"/> as a decision table, independent of the HTTP
/// plumbing — so a regression in the rule is attributable to the rule and not to routing, ordering or
/// the socket.
/// </summary>
public sealed class LoopbackHostDecisionTests {
	[Theory]
	// The three names a loopback server may be addressed by, on the port it is listening on.
	[InlineData("localhost:5111", 5111, true)]
	[InlineData("127.0.0.1:5111", 5111, true)]
	[InlineData("[::1]:5111", 5111, true)]
	// Host names are case-insensitive.
	[InlineData("LOCALHOST:5111", 5111, true)]
	// An absent port means the scheme default, 80 — correct only when that is genuinely the port.
	[InlineData("localhost", 80, true)]
	[InlineData("localhost", 5111, false)]
	// A loopback name on some other server's port.
	[InlineData("localhost:5112", 5111, false)]
	[InlineData("127.0.0.1:80", 5111, false)]
	// Names that are not loopback, including ones that resolve to it.
	[InlineData("evil.example:5111", 5111, false)]
	[InlineData("localtest.me:5111", 5111, false)]
	[InlineData("192.168.1.10:5111", 5111, false)]
	// Prefix and suffix trickery against a sloppier comparison.
	[InlineData("127.0.0.1.evil.example:5111", 5111, false)]
	[InlineData("localhost.evil.example:5111", 5111, false)]
	[InlineData("evil-localhost:5111", 5111, false)]
	[InlineData("evil.example.localhost:5111", 5111, false)]
	// Empty is not a Host.
	[InlineData("", 5111, false)]
	public void IsLoopbackHost_Decides(string hostHeader, int localPort, bool expected) {
		var context = new DefaultHttpContext();
		context.Request.Host = new HostString(hostHeader);
		context.Connection.LocalPort = localPort;

		Assert.Equal(expected, LoopbackHostMiddleware.IsLoopbackHost(context));
	}

	[Fact]
	public void IsLoopbackHost_RejectsAnAbsentHost() {
		// Not the same case as the empty string above: this context never had a Host set at all.
		var context = new DefaultHttpContext();
		context.Connection.LocalPort = 5111;

		Assert.False(LoopbackHostMiddleware.IsLoopbackHost(context));
	}
}

/// <summary>A parsed HTTP response, read straight off the socket.</summary>
internal sealed record RawResponse(int StatusCode, string Head, string Body, IReadOnlyList<string> HeaderNames) {
	public static RawResponse Parse(string raw) {
		int separator = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
		string head = separator < 0 ? raw : raw[..separator];
		string body = separator < 0 ? string.Empty : raw[(separator + 4)..];

		string[] lines = head.Split("\r\n");
		int status = 0;
		if (lines.Length > 0) {
			string[] parts = lines[0].Split(' ');
			if (parts.Length > 1) {
				_ = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out status);
			}
		}

		string[] headerNames = lines.Skip(1)
			.Select(line => {
				int colon = line.IndexOf(':', StringComparison.Ordinal);
				return colon > 0 ? line[..colon].Trim() : string.Empty;
			})
			.Where(name => name.Length > 0)
			.ToArray();

		return new RawResponse(status, head, body, headerNames);
	}
}

/// <summary>
/// A hand-rolled HTTP client. <see cref="HttpClient"/> will not send a malformed or absent
/// <c>Host</c>, and it normalizes the header it does send — which is precisely the input the
/// <c>Host</c> check exists to refuse, so it has to be written onto the socket directly.
/// </summary>
internal static class RawHttp {
	public static async Task<RawResponse> Send(int port, string request) {
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);

		using var stream = client.GetStream();
		await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
		await stream.FlushAsync(timeout.Token);

		var received = new MemoryStream();
		byte[] chunk = new byte[8192];
		while (true) {
			int read = await stream.ReadAsync(chunk, timeout.Token);
			if (read == 0) {
				break;
			}

			received.Write(chunk, 0, read);
		}

		return RawResponse.Parse(Encoding.UTF8.GetString(received.ToArray()));
	}
}