using System;

using DotNetDump.Core;
using DotNetDump.Core.Utilities;

using CoreDumpResolver = DotNetDump.Core.Utilities.DumpResolver;

namespace DotNetDump.Cli;

/// <summary>
/// CLI-side facade for <see cref="DotNetDump.Core.Utilities.DumpResolver"/>, the Core-side
/// implementation. Maps Core exceptions to CLI exceptions so the command layer sees the CLI types
/// it expects, while the Core library remains CLI-independent.
/// </summary>
public static class DumpResolver {
	public const string DumpPathVariable = CoreDumpResolver.DumpPathVariable;

	/// <summary>
	/// Resolves the dump and DAC paths without loading anything. Delegates to the Core implementation.
	/// </summary>
	public static (string DumpPath, string? DacPath) Resolve(
		string? dumpOption,
		string? dacOption,
		string searchStartDirectory,
		Func<string, string?>? getEnvironmentVariable = null) {

		try {
			return CoreDumpResolver.Resolve(dumpOption, dacOption, searchStartDirectory, getEnvironmentVariable);
		} catch (DumpResolutionException ex) {
			throw new CliUsageException(ex);
		}
	}

	/// <summary>
	/// Resolves the dump per <see cref="Resolve"/> against the real environment and current directory,
	/// then loads it. Delegates to the Core implementation and maps exceptions.
	/// </summary>
	public static IDumpContext ResolveAndLoad(string? dumpOption, string? dacOption, bool quiet = false) {
		try {
			return CoreDumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
		} catch (DumpResolutionException ex) {
			throw new CliUsageException(ex);
		} catch (Core.Utilities.DumpLoadException ex) {
			throw new DumpLoadException(ex);
		}
	}
}