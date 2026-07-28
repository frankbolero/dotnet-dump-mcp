using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetDump.Core.Utilities;

/// <summary>A root reported by the runtime, and the object it points at.</summary>
public sealed record RootCandidate(
	ulong ObjectAddress,
	string Kind,
	ulong RootAddress,
	int? ManagedThreadId = null,
	uint? OSThreadId = null,
	bool IsPinned = false,
	bool IsInterior = false);

/// <summary>A chain from a root to the requested object. <see cref="Path"/> runs root-object first, target last.</summary>
public sealed record RootPath(RootCandidate Root, IReadOnlyList<ulong> Path);

/// <summary>
/// Result of a <see cref="RootPathFinder.FindPaths"/> search.
/// <para>
/// <see cref="Truncated"/> is the fix for the defect described in
/// <c>docs/GCROOT_TRUNCATION.md</c>: exhausting the traversal budget and genuinely finding no path
/// both used to collapse to an empty result, so callers reported "unrooted" when the search had
/// simply given up. <see cref="Paths"/> being empty no longer means "the object is unrooted" by
/// itself — check <see cref="Truncated"/> first.
/// </para>
/// <para>
/// Because <c>FindPaths</c> stops at the first pass that fails to produce a path (see the loop in
/// <see cref="RootPathFinder.FindPaths"/>), a non-empty <see cref="Paths"/> combined with
/// <see cref="Truncated"/> = <c>true</c> means: these paths are real and confirmed, but the search
/// gave up while looking for additional node-disjoint paths, so there may be more than shown.
/// </para>
/// </summary>
/// <param name="Paths">Node-disjoint paths found, in the order discovered.</param>
/// <param name="NodesVisited">Total nodes visited across every BFS pass that ran.</param>
/// <param name="Truncated">
/// <c>true</c> if the pass that ended the search (whether or not any earlier pass succeeded)
/// stopped because it exhausted <c>maxNodesVisited</c>, rather than because it exhausted the
/// reachable graph. <c>false</c> means the search was conclusive: either it found every path it was
/// asked for, or it proved no more node-disjoint paths exist.
/// </param>
public sealed record GCRootSearchResult(IReadOnlyList<RootPath> Paths, long NodesVisited, bool Truncated);

/// <summary>
/// Finds retention paths from GC roots to a target object.
/// <para>
/// This is the substance of <c>gcroot</c>. Matching roots that point *directly* at the target — which
/// is what the analyzer used to do — answers almost nothing: a user object is normally held through a
/// field, a collection, or a statics array, so the chain has length &gt; 1 essentially always.
/// </para>
/// <para>
/// Expressed over an abstract successor function so it can be tested against a hand-built graph
/// without a dump.
/// </para>
/// </summary>
public static class RootPathFinder {
	public const int DefaultMaxNodesVisited = 2_000_000;

	/// <summary>
	/// <para>
	/// Breadth-first search from every root at once, so the first path found for a given root is the
	/// shortest one. Asking for more than one path re-runs the search with the interior nodes of
	/// already-found paths banned, which yields node-disjoint chains — i.e. genuinely distinct
	/// reasons the object is alive, rather than N variations on one chain.
	/// </para>
	/// <para>
	/// <b>The budget is per-pass, not a total across all passes.</b> Each of up to <paramref
	/// name="maxPaths"/> BFS passes gets a fresh <paramref name="maxNodesVisited"/> allowance, so the
	/// worst-case total work is <c>maxPaths * maxNodesVisited</c> node visits (e.g. the defaults,
	/// 4 * 2,000,000 = 8,000,000). This was chosen deliberately over a shared/total budget: passes
	/// are independent BFS runs over (almost) the same graph with only a small, growing banned set
	/// between them, so how much of the graph pass 2 gets to see should not depend on how much of the
	/// graph pass 1 happened to need — a shared budget would make a later pass's outcome depend on an
	/// unrelated region of the graph explored by an earlier one, which is a harder result to reason
	/// about than "each attempt to prove one more path costs at most N visits". The documented
	/// trade-off is the multiplication by <paramref name="maxPaths"/>; callers who want a strict total
	/// cap should divide it themselves (<c>maxNodesVisited = totalBudget / maxPaths</c>).
	/// </para>
	/// <para>
	/// <paramref name="maxNodesVisited"/> &lt;= 0 means unlimited. This is not free: the per-pass
	/// <c>parent</c> map retains every visited node for the lifetime of that pass, so peak memory
	/// scales with nodes visited — roughly 40 bytes/node (~80 MB at the 2,000,000 default, ~4 GB at
	/// 100,000,000). Unlimited is the only way <see cref="GCRootSearchResult.Truncated"/> is
	/// guaranteed <c>false</c> at the end of the search, i.e. the only way to get a conclusive answer
	/// without a reverse-reference index (see CLI_DESIGN.md §11.2).
	/// </para>
	/// </summary>
	/// <param name="target">Object address to find paths to.</param>
	/// <param name="roots">Candidate roots. Enumerated once and reused across passes.</param>
	/// <param name="successors">Outbound references of an object.</param>
	/// <param name="maxPaths">Maximum number of node-disjoint paths to return.</param>
	/// <param name="maxNodesVisited">
	/// Traversal budget per pass, to bound cost on a large heap. <c>0</c> (or negative) means
	/// unlimited. Defaults to <see cref="DefaultMaxNodesVisited"/>.
	/// </param>
	public static GCRootSearchResult FindPaths(
		ulong target,
		IEnumerable<RootCandidate> roots,
		Func<ulong, IEnumerable<ulong>> successors,
		int maxPaths = 1,
		int maxNodesVisited = DefaultMaxNodesVisited) {

		if (successors is null) throw new ArgumentNullException(nameof(successors));
		if (maxPaths < 1) maxPaths = 1;

		var rootList = (roots ?? Enumerable.Empty<RootCandidate>()).ToList();
		if (rootList.Count == 0 || target == 0)
			return new GCRootSearchResult(Array.Empty<RootPath>(), NodesVisited: 0, Truncated: false);

		var results = new List<RootPath>();

		// Every node a returned path already used, excluding the target itself. The root object is
		// banned along with the interior nodes: leaving it available lets a later pass re-discover the
		// identical chain, which is what happens for a depth-1 path (it has no interior nodes at all).
		var banned = new HashSet<ulong>();

		long totalNodesVisited = 0;
		bool truncated = false;

		for (int pass = 0; pass < maxPaths; pass++) {
			var outcome = FindOnePath(target, rootList, successors, banned, maxNodesVisited);
			totalNodesVisited += outcome.NodesVisited;

			if (outcome.Path is null) {
				// The pass that ends the loop is the only one whose completion status matters: every
				// earlier pass, by definition, found a path (see below), so it was not truncated.
				truncated = outcome.Truncated;
				break;
			}

			results.Add(outcome.Path);

			for (int i = 0; i < outcome.Path.Path.Count - 1; i++)
				banned.Add(outcome.Path.Path[i]);
		}

		return new GCRootSearchResult(results, totalNodesVisited, truncated);
	}

