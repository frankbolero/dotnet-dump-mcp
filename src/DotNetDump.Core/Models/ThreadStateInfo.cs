namespace DotNetDump.Core.Models;

public class ThreadStateInfo {
	public int ManagedThreadId { get; set; }
	public uint OSThreadId { get; set; }
	public bool IsAlive { get; set; }
	public string? ExceptionType { get; set; }
	public ulong Address { get; set; }

	/// <summary>Cooperative or Preemptive, from <c>ClrThread.GCMode</c>.</summary>
	public string GcMode { get; set; } = string.Empty;

	/// <summary>
	/// Monitor lock count, or <c>null</c> when the runtime does not supply one. Null must render as
	/// "unknown" rather than a number — the DAC reports <c>0xFFFFFFFF</c> for "no data", and passing
	/// that off as a count (or casting it to -1) invents information.
	/// </summary>
	public uint? LockCount { get; set; }

	public string ApartmentState { get; set; } = string.Empty;
	public bool IsThreadPoolThread { get; set; }
	public bool IsGC { get; set; }
	public bool IsFinalizer { get; set; }
	public bool IsBackground { get; set; }
	public bool IsUnstarted { get; set; }
	public bool IsDead { get; set; }
	public bool IsAborted { get; set; }
	public bool IsSuspendPending { get; set; }

	/// <summary>The raw runtime flag names, as SOS prints them.</summary>
	public List<string> StateFlags { get; set; } = new();
}
