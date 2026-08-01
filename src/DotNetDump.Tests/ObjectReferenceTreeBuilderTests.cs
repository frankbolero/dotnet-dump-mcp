using DotNetDump.Core.Models;
using DotNetDump.Core.Trees;

namespace DotNetDump.Tests;

/// <summary>
/// The object reference navigator (DATA_CONTRACT.md &#0167;4.5), exercised against hand-built object
/// graphs. No dump required, which is the whole point: the two behaviours most worth proving --
/// cycle detection and the depth cap -- are exactly the two a healthy fixture dump will not exercise
/// on demand. SERVER.md &#0167;7: "Cycle detection needs a deliberately cyclic fixture -- it will not
/// appear by accident."
/// </summary>
public class ObjectReferenceTreeBuilderTests {
	/// <summary>A synthetic heap: each edge becomes one reference field on the source object. Mirrors
	/// <see cref="RootPathFinderTests"/>' own <c>Graph</c> helper.</summary>
	private static Func<ulong, IReadOnlyList<ObjectField>> Graph(params (ulong From, ulong[] To)[] edges) {
		var map = edges.ToDictionary(
			e => e.From,
			e => (IReadOnlyList<ObjectField>)e.To
				.Select((to, i) => new ObjectField {
					Name = $"_f{i}",
					TypeName = "Node",
					IsReference = true,
					Address = to,
					Offset = 8 + (i * 8),
				})
				.ToList());

		return address => map.TryGetValue(address, out var fields) ? fields : [];
	}

	/// <summary>One expanded node: its depth (addresses on its path, root = 1) and the node itself.</summary>
	private readonly record struct Visited(int Depth, TreeNode Node);

	/// <summary>
	/// Drives the builder the way a user holding the expand key drives the server: expand every node
	/// that claims to have children, recursively, until nothing is left to expand. This is the
	/// termination proof. <paramref name="budget"/> is not a feature of the tree -- it is the test's
	/// own tripwire, so a builder that expanded forever fails the assertion instead of hanging CI.
	/// </summary>
	private static List<Visited> ExpandEverything(
		ulong root, Func<ulong, IReadOnlyList<ObjectField>> graph, int budget = 20_000) {

		var seen = new List<Visited>();
		var pending = new Queue<string>();
		pending.Enqueue(ObjectReferenceTreeBuilder.FormatPath([root]));

		while (pending.Count > 0) {
			string id = pending.Dequeue();
			foreach (var node in ObjectReferenceTreeBuilder.GetChildren(id, graph)) {
				Assert.True(
					seen.Count < budget,
					$"Expansion did not terminate: more than {budget} nodes produced. This is the "
					+ "infinite-expansion failure mode cycle detection and the depth cap exist to prevent.");

				ObjectReferenceTreeBuilder.TryParsePath(node.Id, out var path);
				seen.Add(new Visited(path.Count, node));

				if (node.HasChildren) {
					pending.Enqueue(node.Id);
				}
			}
		}

		return seen;
	}

	// ---- Cycles ------------------------------------------------------------------------------

	[Fact]
	public void ATwoObjectCycleTerminatesAndIsFlagged() {
		// 0x10 -> 0x20 -> 0x10. The canonical parent/child back-reference, and infinitely deep
		// without a visited set on the path.
		var graph = Graph(
			(0x10, [0x20]),
			(0x20, [0x10]));

		var all = ExpandEverything(0x10, graph);

		// Exactly two nodes are ever produced: 0x20 under the root, then 0x10 again as a cycle stop.
		Assert.Equal(2, all.Count);

		var cycle = Assert.Single(all, v => v.Node.Kind == TreeNodeKind.Cycle).Node;
		Assert.Equal(0x10UL, cycle.Address);
		Assert.False(cycle.HasChildren);
		Assert.Contains(cycle.Badges, b => b.Label.Contains("0000000000000010", StringComparison.Ordinal));
		Assert.Contains(cycle.Badges, b => b.Tone == TreeBadgeTone.Warn);
	}

	[Fact]
	public void ASelfReferenceIsACycleAtTheFirstExpansion() {
		var children = ObjectReferenceTreeBuilder.GetChildren("10", Graph((0x10, [0x10])));

		var node = Assert.Single(children);
		Assert.Equal(TreeNodeKind.Cycle, node.Kind);
		Assert.False(node.HasChildren);
	}

	[Fact]
	public void ALongCycleTerminatesAtTheAddressThatClosesIt() {
		// 0x10 -> 0x20 -> 0x30 -> 0x40 -> 0x20: the cycle closes on an interior node, not the root,
		// so the flagged ancestor must be 0x20 and not whatever the tree was rooted at.
		var graph = Graph(
			(0x10, [0x20]),
			(0x20, [0x30]),
			(0x30, [0x40]),
			(0x40, [0x20]));

		var all = ExpandEverything(0x10, graph);

		var cycle = Assert.Single(all, v => v.Node.Kind == TreeNodeKind.Cycle);
		Assert.Equal(0x20UL, cycle.Node.Address);
		Assert.Equal(5, cycle.Depth);      // path 0x10-0x20-0x30-0x40-0x20, the last entry closing the loop
		Assert.Contains(cycle.Node.Badges, b => b.Label.Contains("0000000000000020", StringComparison.Ordinal));
	}

