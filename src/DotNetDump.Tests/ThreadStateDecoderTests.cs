using DotNetDump.Core.Utilities;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Tests;

public class ThreadStateDecoderTests {
	[Fact]
	public void NormalizeLockCount_TreatsAllBitsSetAsUnknown() {
		// The DAC reports 0xFFFFFFFF for "no data". Casting that to int yields -1, which reads as a
		// real count; it must surface as unknown instead.
		Assert.Null(ThreadStateDecoder.NormalizeLockCount(uint.MaxValue));
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(1u)]
	[InlineData(7u)]
	public void NormalizeLockCount_PassesThroughRealCounts(uint count) {
		Assert.Equal(count, ThreadStateDecoder.NormalizeLockCount(count));
	}

	[Fact]
	public void IsBackground_DetectsBackgroundFlag() {
		Assert.True(ThreadStateDecoder.IsBackground(ClrThreadState.TS_Background));
		Assert.False(ThreadStateDecoder.IsBackground(ClrThreadState.TS_Unstarted));
	}

	[Fact]
	public void IsThreadPoolThread_CoversWorkerAndCompletionPortThreads() {
		Assert.True(ThreadStateDecoder.IsThreadPoolThread(ClrThreadState.TS_TPWorkerThread));
		Assert.True(ThreadStateDecoder.IsThreadPoolThread(ClrThreadState.TS_CompletionPortThread));
		Assert.False(ThreadStateDecoder.IsThreadPoolThread(ClrThreadState.TS_Background));
	}

	[Theory]
	[InlineData(ClrThreadState.TS_Aborted)]
	[InlineData(ClrThreadState.TS_AbortRequested)]
	[InlineData(ClrThreadState.TS_AbortInitiated)]
	public void IsAborted_CoversEveryAbortStage(ClrThreadState state) {
		Assert.True(ThreadStateDecoder.IsAborted(state));
	}

	[Fact]
	public void IsAborted_FalseForAHealthyThread() {
		Assert.False(ThreadStateDecoder.IsAborted(ClrThreadState.TS_Background));
	}

	[Theory]
	[InlineData(ClrThreadState.TS_GCSuspendPending)]
	[InlineData(ClrThreadState.TS_UserSuspendPending)]
	[InlineData(ClrThreadState.TS_DebugSuspendPending)]
	public void IsSuspendPending_CoversEverySuspendReason(ClrThreadState state) {
		Assert.True(ThreadStateDecoder.IsSuspendPending(state));
	}

	[Theory]
	[InlineData(ClrThreadState.TS_InSTA, "STA")]
	[InlineData(ClrThreadState.TS_InMTA, "MTA")]
	[InlineData(ClrThreadState.TS_Background, "None")]
	public void ApartmentState_MapsTheApartmentFlags(ClrThreadState state, string expected) {
		Assert.Equal(expected, ThreadStateDecoder.ApartmentState(state));
	}

	[Fact]
	public void CombinedFlags_AreDecodedIndependently() {
		// A background threadpool worker with an abort requested: every fact must come through.
		var state = ClrThreadState.TS_Background
			| ClrThreadState.TS_TPWorkerThread
			| ClrThreadState.TS_AbortRequested;

		Assert.True(ThreadStateDecoder.IsBackground(state));
		Assert.True(ThreadStateDecoder.IsThreadPoolThread(state));
		Assert.True(ThreadStateDecoder.IsAborted(state));
		Assert.False(ThreadStateDecoder.IsUnstarted(state));
		Assert.False(ThreadStateDecoder.IsDead(state));
		Assert.Equal("None", ThreadStateDecoder.ApartmentState(state));
	}

	[Fact]
	public void FlagNames_ListsEverySetFlag() {
		var state = ClrThreadState.TS_Background | ClrThreadState.TS_Dead;
		var names = ThreadStateDecoder.FlagNames(state).ToList();

		Assert.Contains("TS_Background", names);
		Assert.Contains("TS_Dead", names);
		Assert.DoesNotContain("TS_Unstarted", names);
	}

	[Fact]
	public void FlagNames_IsEmptyForNoFlags() {
		Assert.Empty(ThreadStateDecoder.FlagNames(default));
	}
}