using System.Text.RegularExpressions;

using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Combines <see cref="FilterSpec.TypeName"/> and <see cref="FilterSpec.TypeNameRegex"/> — the two
/// independent flags under the <see cref="FilterField.AnyTypeName"/> composite — into a single
/// matcher. Per DATA_CONTRACT.md &#0167;2.3, when both are set they AND: a row must satisfy the
/// substring <em>and</em> the regex, not either.
/// </summary>
/// <remarks>
/// Regex compilation happens once, in <see cref="Create"/>, not once per row — the caller builds one
/// instance per analyzer call and reuses it across the whole filter pass. Compilation happens
/// deliberately alongside <see cref="FilterSpec.EnsureSupported"/>, before the cached computation
/// runs: a malformed pattern is a client error that should cost nothing, on a cold cache or a warm
/// one, exactly like an unsupported field. Letting a bad regex surface later — after a multi-second
/// heap walk, or as a bare <see cref="ArgumentException"/> thrown from inside a LINQ
/// <c>Where</c> deep in the pipeline — would make the same mistake in two different ways.
/// </remarks>
public sealed class TypeNameMatcher {
	private readonly string? _substring;
	private readonly Regex? _regex;

	private TypeNameMatcher(string? substring, Regex? regex) {
		_substring = substring;
		_regex = regex;
	}

	/// <summary>
	/// Builds a matcher for <paramref name="spec"/>. Throws <see cref="ArgumentException"/>
	/// immediately if <see cref="FilterSpec.TypeNameRegex"/> is set but not a valid .NET regular
	/// expression. The regex is matched case-sensitively — <see cref="FilterSpec.TypeName"/> is
	/// documented as case-insensitive, but the regex form gives the caller full control over the
	/// pattern, including case via an inline <c>(?i)</c> option, and silently forcing
	/// case-insensitivity would surprise a caller who wrote an anchored, case-sensitive pattern on
	/// purpose.
	/// </summary>
	public static TypeNameMatcher Create(FilterSpec spec) {
		Regex? regex = null;
		if (spec.TypeNameRegex != null) {
			try {
				regex = new Regex(spec.TypeNameRegex, RegexOptions.CultureInvariant);
			} catch (ArgumentException ex) {
				throw new ArgumentException(
					$"Invalid type name regular expression '{spec.TypeNameRegex}': {ex.Message}",
					nameof(FilterSpec.TypeNameRegex),
					ex);
			}
		}

		return new TypeNameMatcher(spec.TypeName, regex);
	}

	/// <summary>A matcher that accepts every type name — used where neither field is set.</summary>
	public static readonly TypeNameMatcher None = new(null, null);

	public bool Matches(string? typeName) {
		if (_substring != null && !FilterText.ContainsSubstring(typeName, _substring))
			return false;

		if (_regex != null && (typeName == null || !_regex.IsMatch(typeName)))
			return false;

		return true;
	}
}