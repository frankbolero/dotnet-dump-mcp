using DotNetDump.Core.Models;
using DotNetDump.Core.Trees;

namespace DotNetDump.Tests;

/// <summary>
/// The thread&#8594;frames tree (Phase 5.2, DATA_CONTRACT.md &#0167;4.4), exercised against hand-built
/// <see cref="ThreadInfo"/>/<see cref="ThreadStackInfo"/> lists -- pure grouping logic, no dump
/// required. Mirrors <see cref="NamespaceRollupBuilderTests"/>'s style.
/// </summary>
public class ThreadFramesTreeBuilderTests {
	private static ThreadInfo Thread(int managedId, uint osId = 0, bool alive = true, string? exceptionType = null) =>
		new() { ManagedThreadId = managedId, OSThreadId = osId, IsAlive = alive, ExceptionType = exceptionType };

	private static StackFrameInfo Frame(string? methodName, string? moduleName = null, string kind = "ManagedMethod") =>
		new() { MethodName = methodName, ModuleName = moduleName, FrameKind = kind, IsManaged = true };

	private static ThreadStackInfo Stack(int managedId, uint osId, bool alive, string? exceptionType, params StackFrameInfo[] frames) =>
		new() { ManagedThreadId = managedId, OSThreadId = osId, IsAlive = alive, ExceptionType = exceptionType, Frames = [.. frames] };

	[Fact]
	public void SingleThreadWithAUniqueStack_BecomesItsOwnRootNode() {
		var threads = new[] { Thread(1) };
		var stacks = new[] { Stack(1, 0xA, true, null, Frame("DoWork", "MyApp")) };

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		var entry = Assert.Single(roots);
		Assert.Equal("Thread 1", entry.Root.Label);
		Assert.Equal(TreeNodeKind.Thread, entry.Root.Kind);
		Assert.True(entry.Root.HasChildren);
		var frame = Assert.Single(entry.Frames);
		Assert.Equal("MyApp!DoWork", frame.Label);
		Assert.Equal(TreeNodeKind.Frame, frame.Kind);
		Assert.False(frame.HasChildren);
	}

	[Fact]
	public void TwoThreadsWithByteIdenticalStacks_CollapseIntoOneGroupNode() {
		var threads = new[] { Thread(1), Thread(2) };
		var stacks = new[] {
			Stack(1, 0xA, true, null, Frame("WaitForWork", "System.Private.CoreLib")),
			Stack(2, 0xB, true, null, Frame("WaitForWork", "System.Private.CoreLib")),
		};

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		var entry = Assert.Single(roots);
		Assert.Contains("2", entry.Root.Label);
		Assert.Contains("identical stack", entry.Root.Label);
		Assert.Single(entry.Root.Badges, b => b.Label.Contains('2'));
		var frame = Assert.Single(entry.Frames);
		Assert.Equal("System.Private.CoreLib!WaitForWork", frame.Label);
	}

	[Fact]
	public void AGroupOfOne_StaysAnOrdinaryThreadNode_RatherThanACollapsedGroup() {
		// Two candidate threads share a stack (they group), a third has a distinct one -- it must
		// stay its own node, not read as "1 threads (identical stack)".
		var threads = new[] { Thread(1), Thread(2), Thread(3) };
		var stacks = new[] {
			Stack(1, 0xA, true, null, Frame("A")),
			Stack(2, 0xB, true, null, Frame("A")),
			Stack(3, 0xC, true, null, Frame("B")),
		};

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		Assert.Equal(2, roots.Count);
		var solo = Assert.Single(roots, r => r.Root.Label == "Thread 3");
		Assert.DoesNotContain("threads", solo.Root.Label);
	}

	[Fact]
	public void AThreadWithAnExceptionInFlight_IsNeverGrouped_EvenWithAMatchingStack() {
		var threads = new[] { Thread(1), Thread(2, exceptionType: "System.NullReferenceException") };
		var stacks = new[] {
			Stack(1, 0xA, true, null, Frame("A")),
			Stack(2, 0xB, true, "System.NullReferenceException", Frame("A")),
		};

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		// Both have the identical single frame "A", but the exception thread must stand alone.
		Assert.Equal(2, roots.Count);
		Assert.Contains(roots, r => r.Root.Label == "Thread 1");
		Assert.Contains(roots, r => r.Root.Label == "Thread 2");
	}

