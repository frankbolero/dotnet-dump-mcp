using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump syncblk</c> -- CLI_DESIGN.md §4.3. List synchronization blocks.
/// Sort fields: <c>Address</c> (default), <c>Recursion</c>, <c>Waiting</c>.
/// Negatable flag: <c>--[no-]thin-locks</c> (default on).
/// </summary>
public static class SyncBlkCommand {
	public static readonly Option<bool> NoThinLocksOption = new("--no-thin-locks") {
		Description = "Exclude thin locks (they require a full heap walk).",
	};

	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: Address (default), Recursion, Waiting.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	public static Command Create() {
		var command = new Command("syncblk", "Synchronization block details and lock waits.");
		command.Options.Add(NoThinLocksOption);
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);
		command.Options.Add(SortOption);
		command.Options.Add(OrderOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			bool noThinLocks = parseResult.GetValue(NoThinLocksOption);
			bool includeThinLocks = !noThinLocks;
			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context, AnalysisCacheProvider.Default);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset);
			var blocks = analyzer.GetSyncBlocks(parameters, includeThinLocks);

			System.Console.WriteLine(OutputFormatting.Render(format, blocks, MarkdownFormatter.FormatSyncBlocks, JsonFormatter.FormatSyncBlocks, TsvFormatter.FormatSyncBlocks));

			return ExitCodes.Success;
		});

		return command;
	}
}