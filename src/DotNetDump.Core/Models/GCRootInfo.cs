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

/// <summary>
/// The full result of a <c>gcroot</c> search: the paths found, plus whether the traversal actually
/// finished.
/// <para>
/// <see cref="Paths"/> being empty does <b>not</b> by itself mean the target is unrooted — check
/// <see cref="Truncated"/> first. When it is <c>true</c>, the search gave up because it exhausted its
/// node budget, not because it proved the object unreachable from every root; reporting the object as
/// "eligible for collection" in that state is the exact defect this type exists to prevent (see
/// docs/GCROOT_TRUNCATION.md). A non-empty <see cref="Paths"/> with <see cref="Truncated"/> = true
/// means the paths shown are confirmed, but the search stopped looking for additional node-disjoint
/// paths before it could rule out more existing.
/// </para>
/// </summary>
public class GCRootSearchInfo {
	public ulong TargetAddress { get; set; }
	public List<GCRootPathInfo> Paths { get; set; } = new();

	/// <summary>Total nodes visited across every BFS pass the search ran.</summary>
	public long NodesVisited { get; set; }

	/// <summary>
	/// <c>true</c> if the search stopped because it exhausted its traversal budget rather than
	/// because it conclusively found every path it was looking for (or proved none exist). See the
	/// type-level remarks.
	/// </summary>
	public bool Truncated { get; set; }
}