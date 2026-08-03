using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DotNetDump.Core.Models;
using DotNetDump.Web;
using DotNetDump.Web.Analysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
// WaitForShutdownAsync is an IHost extension; WebApplication implements IHost.
using Microsoft.Extensions.Hosting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump serve</c> -- the local web interface (docs/web/SERVER.md &#0167;1.2). Starts
/// <c>DotNetDump.Web</c> in this process, bound to loopback by default (or every interface under
/// <see cref="ContainerOption"/>, for Docker), against one dump for the lifetime of the process.
/// </summary>
/// <remarks>
/// The dependency runs one way: the CLI references the web host so <c>serve</c> can start it
/// in-process, and the web host never references the CLI. Every other command pays nothing for it
/// -- the ASP.NET Core assemblies are only loaded when this command's action actually runs.
/// </remarks>
public static class ServeCommand {
	public static readonly Option<int> PortOption = new("--port") {
		Description = $"Loopback port ({DumpWebHost.DefaultPort} by default; 0 for an ephemeral one).",
		DefaultValueFactory = _ => DumpWebHost.DefaultPort,
	};

	public static readonly Option<bool> NoOpenOption = new("--no-open") {
		Description = "Do not open a browser on start.",
		DefaultValueFactory = _ => false,
	};

	public static readonly Option<bool> NoWarmOption = new("--no-warm") {
		Description = "Skip the background cache warm, for a cheap view of a large dump immediately.",
		DefaultValueFactory = _ => false,
	};

	public static readonly Option<bool> ContainerOption = new("--container") {
		Description = "Bind every interface instead of loopback only. For Docker only: Docker's " +
			"'-p' port publishing cannot reach a Kestrel bound to the container's own loopback " +
			"(docs/web/SERVER.md §6.1), so this widens the bind inside the container while the " +
			"actual 'only this machine can reach it' guarantee moves to publishing the port as " +
			"'-p 127.0.0.1:<port>:<port>' -- never bare '-p <port>:<port>'. Do not pass this " +
			"outside a container; it removes the network-level protection a direct 'dndump serve' " +
			"relies on.",
		DefaultValueFactory = _ => false,
	};

	public static Command Create() {
		var command = new Command("serve", "Local web interface for interactive analysis. Loopback only, no auth.");
		command.Options.Add(PortOption);
		command.Options.Add(NoOpenOption);
		command.Options.Add(NoWarmOption);
		command.Options.Add(ContainerOption);

		command.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);
			int port = parseResult.GetValue(PortOption);
			bool noOpen = parseResult.GetValue(NoOpenOption);
			bool noWarm = parseResult.GetValue(NoWarmOption);
			bool container = parseResult.GetValue(ContainerOption);

			// Resolved first so the path is in hand for the banner and the header bar; the same
			// --dump -> DNDUMP_PATH -> .dndump/session.json precedence as every other command, so
			// 'dndump use X && dndump serve' works the way a user would assume.
			var (dumpPath, dacPath) = DumpResolver.Resolve(dumpOption, dacOption, Directory.GetCurrentDirectory());

			// quiet: true unconditionally. Core's resolver prints "Dump: <path>" to stderr, which is
			// right for a command that emits one result and exits and wrong for a server that prints
			// a startup banner; this command prints its own, below.
			var context = DumpResolver.ResolveAndLoad(dumpPath, dacPath, quiet: true);

			// Eagerly, during startup: a serve that comes up and then fails on the first request is
			// worse than one that refuses to start. A load failure propagates out of here as a
			// DumpLoadException and is mapped to an exit code exactly as it is for every other
			// command.
			var app = DumpWebHost.Build(new DumpWebHostOptions {
				DumpPath = dumpPath,
				Context = context,
				Port = port,
				BindAnyInterface = container,
			});

			await app.StartAsync(cancellationToken);

			string url = DumpWebHost.ResolveUrl(app);
			if (!quiet) {
				int resolvedPort = new Uri(url).Port;
				// "Loopback only" stops being true the moment --container widens the bind (see
				// ContainerOption's own description) -- saying so anyway here would be exactly the
				// kind of claim that reads as coverage it does not provide.
				string reachability = container
					? $"reachable only if this container's port is published as 127.0.0.1:{resolvedPort}:{resolvedPort}"
					: "loopback only";
				Console.Error.WriteLine($"Dump: {dumpPath}");
				Console.Error.WriteLine($"dndump serve listening on {url} -- {reachability}, no authentication.");
				Console.Error.WriteLine("Press Ctrl+C to stop.");
			}

			if (!noWarm) {
				WarmCache(app);
			}

			if (!noOpen) {
				OpenBrowser(url);
			}

			await app.WaitForShutdownAsync(cancellationToken);
			return ExitCodes.Success;
		});

		return command;
	}

	/// <summary>
	/// Queues the heap walk before any request arrives, so the user is still reading the overview
	/// page while the expensive part completes.
	/// </summary>
	/// <remarks>
	/// Phase 6.1 extends this to the full usage order -- heap statistics, then heap exceptions, then
	/// sync blocks -- and adds the pending-state UI that makes a cold walk legible. Phase 2 warms
	/// only heap statistics, which is what <c>GET /</c> renders, so that <c>--no-warm</c> is a flag
	/// that does something rather than one reserved for later.
	/// <para>
	/// Deliberately not awaited: the whole point is that startup does not block on a walk. The queue
	/// serializes it against real requests, so the first request either finds the result cached or
	/// waits behind the walk it would have triggered anyway.
	/// </para>
	/// </remarks>
	private static void WarmCache(WebApplication app) {
		var queue = app.Services.GetRequiredService<IAnalysisQueue>();
		_ = queue.Enqueue(
			(session, _) => session.Heap.GetHeapStatistics(new QueryParameters()),
			"warming heap statistics",
			CancellationToken.None);
	}

	private static void OpenBrowser(string url) {
		try {
			if (OperatingSystem.IsWindows()) {
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			} else if (OperatingSystem.IsMacOS()) {
				Process.Start("open", url);
			} else {
				Process.Start("xdg-open", url);
			}
		} catch (Exception ex) {
			// Headless containers and SSH sessions have no browser. That is not a failure of the
			// server, so it must not look like one -- print the URL and carry on.
			Console.Error.WriteLine($"Could not open a browser ({ex.Message}). Open {url} yourself.");
		}
	}
}