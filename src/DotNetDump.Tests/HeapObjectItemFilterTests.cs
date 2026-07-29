using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class HeapObjectItemFilterTests {
	private static readonly HeapObjectItem Item = new() {
		Address = 0xABCD,
		MethodTable = 0x1000,
		Size = 256,
		TypeName = "System.Net.Http.HttpClient",
		Generation = GenerationFilter.Gen2
	};

	private static bool Matches(FilterSpec spec) => HeapObjectItemFilter.Matches(Item, spec, TypeNameMatcher.Create(spec));

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
		Assert.True(Matches(new FilterSpec { TypeNameRegex = "Http.+$" }));
		Assert.False(Matches(new FilterSpec { TypeNameRegex = "^Socket" }));
	}

	[Fact]
	public void Size_IsThePerInstanceSize_InclusiveBoundaries() {
		Assert.True(Matches(new FilterSpec { MinSize = 256 }));
		Assert.False(Matches(new FilterSpec { MinSize = 257 }));
		Assert.True(Matches(new FilterSpec { MaxSize = 256 }));
		Assert.False(Matches(new FilterSpec { MaxSize = 255 }));
	}

	[Fact]
	public void Generation_ExactMatch() {
		Assert.True(Matches(new FilterSpec { Generation = GenerationFilter.Gen2 }));
		Assert.False(Matches(new FilterSpec { Generation = GenerationFilter.Gen0 }));
	}

	[Fact]
	public void Generation_NullOnItemNeverMatchesASpecificFilter() {
		var unknownGenItem = new HeapObjectItem { TypeName = "X", Size = 1, Generation = null };
		Assert.False(HeapObjectItemFilter.Matches(unknownGenItem, new FilterSpec { Generation = GenerationFilter.Gen0 }, TypeNameMatcher.None));
	}

	[Fact]
	public void Text_MatchesTypeNameOnly() {
		Assert.True(Matches(new FilterSpec { Text = "HttpClient" }));
		Assert.False(Matches(new FilterSpec { Text = "nope" }));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(
			FilterField.AnyTypeName | FilterField.Size | FilterField.Generation | FilterField.Text,
			HeapObjectItemFilter.Honored);
	}
}