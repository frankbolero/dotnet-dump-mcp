using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// Maps <see cref="GCSegmentKind"/> to the generation and label <c>eeheap</c> reports.
/// <para>
/// A regions-based GC (.NET 5+, and the default from .NET 7) reports <see cref="GCSegmentKind.Frozen"/>
/// and <see cref="GCSegmentKind.Ephemeral"/> rather than the per-generation kinds, so a mapping that
/// only understands Generation0/1/2 labels every segment on a modern runtime as "unknown".
/// </para>
/// </summary>
public static class SegmentClassifier {
	/// <summary>
	/// The generation a segment belongs to, or <c>null</c> where the segment spans generations
	/// (<see cref="GCSegmentKind.Ephemeral"/>) or the concept does not apply.
	/// </summary>
	public static int? Generation(GCSegmentKind kind) => kind switch {
		GCSegmentKind.Generation0 => 0,
		GCSegmentKind.Generation1 => 1,
		GCSegmentKind.Generation2 => 2,
		// A frozen segment holds non-collectable objects and is walked as gen2.
		GCSegmentKind.Frozen => 2,
		GCSegmentKind.Large => 3,
		GCSegmentKind.Pinned => 4,
		// Ephemeral regions carry gen0 and gen1 (and sometimes gen2) ranges simultaneously.
		GCSegmentKind.Ephemeral => null,
		_ => null
	};

	/// <summary>A short human label, matching the vocabulary SOS uses.</summary>
	public static string Label(GCSegmentKind kind) => kind switch {
		GCSegmentKind.Generation0 => "Gen0",
		GCSegmentKind.Generation1 => "Gen1",
		GCSegmentKind.Generation2 => "Gen2",
		GCSegmentKind.Large => "LOH",
		GCSegmentKind.Pinned => "POH",
		GCSegmentKind.Frozen => "Frozen",
		GCSegmentKind.Ephemeral => "Ephemeral",
		_ => kind.ToString()
	};

	public static bool IsLargeObjectHeap(GCSegmentKind kind) => kind == GCSegmentKind.Large;

	public static bool IsPinnedObjectHeap(GCSegmentKind kind) => kind == GCSegmentKind.Pinned;
}