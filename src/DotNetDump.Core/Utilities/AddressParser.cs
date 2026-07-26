using System;
using System.Globalization;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// Parses the hex addresses that agents and debuggers hand us. <see cref="NumberStyles.HexNumber"/>
/// on its own rejects the two forms that show up most often in practice: a <c>0x</c> prefix, and
/// the backtick WinDbg uses to split 64-bit values (<c>00000001`3A611F10</c>).
/// </summary>
public static class AddressParser {
	public static bool TryParse(string? text, out ulong address) {
		address = 0;
		if (string.IsNullOrWhiteSpace(text))
			return false;

		var span = text.AsSpan().Trim();

		// WinDbg splits 64-bit addresses with a backtick; strip it before anything else.
		if (span.IndexOf('`') >= 0) {
			return TryParse(text.Replace("`", string.Empty), out address);
		}

		if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			span = span.Slice(2);

		if (span.IsEmpty)
			return false;

		return ulong.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
	}

	/// <summary>
	/// Parses <paramref name="text"/> or throws <see cref="ArgumentException"/> with a message that
	/// tells the caller which forms are accepted.
	/// </summary>
	public static ulong Parse(string? text, string parameterName = "address") {
		if (TryParse(text, out ulong address))
			return address;

		throw new ArgumentException(
			$"Invalid hex address '{text}' for '{parameterName}'. Expected a hex value, " +
			"optionally prefixed with 0x (e.g. 13A611F10, 0x13A611F10, 000000013A611F10).");
	}
}