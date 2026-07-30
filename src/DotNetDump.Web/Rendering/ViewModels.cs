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
/// <param name="Info">
/// Runtime version, architecture and OS for the dump header bar. Sourced from
/// <c>SessionAnalyzer.GetInfo</c> via <see cref="DotNetDump.Web.Analysis.DumpInfoService"/>, which
/// memoizes the one <c>IAnalysisQueue.Enqueue</c> call behind a <see cref="Lazy{T}"/> rather than
/// reissuing it per request -- the dump this process was started against never changes, so the
/// answer cannot either.
/// </param>
/// <param name="CountSummary">
/// The view's honest row count, rendered in the view header rather than inside the fragment. It
/// lives outside the swapped region because the header is not re-rendered on a filter change —
/// Phase 4.5 updates it out of band instead. <c>null</c> for a view that has no count to show.
/// </param>
public sealed record ShellModel(
	string DumpPath,
	DumpInfo Info,
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

/// <summary>A single record: what every <see cref="ViewKind.Detail"/> fragment binds to.</summary>
public sealed record DetailModel<T>(ViewDescriptor View, T Data);

/// <summary>
/// <c>verifyobj</c>'s own record shape, bound as <c>DetailModel&lt;ObjectVerificationModel&gt;</c>.
/// A bare <c>List&lt;HeapCorruptionInfo&gt;</c> can't say which address was checked once the list
/// is empty -- the pass case, and the common one -- so the route's own parsed address rides along
/// instead of being re-derived from a (possibly absent) first item.
/// </summary>
public sealed record ObjectVerificationModel(ulong Address, List<HeapCorruptionInfo> Corruptions);