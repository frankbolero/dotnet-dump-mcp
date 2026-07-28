using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump verifyobj &lt;address&gt;</c> -- CLI_DESIGN.md §4.2. Verify a single object's integrity.
/// </summary>
public static class VerifyObjCommand {
	public static Command Create() {
		var addressArgument = new Argument<string>("address") {
			Description = "Object address (hex, with or without 0x prefix).",
		};

		var command = new Command("verifyobj", "Verify integrity of a single heap object.");
		command.Arguments.Add(addressArgument);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			string addressText = parseResult.GetValue(addressArgument)!;
			ulong address = AddressParser.Parse(addressText, "address");

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context);
			var corruptions = analyzer.VerifyObject(address);

			System.Console.WriteLine(OutputFormatting.Render(format, corruptions, MarkdownFormatter.FormatHeapVerification, JsonFormatter.FormatHeapVerification, TsvFormatter.FormatHeapVerification));

			return ExitCodes.Success;
		});

		return command;
	}
}
