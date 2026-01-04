using System.Collections.Generic;

namespace DotNetDump.Core.Models; 
public class ThreadInfo {
	public int ManagedThreadId { get; set; }
	public uint OSThreadId { get; set; }
	public bool IsAlive { get; set; }
	public string? ExceptionType { get; set; }
	public string? ExceptionMessage { get; set; }
}

public class StackGroup {
	public List<int> ManagedThreadIds { get; set; } = new();
	public List<string> Frames { get; set; } = new();
	public int ThreadCount => ManagedThreadIds.Count;
}