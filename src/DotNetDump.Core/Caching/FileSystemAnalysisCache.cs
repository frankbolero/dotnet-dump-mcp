using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DotNetDump.Core.Caching;

/// <summary>
/// The real, shared cache: one directory per <see cref="DumpIdentity"/> under a configurable
/// root, entries written as JSON via <see cref="JsonCacheSerializer"/> by default
/// (CLI_DESIGN.md &#0167;6.4-6.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity.</b> Entries are written to a per-write temp file and moved into place with
/// <see cref="File.Move(string, string, bool)"/>, so a reader either sees the old entry, no
/// entry, or the complete new entry -- never a partial one.
/// </para>
/// <para>
/// <b>Cross-process duplicate work.</b> Two processes racing to compute the same key take an
/// advisory lock: a lock file created with <see cref="FileMode.CreateNew"/>. That mode maps to
/// an atomic, kernel-level exclusive-create on every supported platform (unlike
/// <see cref="FileStream.Lock(long, long)"/>, which throws <see cref="PlatformNotSupportedException"/>
/// on macOS), so it is safe to use as a cross-process mutex without third-party dependencies.
/// A lock older than <see cref="StaleLockAge"/> is assumed to belong to a crashed process and is
/// stolen, so one dead writer cannot wedge the cache forever.
/// </para>
/// </remarks>
public sealed class FileSystemAnalysisCache : IAnalysisCache {
	/// <summary>Overrides the cache root directory. Unset falls back to an XDG-style user cache directory.</summary>
	public const string CacheRootVariable = "DNDUMP_CACHE";

	private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(10);

	private readonly string _root;
	private readonly ICacheSerializer _serializer;

	public FileSystemAnalysisCache(string? root = null, ICacheSerializer? serializer = null) {
		_root = root ?? ResolveDefaultRoot();
		_serializer = serializer ?? new JsonCacheSerializer();
		Directory.CreateDirectory(_root);
	}

	/// <summary>Root this instance reads and writes under. Exposed for diagnostics (e.g. a future <c>dndump cache list</c>).</summary>
	public string Root => _root;

	/// <summary>
	/// <c>DNDUMP_CACHE</c> if set; otherwise an XDG-style per-OS user cache directory
	/// (<c>$XDG_CACHE_HOME</c> if set, else <c>~/Library/Caches</c> on macOS, <c>%LOCALAPPDATA%</c>
	/// on Windows, <c>~/.cache</c> elsewhere), with a <c>dndump</c> leaf directory.
	/// </summary>
	public static string ResolveDefaultRoot() {
		string? overridePath = Environment.GetEnvironmentVariable(CacheRootVariable);
		if (!string.IsNullOrWhiteSpace(overridePath))
			return overridePath;

		string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
		if (!string.IsNullOrWhiteSpace(xdg))
			return Path.Combine(xdg, "dndump");

		if (OperatingSystem.IsWindows())
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dndump", "cache");

		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		return OperatingSystem.IsMacOS()
			? Path.Combine(home, "Library", "Caches", "dndump")
			: Path.Combine(home, ".cache", "dndump");
	}

	public T GetOrCompute<T>(CacheKey key, Func<T> compute) where T : class {
		string dumpDir = DumpDirectory(key.Dump);
		Directory.CreateDirectory(dumpDir);

		string entryPath = EntryPath(dumpDir, key);
		if (TryRead<T>(entryPath, out T? cached))
			return cached!;

		string lockPath = entryPath + ".lock";
		using (AcquireLock(lockPath)) {
			// Another process may have finished the walk while we were waiting for the lock.
			if (TryRead<T>(entryPath, out cached))
				return cached!;

			T value = compute();
			WriteAtomic(entryPath, value);
			return value;
		}
	}

	public void Invalidate(CacheKey key) {
		string dumpDir = DumpDirectory(key.Dump);
		TryDelete(EntryPath(dumpDir, key));
	}

