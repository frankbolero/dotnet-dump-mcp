using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class ExceptionDetailsFilterTests {
	private static readonly ExceptionDetails Item = new() {
		Address = 0x1,
		TypeName = "System.InvalidOperationException",
		Message = "The operation is not valid due to the current state of the object."
	};

	private static bool Matches(FilterSpec spec) => ExceptionDetailsFilter.Matches(Item, spec, TypeNameMatcher.Create(spec));

	[Fact]
	public void EmptySpec_Matches() {
		Assert.True(Matches(FilterSpec.None));
	}

	[Fact]
	public void TypeName_SubstringMatch() {
		Assert.True(Matches(new FilterSpec { TypeName = "InvalidOperation" }));
		Assert.False(Matches(new FilterSpec { TypeName = "NullReference" }));
	}

	[Fact]
	public void TypeNameRegex_Match() {
		Assert.True(Matches(new FilterSpec { TypeNameRegex = "Exception$" }));
		Assert.False(Matches(new FilterSpec { TypeNameRegex = "^NullReference" }));
	}

	[Fact]
	public void Text_MatchesTypeNameOrMessage() {
		Assert.True(Matches(new FilterSpec { Text = "InvalidOperationException" }));
		Assert.True(Matches(new FilterSpec { Text = "current state" }));
		Assert.False(Matches(new FilterSpec { Text = "not found" }));
	}

	[Fact]
	public void Honored_HasNoManagedThreadId_UnlikeTheInFlightPath() {
		// The heap-scan path has no owning thread -- ManagedThreadId is unsupported here, not merely
		// unpopulated (DATA_CONTRACT.md §2.3's "printexception is two methods" correction).
		Assert.Equal(FilterField.AnyTypeName | FilterField.Text, ExceptionDetailsFilter.Honored);
	}
}