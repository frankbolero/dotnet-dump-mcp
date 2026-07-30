using DotNetDump.Core;
using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;
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

		app.MapGet("/", (HttpContext http, LoadedDump dump, DumpInfoService info, IAnalysisQueue queue, IFragmentRenderer renderer, CancellationToken ct) =>
			RenderShell(http, dump, info, queue, renderer, ViewCatalog.Find(DefaultView)!, address: null, ct));

		// {address?} is a path segment, not a query parameter -- IMPLEMENTATION_PLAN.md's Phase 3
		// notes settle this: an address identifies which record a detail view *is*, unlike the
		// filter/sort/page state in the query string (DATA_CONTRACT.md §3.2), and the trees already
		// route addresses this way (gcroot/{address}, object/{address}).
		app.MapGet("/views/{view}/{address?}", (string view, string? address, HttpContext http, LoadedDump dump, DumpInfoService info, IAnalysisQueue queue, IFragmentRenderer renderer, CancellationToken ct) =>
			RenderView(http, dump, info, queue, renderer, view, address, ct));

		app.MapGet("/api/{view}/{address?}", (string view, string? address, HttpContext http, DumpInfoService info, IAnalysisQueue queue, CancellationToken ct) =>
			RenderJson(http, info, queue, view, address, ct));
	}

	/// <summary>
	/// The outcome of rendering a fragment: markup, or the response that replaces it.
	/// </summary>
	/// <param name="CountSummary">
	/// The row count for the view header, which sits outside the swapped fragment. Carried out here
	/// rather than read back off the markup, because the header and the fragment are rendered by
	/// different templates and only the handler sees the <c>PagedResult</c> both derive from.
	/// </param>
	/// <param name="NotImplemented">
	/// The view exists in the catalog but has no handler yet. Distinguished from an ordinary failure
	/// because it is the only one that still deserves the whole page around it: the navigation must
	/// stay usable so a reader can leave, which a bare 501 body does not allow.
	/// </param>
	private readonly record struct Fragment(
		string? Html, IResult? Failure, string? CountSummary = null, bool NotImplemented = false);

	private static async Task<IResult> RenderShell(
		HttpContext http, LoadedDump dump, DumpInfoService info, IAnalysisQueue queue, IFragmentRenderer renderer,
		ViewDescriptor descriptor, string? address, CancellationToken ct) {

		// Started before the fragment is awaited so the memoized info call and the fragment's own
		// Enqueue (if any) both sit on the analysis queue as early as possible; on a cold dump the
		// fragment's heap walk still dominates, but nothing here waits on it that doesn't have to.
		var infoTask = info.GetAsync();

		var fragment = await BuildFragment(http, info, queue, renderer, descriptor, address, ct);

		if (fragment.Html is null && !fragment.NotImplemented) {
			// A failed fragment is the whole page's failure. Rendering the shell around a rejected
			// query string would present a broken view as a working one.
			return fragment.Failure!;
		}

		string body = fragment.Html
			?? await renderer.RenderAsync(http, "/Views/Fragments/NotImplemented.cshtml", descriptor);

		string html = await renderer.RenderAsync(http, "/Views/Shell/Index.cshtml",
			new ShellModel(dump.Path, await infoTask, descriptor, ViewCatalog.All, new HtmlString(body), fragment.CountSummary));

		// The status stays 501 even though a full page comes back. The page is navigation plus an
		// honest explanation, not the view that was asked for.
		return fragment.NotImplemented
			? Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status501NotImplemented)
			: Html(html);
	}

	/// <summary>
	/// Whether this request wants the bare fragment rather than a whole page.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>/views/{view}</c> is two things at once, and the <c>HX-Request</c> header is what tells
	/// them apart. htmx swapping a view into an existing page wants the fragment; a browser
	/// following a link, restoring history, or opening a pasted URL wants the whole page — nav,
	/// stylesheet and all. Serving the fragment to both is how the navigation came to render an
	/// unstyled table with no shell.
	/// </para>
	/// <para>
	/// This is not only cosmetic. DATA_CONTRACT.md &#0167;3.2 makes the query string the view state and
	/// <c>hx-push-url</c> writes it to the address bar, so <c>/views/dumpheap?type=Http</c> has to be
	/// a page a user can paste, bookmark and share — which is exactly what Phase 4.6's round-trip
	/// criterion requires.
	/// </para>
	/// <para>
	/// <c>HX-History-Restore-Request</c> is the exception that proves it: htmx sends it when
	/// restoring a page from its own history cache, and on that request it wants the full document
	/// back, not a fragment.
	/// </para>
	/// </remarks>
	private static bool WantsFragment(HttpContext http) =>
		http.Request.Headers.ContainsKey("HX-Request")
		&& !http.Request.Headers.ContainsKey("HX-History-Restore-Request");

	private static async Task<IResult> RenderView(
		HttpContext http, LoadedDump dump, DumpInfoService info, IAnalysisQueue queue, IFragmentRenderer renderer,
		string viewName, string? address, CancellationToken ct) {

		var descriptor = ViewCatalog.Find(viewName);
		if (descriptor is null) {
			return Results.NotFound();
		}

		if (WantsFragment(http)) {
			var swap = await BuildFragment(http, info, queue, renderer, descriptor, address, ct);
			return swap.Html is null ? swap.Failure! : Html(swap.Html);
		}

		return await RenderShell(http, dump, info, queue, renderer, descriptor, address, ct);
	}

	private static async Task<Fragment> BuildFragment(
		HttpContext http, DumpInfoService info, IAnalysisQueue queue, IFragmentRenderer renderer, ViewDescriptor descriptor,
		string? address, CancellationToken ct) {

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

			case "info": {
					// Reuses the header bar's own memoized call rather than a fresh Enqueue -- the
					// dump this process was started against cannot answer differently the second time.
					var dumpInfo = await info.GetAsync();
					var model = new DetailModel<DumpInfo>(descriptor, dumpInfo);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/Info.cshtml", model);
					return new Fragment(html, null);
				}

			case "dumpobj": {
					if (!TryRequireAddress(address, descriptor, out ulong objectAddress, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var details = await queue.Enqueue(
						(session, _) => session.Heap.GetObjectDetails(objectAddress), "reading object", ct);
					var model = new DetailModel<ObjectDetails>(descriptor, details);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpObj.cshtml", model);
					return new Fragment(html, null);
				}

			case "gchandles": {
					var handles = await queue.Enqueue(
						(session, _) => session.Heap.GetGCHandles(request.Parameters), "enumerating GC handles", ct);
					var model = new ListModel<GCHandleInfo>(descriptor, handles);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/GCHandles.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "clrmodules": {
					var modules = await queue.Enqueue(
						(session, _) => session.Modules.GetModules(request.Parameters), "listing modules", ct);
					var model = new ListModel<ModuleInfo>(descriptor, modules);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/ClrModules.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "clrthreads": {
					var threads = await queue.Enqueue(
						(session, _) => session.Threads.GetThreads(request.Parameters), "enumerating threads", ct);
					var model = new ListModel<ThreadInfo>(descriptor, threads);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/ClrThreads.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "listobj": {
					var objects = await queue.Enqueue(
						(session, _) => session.Heap.GetObjects(request.Parameters, typeFilter: null), "walking objects", ct);
					var model = new ListModel<HeapObjectItem>(descriptor, objects);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/ListObj.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "threadstate": {
					var states = await queue.Enqueue(
						(session, _) => session.Threads.GetThreadStates(request.Parameters), "enumerating thread states", ct);
					var model = new ListModel<ThreadStateInfo>(descriptor, states);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/ThreadState.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "printexception": {
					var exceptions = await queue.Enqueue(
						(session, _) => session.Threads.GetThreadExceptions(request.Parameters), "finding exceptions", ct);
					var model = new ListModel<ThreadExceptionInfo>(descriptor, exceptions);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/PrintException.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "syncblk": {
					var blocks = await queue.Enqueue(
						(session, _) => session.Heap.GetSyncBlocks(request.Parameters), "enumerating sync blocks", ct);
					var model = new ListModel<SyncBlockInfo>(descriptor, blocks);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/SyncBlk.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "verifyobj": {
					if (!TryRequireAddress(address, descriptor, out ulong objectAddress, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var corruptions = await queue.Enqueue(
						(session, _) => session.Heap.VerifyObject(objectAddress).ToList(), "verifying object", ct);
					var model = new DetailModel<ObjectVerificationModel>(descriptor, new ObjectVerificationModel(objectAddress, corruptions));
					string html = await renderer.RenderAsync(http, "/Views/Fragments/VerifyObj.cshtml", model);
					return new Fragment(html, null);
				}

			case "dumpclass": {
					if (!TryRequireAddress(address, descriptor, out ulong eeClassAddress, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var classInfo = await queue.Enqueue(
						(session, _) => session.Metadata.GetClass(eeClassAddress), "reading class", ct);
					var model = new DetailModel<ClassInfo>(descriptor, classInfo);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpClass.cshtml", model);
					return new Fragment(html, null);
				}

			case "dumpmodule": {
					if (!TryRequireAddress(address, descriptor, out ulong moduleAddress, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var details = await queue.Enqueue(
						(session, _) => session.Modules.GetModuleDetails(moduleAddress), "reading module", ct);
					var model = new DetailModel<ModuleDetails>(descriptor, details);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpModule.cshtml", model);
					return new Fragment(html, null);
				}

			case "dumpassembly": {
					if (!TryRequireAddress(address, descriptor, out ulong assemblyAddress, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var details = await queue.Enqueue(
						(session, _) => session.Modules.GetAssemblyDetails(assemblyAddress), "reading assembly", ct);
					var model = new DetailModel<AssemblyDetails>(descriptor, details);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpAssembly.cshtml", model);
					return new Fragment(html, null);
				}

			case "eeheap": {
					var heapInfo = await queue.Enqueue(
						(session, _) => session.Heap.GetHeapSegments(), "reading heap segments", ct);
					var model = new DetailModel<HeapSummaryInfo>(descriptor, heapInfo);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/EeHeap.cshtml", model);
					return new Fragment(html, null);
				}

			case "threadpool": {
					var threadPoolInfo = await queue.Enqueue(
						(session, _) => session.Threads.GetThreadPoolInfo(), "reading thread pool", ct);
					var model = new DetailModel<ThreadPoolInfo>(descriptor, threadPoolInfo);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/ThreadPool.cshtml", model);
					return new Fragment(html, null);
				}

			case "clrstack": {
					var groups = await queue.Enqueue(
						(session, _) => session.Threads.GetStackTraceGroups(maxFrames: 20).ToList(), "enumerating stacks", ct);
					var model = new DetailModel<List<StackGroup>>(descriptor, groups);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/StackGroups.cshtml", model);
					return new Fragment(html, null);
				}

			case "eestack": {
					var groups = await queue.Enqueue(
						(session, _) => session.Threads.GetStackTraceGroups(maxFrames: 30).ToList(), "enumerating stacks", ct);
					var model = new DetailModel<List<StackGroup>>(descriptor, groups);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/StackGroups.cshtml", model);
					return new Fragment(html, null);
				}

			case "dumpmt": {
					if (!TryRequireAddress(address, descriptor, out ulong methodTableAddress, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var methodTableInfo = await queue.Enqueue(
						(session, _) => session.Metadata.GetMethodTable(methodTableAddress), "reading MethodTable", ct);
					var model = new DetailModel<MethodTableInfo>(descriptor, methodTableInfo);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpMt.cshtml", model);
					return new Fragment(html, null);
				}

			case "dumpmd": {
					if (!TryRequireAddress(address, descriptor, out ulong methodDescAddress, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var methodDescInfo = await queue.Enqueue(
						(session, _) => session.Metadata.GetMethodDesc(methodDescAddress), "reading MethodDesc", ct);
					var model = new DetailModel<MethodDescInfo>(descriptor, methodDescInfo);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpMd.cshtml", model);
					return new Fragment(html, null);
				}

			case "ip2md": {
					if (!TryRequireAddress(address, descriptor, out ulong instructionPointer, out var badAddress)) {
						return new Fragment(null, badAddress);
					}

					var methodDescInfo = await queue.Enqueue(
						(session, _) => session.Modules.GetMethodByIP(instructionPointer), "resolving instruction pointer", ct);
					var model = new DetailModel<MethodDescInfo>(descriptor, methodDescInfo);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpMd.cshtml", model);
					return new Fragment(html, null);
				}

			case "verifyheap": {
					var corruptions = await queue.Enqueue(
						(session, _) => session.Heap.VerifyHeap(request.Parameters), "verifying heap", ct);
					var model = new ListModel<HeapCorruptionInfo>(descriptor, corruptions);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/VerifyHeap.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			case "dumpstack": {
					var stacks = await queue.Enqueue(
						(session, _) => session.Threads.GetDetailedStacks(request.Parameters), "walking thread stacks", ct);
					var model = new ListModel<ThreadStackInfo>(descriptor, stacks);
					string html = await renderer.RenderAsync(http, "/Views/Fragments/DumpStack.cshtml", model);
					return new Fragment(html, null, model.CountSummary);
				}

			default:
				return new Fragment(null, NotWiredYet(descriptor), NotImplemented: true);
		}
	}

	private static async Task<IResult> RenderJson(HttpContext http, DumpInfoService info, IAnalysisQueue queue, string viewName, string? address, CancellationToken ct) {
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

			case "info": {
					var dumpInfo = await info.GetAsync();
					return Results.Content(JsonFormatter.FormatInfo(dumpInfo), "application/json; charset=utf-8");
				}

			case "dumpobj": {
					if (!TryRequireAddress(address, descriptor, out ulong objectAddress, out var badAddress)) {
						return badAddress;
					}

					var details = await queue.Enqueue(
						(session, _) => session.Heap.GetObjectDetails(objectAddress), "reading object", ct);
					return Results.Content(JsonFormatter.FormatObjectDetails(details), "application/json; charset=utf-8");
				}

			case "gchandles": {
					var handles = await queue.Enqueue(
						(session, _) => session.Heap.GetGCHandles(request.Parameters), "enumerating GC handles", ct);
					return Results.Content(JsonFormatter.FormatGCHandles(handles), "application/json; charset=utf-8");
				}

			case "clrmodules": {
					var modules = await queue.Enqueue(
						(session, _) => session.Modules.GetModules(request.Parameters), "listing modules", ct);
					return Results.Content(JsonFormatter.FormatModules(modules), "application/json; charset=utf-8");
				}

			case "clrthreads": {
					var threads = await queue.Enqueue(
						(session, _) => session.Threads.GetThreads(request.Parameters), "enumerating threads", ct);
					return Results.Content(JsonFormatter.FormatThreads(threads), "application/json; charset=utf-8");
				}

			case "listobj": {
					var objects = await queue.Enqueue(
						(session, _) => session.Heap.GetObjects(request.Parameters, typeFilter: null), "walking objects", ct);
					return Results.Content(JsonFormatter.FormatHeapObjects(objects), "application/json; charset=utf-8");
				}

			case "threadstate": {
					var states = await queue.Enqueue(
						(session, _) => session.Threads.GetThreadStates(request.Parameters), "enumerating thread states", ct);
					return Results.Content(JsonFormatter.FormatThreadStates(states), "application/json; charset=utf-8");
				}

			case "printexception": {
					var exceptions = await queue.Enqueue(
						(session, _) => session.Threads.GetThreadExceptions(request.Parameters), "finding exceptions", ct);
					return Results.Content(JsonFormatter.FormatThreadExceptions(exceptions), "application/json; charset=utf-8");
				}

			case "syncblk": {
					var blocks = await queue.Enqueue(
						(session, _) => session.Heap.GetSyncBlocks(request.Parameters), "enumerating sync blocks", ct);
					return Results.Content(JsonFormatter.FormatSyncBlocks(blocks), "application/json; charset=utf-8");
				}

			case "dumpmodule": {
					if (!TryRequireAddress(address, descriptor, out ulong moduleAddress, out var badAddress)) {
						return badAddress;
					}

					var details = await queue.Enqueue(
						(session, _) => session.Modules.GetModuleDetails(moduleAddress), "reading module", ct);
					return Results.Content(JsonFormatter.FormatModuleDetails(details), "application/json; charset=utf-8");
				}

			case "dumpassembly": {
					if (!TryRequireAddress(address, descriptor, out ulong assemblyAddress, out var badAddress)) {
						return badAddress;
					}

					var details = await queue.Enqueue(
						(session, _) => session.Modules.GetAssemblyDetails(assemblyAddress), "reading assembly", ct);
					return Results.Content(JsonFormatter.FormatAssemblyDetails(details), "application/json; charset=utf-8");
				}

			case "eeheap": {
					var heapInfo = await queue.Enqueue(
						(session, _) => session.Heap.GetHeapSegments(), "reading heap segments", ct);
					return Results.Content(JsonFormatter.FormatHeapSegments(heapInfo), "application/json; charset=utf-8");
				}

			case "threadpool": {
					var threadPoolInfo = await queue.Enqueue(
						(session, _) => session.Threads.GetThreadPoolInfo(), "reading thread pool", ct);
					return Results.Content(JsonFormatter.FormatThreadPool(threadPoolInfo), "application/json; charset=utf-8");
				}

			case "clrstack": {
					var groups = await queue.Enqueue(
						(session, _) => session.Threads.GetStackTraceGroups(maxFrames: 20), "enumerating stacks", ct);
					return Results.Content(JsonFormatter.FormatStackGroups(groups), "application/json; charset=utf-8");
				}

			case "eestack": {
					var groups = await queue.Enqueue(
						(session, _) => session.Threads.GetStackTraceGroups(maxFrames: 30), "enumerating stacks", ct);
					return Results.Content(JsonFormatter.FormatStackGroups(groups), "application/json; charset=utf-8");
				}

			case "dumpmt": {
					if (!TryRequireAddress(address, descriptor, out ulong methodTableAddress, out var badAddress)) {
						return badAddress;
					}

					var methodTableInfo = await queue.Enqueue(
						(session, _) => session.Metadata.GetMethodTable(methodTableAddress), "reading MethodTable", ct);
					return Results.Content(JsonFormatter.FormatMethodTable(methodTableInfo), "application/json; charset=utf-8");
				}

			case "dumpmd": {
					if (!TryRequireAddress(address, descriptor, out ulong methodDescAddress, out var badAddress)) {
						return badAddress;
					}

					var methodDescInfo = await queue.Enqueue(
						(session, _) => session.Metadata.GetMethodDesc(methodDescAddress), "reading MethodDesc", ct);
					return Results.Content(JsonFormatter.FormatMethodDesc(methodDescInfo), "application/json; charset=utf-8");
				}

			case "ip2md": {
					if (!TryRequireAddress(address, descriptor, out ulong instructionPointer, out var badAddress)) {
						return badAddress;
					}

					var methodDescInfo = await queue.Enqueue(
						(session, _) => session.Modules.GetMethodByIP(instructionPointer), "resolving instruction pointer", ct);
					return Results.Content(JsonFormatter.FormatMethodDesc(methodDescInfo), "application/json; charset=utf-8");
				}

			case "verifyobj": {
					if (!TryRequireAddress(address, descriptor, out ulong objectAddress, out var badAddress)) {
						return badAddress;
					}

					var corruptions = await queue.Enqueue(
						(session, _) => session.Heap.VerifyObject(objectAddress).ToList(), "verifying object", ct);
					return Results.Content(JsonFormatter.FormatObjectVerification(corruptions), "application/json; charset=utf-8");
				}

			case "dumpclass": {
					if (!TryRequireAddress(address, descriptor, out ulong eeClassAddress, out var badAddress)) {
						return badAddress;
					}

					var classInfo = await queue.Enqueue(
						(session, _) => session.Metadata.GetClass(eeClassAddress), "reading class", ct);
					return Results.Content(JsonFormatter.FormatClass(classInfo), "application/json; charset=utf-8");
				}

			case "verifyheap": {
					var corruptions = await queue.Enqueue(
						(session, _) => session.Heap.VerifyHeap(request.Parameters), "verifying heap", ct);
					return Results.Content(JsonFormatter.FormatHeapVerification(corruptions), "application/json; charset=utf-8");
				}

			case "dumpstack": {
					var stacks = await queue.Enqueue(
						(session, _) => session.Threads.GetDetailedStacks(request.Parameters), "walking thread stacks", ct);
					return Results.Content(JsonFormatter.FormatDetailedStacks(stacks), "application/json; charset=utf-8");
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

	/// <summary>
	/// Parses the trailing <c>/{address}</c> segment a single-record detail view identifies itself
	/// by. A missing or malformed value is a client error, exactly like an unsupported filter field.
	/// </summary>
	private static bool TryRequireAddress(string? address, ViewDescriptor descriptor, out ulong parsed, out IResult badRequest) {
		if (AddressParser.TryParse(address, out parsed)) {
			badRequest = Results.Empty;
			return true;
		}

		badRequest = Results.Text(
			$"'{descriptor.Name}' requires an address, e.g. /views/{descriptor.Name}/7FF6A1B02000 (hex, optionally 0x-prefixed).",
			"text/plain; charset=utf-8",
			statusCode: StatusCodes.Status400BadRequest);
		return false;
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