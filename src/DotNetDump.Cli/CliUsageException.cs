using System;

using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli;

/// <summary>
/// A usage error distinct from what System.CommandLine's own parser catches (unknown command, bad
/// option). Maps to exit code 2 (CLI_DESIGN.md &#0167;3.4).
/// </summary>
public sealed class CliUsageException : Exception {
	public CliUsageException(string message) : base(message) {
	}

	/// <summary>Wraps a <see cref="DumpResolutionException"/> as a CLI usage error.</summary>
	public CliUsageException(DumpResolutionException inner) : base(inner.Message, inner) {
	}
}