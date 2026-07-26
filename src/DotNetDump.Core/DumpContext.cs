using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core;

public class DumpContext : IDumpContext {
	/// <summary>
	/// Semicolon-separated symbol server URLs. When unset, dump loading stays offline.
	/// </summary>
	public const string SymbolPathsVariable = "DOTNETDUMP_SYMBOL_PATHS";

	/// <summary>
	/// Directory used to cache downloaded symbols. Only consulted when
	/// <see cref="SymbolPathsVariable"/> is set.
	/// </summary>
	public const string SymbolCacheVariable = "DOTNETDUMP_SYMBOL_CACHE";

	private const string DefaultSymbolCachePath = "/dumps/.symcache";

	private DataTarget? _dataTarget;
	private ClrRuntime? _runtime;

	public DataTarget? DataTarget => _dataTarget;
	public ClrRuntime? Runtime => _runtime;
	public ClrHeap? Heap => _runtime?.Heap;
	public bool IsLoaded => _runtime != null;

	public void Initialize(string dumpPath, string? dacPath = null) =>
		Load(dumpPath, dacPath); // Backwards compat if needed, but we'll prefer Load

	public void Load(string dumpPath, string? dacPath = null) {
		if (IsLoaded) {
			Unload();
		}

		if (!File.Exists(dumpPath))
			throw new FileNotFoundException("Dump file not found.", dumpPath);

		// Attempt to fetch DAC if not provided and not found locally?
		// For now, we assume the environment (container) has what it needs or the user provides dacPath.
		// In the container model, 'dotnet-symbol' might need to be run *before* this method is called
		// if we want auto-downloading inside the C# app.
		// Ideally, we might want to shell out to dotnet-symbol here if it fails?
		// For now, let's keep the core logic simple: Load what exists.

		_dataTarget = DataTarget.LoadDump(dumpPath, CreateOptions());

		ClrInfo? clrInfo = _dataTarget.ClrVersions.FirstOrDefault();
		if (clrInfo == null)
			throw new InvalidOperationException("No CLR Runtime found in dump.");

		try {
			if (!string.IsNullOrEmpty(dacPath)) {
				_runtime = clrInfo.CreateRuntime(dacPath, ignoreMismatch: true);
			} else {
				_runtime = clrInfo.CreateRuntime();
			}
		} catch (Exception) {
			// Fallback logic for local development if DAC is missing
			string fallbackDac = "/usr/local/share/dotnet/shared/Microsoft.NETCore.App/9.0.11/libmscordaccore.dylib";
			if (File.Exists(fallbackDac)) {
				_runtime = clrInfo.CreateRuntime(fallbackDac, ignoreMismatch: true);
			} else {
				throw;
			}
		}
	}

	/// <summary>
	/// Builds the ClrMD options used to open a dump.
	/// </summary>
	/// <remarks>
	/// ClrMD 4 changed two defaults that matter to us: it no longer reads
	/// <c>_NT_SYMBOL_PATH</c>, and it contacts https://msdl.microsoft.com on demand unless
	/// told otherwise. The container fetches the DAC up front via <c>dotnet-symbol</c>
	/// (see entrypoint.sh), so we stay offline by default rather than adding an unbounded
	/// network round-trip to every dump load. Set DOTNETDUMP_SYMBOL_PATHS to opt back in.
	/// </remarks>
	private static DataTargetOptions CreateOptions() {
		string? symbolPaths = Environment.GetEnvironmentVariable(SymbolPathsVariable);

		if (string.IsNullOrWhiteSpace(symbolPaths))
			return new DataTargetOptions { SymbolPaths = [] };

		string? cachePath = Environment.GetEnvironmentVariable(SymbolCacheVariable);

		return new DataTargetOptions {
			SymbolPaths = symbolPaths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
			// The ClrMD default lands in the system temp directory, which is ephemeral in a
			// container and re-downloads on every restart.
			SymbolCachePath = string.IsNullOrWhiteSpace(cachePath) ? DefaultSymbolCachePath : cachePath
		};
	}

	public void Unload() {
		_runtime?.Dispose();
		_runtime = null;

		_dataTarget?.Dispose();
		_dataTarget = null;
	}

	public void Dispose() {
		Unload();
	}
}