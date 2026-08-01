using DotNetDump.Core.Models;
using DotNetDump.Core.Trees;

namespace DotNetDump.Tests;

/// <summary>
/// The gcroot retention tree (Phase 5.3, DATA_CONTRACT.md &#0167;4.3), exercised against hand-built
/// <see cref="GCRootSearchInfo"/> fixtures -- no dump required, the same way
/// <see cref="RootPathFinderTests"/> covers the search underneath this.
/// </summary>
/// <remarks>
/// The fixtures deliberately include shapes <c>RootPathFinder</c> does not currently produce
/// (paths sharing a prefix, since it returns node-disjoint paths): the trie merge is specified by
/// the contract, not by the current finder, and must be correct if the two ever diverge.
/// </remarks>
public class GCRootTreeBuilderTests {
	private static GCRootPathNode Node(ulong address, string typeName, ulong size = 24) =>
		new() { Address = address, TypeName = typeName, Size = size };

	private static GCRootPathInfo Path(string rootKind, params GCRootPathNode[] nodes) =>
		new() {
			RootKind = rootKind,
			RootAddress = 0xAA,
			TargetAddress = nodes[^1].Address,
			Path = nodes.ToList(),
		};

	private static GCRootSearchInfo Search(ulong target, bool truncated, long nodesVisited, params GCRootPathInfo[] paths) =>
		new() { TargetAddress = target, Truncated = truncated, NodesVisited = nodesVisited, Paths = paths.ToList() };

	// ---- The trie merge ----------------------------------------------------------------------

	[Fact]
	public void OnePathBecomesOneChainRootFirst() {
		var search = Search(0x50, truncated: false, nodesVisited: 10,
			Path("StaticVar", Node(0x10, "MyApp.Holder"), Node(0x20, "System.Collections.Generic.List`1"), Node(0x50, "MyApp.Leaf")));

		var tree = GCRootTreeBuilder.Build(search);

		var root = Assert.Single(tree.Roots);
		Assert.Equal("MyApp.Holder", root.Node.Label);
		Assert.Equal(TreeNodeKind.Root, root.Node.Kind);
		Assert.Equal(0x10ul, root.Node.Address);

		var middle = Assert.Single(root.Children);
		Assert.Equal("System.Collections.Generic.List`1", middle.Node.Label);
		Assert.Equal(TreeNodeKind.Object, middle.Node.Kind);

		var leaf = Assert.Single(middle.Children);
		Assert.Equal("MyApp.Leaf", leaf.Node.Label);
		Assert.Empty(leaf.Children);
		Assert.False(leaf.Node.HasChildren);
		Assert.Contains(leaf.Node.Badges, badge => badge.Label == "target");
	}

	[Fact]
	public void PathsSharingAPrefixCollapseIntoOneBranch() {
		// Two chains through the same holder and the same list, diverging only at the last hop.
		var search = Search(0x50, truncated: false, nodesVisited: 20,
			Path("StaticVar", Node(0x10, "MyApp.Holder"), Node(0x20, "MyApp.Box"), Node(0x50, "MyApp.Leaf")),
			Path("StaticVar", Node(0x10, "MyApp.Holder"), Node(0x20, "MyApp.Box"), Node(0x51, "MyApp.Other")));

		var tree = GCRootTreeBuilder.Build(search);

		// The shared prefix is rendered once, not twice: that is the whole point of the merge.
		var root = Assert.Single(tree.Roots);
		var box = Assert.Single(root.Children);
		Assert.Equal(0x20ul, box.Node.Address);
		Assert.Equal(2, box.Children.Count);
		Assert.Equal(2, box.Node.ChildCount);
		Assert.Equal(new ulong[] { 0x50, 0x51 }, box.Children.Select(c => c.Node.Address!.Value).ToArray());
	}

	[Fact]
	public void PathsForkAtTheNodeWhereTheyDiverge() {
		var search = Search(0x99, truncated: false, nodesVisited: 30,
			Path("StaticVar", Node(0x10, "A"), Node(0x20, "B"), Node(0x30, "C"), Node(0x99, "Target")),
			Path("StaticVar", Node(0x10, "A"), Node(0x20, "B"), Node(0x40, "D"), Node(0x99, "Target")));

		var tree = GCRootTreeBuilder.Build(search);

		var a = Assert.Single(tree.Roots);
		var b = Assert.Single(a.Children);
		Assert.Equal(0x20ul, b.Node.Address);

		// The fork is at B, not before it and not after it.
		Assert.Equal(new ulong[] { 0x30, 0x40 }, b.Children.Select(c => c.Node.Address!.Value).ToArray());

		// The target is reached down both forks and is a distinct node under each -- a trie keyed on
		// the path taken, not a graph that re-joins.
		foreach (var fork in b.Children) {
			var target = Assert.Single(fork.Children);
			Assert.Equal(0x99ul, target.Node.Address);
		}

		Assert.NotEqual(b.Children[0].Children[0].Node.Id, b.Children[1].Children[0].Node.Id);
	}

