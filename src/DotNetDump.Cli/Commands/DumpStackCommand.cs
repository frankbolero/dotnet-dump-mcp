using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpstack</c> -- CLI_DESIGN.md §4.3. Detailed per-thread stacks.
/// Sort fields: <c>ManagedThreadId</c> (default), <c>OSThreadId</c>.
/// </summary>
/// <remarks>
/// <c>ThreadStackInfoFilter</c>'s own doc comment (Filtering/ThreadStackInfoFilter.cs) and
/// DATA_CONTRACT.md &#0167;2.3 both name this method as backing <c>dumpstack</c> <i>and</i>
/// <c>clrstack</c>. As implemented, <c>clrstack</c> (and <c>eestack</c>) call
/// <see cref="ThreadAnalyzer.GetStackTraceGroups"/> instead -- a grouped summary with no
/// <c>QueryParameters</c>, no pagination and no filter plumbing at all. Wiring <c>--filter</c> into
/// <c>clrstack</c> would need that method to grow filter support first, which is a Core change task
/// 0.4 is explicitly not scoped to make; only <c>dumpstack</c> is wired here. Flagged as a finding
/// for the lead rather than silently reconciled.
/// </remarks>
public static class DumpStackCommand {
	public static readonly Option<int> MaxFramesOption = new("--max-frames") {
		Description = "Maximum stack frames per thread.",
		DefaultValueFactory = _ => 100,
	};

	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: ManagedThreadId (default), OSThreadId.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	// Honored set: ThreadAnalyzer.GetDetailedStacks -> ThreadStackInfoFilter.Honored
	// (DATA_CONTRACT.md §2.3). 'text' matches frame method names and requires walking every
	// remaining candidate's stack once set (see GetDetailedStacks' doc comment) -- the other three
	// fields stay free.
	public static readonly Option<string[]> FilterOption = GlobalOptions.CreateFilterOption(
		"Filter expression '<field><op><value>', repeatable and ANDed. Honored fields: " +
		"thread, osthread, exception (true/false), text (across stack frame method names -- setting " +
		"this walks every remaining thread's stack to test it, not just the requested page).");

	public static Command Create() {
		var command = new Command("dumpstack", "Detailed stack traces for each thread.");
		command.Options.Add(MaxFramesOption);
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);
		command.Options.Add(SortOption);
		command.Options.Add(OrderOption);
		command.Options.Add(FilterOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			int maxFrames = parseResult.GetValue(MaxFramesOption);
			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);
			FilterSpec filter = FilterExpressionParser.Parse(parseResult.GetValue(FilterOption));

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ThreadAnalyzer(context, AnalysisCacheProvider.Default);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset, filter);
			var stacks = analyzer.GetDetailedStacks(parameters, maxFrames);

			System.Console.WriteLine(OutputFormatting.Render(format, stacks, MarkdownFormatter.FormatDetailedStacks, JsonFormatter.FormatDetailedStacks, TsvFormatter.FormatDetailedStacks));

			return ExitCodes.Success;
		});

		return command;
	}
}