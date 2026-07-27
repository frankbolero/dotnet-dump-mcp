using DotNetDump.Core.Caching;

namespace DotNetDump.Tests;

public class CacheKeyTests {
	private static readonly DumpIdentity SampleDump = DumpIdentity.FromComponents("dump-a", "dac-a");
	private static readonly DumpIdentity OtherDump = DumpIdentity.FromComponents("dump-b", "dac-a");

	[Fact]
	public void SameInputs_ProduceEqualKeys() {
		var a = new CacheKey(SampleDump, "heap-statistics", "args-1", 1);
		var b = new CacheKey(SampleDump, "heap-statistics", "args-1", 1);

		Assert.Equal(a, b);
		Assert.Equal(a.GetHashCode(), b.GetHashCode());
	}

	[Fact]
	public void ChangingDumpIdentity_ProducesDifferentKey() {
		var a = new CacheKey(SampleDump, "heap-statistics", "args-1", 1);
		var b = new CacheKey(OtherDump, "heap-statistics", "args-1", 1);

		Assert.NotEqual(a, b);
	}

	[Fact]
	public void ChangingOperation_ProducesDifferentKey() {
		var a = new CacheKey(SampleDump, "heap-statistics", "args-1", 1);
		var b = new CacheKey(SampleDump, "gcroot", "args-1", 1);

		Assert.NotEqual(a, b);
	}

	[Fact]
	public void ChangingArgumentsHash_ProducesDifferentKey() {
		var a = new CacheKey(SampleDump, "heap-statistics", "args-1", 1);
		var b = new CacheKey(SampleDump, "heap-statistics", "args-2", 1);

		Assert.NotEqual(a, b);
	}

	[Fact]
	public void ChangingSchemaVersion_ProducesDifferentKey() {
		var a = new CacheKey(SampleDump, "heap-statistics", "args-1", 1);
		var b = new CacheKey(SampleDump, "heap-statistics", "args-1", 2);

		Assert.NotEqual(a, b);
	}

	[Fact]
	public void HashArguments_IsStableForIdenticalInputs() {
		string hash1 = CacheKey.HashArguments("gcroot", 0x1000UL, 4);
		string hash2 = CacheKey.HashArguments("gcroot", 0x1000UL, 4);

		Assert.Equal(hash1, hash2);
	}

	[Fact]
	public void HashArguments_DiffersWhenAnArgumentChanges() {
		string hash1 = CacheKey.HashArguments("gcroot", 0x1000UL, 4);
		string hash2 = CacheKey.HashArguments("gcroot", 0x2000UL, 4);

		Assert.NotEqual(hash1, hash2);
	}

	[Fact]
	public void HashArguments_DistinguishesNullFromTheStringItWouldOtherwiseCollideWith() {
		// Guards the null placeholder: without one, HashArguments(null) and HashArguments("null")
		// would hash identically because both stringify the same way.
		string hashOfNull = CacheKey.HashArguments((object?)null);
		string hashOfLiteralNullString = CacheKey.HashArguments("null");

		Assert.NotEqual(hashOfNull, hashOfLiteralNullString);
	}
}