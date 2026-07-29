using System;
using System.Collections.Generic;
using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump printexception</c> (alias <c>pe</c>) -- CLI_DESIGN.md §4.4.
/// If an address is provided, show that single exception. Otherwise, list exceptions from all threads.
/// </summary>
public static class PrintExceptionCommand {
	public static readonly Option<bool> NoHeapExceptionsOption = new("--no-heap-exceptions") {
		Description = "Exclude exceptions found on the heap.",
	};

	public static readonly Option<bool> AllThreadsOption = new("--all-threads") {
		Description = "Include threads without exceptions.",
		DefaultValueFactory = _ => false,
	};

	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field (for collection mode).",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	// Honored set: ThreadAnalyzer.GetThreadExceptions -> ThreadExceptionInfoFilter.Honored
	// (DATA_CONTRACT.md §2.3, "in flight" row -- this command always goes through the combined
	// in-flight-plus-heap path, never GetHeapExceptions directly, so ManagedThreadId/OSThreadId are
	// honored here even though they are not on the heap-scan-only method). Applies only in
	// collection mode; a single-address lookup has nothing to filter.
	public static readonly Option<string[]> FilterOption = GlobalOptions.CreateFilterOption(
		"Filter expression '<field><op><value>', repeatable and ANDed. Collection mode only " +
		"(ignored when an address is given). Honored fields: type (~ or =/regex/, matches the " +
		"exception's type), thread, osthread, text (across exception type and message).");

	public static Command Create() {
		var addressArgument = new Argument<string?>("address") {
			Description = "Optional: exception address (hex, with or without 0x prefix). If omitted, list all exceptions.",
			Arity = ArgumentArity.ZeroOrOne,
		};

		var command = new Command("printexception", "Show exception information.");
		command.Aliases.Add("pe");
		command.Arguments.Add(addressArgument);
		command.Options.Add(NoHeapExceptionsOption);
		command.Options.Add(AllThreadsOption);
		command.Options.Add(LimitOption);
		command.Options.Add(OffsetOption);
		command.Options.Add(SortOption);
		command.Options.Add(OrderOption);
		command.Options.Add(FilterOption);

		command.SetAction((ParseResult parseResult) => {
			string? dumpOption = parseResult.GetValue(GlobalOptions.Dump);
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			string? addressText = parseResult.GetValue(addressArgument);
			bool noHeapExceptions = parseResult.GetValue(NoHeapExceptionsOption);
			bool allThreads = parseResult.GetValue(AllThreadsOption);
			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);
			FilterSpec filter = FilterExpressionParser.Parse(parseResult.GetValue(FilterOption));

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new ThreadAnalyzer(context, AnalysisCacheProvider.Default);

			if (!string.IsNullOrWhiteSpace(addressText)) {
				// Single-address mode: get one exception and wrap in PagedResult for formatter
				ulong address = AddressParser.Parse(addressText, "address");
				var singleResult = analyzer.GetExceptionByAddress(address);
				var wrapped = new PagedResult<ThreadExceptionInfo>(new[] { singleResult }, 1, 1, 0, 1);
				System.Console.WriteLine(OutputFormatting.Render(format, wrapped, MarkdownFormatter.FormatThreadExceptions, JsonFormatter.FormatThreadExceptions, TsvFormatter.FormatThreadExceptions));
			} else {
				// Collection mode: get all exceptions with pagination
				bool includeHeapExceptions = !noHeapExceptions;
				bool onlyWithExceptions = !allThreads;
				int limitValue = limit;
				int offsetValue = offset;

				var parameters = QueryParametersBuilder.Build(sort, order, limitValue, offsetValue, filter);
				var results = analyzer.GetThreadExceptions(parameters, onlyWithExceptions, includeHeapExceptions);
				System.Console.WriteLine(OutputFormatting.Render(format, results, MarkdownFormatter.FormatThreadExceptions, JsonFormatter.FormatThreadExceptions, TsvFormatter.FormatThreadExceptions));
			}

			return ExitCodes.Success;
		});

		return command;
	}
}