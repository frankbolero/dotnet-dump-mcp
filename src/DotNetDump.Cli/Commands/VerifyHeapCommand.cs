using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump verifyheap</c> -- CLI_DESIGN.md §4.2. Verify heap integrity.
/// </summary>
public static class VerifyHeapCommand {
	public static Command Create() {
		var command = new Command("verifyheap", "Verify heap integrity and report corruptions.");

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context, AnalysisCacheProvider.Default);
			var corruptions = analyzer.VerifyHeap();

			System.Console.WriteLine(OutputFormatting.Render(format, corruptions, MarkdownFormatter.FormatHeapVerification, JsonFormatter.FormatHeapVerification, TsvFormatter.FormatHeapVerification));

			return ExitCodes.Success;
		});

		return command;
	}
}
