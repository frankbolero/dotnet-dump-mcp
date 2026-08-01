using DotNetDump.Core.Models;
using DotNetDump.Core.Trees;

namespace DotNetDump.Tests;

/// <summary>
/// The namespace/assembly rollup (Phase 5.1, DATA_CONTRACT.md &#0167;4.2), exercised against hand-built
/// <see cref="HeapStatItem"/> lists. Pure grouping, no dump required.
/// </summary>
public class NamespaceRollupBuilderTests {
	private static HeapStatItem Stat(string typeName, int count, long totalSize) =>
		new() { TypeName = typeName, Count = count, TotalSize = totalSize };

	[Fact]
	public void RootChildrenAreTopLevelNamespaces() {
		var stats = new[] {
			Stat("System.String", 10, 1000),
			Stat("System.Collections.Generic.List`1", 5, 500),
			Stat("MyApp.Domain.CacheEntry", 3, 300),
		};

		var roots = NamespaceRollupBuilder.GetChildren(stats, nodeId: null);

		Assert.Equal(2, roots.Count);
		Assert.Contains(roots, n => n.Label == "System" && n.Kind == TreeNodeKind.Namespace);
		Assert.Contains(roots, n => n.Label == "MyApp" && n.Kind == TreeNodeKind.Namespace);
	}

	[Fact]
	public void NamespaceNodeRollsUpCountAndSizeFromEveryDescendant() {
		var stats = new[] {
			Stat("System.String", 10, 1000),
			Stat("System.Collections.Generic.List`1", 5, 500),
		};

		var system = Assert.Single(NamespaceRollupBuilder.GetChildren(stats, nodeId: null));
		Assert.Equal(15, int.Parse(system.Badges[0].Label.Replace(",", "")));
		Assert.True(system.HasChildren);
	}

	[Fact]
	public void GenericArityStaysPartOfTheLeafAndNeverBecomesANamespaceLevel() {
		// Dictionary<System.String,MyApp.Domain.CacheEntry> must not split on the '.' inside
		// System.String or MyApp.Domain -- those belong to the generic arguments, not the outer
		// type's own namespace path.
		var stats = new[] {
			Stat("System.Collections.Generic.Dictionary<System.String,MyApp.Domain.CacheEntry>", 1, 100),
		};

		var system = Assert.Single(NamespaceRollupBuilder.GetChildren(stats, nodeId: null));
		var collections = Assert.Single(NamespaceRollupBuilder.GetChildren(stats, system.Id));
		var generic = Assert.Single(NamespaceRollupBuilder.GetChildren(stats, collections.Id));

		Assert.Equal("Generic", generic.Label);
		var leaf = Assert.Single(NamespaceRollupBuilder.GetChildren(stats, generic.Id));
		Assert.Equal("Dictionary<System.String,MyApp.Domain.CacheEntry>", leaf.Label);
		Assert.Equal(TreeNodeKind.Type, leaf.Kind);
		Assert.False(leaf.HasChildren);
	}

	[Fact]
	public void ANamespaceWithNoDotIsALeafDirectlyUnderTheRoot() {
		var stats = new[] { Stat("Byte[]", 1, 8) };

		var leaf = Assert.Single(NamespaceRollupBuilder.GetChildren(stats, nodeId: null));

		Assert.Equal("Byte[]", leaf.Label);
		Assert.Equal(TreeNodeKind.Type, leaf.Kind);
		Assert.False(leaf.HasChildren);
	}

	[Fact]
	public void FanOutBeyondFiftyChildrenGetsACappedPageAndAMoreNode() {
		var stats = Enumerable.Range(0, 60)
			.Select(i => Stat($"System.Generated.Type{i:D2}", 1, 100 - i))
			.ToArray();

		var children = NamespaceRollupBuilder.GetChildren(stats, "System.Generated");

		Assert.Equal(NamespaceRollupBuilder.PageSize + 1, children.Count);
		Assert.Equal(NamespaceRollupBuilder.PageSize, children.Count(n => n.Kind == TreeNodeKind.Type));
		var more = Assert.Single(children, n => n.Kind == TreeNodeKind.More);
		Assert.Contains("10", more.Label);
		Assert.False(more.HasChildren);
	}

	[Fact]
	public void ExpandingTheMoreNodeReturnsTheRemainingSiblingsAndNoFurtherMoreNodeWhenTheyFit() {
		var stats = Enumerable.Range(0, 60)
			.Select(i => Stat($"System.Generated.Type{i:D2}", 1, 100 - i))
			.ToArray();

		var firstPage = NamespaceRollupBuilder.GetChildren(stats, "System.Generated");
		var more = Assert.Single(firstPage, n => n.Kind == TreeNodeKind.More);

		var nextPage = NamespaceRollupBuilder.GetChildren(stats, more.Id);

		Assert.Equal(10, nextPage.Count);
		Assert.DoesNotContain(nextPage, n => n.Kind == TreeNodeKind.More);
	}

	[Fact]
	public void ChildrenAreOrderedByRolledUpSizeDescending() {
		var stats = new[] {
			Stat("Small", 1, 10),
			Stat("Big", 1, 1000),
			Stat("Medium", 1, 100),
		};

		var children = NamespaceRollupBuilder.GetChildren(stats, nodeId: null);

		Assert.Equal(["Big", "Medium", "Small"], children.Select(c => c.Label).ToArray());
	}

	[Fact]
	public void UnknownNodeIdReturnsNoChildrenRatherThanThrowing() {
		var stats = new[] { Stat("System.String", 1, 10) };

		var children = NamespaceRollupBuilder.GetChildren(stats, "NoSuchNamespace");

		Assert.Empty(children);
	}
}