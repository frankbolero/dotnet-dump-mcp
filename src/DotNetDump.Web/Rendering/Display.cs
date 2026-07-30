using System.Globalization;

namespace DotNetDump.Web.Rendering;

/// <summary>
/// A string split for middle truncation: the head clips, the tail never does.
/// </summary>
/// <remarks>
/// <para>
/// A .NET type name must lose its middle rather than its tail.
/// <c>System.Collections.Generic.Dictionary&lt;System.String,MyApp.Domain.CacheEntry&gt;+Entry[]</c>
/// and the same name without <c>+Entry[]</c> are different types, and plain
/// <c>text-overflow: ellipsis</c> cuts exactly the part that tells them apart — which is why the
/// Phase 1 exit criterion names that string specifically.
/// </para>
/// <para>
/// The split is by character count, but the *rendering* is not: CSS flexes the head and pins the
/// tail, so the visible truncation point follows the column width. That is the difference between
/// this and the design page's JavaScript, which truncated at a fixed 38/14 split and so cut short
/// names on a wide screen and long ones anyway on a narrow one.
/// </para>
/// </remarks>
/// <param name="Head">Everything but the tail. Clipped with an ellipsis when the column is narrow.</param>
/// <param name="Tail">The distinguishing end of the name. Never clipped.</param>
/// <param name="Full">The whole value, for the <c>title</c> attribute.</param>
public readonly record struct MiddleTruncated(string Head, string Tail, string Full) {
	/// <summary>
	/// How many trailing characters are protected. Sized to carry the shapes that actually
	/// distinguish .NET type names — <c>&gt;+Entry[]</c>, <c>[,]</c>, <c>+Enumerator</c> — without
	/// eating so much width that the head has nothing left on a narrow column.
	/// </summary>
	public const int TailLength = 16;

	public static MiddleTruncated From(string? value) {
		string full = value ?? string.Empty;
		return full.Length <= TailLength
			? new MiddleTruncated(full, string.Empty, full)
			: new MiddleTruncated(full[..^TailLength], full[^TailLength..], full);
	}
}

/// <summary>
/// Formatting the Razor views call directly, so no view model has to carry a pre-rendered string
/// and no template has to contain a conditional.
/// </summary>
/// <remarks>
/// Deliberately here rather than in <c>DotNetDump.Core.Formatting</c>: that layer renders Markdown,
/// JSON and TSV for the CLI and MCP front ends, and byte humanization is a choice this UI makes for
/// a human reading a table, not part of the data contract. The JSON route still reports exact bytes.
/// </remarks>
public static class Display {
	private static readonly string[] ByteUnits = ["KB", "MB", "GB", "TB", "PB"];

	/// <summary>Thousands-separated, invariant. Table numerics are right-aligned and monospaced.</summary>
	public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

	/// <summary>
	/// Human-readable size — <c>255.3 MB</c>. The exact byte count goes in a <c>title</c> alongside
	/// it (<see cref="Bytes"/>), because "255.3 MB" is what you scan a column for and
	/// "267,670,016 bytes" is what you quote in a bug report.
	/// </summary>
	public static string Size(long bytes) {
		if (bytes < 1024) {
			return Count(bytes) + " B";
		}

		double value = bytes;
		int unit = -1;
		do {
			value /= 1024;
			unit++;
		} while (value >= 1024 && unit < ByteUnits.Length - 1);

		return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + ByteUnits[unit];
	}

	/// <summary>The exact count, for the <c>title</c> that accompanies <see cref="Size"/>.</summary>
	public static string Bytes(long bytes) => Count(bytes) + " bytes";

	/// <summary>
	/// 16-character uppercase hex with no <c>0x</c> prefix — the <c>Addr</c> convention the Markdown
	/// and JSON formatters already use, so an address copied from any front end pastes into any
	/// other.
	/// </summary>
	public static string Address(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);
}