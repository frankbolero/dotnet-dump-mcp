namespace DotNetDump.Core.Models;

/// <summary>One node on a retention chain.</summary>
public class GCRootPathNode {
	public ulong Address { get; set; }
	public string? TypeName { get; set; }
	public ulong Size { get; set; }
}

/// <summary>
/// A retention chain from a GC root to the requested object. <see cref="Path"/> is ordered
/// root-object first, target last, so it reads the same direction as SOS's <c>gcroot</c> output.
/// </summary>
public class GCRootPathInfo {
	public ulong RootAddress { get; set; }
	public string? RootKind { get; set; }
	public string? RootName { get; set; }
	public int? ManagedThreadId { get; set; }
	public uint? OSThreadId { get; set; }
	public bool IsPinned { get; set; }
	public bool IsInterior { get; set; }
	public ulong TargetAddress { get; set; }
	public List<GCRootPathNode> Path { get; set; } = new();

	/// <summary>Number of references traversed from the root object to the target.</summary>
	public int Depth => Path.Count > 0 ? Path.Count - 1 : 0;
}