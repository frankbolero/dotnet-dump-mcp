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
}