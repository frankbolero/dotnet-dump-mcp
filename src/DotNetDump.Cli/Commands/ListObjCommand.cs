using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump listobj</c> -- CLI_DESIGN.md §4.2. List objects with optional type filter.
/// Sort fields: <c>Address</c> (default), <c>Size</c>.
/// </summary>
public static class ListObjCommand {
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> TypeOption = new("--type") {
		Description = "Filter by type name substring.",
	};

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: Address (default), Size.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	public static Command Create() {
		var command = new Command("listobj", "List objects on the heap, optionally filtering by type.");
		command.Options.Add(TypeOption);
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
			string? typeFilter = parseResult.GetValue(TypeOption);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset);
			var objects = analyzer.GetObjects(parameters, typeFilter);

			System.Console.WriteLine(OutputFormatting.Render(format, objects, MarkdownFormatter.FormatHeapObjects, JsonFormatter.FormatHeapObjects, TsvFormatter.FormatHeapObjects));

			return ExitCodes.Success;
		});

		return command;
	}
}
