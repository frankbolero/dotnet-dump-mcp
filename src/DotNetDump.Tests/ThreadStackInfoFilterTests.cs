using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class ThreadStackInfoFilterTests {
	private static readonly ThreadStackInfo Item = new() {
		ManagedThreadId = 3,
		OSThreadId = 4004,
		IsAlive = true,
		ExceptionType = "System.InvalidOperationException",
		Frames = new List<StackFrameInfo> {
			new() { MethodName = "MyApp.Worker.DoWork" },
			new() { MethodName = "System.Threading.ThreadHelper.ThreadStart" }
		}
	};

	[Fact]
	public void MatchesThread_EmptySpec_Matches() {
		Assert.True(ThreadStackInfoFilter.MatchesThread(3, 4004, "X", FilterSpec.None));
	}

	[Fact]
	public void MatchesThread_ManagedThreadId_ExactMatch() {
		Assert.True(ThreadStackInfoFilter.MatchesThread(3, 4004, null, new FilterSpec { ManagedThreadId = 3 }));
		Assert.False(ThreadStackInfoFilter.MatchesThread(3, 4004, null, new FilterSpec { ManagedThreadId = 4 }));
	}

	[Fact]
	public void MatchesThread_OSThreadId_ExactMatch() {
		Assert.True(ThreadStackInfoFilter.MatchesThread(3, 4004, null, new FilterSpec { OSThreadId = 4004 }));
		Assert.False(ThreadStackInfoFilter.MatchesThread(3, 4004, null, new FilterSpec { OSThreadId = 1 }));
	}

	[Fact]
	public void MatchesThread_HasException_MatchesPresence() {
		Assert.True(ThreadStackInfoFilter.MatchesThread(3, 4004, "X", new FilterSpec { HasException = true }));
		Assert.False(ThreadStackInfoFilter.MatchesThread(3, 4004, null, new FilterSpec { HasException = true }));
	}

	[Fact]
	public void MatchesFrameText_MatchesAnyFrameMethodName() {
		Assert.True(ThreadStackInfoFilter.MatchesFrameText(Item.Frames, "DoWork"));
		Assert.True(ThreadStackInfoFilter.MatchesFrameText(Item.Frames, "threadstart"));
		Assert.False(ThreadStackInfoFilter.MatchesFrameText(Item.Frames, "NoSuchFrame"));
	}

	[Fact]
	public void Matches_CombinesThreadAndTextChecks() {
		Assert.True(ThreadStackInfoFilter.Matches(Item, new FilterSpec { ManagedThreadId = 3, Text = "DoWork" }));
		Assert.False(ThreadStackInfoFilter.Matches(Item, new FilterSpec { ManagedThreadId = 3, Text = "NoSuchFrame" }));
		Assert.False(ThreadStackInfoFilter.Matches(Item, new FilterSpec { ManagedThreadId = 99, Text = "DoWork" }));
	}

	[Fact]
	public void Honored_IsExactlyTheDeclaredMatrixSet() {
		Assert.Equal(
			FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.HasException | FilterField.Text,
			ThreadStackInfoFilter.Honored);
	}
}