using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace DotNetDump.Web.Rendering;

/// <summary>Renders a Razor view to a string. Every route response goes through this.</summary>
public interface IFragmentRenderer {
	/// <summary>
	/// Renders <paramref name="viewPath"/> against <paramref name="model"/>.
	/// </summary>
	/// <param name="viewPath">
	/// An explicit application-relative path, e.g. <c>/Views/Fragments/DumpHeap.cshtml</c>. Explicit
	/// rather than a convention-resolved name: there are no controllers here for
	/// <c>{controller}/{action}</c> discovery to key off, and a view that silently fails to resolve
	/// is worse than one that throws.
	/// </param>
	Task<string> RenderAsync<TModel>(HttpContext context, string viewPath, TModel model);
}

/// <summary>
/// <see cref="IFragmentRenderer"/> over the MVC Razor view engine.
/// </summary>
/// <remarks>
/// Views are compiled at build time into this assembly; there is no runtime compilation package and
/// no Node, so a synced design change is a rebuild, not a hot reload. Views take a view model and
/// never an analyzer or an <c>IDumpContext</c> (SERVER.md &#0167;5.3) — handlers do the analysis, views do
/// the markup, which is the discipline that keeps the design library swappable in Phase 3.
/// </remarks>
public sealed class RazorFragmentRenderer(
	IRazorViewEngine viewEngine,
	ITempDataProvider tempDataProvider,
	IModelMetadataProvider metadataProvider) : IFragmentRenderer {

	public async Task<string> RenderAsync<TModel>(HttpContext context, string viewPath, TModel model) {
		var actionContext = new ActionContext(context, context.GetRouteData(), new ActionDescriptor());

		var found = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: true);
		if (!found.Success) {
			throw new InvalidOperationException(
				$"View '{viewPath}' was not found. Searched: {string.Join(", ", found.SearchedLocations)}");
		}

		await using var writer = new StringWriter();
		var viewData = new ViewDataDictionary<TModel>(metadataProvider, new ModelStateDictionary()) {
			Model = model,
		};

		var viewContext = new ViewContext(
			actionContext,
			found.View,
			viewData,
			new TempDataDictionary(context, tempDataProvider),
			writer,
			new HtmlHelperOptions());

		await found.View.RenderAsync(viewContext);
		return writer.ToString();
	}
}