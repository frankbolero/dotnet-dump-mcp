using DotNetDump.Core.Models;
using DotNetDump.Web.Catalog;

using Microsoft.AspNetCore.Html;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// The full page: navigation, the dump header bar, and one view's or tree's fragment already
/// rendered.
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
/// <param name="Title">The view header's title, command and description, as flat strings rather
/// than <c>Model.CurrentView.X</c> -- a tree page (Phase 5) has no <see cref="ViewDescriptor"/> to
/// read them from, and the header does not need to know which kind of page it is rendering.</param>
/// <param name="Command">See <paramref name="Title"/>.</param>
/// <param name="Description">See <paramref name="Title"/>.</param>
/// <param name="CurrentView">
/// The view this page renders, for the nav's <c>aria-current</c> match. <c>null</c> on a tree page,
/// where no entry in <see cref="Views"/> is the current one.
/// </param>
/// <param name="CurrentTreeName">
/// The tree this page renders (<see cref="Catalog.TreeDescriptor.Name"/>), for the same match
/// against <see cref="Trees"/>. <c>null</c> on a view page.
/// </param>
/// <param name="CountSummary">
/// The view's honest row count, rendered in the view header rather than inside the fragment. It
/// lives outside the swapped region because the header is not re-rendered on a filter change —
/// Phase 4.5 updates it out of band instead. <c>null</c> for a view or tree that has no count to
/// show.
/// </param>
public sealed record ShellModel(
	string DumpPath,
	DumpInfo Info,
	string Title,
	string Command,
	string Description,
	ViewDescriptor? CurrentView,
	string? CurrentTreeName,
	IReadOnlyList<ViewDescriptor> Views,
	IReadOnlyList<TreeDescriptor> Trees,
	IHtmlContent FragmentHtml,
	string? CountSummary) {

	/// <summary>Convenience for the common case: a view page, where the header fields are always
	/// the descriptor's own. Every Phase 0-4 call site uses this; only Phase 5's tree pages build a
	/// <see cref="ShellModel"/> directly.</summary>
	public static ShellModel ForView(
		string dumpPath, DumpInfo info, ViewDescriptor descriptor, IReadOnlyList<ViewDescriptor> views,
		IReadOnlyList<TreeDescriptor> trees, IHtmlContent fragmentHtml, string? countSummary) =>
		new(dumpPath, info, descriptor.Title, descriptor.Command, descriptor.Description, descriptor, null,
			views, trees, fragmentHtml, countSummary);
}

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