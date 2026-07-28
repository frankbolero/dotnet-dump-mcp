using System;

namespace DotNetDump.Cli;

/// <summary>
/// The dump could not be loaded -- missing file, no CLR found, DAC mismatch (CLI_DESIGN.md
/// &#0167;3.4, exit code 3). Wraps whatever <c>IDumpContext.Load</c> threw so the top-level handler
/// can distinguish "dump resolved but failed to load" from "dump analysis failed" (exit code 1)
/// without inspecting exception types from Core, which are not part of this CLI's contract.
/// </summary>
public sealed class DumpLoadException : Exception {
	public DumpLoadException(string message, Exception inner) : base(message, inner) {
	}
}