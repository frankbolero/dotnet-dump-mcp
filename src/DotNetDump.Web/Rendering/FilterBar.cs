using DotNetDump.Core.Models;
using DotNetDump.Web.Catalog;

using Microsoft.AspNetCore.Http;

namespace DotNetDump.Web.Rendering;

/// <summary>Which HTML control a <see cref="FilterControl"/> renders as.</summary>
public enum FilterControlKind {
	/// <summary>A single free-text input, rendered as <c>type="search"</c>.</summary>
	Text,

	/// <summary>A single numeric input.</summary>
	Number,

	/// <summary>Two numeric inputs sharing one label -- <see cref="FilterControl.SecondaryName"/> etc. carry the second.</summary>
	Range,

	/// <summary>A closed set of values, including an "Any" option that clears the field.</summary>
	Select,
}

public sealed record FilterSelectOption(string Value, string Label);

/// <summary>
/// One control in the filter bar. <see cref="FilterControlKind.Range"/> is the only kind that uses
/// the <c>Secondary*</c> members; every other kind ignores them.
/// </summary>
public sealed record FilterControl(
	FilterControlKind Kind,
	string Label,
	string Name,
	string? Value,
	string? Placeholder,
	string? SecondaryName = null,
	string? SecondaryValue = null,
	string? SecondaryPlaceholder = null,
	IReadOnlyList<FilterSelectOption>? Options = null);

/// <summary>One active filter, rendered as a removable chip (task 4.2).</summary>
public sealed record FilterChip(string Label, string RemoveUrl);

/// <summary>
/// Everything a view's filter bar needs to render: which controls to show, their current values,
/// which filters are active as chips, and where "clear all" goes.
/// </summary>
public sealed record FilterBarModel(
	ViewDescriptor View,
	string ActionUrl,
	IReadOnlyList<FilterControl> Controls,
	IReadOnlyList<FilterChip> Chips,
	string? ClearAllUrl);

/// <summary>
/// Builds a <see cref="FilterBarModel"/> from a view's honored fields (task 4.1) and the query
/// string already on the request.
/// </summary>
/// <remarks>
/// <para>
/// The mapping from <see cref="FilterField"/> to control is the single data-driven table in
/// <see cref="Build"/> -- which controls appear for a given view falls out of
/// <see cref="ViewDescriptor.HonoredFilters"/> alone, so the eight views this task covers do not
/// each carry their own hand-copied field list. A new honored field needs one new
/// <c>honored.HasFlag(...)</c> branch here, never a new per-view template.
/// </para>
/// <para>
/// <see cref="FilterBarModel.ActionUrl"/> and every chip/clear-all URL carry <c>limit</c> when
/// present, but never <c>offset</c>: changing, adding or removing a filter is a new result set, and
/// starting it back at the first page is simpler and safer than trying to keep an offset
/// meaningful against row counts that just changed underneath it. Infinite scroll (task 4.4) is
/// what re-extends the page from there. <c>sort</c>/<c>order</c> are excluded too, deliberately --
/// see the remarks on <see cref="StateParams"/> for why.
/// </para>
/// </remarks>
public static class FilterBar {
	// Query parameter names, DATA_CONTRACT.md §3.2.
	private const string Type = "type";
	private const string Module = "module";
	private const string TextParam = "text";
	private const string MinSize = "minSize";
	private const string MaxSize = "maxSize";
	private const string MinCount = "minCount";
	private const string MaxCount = "maxCount";
	private const string Gen = "gen";
	private const string Thread = "thread";
	private const string OSThread = "osthread";
	private const string HasException = "hasException";