	[Fact]
	public void NodeDisjointPathsStayAsSeparateBranches() {
		// What RootPathFinder actually produces today: two chains sharing nothing but the target.
		var search = Search(0x99, truncated: false, nodesVisited: 40,
			Path("StaticVar", Node(0x10, "A"), Node(0x99, "Target")),
			Path("Pinning", Node(0x20, "B"), Node(0x99, "Target")));

		var tree = GCRootTreeBuilder.Build(search);

		Assert.Equal(2, tree.Roots.Count);
		Assert.All(tree.Roots, branch => Assert.Equal(TreeNodeKind.Root, branch.Node.Kind));
		Assert.Equal(new ulong[] { 0x10, 0x20 }, tree.Roots.Select(r => r.Node.Address!.Value).ToArray());
	}

	// ---- Root badges -------------------------------------------------------------------------

	[Fact]
	public void RootNodeCarriesKindPinnedInteriorAndOwningThread() {
		var path = Path("Stack", Node(0x10, "MyApp.Holder"), Node(0x50, "MyApp.Leaf"));
		path.RootName = "Main.args";
		path.ManagedThreadId = 7;
		path.OSThreadId = 0x1A4;
		path.IsPinned = true;
		path.IsInterior = true;

		var tree = GCRootTreeBuilder.Build(Search(0x50, truncated: false, nodesVisited: 5, path));

		var labels = Assert.Single(tree.Roots).Node.Badges.Select(b => b.Label).ToList();
		Assert.Contains("Stack", labels);
		Assert.Contains("Main.args", labels);
		Assert.Contains("thread 7", labels);
		Assert.Contains("pinned", labels);
		Assert.Contains("interior", labels);
	}

	[Fact]
	public void RootWithoutAManagedThreadFallsBackToTheOSThread() {
		var path = Path("Stack", Node(0x10, "MyApp.Holder"), Node(0x50, "MyApp.Leaf"));
		path.OSThreadId = 0x1A4;

		var tree = GCRootTreeBuilder.Build(Search(0x50, truncated: false, nodesVisited: 5, path));

		Assert.Contains(Assert.Single(tree.Roots).Node.Badges, b => b.Label == "OS thread 1A4");
	}

	[Fact]
	public void OnlyTheRootNodeIsKindRoot() {
		var tree = GCRootTreeBuilder.Build(Search(0x50, truncated: false, nodesVisited: 5,
			Path("StaticVar", Node(0x10, "A"), Node(0x20, "B"), Node(0x50, "Target"))));

		var root = Assert.Single(tree.Roots);
		Assert.Equal(TreeNodeKind.Root, root.Node.Kind);
		Assert.Equal(TreeNodeKind.Object, root.Children[0].Node.Kind);
		Assert.Equal(TreeNodeKind.Object, root.Children[0].Children[0].Node.Kind);
	}

	// ---- The truncation distinction ----------------------------------------------------------
	//
	// The single most important behaviour in this task. docs/GCROOT_TRUNCATION.md: a search that
	// exhausted its budget and a search that proved nothing is holding the object are different
	// answers, and only the second one may be called "unrooted".

	[Fact]
	public void NoPathsAndNotTruncatedIsTheOnlyUnrootedOutcome() {
		var tree = GCRootTreeBuilder.Build(Search(0x99, truncated: false, nodesVisited: 1234));

		Assert.Empty(tree.Roots);
		Assert.Equal(GCRootOutcome.Unrooted, tree.Outcome);
		Assert.False(tree.Truncated);
	}

	[Fact]
	public void NoPathsButTruncatedIsInconclusive_NeverUnrooted() {
		var tree = GCRootTreeBuilder.Build(Search(0x99, truncated: true, nodesVisited: 2_000_000));

		Assert.Empty(tree.Roots);
		Assert.Equal(GCRootOutcome.Inconclusive, tree.Outcome);
		Assert.NotEqual(GCRootOutcome.Unrooted, tree.Outcome);
		Assert.True(tree.Truncated);
		Assert.Equal(2_000_000, tree.NodesVisited);
	}

