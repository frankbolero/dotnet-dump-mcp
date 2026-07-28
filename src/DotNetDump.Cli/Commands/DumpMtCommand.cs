using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpmt &lt;address&gt;</c> -- CLI_DESIGN.md §4.5. Display MethodTable details.
/// </summary>
public static class DumpMtCommand {
	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "MethodTable address (hex, with or without 0x prefix).",
		};

		var command = new Command("dumpmt", "Display MethodTable details.");
		command.Arguments.Add(addressArgument);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			string addressText = parseResult.GetValue(addressArgument)!;
			ulong address = AddressParser.Parse(addressText, "address");

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new MetadataAnalyzer(context);
			var info = analyzer.GetMethodTable(address);

			System.Console.WriteLine(OutputFormatting.Render(format, info, MarkdownFormatter.FormatMethodTable, JsonFormatter.FormatMethodTable, TsvFormatter.FormatMethodTable));

			return ExitCodes.Success;
		});

		return command;
	}
}
