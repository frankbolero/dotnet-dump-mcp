using System;

using DotNetDump.Core;

namespace DotNetDump.Cli;

/// <summary>
/// Maps an exception escaping a command action to one of the four exit codes in CLI_DESIGN.md
/// &#0167;3.4. Extracted from <see cref="CliRunner"/> as a pure function so the mapping itself is unit
/// testable without invoking a real command (CLI_DESIGN.md &#0167;7: "exit-code assertions").
/// </summary>
internal static class ExitCodeMapper {
	public static int Map(Exception ex) => ex switch {
		CliUsageException => ExitCodes.UsageError,
		// A filter naming a field the target analyzer method does not honor is a usage error, not
		// an analysis failure (DATA_CONTRACT.md &#0167;2.3): the request itself was invalid, and the
		// analyzer never ran. Same exit code CliUsageException gets, for the same reason.
		UnsupportedFilterException => ExitCodes.UsageError,
		DumpLoadException => ExitCodes.DumpLoadFailure,
		_ => ExitCodes.AnalysisError,
	};
}