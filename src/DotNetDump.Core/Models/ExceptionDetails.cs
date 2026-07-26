namespace DotNetDump.Core.Models;

public class ExceptionDetails {
	public ulong Address { get; set; }
	public string TypeName { get; set; } = string.Empty;
	public string? Message { get; set; }
	public int HResult { get; set; }
	public List<string> StackTrace { get; set; } = new();
	public List<ExceptionDetails> InnerExceptions { get; set; } = new();
}

/// <summary>Where an exception was found.</summary>
public enum ExceptionSource {
	/// <summary>In flight on a thread (<c>ClrThread.CurrentException</c>).</summary>
	ThreadCurrentException,

	/// <summary>An exception object living on the heap — typically already caught and handled.</summary>
	Heap,

	/// <summary>Looked up by explicit address.</summary>
	Address
}

/// <summary>
/// An exception plus its provenance. Thread ids are nullable because most exceptions in a collected
/// dump are not in flight on any thread — they have been caught, and are only reachable on the heap.
/// </summary>
public class ThreadExceptionInfo {
	public int? ManagedThreadId { get; set; }
	public uint? OSThreadId { get; set; }
	public ExceptionSource Source { get; set; } = ExceptionSource.ThreadCurrentException;
	public ExceptionDetails? Exception { get; set; }
}