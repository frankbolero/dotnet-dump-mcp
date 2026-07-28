using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump clrstack</c> -- CLI_DESIGN.md §4.3. Show grouped stack traces.
/// </summary>
public static class ClrStackCommand {
	public static readonly Option<int> MaxFramesOption = new("--max-frames") {
		Description = "Maximum stack frames per thread.",
		DefaultValueFactory = _ => 20,
	};

	public static Command Create() {
		var command = new Command("clrstack", "Grouped stack traces from all threads.");
		command.Options.Add(MaxFramesOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			int maxFrames = parseResult.GetValue(MaxFramesOption);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ThreadAnalyzer(context);
			var groups = analyzer.GetStackTraceGroups(maxFrames);

			System.Console.WriteLine(OutputFormatting.Render(format, groups, MarkdownFormatter.FormatStackGroups, JsonFormatter.FormatStackGroups, TsvFormatter.FormatStackGroups));

			return ExitCodes.Success;
		});

		return command;
	}
}
