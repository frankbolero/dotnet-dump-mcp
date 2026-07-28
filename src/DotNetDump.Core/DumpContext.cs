using DotNetDump.Core.Caching;

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
	private DumpIdentity _identity = DumpIdentity.None;

	public DataTarget? DataTarget => _dataTarget;
	public ClrRuntime? Runtime => _runtime;
	public ClrHeap? Heap => _runtime?.Heap;
	public bool IsLoaded => _runtime != null;

	/// <inheritdoc />
	public DumpIdentity Identity => _identity;

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

		// Tracks whichever DAC path was actually handed to CreateRuntime, if any, so Identity
		// below can fold it in. This matters specifically for the two branches that pass
		// ignoreMismatch: true -- an explicit or fallback DAC can be wrong, and a wrong DAC must
		// not share a cache entry with a correct one once it is replaced. The no-argument
		// CreateRuntime() branch has ClrMD verify the match itself, so it can only ever succeed
		// with a matching DAC and needs no path tracked for that purpose.
		string? resolvedDacPath = dacPath;

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
				resolvedDacPath = fallbackDac;
			} else {
				throw;
			}
		}

		_identity = ComputeIdentity(dumpPath, resolvedDacPath, clrInfo);
	}

	/// <summary>
	/// Builds a <see cref="DumpIdentity"/> from cheap metadata only -- never dump content. The
	/// dump component is the resolved path plus size and last-write time; a true inode number
	/// would need per-platform P/Invoke (the BCL exposes no cross-platform accessor), which is a
	/// portability risk out of proportion to what it would add over path+size+mtime, so it is
	/// deliberately not attempted here (see CLI_DESIGN.md &#0167;6.1, which allows either
	/// size+mtime+inode or a cheap proxy).
	/// </summary>
	/// <remarks>
	/// The DAC component is deliberately derived differently depending on how the runtime was
	/// created:
	/// <list type="bullet">
	/// <item>If a DAC path was explicitly provided or a local fallback DAC was used (both call
	/// <c>CreateRuntime(path, ignoreMismatch: true)</c>), that path can be wrong for this dump.
	/// Its resolved path, size and last-write time are folded in, so replacing it with a
	/// different (or corrected) DAC file changes the identity.</item>
	/// <item>Otherwise (<c>CreateRuntime()</c> with no path), ClrMD resolved and verified the DAC
	/// itself -- a mismatch would have thrown -- so the dump's own runtime build signature
	/// (<see cref="ClrInfo.BuildId"/>, <see cref="ClrInfo.IndexTimeStamp"/>,
	/// <see cref="ClrInfo.IndexFileSize"/>) is a safe, cheap stand-in that still changes if the
	/// dump is later re-analyzed against a different runtime build.</item>
	/// </list>
	/// </remarks>
	private static DumpIdentity ComputeIdentity(string dumpPath, string? resolvedDacPath, ClrInfo clrInfo) {
		string fullDumpPath = Path.GetFullPath(dumpPath);
		var dumpInfo = new FileInfo(fullDumpPath);
		string dumpComponent = $"dump:{fullDumpPath}|{dumpInfo.Length}|{dumpInfo.LastWriteTimeUtc.Ticks}";

		string dacComponent;
		if (!string.IsNullOrEmpty(resolvedDacPath) && File.Exists(resolvedDacPath)) {
			string fullDacPath = Path.GetFullPath(resolvedDacPath);
			var dacInfo = new FileInfo(fullDacPath);
			dacComponent = $"dac:{fullDacPath}|{dacInfo.Length}|{dacInfo.LastWriteTimeUtc.Ticks}";
		} else {
			string buildId = clrInfo.BuildId.IsDefault ? string.Empty : Convert.ToHexString(clrInfo.BuildId.ToArray());
			dacComponent = $"dac-auto:{clrInfo.Flavor}|{clrInfo.Version}|{clrInfo.IndexTimeStamp}|{clrInfo.IndexFileSize}|{buildId}";
		}

		return DumpIdentity.FromComponents(dumpComponent, dacComponent);
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

		_identity = DumpIdentity.None;
	}

	public void Dispose() {
		Unload();
	}
}