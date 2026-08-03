using System.Net;

using DotNetDump.Core;
using DotNetDump.Core.Caching;
using DotNetDump.Web.Analysis;
using DotNetDump.Web.Rendering;
using DotNetDump.Web.Routes;
using DotNetDump.Web.Security;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNetDump.Web;

/// <summary>How the host is configured. Everything the caller may choose, and nothing it may not.</summary>
public sealed class DumpWebHostOptions {
	/// <summary>The resolved dump path, for the header bar and for logging. Display only.</summary>
	public required string DumpPath { get; init; }

	/// <summary>An already-loaded dump. The host takes ownership and disposes it on shutdown.</summary>
	public required IDumpContext Context { get; init; }

	/// <summary>
	/// Result cache. Defaults to memory over disk — unlike the CLI, this process is long-lived, so
	/// there genuinely is a second <c>GetOrCompute</c> for RAM to serve.
	/// </summary>
	public IAnalysisCache? Cache { get; init; }

	/// <summary>Loopback port. <c>0</c> asks the OS for an ephemeral one.</summary>
	public int Port { get; init; } = DumpWebHost.DefaultPort;

	/// <summary>
	/// Binds every interface instead of loopback only. Exists for exactly one reason: inside a
	/// Docker container, Kestrel binding to <c>127.0.0.1</c> means the container's own loopback,
	/// and Docker's <c>-p</c> port publishing delivers packets to the container's routable
	/// interface, never to its loopback — so a loopback-bound Kestrel is unreachable from the host
	/// even with the port published. Set this only when the process's own network namespace is not
	/// the host's; the "only this machine can reach it" guarantee then comes entirely from
	/// publishing the port as <c>-p 127.0.0.1:&lt;port&gt;:&lt;port&gt;</c> (SERVER.md &#0167;6.1)
	/// rather than from this bind. <see cref="Security.LoopbackHostMiddleware"/> needs no change to
	/// remain correct here — it already validates the <c>Host</c> header and
	/// <c>Connection.LocalPort</c>, independent of which address Kestrel bound to.
	/// </summary>
	public bool BindAnyInterface { get; init; }
}

/// <summary>
/// Builds the <c>dndump serve</c> host: an ASP.NET Core application that renders HTML fragments
/// from <c>DotNetDump.Core</c> analyzer results, bound to loopback by default (or every interface
/// under <see cref="DumpWebHostOptions.BindAnyInterface"/>, for Docker), one dump per process.
/// </summary>
public static class DumpWebHost {
	public const int DefaultPort = 5111;

	/// <summary>
	/// Environment variables that can widen or redirect the binding. Cleared before the builder
	/// reads configuration, so no inherited environment can move this server off loopback
	/// (SERVER.md &#0167;6). <c>Listen</c> below already takes precedence over all of them; this is the
	/// second lock on the same door, because the cost of it being wrong is heap contents on a
	/// network interface.
	/// </summary>
	private static readonly string[] BindingVariables = [
		"ASPNETCORE_URLS",
		"ASPNETCORE_HTTP_PORTS",
		"ASPNETCORE_HTTPS_PORTS",
		"DOTNET_URLS",
	];

