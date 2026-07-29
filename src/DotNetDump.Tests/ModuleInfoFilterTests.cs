using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class ModuleInfoFilterTests {
	private static readonly ModuleInfo Item = new() {
		Name = "MyApp.Domain.dll",
		ImageBase = 0x1000,
		Size = 65536,
		IsUserCode = true
	};

	[Fact]
	public void EmptySpec_Matches() {
		Assert.True(ModuleInfoFilter.Matches(Item, FilterSpec.None));
	}

	[Fact]
	public void Module_SubstringMatchOnName() {
		Assert.True(ModuleInfoFilter.Matches(Item, new FilterSpec { Module = "domain" }));
		Assert.False(ModuleInfoFilter.Matches(Item, new FilterSpec { Module = "Infra" }));
	}

	[Fact]
	public void Size_IsTheImageSize_InclusiveBoundaries() {
		Assert.True(ModuleInfoFilter.Matches(Item, new FilterSpec { MinSize = 65536 }));
		Assert.False(ModuleInfoFilter.Matches(Item, new FilterSpec { MinSize = 65537 }));
		Assert.True(ModuleInfoFilter.Matches(Item, new FilterSpec { MaxSize = 65536 }));
		Assert.False(ModuleInfoFilter.Matches(Item, new FilterSpec { MaxSize = 65535 }));
	}

	[Fact]
	public void Text_MatchesNameOnly() {
		Assert.True(ModuleInfoFilter.Matches(Item, new FilterSpec { Text = "MyApp.Domain" }));
		Assert.False(ModuleInfoFilter.Matches(Item, new FilterSpec { Text = "nope" }));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(FilterField.Module | FilterField.Size | FilterField.Text, ModuleInfoFilter.Honored);
	}
}