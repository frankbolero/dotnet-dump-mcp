namespace DotNetDump.Core.Models;

public class HeapSegmentInfo {
	public ulong Start { get; set; }
	public ulong End { get; set; }
	public ulong Size { get; set; }
	public int Generation { get; set; }
	public bool IsLargeObjectHeap { get; set; }
	public bool IsPinnedObjectHeap { get; set; }
}