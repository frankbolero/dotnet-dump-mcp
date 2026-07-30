using System.Globalization;

using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;
using DotNetDump.Web.Catalog;

using Microsoft.AspNetCore.Http;

namespace DotNetDump.Web.Binding;

/// <summary>A bound request: the filter to apply and the sort/page window to apply it in.</summary>
public sealed record ViewRequest(FilterSpec Filter, QueryParameters Parameters);

/// <summary>
/// A query string this server will not act on. Carries the offending parameter so the message can
/// name it, and is mapped to <c>400</c> by the routes.
/// </summary>
public sealed class ViewRequestException(string parameter, string message) : Exception(message) {
	public string Parameter { get; } = parameter;
}

/// <summary>
/// Turns the query string of DATA_CONTRACT.md &#0167;3.2 into <c>(FilterSpec, QueryParameters)</c>. One
/// binder for every route and every view: the HTML fragment route, the rows route and the JSON
/// route all bind identically, which is what keeps the API contract from drifting away from what
/// the UI exercises (SERVER.md &#0167;2).
/// </summary>
/// <remarks>
/// The query string arrives from the address bar, so it arrives from the user, so it is untrusted —
/// every numeric field is range-checked and <c>limit</c> is clamped.
/// </remarks>
public static class ViewRequestBinder {
	public const int DefaultLimit = 50;

	/// <summary>
	/// Ceiling on <c>limit</c>. A page is a fragment the browser has to lay out; 500 rows of a table
	/// whose type-name column can be 200 characters wide is already past the point of being useful,
	/// and infinite scroll (Phase 4.4) is how more rows are reached.
	/// </summary>
	public const int MaxLimit = 500;

	public static ViewRequest Bind(IQueryCollection query, ViewDescriptor view) {
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(view);

		var filter = new FilterSpec {
			TypeName = Text(query, "type"),
			TypeNameRegex = Text(query, "typeRegex"),
			Module = Text(query, "module"),
			Text = Text(query, "text"),
			MinSize = UInt64(query, "minSize"),
			MaxSize = UInt64(query, "maxSize"),
			MinCount = Int32(query, "minCount", min: 0),
			MaxCount = Int32(query, "maxCount", min: 0),
			Generation = Generation(query, "gen"),
			ManagedThreadId = Int32(query, "thread", min: 0),
			OSThreadId = UInt32(query, "osthread"),
			HasException = Bool(query, "hasException"),
		};

		// Before anything is enqueued. A field this view does not honor is a client error, and
		// SERVER.md §2.1 requires the JSON route to say so rather than silently returning unfiltered
		// data -- the failure mode that DATA_CONTRACT.md §2.3 exists to prevent.
		try {
			filter.EnsureSupported(view.Name, view.HonoredFilters);
		} catch (Core.UnsupportedFilterException ex) {
			throw new ViewRequestException("filter", ex.Message);
		}

		// Compiling the pattern here rather than letting it fail inside the analyzer keeps a
		// malformed regex off the analysis queue entirely, and gives it the same "costs nothing,
		// cold cache or warm" property TypeNameMatcher already documents.
		try {
			_ = TypeNameMatcher.Create(filter);
		} catch (ArgumentException ex) {
			throw new ViewRequestException("typeRegex", ex.Message);
		}

		var parameters = new QueryParameters {
			Filter = filter,
			SortBy = Text(query, "sort"),
			SortDirection = Order(query, "order"),
			Offset = Int32(query, "offset", min: 0) ?? 0,
			Limit = Math.Clamp(Int32(query, "limit", min: 1) ?? DefaultLimit, 1, MaxLimit),
		};

		return new ViewRequest(filter, parameters);
	}