	public void ClearDump(DumpIdentity dump) {
		string dumpDir = DumpDirectory(dump);
		if (!Directory.Exists(dumpDir))
			return;

		try {
			Directory.Delete(dumpDir, recursive: true);
		} catch (IOException) {
			// Best effort: a concurrent reader may be mid-open. Leaving stale files behind here
			// is harmless -- they are simply orphaned until the next ClearDump/Prune.
		} catch (UnauthorizedAccessException) { }
	}

	/// <summary>
	/// Deletes the least-recently-written entries across every dump until the cache is at or
	/// under <paramref name="maxTotalBytes"/>. Tier-1 entries are a few MB each, so this is
	/// intentionally a coarse safety net rather than a tuned LRU policy -- see CLI_DESIGN.md
	/// &#0167;6.5 and the implementation plan's note that elaborate eviction is speculative at
	/// this size. Not called automatically; a future <c>dndump cache prune</c> would call it.
	/// </summary>
	public void Prune(long maxTotalBytes) {
		if (!Directory.Exists(_root))
			return;

		var files = new DirectoryInfo(_root)
			.GetFiles("*.json", SearchOption.AllDirectories)
			.OrderBy(f => f.LastWriteTimeUtc)
			.ToList();

		long total = files.Sum(f => f.Length);

		foreach (FileInfo file in files) {
			if (total <= maxTotalBytes)
				break;

			try {
				total -= file.Length;
				file.Delete();
			} catch (IOException) { } catch (UnauthorizedAccessException) { }
		}
	}

	private string DumpDirectory(DumpIdentity dump) => Path.Combine(_root, Sanitize(dump.Fingerprint));

	private static string EntryPath(string dumpDir, CacheKey key) {
		string name = $"{Sanitize(key.Operation)}-{Sanitize(key.ArgumentsHash)}-v{key.SchemaVersion}.json";
		return Path.Combine(dumpDir, name);
	}

	private static string Sanitize(string value) {
		char[] invalid = Path.GetInvalidFileNameChars();
		if (value.IndexOfAny(invalid) < 0)
			return value;

		var builder = new StringBuilder(value.Length);
		foreach (char c in value)
			builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
		return builder.ToString();
	}

	private bool TryRead<T>(string entryPath, out T? value) where T : class {
		if (!File.Exists(entryPath)) {
			value = null;
			return false;
		}

		try {
			using var stream = new FileStream(entryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			value = _serializer.Read<T>(stream);
			return value != null;
		} catch (IOException) {
			// Lost a race with a concurrent writer's rename; treat as a miss rather than fail --
			// the caller will recompute (or, if it's mid-lock-wait, retry the read after).
			value = null;
			return false;
		}
	}

	private void WriteAtomic<T>(string entryPath, T value) where T : class {
		string tempPath = $"{entryPath}.{Guid.NewGuid():N}.tmp";
		try {
			using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
				_serializer.Write(stream, value);
			}
			File.Move(tempPath, entryPath, overwrite: true);
		} finally {
			TryDelete(tempPath);
		}
	}

	private static void TryDelete(string path) {
		try {
			if (File.Exists(path))
				File.Delete(path);
		} catch (IOException) { } catch (UnauthorizedAccessException) { }
	}

	private static IDisposable AcquireLock(string lockPath) {
		while (true) {
			try {
				var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				return new LockHandle(stream, lockPath);
			} catch (IOException) {
				if (IsStale(lockPath))
					TryDelete(lockPath);
				Thread.Sleep(LockPollInterval);
			}
		}
	}

	private static bool IsStale(string lockPath) {
		try {
			return File.Exists(lockPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) > StaleLockAge;
		} catch (IOException) {
			return false;
		}
	}

	private sealed class LockHandle : IDisposable {
		private readonly FileStream _stream;
		private readonly string _path;
		private bool _disposed;

		public LockHandle(FileStream stream, string path) {
			_stream = stream;
			_path = path;
		}

		public void Dispose() {
			if (_disposed)
				return;
			_disposed = true;
			_stream.Dispose();
			TryDelete(_path);
		}
	}
}