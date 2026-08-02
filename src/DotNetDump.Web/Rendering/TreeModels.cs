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

/// <summary>
/// The object reference navigator's top-level fragment (DATA_CONTRACT.md &#0167;4.5). Everything below
/// the breadcrumb is an ordinary lazy <see cref="TreeNodesModel"/>; the extra field here is the path
/// itself, because &#0167;4.5's history model is the breadcrumb -- the node id already encodes the
/// chain of addresses, so the address bar and the back button are the navigation history and there is
/// no client-side state machine to keep in step.
/// </summary>
/// <param name="Path">Addresses from the tree's original root to the object being shown, root first.
/// The last entry is the current object; every earlier one links back to itself as a root.</param>
/// <param name="Nodes">The current object's reference fields.</param>
public sealed record ObjectTreeModel(IReadOnlyList<ulong> Path, TreeNodesModel Nodes) {
	/// <summary>
	/// The breadcrumb, ready to render. Built here rather than in the template for the same reason
	/// every other fragment keeps formatting out of its markup: an ancestor's href is the *prefix* of
	/// the path up to it -- which is a fact about how <see cref="Core.Trees.ObjectReferenceTreeBuilder"/>
	/// encodes ids, not about how a row looks.
	/// </summary>
	public IReadOnlyList<ObjectCrumb> Crumbs =>
		[.. Path.Select((address, index) => new ObjectCrumb(
			Href: $"/trees/object/{Core.Trees.ObjectReferenceTreeBuilder.FormatPath([.. Path.Take(index + 1)])}",
			Label: Display.Address(address),
			IsCurrent: index == Path.Count - 1))];
}

/// <param name="Href">Where clicking it re-roots the navigator -- a plain link, so browser history
/// and the back button do the work with no client-side state.</param>
/// <param name="IsCurrent">The last crumb: the object whose fields are listed below.</param>
public sealed record ObjectCrumb(string Href, string Label, bool IsCurrent);