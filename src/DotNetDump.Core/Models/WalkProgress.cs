namespace DotNetDump.Core.Models;

/// <summary>
/// A snapshot of progress through a full-heap walk (CLI_DESIGN.md &#0167;6.3's five walk-scale
/// enumerations), reported via <see cref="System.IProgress{T}"/> at a throttled interval -- not once
/// per object; see <see cref="DotNetDump.Core.Utilities.WalkProgressThrottle"/>.
/// </summary>
/// <remarks>
/// A small immutable value type: one report is one allocation, and reports happen every few thousand
/// objects rather than every object, so the allocation rate stays negligible against the walk itself.
/// <para>
/// <see cref="TotalBytes"/> is the honest denominator this type offers. ClrMD's segment metadata
/// (<c>ClrHeap.Segments</c>) reports byte ranges, not object counts, so there is no real total to pair
/// with <see cref="ObjectsWalked"/> -- estimating one from an average or minimum object size would be
/// a guess dressed up as data, which SERVER.md's pending-state rules and CLI_DESIGN.md &#0167;10.4 both
/// rule out. <see cref="ObjectsWalked"/> is therefore a running count with no companion total; a
/// consumer that wants a completion fraction uses <see cref="FractionComplete"/>, derived from bytes.
/// </para>
/// </remarks>
/// <param name="ObjectsWalked">Running count of objects visited so far in this walk.</param>
/// <param name="BytesSeen">Running sum of <c>ClrObject.Size</c> for every object visited so far.</param>
/// <param name="TotalBytes">
/// The real denominator: the sum of every heap segment's object range length
/// (<c>ClrSegment.Length</c>, byte-equivalent), captured once at the start of the walk. Objects tile
/// a segment contiguously, so this is the number of bytes the walk will visit in total -- not a guess
/// and not a running maximum.
/// </param>
public readonly record struct WalkProgress(long ObjectsWalked, long BytesSeen, long TotalBytes) {
	/// <summary>
	/// <see cref="BytesSeen"/> divided by <see cref="TotalBytes"/>, or <c>null</c> when
	/// <see cref="TotalBytes"/> is not positive (an empty or unreadable heap) -- callers must not treat
	/// a missing fraction as zero progress.
	/// </summary>
	public double? FractionComplete => TotalBytes > 0 ? (double)BytesSeen / TotalBytes : null;
}