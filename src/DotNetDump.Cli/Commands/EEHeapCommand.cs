using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump eeheap</c> -- CLI_DESIGN.md §4.2. Show heap segments summary.
/// </summary>
public static class EEHeapCommand {
	public static Command Create() {
		var command = new Command("eeheap", "Heap segments and committed memory summary.");

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context);
			var summary = analyzer.GetHeapSegments();

			System.Console.WriteLine(OutputFormatting.Render(format, summary, MarkdownFormatter.FormatHeapSegments, JsonFormatter.FormatHeapSegments, TsvFormatter.FormatHeapSegments));

			return ExitCodes.Success;
		});

		return command;
	}
}
