namespace DotNetDump.Core.Models;

public class ThreadStateInfo {
	public int ManagedThreadId { get; set; }
	public uint OSThreadId { get; set; }
	public bool IsAlive { get; set; }
	public string? ExceptionType { get; set; }
	public ulong Address { get; set; }
	public string GcMode { get; set; } = string.Empty;
	public int LockCount { get; set; }
	public string ApartmentState { get; set; } = string.Empty;
	public bool IsThreadPoolThread { get; set; }
	public bool IsGC { get; set; }
	public bool IsFinalizer { get; set; }
	public bool IsBackground { get; set; }
	public bool IsUnstarted { get; set; }
	public bool IsAborted { get; set; }
}