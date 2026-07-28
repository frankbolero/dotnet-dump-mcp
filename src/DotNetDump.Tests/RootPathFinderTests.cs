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
		var result = RootPathFinder.FindPaths(
			target: 0x10,
			roots: new[] { new RootCandidate(0x10, "StrongHandle", 0xAA) },
			successors: Graph());

		var path = Assert.Single(result.Paths);
		Assert.Equal("StrongHandle", path.Root.Kind);
		Assert.Equal(new ulong[] { 0x10 }, path.Path);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void FindsATransitivelyHeldObject() {
		// static -> Holder -> List -> Array -> Leaf. This is the shape that a direct-match-only
		// implementation misses entirely, and it is the normal shape for a user object.
		var result = RootPathFinder.FindPaths(
			target: 0x50,
			roots: new[] { new RootCandidate(0x10, "StaticVar", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20 }),
				(0x20, new ulong[] { 0x30 }),
				(0x30, new ulong[] { 0x40 }),
				(0x40, new ulong[] { 0x50 })));

		var path = Assert.Single(result.Paths);
		Assert.Equal(new ulong[] { 0x10, 0x20, 0x30, 0x40, 0x50 }, path.Path);
		Assert.Equal(4, path.Path.Count - 1);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void ReturnsNothingForAnUnrootedObject() {
		var result = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph((0x10, new ulong[] { 0x20 })));

		Assert.Empty(result.Paths);
		// The search exhausted the reachable graph and conclusively found nothing — this must be
		// distinguishable from giving up on the node budget.
		Assert.False(result.Truncated);
	}

	[Fact]
	public void ReturnsNothingWhenThereAreNoRoots() {
		var result = RootPathFinder.FindPaths(0x10, Array.Empty<RootCandidate>(), Graph());
		Assert.Empty(result.Paths);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void PrefersTheShortestPath() {
		// Two routes to the target: 0x10 -> 0x50, and 0x10 -> 0x20 -> 0x30 -> 0x50.
		var result = RootPathFinder.FindPaths(
			target: 0x50,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20, 0x50 }),
				(0x20, new ulong[] { 0x30 }),
				(0x30, new ulong[] { 0x50 })));

		var path = Assert.Single(result.Paths);
		Assert.Equal(new ulong[] { 0x10, 0x50 }, path.Path);
	}

	[Fact]
	public void TerminatesOnACycle() {
		var result = RootPathFinder.FindPaths(
			target: 0x40,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20 }),
				(0x20, new ulong[] { 0x30 }),
				(0x30, new ulong[] { 0x20, 0x40 })));   // 0x30 points back at 0x20

		var path = Assert.Single(result.Paths);
		Assert.Equal(new ulong[] { 0x10, 0x20, 0x30, 0x40 }, path.Path);
	}

	[Fact]
	public void TerminatesWhenTheTargetIsInsideACycleThatExcludesIt() {
		var result = RootPathFinder.FindPaths(
			target: 0xDEAD,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph(
				(0x10, new ulong[] { 0x20 }),
				(0x20, new ulong[] { 0x10 })));

		Assert.Empty(result.Paths);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void FindsMultipleNodeDisjointPathsWhenAsked() {
		// Two independent reasons the target is alive; both should be reported.
		var result = RootPathFinder.FindPaths(
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

		Assert.Equal(2, result.Paths.Count);
		Assert.Contains(result.Paths, p => p.Root.Kind == "StaticVar");
		Assert.Contains(result.Paths, p => p.Root.Kind == "Stack");
		Assert.False(result.Truncated);
	}

	[Fact]
	public void NeverReturnsTheSamePathTwice() {
		// Two roots pointing at the same object produce one chain, not two copies of it. A depth-1
		// path has no interior nodes, so banning only interior nodes let the second pass re-find it.
		var result = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] {
				new RootCandidate(0x10, "StrongHandle", 0xAA),
				new RootCandidate(0x10, "StrongHandle", 0xAA)
			},
			successors: Graph((0x10, new ulong[] { 0x99 })),
			maxPaths: 4);

		Assert.Single(result.Paths);
	}

	[Fact]
	public void DistinctPathsShareNoIntermediateNodes() {
		var result = RootPathFinder.FindPaths(
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
		Assert.Single(result.Paths);
	}

	[Fact]
	public void ReturnsOnlyOnePathByDefault() {
		var result = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] {
				new RootCandidate(0x10, "StaticVar", 0xAA),
				new RootCandidate(0x20, "Stack", 0xBB)
			},
			successors: Graph(
				(0x10, new ulong[] { 0x99 }),
				(0x20, new ulong[] { 0x99 })));

		Assert.Single(result.Paths);
	}

	[Fact]
	public void DoesNotExceedTheRequestedPathCount() {
		var result = RootPathFinder.FindPaths(
			target: 0x99,
			roots: Enumerable.Range(1, 10).Select(i => new RootCandidate((ulong)i * 0x10, "Stack", (ulong)i)).ToList(),
			successors: address => address == 0x99 ? Array.Empty<ulong>() : new ulong[] { 0x99 },
			maxPaths: 3);

		Assert.Equal(3, result.Paths.Count);
	}

	[Fact]
	public void SurvivesAnUnreadableObject() {
		// A corrupt object must not abort the whole search.
		var result = RootPathFinder.FindPaths(
			target: 0x50,
			roots: new[] {
				new RootCandidate(0xBAD, "Stack", 0xAA),
				new RootCandidate(0x10, "Stack", 0xBB)
			},
			successors: address => address == 0xBAD
				? throw new InvalidOperationException("unreadable memory")
				: (address == 0x10 ? new ulong[] { 0x50 } : Array.Empty<ulong>()));

		var path = Assert.Single(result.Paths);
		Assert.Equal(new ulong[] { 0x10, 0x50 }, path.Path);
	}

	[Fact]
	public void RespectsTheTraversalBudget() {
		// A chain longer than the budget must give up rather than walk the whole heap. Before the
		// fix, this and ReturnsNothingForAnUnrootedObject were indistinguishable: both returned an
		// empty list. Now the caller can tell "gave up" from "genuinely no path" via Truncated.
		var result = RootPathFinder.FindPaths(
			target: 1000,
			roots: new[] { new RootCandidate(1, "Stack", 0xAA) },
			successors: address => address < 1000 ? new[] { address + 1 } : Array.Empty<ulong>(),
			maxPaths: 1,
			maxNodesVisited: 10);

		Assert.Empty(result.Paths);
		Assert.True(result.Truncated);
		Assert.Equal(10, result.NodesVisited);
	}

	[Fact]
	public void ACompletedSearchReportingNoPathsIsDistinguishableFromATruncatedOne() {
		// Same shape of result (zero paths) for two different reasons: this is the core of the
		// defect. A short chain that never reaches the target, searched with a generous budget,
		// must report Truncated = false — the search actually finished.
		var result = RootPathFinder.FindPaths(
			target: 0xDEAD,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA) },
			successors: Graph((0x10, new ulong[] { 0x20 }), (0x20, new ulong[] { 0x30 })),
			maxNodesVisited: 1000);

		Assert.Empty(result.Paths);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void APartialMultiPathResultIsFlaggedTruncatedEvenThoughPathsWereFound() {
		// maxPaths gives each pass a fresh budget. Two independent, cheap-to-find paths exist; a
		// third would require walking a long chain that exceeds the budget. The two found paths are
		// real and must be returned, but the overall result must still say the search did not
		// conclusively rule out further paths.
		var edges = new List<(ulong From, ulong[] To)> {
			(0x10, new ulong[] { 0x99 }),   // pass 1: found immediately
			(0x20, new ulong[] { 0x99 }),   // pass 2: found immediately (banned nodes don't affect this seed)
		};
		// A third root leads to the target only via a long chain that blows the budget.
		ulong cursor = 0x1000;
		edges.Add((0x30, new ulong[] { cursor }));
		for (ulong i = 0; i < 50; i++) {
			edges.Add((cursor, new[] { cursor + 1 }));
			cursor++;
		}

		var result = RootPathFinder.FindPaths(
			target: 0x99,
			roots: new[] {
				new RootCandidate(0x10, "StaticVar", 0xAA),
				new RootCandidate(0x20, "Stack", 0xBB),
				new RootCandidate(0x30, "Handle", 0xCC),
			},
			successors: Graph(edges.ToArray()),
			maxPaths: 3,
			maxNodesVisited: 5);

		Assert.Equal(2, result.Paths.Count);
		Assert.True(result.Truncated);
	}

	[Fact]
	public void MaxNodesZeroMeansUnlimitedAndCompletesASearchTheDefaultBudgetWouldTruncate() {
		// A chain of 100 nodes exceeds a tiny budget but must complete when maxNodesVisited is 0.
		var result = RootPathFinder.FindPaths(
			target: 100,
			roots: new[] { new RootCandidate(1, "Stack", 0xAA) },
			successors: address => address < 100 ? new[] { address + 1 } : Array.Empty<ulong>(),
			maxPaths: 1,
			maxNodesVisited: 0);

		var path = Assert.Single(result.Paths);
		Assert.Equal(100, path.Path.Count);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void NegativeMaxNodesAlsoMeansUnlimited() {
		var result = RootPathFinder.FindPaths(
			target: 100,
			roots: new[] { new RootCandidate(1, "Stack", 0xAA) },
			successors: address => address < 100 ? new[] { address + 1 } : Array.Empty<ulong>(),
			maxPaths: 1,
			maxNodesVisited: -1);

		Assert.Single(result.Paths);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void IgnoresNullReferences() {
		var result = RootPathFinder.FindPaths(
			target: 0x30,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA), new RootCandidate(0, "Stack", 0xBB) },
			successors: Graph((0x10, new ulong[] { 0, 0x30 })));

		var path = Assert.Single(result.Paths);
		Assert.Equal(new ulong[] { 0x10, 0x30 }, path.Path);
	}

	[Fact]
	public void ReturnsNothingForAZeroTarget() {
		var result = RootPathFinder.FindPaths(0, new[] { new RootCandidate(0x10, "Stack", 0xAA) }, Graph());
		Assert.Empty(result.Paths);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void CarriesRootMetadataThrough() {
		var result = RootPathFinder.FindPaths(
			target: 0x20,
			roots: new[] { new RootCandidate(0x10, "Stack", 0xAA, ManagedThreadId: 7, OSThreadId: 0x1234, IsPinned: true, IsInterior: true) },
			successors: Graph((0x10, new ulong[] { 0x20 })));

		var path = Assert.Single(result.Paths);
		Assert.Equal(7, path.Root.ManagedThreadId);
		Assert.Equal(0x1234u, path.Root.OSThreadId);
		Assert.True(path.Root.IsPinned);
		Assert.True(path.Root.IsInterior);
		Assert.Equal(0xAAu, path.Root.RootAddress);
	}
}