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

	/// <summary>
	/// Uppercase hex with no <c>0x</c> prefix and no padding — the convention
	/// <c>MarkdownFormatter.FormatThreads</c> already uses for an OS thread id via <c>{value:X}</c>.
	/// Deliberately unpadded, unlike <see cref="Address"/>: a native thread id is not a heap address,
	/// and padding it to 16 characters would manufacture leading zeros no other front end shows.
	/// </summary>
	public static string ThreadId(uint value) => value.ToString("X", CultureInfo.InvariantCulture);

	/// <summary>"Alive" or "Dead" — the same two words <c>MarkdownFormatter.FormatThreads</c> uses.</summary>
	public static string ThreadState(bool isAlive) => isAlive ? "Alive" : "Dead";

	/// <summary>
	/// A possibly-null value rendered as itself, or the literal <c>(none)</c> when absent — the same
	/// fallback <c>MarkdownFormatter.FormatThreads</c> uses for a thread with no exception in flight.
	/// </summary>
	public static string OrNone(string? value) => value ?? "(none)";

	/// <summary>
	/// Lock count rendered as a number, or the literal <c>unknown</c> when null. Per the comment
	/// on <c>ThreadStateInfo.LockCount</c>, a null means the runtime did not supply one, not that
	/// the count is zero — the DAC's "no data" sentinel must not be presented as a count.
	/// </summary>
	public static string LockCountOrUnknown(uint? value) => value.HasValue ? Count((long)value.Value) : "unknown";

	/// <summary>
	/// A list of strings joined with ", ", or a dash when empty. Used for rendering StateFlags
	/// in thread state views where an empty list is more readable as a dash than a blank cell.
	/// </summary>
	public static string JoinOrDash(IEnumerable<string> values) =>
		values.Any() ? string.Join(", ", values) : "-";

	/// <summary>
	/// Renders the owning thread of an exception, or "Heap" if it is heap-resident. A dump's
	/// in-flight exceptions always carry a managed thread id (<c>GetThreadExceptions</c> only ever
	/// produces <see cref="DotNetDump.Core.Models.ExceptionSource.ThreadCurrentException"/> or
	/// <see cref="DotNetDump.Core.Models.ExceptionSource.Heap"/>), so its presence alone
	/// distinguishes the two cases without needing the source itself.
	/// </summary>
	public static string ThreadOrHeap(int? managedThreadId, uint? osThreadId) =>
		managedThreadId.HasValue ? $"Thread {managedThreadId} ({ThreadId(osThreadId ?? 0)})" : "Heap";

	/// <summary>
	/// An object field's byte offset as hex, or a dash when the analyzer could not place it
	/// (<c>ObjectField.Offset == -1</c>) — the same convention
	/// <c>MarkdownFormatter.FormatObjectDetails</c> already uses.
	/// </summary>
	public static string FieldOffset(int value) => value != -1 ? value.ToString("X", CultureInfo.InvariantCulture) : "-";

	/// <summary>
	/// The DAC path/name plus its match status, exactly as <c>MarkdownFormatter.FormatInfo</c>
	/// composes it: an explicit <c>--dac</c> path if one was given, else the expected file name, else
	/// "&lt;unknown&gt;"; and "matched" unless an explicit path bypassed ClrMD's own check.
	/// </summary>
	public static string DacSummary(DotNetDump.Core.Models.DumpInfo info) {
		string description = info.ExplicitDacPath ?? info.ExpectedDacFileName ?? "<unknown>";
		string status = info.DacMatchVerified ? "matched" : "unverified -- explicit path bypasses ClrMD's match check";
		return $"{description} ({status})";
	}

	/// <summary>
	/// An 8-digit, zero-padded, 0x-prefixed hex string for a metadata token.
	/// </summary>
	public static string MetadataToken(int value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
}