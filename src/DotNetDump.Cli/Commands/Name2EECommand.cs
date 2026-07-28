using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump name2ee &lt;module&gt; &lt;type[.method]&gt;</c> -- CLI_DESIGN.md §4.5.
/// Look up a type or method by module and name.
/// </summary>
public static class Name2EECommand {
	public static Command Create() {
		var moduleArgument = new Argument<string>("module") {
			Description = "Module name (e.g., mscorlib, MyAssembly).",
		};

		var typeArgument = new Argument<string>("type") {
			Description = "Type name, optionally followed by .method (e.g., System.String or System.String.Concat).",
		};

		var command = new Command("name2ee", "Look up a type or method by name.");
		command.Arguments.Add(moduleArgument);
		command.Arguments.Add(typeArgument);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			string moduleName = parseResult.GetValue(moduleArgument)!;
			string typeName = parseResult.GetValue(typeArgument)!;

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ModuleAnalyzer(context);
			var result = analyzer.Name2EE(moduleName, typeName);

			System.Console.WriteLine(OutputFormatting.Render(format, result, MarkdownFormatter.FormatName2EE, JsonFormatter.FormatName2EE, TsvFormatter.FormatName2EE));

			return ExitCodes.Success;
		});

		return command;
	}
}
