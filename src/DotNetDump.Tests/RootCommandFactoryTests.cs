using System.Text.RegularExpressions;

using DotNetDump.Cli;
using DotNetDump.Cli.Commands;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// Parser tests for the command graph (CLI_DESIGN.md &#0167;7): option binding, defaults, validation
/// and global-option recursion into subcommands. None of these require a dump file -- they only
/// exercise System.CommandLine's parse step.
/// </summary>
public class RootCommandFactoryTests {
	[Theory]
	[InlineData("md")]
	[InlineData("json")]
	[InlineData("tsv")]
	public void Format_AcceptsDocumentedValues(string value) {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "commands", "--format", value });

		Assert.Empty(parseResult.Errors);
		Assert.Equal(value, parseResult.GetValue(GlobalOptions.Format));
	}

	[Fact]
	public void Format_RejectsUnknownValue() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "commands", "--format", "xml" });

		Assert.NotEmpty(parseResult.Errors);
	}

	[Fact]
	public void Format_DefaultsToMarkdown() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "commands" });

		Assert.Equal("md", parseResult.GetValue(GlobalOptions.Format));
	}

	[Fact]
	public void Quiet_DefaultsToFalse() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "commands" });

		Assert.False(parseResult.GetValue(GlobalOptions.Quiet));
	}

	[Fact]
	public void Quiet_CanBeSet() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "commands", "--quiet" });

		Assert.True(parseResult.GetValue(GlobalOptions.Quiet));
	}

	[Fact]
	public void GlobalOptions_AreRecursiveIntoSubcommands() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "dumpheap", "--dump", "/tmp/x.core", "--dac", "/tmp/dac.dylib" });

		Assert.Empty(parseResult.Errors);
		Assert.Equal("/tmp/x.core", parseResult.GetValue(GlobalOptions.Dump));
		Assert.Equal("/tmp/dac.dylib", parseResult.GetValue(GlobalOptions.Dac));
	}

	[Fact]
	public void UnknownCommand_ProducesParseError() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "bogus" });

		Assert.NotEmpty(parseResult.Errors);
	}

	[Fact]
	public void MissingRequiredArgument_OnUse_ProducesParseError() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "use" });

		Assert.NotEmpty(parseResult.Errors);
	}

	[Fact]
	public void Use_BindsPositionalPathArgument() {
		var command = RootCommandFactory.Create();
		var parseResult = command.Parse(new[] { "use", "/dumps/prod.core" });

		Assert.Empty(parseResult.Errors);
	}

	[Fact]
	public void DumpHeap_Limit_DefaultsTo50() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "dumpheap" });

		Assert.Equal(50, parseResult.GetValue(DumpHeapCommand.LimitOption));
	}

	[Fact]
	public void DumpHeap_Offset_DefaultsTo0() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "dumpheap" });

		Assert.Equal(0, parseResult.GetValue(DumpHeapCommand.OffsetOption));
	}

	[Fact]
	public void DumpHeap_Top_IsNullByDefault() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "dumpheap" });

		Assert.Null(parseResult.GetValue(DumpHeapCommand.TopOption));
	}

	[Fact]
	public void DumpHeap_BindsLimitOffsetSortOrderTop() {
		var parseResult = RootCommandFactory.Create().Parse(new[] {
			"dumpheap", "--limit", "10", "--offset", "20", "--sort", "Count", "--order", "asc", "--top", "5",
		});

		Assert.Empty(parseResult.Errors);
		Assert.Equal(10, parseResult.GetValue(DumpHeapCommand.LimitOption));
		Assert.Equal(20, parseResult.GetValue(DumpHeapCommand.OffsetOption));
		Assert.Equal("Count", parseResult.GetValue(DumpHeapCommand.SortOption));
		Assert.Equal("asc", parseResult.GetValue(DumpHeapCommand.OrderOption));
		Assert.Equal(5, parseResult.GetValue(DumpHeapCommand.TopOption));
	}

	[Fact]
	public void DumpHeap_Filter_DefaultsToEmpty() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "dumpheap" });

		Assert.Empty(parseResult.GetValue(DumpHeapCommand.FilterOption)!);
	}

	[Fact]
	public void DumpHeap_Filter_IsRepeatable() {
		var parseResult = RootCommandFactory.Create().Parse(new[] {
			"dumpheap", "--filter", "type~Http", "--filter", "size>100mb",
		});

		Assert.Empty(parseResult.Errors);
		Assert.Equal(new[] { "type~Http", "size>100mb" }, parseResult.GetValue(DumpHeapCommand.FilterOption));
	}

	// ---- listobj: --type (scope) and --filter 'type~' (filter) are two options, not one --------
	//
	// DATA_CONTRACT.md §2.4, "listobj --type is not an alias for --filter 'type~'": --type narrows
	// the heap walk and is part of the cache key; --filter runs after the (possibly narrowed) walk
	// and is excluded from it. These assertions parse both together and check each binds to its own
	// option value, independent of the other -- proving the CLI layer never merges or rewrites one
	// into the other, which is the specific regression the plan calls out.

	[Fact]
	public void ListObj_Type_And_Filter_BindToIndependentOptions_WhenOnlyTypeGiven() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "listobj", "--type", "MyApp.Cache" });

		Assert.Empty(parseResult.Errors);
		Assert.Equal("MyApp.Cache", parseResult.GetValue(ListObjCommand.TypeOption));
		Assert.Empty(parseResult.GetValue(ListObjCommand.FilterOption)!);
	}

	[Fact]
	public void ListObj_Type_And_Filter_BindToIndependentOptions_WhenOnlyFilterGiven() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "listobj", "--filter", "type~MyApp.Cache" });

		Assert.Empty(parseResult.Errors);
		Assert.Null(parseResult.GetValue(ListObjCommand.TypeOption));
		Assert.Equal(new[] { "type~MyApp.Cache" }, parseResult.GetValue(ListObjCommand.FilterOption));
	}

	[Fact]
	public void ListObj_Type_And_Filter_ComposeWithoutEitherOverwritingTheOther() {
		// Both given together: the scope (--type) and the filter (--filter 'type~') keep their own
		// distinct values, exactly as HeapAnalyzer.GetObjects(parameters, typeFilter) takes them as
		// two independent parameters. Neither is derived from or collapsed into the other.
		var parseResult = RootCommandFactory.Create().Parse(new[] {
			"listobj", "--type", "MyApp.Cache", "--filter", "type~Entry",
		});

		Assert.Empty(parseResult.Errors);
		Assert.Equal("MyApp.Cache", parseResult.GetValue(ListObjCommand.TypeOption));
		Assert.Equal(new[] { "type~Entry" }, parseResult.GetValue(ListObjCommand.FilterOption));
	}

	[Fact]
	public void ListObj_Filter_ParsesIntoTypeNameNotTypeScope() {
		FilterSpec filter = FilterExpressionParser.Parse(new[] { "type~Entry" });

		// The parsed FilterSpec carries TypeName (the post-walk filter field) -- there is no
		// property on FilterSpec that could be confused with --type's walk-time scope, since --type
		// never goes through FilterExpressionParser at all.
		Assert.Equal("Entry", filter.TypeName);
	}

	// ---- --help must name honored fields, and only honored fields ------------------------------
	//
	// Task 0.4's own instruction: help text must name the fields a command honors, "so dndump
	// clrmodules --help does not advertise gen". \b word-boundary matching, not a bare substring
	// check, so prose like "generation" in an explanatory sentence doesn't false-positive.

	[Fact]
	public void ClrModules_FilterHelp_DoesNotAdvertiseGen() {
		// The task's own example: "dndump clrmodules --help does not advertise gen". Word-boundary
		// match so explanatory prose using "generation" (not the grammar keyword "gen") can't
		// false-positive this check.
		string description = ClrModulesCommand.FilterOption.Description!;

		Assert.DoesNotMatch(new Regex(@"\bgen\b"), description);
		Assert.Contains("module", description);
		Assert.Contains("size", description);
	}

	[Fact]
	public void DumpHeap_FilterHelp_NamesItsHonoredFields() {
		string description = DumpHeapCommand.FilterOption.Description!;

		Assert.Contains("type", description);
		Assert.Contains("size", description);
		Assert.Contains("count", description);
		Assert.Contains("text", description);
		Assert.DoesNotMatch(new Regex(@"\bgen\b"), description);
	}

	[Fact]
	public void ListObj_FilterHelp_NamesGenerationUnlikeDumpHeap() {
		// listobj is the one heap-family command that honors Generation (HeapObjectItemFilter);
		// dumpheap does not (HeapStatItemFilter).
		string description = ListObjCommand.FilterOption.Description!;

		Assert.Matches(new Regex(@"\bgen\b"), description);
	}

	[Fact]
	public void ClrThreads_FilterHelp_DoesNotAdvertiseTypeOrSize() {
		string description = ClrThreadsCommand.FilterOption.Description!;

		Assert.DoesNotMatch(new Regex(@"\btype\b"), description);
		Assert.DoesNotMatch(new Regex(@"\bsize\b"), description);
		Assert.Contains("thread", description);
		Assert.Contains("exception", description);
	}

	[Fact]
	public void HelpOption_IsAvailableOnRoot() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "--help" });

		Assert.Empty(parseResult.Errors);
	}

	[Fact]
	public void VersionOption_IsAvailableOnRoot() {
		var parseResult = RootCommandFactory.Create().Parse(new[] { "--version" });

		Assert.Empty(parseResult.Errors);
	}
}