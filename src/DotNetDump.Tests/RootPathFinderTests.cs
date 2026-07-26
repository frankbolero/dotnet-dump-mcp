using DotNetDump.Core.Utilities;

namespace DotNetDump.Tests;

/// <summary>
/// The gcroot search, exercised against hand-built object graphs. No dump required, which lets these
/// cover shapes a healthy fixture dump would never contain (cycles, unrooted objects, unreadable
/// objects).
/// </summary>
public class RootPathFinderTests {
	private static Func<ulong, IEnumerable<ulong>> Graph(params (ulong From, ulong[] To)[] edges) {
		var map = edges.ToDictionary(e => e.From, e => (IEnumerable<ulong>)e.To);
		return address => map.TryGetValue(address, out var to) ? to : Array.Empty<ulong>();
	}

	[Fact]
	public void FindsTheTargetWhenARootPointsAtItDirectly() {
		var paths = RootPathFinder.FindPaths(
			target: 0x10,
			roots: new[] { new RootCandidate(0x10, "StrongHandle", 0xAA) },
			successors: Graph());

		var path = Assert.Single(paths);
		Assert.Equal("StrongHandle", path.Root.Kind);
		Assert.Equal(new ulong[] { 0x10 }, path.Path);
	}

	[Fact]
	public void FindsATransitivelyHeldObject() {
		// static -> Holder -> List -> Array -> Leaf. This is the shape that a direct-match-only
		// implementation misses entirely, and it is the normal shape for a user object.
		var paths = RootPathFinder.FindPaths(
			target: 0x50,
			roots: new[] { new RootCandidate(0x10, "StaticVar", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20 }),
				(0x20, new ulong[] { 0x30 }),
				(0x30, new ulong[] { 0x40 }),
				(0x40, new ulong[] { 0x50 })));

