using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump clrthreads</c> -- CLI_DESIGN.md §4.3. List managed threads.
/// Sort fields: <c>ManagedThreadId</c> (default), <c>OSThreadId</c>, <c>Exception</c>.
/// </summary>
public static class ClrThreadsCommand {
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: ManagedThreadId (default), OSThreadId, Exception.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	public static Command Create() {
		var command = new Command("clrthreads", "List managed threads with basic information.");
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);
		command.Options.Add(SortOption);
		command.Options.Add(OrderOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ThreadAnalyzer(context, AnalysisCacheProvider.Default);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset);
			var threads = analyzer.GetThreads(parameters);

			System.Console.WriteLine(OutputFormatting.Render(format, threads, MarkdownFormatter.FormatThreads, JsonFormatter.FormatThreads, TsvFormatter.FormatThreads));

			return ExitCodes.Success;
		});

		return command;
	}
}
