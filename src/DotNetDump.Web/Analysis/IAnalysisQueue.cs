namespace DotNetDump.Web.Analysis;

/// <summary>
/// The single serialized analysis worker (SERVER.md &#0167;3). Every access to <c>ClrRuntime</c>,
/// <c>ClrHeap</c> or anything derived from them goes through here, because ClrMD makes no
/// concurrency guarantee and the DAC underneath it is a single-threaded interface. A web server is
/// concurrent by default — two browser tabs, or one tab whose filter box fires while a tree
/// expands, is enough to corrupt state or crash the DAC.
/// </summary>
/// <remarks>
/// The failure this prevents does not present as an obvious bug, which is why the queue exists in
/// Phase 2 before any real handler does rather than being retrofitted later.
/// </remarks>
public interface IAnalysisQueue {
	/// <summary>
	/// Runs <paramref name="work"/> on the single analysis thread and returns its result. Order is
	/// FIFO.
	/// </summary>
	/// <param name="work">
	/// The analysis to run. Receives the one <see cref="AnalysisSession"/> — the only way to reach an
	/// analyzer — and the queue's shutdown token, <em>not</em> <paramref name="ct"/>.
	/// </param>
	/// <param name="label">
	/// What the pending-state UI displays while this job runs: "walking heap", "resolving roots".
	/// </param>
	/// <param name="ct">
	/// Cancels the <em>wait</em>, not the work. A browser navigating away abandons the returned task
	/// with an <see cref="OperationCanceledException"/>; the job still runs to completion so its
	/// cache entry lands. Abandoning a 12-second walk at second 11 because a user clicked elsewhere
	/// is the wrong trade (SERVER.md &#0167;3).
	/// </param>
	Task<T> Enqueue<T>(Func<AnalysisSession, CancellationToken, T> work, string label, CancellationToken ct);

	/// <summary>
	/// Jobs waiting plus the one running. Non-zero means a request submitting work now will wait,
	/// which is what the depth-aware pending state of SERVER.md &#0167;3.1 keys off.
	/// </summary>
	int Depth { get; }

	/// <summary>The job occupying the worker, or <c>null</c> when it is idle.</summary>
	RunningJob? Running { get; }
}

/// <summary>The job currently on the analysis thread: what it is and how long it has been going.</summary>
public sealed record RunningJob(string Label, DateTimeOffset StartedAt) {
	public TimeSpan Elapsed => DateTimeOffset.UtcNow - StartedAt;
}