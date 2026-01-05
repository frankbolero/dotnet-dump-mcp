namespace DotNetDump.Core.Models;

public class ExceptionDetails {
	public ulong Address { get; set; }
	public string TypeName { get; set; } = string.Empty;
	public string? Message { get; set; }
	public int HResult { get; set; }
	public List<string> StackTrace { get; set; } = new();
	public List<ExceptionDetails> InnerExceptions { get; set; } = new();
}

public class ThreadExceptionInfo {
	public int ManagedThreadId { get; set; }
	public uint OSThreadId { get; set; }
	public ExceptionDetails? Exception { get; set; }
}
