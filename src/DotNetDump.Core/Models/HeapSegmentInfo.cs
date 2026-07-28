namespace DotNetDump.Core.Models;

public class HeapSegmentInfo {
	public ulong Start { get; set; }
	public ulong End { get; set; }
	public ulong Size { get; set; }

	/// <summary>
	/// The generation this segment holds, or <c>null</c> for a segment that spans generations
	/// (an ephemeral region) or where the concept does not apply.
	/// </summary>
	public int? Generation { get; set; }

	/// <summary>Segment kind as the runtime reports it: Gen0/1/2, LOH, POH, Frozen, Ephemeral.</summary>
	public string Kind { get; set; } = string.Empty;

	public bool IsLargeObjectHeap { get; set; }
	public bool IsPinnedObjectHeap { get; set; }

	/// <summary>Bytes actually committed — usually the number that matters for memory pressure.</summary>
	public ulong CommittedSize { get; set; }

	/// <summary>Bytes reserved by the GC but not committed.</summary>
	public ulong ReservedSize { get; set; }

	public ulong Gen0Size { get; set; }
	public ulong Gen1Size { get; set; }
	public ulong Gen2Size { get; set; }

	/// <summary>Which GC heap this segment belongs to (always 0 for workstation GC).</summary>
	public int SubHeapIndex { get; set; }
}

/// <summary>Process-wide heap facts that give the segment table its context.</summary>
public class HeapSummaryInfo {
	public bool IsServerGC { get; set; }
	public int SubHeapCount { get; set; }
	public bool CanWalkHeap { get; set; }

	/// <summary>
	/// DATAS mode (.NET 9+), or <c>null</c> when the runtime does not report it — which also means
	/// DATAS is off.
	/// </summary>
	public int? DynamicAdaptationMode { get; set; }

	public List<HeapSegmentInfo> Segments { get; set; } = new();
}