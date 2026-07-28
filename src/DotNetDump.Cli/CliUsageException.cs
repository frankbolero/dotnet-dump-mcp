using System;

namespace DotNetDump.Cli;

/// <summary>
/// A usage error distinct from what System.CommandLine's own parser catches (unknown command, bad
/// option): specifically, no dump could be resolved from any of the three sources in
/// CLI_DESIGN.md &#0167;3.1. Maps to exit code 2 (&#0167;3.4).
/// </summary>
public sealed class CliUsageException : Exception {
	public CliUsageException(string message) : base(message) {
	}
}