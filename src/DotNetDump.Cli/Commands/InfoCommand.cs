using System;
using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump info</c> -- CLI_DESIGN.md &#0167;4.1. The cheap "what am I looking at" entry point:
/// runtime version, architecture, OS, DAC status, heap size, segment and thread counts. Backed by
/// <see cref="SessionAnalyzer"/>, which has no MCP tool equivalent.
/// </summary>
public static class InfoCommand {
	public static Command Create() {
		var command = new Command("info", "Show runtime, DAC and heap summary for the current dump.");

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new SessionAnalyzer(context);
			var info = analyzer.GetInfo(dacOption);

			Console.WriteLine(OutputFormatting.Render(format, info, MarkdownFormatter.FormatInfo, JsonFormatter.FormatInfo, TsvFormatter.FormatInfo));

			return ExitCodes.Success;
		});

		return command;
	}
}