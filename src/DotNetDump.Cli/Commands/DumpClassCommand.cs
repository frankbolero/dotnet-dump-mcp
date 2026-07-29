using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpclass &lt;address&gt;</c> -- CLI_DESIGN.md §4.5. Display EEClass details.
/// The address argument is a MethodTable address (ClrMD does not expose EEClass separately).
/// </summary>
public static class DumpClassCommand {
	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "MethodTable address for the class (hex, with or without 0x prefix).",
		};

		var command = new Command("dumpclass", "Display class (EEClass) details from a MethodTable address.");
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
			var info = analyzer.GetClass(address);

			System.Console.WriteLine(OutputFormatting.Render(format, info, MarkdownFormatter.FormatClass, JsonFormatter.FormatClass, TsvFormatter.FormatClass));

			return ExitCodes.Success;
		});

		return command;
	}
}