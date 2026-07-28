using System;
using System.IO;
using System.Text.Json;

namespace DotNetDump.Cli;

/// <summary>
/// Persisted contents of <c>.dndump/session.json</c> (CLI_DESIGN.md &#0167;3.1), written by <c>use</c>
/// and consulted last in the dump-resolution precedence. Holds no analysis state -- only what is
/// needed to re-open the same dump on a later, unrelated invocation -- and is safe to delete at
/// any time.
/// </summary>
public sealed class SessionFile {
	public const string DirectoryName = ".dndump";
	public const string FileName = "session.json";

	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	public string DumpPath { get; set; } = string.Empty;

	/// <summary>
	/// The <c>--dac</c> value passed to <c>use</c>, if any. <c>null</c> means the DAC was
	/// auto-detected.
	/// </summary>
	public string? DacPath { get; set; }

	public DateTimeOffset Timestamp { get; set; }

	/// <summary>Full path to the session file under <paramref name="directory"/>.</summary>
	public static string GetPath(string directory) => Path.Combine(directory, DirectoryName, FileName);

	/// <summary>Writes this session to <c>&lt;directory&gt;/.dndump/session.json</c>, creating the
	/// directory if needed. Overwrites any existing session.</summary>
	public void Save(string directory) {
		string dndumpDir = Path.Combine(directory, DirectoryName);
		Directory.CreateDirectory(dndumpDir);
		string path = Path.Combine(dndumpDir, FileName);
		string json = JsonSerializer.Serialize(this, SerializerOptions);
		File.WriteAllText(path, json);
	}

	/// <summary>
	/// Searches <paramref name="startDirectory"/> and each ancestor directory in turn for
	/// <c>.dndump/session.json</c>, returning the first one found. A corrupt session file is
	/// treated the same as a missing one -- <c>null</c> -- rather than throwing, so a damaged
	/// `.dndump` directory degrades to "no session" instead of breaking every command.
	/// </summary>
	public static SessionFile? FindUpward(string startDirectory) {
		var directory = new DirectoryInfo(startDirectory);

		while (directory != null) {
			string candidate = GetPath(directory.FullName);
			if (File.Exists(candidate)) {
				try {
					string json = File.ReadAllText(candidate);
					return JsonSerializer.Deserialize<SessionFile>(json, SerializerOptions);
				} catch (Exception) {
					return null;
				}
			}

			directory = directory.Parent;
		}

		return null;
	}
}