using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump clrmodules</c> -- CLI_DESIGN.md §4.5. List loaded modules/assemblies.
/// Sort fields: <c>Address</c> (default), <c>Size</c>, <c>Name</c>.
/// </summary>
public static class ClrModulesCommand {
	public static readonly Option<bool> IncludeSystemOption = new("--include-system") {
		Description = "Include system and framework modules.",
		DefaultValueFactory = _ => false,
	};

	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: Address (default), Size, Name.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	public static Command Create() {
		var command = new Command("clrmodules", "List loaded .NET modules and assemblies.");
		command.Options.Add(IncludeSystemOption);
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);
		command.Options.Add(SortOption);
		command.Options.Add(OrderOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			bool includeSystem = parseResult.GetValue(IncludeSystemOption);
			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ModuleAnalyzer(context);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset);
			var modules = analyzer.GetModules(parameters, includeSystem);

			System.Console.WriteLine(OutputFormatting.Render(format, modules, MarkdownFormatter.FormatModules, JsonFormatter.FormatModules, TsvFormatter.FormatModules));

			return ExitCodes.Success;
		});

		return command;
	}
}
