using DotNetDump.Cli;
using DotNetDump.Core.Utilities;
// Prefer the CLI DumpResolver since these tests check for CliUsageException
using DumpResolver = DotNetDump.Cli.DumpResolver;

namespace DotNetDump.Tests;

/// <summary>
/// Exercises the dump-resolution precedence from CLI_DESIGN.md &#0167;3.1 (<c>--dump</c> flag, then
/// <c>DNDUMP_PATH</c>, then <c>.dndump/session.json</c> searched upward) as a pure function --
/// <see cref="DumpResolver.Resolve"/> takes the starting directory and an environment-variable
/// lookup as parameters specifically so these tests need not touch the real process environment
/// or working directory. Tests the CLI facade which wraps the Core implementation and maps
/// exceptions appropriately.
/// </summary>
public class DumpResolverTests : IDisposable {
	private readonly string _tempRoot;

	public DumpResolverTests() {
		_tempRoot = Path.Combine(Path.GetTempPath(), "dndump-resolver-tests-" + Guid.NewGuid());
		Directory.CreateDirectory(_tempRoot);
	}

	public void Dispose() {
		if (Directory.Exists(_tempRoot)) {
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	private static Func<string, string?> NoEnv => _ => null;

	private void WriteSession(string dumpPath, string? dacPath = null) {
		new SessionFile { DumpPath = dumpPath, DacPath = dacPath, Timestamp = DateTimeOffset.UtcNow }.Save(_tempRoot);
	}

	[Fact]
	public void DumpFlag_WinsOverEnvVarAndSessionFile() {
		WriteSession("/session/dump.core");

		var (path, _) = DumpResolver.Resolve("/flag/dump.core", null, _tempRoot, name => name == DumpResolver.DumpPathVariable ? "/env/dump.core" : null);

		Assert.Equal("/flag/dump.core", path);
	}

	[Fact]
	public void EnvVar_WinsOverSessionFile_WhenNoFlag() {
		WriteSession("/session/dump.core");

		var (path, _) = DumpResolver.Resolve(null, null, _tempRoot, name => name == DumpResolver.DumpPathVariable ? "/env/dump.core" : null);

		Assert.Equal("/env/dump.core", path);
	}

	[Fact]
	public void SessionFile_UsedWhenNoFlagOrEnvVar() {
		WriteSession("/session/dump.core");

		var (path, _) = DumpResolver.Resolve(null, null, _tempRoot, NoEnv);

		Assert.Equal("/session/dump.core", path);
	}

	[Fact]
	public void SessionFile_FoundFromNestedDirectory() {
		WriteSession("/session/dump.core");
		string nested = Path.Combine(_tempRoot, "a", "b", "c");
		Directory.CreateDirectory(nested);

		var (path, _) = DumpResolver.Resolve(null, null, nested, NoEnv);

		Assert.Equal("/session/dump.core", path);
	}

	[Fact]
	public void NoSource_ThrowsCliUsageException() {
		Assert.Throws<CliUsageException>(() => DumpResolver.Resolve(null, null, _tempRoot, NoEnv));
	}

	[Fact]
	public void DacFlag_WinsOverSessionDac() {
		WriteSession("/session/dump.core", dacPath: "/session/dac.dylib");

		var (_, dac) = DumpResolver.Resolve(null, "/flag/dac.dylib", _tempRoot, NoEnv);

		Assert.Equal("/flag/dac.dylib", dac);
	}

	[Fact]
	public void SessionDac_UsedWhenNoDacFlag() {
		WriteSession("/session/dump.core", dacPath: "/session/dac.dylib");

		var (_, dac) = DumpResolver.Resolve(null, null, _tempRoot, NoEnv);

		Assert.Equal("/session/dac.dylib", dac);
	}

	[Fact]
	public void DacIsNull_WhenNeitherFlagNorSessionProvidesOne() {
		WriteSession("/session/dump.core");

		var (_, dac) = DumpResolver.Resolve(null, null, _tempRoot, NoEnv);

		Assert.Null(dac);
	}

	[Fact]
	public void EmptyDumpFlag_FallsThroughToEnvVar() {
		// An empty/whitespace --dump should not "win" over a real source -- it means the option
		// was not meaningfully provided.
		var (path, _) = DumpResolver.Resolve("   ", null, _tempRoot, name => name == DumpResolver.DumpPathVariable ? "/env/dump.core" : null);

		Assert.Equal("/env/dump.core", path);
	}
}