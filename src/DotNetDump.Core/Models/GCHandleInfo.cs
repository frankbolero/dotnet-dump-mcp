namespace DotNetDump.Core.Models;

public class GCHandleInfo {
	public ulong Address { get; set; }
	public ulong Object { get; set; }
	public string? Kind { get; set; }
	public string? TypeName { get; set; }

	/// <summary>Whether this handle keeps its target alive.</summary>
	public bool IsStrong { get; set; }

	/// <summary>Ref count, meaningful only for RefCounted handles.</summary>
	public uint ReferenceCount { get; set; }

	/// <summary>For a dependent handle, the secondary object kept alive alongside the target.</summary>
	public ulong DependentTarget { get; set; }

	public string? AppDomainName { get; set; }

	public ulong Size { get; set; }
}

/// <summary>Per-kind rollup, which is what SOS's <c>gchandles</c> shows by default.</summary>
public class GCHandleStatItem {
	public string Kind { get; set; } = string.Empty;
	public int Count { get; set; }
	public int StrongCount { get; set; }
	public ulong TotalSize { get; set; }
}