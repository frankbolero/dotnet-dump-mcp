using DotNetDump.Core.Trees;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// What <c>GcRootTree.cshtml</c> binds to: the whole retention tree (DATA_CONTRACT.md &#0167;4.3 --
/// this tree arrives complete from one analyzer call, so there is no per-node fetch and no
/// <see cref="TreeNodesModel"/> involved), plus the one link the truncation banner needs.
/// </summary>
/// <param name="Tree">The merged trie and, critically, its <see cref="GCRootTree.Outcome"/>. The
/// view switches on that; it never infers a conclusion from <see cref="GCRootTree.Roots"/> being
/// empty.</param>
/// <param name="ConclusiveHref">
/// This same request re-run with an unlimited traversal budget (<c>maxNodes=0</c>) -- the only way
/// to turn a truncated search into a conclusive one without a reverse-reference index
/// (CLI_DESIGN.md &#0167;11.2). Built by the route rather than the view so the view holds no URL grammar.
/// </param>
public sealed record GCRootTreeModel(GCRootTree Tree, string ConclusiveHref);