	[Fact]
	public void MutuallyReferencingObjectsUnderACommonParentBothTerminate() {
		// The shape an event-handler graph really has: root holds two objects that hold each other.
		var graph = Graph(
			(0x10, [0x20, 0x30]),
			(0x20, [0x30]),
			(0x30, [0x20]));

		var all = ExpandEverything(0x10, graph);

		Assert.All(all, v => Assert.True(v.Depth <= ObjectReferenceTreeBuilder.MaxDepth));
		Assert.Contains(all, v => v.Node.Kind == TreeNodeKind.Cycle);
	}

	// ---- Not cycles --------------------------------------------------------------------------

	[Fact]
	public void AnAcyclicGraphExpandsWithNoCycleFlags() {
		var graph = Graph(
			(0x10, [0x20, 0x30]),
			(0x20, [0x40]),
			(0x30, [0x50]));

		var all = ExpandEverything(0x10, graph);

		Assert.DoesNotContain(all, v => v.Node.Kind == TreeNodeKind.Cycle);
		Assert.Equal(4, all.Count);   // 0x20, 0x30, 0x40, 0x50
		Assert.All(all, v => Assert.Equal(TreeNodeKind.Field, v.Node.Kind));
	}

	[Fact]
	public void TwoSiblingsReferencingTheSameLeafAreNotCycles() {
		// The false positive a *global* visited set would produce, and the reason §4.5 says the visited
		// set is the path from the root: 0x50 is reached twice, but neither route revisits an address
		// already on its own root-to-node path. It is a shared leaf, not a cycle.
		var graph = Graph(
			(0x10, [0x20, 0x30]),
			(0x20, [0x50]),
			(0x30, [0x50]));

		var all = ExpandEverything(0x10, graph);

		Assert.DoesNotContain(all, v => v.Node.Kind == TreeNodeKind.Cycle);

		var shared = all.Where(v => v.Node.Address == 0x50).ToList();
		Assert.Equal(2, shared.Count);
		Assert.All(shared, v => Assert.True(v.Node.HasChildren, "A shared leaf stays expandable on every path that reaches it."));
	}

	[Fact]
	public void ADiamondIsNotACycle() {
		// 0x10 -> {0x20, 0x30} -> 0x40 -> 0x50. Classic re-convergence; no address repeats on any
		// single path, so nothing is flagged and both branches expand all the way down.
		var graph = Graph(
			(0x10, [0x20, 0x30]),
			(0x20, [0x40]),
			(0x30, [0x40]),
			(0x40, [0x50]));

		var all = ExpandEverything(0x10, graph);

		Assert.DoesNotContain(all, v => v.Node.Kind == TreeNodeKind.Cycle);
		Assert.Equal(2, all.Count(v => v.Node.Address == 0x50));
	}

	[Fact]
	public void AnAddressRepeatedOnASiblingBranchDoesNotSuppressTheRealCycleOnAnother() {
		// 0x30 is shared (not a cycle) while 0x20's own branch closes a genuine cycle back on 0x20.
		var graph = Graph(
			(0x10, [0x20, 0x40]),
			(0x20, [0x30]),
			(0x40, [0x30]),
			(0x30, [0x20]));

		var all = ExpandEverything(0x10, graph);

		// Under 0x10 -> 0x20 -> 0x30, the reference back to 0x20 is a cycle.
		Assert.Contains(all, v => v.Node.Kind == TreeNodeKind.Cycle && v.Node.Address == 0x20);
		// Under 0x10 -> 0x40 -> 0x30, the same reference to 0x20 is an ordinary expandable field.
		Assert.Contains(all, v => v.Node.Kind == TreeNodeKind.Field && v.Node.Address == 0x20 && v.Node.HasChildren);
	}

	// ---- Depth cap ---------------------------------------------------------------------------

	/// <summary>An infinite chain of distinct addresses: cycle detection can never stop it, because
	/// nothing ever repeats. Only the depth cap can.</summary>
	private static IReadOnlyList<ObjectField> Chain(ulong address) =>
		[new ObjectField { Name = "_next", TypeName = "Node", IsReference = true, Address = address + 1 }];

	[Fact]
	public void TheDepthCapStopsAnUnboundedChainOfDistinctAddresses() {
		var all = ExpandEverything(1, Chain);

		// Root is depth 1, so the deepest node produced is depth 64 and there are 63 of them.
		Assert.Equal(ObjectReferenceTreeBuilder.MaxDepth - 1, all.Count);
		Assert.Equal(ObjectReferenceTreeBuilder.MaxDepth, all.Max(v => v.Depth));
		Assert.DoesNotContain(all, v => v.Node.Kind == TreeNodeKind.Cycle);
	}

