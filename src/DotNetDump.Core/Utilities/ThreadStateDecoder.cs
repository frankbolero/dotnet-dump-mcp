using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// Decodes <see cref="ClrThreadState"/> into the individual facts SOS's <c>threadstate</c> reports.
/// Kept as a pure function over the flags enum so it is testable without a dump.
/// </summary>
public static class ThreadStateDecoder {
	/// <summary>
	/// <see cref="ClrThread.LockCount"/> returns this when the DAC cannot supply a count. Reporting
	/// it verbatim (or casting it to <see cref="int"/>, which yields a plausible-looking -1) claims
	/// knowledge we do not have.
	/// </summary>
	public const uint UnknownLockCount = uint.MaxValue;

	public static uint? NormalizeLockCount(uint lockCount) =>
		lockCount == UnknownLockCount ? null : lockCount;

	public static bool IsBackground(ClrThreadState state) => state.HasFlag(ClrThreadState.TS_Background);

	public static bool IsUnstarted(ClrThreadState state) => state.HasFlag(ClrThreadState.TS_Unstarted);

	public static bool IsDead(ClrThreadState state) => state.HasFlag(ClrThreadState.TS_Dead);

	public static bool IsThreadPoolThread(ClrThreadState state) =>
		state.HasFlag(ClrThreadState.TS_TPWorkerThread) || state.HasFlag(ClrThreadState.TS_CompletionPortThread);

	/// <summary>True for an abort in any stage: requested, initiated, or completed.</summary>
	public static bool IsAborted(ClrThreadState state) =>
		state.HasFlag(ClrThreadState.TS_Aborted) ||
		state.HasFlag(ClrThreadState.TS_AbortRequested) ||
		state.HasFlag(ClrThreadState.TS_AbortInitiated);

	public static bool IsSuspendPending(ClrThreadState state) =>
		state.HasFlag(ClrThreadState.TS_GCSuspendPending) ||
		state.HasFlag(ClrThreadState.TS_UserSuspendPending) ||
		state.HasFlag(ClrThreadState.TS_DebugSuspendPending);

	public static string ApartmentState(ClrThreadState state) {
		if (state.HasFlag(ClrThreadState.TS_InSTA)) return "STA";
		if (state.HasFlag(ClrThreadState.TS_InMTA)) return "MTA";
		return "None";
	}

	/// <summary>
	/// The raw flag names, for callers that want the unabridged state the way SOS prints it.
	/// </summary>
	public static IEnumerable<string> FlagNames(ClrThreadState state) {
		foreach (ClrThreadState flag in Enum.GetValues<ClrThreadState>()) {
			if (flag != 0 && state.HasFlag(flag))
				yield return flag.ToString();
		}
	}
}
