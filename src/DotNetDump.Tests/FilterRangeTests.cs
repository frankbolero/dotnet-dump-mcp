using DotNetDump.Core.Filtering;

namespace DotNetDump.Tests;

public class FilterRangeTests {
	[Fact]
	public void InRange_Ulong_NoBoundsAlwaysMatches() {
		Assert.True(FilterRange.InRange(0UL, null, null));
		Assert.True(FilterRange.InRange(ulong.MaxValue, null, null));
	}

	[Fact]
	public void InRange_Ulong_MinIsInclusive() {
		Assert.True(FilterRange.InRange(100UL, 100UL, null));
		Assert.False(FilterRange.InRange(99UL, 100UL, null));
	}

	[Fact]
	public void InRange_Ulong_MaxIsInclusive() {
		Assert.True(FilterRange.InRange(100UL, null, 100UL));
		Assert.False(FilterRange.InRange(101UL, null, 100UL));
	}

	[Fact]
	public void InRange_Ulong_BothBoundsTogether() {
		Assert.True(FilterRange.InRange(50UL, 10UL, 100UL));
		Assert.False(FilterRange.InRange(5UL, 10UL, 100UL));
		Assert.False(FilterRange.InRange(200UL, 10UL, 100UL));
	}

	[Fact]
	public void InRange_Int_MinIsInclusive() {
		Assert.True(FilterRange.InRange(5, 5, null));
		Assert.False(FilterRange.InRange(4, 5, null));
	}

	[Fact]
	public void InRange_Int_MaxIsInclusive() {
		Assert.True(FilterRange.InRange(5, null, 5));
		Assert.False(FilterRange.InRange(6, null, 5));
	}

	[Fact]
	public void ClampToUlong_PassesThroughNonNegativeValues() {
		Assert.Equal(12345UL, FilterRange.ClampToUlong(12345L));
		Assert.Equal(0UL, FilterRange.ClampToUlong(0L));
	}

	[Fact]
	public void ClampToUlong_ClampsNegativeToZero() {
		// TotalSize is a sum of object sizes and should never be negative in practice; this is a
		// deliberate guard against an unchecked cast wrapping a corrupt value into a huge ulong.
		Assert.Equal(0UL, FilterRange.ClampToUlong(-1L));
		Assert.Equal(0UL, FilterRange.ClampToUlong(long.MinValue));
	}
}