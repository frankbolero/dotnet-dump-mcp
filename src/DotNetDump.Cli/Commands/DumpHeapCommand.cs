using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpheap</c> -- CLI_DESIGN.md &#0167;4.2. This is the reference command: exactly one
/// analysis command wired end to end (parse -&gt; resolve/load the dump -&gt; call the analyzer -&gt;
/// hand the model to the selected formatter -&gt; exit code), which Phase 6 replicates for the
/// remaining 24. Sort fields: <c>TotalSize</c> (default), <c>Count</c>, <c>TypeName</c>.
/// </summary>
public static class DumpHeapCommand {
	// Exposed as public static fields (rather than locals inside Create()) so tests can bind
	// against the exact same Option instances the command uses -- the same reason GlobalOptions'
	// members are public static.
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: TotalSize (default), Count, TypeName.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	public static readonly Option<int?> TopOption = new("--top") {
		Description = "Alias for --limit.",
	};

	// Honored set: HeapAnalyzer.GetHeapStatistics -> HeapStatItemFilter.Honored
	// (DATA_CONTRACT.md §2.3). 'size' here is the aggregate TotalSize per type, not one object's
	// size -- listobj's --filter 'size' means something different; the description says so.
	public static readonly Option<string[]> FilterOption = GlobalOptions.CreateFilterOption(
		"Filter expression '<field><op><value>', repeatable and ANDed. Honored fields: type (~ or =/regex/), " +
		"size (total size per type, not per-object -- see 'listobj --filter' for per-object size), " +
		"count (instance count), text (across type name).");

	public static Command Create() {
		var command = new Command("dumpheap", "Heap statistics by type: count, total size, MethodTable.");
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);
		command.Options.Add(SortOption);
		command.Options.Add(OrderOption);
		command.Options.Add(TopOption);
		command.Options.Add(FilterOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			int limit = EffectiveLimit.Resolve(parseResult.GetValue(LimitOption), parseResult.GetValue(TopOption));
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);
			FilterSpec filter = FilterExpressionParser.Parse(parseResult.GetValue(FilterOption));

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context, AnalysisCacheProvider.Default);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset, filter);
			var stats = analyzer.GetHeapStatistics(parameters);

			System.Console.WriteLine(OutputFormatting.Render(format, stats, MarkdownFormatter.FormatHeapStatistics, JsonFormatter.FormatHeapStatistics, TsvFormatter.FormatHeapStatistics));

			return ExitCodes.Success;
		});

		return command;
	}
}