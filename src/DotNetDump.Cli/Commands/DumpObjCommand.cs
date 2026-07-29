using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpobj &lt;address&gt;</c> -- CLI_DESIGN.md §4.2. Display details of a single object.
/// </summary>
public static class DumpObjCommand {
	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "Object address (hex, with or without 0x prefix).",
		};

		var command = new Command("dumpobj", "Display details of a single heap object.");
		command.Arguments.Add(addressArgument);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			string addressText = parseResult.GetValue(addressArgument)!;
			ulong address = AddressParser.Parse(addressText, "address");

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context, AnalysisCacheProvider.Default);
			var details = analyzer.GetObjectDetails(address);

			System.Console.WriteLine(OutputFormatting.Render(format, details, MarkdownFormatter.FormatObjectDetails, JsonFormatter.FormatObjectDetails, TsvFormatter.FormatObjectDetails));

			return ExitCodes.Success;
		});

		return command;
	}
}