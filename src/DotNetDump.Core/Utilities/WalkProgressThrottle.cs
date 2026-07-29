using System;
using System.Linq;

using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// Throttles <see cref="WalkProgress"/> reporting for a full-heap walk (CLI_DESIGN.md &#0167;6.3's
/// five walk-scale enumerations) so the walk pays for progress reporting only occasionally, not once
/// per object -- these walks cover millions of objects, and per-object reporting would make the
/// progress hook itself the performance problem.
/// </summary>
/// <remarks>
/// Throttled purely by object count, not elapsed time: a per-object clock read would itself be the
/// per-object cost this type exists to avoid. <see cref="Record"/> checks a modulo on an incrementing
/// counter, which is cheap enough to pay on every object once a progress sink is present.
/// <para>
/// When no sink is supplied, <see cref="Record"/> is a single null check and returns -- no counters
/// are touched, nothing is allocated, and no clock is read, so a caller passing <c>null</c> (as the
/// CLI always does per DATA_CONTRACT.md &#0167;5) pays as little as this design can make it pay.
/// </para>
/// <para>
/// Extracted from the five walk loops so the throttling policy is unit-testable on its own, rather
/// than re-implemented (and re-verified) five times inline.
/// </para>
/// </remarks>
public sealed class WalkProgressThrottle {
	/// <summary>
	/// Objects between reports. Chosen, not measured: frequent enough that a multi-second walk shows
	/// several updates, coarse enough that the reporting overhead stays well under the cost of walking
	/// that many objects. Revisit with a real measurement before relying on this number for anything
	/// stronger than "occasionally".
	/// </summary>
	public const int DefaultReportIntervalObjects = 10_000;

	private readonly IProgress<WalkProgress>? _progress;
	private readonly long _totalBytes;
	private readonly int _intervalObjects;

	private long _objectsWalked;
	private long _bytesSeen;

	/// <param name="progress">
	/// Where reports go, or <c>null</c> to disable reporting entirely (the near-zero-cost path).
	/// </param>
	/// <param name="totalBytes">
	/// The real denominator for this walk -- see <see cref="WalkProgress.TotalBytes"/> -- captured
	/// once before the walk starts.
	/// </param>
	/// <param name="intervalObjects">
	/// Objects between reports. Defaults to <see cref="DefaultReportIntervalObjects"/>; a
	/// non-positive value is treated as the default rather than throwing, since a throttle interval
	/// of zero or less has no sensible meaning.
	/// </param>
	public WalkProgressThrottle(IProgress<WalkProgress>? progress, long totalBytes, int intervalObjects = DefaultReportIntervalObjects) {
		_progress = progress;
		_totalBytes = totalBytes;
		_intervalObjects = intervalObjects > 0 ? intervalObjects : DefaultReportIntervalObjects;
	}

	/// <summary>
	/// Builds a throttle for a walk over <paramref name="heap"/>, deriving <see cref="WalkProgress.TotalBytes"/>
	/// from <c>ClrHeap.Segments</c> -- the sum of every segment's object-range length, the real
	/// denominator described on <see cref="WalkProgress"/>.
	/// </summary>
	/// <remarks>
	/// The segment sum is skipped entirely when <paramref name="progress"/> is <c>null</c>: nothing
	/// downstream will read <see cref="WalkProgress.TotalBytes"/>, so there is no reason to pay for it.
	/// </remarks>
	public static WalkProgressThrottle ForHeap(ClrHeap heap, IProgress<WalkProgress>? progress, int intervalObjects = DefaultReportIntervalObjects) {
		long totalBytes = progress != null ? heap.Segments.Sum(s => (long)s.Length) : 0;
		return new WalkProgressThrottle(progress, totalBytes, intervalObjects);
	}

	/// <summary>
	/// Records one more object visited. Reports at most once every <see cref="DefaultReportIntervalObjects"/>
	/// (or the constructor override's) objects, and only when a progress sink was supplied.
	/// </summary>
	/// <param name="objectSize">The visited object's size in bytes, added to the running byte total.</param>
	public void Record(long objectSize) {
		if (_progress == null)
			return;

		_objectsWalked++;
		_bytesSeen += objectSize;

		if (_objectsWalked % _intervalObjects == 0)
			_progress.Report(new WalkProgress(_objectsWalked, _bytesSeen, _totalBytes));
	}

	/// <summary>
	/// Reports the true final counts, whether or not the last batch landed on the interval boundary.
	/// Call once after the walk completes. A no-op when no progress sink was supplied.
	/// </summary>
	public void ReportFinal() {
		if (_progress == null)
			return;

		_progress.Report(new WalkProgress(_objectsWalked, _bytesSeen, _totalBytes));
	}
}