	/// <summary>
	/// Carried by every control's action URL and by chip/clear-all URLs: <c>limit</c> describes
	/// *how much* of the view is displayed, not *what* is included, so narrowing or clearing a
	/// filter must not reset it. <c>offset</c> is deliberately not one of these -- see the remarks
	/// on <see cref="Build"/>.
	/// </summary>
	/// <remarks>
	/// <c>sort</c> and <c>order</c> are deliberately excluded, even though they are also "how, not
	/// what" state. <see cref="SortHeader"/> is the only thing that ever sets them, with an
	/// explicit new value baked into its own <c>href</c>; a filter control or chip that also carried
	/// the *current* sort into its own URL would put two sources of truth for the same query key
	/// into the one form <c>hx-include="closest form"</c> reads from, and if a stale value ever won
	/// that race a header click would silently stop changing the sort -- exactly the class of bug
	/// this phase exists to prevent, just aimed at the opposite parameter. The cost is narrow:
	/// typing a filter or removing one resets the view to its default sort rather than preserving a
	/// previously chosen one.
	/// </remarks>
	private static readonly string[] StateParams = ["limit"];

	private static readonly string[] FilterParams =
		[Type, Module, TextParam, MinSize, MaxSize, MinCount, MaxCount, Gen, Thread, OSThread, HasException];

	private static readonly IReadOnlyList<FilterSelectOption> GenerationOptions = [
		new("", "Any"),
		new("0", "Gen 0"),
		new("1", "Gen 1"),
		new("2", "Gen 2"),
		new("loh", "LOH"),
		new("poh", "POH"),
		new("frozen", "Frozen"),
	];

	private static readonly IReadOnlyList<FilterSelectOption> HasExceptionOptions = [
		new("", "Any"),
		new("true", "Yes"),
		new("false", "No"),
	];

	public static FilterBarModel Build(ViewDescriptor view, IQueryCollection query) {
		var honored = view.HonoredFilters;
		var controls = new List<FilterControl>();

		// Text first -- the one control every honored set in this task carries (DATA_CONTRACT.md
		// §2.3's Text column is populated for all eight), and the broadest net, so it reads as the
		// primary search box the design brief calls for.
		if (honored.HasFlag(FilterField.Text)) {
			controls.Add(new FilterControl(FilterControlKind.Text, "Search", TextParam, QueryStrings.Raw(query, TextParam), TextPlaceholder(view.Name)));
		}

		if (honored.HasFlag(FilterField.AnyTypeName) || honored.HasFlag(FilterField.TypeName)) {
			controls.Add(new FilterControl(FilterControlKind.Text, "Type name", Type, QueryStrings.Raw(query, Type), "e.g. System.String"));
		}

		if (honored.HasFlag(FilterField.Module)) {
			controls.Add(new FilterControl(FilterControlKind.Text, "Module", Module, QueryStrings.Raw(query, Module), "e.g. System.Private.CoreLib"));
		}

		if (honored.HasFlag(FilterField.Size)) {
			controls.Add(new FilterControl(
				FilterControlKind.Range, SizeLabel(view.Name), MinSize, QueryStrings.Raw(query, MinSize), "bytes",
				SecondaryName: MaxSize, SecondaryValue: QueryStrings.Raw(query, MaxSize), SecondaryPlaceholder: "bytes"));
		}

		if (honored.HasFlag(FilterField.Count)) {
			controls.Add(new FilterControl(
				FilterControlKind.Range, "Count", MinCount, QueryStrings.Raw(query, MinCount), "min",
				SecondaryName: MaxCount, SecondaryValue: QueryStrings.Raw(query, MaxCount), SecondaryPlaceholder: "max"));
		}

		if (honored.HasFlag(FilterField.Generation)) {
			controls.Add(new FilterControl(FilterControlKind.Select, "Generation", Gen, QueryStrings.Raw(query, Gen), null, Options: GenerationOptions));
		}

		if (honored.HasFlag(FilterField.ManagedThreadId)) {
			controls.Add(new FilterControl(FilterControlKind.Number, "Managed thread ID", Thread, QueryStrings.Raw(query, Thread), "e.g. 14"));
		}

		if (honored.HasFlag(FilterField.OSThreadId)) {
			controls.Add(new FilterControl(FilterControlKind.Number, "OS thread ID", OSThread, QueryStrings.Raw(query, OSThread), "e.g. 1234"));
		}

		if (honored.HasFlag(FilterField.HasException)) {
			controls.Add(new FilterControl(FilterControlKind.Select, "Has exception", HasException, QueryStrings.Raw(query, HasException), null, Options: HasExceptionOptions));
		}

		string actionUrl = QueryStrings.BuildUrl(view.Name, StatePairs(query));
		var chips = BuildChips(view, query);
		string? clearAllUrl = chips.Count > 0 ? actionUrl : null;

		return new FilterBarModel(view, actionUrl, controls, chips, clearAllUrl);
	}

