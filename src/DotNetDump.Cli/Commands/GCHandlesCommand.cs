using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump gchandles</c> -- CLI_DESIGN.md §4.2. List GC handles.
/// Sort fields: <c>Address</c> (default), <c>Kind</c>, <c>TypeName</c>.
/// </summary>
public static class GCHandlesCommand {
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: Address (default), Kind, TypeName.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	// Honored set: HeapAnalyzer.GetGCHandles -> GCHandleInfoFilter.Honored (DATA_CONTRACT.md §2.3).
	// 'size' is deliberately not honored: a handle's size is its target object's size, and a caller
	// filtering by size almost certainly means listobj.
	public static readonly Option<string[]> FilterOption = GlobalOptions.CreateFilterOption(
		"Filter expression '<field><op><value>', repeatable and ANDed. Honored fields: " +
		"type (~ or =/regex/), text (across type name and handle kind). Not size -- a handle's " +
		"size is its target object's size; use 'listobj --filter' for that.");

	public static Command Create() {
		var command = new Command("gchandles", "List GC handle information.");
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

			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);
			FilterSpec filter = FilterExpressionParser.Parse(parseResult.GetValue(FilterOption));

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context, AnalysisCacheProvider.Default);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset, filter);
			var handles = analyzer.GetGCHandles(parameters);

			System.Console.WriteLine(OutputFormatting.Render(format, handles, MarkdownFormatter.FormatGCHandles, JsonFormatter.FormatGCHandles, TsvFormatter.FormatGCHandles));

			return ExitCodes.Success;
		});

		return command;
	}
}