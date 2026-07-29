using System;

using CoreDumpLoadException = DotNetDump.Core.Utilities.DumpLoadException;

namespace DotNetDump.Cli;

/// <summary>
/// The dump could not be loaded -- missing file, no CLR found, DAC mismatch (CLI_DESIGN.md
/// &#0167;3.4, exit code 3). This CLI-side exception wraps the Core-side version for compatibility.
/// </summary>
public sealed class DumpLoadException : Exception {
	public DumpLoadException(string message, Exception inner) : base(message, inner) {
	}

	/// <summary>Wraps a <see cref="CoreDumpLoadException"/> as a CLI exception.</summary>
	public DumpLoadException(CoreDumpLoadException inner) : base(inner.Message, inner) {
	}
}