	public static WebApplication Build(DumpWebHostOptions options) {
		ArgumentNullException.ThrowIfNull(options);

		foreach (string variable in BindingVariables) {
			Environment.SetEnvironmentVariable(variable, null);
		}

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
			// The entry assembly is dndump, the CLI. Naming this assembly instead is what lets MVC
			// find the Razor views compiled into it.
			ApplicationName = typeof(DumpWebHost).Assembly.GetName().Name,
			// Never the current working directory: it is wherever the user happened to run 'dndump
			// serve' from, and nothing this host needs lives there.
			ContentRootPath = AppContext.BaseDirectory,
			// No developer exception page. A stack trace rendered into a response from a process
			// holding a memory dump is an information leak with no upside on a local tool.
			EnvironmentName = Environments.Production,
		});

		// Belt and braces alongside clearing the environment: blanks any 'urls' arriving from a
		// configuration source rather than the environment.
		builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
		// Refuse ASPNETCORE_HOSTINGSTARTUPASSEMBLIES. It loads arbitrary assemblies into the process
		// before Startup runs, which is not something a dump reader should honor from its ambient
		// environment.
		builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");

		builder.WebHost.ConfigureKestrel(kestrel => {
			// Explicit, not UseUrls, not honoring any inherited configuration. IPv4 loopback only
			// unless BindAnyInterface opts into every interface for Docker's sake (see that
			// property's own remarks) -- there is still no general-purpose --bind option, only this
			// one deliberate, documented exception. A single Listen call also fixes the port for
			// '--port 0': binding a second address would take a different ephemeral port and there
			// would be no single URL to print.
			kestrel.Listen(options.BindAnyInterface ? IPAddress.Any : IPAddress.Loopback, options.Port);
			kestrel.AddServerHeader = false;
		});

		builder.Logging.ClearProviders();
		builder.Logging.AddSimpleConsole(formatter => formatter.SingleLine = true);
		// Everything to stderr, so stdout stays clean for anything a caller might pipe -- and so
		// diagnostics never interleave with the URL banner 'dndump serve' writes there.
		builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
		builder.Logging.SetMinimumLevel(LogLevel.Warning);

		builder.Services
			.AddControllersWithViews()
			// Explicit: with the CLI as the entry assembly, default part discovery does not reach
			// the views compiled into this one.
			.AddApplicationPart(typeof(DumpWebHost).Assembly);

		// Keys in memory, never on disk. MVC's view services pull in Data Protection for antiforgery,
		// whose default provider writes a key ring into the user's home directory on first use. This
		// server has no POST route, no session and no cookie to protect, and "read-only" in
		// SERVER.md §6 has to mean it leaves nothing behind either.
		builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

		builder.Services.AddSingleton<IFragmentRenderer, RazorFragmentRenderer>();
		builder.Services.AddSingleton(new LoadedDump(options.DumpPath));

		// The queue is the only thing holding the dump, and AnalysisSession is not registered at
		// all -- a handler cannot reach an analyzer except by enqueueing work (SERVER.md §3).
		builder.Services.AddSingleton<IAnalysisQueue>(services => new AnalysisQueue(
			options.DumpPath,
			options.Context,
			options.Cache ?? new TieredAnalysisCache(new MemoryAnalysisCache(), new FileSystemAnalysisCache()),
			services.GetRequiredService<ILogger<AnalysisQueue>>()));

		// Memoized ahead of the dump header bar that renders on every page -- see the type's own
		// remarks for why this cannot be a per-request Enqueue call.
		builder.Services.AddSingleton<DumpInfoService>();

		var app = builder.Build();

		// First, ahead of static files: a rebound host must not be served assets either.
		app.UseMiddleware<LoopbackHostMiddleware>();

		app.UseStaticFiles(new StaticFileOptions {
			// Rooted at the binary's own directory, never the working directory: 'dndump serve' is
			// run from wherever the user happens to be, and the assets live next to the assembly.
			FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot")),
			// Nothing outside the vendored set should ever be reachable, and an unknown extension
			// served as octet-stream is a download prompt rather than a 404.
			ServeUnknownFileTypes = false,
			ContentTypeProvider = new FileExtensionContentTypeProvider(),
		});

		app.MapDumpRoutes();
		app.MapTreeRoutes();

		// The queue owns the dump context; disposing it here is what closes the dump, and it must
		// happen after the server has stopped accepting requests.
		app.Lifetime.ApplicationStopped.Register(() => {
			if (app.Services.GetService<IAnalysisQueue>() is IDisposable disposable) {
				disposable.Dispose();
			}
		});

		return app;
	}

	/// <summary>
	/// The URL to print and to open, once the server has started. Only the port is read back from
	/// the server (needed because <see cref="DumpWebHostOptions.Port"/> is <c>0</c> when the OS
	/// picked one); the host is always reported as <c>127.0.0.1</c> regardless of
	/// <see cref="DumpWebHostOptions.BindAnyInterface"/>. From outside the process, loopback is how
	/// this server is reached either way — directly, or through Docker's own
	/// <c>-p 127.0.0.1:&lt;port&gt;:&lt;port&gt;</c> publish — whereas Kestrel's own report of an
	/// any-interface bind (e.g. <c>http://[::]:&lt;port&gt;</c>) is not a usable client URL and would
	/// be a confusing thing to print or hand to a browser.
	/// </summary>
	public static string ResolveUrl(WebApplication app) {
		var addresses = app.Services
			.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
			.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

		string? address = addresses?.Addresses.FirstOrDefault();
		if (address is null) {
			return $"http://127.0.0.1:{DefaultPort}";
		}

		int port = new Uri(address).Port;
		return $"http://127.0.0.1:{port}";
	}
}