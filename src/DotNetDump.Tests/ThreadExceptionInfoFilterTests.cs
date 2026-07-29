using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class ThreadExceptionInfoFilterTests {
	private static readonly ThreadExceptionInfo Item = new() {
		ManagedThreadId = 3,
		OSThreadId = 4004,
		Source = ExceptionSource.ThreadCurrentException,
		Exception = new ExceptionDetails {
			TypeName = "System.InvalidOperationException",
			Message = "boom"
		}
	};

	private static bool Matches(FilterSpec spec) => ThreadExceptionInfoFilter.Matches(Item, spec, TypeNameMatcher.Create(spec));

	[Fact]
	public void EmptySpec_Matches() {
		Assert.True(Matches(FilterSpec.None));
	}

	[Fact]
	public void TypeName_MatchesTheNestedExceptionTypeName() {
		Assert.True(Matches(new FilterSpec { TypeName = "InvalidOperation" }));
		Assert.False(Matches(new FilterSpec { TypeName = "NullReference" }));
	}

	[Fact]
	public void TypeNameRegex_MatchesTheNestedExceptionTypeName() {
		Assert.True(Matches(new FilterSpec { TypeNameRegex = "Exception$" }));
		Assert.False(Matches(new FilterSpec { TypeNameRegex = "^NullReference" }));
	}

	[Fact]
	public void ManagedThreadId_ExactMatch() {
		Assert.True(Matches(new FilterSpec { ManagedThreadId = 3 }));
		Assert.False(Matches(new FilterSpec { ManagedThreadId = 99 }));
	}

	[Fact]
	public void OSThreadId_ExactMatch() {
		Assert.True(Matches(new FilterSpec { OSThreadId = 4004 }));
		Assert.False(Matches(new FilterSpec { OSThreadId = 1 }));
	}

	[Fact]
	public void Text_MatchesTypeNameOrMessage() {
		Assert.True(Matches(new FilterSpec { Text = "InvalidOperationException" }));
		Assert.True(Matches(new FilterSpec { Text = "boom" }));
		Assert.False(Matches(new FilterSpec { Text = "nope" }));
	}

	[Fact]
	public void NullException_FailsTypeAndTextChecksRatherThanThrowing() {
		var noException = new ThreadExceptionInfo { ManagedThreadId = 1 };
		Assert.False(ThreadExceptionInfoFilter.Matches(noException, new FilterSpec { TypeName = "Anything" }, TypeNameMatcher.Create(new FilterSpec { TypeName = "Anything" })));
		Assert.False(ThreadExceptionInfoFilter.Matches(noException, new FilterSpec { Text = "Anything" }, TypeNameMatcher.None));
		// The fields that don't touch Exception still work.
		Assert.True(ThreadExceptionInfoFilter.Matches(noException, new FilterSpec { ManagedThreadId = 1 }, TypeNameMatcher.None));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(
			FilterField.AnyTypeName | FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.Text,
			ThreadExceptionInfoFilter.Honored);
	}
}