using System;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// No dump could be resolved from any of the three sources (--dump flag, DNDUMP_PATH environment
/// variable, or .dndump/session.json searched upward). This is a usage error indicating that the
/// user must provide a dump before any analysis can proceed.
/// </summary>
public sealed class DumpResolutionException : Exception {
	public DumpResolutionException(string message) : base(message) {
	}
}