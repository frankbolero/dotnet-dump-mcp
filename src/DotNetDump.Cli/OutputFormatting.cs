using System;

namespace DotNetDump.Cli;

/// <summary>
/// Dispatches a Core model to whichever formatter matches the resolved <c>--format</c> value
/// (CLI_DESIGN.md &#0167;3.3). Every command hands its result through here rather than switching on
/// the format string itself, so the three-formatter dispatch stays in one place as Phase 6 adds
/// commands.
/// </summary>
internal static class OutputFormatting {
	public static string Render<T>(string format, T value, Func<T, string> markdown, Func<T, string> json, Func<T, string> tsv) => format switch {
		"json" => json(value),
		"tsv" => tsv(value),
		_ => markdown(value),
	};
}