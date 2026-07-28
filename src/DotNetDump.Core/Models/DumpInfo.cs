namespace DotNetDump.Core.Models;

/// <summary>
/// Cheap "what am I looking at" orientation summary for the CLI's <c>info</c> command
/// (CLI_DESIGN.md &#0167;4.1). Deliberately avoids a heap walk -- heap size and segment count come
/// from <c>ClrHeap.Segments</c>, not <c>EnumerateObjects()</c>.
/// </summary>
public class DumpInfo {
	public string? RuntimeVersion { get; set; }
	public string? RuntimeFlavor { get; set; }
	public string? Architecture { get; set; }
	public string? OperatingSystem { get; set; }

	/// <summary>
	/// The DAC file name ClrMD expects for this runtime. Not necessarily the absolute path that was
	/// actually used to create the runtime -- <see cref="IDumpContext"/> does not expose the
	/// resolved path, only whether loading succeeded.
	/// </summary>
	public string? ExpectedDacFileName { get; set; }

	/// <summary>
	/// The explicit <c>--dac</c> path the caller supplied, if any. <c>null</c> means the DAC was
	/// auto-detected by ClrMD.
	/// </summary>
	public string? ExplicitDacPath { get; set; }

	/// <summary>
	/// Whether the DAC is known to match this dump's runtime. Auto-detection only succeeds with a
	/// matching DAC -- ClrMD throws otherwise -- so this is <c>true</c> whenever
	/// <see cref="ExplicitDacPath"/> is <c>null</c> and the dump loaded at all. An explicit DAC path
	/// bypasses that check (<c>ignoreMismatch: true</c>), so its match status is unverified.
	/// </summary>
	public bool DacMatchVerified { get; set; }

	public bool IsServerGC { get; set; }
	public int SubHeapCount { get; set; }
	public ulong HeapSizeBytes { get; set; }
	public int SegmentCount { get; set; }
	public int ManagedThreadCount { get; set; }
}