	/// <summary>
	/// A present-but-empty parameter reads as unset, not as an empty filter.
	/// </summary>
	/// <remarks>
	/// This is load-bearing, not tidiness. <c>hx-include="closest form"</c> submits every input in
	/// the filter bar on every keystroke, including the ones the user has not typed into, so
	/// <c>?type=&amp;text=Http</c> is the normal shape of a request. Binding <c>type=</c> to
	/// <c>TypeName = ""</c> would mark <see cref="FilterField.TypeName"/> as set, which makes
	/// <see cref="FilterSpec.EnsureSupported"/> reject the request on any view that does not honor
	/// type filtering — a 400 the user could not have caused and could not clear.
	/// </remarks>
	private static string? Text(IQueryCollection query, string name) {
		if (!query.TryGetValue(name, out var values)) {
			return null;
		}

		string? value = values.Count > 0 ? values[^1] : null;
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	private static int? Int32(IQueryCollection query, string name, int min) {
		string? raw = Text(query, name);
		if (raw is null) {
			return null;
		}

		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min) {
			throw new ViewRequestException(name, $"'{name}' must be a whole number of at least {min}.");
		}

		return value;
	}

	private static ulong? UInt64(IQueryCollection query, string name) {
		string? raw = Text(query, name);
		if (raw is null) {
			return null;
		}

		// Bytes, plain. The '4kb' unit grammar belongs to the CLI's --filter expressions
		// (DATA_CONTRACT.md §2.4) and is parsed there; the web host binds query parameters directly
		// and never parses that grammar.
		if (!ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value)) {
			throw new ViewRequestException(name, $"'{name}' must be a whole number of bytes.");
		}

		return value;
	}

	private static uint? UInt32(IQueryCollection query, string name) {
		string? raw = Text(query, name);
		if (raw is null) {
			return null;
		}

		if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value)) {
			throw new ViewRequestException(name, $"'{name}' must be a whole number.");
		}

		return value;
	}

	private static bool? Bool(IQueryCollection query, string name) {
		string? raw = Text(query, name);
		return raw switch {
			null => null,
			_ when raw.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
			_ when raw.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
			// A checkbox submits its value only when checked, so htmx sends 'on' for a ticked box.
			_ when raw.Equals("on", StringComparison.OrdinalIgnoreCase) => true,
			_ => throw new ViewRequestException(name, $"'{name}' must be true or false."),
		};
	}

	private static GenerationFilter? Generation(IQueryCollection query, string name) {
		string? raw = Text(query, name);
		if (raw is null) {
			return null;
		}

		// Both the bare digits a generation selector naturally submits and the enum's own names.
		return raw.ToLowerInvariant() switch {
			"0" or "gen0" => GenerationFilter.Gen0,
			"1" or "gen1" => GenerationFilter.Gen1,
			"2" or "gen2" => GenerationFilter.Gen2,
			"loh" => GenerationFilter.Loh,
			"poh" => GenerationFilter.Poh,
			"frozen" => GenerationFilter.Frozen,
			_ => throw new ViewRequestException(name, $"'{name}' must be one of 0, 1, 2, loh, poh, frozen."),
		};
	}

	/// <summary>
	/// Rejects an unrecognized <c>order</c> rather than falling back to descending.
	/// </summary>
	/// <remarks>
	/// This is a deliberate divergence from <c>DotNetDump.Cli.QueryParametersBuilder</c>, which is
	/// tolerant so the CLI and MCP front ends behave identically for the same input. The query
	/// string here is not an argument the user typed once — it is the view state that
	/// <c>hx-push-url</c> writes to the address bar and the back button replays (DATA_CONTRACT.md
	/// &#0167;3.2). Quietly reinterpreting view state is the class of bug Phase 4 exists to prevent, so
	/// the web host says what it will not do instead of guessing.
	/// </remarks>
	private static SortDirection Order(IQueryCollection query, string name) {
		string? raw = Text(query, name);
		return raw switch {
			null => SortDirection.Desc,
			_ when raw.Equals("asc", StringComparison.OrdinalIgnoreCase) => SortDirection.Asc,
			_ when raw.Equals("desc", StringComparison.OrdinalIgnoreCase) => SortDirection.Desc,
			_ => throw new ViewRequestException(name, $"'{name}' must be asc or desc."),
		};
	}
}