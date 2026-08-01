using DotNetDump.Core.Models;

namespace DotNetDump.Core.Trees;

/// <summary>
/// What a <c>gcroot</c> search actually established. Four states, not two: the whole point of
/// docs/GCROOT_TRUNCATION.md is that "no paths found" and "gave up looking" are different answers,
/// and that only one of them licenses the words "unrooted" or "eligible for collection".
/// </summary>
public enum GCRootOutcome {
	/// <summary>Paths found, and the search finished. Everything it was asked for, conclusively.</summary>
	Rooted,

	/// <summary>
	/// Paths found, but the search exhausted its node budget while looking for further node-disjoint
	/// paths. The chains shown are real; there may be more that were never reached.
	/// </summary>
	RootedPartial,

	/// <summary>
	/// The search completed, reached everything reachable, and found nothing. <b>The only outcome
	/// that may be described as unrooted.</b>
	/// </summary>
	Unrooted,

	/// <summary>
	/// No paths, because the search exhausted its budget rather than because it proved anything.
	/// This is the state docs/GCROOT_TRUNCATION.md exists to keep distinct from
	/// <see cref="Unrooted"/>: reporting it as "eligible for collection" is a confident false answer
	/// to the one question <c>gcroot</c> is asked.
	/// </summary>
	Inconclusive
}

/// <summary>
/// One node of the merged retention trie: a <see cref="TreeNode"/> plus the children it forks into.
/// </summary>
/// <remarks>
/// <see cref="TreeNode"/> is a flat wire type with no child collection of its own, by design -- the
/// lazy trees (namespace rollup, object navigator) fetch each level through
/// <c>hx-get="/trees/{tree}/{id}"</c> and so never hold a nested structure in memory. <c>gcroot</c>
/// arrives whole from one analyzer call (DATA_CONTRACT.md &#0167;4.3), so it needs somewhere to put the
/// nesting; this composes <see cref="TreeNode"/> rather than changing its shape.
/// </remarks>
public sealed record GCRootTreeNode(TreeNode Node, IReadOnlyList<GCRootTreeNode> Children);

/// <summary>
/// A built retention tree: the merged trie, plus the search-level facts a caller must not render
/// without.
/// </summary>
/// <param name="TargetAddress">The object the search was asked about.</param>
/// <param name="Roots">Top-level branches -- one per distinct root object across all merged paths.</param>
/// <param name="Outcome">What the search established. Render the conclusion from this, never from
/// <see cref="Roots"/> being empty.</param>
/// <param name="NodesVisited">Nodes visited across every BFS pass, for the truncation report.</param>
/// <param name="Truncated">Whether any pass gave up on its budget. Equivalent to
/// <see cref="Outcome"/> being <see cref="GCRootOutcome.RootedPartial"/> or
/// <see cref="GCRootOutcome.Inconclusive"/>, carried separately because it is also the
/// <c>state.truncated</c> envelope field of DATA_CONTRACT.md &#0167;3.3.</param>
public sealed record GCRootTree(
	ulong TargetAddress,
	IReadOnlyList<GCRootTreeNode> Roots,
	GCRootOutcome Outcome,
	long NodesVisited,
	bool Truncated);

