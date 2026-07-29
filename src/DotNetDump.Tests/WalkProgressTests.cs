using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class WalkProgressTests {
	[Fact]
	public void FractionComplete_DividesBytesSeenByTotalBytes() {
		var progress = new WalkProgress(ObjectsWalked: 500, BytesSeen: 2_500, TotalBytes: 10_000);

		Assert.Equal(0.25, progress.FractionComplete);
	}

	[Fact]
	public void FractionComplete_NullWhenTotalBytesIsZero() {
		var progress = new WalkProgress(ObjectsWalked: 0, BytesSeen: 0, TotalBytes: 0);

		Assert.Null(progress.FractionComplete);
	}

	[Fact]
	public void FractionComplete_NullWhenTotalBytesIsNegative() {
		// Not expected in practice, but the guard should not divide by a non-positive denominator.
		var progress = new WalkProgress(ObjectsWalked: 0, BytesSeen: 0, TotalBytes: -1);

		Assert.Null(progress.FractionComplete);
	}

	[Fact]
	public void RecordStructEquality_IsValueBased() {
		var a = new WalkProgress(1, 2, 3);
		var b = new WalkProgress(1, 2, 3);

		Assert.Equal(a, b);
	}
}