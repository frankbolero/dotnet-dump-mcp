using DotNetDump.Core.Models;
using DotNetDump.Core.Trees;
using DotNetDump.Web.Analysis;
using DotNetDump.Web.Catalog;
using DotNetDump.Web.Rendering;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotNetDump.Web.Routes;

/// <summary>
/// <c>GET /trees/{tree}/{seed?}</c> (SERVER.md &#0167;2, DATA_CONTRACT.md &#0167;4): the four trees.
/// </summary>
/// <remarks>
/// <para>
/// A tree is not a view. DATA_CONTRACT.md &#0167;3.1's "one view per CLI command" does not hold for
/// it -- a tree is a rendering of an existing analyzer result, not a new one -- so trees get their
/// own tiny catalog (<see cref="TreeCatalog"/>) and their own route family here rather than growing
/// <see cref="ViewCatalog"/> with a third <see cref="ViewKind"/> and this file's switches with a
/// shape that does not fit them (a tree has no <c>FilterSpec</c>, no sort, no <c>PagedResult</c>).
/// </para>
/// <para>
/// The same full-page-vs-fragment split as <see cref="DumpRoutes"/>'s <c>WantsFragment</c> applies
/// here, for the identical reason: a browser following a nav link or restoring history wants the
/// whole shell, htmx expanding a node wants only the <c>&lt;li&gt;</c> subtree it asked for.
/// </para>
/// <para>
/// <c>seed</c> means different things per tree and is opaque outside the builder that owns it
/// (<see cref="Core.Models.TreeNode"/>'s own doc comment): absent, it is "give me the root(s)"
/// (<c>heap</c>, <c>threads</c>); present on the first request, it is the address the tree is
/// rooted at (<c>gcroot</c>, <c>object</c>); present on a later request, it is whatever
/// <see cref="Core.Models.TreeNode.Id"/> a previous response handed back for expansion.
/// </para>
/// <para>
/// Only <c>heap</c> and <c>threads</c> are nav-reachable. <c>object</c> needs an address a user does
/// not have until viewing an object, so it is linked to contextually from <c>dumpobj</c>'s own page
/// instead of appearing in <see cref="TreeCatalog"/>. <c>gcroot</c> also needs an address and keeps
/// its pre-existing <see cref="ViewCatalog"/> entry for <c>ViewCatalogCoverageTests</c> parity (it is
/// a real CLI command) -- <b>not yet wired here</b>. Phase 5.3 owns making
/// <c>/views/gcroot/{address}</c> redirect into <c>/trees/gcroot/{address}</c> rather than this route
/// family growing a second, duplicate implementation, and owns updating
/// <c>ViewRoutingTests.UnwiredView</c> (currently <c>"gcroot"</c>, by design, per that constant's own
/// doc comment) once the redirect lands and the name stops being an honest probe of "unwired".
/// </para>
/// </remarks>
public static class TreeRoutes {
	public static void MapTreeRoutes(this WebApplication app) {
		app.MapGet("/trees/{tree}/{seed?}",
			(string tree, string? seed, HttpContext http, LoadedDump dump, DumpInfoService info, IAnalysisQueue queue, IFragmentRenderer renderer, CancellationToken ct) =>
				RenderTree(http, dump, info, queue, renderer, tree, seed, ct));
	}

	/// <summary>Identical rule to <see cref="DumpRoutes"/>'s own <c>WantsFragment</c> -- duplicated
	/// rather than shared because the two are one-line predicates over <see cref="HttpContext"/> with
	/// no state, and a shared helper would only add an indirection between two files that otherwise
	/// have nothing else in common.</summary>
	private static bool WantsFragment(HttpContext http) =>
		http.Request.Headers.ContainsKey("HX-Request")
		&& !http.Request.Headers.ContainsKey("HX-History-Restore-Request");

	private static async Task<IResult> RenderTree(
		HttpContext http, LoadedDump dump, DumpInfoService info, IAnalysisQueue queue, IFragmentRenderer renderer,
		string tree, string? seed, CancellationToken ct) {

		string? fragment = await BuildTreeFragment(http, queue, renderer, tree, seed, ct);
		if (fragment is null) {
			return Results.Text(
				$"'{tree}' is not a known tree. Known trees: heap, threads, object, gcroot.",
				"text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
		}

		if (WantsFragment(http)) {
			return Html(fragment);
		}

		var descriptor = TreeCatalog.Find(tree);
		var infoResult = await info.GetAsync();
		string html = await renderer.RenderAsync(http, "/Views/Shell/Index.cshtml",
			new ShellModel(
				dump.Path, infoResult,
				Title: descriptor?.Title ?? tree,
				Command: descriptor?.Command ?? tree,
				Description: descriptor?.Description ?? "",
				CurrentView: null,
				CurrentTreeName: tree,
				Views: ViewCatalog.All,
				Trees: TreeCatalog.All,
				FragmentHtml: new HtmlString(fragment),
				CountSummary: null));

		return Html(html);
	}

	/// <summary>
	/// One case per tree name -- each of Phase 5's builder tasks adds exactly one, the same
	/// "trivial additive conflict, resolved by keeping both sides" shape Phase 3.3's fan-out already
	/// proved works when several worktrees each add a case to the same switch. <paramref name="seed"/>
	/// is <see langword="null"/> for a root-level request to <c>heap</c>/<c>threads</c>, an address
	/// for the first request to <c>gcroot</c>/<c>object</c>, and a previously-issued
	/// <see cref="Core.Models.TreeNode.Id"/> for every lazy-expand request.
	/// </summary>
	private static async Task<string?> BuildTreeFragment(
		HttpContext http, IAnalysisQueue queue, IFragmentRenderer renderer, string tree, string? seed, CancellationToken ct) {

		switch (tree) {
			case "heap": {
					// Limit = int.MaxValue: every row, not a page of one -- the same cached
					// heap-statistics entry dumpheap populates (the cache key excludes Limit, per
					// DATA_CONTRACT.md §2.1), so this costs nothing extra on a warm cache.
					var stats = await queue.Enqueue(
						(session, _) => session.Heap.GetHeapStatistics(new QueryParameters { Limit = int.MaxValue }),
						"walking heap", ct);
					var nodes = NamespaceRollupBuilder.GetChildren(stats.Items, seed);
					var model = new TreeNodesModel("heap", nodes);
					return await renderer.RenderAsync(
						http,
						string.IsNullOrEmpty(seed) ? "/Views/Fragments/HeapTree.cshtml" : "/Views/Fragments/_TreeNodes.cshtml",
						model);
				}

			// case "threads": ...       (5.2)
			// case "object": ...        (5.4)
			// case "gcroot": ...        (5.3)

			default:
				return null;
		}
	}

	private static IResult Html(string markup) => Results.Content(markup, "text/html; charset=utf-8");
}