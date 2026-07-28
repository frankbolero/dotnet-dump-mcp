using DotNetDump.Cli;
using DotNetDump.Cli.Commands;

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