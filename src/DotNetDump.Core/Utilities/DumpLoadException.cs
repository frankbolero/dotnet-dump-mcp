using System;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// The dump could not be loaded -- missing file, no CLR found, DAC mismatch. Wraps whatever
/// <c>IDumpContext.Load</c> threw so callers can distinguish "dump resolved but failed to load"
/// from other exceptions without inspecting exception types from Core's dependencies.
/// </summary>
public sealed class DumpLoadException : Exception {
	public DumpLoadException(string message, Exception inner) : base(message, inner) {
	}
}