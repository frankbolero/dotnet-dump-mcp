using DotNetDump.Core.Models;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// A batch of sibling <see cref="TreeNode"/>s to render as <c>&lt;li&gt;</c> elements -- what
/// <c>_TreeNodes.cshtml</c> binds to. Shared by every genuinely lazy tree (namespace rollup,
/// object references): a tree computed whole up front (gcroot, thread/frames) renders its own
/// nested structure directly over <c>_TreeRow.cshtml</c> instead, since it has no "fetch this
/// node's children later" step for this model to represent.
/// </summary>
/// <param name="Tree">The <c>{tree}</c> route segment, baked into each lazy child's own
/// <c>hx-get="/trees/{Tree}/{id}"</c>.</param>
public sealed record TreeNodesModel(string Tree, IReadOnlyList<TreeNode> Nodes);