/// <summary>
/// Builds DATA_CONTRACT.md &#0167;4.3's retention tree: merges <see cref="GCRootSearchInfo.Paths"/> --
/// each an ordered <see cref="GCRootPathInfo.Path"/> running root-object first, target last -- into
/// a trie keyed on <see cref="GCRootPathNode.Address"/>, so paths sharing a prefix collapse into one
/// branch that forks where they diverge.
/// </summary>
/// <remarks>
/// <para>
/// <b>On how much the merge currently collapses.</b> <c>RootPathFinder.FindPaths</c> bans every node
/// of a returned path except the target before the next pass, so the paths it produces are
/// node-disjoint and in practice share no prefix at all -- the trie then degenerates to one branch
/// per path, meeting at the target. The merge is still what the contract specifies and still the
/// right structure: it is correct either way, it is what makes the target a single node rather than
/// one repeated leaf per chain, and it does not silently mis-render if the finder ever returns paths
/// that do overlap (a different <c>maxPaths</c> strategy, or a caller assembling
/// <see cref="GCRootSearchInfo"/> itself).
/// </para>
/// <para>
/// <b>The outcome is computed here, once.</b> The four <see cref="GCRootOutcome"/> states exist so
/// that no renderer has to derive "is this object unrooted?" from an empty collection -- the exact
/// inference docs/GCROOT_TRUNCATION.md documents as the defect. A caller switches on
/// <see cref="GCRootTree.Outcome"/>; it never tests <see cref="GCRootTree.Roots"/> for emptiness.
/// </para>
/// </remarks>
public static class GCRootTreeBuilder {
	public static GCRootTree Build(GCRootSearchInfo search) {
		ArgumentNullException.ThrowIfNull(search);

		var forest = new Forest();
		int usablePaths = 0;

		foreach (var path in search.Paths) {
			if (path is null || path.Path.Count == 0) {
				continue;
			}

			usablePaths++;
			Insert(forest, path, search.TargetAddress);
		}

		var roots = forest.Order.Select(branch => ToTreeNode(branch, parentId: null)).ToList();

		return new GCRootTree(
			search.TargetAddress,
			roots,
			Classify(roots.Count > 0, reportedPaths: search.Paths.Count, usablePaths, search.Truncated),
			search.NodesVisited,
			search.Truncated);
	}

	/// <summary>
	/// The one place "unrooted" is decided.
	/// </summary>
	/// <remarks>
	/// The <paramref name="reportedPaths"/>/<paramref name="usablePaths"/> disagreement case --
	/// paths present but none carrying any nodes -- cannot arise from
	/// <c>HeapAnalyzer.GetGCRoots</c> as written, and is resolved towards
	/// <see cref="GCRootOutcome.Inconclusive"/> rather than <see cref="GCRootOutcome.Unrooted"/>
	/// anyway: an unexplained empty result is exactly the shape that must not be reported as proof
	/// of anything.
	/// </remarks>
	private static GCRootOutcome Classify(bool hasBranches, int reportedPaths, int usablePaths, bool truncated) {
		if (hasBranches) {
			return truncated ? GCRootOutcome.RootedPartial : GCRootOutcome.Rooted;
		}

		if (truncated || reportedPaths != usablePaths) {
			return GCRootOutcome.Inconclusive;
		}

		return GCRootOutcome.Unrooted;
	}

	/// <summary>Sibling branches at one trie level, indexed by address for the merge and kept in
	/// discovery order for rendering -- so the first path found is the first branch shown.</summary>
	private sealed class Forest {
		public readonly Dictionary<ulong, Branch> Index = new();
		public readonly List<Branch> Order = new();

		public Branch GetOrAdd(ulong address, GCRootPathNode node) {
			if (Index.TryGetValue(address, out var existing)) {
				return existing;
			}

			var branch = new Branch(address, node);
			Index[address] = branch;
			Order.Add(branch);
			return branch;
		}
	}

	private sealed class Branch(ulong address, GCRootPathNode node) {
		public readonly ulong Address = address;
		public GCRootPathNode Node = node;
		public bool IsRoot;
		public bool IsTarget;
		public readonly List<TreeBadge> Badges = new();
		public readonly Forest Children = new();

		/// <summary>Badges merge by label: two paths meeting at the same node contribute the same
		/// annotation twice, and a node wearing "pinned" twice is noise, not information.</summary>
		public void AddBadge(TreeBadge badge) {
			if (!Badges.Any(existing => string.Equals(existing.Label, badge.Label, StringComparison.Ordinal))) {
				Badges.Add(badge);
			}
		}
	}