	private static IEnumerable<(string, string?)> StatePairs(IQueryCollection query) =>
		StateParams.Select(name => (name, QueryStrings.Raw(query, name)));

	private static List<FilterChip> BuildChips(ViewDescriptor view, IQueryCollection query) {
		var active = FilterParams
			.Select(name => (Name: name, Value: QueryStrings.Raw(query, name)))
			.Where(field => field.Value != null)
			.ToList();

		var state = StatePairs(query).ToList();
		var chips = new List<FilterChip>();

		foreach (var (name, value) in active) {
			// Every other active filter stays, alongside sort/order/limit -- only this one field
			// drops out. That is what makes each chip's removal independent of the others.
			var remaining = state.Concat(active.Where(field => field.Name != name).Select(field => (field.Name, (string?)field.Value)));
			chips.Add(new FilterChip(ChipLabel(name, value!), QueryStrings.BuildUrl(view.Name, remaining)));
		}

		return chips;
	}

	private static string ChipLabel(string name, string value) => name switch {
		Type => $"type: {value}",
		Module => $"module: {value}",
		TextParam => $"search: {value}",
		MinSize => $"size ≥ {FormatBytes(value)}",
		MaxSize => $"size ≤ {FormatBytes(value)}",
		MinCount => $"count ≥ {FormatCount(value)}",
		MaxCount => $"count ≤ {FormatCount(value)}",
		Gen => $"gen: {GenerationLabel(value)}",
		Thread => $"thread: {value}",
		OSThread => $"OS thread: {value}",
		HasException => $"exception: {(value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "no" : "yes")}",
		_ => $"{name}: {value}",
	};

	private static string FormatBytes(string raw) => ulong.TryParse(raw, out ulong value) ? Display.Size((long)value) : raw;

	private static string FormatCount(string raw) => int.TryParse(raw, out int value) ? Display.Count(value) : raw;

	private static string GenerationLabel(string raw) => raw.ToLowerInvariant() switch {
		"0" or "gen0" => "Gen 0",
		"1" or "gen1" => "Gen 1",
		"2" or "gen2" => "Gen 2",
		"loh" => "LOH",
		"poh" => "POH",
		"frozen" => "Frozen",
		_ => raw,
	};

	/// <summary>
	/// "Size" means something different per view (DATA_CONTRACT.md &#0167;2.3's correction: aggregate
	/// on <c>dumpheap</c>, per-instance on <c>listobj</c>, image size on <c>clrmodules</c>), and the
	/// filter bar must label it accordingly rather than showing the bare word "size" on all three.
	/// </summary>
	private static string SizeLabel(string viewName) => viewName switch {
		"dumpheap" => "Total size",
		"listobj" => "Object size",
		"clrmodules" => "Image size",
		_ => "Size",
	};

	/// <summary>
	/// What the search box actually matches, per the "Text refers to" column of DATA_CONTRACT.md
	/// &#0167;2.3 -- stated in the placeholder so a user is not left guessing why a search hits rows
	/// with no visible match (e.g. `printexception`'s message text is not a rendered column here).
	/// </summary>
	private static string TextPlaceholder(string viewName) => viewName switch {
		"dumpheap" or "listobj" or "syncblk" => "Search type name…",
		"gchandles" => "Search type or kind…",
		"printexception" => "Search type or message…",
		"clrthreads" => "Search exception type…",
		"threadstate" => "Search exception or flags…",
		"clrmodules" => "Search module name…",
		_ => "Search…",
	};
}