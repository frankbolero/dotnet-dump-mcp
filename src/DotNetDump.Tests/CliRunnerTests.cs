using DotNetDump.Cli;

namespace DotNetDump.Tests;

/// <summary>
/// End-to-end exit-code assertions (CLI_DESIGN.md &#0167;3.4, &#0167;7) driven through
/// <see cref="CliRunner.RunAsync"/> exactly as <c>Program.Main</c> does, with output captured
/// instead of going to the real console. None of these require a dump file: the scenarios that do
/// need a dump path use one that deliberately does not exist, so resolution succeeds and loading
/// fails -- exit code 3, not 0.
/// </summary>
public class CliRunnerTests {
	[Fact]
	public async Task UnknownCommand_ReturnsUsageErrorExitCode() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { "bogus" }, stdout, stderr);

		Assert.Equal(2, exitCode);
		Assert.Contains("bogus", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task MissingRequiredArgument_ReturnsUsageErrorExitCode() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { "use" }, stdout, stderr);

		Assert.Equal(2, exitCode);
	}

	[Fact]
	public async Task Commands_ReturnsSuccessExitCode_AndListsDumpHeap() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { "commands" }, stdout, stderr);

		Assert.Equal(0, exitCode);
		Assert.Contains("dumpheap", stdout.ToString());
	}

	[Fact]
	public async Task Commands_Json_ProducesDataEnvelope() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { "commands", "--format", "json" }, stdout, stderr);

		Assert.Equal(0, exitCode);
		Assert.Contains("\"data\"", stdout.ToString());
	}

	[Fact]
	public async Task DumpHeap_NonExistentDumpFile_ReturnsDumpLoadFailureExitCode() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { "dumpheap", "--dump", "/no/such/dump.core" }, stdout, stderr);

		Assert.Equal(3, exitCode);
		Assert.Contains("/no/such/dump.core", stderr.ToString());
	}

	[Fact]
	public async Task Info_NonExistentDumpFile_ReturnsDumpLoadFailureExitCode() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { "info", "--dump", "/no/such/dump.core" }, stdout, stderr);

		Assert.Equal(3, exitCode);
	}

	[Fact]
	public async Task Use_NonExistentDumpFile_ReturnsDumpLoadFailureExitCode() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		// `use` validates eagerly (CLI_DESIGN.md §12 open question #2): a load failure must stop
		// before the session file is ever written, so this exercises the same DumpLoadException
		// path as `dumpheap`/`info` rather than a separate one.
		int exitCode = await CliRunner.RunAsync(new[] { "use", "/no/such/dump.core" }, stdout, stderr);

		Assert.Equal(3, exitCode);
	}

	[Fact]
	public async Task Format_InvalidValue_ReturnsUsageErrorExitCode() {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { "commands", "--format", "xml" }, stdout, stderr);

		Assert.Equal(2, exitCode);
	}

	// ---- --filter wiring (DATA_CONTRACT.md §2.4, IMPLEMENTATION_PLAN.md task 0.4) ----------------
	//
	// FilterExpressionParser.Parse runs before DumpResolver.ResolveAndLoad in every wired command,
	// so a malformed --filter is rejected without ever touching a dump -- these assertions
	// deliberately pass no --dump, proving the ordering rather than merely the exit code.

	[Theory]
	[InlineData("dumpheap")]
	[InlineData("listobj")]
	public async Task MalformedFilter_OnEveryWiredListCommand_ReturnsUsageErrorExitCode_WithoutADump(string command) {
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { command, "--filter", "not-a-real-expression" }, stdout, stderr);

		Assert.Equal(2, exitCode);
		Assert.Contains("Invalid --filter", stderr.ToString());
	}

	[Theory]
	[InlineData("eeheap")]
	[InlineData("threadpool")]
	[InlineData("verifyheap")]
	[InlineData("info")]
	[InlineData("dumpobj")]
	[InlineData("gcroot")]
	public async Task Filter_OnACommandThatHonorsNone_IsRejectedByTheParserNotAccepted(string command) {
		// These commands must not advertise --filter at all (task 0.4 Part 2): System.CommandLine's
		// own parser rejects the unknown option before the command action ever runs, which is a
		// different (and stronger) guarantee than the command silently accepting and ignoring it.
		var stdout = new StringWriter();
		var stderr = new StringWriter();

		int exitCode = await CliRunner.RunAsync(new[] { command, "--filter", "type~Foo" }, stdout, stderr);

		// The exact wording differs by command (an option-taking command reports the unknown
		// "--filter" token; an argument-taking command like dumpobj/gcroot reports the stray value
		// as an unrecognized argument once --filter itself isn't consumed as an option) -- what
		// matters is that System.CommandLine's own parser rejects it pre-action, at usage-error
		// severity, rather than the command running with the filter silently dropped.
		Assert.Equal(2, exitCode);
		Assert.DoesNotContain("Error:", stderr.ToString());
	}
}