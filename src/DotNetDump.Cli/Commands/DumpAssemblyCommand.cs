using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump dumpassembly &lt;address&gt;</c> -- CLI_DESIGN.md §4.5. Display assembly details.
/// </summary>
public static class DumpAssemblyCommand {
	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "Assembly address (hex, with or without 0x prefix).",
		};

		var command = new Command("dumpassembly", "Display details of an assembly.");
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
			var details = analyzer.GetAssemblyDetails(address);

			System.Console.WriteLine(OutputFormatting.Render(format, details, MarkdownFormatter.FormatAssemblyDetails, JsonFormatter.FormatAssemblyDetails, TsvFormatter.FormatAssemblyDetails));

			return ExitCodes.Success;
		});

		return command;
	}
}