	[Fact]
	public void TheNodeAtTheCapIsNotExpandableAndSaysWhy() {
		var all = ExpandEverything(1, Chain);

		var deepest = Assert.Single(all, v => v.Depth == ObjectReferenceTreeBuilder.MaxDepth);
		Assert.False(deepest.Node.HasChildren);
		Assert.Contains(deepest.Node.Badges, b => b.Tone == TreeBadgeTone.Warn && b.Label.Contains("64", StringComparison.Ordinal));

		// It is a real reference that was not followed, not a cycle: the distinction matters, because
		// "this graph loops" and "we stopped counting" are different facts about the dump.
		Assert.Equal(TreeNodeKind.Field, deepest.Node.Kind);

		// One level up is still an ordinary expandable node -- the cap bites at exactly 64, not 63.
		var oneAbove = Assert.Single(all, v => v.Depth == ObjectReferenceTreeBuilder.MaxDepth - 1);
		Assert.True(oneAbove.Node.HasChildren);
		Assert.Empty(oneAbove.Node.Badges);
	}

	[Fact]
	public void AHandTypedPathAtTheCapYieldsNoChildren() {
		// The disclosure is not rendered at the cap, but a URL can still be typed. The cap must be
		// enforced by the builder, not only by the markup that usually hides it.
		string atCap = ObjectReferenceTreeBuilder.FormatPath(
			[.. Enumerable.Range(1, ObjectReferenceTreeBuilder.MaxDepth).Select(i => (ulong)i)]);

		Assert.Empty(ObjectReferenceTreeBuilder.GetChildren(atCap, Chain));
	}

	// ---- Field filtering and content ---------------------------------------------------------

	[Fact]
	public void NonReferenceAndNullReferenceFieldsAreSkipped() {
		Func<ulong, IReadOnlyList<ObjectField>> fields = _ => [
			new ObjectField { Name = "_count", TypeName = "System.Int32", IsReference = false, Address = 0, Value = "7" },
			new ObjectField { Name = "_unset", TypeName = "System.String", IsReference = true, Address = 0 },
			new ObjectField { Name = "_name", TypeName = "System.String", IsReference = true, Address = 0x2000 },
		];

		var node = Assert.Single(ObjectReferenceTreeBuilder.GetChildren("1000", fields));
		Assert.Equal("_name", node.Label);
		Assert.Equal(0x2000UL, node.Address);
	}

	[Fact]
	public void ANodeCarriesItsFieldNameTypeAndReferentAddress() {
		var node = Assert.Single(ObjectReferenceTreeBuilder.GetChildren("13A611F10", Graph((0x13A611F10, [0x13A612440]))));

		Assert.Equal("_f0", node.Label);
		Assert.Equal("Node @ 000000013A612440", node.Detail);
		Assert.Equal(0x13A612440UL, node.Address);
		Assert.Equal(TreeNodeKind.Field, node.Kind);
	}

	[Fact]
	public void AnObjectWithNoReferenceFieldsExpandsToNothing() {
		Assert.Empty(ObjectReferenceTreeBuilder.GetChildren("10", Graph()));
	}

	[Fact]
	public void TheBuilderReadsExactlyOneObjectPerExpansion() {
		// The lazy contract from §4.5: "one cheap single-object read, never a walk". If this ever
		// grows to two, the tree has started walking the graph on the server's time.
		var reads = new List<ulong>();
		var graph = Graph((0x10, [0x20, 0x30]), (0x20, [0x40]), (0x30, [0x50]));

		ObjectReferenceTreeBuilder.GetChildren("10-20", address => {
			reads.Add(address);
			return graph(address);
		});

		Assert.Equal(new ulong[] { 0x20 }, reads);
	}

	// ---- Node ids ----------------------------------------------------------------------------

	[Fact]
	public void ANodeIdIsThePathFromTheRoot() {
		var child = Assert.Single(ObjectReferenceTreeBuilder.GetChildren("10-20", Graph((0x20, [0x30]))));
		Assert.Equal("10-20-30", child.Id);
	}

	[Fact]
	public void PathsRoundTrip() {
		ulong[] path = [0x13A611F10, 0x20, 0xFFFFFFFFFFFFFFFF];
		Assert.True(ObjectReferenceTreeBuilder.TryParsePath(ObjectReferenceTreeBuilder.FormatPath(path), out var parsed));
		Assert.Equal(path, parsed);
	}

	[Theory]
	[InlineData("0x13A611F10")]                 // the form a user pastes from a debugger
	[InlineData("000000013A611F10")]            // the padded form the UI displays
	[InlineData("0x10-0x20")]                   // and a prefixed multi-segment path
	public void AHandTypedSeedIsAccepted(string seed) {
		Assert.True(ObjectReferenceTreeBuilder.TryParsePath(seed, out var path));
		Assert.NotEmpty(path);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("nonsense")]
	[InlineData("10-")]
	[InlineData("10--20")]
	[InlineData("10-zz")]
	public void AMalformedPathIsRejectedRatherThanPartiallyParsed(string? seed) {
		Assert.False(ObjectReferenceTreeBuilder.TryParsePath(seed, out var path));
		Assert.Empty(path);
	}

	[Fact]
	public void GetChildrenRejectsAMalformedNodeId() {
		Assert.Throws<ArgumentException>(() => ObjectReferenceTreeBuilder.GetChildren("not-an-address", Graph()));
	}
}