using System;
using System.Collections.Generic;

using DotNetDump.Core.Models;

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

	/// <summary>
	/// For analyzer methods Phase 4 wired through <c>IAnalysisCache</c>: <see cref="JsonFormatter"/>
	/// takes the full <see cref="PagedResult{T}"/> to report real pagination metadata, while Markdown
	/// and TSV render only the already-sliced page and haven't changed shape.
	/// </summary>
	public static string Render<T>(string format, PagedResult<T> value, Func<IEnumerable<T>, string> markdown, Func<PagedResult<T>, string> json, Func<IEnumerable<T>, string> tsv) => format switch {
		"json" => json(value),
		"tsv" => tsv(value.Items),
		_ => markdown(value.Items),
	};
}