	[Fact]
	public void TheTwoEmptyResultsAreDistinguishable() {
		// Stated as its own test because the defect was precisely that they were not: the same empty
		// collection reached the formatter from both, and the formatter guessed.
		var conclusive = GCRootTreeBuilder.Build(Search(0x99, truncated: false, nodesVisited: 500));
		var truncated = GCRootTreeBuilder.Build(Search(0x99, truncated: true, nodesVisited: 2_000_000));

		Assert.Empty(conclusive.Roots);
		Assert.Empty(truncated.Roots);
		Assert.NotEqual(conclusive.Outcome, truncated.Outcome);
	}

	[Fact]
	public void PathsFoundButTruncatedIsPartial_NotComplete() {
		// Some passes succeeded, a later one exhausted its budget: the chains shown are real, but
		// the search stopped before it could rule out more (GCROOT_TRUNCATION.md, "Evidence").
		var tree = GCRootTreeBuilder.Build(Search(0x50, truncated: true, nodesVisited: 2_000_042,
			Path("StaticVar", Node(0x10, "A"), Node(0x50, "Target"))));

		Assert.Single(tree.Roots);
		Assert.Equal(GCRootOutcome.RootedPartial, tree.Outcome);
		Assert.True(tree.Truncated);
	}

	[Fact]
	public void PathsFoundAndNotTruncatedIsConclusivelyRooted() {
		var tree = GCRootTreeBuilder.Build(Search(0x50, truncated: false, nodesVisited: 900,
			Path("StaticVar", Node(0x10, "A"), Node(0x50, "Target"))));

		Assert.Equal(GCRootOutcome.Rooted, tree.Outcome);
		Assert.False(tree.Truncated);
	}

	[Fact]
	public void APathCarryingNoNodesIsNeverReportedAsUnrooted() {
		// Degenerate, and not something GetGCRoots produces -- but an unexplained empty result is
		// exactly the shape that must not become a confident "eligible for collection".
		var search = new GCRootSearchInfo {
			TargetAddress = 0x99,
			Truncated = false,
			NodesVisited = 10,
			Paths = [new GCRootPathInfo { RootKind = "StaticVar" }],
		};

		var tree = GCRootTreeBuilder.Build(search);

		Assert.Empty(tree.Roots);
		Assert.Equal(GCRootOutcome.Inconclusive, tree.Outcome);
	}

	// ---- Node shape --------------------------------------------------------------------------

	[Fact]
	public void DetailCarriesTheAddressAndSize() {
		var tree = GCRootTreeBuilder.Build(Search(0x50, truncated: false, nodesVisited: 5,
			Path("StaticVar", Node(0x10, "A", size: 2048), Node(0x50, "Target", size: 24))));

		var root = Assert.Single(tree.Roots);
		Assert.Contains("0000000000000010", root.Node.Detail, StringComparison.Ordinal);
		Assert.Contains("2.0 KB", root.Node.Detail, StringComparison.Ordinal);
	}

	[Fact]
	public void EveryNodeIdIsUniqueWithinTheTree() {
		var search = Search(0x99, truncated: false, nodesVisited: 30,
			Path("StaticVar", Node(0x10, "A"), Node(0x20, "B"), Node(0x99, "Target")),
			Path("Pinning", Node(0x30, "C"), Node(0x20, "B"), Node(0x99, "Target")));

		var tree = GCRootTreeBuilder.Build(search);

		var ids = Flatten(tree.Roots).Select(n => n.Node.Id).ToList();
		Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
		// The same object reached from two different roots is two nodes of this tree, one per chain.
		Assert.Equal(6, ids.Count);
	}

	[Fact]
	public void AnUnknownTypeStillRendersALabel() {
		var tree = GCRootTreeBuilder.Build(Search(0x50, truncated: false, nodesVisited: 5,
			Path("StaticVar", new GCRootPathNode { Address = 0x10, TypeName = null, Size = 0 }, Node(0x50, "Target"))));

		Assert.Equal("<unknown type>", Assert.Single(tree.Roots).Node.Label);
	}

	private static IEnumerable<GCRootTreeNode> Flatten(IEnumerable<GCRootTreeNode> nodes) {
		foreach (var node in nodes) {
			yield return node;
			foreach (var child in Flatten(node.Children)) {
				yield return child;
			}
		}
	}
}