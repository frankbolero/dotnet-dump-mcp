namespace DotNetDump.Core.Models;

public class ThreadPoolInfo {
	public int TotalThreads { get; set; }
	public int ActiveThreads { get; set; }
	public int IdleThreads { get; set; }
	public int RetiredThreads { get; set; }
	public int MinThreads { get; set; }
	public int MaxThreads { get; set; }

	/// <summary>"Portable", "Windows", "Legacy", or "Unavailable".</summary>
	public string? Type { get; set; }

	/// <summary>Percentage as the runtime recorded it, or <c>null</c> when unavailable.</summary>
	public int? CpuUtilization { get; set; }

	/// <summary>Completion-port counters. Only populated when the runtime carries legacy data.</summary>
	public bool HasCompletionPortData { get; set; }
	public int TotalCompletionPorts { get; set; }
	public int FreeCompletionPorts { get; set; }
	public int MaxFreeCompletionPorts { get; set; }
	public int CompletionPortCurrentLimit { get; set; }
	public int MinCompletionPorts { get; set; }
	public int MaxCompletionPorts { get; set; }
}
