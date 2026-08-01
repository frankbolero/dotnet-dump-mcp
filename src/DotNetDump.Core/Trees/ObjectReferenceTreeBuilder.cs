using System.Globalization;

using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Core.Trees;

/// <summary>
/// Builds DATA_CONTRACT.md &#0167;4.5's object reference navigator: one level of children at a time,
/// each level being the reference-typed fields of a single object. Every expansion is one cheap
/// single-object read (<c>HeapAnalyzer.GetObjectDetails</c>), never a walk, which is why this tree
/// stays fast on a cold cache when none of the other three do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cycle safety lives in the node id.</b> <see cref="TreeNode.Id"/> here is the whole chain of
/// addresses from the tree's root to the node, hex, joined by <see cref="PathSeparator"/> --
/// <c>13A611F10-13A612440-13A6118C0</c>. A hex address can never contain a <c>-</c>, so the encoding
/// is unambiguous and re-splittable, and it is exactly the "visited set on the path from the root"
/// &#0167;4.5 requires: expanding a node needs no server-side session state, because the request
/// carries its own history. Object graphs are routinely cyclic (parent/child back-references, event
/// handlers, caches keyed on their own values), so without this the tree is infinitely deep and a
/// user holding the expand key hangs the server.
/// </para>
/// <para>
/// The visited set is per-path, not global. Two siblings referencing the same object are both
/// expandable -- that is a shared leaf, not a cycle. Only revisiting an address already on the
/// current root-to-node path is a cycle, and it is emitted as
/// <see cref="TreeNodeKind.Cycle"/> with <see cref="TreeNode.HasChildren"/> = <see langword="false"/>
/// and a badge naming the ancestor it points back at.
/// </para>
/// <para>
/// <b>Expressed over an abstract field lookup</b>, for the same reason
/// <see cref="RootPathFinder"/> is expressed over an abstract successor function: cycle detection and
/// the depth cap are the two things most worth testing and the two things a healthy fixture dump will
/// never exercise on demand. A synthetic graph makes both deterministic. The builder calls
/// <c>referenceFields</c> exactly once per invocation -- on the last address of the path -- so a
/// caller that has already fetched that object (as the web route has, through its analysis queue) can
/// pass a closure over the result rather than a live reader.
/// </para>
/// </remarks>
public static class ObjectReferenceTreeBuilder {
	/// <summary>
	/// Maximum number of addresses on a node's path, counting the tree's root as depth 1. A node at
	/// this depth renders with <see cref="TreeNode.HasChildren"/> = <see langword="false"/> and a
	/// <see cref="TreeBadgeTone.Warn"/> badge instead of expanding further, so the deepest reachable
	/// node is depth 64 and the deepest expansion request is for the children of a depth-63 node.
	/// DATA_CONTRACT.md &#0167;4.5: "a practical guard against pathological linked lists, not a
	/// semantic limit" -- a 64-deep chain of distinct objects is a real graph, not a cycle, and
	/// cycle detection above would never stop it on its own.
	/// </summary>
	public const int MaxDepth = 64;

	/// <summary>Delimiter between addresses in a node id. Not a hex digit, so it cannot collide with
	/// any part of an address's own representation.</summary>
	public const char PathSeparator = '-';

