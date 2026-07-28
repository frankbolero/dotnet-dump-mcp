using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump ip2md &lt;address&gt;</c> -- CLI_DESIGN.md §4.5. Get MethodDesc from instruction pointer.
/// </summary>
public static class IP2MdCommand {
	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "Instruction pointer address (hex, with or without 0x prefix).",
		};

		var command = new Command("ip2md", "Get MethodDesc details from an instruction pointer address.");
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
			var info = analyzer.GetMethodByIP(address);

			System.Console.WriteLine(OutputFormatting.Render(format, info, MarkdownFormatter.FormatMethodDesc, JsonFormatter.FormatMethodDesc, TsvFormatter.FormatMethodDesc));

			return ExitCodes.Success;
		});

		return command;
	}
}
