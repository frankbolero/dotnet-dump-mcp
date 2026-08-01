namespace DotNetDump.Core.Models;

/// <summary>What kind of thing a <see cref="TreeNode"/> denotes. Drives icon/badge choice and,
/// for <see cref="More"/> and <see cref="Cycle"/>, rendering that is not really "data" at all.</summary>
public enum TreeNodeKind {
	Namespace,
	Type,
	Object,
	Field,
	Thread,
	Frame,
	Root,
	More,
	Cycle
}

/// <summary>Visual weight for a <see cref="TreeBadge"/>. Maps to the <c>--dn-warn</c>/<c>--dn-danger</c>/
/// <c>--dn-ok</c>/<c>--dn-accent</c> token families in <c>dndump.css</c>; never a literal colour.</summary>
public enum TreeBadgeTone {
	Neutral,
	Info,
	Warn,
	Danger
}

/// <summary>A short label-plus-tone annotation on a <see cref="TreeNode"/> — "pinned", an exception
/// type name, "cycle", a thread id. Never load-bearing data on its own; always a restatement of
/// something the node's own fields already carry, made visually scannable.</summary>
public sealed record TreeBadge(string Label, TreeBadgeTone Tone);

/// <summary>
/// One node in any of the four DATA_CONTRACT.md &#0167;4 trees. The wire contract every tree shares —
/// four different backend shapes (namespace rollup, gcroot trie, thread/frame, object graph), one
/// node type — so the client-side rendering and the JSON API need exactly one shape to know about.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is opaque to the client: it is parsed only by the <see cref="TreeNodeKind"/>-
/// specific builder that produced it, and it encodes whatever that builder's own expansion needs
/// (the path taken to reach the node, for cycle detection; a paging offset, for <see cref="More"/>).
/// The client only ever echoes back an <see cref="Id"/> it was given — it never constructs one — so
/// each builder is free to choose its own encoding without coordinating with the others.
/// </remarks>
public sealed class TreeNode {
	public required string Id { get; init; }
	public required string Label { get; init; }
	public string? Detail { get; init; }
	public required TreeNodeKind Kind { get; init; }
	public required bool HasChildren { get; init; }
	public int? ChildCount { get; init; }
	public ulong? Address { get; init; }
	public IReadOnlyList<TreeBadge> Badges { get; init; } = [];
}