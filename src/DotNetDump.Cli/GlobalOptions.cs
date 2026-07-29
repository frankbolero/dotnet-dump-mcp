using System;
using System.CommandLine;

namespace DotNetDump.Cli;

/// <summary>
/// Shared option definitions from CLI_DESIGN.md &#0167;3.2.
///
/// <c>Dump</c>, <c>Dac</c>, <c>Format</c> and <c>Quiet</c> are genuinely global: every command,
/// including future ones, needs to know which dump to open and how to render output. They are
/// added to the root command with <c>Recursive = true</c>, so any subcommand's <see
/// cref="System.CommandLine.ParseResult"/> can read them without redeclaring them.
///
/// <c>--limit</c>/<c>--offset</c>/<c>--sort</c>/<c>--order</c> are shared *definitions* -- one
/// source of truth for names and defaults -- but are deliberately not recursive: only commands
/// that page or sort a result (like <c>dumpheap</c>) add them, via the factory methods below.
/// <c>use</c>, <c>info</c> and <c>commands</c> have nothing to page and would silently accept-but-
/// ignore them if they were global, which is worse than a "no such option" error.
/// </summary>
public static class GlobalOptions {
	public static readonly Option<string?> Dump = new("--dump") {
		Description = "Dump file to analyze.",
		Recursive = true,
	};

	public static readonly Option<string?> Dac = new("--dac") {
		Description = "Explicit DAC path; overrides detection.",
		Recursive = true,
	};

	public static readonly Option<string> Format = CreateFormatOption();

	public static readonly Option<bool> Quiet = new("--quiet") {
		Description = "Suppress the informational header on stderr.",
		DefaultValueFactory = _ => false,
		Recursive = true,
	};

	private static Option<string> CreateFormatOption() {
		var option = new Option<string>("--format") {
			Description = "Output format: md, json or tsv.",
			DefaultValueFactory = _ => "md",
			Recursive = true,
		};
		option.AcceptOnlyFromAmong("md", "json", "tsv");
		return option;
	}

	/// <summary>A fresh <c>--limit</c> instance per command that needs one. Unlike the four above,
	/// this is a factory rather than a shared singleton: <c>--sort</c> and <c>--order</c> need
	/// per-command valid values and defaults anyway (CLI_DESIGN.md &#0167;4.2-&#0167;4.5's "Sort fields"
	/// column differs by command), so every paging/sorting option follows the same
	/// factory-per-command shape for Phase 6 to copy consistently.</summary>
	public static Option<int> CreateLimitOption() => new("--limit") {
		Description = "Max rows to return.",
		DefaultValueFactory = _ => 50,
	};

	public static Option<int> CreateOffsetOption() => new("--offset") {
		Description = "Rows to skip.",
		DefaultValueFactory = _ => 0,
	};

	/// <summary>
	/// A fresh <c>--filter</c> instance per command that honors filtering. Repeatable (each
	/// occurrence adds one <c>&lt;field&gt;&lt;op&gt;&lt;value&gt;</c> expression, parsed by
	/// <see cref="FilterExpressionParser"/> and ANDed together per DATA_CONTRACT.md &#0167;2.4).
	/// Factory-per-command like <see cref="CreateLimitOption"/>/<see cref="CreateOffsetOption"/>,
	/// and for the same reason it is not offered here as a recursive singleton: commands that honor
	/// no filter at all (<c>eeheap</c>, <c>threadpool</c>, <c>verifyheap</c>, <c>info</c>, the
	/// detail commands) must not advertise <c>--filter</c> in their own <c>--help</c> only to have
	/// <see cref="DotNetDump.Core.UnsupportedFilterException"/> reject it at run time -- the option
	/// should not exist there in the first place.
	/// <para>
	/// <paramref name="honoredFieldsDescription"/> is required, not defaulted, so a command wiring
	/// this in cannot forget to name the fields it actually honors (DATA_CONTRACT.md &#0167;2.3's
	/// per-method matrix) -- the task's own instruction that <c>--help</c> must name them per
	/// command, e.g. so <c>clrmodules --help</c> does not advertise <c>gen</c>.
	/// </para>
	/// </summary>
	public static Option<string[]> CreateFilterOption(string honoredFieldsDescription) => new("--filter") {
		Description = honoredFieldsDescription,
		DefaultValueFactory = _ => Array.Empty<string>(),
	};
}