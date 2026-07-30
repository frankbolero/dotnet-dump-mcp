using DotNetDump.Core;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;
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
/// The dump this process was started against. A plain string in DI rather than something read off
/// the <see cref="AnalysisSession"/>: the header bar needs it on every page, and queueing behind a
/// heap walk to learn a path already known at startup would be absurd.
/// </summary>
public sealed record LoadedDump(string Path);

/// <summary>
/// The route families of SERVER.md &#0167;2. The HTML and JSON routes share one handler pipeline — the
/// same binder, the same <see cref="QueryParameters"/>, the same <see cref="FilterSpec"/> — and
/// diverge only at the final formatter. That is what keeps the API contract from drifting away
/// from what the UI actually exercises, and it means an agent can <c>curl</c> the running server
/// with the vocabulary it already knows from the CLI.
/// </summary>
public static class DumpRoutes {
	/// <summary>The view shown by <c>GET /</c>. Heap statistics are the usual first question of a dump.</summary>
	private const string DefaultView = "dumpheap";

	public static void MapDumpRoutes(this WebApplication app) {
		// Liveness, for the browser-open race in 'dndump serve' and for scripts.
		app.MapGet("/health", () => Results.Text("ok", "text/plain"));

		app.MapGet("/", (HttpContext http, LoadedDump dump, IAnalysisQueue queue, IFragmentRenderer renderer, CancellationToken ct) =>
			RenderShell(http, dump, queue, renderer, DefaultView, ct));

		app.MapGet("/views/{view}", (string view, HttpContext http, IAnalysisQueue queue, IFragmentRenderer renderer, CancellationToken ct) =>
			RenderFragment(http, queue, renderer, view, ct));

		app.MapGet("/api/{view}", (string view, HttpContext http, IAnalysisQueue queue, CancellationToken ct) =>
			RenderJson(http, queue, view, ct));
	}

	/// <summary>
	/// The outcome of rendering a fragment: markup, or the response that replaces it.
	/// </summary>
	/// <param name="CountSummary">
	/// The row count for the view header, which sits outside the swapped fragment. Carried out here
	/// rather than read back off the markup, because the header and the fragment are rendered by
	/// different templates and only the handler sees the <c>PagedResult</c> both derive from.
	/// </param>
	private readonly record struct Fragment(string? Html, IResult? Failure, string? CountSummary = null);

	private static async Task<IResult> RenderShell(
		HttpContext http, LoadedDump dump, IAnalysisQueue queue, IFragmentRenderer renderer, string viewName, CancellationToken ct) {

		var descriptor = ViewCatalog.Find(viewName);
		if (descriptor is null) {
			return Results.NotFound();
		}

		var fragment = await BuildFragment(http, queue, renderer, descriptor, ct);
		if (fragment.Html is null) {
			// A failed fragment is the whole page's failure. Rendering the shell around an error
			// would present a broken view as a working one.
			return fragment.Failure!;
		}

		string html = await renderer.RenderAsync(http, "/Views/Shell/Index.cshtml",
			new ShellModel(dump.Path, descriptor, ViewCatalog.All, new HtmlString(fragment.Html), fragment.CountSummary));

		return Html(html);
	}

	private static async Task<IResult> RenderFragment(
		HttpContext http, IAnalysisQueue queue, IFragmentRenderer renderer, string viewName, CancellationToken ct) {

		var descriptor = ViewCatalog.Find(viewName);
		if (descriptor is null) {
			return Results.NotFound();
		}

		var fragment = await BuildFragment(http, queue, renderer, descriptor, ct);
		return fragment.Html is null ? fragment.Failure! : Html(fragment.Html);
	}

	private static async Task<Fragment> BuildFragment(
		HttpContext http, IAnalysisQueue queue, IFragmentRenderer renderer, ViewDescriptor descriptor, CancellationToken ct) {

		if (!TryBind(http, descriptor, out var request, out var badRequest)) {
			return new Fragment(null, badRequest);
		}

		switch (descriptor.Name) {
			case "dumpheap": {
					var stats = await queue.Enqueue(
						(session, _) => session.Heap.GetHeapStatistics(request.Parameters), "walking heap", ct);
					var model = new ListModel<HeapStatItem>(descriptor, stats);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpHeap.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			default:
				return new Fragment(null, NotWiredYet(descriptor));
		}
	}

	private static async Task<IResult> RenderJson(HttpContext http, IAnalysisQueue queue, string viewName, CancellationToken ct) {
		var descriptor = ViewCatalog.Find(viewName);
		if (descriptor is null) {
			return Results.NotFound();
		}

		if (!TryBind(http, descriptor, out var request, out var badRequest)) {
			return badRequest;
		}

		switch (descriptor.Name) {
			case "dumpheap": {
					var stats = await queue.Enqueue(
						(session, _) => session.Heap.GetHeapStatistics(request.Parameters), "walking heap", ct);
					// The existing envelope, unchanged. DATA_CONTRACT.md §3.3's `state` block is not part
					// of Phase 2; it lands with the cache-state indicator in 6.5.
					return Results.Content(JsonFormatter.FormatHeapStatistics(stats), "application/json; charset=utf-8");
				}

			default:
				return NotWiredYet(descriptor);
		}
	}

	/// <summary>
	/// Binds the query string, turning every rejection into a <c>400</c> naming the reason. All
	/// three exceptions are client errors: the binder's own, the one
	/// <see cref="FilterSpec.EnsureSupported"/> raises for a field the view does not honor, and the
	/// <see cref="ArgumentException"/> a malformed type regex produces.
	/// </summary>
	private static bool TryBind(HttpContext http, ViewDescriptor descriptor, out ViewRequest request, out IResult badRequest) {
		try {
			request = ViewRequestBinder.Bind(http.Request.Query, descriptor);
			badRequest = Results.Empty;
			return true;
		} catch (Exception ex) when (ex is ViewRequestException or UnsupportedFilterException or ArgumentException) {
			request = null!;
			badRequest = Results.Text(ex.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
			return false;
		}
	}

	private static IResult Html(string markup) => Results.Content(markup, "text/html; charset=utf-8");

	/// <summary>
	/// A real view with no handler yet. <c>501</c> rather than <c>404</c>: the view exists and the
	/// navigation links to it, and saying "not found" about something the nav offers would be a lie.
	/// </summary>
	private static IResult NotWiredYet(ViewDescriptor descriptor) => Results.Text(
		$"'{descriptor.Name}' is in the view catalog but not yet wired to a handler (Phase 3).",
		"text/plain; charset=utf-8",
		statusCode: StatusCodes.Status501NotImplemented);
}