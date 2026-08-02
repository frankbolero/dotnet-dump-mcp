using System.Globalization;

namespace DotNetDump.Core.Trees;

/// <summary>
/// Byte-size humanization for <see cref="Models.TreeNode.Detail"/>. Unlike a list view's JSON
/// envelope -- which reports an analyzer model's exact numeric fields and leaves humanizing them to
/// <c>DotNetDump.Web.Rendering.Display</c> -- a <see cref="Models.TreeNode"/> has no numeric sibling
/// field for its <see cref="Models.TreeNode.Detail"/> text to duplicate: DATA_CONTRACT.md &#0167;4.1
/// specs <c>Detail</c> itself as ready-to-read secondary text ("size, count, address"), so producing
/// that text is part of building the tree, not a rendering choice layered on afterwards. Shared
/// across <c>DotNetDump.Core/Trees/</c> so each of Phase 5's four builders formats a byte count the
/// same way rather than reimplementing it once each.
/// </summary>
internal static class TreeFormat {
	private static readonly string[] ByteUnits = ["KB", "MB", "GB", "TB", "PB"];

	public static string Size(long bytes) {
		if (bytes < 1024) {
			return bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
		}

		double value = bytes;
		int unit = -1;
		do {
			value /= 1024;
			unit++;
		} while (value >= 1024 && unit < ByteUnits.Length - 1);

		return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + ByteUnits[unit];
	}

	/// <summary>Zero-padded 16-digit uppercase hex, matching <c>DotNetDump.Web.Rendering.Display</c>'s
	/// own <c>Address</c> so an address reads identically whether it appears in a table cell or in a
	/// tree node's <see cref="Models.TreeNode.Detail"/>. Distinct from an opaque node
	/// <see cref="Models.TreeNode.Id"/>, which is not display text and is not padded.</summary>
	public static string Address(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);
}