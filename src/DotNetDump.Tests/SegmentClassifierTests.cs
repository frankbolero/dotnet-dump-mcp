using DotNetDump.Core.Utilities;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Tests;

public class SegmentClassifierTests {
	[Theory]
	[InlineData(GCSegmentKind.Generation0, 0)]
	[InlineData(GCSegmentKind.Generation1, 1)]
	[InlineData(GCSegmentKind.Generation2, 2)]
	[InlineData(GCSegmentKind.Frozen, 2)]
	[InlineData(GCSegmentKind.Large, 3)]
	[InlineData(GCSegmentKind.Pinned, 4)]
	public void Generation_MapsEveryConcreteKind(GCSegmentKind kind, int expected) {
		Assert.Equal(expected, SegmentClassifier.Generation(kind));
	}

	[Fact]
	public void Generation_IsUnknownForEphemeralRegions() {
		// An ephemeral region carries gen0 and gen1 simultaneously, so no single generation applies.
		Assert.Null(SegmentClassifier.Generation(GCSegmentKind.Ephemeral));
	}

	[Theory]
	[InlineData(GCSegmentKind.Generation0, "Gen0")]
	[InlineData(GCSegmentKind.Generation1, "Gen1")]
	[InlineData(GCSegmentKind.Generation2, "Gen2")]
	[InlineData(GCSegmentKind.Large, "LOH")]
	[InlineData(GCSegmentKind.Pinned, "POH")]
	[InlineData(GCSegmentKind.Frozen, "Frozen")]
	[InlineData(GCSegmentKind.Ephemeral, "Ephemeral")]
	public void Label_NamesEveryKind(GCSegmentKind kind, string expected) {
		Assert.Equal(expected, SegmentClassifier.Label(kind));
	}

	[Fact]
	public void EveryDefinedKind_HasANonNumericLabel() {
		// A regions-based GC reports Frozen and Ephemeral; a mapping that only knows Generation0/1/2
		// used to label those segments "-1", which is what this guards against.
		foreach (GCSegmentKind kind in Enum.GetValues<GCSegmentKind>()) {
			string label = SegmentClassifier.Label(kind);
			Assert.False(string.IsNullOrWhiteSpace(label));
			Assert.DoesNotContain("-1", label);
		}
	}

	[Fact]
	public void LargeAndPinnedHeaps_AreIdentified() {
		Assert.True(SegmentClassifier.IsLargeObjectHeap(GCSegmentKind.Large));
		Assert.False(SegmentClassifier.IsLargeObjectHeap(GCSegmentKind.Pinned));

		Assert.True(SegmentClassifier.IsPinnedObjectHeap(GCSegmentKind.Pinned));
		Assert.False(SegmentClassifier.IsPinnedObjectHeap(GCSegmentKind.Large));
	}
}
