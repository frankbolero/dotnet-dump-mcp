using DotNetDump.Core.Models;
using DotNetDump.Core.Trees;
using DotNetDump.Core.Utilities;
using DotNetDump.Web.Analysis;
using DotNetDump.Web.Binding;
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
/// a real CLI command); <c>/views/gcroot/{address}</c> redirects into <c>/trees/gcroot/{address}</c>
/// rather than duplicating the tree there.
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

		// gcroot is the one tree whose seed is required and is always an address: it is computed whole
		// up front (DATA_CONTRACT.md §4.3), so it never issues a TreeNode.Id for a later expansion and
		// therefore never receives a seed that is not an address. Validated here rather than inside
		// BuildTreeFragment because a missing or malformed address is a 400 and that switch's shared
		// signature only distinguishes "rendered" from "no such tree" (404) -- same rule, same message
		// shape, as DumpRoutes.TryRequireAddress.
		if (IsGCRoot(tree) && !TryValidateGCRootRequest(http, seed, out var badGCRootRequest)) {
			return badGCRootRequest;
		}

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

		// A tree with no TreeCatalog entry borrows the header text and the nav highlight of the
		// ViewCatalog entry of the same name, when there is one. Today that is exactly gcroot: it
		// needs an address so it is not nav-reachable and has no TreeDescriptor, but it keeps its
		// ViewCatalog entry (a real CLI command), and /views/gcroot/{address} now redirects here --
		// so this *is* the page that nav entry leads to, and it should say so rather than heading
		// itself with the bare route segment.
		var view = descriptor is null ? ViewCatalog.Find(tree) : null;

		var infoResult = await info.GetAsync();
		string html = await renderer.RenderAsync(http, "/Views/Shell/Index.cshtml",
			new ShellModel(
				dump.Path, infoResult,
				Title: descriptor?.Title ?? view?.Title ?? tree,
				Command: descriptor?.Command ?? view?.Command ?? tree,
				Description: descriptor?.Description ?? view?.Description ?? "",
				CurrentView: view,
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

			case "threads": {
					// DATA_CONTRACT.md §4.4: "fully computed up front" -- two independent Enqueue
					// calls, no per-node lazy fetch. Ascending order and every thread in one page
					// (Limit = int.MaxValue) so ThreadFramesTreeBuilder sees the whole thread set
					// rather than one page of it; the cache key excludes both (§2.1), so this costs
					// nothing extra on a warm cache.
					var allThreads = new QueryParameters { Limit = int.MaxValue, SortDirection = SortDirection.Asc };
					var threadsResult = await queue.Enqueue(
						(session, _) => session.Threads.GetThreads(allThreads),
						"enumerating threads", ct);
					var stacksResult = await queue.Enqueue(
						(session, _) => session.Threads.GetDetailedStacks(allThreads),
						"walking thread stacks", ct);
					var entries = ThreadFramesTreeBuilder.BuildRoots(threadsResult.Items, stacksResult.Items);
					var model = new ThreadsTreeModel(entries);
					return await renderer.RenderAsync(http, "/Views/Fragments/ThreadsTree.cshtml", model);
				}

			case "gcroot": {
					// Both parses are guaranteed to succeed: RenderTree ran the identical validation
					// and answered 400 before this method was called.
					AddressParser.TryParse(seed, out ulong target);
					GCRootBudget.TryRead(http.Request.Query, out int? maxNodes, out _);

					var search = await queue.Enqueue(
						(session, _) => session.Heap.GetGCRoots(target, new QueryParameters(), maxPaths: GCRootBudget.DefaultMaxPaths, maxNodesVisited: maxNodes),
						"resolving roots", ct);

					var model = new GCRootTreeModel(
						GCRootTreeBuilder.Build(search),
						ConclusiveHref: $"/trees/gcroot/{target:X}?{GCRootBudget.Parameter}=0");

					// Fully nested up front, not through _TreeNodes.cshtml: one analyzer call returned
					// the whole tree, so there is no lazy level for that partial's hx-get to fetch
					// (DATA_CONTRACT.md §4.3, and _TreeNodes.cshtml's own doc comment).
					return await renderer.RenderAsync(http, "/Views/Fragments/GcRootTree.cshtml", model);
				}

			// case "object": ...        (5.4)

			default:
				return null;
		}
	}

	private static bool IsGCRoot(string tree) => string.Equals(tree, "gcroot", StringComparison.Ordinal);

	/// <summary>The <c>400</c>s for <c>/trees/gcroot</c>: a required address it did not get, and a
	/// budget override it cannot act on.</summary>
	private static bool TryValidateGCRootRequest(HttpContext http, string? seed, out IResult badRequest) {
		if (!AddressParser.TryParse(seed, out _)) {
			badRequest = Results.Text(
				"'gcroot' requires an address, e.g. /trees/gcroot/7FF6A1B02000 (hex, optionally 0x-prefixed). "
				+ "It answers 'what is keeping this object alive?', so there is nothing to show without one.",
				"text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
			return false;
		}

		if (!GCRootBudget.TryRead(http.Request.Query, out _, out string error)) {
			badRequest = Results.Text(error, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
			return false;
		}

		badRequest = Results.Empty;
		return true;
	}

	private static IResult Html(string markup) => Results.Content(markup, "text/html; charset=utf-8");
}
