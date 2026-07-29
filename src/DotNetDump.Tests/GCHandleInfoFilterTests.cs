using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class GCHandleInfoFilterTests {
	private static readonly GCHandleInfo Item = new() {
		Address = 0x1,
		Object = 0x2,
		Kind = "Strong",
		TypeName = "System.Net.Http.HttpClient",
		IsStrong = true
	};

	private static bool Matches(FilterSpec spec) => GCHandleInfoFilter.Matches(Item, spec, TypeNameMatcher.Create(spec));

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
	public void Text_MatchesTypeNameOrKind() {
		Assert.True(Matches(new FilterSpec { Text = "HttpClient" }));
		Assert.True(Matches(new FilterSpec { Text = "strong" }));
		Assert.False(Matches(new FilterSpec { Text = "Weak" }));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(FilterField.AnyTypeName | FilterField.Text, GCHandleInfoFilter.Honored);
	}
}