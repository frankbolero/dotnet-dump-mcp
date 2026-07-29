using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class ThreadInfoFilterTests {
	private static readonly ThreadInfo WithException = new() {
		ManagedThreadId = 3,
		OSThreadId = 4004,
		IsAlive = true,
		ExceptionType = "System.InvalidOperationException"
	};

	private static readonly ThreadInfo WithoutException = new() {
		ManagedThreadId = 9,
		OSThreadId = 9009,
		IsAlive = true,
		ExceptionType = null
	};

	[Fact]
	public void EmptySpec_MatchesEverything() {
		Assert.True(ThreadInfoFilter.Matches(WithException, FilterSpec.None));
		Assert.True(ThreadInfoFilter.Matches(WithoutException, FilterSpec.None));
	}

	[Fact]
	public void ManagedThreadId_ExactMatch() {
		Assert.True(ThreadInfoFilter.Matches(WithException, new FilterSpec { ManagedThreadId = 3 }));
		Assert.False(ThreadInfoFilter.Matches(WithException, new FilterSpec { ManagedThreadId = 4 }));
	}

	[Fact]
	public void OSThreadId_ExactMatch() {
		Assert.True(ThreadInfoFilter.Matches(WithException, new FilterSpec { OSThreadId = 4004 }));
		Assert.False(ThreadInfoFilter.Matches(WithException, new FilterSpec { OSThreadId = 1 }));
	}

	[Fact]
	public void HasException_TrueMatchesOnlyThreadsWithAnException() {
		Assert.True(ThreadInfoFilter.Matches(WithException, new FilterSpec { HasException = true }));
		Assert.False(ThreadInfoFilter.Matches(WithoutException, new FilterSpec { HasException = true }));
	}

	[Fact]
	public void HasException_FalseMatchesOnlyThreadsWithoutAnException() {
		Assert.True(ThreadInfoFilter.Matches(WithoutException, new FilterSpec { HasException = false }));
		Assert.False(ThreadInfoFilter.Matches(WithException, new FilterSpec { HasException = false }));
	}

	[Fact]
	public void Text_MatchesExceptionTypeOnly() {
		Assert.True(ThreadInfoFilter.Matches(WithException, new FilterSpec { Text = "InvalidOperation" }));
		Assert.False(ThreadInfoFilter.Matches(WithException, new FilterSpec { Text = "NullReference" }));
		Assert.False(ThreadInfoFilter.Matches(WithoutException, new FilterSpec { Text = "anything" }));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(
			FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.HasException | FilterField.Text,
			ThreadInfoFilter.Honored);
	}
}