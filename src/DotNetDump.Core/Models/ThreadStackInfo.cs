namespace DotNetDump.Core.Models;

public class ThreadStackInfo {
	public int ManagedThreadId { get; set; }
	public uint OSThreadId { get; set; }
	public bool IsAlive { get; set; }
	public string? ExceptionType { get; set; }
	public List<StackFrameInfo> Frames { get; set; } = new();
}

public class StackFrameInfo {
	public ulong InstructionPointer { get; set; }
	public ulong StackPointer { get; set; }
	public string FrameKind { get; set; } = string.Empty;
	public string? MethodName { get; set; }
	public string? ModuleName { get; set; }
	public bool IsManaged { get; set; }
}