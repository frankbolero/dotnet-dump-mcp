using DotNetDump.Core.Models;

namespace DotNetDump.Core.Trees;

/// <summary>
/// Builds DATA_CONTRACT.md &#0167;4.2's namespace/assembly rollup over an already-fetched
/// <c>PagedResult&lt;HeapStatItem&gt;</c> -- pure grouping, no dump access. Split each
/// <c>TypeName</c> on <c>.</c> outside <c>&lt;&gt;</c> and sum <see cref="HeapStatItem.Count"/>/
/// <see cref="HeapStatItem.TotalSize"/> up the resulting trie; generic arity stays part of the leaf
/// label, never becomes a namespace level of its own.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TreeNode.Id"/> for a namespace level is the dotted prefix reached so far ("" for the
/// root) -- reversible by re-splitting on <c>.</c>, which is safe because a namespace segment itself
/// never contains a literal <c>.</c> (that is exactly what the split rule guarantees). A leaf type's
/// own id is its full dotted name and is never navigated into: DATA_CONTRACT.md &#0167;4.2 is explicit
/// that generic arguments do not become tree levels, so a leaf has no children and its id's
/// re-splittability is moot.
/// </para>
/// <para>
/// The synthetic "N more" node's id is <c>{prefix}::more::{offset}</c> -- <c>::</c> cannot appear in
/// a .NET namespace segment, so it cannot collide with one. Expanding it does not nest a new level
/// under the placeholder; it returns the next page of *siblings*, which the caller splices in where
/// the placeholder was (the same self-replacing shape <c>Rendering/InfiniteScroll.cs</c>'s
/// infinite-scroll sentinel already uses for a table instead of a tree).
/// </para>
/// </remarks>
public static class NamespaceRollupBuilder {
	public const int PageSize = 50;
	private const string MoreMarker = "::more::";

	private sealed class Bucket {
		public string Segment = "";
		public long Count;
		public long TotalSize;
		public bool IsLeaf;
		public readonly Dictionary<string, Bucket> Children = new(StringComparer.Ordinal);
	}

	/// <summary>The children of the node named by <paramref name="nodeId"/> -- the root's, when
	/// <see langword="null"/> or empty.</summary>
	public static IReadOnlyList<TreeNode> GetChildren(IReadOnlyList<HeapStatItem> stats, string? nodeId) {
		string prefix = nodeId ?? "";
		int moreOffset = 0;

		int markerIndex = prefix.IndexOf(MoreMarker, StringComparison.Ordinal);
		if (markerIndex >= 0) {
			moreOffset = int.Parse(prefix[(markerIndex + MoreMarker.Length)..]);
			prefix = prefix[..markerIndex];
		}

		var root = BuildTrie(stats);
		var bucket = prefix.Length == 0 ? root : Navigate(root, prefix);
		return bucket is null ? [] : RenderChildren(bucket, prefix, moreOffset);
	}

	private static Bucket BuildTrie(IReadOnlyList<HeapStatItem> stats) {
		var root = new Bucket();
		foreach (var item in stats) {
			string typeName = item.TypeName ?? "<unknown>";
			var segments = SplitNamespace(typeName);
			var current = root;
			Accumulate(current, item);
			for (int i = 0; i < segments.Count; i++) {
				bool isLeaf = i == segments.Count - 1;
				string key = segments[i];
				if (!current.Children.TryGetValue(key, out var next)) {
					next = new Bucket { Segment = key, IsLeaf = isLeaf };
					current.Children[key] = next;
				}
				Accumulate(next, item);
				current = next;
			}
		}
		return root;
	}

	private static void Accumulate(Bucket bucket, HeapStatItem item) {
		bucket.Count += item.Count;
		bucket.TotalSize += item.TotalSize;
	}

	/// <summary>Splits on <c>.</c> only when not nested inside <c>&lt;...&gt;</c>, so a generic
	/// argument's own namespace (<c>Dictionary&lt;System.String,T&gt;</c>) never fragments the
	/// outer type's path. The final element is the leaf label and is never split further.</summary>
	internal static List<string> SplitNamespace(string typeName) {
		var segments = new List<string>();
		int depth = 0;
		int start = 0;
		for (int i = 0; i < typeName.Length; i++) {
			char c = typeName[i];
			if (c == '<') {
				depth++;
			} else if (c == '>') {
				depth = Math.Max(0, depth - 1);
			} else if (c == '.' && depth == 0) {
				segments.Add(typeName[start..i]);
				start = i + 1;
			}
		}
		segments.Add(typeName[start..]);
		return segments;
	}

	private static Bucket? Navigate(Bucket root, string prefix) {
		var current = root;
		foreach (var segment in prefix.Split('.')) {
			if (!current.Children.TryGetValue(segment, out var next)) {
				return null;
			}
			current = next;
		}
		return current;
	}

	private static List<TreeNode> RenderChildren(Bucket bucket, string prefix, int moreOffset) {
		var ordered = bucket.Children.Values
			.OrderByDescending(b => b.TotalSize)
			.ThenBy(b => b.Segment, StringComparer.Ordinal)
			.ToList();

		var page = ordered.Skip(moreOffset).Take(PageSize).ToList();
		var nodes = new List<TreeNode>(page.Count + 1);

		foreach (var child in page) {
			string childId = prefix.Length == 0 ? child.Segment : $"{prefix}.{child.Segment}";
			bool isLeafType = child.IsLeaf && child.Children.Count == 0;
			nodes.Add(new TreeNode {
				Id = childId,
				Label = child.Segment,
				Detail = TreeFormat.Size(child.TotalSize),
				Kind = isLeafType ? TreeNodeKind.Type : TreeNodeKind.Namespace,
				HasChildren = child.Children.Count > 0,
				ChildCount = child.Children.Count == 0 ? null : child.Children.Count,
				Badges = [new TreeBadge($"{child.Count:N0}", TreeBadgeTone.Neutral)],
			});
		}

		int remaining = ordered.Count - (moreOffset + page.Count);
		if (remaining > 0) {
			long remainingSize = ordered.Skip(moreOffset + page.Count).Sum(b => b.TotalSize);
			nodes.Add(new TreeNode {
				Id = $"{prefix}{MoreMarker}{moreOffset + PageSize}",
				Label = $"{remaining:N0} more",
				Detail = TreeFormat.Size(remainingSize),
				Kind = TreeNodeKind.More,
				HasChildren = false,
			});
		}

		return nodes;
	}
}