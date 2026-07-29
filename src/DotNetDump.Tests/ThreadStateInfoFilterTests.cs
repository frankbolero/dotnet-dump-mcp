using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class ThreadStateInfoFilterTests {
	private static readonly ThreadStateInfo Item = new() {
		ManagedThreadId = 3,
		OSThreadId = 4004,
		IsAlive = true,
		ExceptionType = "System.InvalidOperationException",
		StateFlags = new List<string> { "Background", "ThreadPoolThread" }
	};

	[Fact]
	public void EmptySpec_Matches() {
		Assert.True(ThreadStateInfoFilter.Matches(Item, FilterSpec.None));
	}

	[Fact]
	public void ManagedThreadId_ExactMatch() {
		Assert.True(ThreadStateInfoFilter.Matches(Item, new FilterSpec { ManagedThreadId = 3 }));
		Assert.False(ThreadStateInfoFilter.Matches(Item, new FilterSpec { ManagedThreadId = 4 }));
	}

	[Fact]
	public void OSThreadId_ExactMatch() {
		Assert.True(ThreadStateInfoFilter.Matches(Item, new FilterSpec { OSThreadId = 4004 }));
		Assert.False(ThreadStateInfoFilter.Matches(Item, new FilterSpec { OSThreadId = 1 }));
	}

	[Fact]
	public void HasException_MatchesPresence() {
		Assert.True(ThreadStateInfoFilter.Matches(Item, new FilterSpec { HasException = true }));
		Assert.False(ThreadStateInfoFilter.Matches(Item, new FilterSpec { HasException = false }));
	}

	[Fact]
	public void Text_MatchesExceptionType() {
		Assert.True(ThreadStateInfoFilter.Matches(Item, new FilterSpec { Text = "InvalidOperation" }));
	}

	[Fact]
	public void Text_MatchesAnyStateFlag() {
		Assert.True(ThreadStateInfoFilter.Matches(Item, new FilterSpec { Text = "background" }));
		Assert.True(ThreadStateInfoFilter.Matches(Item, new FilterSpec { Text = "ThreadPool" }));
		Assert.False(ThreadStateInfoFilter.Matches(Item, new FilterSpec { Text = "Suspended" }));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(
			FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.HasException | FilterField.Text,
			ThreadStateInfoFilter.Honored);
	}
}