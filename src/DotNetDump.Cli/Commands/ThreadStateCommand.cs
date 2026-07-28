using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump threadstate</c> -- CLI_DESIGN.md §4.3. List thread states and synchronization info.
/// Sort fields: <c>ManagedThreadId</c> (default), <c>OSThreadId</c>, <c>LockCount</c>.
/// </summary>
public static class ThreadStateCommand {
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: ManagedThreadId (default), OSThreadId, LockCount.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	public static Command Create() {
		var command = new Command("threadstate", "Thread state and synchronization details.");
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
			var analyzer = new ThreadAnalyzer(context);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset);
			var states = analyzer.GetThreadStates(parameters);

			System.Console.WriteLine(OutputFormatting.Render(format, states, MarkdownFormatter.FormatThreadStates, JsonFormatter.FormatThreadStates, TsvFormatter.FormatThreadStates));

			return ExitCodes.Success;
		});

		return command;
	}
}
