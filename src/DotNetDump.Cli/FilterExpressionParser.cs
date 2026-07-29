using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using DotNetDump.Core.Models;

namespace DotNetDump.Cli;

/// <summary>
/// Parses the repeatable <c>--filter &lt;field&gt;&lt;op&gt;&lt;value&gt;</c> grammar
/// (DATA_CONTRACT.md &#0167;2.4) into a single <see cref="FilterSpec"/>. Every parse failure is a
/// <see cref="CliUsageException"/> -- there is exactly one error channel for a bad expression, the
/// same one <see cref="DumpResolver"/> already uses for "no dump resolved" (CLI_DESIGN.md &#0167;3.4:
/// exit code 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Which <see cref="FilterSpec"/> property an operator can target is decided by what that
/// property can represent, not by which operators the grammar table lists in general.</b> Three
/// groups:
/// </para>
/// <list type="bullet">
/// <item><description><c>type</c>, <c>module</c>, <c>text</c> back a case-insensitive substring
/// property only (<see cref="FilterSpec.TypeName"/>, <see cref="FilterSpec.Module"/>,
/// <see cref="FilterSpec.Text"/>). Only <c>~</c> is accepted. <c>type</c> additionally accepts
/// <c>=</c> when the value is <c>/regex/</c>-delimited, which targets
/// <see cref="FilterSpec.TypeNameRegex"/> instead -- a distinct property, so
/// <c>--filter 'type~Http' --filter 'type=/^Http/'</c> is not a duplicate-field conflict; both are
/// honored and ANDed by <c>TypeNameMatcher</c> (Filtering/TypeNameMatcher.cs).</description></item>
/// <item><description><c>size</c>, <c>count</c> back an inclusive min/max pair. <c>&gt;=</c>/<c>&lt;=</c>
/// map straight onto <see cref="FilterSpec.MinSize"/>/<see cref="FilterSpec.MaxSize"/> (or the
/// <c>Count</c> equivalents). <c>&gt;</c>/<c>&lt;</c> are strict, and there is no exclusive-bound
/// property to hold them -- both fields are documented inclusive (FilterSpec.cs). For an integral
/// domain "strictly greater than N" is exactly "greater than or equal to N+1", so <c>&gt;</c> sets
/// Min to <c>value + 1</c> and <c>&lt;</c> sets Max to <c>value - 1</c>. This is exact, not an
/// approximation, and deliberately not the same value <c>&gt;=</c>/<c>&lt;=</c> would produce --
/// aliasing <c>&gt;</c> to <c>&gt;=</c> would silently include one extra value at the
/// boundary. <c>=</c> sets both Min and Max to <c>value</c>, an exact-value range.</description></item>
/// <item><description><c>gen</c>, <c>thread</c>, <c>osthread</c>, <c>exception</c> back a single
/// scalar equality property. Only <c>=</c> is accepted.</description></item>
/// </list>
/// <para>
/// <b><c>type=X</c> with a bare (non-slash) value is rejected, not coerced.</b>
/// <see cref="FilterSpec"/> has no exact-match type property: <see cref="FilterSpec.TypeName"/> is
/// documented substring, <see cref="FilterSpec.TypeNameRegex"/> is a pattern. Silently mapping
/// <c>type=Foo</c> onto TypeName would turn a user's "equals" into "contains" without telling them;
/// mapping it onto TypeNameRegex would turn it into an implicitly-anchored pattern they did not
/// write. Both are wrong answers dressed as acceptance, so this is a usage error: use
/// <c>type~Foo</c> for substring or <c>type=/^Foo$/</c> to say "equals" precisely. Same reasoning
/// for <c>module=X</c> -- <see cref="FilterSpec.Module"/> is substring-only and has no regex
/// counterpart, so <c>module</c> accepts only <c>~</c>.
/// </para>
/// <para>
/// <b>Negating operators (<c>!~</c>, <c>!=</c>) are rejected on every field</b>, not silently
/// dropped and not inverted onto some other property. <see cref="FilterSpec"/> has no negated
/// counterpart for any field (every property is a positive match) -- see the type's remarks. Adding
/// one is a decision for whoever owns <see cref="FilterSpec"/>, not something this parser should
/// invent by, say, storing a negated substring in the same property and hoping every consumer checks
/// a sign bit.
/// </para>
/// <para>
/// <b>Byte-size units are binary (1024-based)</b>: <c>1kb</c> = 1024 bytes, <c>1mb</c> = 1024&#178;
/// bytes, <c>1gb</c> = 1024&#179; bytes -- the SOS/debugger convention this tool otherwise follows,
/// not the decimal (1000-based) SI convention. A bare integer with no unit is bytes.
/// </para>
/// <para>
/// <b>Two <c>--filter</c> arguments that target the same <see cref="FilterSpec"/> property
/// conflict</b> and are rejected rather than silently taking the last one: the property is
/// single-valued, so "last one wins" would make <c>--filter 'type~A' --filter 'type~B'</c> quietly
/// mean "type~B", not "type~A AND type~B" (which is not otherwise representable) and not an error a
/// user would notice. Two arguments that target <i>different</i> properties of the same CLI field
/// name (<c>size&gt;100mb</c> then <c>size&lt;200mb</c>, setting Min and Max respectively) compose
/// into a range, which is exactly what the grammar is for.
/// </para>
/// </remarks>
internal static class FilterExpressionParser {
	private static readonly Regex ByteSizePattern = new(
		@"^(?<num>\d+)\s*(?<unit>b|kb|mb|gb)?$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	private const ulong BytesPerKb = 1024UL;
	private const ulong BytesPerMb = BytesPerKb * 1024UL;
	private const ulong BytesPerGb = BytesPerMb * 1024UL;

	/// <summary>Parses zero or more <c>--filter</c> expressions into one ANDed <see cref="FilterSpec"/>.
	/// An empty or null list returns <see cref="FilterSpec.None"/> unchanged, so commands that never
	/// receive <c>--filter</c> pay nothing.</summary>
	public static FilterSpec Parse(IReadOnlyList<string>? expressions) {
		if (expressions == null || expressions.Count == 0)
			return FilterSpec.None;

		var state = new BuilderState();
		foreach (string expression in expressions) {
			ParseOne(expression, state);
		}

		return state.Build();
	}

	private static void ParseOne(string expression, BuilderState state) {
		if (string.IsNullOrWhiteSpace(expression)) {
			throw new CliUsageException("Invalid --filter '': expected '<field><op><value>', e.g. 'type~Http' or 'size>100mb'.");
		}

		string trimmed = expression.Trim();
		(string field, int fieldEnd) = ReadField(trimmed, expression);
		(FilterOp op, int opEnd) = ReadOperator(trimmed, fieldEnd, expression);
		string rawValue = trimmed.Substring(opEnd).Trim();
		string value = Unquote(rawValue);

		if (op is FilterOp.NotContains or FilterOp.NotEquals) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': negating operators ('!~', '!=') are not supported. " +
				"FilterSpec has no negated form for any field -- every filter is a positive match.");
		}

