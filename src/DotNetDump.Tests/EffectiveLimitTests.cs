using DotNetDump.Cli;

namespace DotNetDump.Tests;

public class EffectiveLimitTests {
	[Fact]
	public void Top_WinsWhenProvided() {
		Assert.Equal(5, EffectiveLimit.Resolve(50, 5));
	}

	[Fact]
	public void Limit_UsedWhenTopNotProvided() {
		Assert.Equal(50, EffectiveLimit.Resolve(50, null));
	}

	[Fact]
	public void Top_OfZero_StillWins() {
		// A user explicitly asking for --top 0 should not silently fall back to --limit's default.
		Assert.Equal(0, EffectiveLimit.Resolve(50, 0));
	}
}