	/// <summary>
	/// The children of the node named by <paramref name="nodeId"/> -- the reference fields of the last
	/// object on its path.
	/// </summary>
	/// <param name="nodeId">
	/// A path of hex addresses joined by <see cref="PathSeparator"/>. A single address (the tree's
	/// root, as it arrives from <c>/trees/object/{address}</c>) is the degenerate one-element case, so
	/// the first request and every later expansion take the identical route through this method.
	/// </param>
	/// <param name="referenceFields">
	/// The fields of one object, by address. Called exactly once, for <c>path[^1]</c>. Non-reference
	/// and null-reference fields may be included; they are filtered here.
	/// </param>
	/// <exception cref="ArgumentException"><paramref name="nodeId"/> is not a well-formed path.</exception>
	public static IReadOnlyList<TreeNode> GetChildren(
		string? nodeId, Func<ulong, IReadOnlyList<ObjectField>> referenceFields) {

		ArgumentNullException.ThrowIfNull(referenceFields);

		if (!TryParsePath(nodeId, out var path)) {
			throw new ArgumentException(
				$"'{nodeId}' is not a valid object-tree node id. Expected one or more hex addresses joined by '{PathSeparator}'.",
				nameof(nodeId));
		}

		// Defensive: a node at the cap is rendered without a disclosure, so no client should ask for
		// its children. A hand-typed URL still can, and must not be a way around the cap.
		if (path.Count >= MaxDepth) {
			return [];
		}

		var visited = new HashSet<ulong>(path);
		int childDepth = path.Count + 1;

		var fields = referenceFields(path[^1]);
		if (fields is null) {
			return [];
		}

		var nodes = new List<TreeNode>();
		foreach (var field in fields) {
			if (field is null || !field.IsReference || field.Address == 0) {
				continue;
			}

			nodes.Add(BuildChild(field, path, visited, childDepth));
		}

		return nodes;
	}

	private static TreeNode BuildChild(
		ObjectField field, IReadOnlyList<ulong> path, HashSet<ulong> visited, int childDepth) {

		string label = string.IsNullOrEmpty(field.Name) ? "<field>" : field.Name!;
		string detail = $"{field.TypeName ?? "<unknown>"} @ {TreeFormat.Address(field.Address)}";
		string id = FormatPath([.. path, field.Address]);

		if (visited.Contains(field.Address)) {
			return new TreeNode {
				Id = id,
				Label = label,
				Detail = detail,
				Kind = TreeNodeKind.Cycle,
				HasChildren = false,
				Address = field.Address,
				Badges = [new TreeBadge($"cycle → {TreeFormat.Address(field.Address)}", TreeBadgeTone.Warn)],
			};
		}

		if (childDepth >= MaxDepth) {
			return new TreeNode {
				Id = id,
				Label = label,
				Detail = detail,
				Kind = TreeNodeKind.Field,
				HasChildren = false,
				Address = field.Address,
				Badges = [new TreeBadge($"depth limit {MaxDepth}", TreeBadgeTone.Warn)],
			};
		}

		// HasChildren is optimistic, and deliberately so: knowing whether the referent has reference
		// fields of its own means reading it, which is the walk this tree exists to avoid. Expanding a
		// referent that turns out to have none yields an empty child list -- the cost of being wrong is
		// one empty disclosure, against one extra object read per rendered row for being right.
		return new TreeNode {
			Id = id,
			Label = label,
			Detail = detail,
			Kind = TreeNodeKind.Field,
			HasChildren = true,
			Address = field.Address,
		};
	}

	/// <summary>Splits a node id into its chain of addresses, root first. Accepts every form
	/// <see cref="AddressParser"/> does for each segment, so a hand-typed
	/// <c>/trees/object/0x13A611F10</c> works as well as one this builder emitted.</summary>
	public static bool TryParsePath(string? nodeId, out IReadOnlyList<ulong> path) {
		path = [];
		if (string.IsNullOrWhiteSpace(nodeId)) {
			return false;
		}

		var segments = nodeId.Split(PathSeparator);
		var parsed = new List<ulong>(segments.Length);
		foreach (string segment in segments) {
			if (!AddressParser.TryParse(segment, out ulong address)) {
				return false;
			}

			parsed.Add(address);
		}

		path = parsed;
		return true;
	}

	/// <summary>The node id for a root-to-node chain of addresses. Unpadded hex: a 64-deep path is
	/// already a long URL segment, and the padding <c>Display.Address</c> uses is a display concern
	/// that an opaque id does not share.</summary>
	public static string FormatPath(IReadOnlyList<ulong> path) =>
		string.Join(PathSeparator, path.Select(address => address.ToString("X", CultureInfo.InvariantCulture)));
}