		switch (field) {
			case "type":
				ApplyType(expression, op, value, state);
				break;
			case "module":
				ApplySubstring(expression, "module", op, value, state, FilterField.Module, v => state.Module = v);
				break;
			case "text":
				ApplySubstring(expression, "text", op, value, state, FilterField.Text, v => state.Text = v);
				break;
			case "size":
				ApplySizeRange(expression, op, value, state);
				break;
			case "count":
				ApplyCountRange(expression, op, value, state);
				break;
			case "gen":
				ApplyGeneration(expression, op, value, state);
				break;
			case "thread":
				ApplyManagedThreadId(expression, op, value, state);
				break;
			case "osthread":
				ApplyOSThreadId(expression, op, value, state);
				break;
			case "exception":
				ApplyHasException(expression, op, value, state);
				break;
			default:
				throw new CliUsageException(
					$"Invalid --filter '{expression}': unknown field '{field}'. Expected one of: " +
					"type, module, size, count, gen, thread, osthread, exception, text.");
		}
	}

	// ---- field tokenizing -------------------------------------------------------------------

	private static readonly string[] KnownFields = {
		// "osthread" must be tried before "thread" would otherwise be irrelevant (no shared
		// prefix), but ordered longest-first regardless so this stays correct if a future field
		// ever does share one.
		"osthread", "exception", "thread", "module", "type", "size", "count", "gen", "text"
	};

	private static (string field, int end) ReadField(string trimmed, string original) {
		foreach (string candidate in KnownFields) {
			if (trimmed.Length >= candidate.Length &&
				trimmed.AsSpan(0, candidate.Length).Equals(candidate, StringComparison.OrdinalIgnoreCase)) {
				return (candidate, candidate.Length);
			}
		}

		throw new CliUsageException(
			$"Invalid --filter '{original}': could not find a field name at the start. Expected one of: " +
			"type, module, size, count, gen, thread, osthread, exception, text.");
	}

	private enum FilterOp { Contains, NotContains, Equals, NotEquals, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual }

	private static (FilterOp op, int end) ReadOperator(string trimmed, int start, string original) {
		int i = start;
		while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i])) i++;

		if (i >= trimmed.Length) {
			throw new CliUsageException(
				$"Invalid --filter '{original}': missing an operator after the field name. Expected one of: " +
				"~ !~ = != > >= < <=.");
		}

		// Two-character operators must be tried before their one-character prefixes.
		string rest = trimmed.Substring(i);
		(FilterOp op, int len)? match = rest switch {
			var r when r.StartsWith("!~", StringComparison.Ordinal) => (FilterOp.NotContains, 2),
			var r when r.StartsWith("!=", StringComparison.Ordinal) => (FilterOp.NotEquals, 2),
			var r when r.StartsWith(">=", StringComparison.Ordinal) => (FilterOp.GreaterOrEqual, 2),
			var r when r.StartsWith("<=", StringComparison.Ordinal) => (FilterOp.LessOrEqual, 2),
			var r when r.StartsWith("~", StringComparison.Ordinal) => (FilterOp.Contains, 1),
			var r when r.StartsWith("=", StringComparison.Ordinal) => (FilterOp.Equals, 1),
			var r when r.StartsWith(">", StringComparison.Ordinal) => (FilterOp.GreaterThan, 1),
			var r when r.StartsWith("<", StringComparison.Ordinal) => (FilterOp.LessThan, 1),
			_ => null,
		};

		if (match is null) {
			throw new CliUsageException(
				$"Invalid --filter '{original}': unrecognized operator. Expected one of: ~ !~ = != > >= < <=.");
		}

		int end = i + match.Value.len;
		while (end < trimmed.Length && char.IsWhiteSpace(trimmed[end])) end++;
		return (match.Value.op, end);
	}

	/// <summary>Strips a single layer of matching quotes (<c>'...'</c> or <c>"..."</c>) if the whole
	/// value is wrapped in them. A bare or partially-quoted value passes through unchanged --
	/// quoting is optional, needed only to carry whitespace (DATA_CONTRACT.md &#0167;2.4: "bare or
	/// quoted string").</summary>
	private static string Unquote(string value) {
		if (value.Length >= 2) {
			char first = value[0];
			char last = value[value.Length - 1];
			if ((first == '\'' || first == '"') && first == last) {
				return value.Substring(1, value.Length - 2);
			}
		}

		return value;
	}

	// ---- per-field application ---------------------------------------------------------------

	private static void ApplyType(string expression, FilterOp op, string value, BuilderState state) {
		if (op == FilterOp.Contains) {
			state.Set(FilterField.TypeName, expression, () => state.TypeName = value);
			return;
		}

		if (op == FilterOp.Equals) {
			string? regex = TryStripRegexDelimiters(value);
			if (regex == null) {
				throw new CliUsageException(
					$"Invalid --filter '{expression}': 'type=' requires a /regex/ value. " +
					"Use 'type~value' for a substring match, or 'type=/pattern/' for a regular expression.");
			}

			state.Set(FilterField.TypeNameRegex, expression, () => state.TypeNameRegex = regex);
			return;
		}

		throw new CliUsageException(
			$"Invalid --filter '{expression}': 'type' supports '~' (substring) and '=' with a /regex/ value only.");
	}

	private static void ApplySubstring(string expression, string fieldName, FilterOp op, string value, BuilderState state, FilterField target, Action<string> assign) {
		if (op != FilterOp.Contains) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': '{fieldName}' supports only '~' (substring match); " +
				$"it has no exact-match or ordered form.");
		}

		state.Set(target, expression, () => assign(value));
	}

	private static string? TryStripRegexDelimiters(string value) {
		if (value.Length >= 2 && value[0] == '/' && value[value.Length - 1] == '/') {
			return value.Substring(1, value.Length - 2);
		}

		return null;
	}

	private static void ApplySizeRange(string expression, FilterOp op, string value, BuilderState state) {
		ulong bytes = ParseByteSize(expression, value);

		switch (op) {
			case FilterOp.GreaterOrEqual:
				state.Set(FilterField.MinSize, expression, () => state.MinSize = bytes);
				break;
			case FilterOp.LessOrEqual:
				state.Set(FilterField.MaxSize, expression, () => state.MaxSize = bytes);
				break;
			case FilterOp.GreaterThan:
				state.Set(FilterField.MinSize, expression, () => state.MinSize = CheckedIncrement(expression, bytes));
				break;
			case FilterOp.LessThan:
				state.Set(FilterField.MaxSize, expression, () => state.MaxSize = CheckedDecrement(expression, bytes));
				break;
			case FilterOp.Equals:
				state.Set(FilterField.MinSize, expression, () => state.MinSize = bytes);
				state.Set(FilterField.MaxSize, expression, () => state.MaxSize = bytes);
				break;
			default:
				throw new CliUsageException(
					$"Invalid --filter '{expression}': 'size' supports =, >, >=, < and <= only.");
		}
	}

	private static void ApplyCountRange(string expression, FilterOp op, string value, BuilderState state) {
		int count = ParseInt(expression, "count", value);

		switch (op) {
			case FilterOp.GreaterOrEqual:
				state.Set(FilterField.MinCount, expression, () => state.MinCount = count);
				break;
			case FilterOp.LessOrEqual:
				state.Set(FilterField.MaxCount, expression, () => state.MaxCount = count);
				break;
			case FilterOp.GreaterThan:
				state.Set(FilterField.MinCount, expression, () => state.MinCount = CheckedIncrement(expression, count));
				break;
			case FilterOp.LessThan:
				state.Set(FilterField.MaxCount, expression, () => state.MaxCount = CheckedDecrement(expression, count));
				break;
			case FilterOp.Equals:
				state.Set(FilterField.MinCount, expression, () => state.MinCount = count);
				state.Set(FilterField.MaxCount, expression, () => state.MaxCount = count);
				break;
			default:
				throw new CliUsageException(
					$"Invalid --filter '{expression}': 'count' supports =, >, >=, < and <= only.");
		}
	}

	private static void ApplyGeneration(string expression, FilterOp op, string value, BuilderState state) {
		if (op != FilterOp.Equals) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': 'gen' supports only '=' (it names one generation, not a range or substring).");
		}

		GenerationFilter generation = value.Trim().ToLowerInvariant() switch {
			"0" or "gen0" => GenerationFilter.Gen0,
			"1" or "gen1" => GenerationFilter.Gen1,
			"2" or "gen2" => GenerationFilter.Gen2,
			"loh" => GenerationFilter.Loh,
			"poh" => GenerationFilter.Poh,
			"frozen" => GenerationFilter.Frozen,
			_ => throw new CliUsageException(
				$"Invalid --filter '{expression}': unrecognized generation '{value}'. Expected one of: " +
				"0, 1, 2, loh, poh, frozen."),
		};

		state.Set(FilterField.Generation, expression, () => state.Generation = generation);
	}

	private static void ApplyManagedThreadId(string expression, FilterOp op, string value, BuilderState state) {
		if (op != FilterOp.Equals) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': 'thread' supports only '=' (it names one thread, not a range or substring).");
		}

		if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int threadId)) {
			throw new CliUsageException($"Invalid --filter '{expression}': '{value}' is not a valid integer thread id.");
		}

		state.Set(FilterField.ManagedThreadId, expression, () => state.ManagedThreadId = threadId);
	}

	private static void ApplyOSThreadId(string expression, FilterOp op, string value, BuilderState state) {
		if (op != FilterOp.Equals) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': 'osthread' supports only '=' (it names one thread, not a range or substring).");
		}

		if (!uint.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint threadId)) {
			throw new CliUsageException($"Invalid --filter '{expression}': '{value}' is not a valid non-negative integer OS thread id.");
		}

		state.Set(FilterField.OSThreadId, expression, () => state.OSThreadId = threadId);
	}

	private static void ApplyHasException(string expression, FilterOp op, string value, BuilderState state) {
		if (op != FilterOp.Equals) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': 'exception' supports only '=' (it is a true/false flag).");
		}

		bool hasException = value.Trim().ToLowerInvariant() switch {
			"true" => true,
			"false" => false,
			_ => throw new CliUsageException(
				$"Invalid --filter '{expression}': '{value}' is not 'true' or 'false'."),
		};

		state.Set(FilterField.HasException, expression, () => state.HasException = hasException);
	}

	// ---- value parsing helpers -----------------------------------------------------------------

	/// <summary>Parses a byte size: a bare integer (bytes) or an integer followed by one of
	/// b/kb/mb/gb, case-insensitive, binary (1024-based) units. See the type-level remarks for why
	/// binary rather than decimal.</summary>
	private static ulong ParseByteSize(string expression, string value) {
		Match match = ByteSizePattern.Match(value.Trim());
		if (!match.Success) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': '{value}' is not a valid byte size. Expected an integer, " +
				"optionally followed by b, kb, mb or gb (e.g. 512, 4kb, 1mb, 2gb).");
		}

		if (!ulong.TryParse(match.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number)) {
			throw new CliUsageException($"Invalid --filter '{expression}': '{value}' is out of range.");
		}

		string unit = match.Groups["unit"].Success ? match.Groups["unit"].Value.ToLowerInvariant() : "b";
		ulong multiplier = unit switch {
			"b" => 1UL,
			"kb" => BytesPerKb,
			"mb" => BytesPerMb,
			"gb" => BytesPerGb,
			_ => 1UL, // unreachable given ByteSizePattern
		};

		try {
			return checked(number * multiplier);
		} catch (OverflowException) {
			throw new CliUsageException($"Invalid --filter '{expression}': '{value}' overflows a 64-bit byte count.");
		}
	}

	private static int ParseInt(string expression, string fieldName, string value) {
		if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) {
			throw new CliUsageException($"Invalid --filter '{expression}': '{value}' is not a valid integer for '{fieldName}'.");
		}

		return result;
	}

	/// <summary>Strict '&gt;' on an inclusive Min bound is Min = value + 1 (see type-level remarks).
	/// Guards the one input that cannot be represented: the maximum value, where +1 would silently
	/// wrap around to a bound that matches everything instead of nothing.</summary>
	private static ulong CheckedIncrement(string expression, ulong value) {
		try {
			return checked(value + 1);
		} catch (OverflowException) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': no byte size is strictly greater than the 64-bit maximum; use >= instead.");
		}
	}

	/// <summary>Strict '&lt;' on an inclusive Max bound is Max = value - 1. Guards zero, where -1
	/// would silently wrap to the maximum ulong and mean "no upper bound" -- the opposite of what
	/// was asked.</summary>
	private static ulong CheckedDecrement(string expression, ulong value) {
		try {
			return checked(value - 1);
		} catch (OverflowException) {
			throw new CliUsageException(
				$"Invalid --filter '{expression}': no byte size is strictly less than 0; did you mean >= 0 or a positive bound?");
		}
	}

	private static int CheckedIncrement(string expression, int value) {
		try {
			return checked(value + 1);
		} catch (OverflowException) {
			throw new CliUsageException($"Invalid --filter '{expression}': '{value}' is already the maximum representable count.");
		}
	}

	private static int CheckedDecrement(string expression, int value) {
		try {
			return checked(value - 1);
		} catch (OverflowException) {
			throw new CliUsageException($"Invalid --filter '{expression}': '{value}' is already the minimum representable count.");
		}
	}

	/// <summary>
	/// Accumulates parsed fields across every <c>--filter</c> expression before building the final
	/// <see cref="FilterSpec"/>, and rejects a second expression that targets a property already
	/// set by an earlier one -- see the type-level remarks on why this is checked per
	/// <see cref="FilterField"/> bit rather than per CLI field keyword.
	/// </summary>
	private sealed class BuilderState {
		private readonly HashSet<string> _rawExpressions = new();
		private FilterField _setFields = FilterField.None;

		public string? TypeName;
		public string? TypeNameRegex;
		public string? Module;
		public string? Text;
		public ulong? MinSize;
		public ulong? MaxSize;
		public int? MinCount;
		public int? MaxCount;
		public GenerationFilter? Generation;
		public int? ManagedThreadId;
		public uint? OSThreadId;
		public bool? HasException;

		public void Set(FilterField field, string expression, Action assign) {
			if ((_setFields & field) != FilterField.None) {
				string priorExpressions = string.Join(", ", _rawExpressions);
				throw new CliUsageException(
					$"Invalid --filter '{expression}': '{field}' is already set by an earlier --filter " +
					$"({priorExpressions}). Each field can only be set once; combine values into a " +
					"single expression where the grammar supports it, or remove the duplicate.");
			}

			assign();
			_setFields |= field;
			_rawExpressions.Add(expression);
		}

		public FilterSpec Build() => new FilterSpec {
			TypeName = TypeName,
			TypeNameRegex = TypeNameRegex,
			Module = Module,
			Text = Text,
			MinSize = MinSize,
			MaxSize = MaxSize,
			MinCount = MinCount,
			MaxCount = MaxCount,
			Generation = Generation,
			ManagedThreadId = ManagedThreadId,
			OSThreadId = OSThreadId,
			HasException = HasException,
		};
	}
}