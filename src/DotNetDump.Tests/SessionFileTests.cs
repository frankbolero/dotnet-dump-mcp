using DotNetDump.Cli;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Tests;

public class SessionFileTests : IDisposable {
	private readonly string _tempRoot;

	public SessionFileTests() {
		_tempRoot = Path.Combine(Path.GetTempPath(), "dndump-session-tests-" + Guid.NewGuid());
		Directory.CreateDirectory(_tempRoot);
	}

	public void Dispose() {
		if (Directory.Exists(_tempRoot)) {
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	[Fact]
	public void Save_WritesUnderDotDndumpDirectory() {
		var session = new SessionFile { DumpPath = "/dumps/a.core", Timestamp = DateTimeOffset.UtcNow };
		session.Save(_tempRoot);

		Assert.True(File.Exists(Path.Combine(_tempRoot, ".dndump", "session.json")));
	}

	[Fact]
	public void FindUpward_ReturnsNull_WhenNoSessionExists() {
		Assert.Null(SessionFile.FindUpward(_tempRoot));
	}

	[Fact]
	public void FindUpward_FindsSessionInAncestorDirectory() {
		var session = new SessionFile { DumpPath = "/dumps/a.core", Timestamp = DateTimeOffset.UtcNow };
		session.Save(_tempRoot);
		string nested = Path.Combine(_tempRoot, "x", "y");
		Directory.CreateDirectory(nested);

		var found = SessionFile.FindUpward(nested);

		Assert.NotNull(found);
		Assert.Equal("/dumps/a.core", found!.DumpPath);
	}

	[Fact]
	public void FindUpward_RoundTripsDacPathAndTimestamp() {
		var timestamp = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
		var session = new SessionFile { DumpPath = "/dumps/a.core", DacPath = "/dacs/dac.dylib", Timestamp = timestamp };
		session.Save(_tempRoot);

		var found = SessionFile.FindUpward(_tempRoot);

		Assert.NotNull(found);
		Assert.Equal("/dacs/dac.dylib", found!.DacPath);
		Assert.Equal(timestamp, found.Timestamp);
	}

	[Fact]
	public void FindUpward_ReturnsNull_ForCorruptSessionFile() {
		string dndumpDir = Path.Combine(_tempRoot, ".dndump");
		Directory.CreateDirectory(dndumpDir);
		File.WriteAllText(Path.Combine(dndumpDir, "session.json"), "{ not json");

		Assert.Null(SessionFile.FindUpward(_tempRoot));
	}

	[Fact]
	public void Save_Overwrites_ExistingSession() {
		new SessionFile { DumpPath = "/dumps/first.core", Timestamp = DateTimeOffset.UtcNow }.Save(_tempRoot);
		new SessionFile { DumpPath = "/dumps/second.core", Timestamp = DateTimeOffset.UtcNow }.Save(_tempRoot);

		var found = SessionFile.FindUpward(_tempRoot);

		Assert.Equal("/dumps/second.core", found!.DumpPath);
	}
}