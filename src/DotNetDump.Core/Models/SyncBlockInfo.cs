namespace DotNetDump.Core.Models;

public class SyncBlockInfo {
	public ulong ObjectAddress { get; set; }
	public string? TypeName { get; set; }
	public bool IsMonitorHeld { get; set; }
	public ulong HoldingThreadAddress { get; set; }
	public int RecursionCount { get; set; }
	public int WaitingThreadCount { get; set; }

	/// <summary>Owning managed thread, or <c>null</c> when it could not be mapped.</summary>
	public int? ManagedThreadId { get; set; }

	public uint? OSThreadId { get; set; }

	/// <summary>
	/// True for a thin lock — an uncontended monitor that the runtime stores in the object header
	/// without allocating a sync block. An uncontended <c>lock</c> is invisible to sync-block
	/// enumeration, so <c>syncblk</c> reports nothing unless thin locks are included.
	/// </summary>
	public bool IsThinLock { get; set; }
}