	[Fact]
	public void AThreadWithAnException_CarriesADangerBadgeNamingIt() {
		var threads = new[] { Thread(1, 0xA, exceptionType: "System.NullReferenceException") };
		var stacks = new[] { Stack(1, 0xA, true, "System.NullReferenceException", Frame("A")) };

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		var badge = Assert.Single(roots[0].Root.Badges, b => b.Tone == TreeBadgeTone.Danger);
		Assert.Equal("System.NullReferenceException", badge.Label);
	}

	[Fact]
	public void ADeadThread_RendersAsALeafWithNoChildren() {
		// GetDetailedStacks only ever walks alive threads, so a dead ThreadInfo never has a
		// matching ThreadStackInfo -- the tree must still render it, just with no frames.
		var threads = new[] { Thread(1, alive: false) };
		var stacks = Array.Empty<ThreadStackInfo>();

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		var entry = Assert.Single(roots);
		Assert.False(entry.Root.HasChildren);
		Assert.Empty(entry.Frames);
		Assert.Contains(entry.Root.Badges, b => b.Label == "Dead");
	}

	[Fact]
	public void AnAliveThreadWithNoCapturedFrames_RendersAsALeaf() {
		var threads = new[] { Thread(1) };
		var stacks = new[] { Stack(1, 0xA, true, null) }; // zero frames

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		var entry = Assert.Single(roots);
		Assert.False(entry.Root.HasChildren);
		Assert.Empty(entry.Frames);
	}

	[Fact]
	public void AGroupWhoseSharedStackMentionsThreadPool_IsLabelledAccordingly() {
		var threads = new[] { Thread(1), Thread(2) };
		var stacks = new[] {
			Stack(1, 0xA, true, null, Frame("WaitCallback", "System.Threading.ThreadPoolWorkQueue")),
			Stack(2, 0xB, true, null, Frame("WaitCallback", "System.Threading.ThreadPoolWorkQueue")),
		};

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		Assert.Contains("ThreadPool worker", roots[0].Root.Label);
	}

	[Fact]
	public void AGroupWhoseSharedStackHasNoResolvableThreadPoolHint_GetsTheGenericLabel() {
		// The real fixture-dump landmine: every frame comes back "[(unknown)]" with no module. The
		// grouping still fires (every candidate shares the identical one-frame signature), but there
		// is nothing to match "ThreadPool" against, so the label stays generic rather than guessing.
		var threads = new[] { Thread(1), Thread(2) };
		var stacks = new[] {
			Stack(1, 0xA, true, null, Frame(null)),
			Stack(2, 0xB, true, null, Frame(null)),
		};

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		var entry = Assert.Single(roots);
		Assert.Contains("identical stack", entry.Root.Label);
		Assert.DoesNotContain("ThreadPool", entry.Root.Label);
		Assert.Equal("(unknown)", entry.Frames[0].Label);
	}

	[Fact]
	public void RootsAreOrderedByTheLowestManagedThreadIdTheyRepresent() {
		// Thread 5 is standalone; threads 2 and 3 group (min id 2); thread 10 is standalone. Expect
		// order: 2 (the group), 5, 10.
		var threads = new[] { Thread(10), Thread(5), Thread(2), Thread(3) };
		var stacks = new[] {
			Stack(10, 0xA, true, null, Frame("X")),
			Stack(5, 0xB, true, null, Frame("Y")),
			Stack(2, 0xC, true, null, Frame("Z")),
			Stack(3, 0xD, true, null, Frame("Z")),
		};

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		Assert.Equal(3, roots.Count);
		Assert.Contains("2", roots[0].Root.Label);
		Assert.Equal("Thread 5", roots[1].Root.Label);
		Assert.Equal("Thread 10", roots[2].Root.Label);
	}

	[Fact]
	public void FrameLabelFallsBackToUnknown_AndOmitsTheBangWhenNoModule() {
		var threads = new[] { Thread(1) };
		var stacks = new[] { Stack(1, 0xA, true, null, Frame(null, moduleName: null)) };

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		Assert.Equal("(unknown)", roots[0].Frames[0].Label);
	}

	[Fact]
	public void EveryRoot_CarriesAnOsThreadIdBadgeAndAnAliveOrDeadBadge() {
		var threads = new[] { Thread(1, 0xABCD, alive: true) };
		var stacks = new[] { Stack(1, 0xABCD, true, null, Frame("X")) };

		var roots = ThreadFramesTreeBuilder.BuildRoots(threads, stacks);

		Assert.Contains(roots[0].Root.Badges, b => b.Label == "OS ABCD");
		Assert.Contains(roots[0].Root.Badges, b => b.Label == "Alive" && b.Tone == TreeBadgeTone.Neutral);
	}
}