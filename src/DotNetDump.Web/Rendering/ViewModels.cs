using DotNetDump.Core.Models;
using DotNetDump.Web.Catalog;

using Microsoft.AspNetCore.Html;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// The full page: navigation, the dump header bar, and one view's fragment already rendered.
/// </summary>
/// <param name="FragmentHtml">
/// Pre-rendered by the handler rather than composed by the layout. A fragment must produce
/// byte-identical markup whether it arrives as a full page load or as an htmx swap — rendering it
/// through the same path in both cases is what guarantees that, instead of leaving it to two
/// templates staying in agreement.
/// </param>
/// <param name="CountSummary">
/// The view's honest row count, rendered in the view header rather than inside the fragment. It
/// lives outside the swapped region because the header is not re-rendered on a filter change —
/// Phase 4.5 updates it out of band instead. <c>null</c> for a view that has no count to show.
/// </param>
public sealed record ShellModel(
	string DumpPath,
	ViewDescriptor Current,
	IReadOnlyList<ViewDescriptor> Views,
	IHtmlContent FragmentHtml,
	string? CountSummary);

/// <summary>A paged, filtered, sorted table: what every <see cref="ViewKind.List"/> fragment binds to.</summary>
public sealed record ListModel<T>(ViewDescriptor View, PagedResult<T> Result) {
	/// <summary>
	/// The honest count line: post-filter over pre-filter (DATA_CONTRACT.md &#0167;2.5). The pre-filter
	/// total is what distinguishes "this dump has few types" from "your filter is too narrow", so it
	/// is shown even when the two are equal.
	/// </summary>
	public string CountSummary =>
		Result.TotalAvailable == Result.TotalUnfiltered
			? $"{Result.TotalAvailable:N0} rows"
			: $"{Result.TotalAvailable:N0} of {Result.TotalUnfiltered:N0} rows";
}