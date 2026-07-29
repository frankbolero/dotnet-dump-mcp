using System.CommandLine;

using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump listobj</c> -- CLI_DESIGN.md §4.2. List objects with optional type filter.
/// Sort fields: <c>Address</c> (default), <c>Size</c>.
/// </summary>
/// <remarks>
/// <b>--type and --filter 'type~'/'type=/regex/' are not the same thing and must never be
/// aliased</b> (DATA_CONTRACT.md §2.4, "listobj --type is not an alias for --filter 'type~'").
/// <c>--type</c> is a walk-time <i>scope</i>: it narrows <see cref="HeapAnalyzer.GetObjects"/>'s
/// heap walk itself and is part of the cache key, so a distinct <c>--type</c> value costs another
/// full walk. <c>--filter</c>'s <c>type</c> field is a post-walk <i>filter</i>: it runs over
/// whatever the (possibly type-scoped) cached walk already produced and is excluded from the cache
/// key, so changing it is free. They compose -- scope at walk time, filter after, ANDed -- exactly
/// as <see cref="HeapAnalyzer.GetObjects"/> already takes both parameters independently. Aliasing
/// <c>--type</c> to <c>--filter 'type~'</c> in either direction would make a scoped
/// <c>listobj --type Foo</c> either walk and cache the entire heap to return a few hundred rows
/// (aliasing scope onto filter), or silently stop caching per-type entries (the reverse) --
/// a large, quiet performance regression either way. See <see cref="TypeOption"/> vs
/// <see cref="FilterOption"/> below: two options, two purposes, deliberately never merged.
/// </remarks>
public static class ListObjCommand {
	public static readonly Option<int> LimitOption = GlobalOptions.CreateLimitOption();
	public static readonly Option<int> OffsetOption = GlobalOptions.CreateOffsetOption();

	/// <summary>The walk-time scope. Narrows what <see cref="HeapAnalyzer.GetObjects"/> walks and
	/// caches; a new value here costs a fresh walk. Not part of the <c>--filter</c> grammar and not
	/// interchangeable with it -- see the type-level remarks.</summary>
	public static readonly Option<string?> TypeOption = new("--type") {
		Description = "Scope the heap walk to types whose name contains this substring. Unlike " +
			"--filter 'type~...', this changes what is walked and cached -- a new value costs a " +
			"fresh walk, not a free re-filter of the cached result. Use --filter for interactive " +
			"narrowing of an already-cached listobj; use --type when you know the type up front " +
			"and want to avoid caching the rest of the heap.",
	};

	public static readonly Option<string?> SortOption = new("--sort") {
		Description = "Sort field: Address (default), Size.",
	};

	public static readonly Option<string?> OrderOption = new("--order") {
		Description = "Sort direction: asc or desc (default desc).",
	};

	// Honored set: HeapAnalyzer.GetObjects -> HeapObjectItemFilter.Honored (DATA_CONTRACT.md §2.3).
	// 'size' here is one object's own size, not the aggregate dumpheap uses. Post-walk and free on
	// a warm cache regardless of the --type scope in effect -- see the type-level remarks.
	public static readonly Option<string[]> FilterOption = GlobalOptions.CreateFilterOption(
		"Filter expression '<field><op><value>', repeatable and ANDed; applied after the (possibly " +
		"--type-scoped) cached walk, free on a warm cache. Honored fields: type (~ or =/regex/ -- " +
		"see --type for the walk-time scope instead), size (one object's own size, not the " +
		"per-type total dumpheap's --filter 'size' means), gen (0, 1, 2, loh, poh, frozen), " +
		"text (across type name).");

	public static Command Create() {
		var command = new Command("listobj", "List objects on the heap, optionally filtering by type.");
		command.Options.Add(TypeOption);
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

			int limit = parseResult.GetValue(LimitOption);
			int offset = parseResult.GetValue(OffsetOption);
			string? sort = parseResult.GetValue(SortOption);
			string? order = parseResult.GetValue(OrderOption);
			string? typeScope = parseResult.GetValue(TypeOption);
			FilterSpec filter = FilterExpressionParser.Parse(parseResult.GetValue(FilterOption));

			using var context = DumpResolver.ResolveAndLoad(dumpOption, dacOption, quiet);
			var analyzer = new HeapAnalyzer(context, AnalysisCacheProvider.Default);
			var parameters = QueryParametersBuilder.Build(sort, order, limit, offset, filter);
			// typeScope narrows the walk (cache key); filter.Filter narrows the already-walked
			// result (no cache key impact). Passed as two independent arguments on purpose --
			// see the type-level remarks on why these must never be merged into one.
			var objects = analyzer.GetObjects(parameters, typeScope);

			System.Console.WriteLine(OutputFormatting.Render(format, objects, MarkdownFormatter.FormatHeapObjects, JsonFormatter.FormatHeapObjects, TsvFormatter.FormatHeapObjects));

			return ExitCodes.Success;
		});

		return command;
	}
}