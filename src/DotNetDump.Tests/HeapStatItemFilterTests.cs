using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class HeapStatItemFilterTests {
	private static readonly HeapStatItem Item = new() {
		TypeName = "System.Net.Http.HttpClient",
		MethodTable = 0x1000,
		Count = 42,
		TotalSize = 8192
	};

	private static bool Matches(FilterSpec spec) => HeapStatItemFilter.Matches(Item, spec, TypeNameMatcher.Create(spec));

	[Fact]
	public void EmptySpec_Matches() {
		Assert.True(Matches(FilterSpec.None));
	}

	[Fact]
	public void TypeName_SubstringMatch() {
		Assert.True(Matches(new FilterSpec { TypeName = "http" }));
		Assert.False(Matches(new FilterSpec { TypeName = "Socket" }));
	}

	[Fact]
	public void TypeNameRegex_Match() {
		Assert.True(Matches(new FilterSpec { TypeNameRegex = "^System\\.Net" }));
		Assert.False(Matches(new FilterSpec { TypeNameRegex = "^Other" }));
	}

	[Fact]
	public void Size_IsTheAggregateTotalSize_InclusiveBoundaries() {
		Assert.True(Matches(new FilterSpec { MinSize = 8192 }));
		Assert.False(Matches(new FilterSpec { MinSize = 8193 }));
		Assert.True(Matches(new FilterSpec { MaxSize = 8192 }));
		Assert.False(Matches(new FilterSpec { MaxSize = 8191 }));
	}

	[Fact]
	public void Count_InclusiveBoundaries() {
		Assert.True(Matches(new FilterSpec { MinCount = 42 }));
		Assert.False(Matches(new FilterSpec { MinCount = 43 }));
		Assert.True(Matches(new FilterSpec { MaxCount = 42 }));
		Assert.False(Matches(new FilterSpec { MaxCount = 41 }));
	}

	[Fact]
	public void Text_MatchesTypeNameOnly() {
		Assert.True(Matches(new FilterSpec { Text = "HttpClient" }));
		Assert.False(Matches(new FilterSpec { Text = "nope" }));
	}

	[Fact]
	public void AllFieldsMustMatch_AndedTogether() {
		var spec = new FilterSpec { TypeName = "Http", MinCount = 10, MaxSize = 100_000 };
		Assert.True(Matches(spec));

		var failing = new FilterSpec { TypeName = "Http", MinCount = 100 };
		Assert.False(Matches(failing));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(
			FilterField.AnyTypeName | FilterField.Size | FilterField.Count | FilterField.Text,
			HeapStatItemFilter.Honored);
	}
}