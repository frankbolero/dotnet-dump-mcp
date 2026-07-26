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
	/// Breadth-first search from every root at once, so the first path found for a given root is the
	/// shortest one. Asking for more than one path re-runs the search with the interior nodes of
	/// already-found paths banned, which yields node-disjoint chains — i.e. genuinely distinct
	/// reasons the object is alive, rather than N variations on one chain.
	/// </summary>
	/// <param name="target">Object address to find paths to.</param>
	/// <param name="roots">Candidate roots. Enumerated once and reused across passes.</param>
	/// <param name="successors">Outbound references of an object.</param>
	/// <param name="maxPaths">Maximum number of node-disjoint paths to return.</param>
	/// <param name="maxNodesVisited">Traversal budget per pass, to bound cost on a large heap.</param>
	public static IReadOnlyList<RootPath> FindPaths(
		ulong target,
		IEnumerable<RootCandidate> roots,
		Func<ulong, IEnumerable<ulong>> successors,
		int maxPaths = 1,
		int maxNodesVisited = DefaultMaxNodesVisited) {

		if (successors is null) throw new ArgumentNullException(nameof(successors));
		if (maxPaths < 1) maxPaths = 1;

		var rootList = (roots ?? Enumerable.Empty<RootCandidate>()).ToList();
		if (rootList.Count == 0 || target == 0)
			return Array.Empty<RootPath>();

		var results = new List<RootPath>();

		// Every node a returned path already used, excluding the target itself. The root object is
		// banned along with the interior nodes: leaving it available lets a later pass re-discover the
		// identical chain, which is what happens for a depth-1 path (it has no interior nodes at all).
		var banned = new HashSet<ulong>();

		for (int pass = 0; pass < maxPaths; pass++) {
			var found = FindOnePath(target, rootList, successors, banned, maxNodesVisited);
			if (found is null)
				break;

			results.Add(found);

			for (int i = 0; i < found.Path.Count - 1; i++)
				banned.Add(found.Path[i]);
		}

		return results;
	}

	private static RootPath? FindOnePath(
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
				return new RootPath(root, new[] { seed });

			queue.Enqueue(seed);
		}

		int visited = 0;
		while (queue.Count > 0) {
			if (++visited > maxNodesVisited)
				return null;

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
					return Reconstruct(target, parent, seedRoot);
				}

				if (banned.Contains(child))
					continue;

				parent[child] = current;
				queue.Enqueue(child);
			}
		}

		return null;
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