using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump gcroot &lt;address&gt;</c> -- CLI_DESIGN.md §4.2. Find paths to GC roots.
/// </summary>
public static class GCRootCommand {
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<int> MaxPathsOption = new("--max-paths") {
		Description = "Maximum number of root paths to find.",
		DefaultValueFactory = _ => 4,
	};

	public static readonly Option<int?> MaxNodesOption = new("--max-nodes") {
		Description = "Maximum nodes to visit in the search; 0 = unlimited (warning: can use significant memory).",
	};

	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "Object address to search for roots (hex, with or without 0x prefix).",
		};

		var command = new Command("gcroot", "Find GC root paths to an object.");
		command.Arguments.Add(addressArgument);
		command.Options.Add(MaxPathsOption);
		command.Options.Add(MaxNodesOption);
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			string addressText = parseResult.GetValue(addressArgument)!;
			ulong address = AddressParser.Parse(addressText, "address");
			int maxPaths = parseResult.GetValue(MaxPathsOption);
			int? maxNodes = parseResult.GetValue(MaxNodesOption);
			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context);
			var parameters = QueryParametersBuilder.Build(null, null, limit, offset);
			var result = analyzer.GetGCRoots(address, parameters, maxPaths, maxNodes);

			System.Console.WriteLine(OutputFormatting.Render(format, result, MarkdownFormatter.FormatGCRootPaths, JsonFormatter.FormatGCRootPaths, TsvFormatter.FormatGCRootPaths));

			return ExitCodes.Success;
		});

		return command;
	}
}