	/// <summary>Outcome of a single BFS pass. <see cref="Path"/> and <see cref="Truncated"/> are
	/// never both meaningful at once: finding a path means the pass stopped before the budget was an
	/// issue, so a found path always carries <see cref="Truncated"/> = <c>false</c>.</summary>
	private readonly record struct PassOutcome(RootPath? Path, long NodesVisited, bool Truncated);

	private static PassOutcome FindOnePath(
		ulong target,
		List<RootCandidate> roots,
		Func<ulong, IEnumerable<ulong>> successors,
		HashSet<ulong> banned,
		int maxNodesVisited) {

		// parent[x] is the object we reached x from; the seed maps to the root that introduced it.
		var parent = new Dictionary<ulong, ulong>();
		var seedRoot = new Dictionary<ulong, RootCandidate>();
		var queue = new Queue<ulong>();

		foreach (var root in roots) {
			ulong seed = root.ObjectAddress;
			if (seed == 0 || parent.ContainsKey(seed))
				continue;
			if (banned.Contains(seed) && seed != target)
				continue;

			parent[seed] = 0;
			seedRoot[seed] = root;

			if (seed == target)
				return new PassOutcome(new RootPath(root, new[] { seed }), NodesVisited: 0, Truncated: false);

			queue.Enqueue(seed);
		}

		// <= 0 means unlimited (docs/GCROOT_TRUNCATION.md, CLI_DESIGN.md §11.4): treat it as "no cap"
		// rather than trying to special-case the loop below.
		long budget = maxNodesVisited > 0 ? maxNodesVisited : long.MaxValue;

		long visited = 0;
		while (queue.Count > 0) {
			if (++visited > budget)
				return new PassOutcome(null, NodesVisited: visited - 1, Truncated: true);

			ulong current = queue.Dequeue();

			IEnumerable<ulong> children;
			try {
				children = successors(current);
			} catch (Exception) {
				// A corrupt or unreadable object should not abort the whole search.
				continue;
			}

			foreach (ulong child in children) {
				if (child == 0 || parent.ContainsKey(child))
					continue;

				if (child == target) {
					parent[child] = current;
					return new PassOutcome(Reconstruct(target, parent, seedRoot), visited, Truncated: false);
				}

				if (banned.Contains(child))
					continue;

				parent[child] = current;
				queue.Enqueue(child);
			}
		}

		return new PassOutcome(null, visited, Truncated: false);
	}

	private static RootPath? Reconstruct(
		ulong target,
		Dictionary<ulong, ulong> parent,
		Dictionary<ulong, RootCandidate> seedRoot) {

		var path = new List<ulong>();
		ulong cursor = target;

		while (true) {
			path.Add(cursor);

			if (!parent.TryGetValue(cursor, out ulong next))
				return null;

			if (next == 0)
				break;

			cursor = next;

			// Defensive: a malformed parent map must not spin forever.
			if (path.Count > parent.Count + 1)
				return null;
		}

		if (!seedRoot.TryGetValue(cursor, out var root))
			return null;

		path.Reverse();
		return new RootPath(root, path);
	}
}