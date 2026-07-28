using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump threadpool</c> -- CLI_DESIGN.md §4.3. Show thread pool statistics.
/// </summary>
public static class ThreadPoolCommand {
	public static Command Create() {
		var command = new Command("threadpool", "Thread pool queue and worker thread statistics.");

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ThreadAnalyzer(context);
			var info = analyzer.GetThreadPoolInfo();

			System.Console.WriteLine(OutputFormatting.Render(format, info, MarkdownFormatter.FormatThreadPool, JsonFormatter.FormatThreadPool, TsvFormatter.FormatThreadPool));

			return ExitCodes.Success;
		});

		return command;
	}
}
