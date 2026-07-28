using System;
using System.IO;

using DotNetDump.Core;

namespace DotNetDump.Cli;

/// <summary>
/// Resolves which dump a command should analyze, per the precedence in CLI_DESIGN.md &#0167;3.1:
/// <c>--dump</c> flag, then <c>DNDUMP_PATH</c>, then <c>.dndump/session.json</c> searched upward
/// from the working directory. Also loads the resolved dump into a fresh
/// <see cref="IDumpContext"/>, wrapping any load failure as a <see cref="DumpLoadException"/> so
/// the top-level handler can tell "no dump could even be found" (usage error, exit 2) apart from
/// "a dump was found but would not open" (exit 3).
/// </summary>
public static class DumpResolver {
	public const string DumpPathVariable = "DNDUMP_PATH";

	/// <summary>
	/// Resolves the dump and DAC paths without loading anything. Exposed separately from
	/// <see cref="ResolveAndLoad"/> so the precedence logic itself is unit-testable against a fake
	/// environment-variable source and an arbitrary starting directory, with no process-global
	/// state involved.
	/// </summary>
	public static (string DumpPath, string? DacPath) Resolve(
		string? dumpOption,
		string? dacOption,
		string searchStartDirectory,
		Func<string, string?>? getEnvironmentVariable = null) {

		getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

		if (!string.IsNullOrWhiteSpace(dumpOption)) {
			return (dumpOption, dacOption);
		}

		string? envPath = getEnvironmentVariable(DumpPathVariable);
		if (!string.IsNullOrWhiteSpace(envPath)) {
			return (envPath, dacOption);
		}

		var session = SessionFile.FindUpward(searchStartDirectory);
		if (session != null && !string.IsNullOrWhiteSpace(session.DumpPath)) {
			return (session.DumpPath, dacOption ?? session.DacPath);
		}

		throw new CliUsageException(
			"No dump specified. Pass --dump <path>, set DNDUMP_PATH, or run 'dndump use <path>' first.");
	}

	/// <summary>Resolves the dump per <see cref="Resolve"/> against the real environment and
	/// current directory, then loads it. Caller owns disposing the returned context.</summary>
	public static IDumpContext ResolveAndLoad(string? dumpOption, string? dacOption, bool quiet = false) {
		var (dumpPath, dacPath) = Resolve(dumpOption, dacOption, Directory.GetCurrentDirectory());

		if (!quiet) {
			Console.Error.WriteLine($"Dump: {dumpPath}");
		}

		var context = new DumpContext();
		try {
			context.Load(dumpPath, dacPath);
		} catch (Exception ex) {
			throw new DumpLoadException($"Could not load dump '{dumpPath}': {ex.Message}", ex);
		}

		return context;
	}
}