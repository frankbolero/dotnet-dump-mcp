using DotNetDump.Core;
using DotNetDump.Core.Caching;

namespace DotNetDump.Tests;

public class DumpIdentityTests {
	[Fact]
	public void FromComponents_IsStableForIdenticalInputs() {
		var a = DumpIdentity.FromComponents("dump:/dumps/a.core|100|200", "dac:/dac/a.dylib|10|20");
		var b = DumpIdentity.FromComponents("dump:/dumps/a.core|100|200", "dac:/dac/a.dylib|10|20");

		Assert.Equal(a, b);
	}

	[Fact]
	public void FromComponents_DiffersWhenDumpComponentChanges() {
		const string dac = "dac:/dac/a.dylib|10|20";
		var a = DumpIdentity.FromComponents("dump:/dumps/a.core|100|200", dac);
		var b = DumpIdentity.FromComponents("dump:/dumps/b.core|100|200", dac);

		Assert.NotEqual(a, b);
	}

	/// <summary>
	/// The test that matters most (per the Phase 1 brief): a mismatched DAC produces wrong
	/// analysis results, so once the correct DAC replaces it, the identity -- and therefore the
	/// cache key -- must change, or a stale/wrong result would be served forever. This exercises
	/// the same component-hashing mechanism <c>DumpContext.ComputeIdentity</c> uses for its DAC
	/// component (resolved path + size + mtime, or a runtime build signature when ClrMD resolved
	/// the DAC itself) at the pure-function level, since building a real mismatched-DAC scenario
	/// needs an actual dump file.
	/// </summary>
	[Fact]
	public void FromComponents_DiffersWhenOnlyTheDacComponentChanges() {
		const string dumpComponent = "dump:/dumps/prod-oom.core|6300000000|638600000000000000";
		const string wrongDac = "dac:/dac/mismatched.dylib|1000000|638500000000000000";
		const string correctDac = "dac:/dac/matching.dylib|1000100|638500000000000001";

		var withWrongDac = DumpIdentity.FromComponents(dumpComponent, wrongDac);
		var withCorrectDac = DumpIdentity.FromComponents(dumpComponent, correctDac);

		Assert.NotEqual(withWrongDac, withCorrectDac);
	}

	[Fact]
	public void FromComponents_DoesNotCollideAcrossComponentBoundaries() {
		// Guards the choice of separator: without one, ("ab", "c") and ("a", "bc") would hash
		// identically because plain concatenation can't tell them apart.
		var a = DumpIdentity.FromComponents("ab", "c");
		var b = DumpIdentity.FromComponents("a", "bc");

		Assert.NotEqual(a, b);
	}

	[Fact]
	public void None_IsTheSentinelForANotLoadedContext() {
		var context = new DumpContext();

		Assert.False(context.IsLoaded);
		Assert.Equal(DumpIdentity.None, context.Identity);
	}
}