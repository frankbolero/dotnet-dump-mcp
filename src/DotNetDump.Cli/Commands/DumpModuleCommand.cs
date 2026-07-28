using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpmodule &lt;address&gt;</c> -- CLI_DESIGN.md §4.5. Display module details.
/// </summary>
public static class DumpModuleCommand {
	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "Module address (hex, with or without 0x prefix).",
		};

		var command = new Command("dumpmodule", "Display details of a loaded module.");
		command.Arguments.Add(addressArgument);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			string addressText = parseResult.GetValue(addressArgument)!;
			ulong address = AddressParser.Parse(addressText, "address");

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ModuleAnalyzer(context);
			var details = analyzer.GetModuleDetails(address);

			System.Console.WriteLine(OutputFormatting.Render(format, details, MarkdownFormatter.FormatModuleDetails, JsonFormatter.FormatModuleDetails, TsvFormatter.FormatModuleDetails));

			return ExitCodes.Success;
		});

		return command;
	}
}
