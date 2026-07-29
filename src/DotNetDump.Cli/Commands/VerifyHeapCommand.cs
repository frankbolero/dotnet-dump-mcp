using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump verifyheap</c> -- CLI_DESIGN.md §4.2. Verify heap integrity.
/// </summary>
public static class VerifyHeapCommand {
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static Command Create() {
		var command = new Command("verifyheap", "Verify heap integrity and report corruptions.");
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context, AnalysisCacheProvider.Default);
			var parameters = QueryParametersBuilder.Build(null, null, limit, offset);
			var corruptions = analyzer.VerifyHeap(parameters);

			System.Console.WriteLine(OutputFormatting.Render(format, corruptions, MarkdownFormatter.FormatHeapVerification, JsonFormatter.FormatHeapVerification, TsvFormatter.FormatHeapVerification));

			return ExitCodes.Success;
		});

		return command;
	}
}