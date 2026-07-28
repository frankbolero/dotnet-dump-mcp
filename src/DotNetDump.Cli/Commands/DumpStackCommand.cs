using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpstack</c> -- CLI_DESIGN.md §4.3. Detailed per-thread stacks.
/// Sort fields: <c>ManagedThreadId</c> (default), <c>OSThreadId</c>.
/// </summary>
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

	public static Command Create() {
		var command = new Command("dumpstack", "Detailed stack traces for each thread.");
		command.Options.Add(MaxFramesOption);
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);
		command.Options.Add(SortOption);
		command.Options.Add(OrderOption);

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

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ThreadAnalyzer(context);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset);
			var stacks = analyzer.GetDetailedStacks(parameters, maxFrames);

			System.Console.WriteLine(OutputFormatting.Render(format, stacks, MarkdownFormatter.FormatDetailedStacks, JsonFormatter.FormatDetailedStacks, TsvFormatter.FormatDetailedStacks));

			return ExitCodes.Success;
		});

		return command;
	}
}