	private static void Insert(Forest forest, GCRootPathInfo path, ulong targetAddress) {
		var level = forest;
		Branch? branch = null;

		for (int i = 0; i < path.Path.Count; i++) {
			var node = path.Path[i];
			branch = level.GetOrAdd(node.Address, node);

			// A later path can reach a node the first one only saw as unknown; keep whichever
			// representative actually resolved a type.
			if (branch.Node.TypeName is null && node.TypeName is not null) {
				branch.Node = node;
			}

			if (i == 0) {
				branch.IsRoot = true;
				foreach (var badge in RootBadges(path)) {
					branch.AddBadge(badge);
				}
			}

			level = branch.Children;
		}

		// The last node of every path is the object the search was asked about. Confirmed against
		// TargetAddress rather than assumed, so a caller-assembled GCRootSearchInfo whose chains end
		// somewhere else does not get a chain labelled "target" that is not one. TargetAddress = 0
		// means the caller did not say, and the path's own end is then the best available answer.
		if (branch is not null && (branch.Address == targetAddress || targetAddress == 0)) {
			branch.IsTarget = true;
		}
	}

	/// <summary>DATA_CONTRACT.md &#0167;4.3's root badges: kind, name, pinned, interior, and the owning
	/// thread when the root is a stack slot the runtime could attribute to one.</summary>
	private static IEnumerable<TreeBadge> RootBadges(GCRootPathInfo path) {
		if (!string.IsNullOrWhiteSpace(path.RootKind)) {
			yield return new TreeBadge(path.RootKind!, TreeBadgeTone.Info);
		}

		if (!string.IsNullOrWhiteSpace(path.RootName)) {
			yield return new TreeBadge(path.RootName!, TreeBadgeTone.Neutral);
		}

		if (path.ManagedThreadId is int managed) {
			yield return new TreeBadge($"thread {managed}", TreeBadgeTone.Neutral);
		} else if (path.OSThreadId is uint os) {
			yield return new TreeBadge($"OS thread {os:X}", TreeBadgeTone.Neutral);
		}

		// Pinned matters to a leak investigation on its own: a pinned root cannot be moved or
		// collected, which is often the whole answer.
		if (path.IsPinned) {
			yield return new TreeBadge("pinned", TreeBadgeTone.Warn);
		}

		if (path.IsInterior) {
			yield return new TreeBadge("interior", TreeBadgeTone.Warn);
		}
	}

	private static GCRootTreeNode ToTreeNode(Branch branch, string? parentId) {
		// Ids are the address chain from the root down to this node. This tree is computed whole up
		// front, so nothing ever fetches one back (DATA_CONTRACT.md §4.3) -- but TreeNode.Id is
		// required and must be unique within a response, and the same node address reached along two
		// different chains is a different node of this tree.
		string id = parentId is null
			? TreeFormat.Address(branch.Address)
			: parentId + "-" + TreeFormat.Address(branch.Address);

		var children = branch.Children.Order.Select(child => ToTreeNode(child, id)).ToList();

		var badges = new List<TreeBadge>(branch.Badges);
		if (branch.IsTarget) {
			badges.Add(new TreeBadge("target", TreeBadgeTone.Info));
		}

		var node = new TreeNode {
			Id = id,
			Label = branch.Node.TypeName ?? "<unknown type>",
			Detail = Detail(branch),
			Kind = branch.IsRoot ? TreeNodeKind.Root : TreeNodeKind.Object,
			HasChildren = children.Count > 0,
			ChildCount = children.Count == 0 ? null : children.Count,
			Address = branch.Address,
			Badges = badges,
		};

		return new GCRootTreeNode(node, children);
	}

	private static string Detail(Branch branch) =>
		branch.Node.Size == 0
			? TreeFormat.Address(branch.Address)
			: TreeFormat.Address(branch.Address) + " · " + TreeFormat.Size((long)branch.Node.Size);
}