		var path = Assert.Single(paths);
		Assert.Equal(new ulong[] { 0x10, 0x20, 0x30, 0x40, 0x50 }, path.Path);
		Assert.Equal(4, path.Path.Count - 1);
	}

	[Fact]
	public void ReturnsNothingForAnUnrootedObject() {
		var paths = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph((0x10, new ulong[] { 0x20 })));

		Assert.Empty(paths);
	}

	[Fact]
	public void ReturnsNothingWhenThereAreNoRoots() {
		var paths = RootPathFinder.FindPaths(0x10, Array.Empty<RootCandidate>(), Graph());
		Assert.Empty(paths);
	}

	[Fact]
	public void PrefersTheShortestPath() {
		// Two routes to the target: 0x10 -> 0x50, and 0x10 -> 0x20 -> 0x30 -> 0x50.
		var paths = RootPathFinder.FindPaths(
			target: 0x50,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20, 0x50 }),
				(0x20, new ulong[] { 0x30 }),
				(0x30, new ulong[] { 0x50 })));

		var path = Assert.Single(paths);
		Assert.Equal(new ulong[] { 0x10, 0x50 }, path.Path);
	}

	[Fact]
	public void TerminatesOnACycle() {
		var paths = RootPathFinder.FindPaths(
			target: 0x40,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20 }),
				(0x20, new ulong[] { 0x30 }),
				(0x30, new ulong[] { 0x20, 0x40 })));   // 0x30 points back at 0x20

		var path = Assert.Single(paths);
		Assert.Equal(new ulong[] { 0x10, 0x20, 0x30, 0x40 }, path.Path);
	}

	[Fact]
	public void TerminatesWhenTheTargetIsInsideACycleThatExcludesIt() {
		var paths = RootPathFinder.FindPaths(
			target: 0xDEAD,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20 }),
				(0x20, new ulong[] { 0x10 })));

		Assert.Empty(paths);
	}

	[Fact]
	public void FindsMultipleNodeDisjointPathsWhenAsked() {
		// Two independent reasons the target is alive; both should be reported.
		var paths = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] {
				new RootCandidate(0x10, "StaticVar", 0xAA),
				new RootCandidate(0x20, "Stack", 0xBB)
			},
			successors: Graph(
				(0x10, new ulong[] { 0x11 }),
				(0x11, new ulong[] { 0x99 }),
				(0x20, new ulong[] { 0x21 }),
				(0x21, new ulong[] { 0x99 })),
			maxPaths: 2);

		Assert.Equal(2, paths.Count);
		Assert.Contains(paths, p => p.Root.Kind == "StaticVar");
		Assert.Contains(paths, p => p.Root.Kind == "Stack");
	}

	[Fact]
	public void NeverReturnsTheSamePathTwice() {
		// Two roots pointing at the same object produce one chain, not two copies of it. A depth-1
		// path has no interior nodes, so banning only interior nodes let the second pass re-find it.
		var paths = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] {
				new RootCandidate(0x10, "StrongHandle", 0xAA),
				new RootCandidate(0x10, "StrongHandle", 0xAA)
			},
			successors: Graph((0x10, new ulong[] { 0x99 })),
			maxPaths: 4);

		Assert.Single(paths);
	}

	[Fact]
	public void DistinctPathsShareNoIntermediateNodes() {
		var paths = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] {
				new RootCandidate(0x10, "StaticVar", 0xAA),
				new RootCandidate(0x20, "Stack", 0xBB)
			},
			successors: Graph(
				(0x10, new ulong[] { 0x30 }),
				(0x20, new ulong[] { 0x30 }),   // both routes converge on 0x30
				(0x30, new ulong[] { 0x99 })),
			maxPaths: 4);

		// Only one chain exists once the shared node is accounted for.
		Assert.Single(paths);
	}

	[Fact]
	public void ReturnsOnlyOnePathByDefault() {
		var paths = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] {
				new RootCandidate(0x10, "StaticVar", 0xAA),
				new RootCandidate(0x20, "Stack", 0xBB)
			},
			successors: Graph(
				(0x10, new ulong[] { 0x99 }),
				(0x20, new ulong[] { 0x99 })));

		Assert.Single(paths);
	}

	[Fact]
	public void DoesNotExceedTheRequestedPathCount() {
		var paths = RootPathFinder.FindPaths(
			target: 0x99,
			roots: Enumerable.Range(1, 10).Select(i => new RootCandidate((ulong)i * 0x10, "Stack", (ulong)i)).ToList(),
			successors: address => address == 0x99 ? Array.Empty<ulong>() : new ulong[] { 0x99 },
			maxPaths: 3);

		Assert.Equal(3, paths.Count);
	}

	[Fact]
	public void SurvivesAnUnreadableObject() {
		// A corrupt object must not abort the whole search.
		var paths = RootPathFinder.FindPaths(
			target: 0x50,
			roots: new[] {
				new RootCandidate(0xBAD, "Stack", 0xAA),
				new RootCandidate(0x10, "Stack", 0xBB)
			},
			successors: address => address == 0xBAD
				? throw new InvalidOperationException("unreadable memory")
				: (address == 0x10 ? new ulong[] { 0x50 } : Array.Empty<ulong>()));

		var path = Assert.Single(paths);
		Assert.Equal(new ulong[] { 0x10, 0x50 }, path.Path);
	}

	[Fact]
	public void RespectsTheTraversalBudget() {
		// A chain longer than the budget must give up rather than walk the whole heap.
		var paths = RootPathFinder.FindPaths(
			target: 1000,
			roots: new[] { new RootCandidate(1, "Stack", 0xAA) },
			successors: address => address < 1000 ? new[] { address + 1 } : Array.Empty<ulong>(),
			maxPaths: 1,
			maxNodesVisited: 10);

		Assert.Empty(paths);
	}

	[Fact]
	public void IgnoresNullReferences() {
		var paths = RootPathFinder.FindPaths(
			target: 0x30,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA), new RootCandidate(0, "Stack", 0xBB) },
			successors: Graph((0x10, new ulong[] { 0, 0x30 })));

		var path = Assert.Single(paths);
		Assert.Equal(new ulong[] { 0x10, 0x30 }, path.Path);
	}

	[Fact]
	public void ReturnsNothingForAZeroTarget() {
		Assert.Empty(RootPathFinder.FindPaths(0, new[] { new RootCandidate(0x10, "Stack", 0xAA) }, Graph()));
	}

	[Fact]
	public void CarriesRootMetadataThrough() {
		var paths = RootPathFinder.FindPaths(
			target: 0x20,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA, ManagedThreadId: 7, OSThreadId: 0x1234, IsPinned: true, IsInterior: true) },
			successors: Graph((0x10, new ulong[] { 0x20 })));

		var path = Assert.Single(paths);
		Assert.Equal(7, path.Root.ManagedThreadId);
		Assert.Equal(0x1234u, path.Root.OSThreadId);
		Assert.True(path.Root.IsPinned);
		Assert.True(path.Root.IsInterior);
		Assert.Equal(0xAAu, path.Root.RootAddress);
	}
}