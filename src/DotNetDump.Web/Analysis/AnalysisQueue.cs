using System.Collections.Concurrent;

using DotNetDump.Core;
using DotNetDump.Core.Caching;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetDump.Web.Analysis;

/// <summary>
/// <see cref="IAnalysisQueue"/> over one dedicated, long-lived thread.
/// </summary>
/// <remarks>
/// <para>
/// A dedicated <see cref="Thread"/> rather than the thread pool: the guarantee wanted is "the same
/// OS thread, always", and a pool thread only guarantees "one at a time". ClrMD does not require
/// thread affinity today, but the DAC is a single-threaded interface and a pool-based
/// implementation would hide any future affinity requirement behind an intermittent failure.
/// </para>
/// <para>
/// An alternative — a pool of <c>ClrRuntime</c> instances over the same dump — was considered and
/// rejected in SERVER.md &#0167;3: it multiplies the DAC's memory footprint per instance and buys
/// parallelism that a single-user local tool has no use for.
/// </para>
/// </remarks>
public sealed class AnalysisQueue : IAnalysisQueue, IDisposable {
	/// <summary>How long <see cref="Dispose"/> waits for the worker to unwind before giving up on it.</summary>
	private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(10);

	private readonly BlockingCollection<WorkItem> _pending = new(new ConcurrentQueue<WorkItem>());
	private readonly CancellationTokenSource _shutdown = new();
	private readonly AnalysisSession _session;
	private readonly ILogger _log;
	private readonly Thread _worker;
	private readonly int _workerThreadId;

	private volatile RunningJob? _running;
	private bool _disposed;

	public AnalysisQueue(string dumpPath, IDumpContext context, IAnalysisCache cache, ILogger<AnalysisQueue>? log = null) {
		_session = new AnalysisSession(dumpPath, context, cache);
		_log = (ILogger?)log ?? NullLogger.Instance;

		_worker = new Thread(RunWorker) {
			Name = "dndump-analysis",
			// Background so a crash in host shutdown cannot leave the process alive on this thread.
			// Dispose still joins it first, so orderly shutdown does not rely on this.
			IsBackground = true,
		};

		// Read before Start(): ManagedThreadId is assigned at construction, so the re-entrancy check
		// below is correct even for an Enqueue that races the thread actually starting.
		_workerThreadId = _worker.ManagedThreadId;
		_worker.Start();
	}

	public int Depth => _pending.Count + (_running is null ? 0 : 1);

	public RunningJob? Running => _running;

	public Task<T> Enqueue<T>(Func<AnalysisSession, CancellationToken, T> work, string label, CancellationToken ct) {
		ArgumentNullException.ThrowIfNull(work);

		if (Environment.CurrentManagedThreadId == _workerThreadId) {
			// Re-entrant: work already on the analysis thread is composing another analyzer call.
			// Queueing it would deadlock — nothing can dequeue it until the caller returns — and the
			// caller is already on the one thread allowed to touch ClrMD, so run it here. This breaks
			// FIFO for the nested call only, which is the correct reading of FIFO: the outer job
			// still holds the worker for the whole of its own execution.
			try {
				return Task.FromResult(work(_session, _shutdown.Token));
			} catch (Exception ex) {
				// Faulted task, not a synchronous throw, so the nested path has the same shape as
				// the queued one for every caller.
				return Task.FromException<T>(ex);
			}
		}

		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		var item = new WorkItem(
			label,
			run: (session, token) => {
				try {
					completion.TrySetResult(work(session, token));
				} catch (Exception ex) {
					completion.TrySetException(ex);
				}
			},
			abandon: () => completion.TrySetCanceled());

		try {
			_pending.Add(item);
		} catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) {
			// CompleteAdding has run: the host is shutting down and this work will never execute.
			return Task.FromCanceled<T>(new CancellationToken(canceled: true));
		}

		return ct.CanBeCanceled ? AwaitAbandonable(completion.Task, ct) : completion.Task;
	}

	/// <summary>
	/// Waits for the job, but abandons only the wait if <paramref name="ct"/> fires. The job keeps
	/// running on the worker and its result still reaches the cache — the caller simply stops
	/// listening.
	/// </summary>
	private static async Task<T> AwaitAbandonable<T>(Task<T> job, CancellationToken ct) {
		try {
			return await job.WaitAsync(ct).ConfigureAwait(false);
		} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
			// Nobody is left to observe a failure of the abandoned job. Consume it here so it does
			// not surface later as an unobserved task exception attributed to unrelated code.
			job.ContinueWith(
				static abandoned => _ = abandoned.Exception,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
			throw;
		}
	}

	private void RunWorker() {
		try {
			foreach (var item in _pending.GetConsumingEnumerable(_shutdown.Token)) {
				_running = new RunningJob(item.Label, DateTimeOffset.UtcNow);
				try {
					item.Run(_session, _shutdown.Token);
				} catch (Exception ex) {
					// WorkItem.Run already routes the caller's exceptions into their task. Reaching
					// here means the queue itself is broken, not the analysis, so it must not kill
					// the worker and strand every subsequent request.
					_log.LogError(ex, "Analysis worker failed outside the job's own error handling.");
				} finally {
					_running = null;
				}
			}
		} catch (OperationCanceledException) {
			// Shutdown while blocked on the queue. Expected.
		} finally {
			AbandonPending();
		}
	}

	/// <summary>
	/// Fails every still-queued job as cancelled. Without this a caller awaiting work that shutdown
	/// discarded would wait forever rather than being told the server is going away.
	/// </summary>
	private void AbandonPending() {
		while (_pending.TryTake(out var item)) {
			item.Abandon();
		}
	}

	public void Dispose() {
		if (_disposed) {
			return;
		}
		_disposed = true;

		_pending.CompleteAdding();
		_shutdown.Cancel();

		if (!_worker.Join(ShutdownGrace)) {
			// A walk-scale job that ignores the shutdown token can outlast the grace period. The
			// thread is a background one, so the process still exits regardless — but the worker may
			// still be inside ClrMD, so nothing here may touch the session or the queue it is
			// reading. Say so and leave them alone.
			_log.LogWarning("Analysis worker did not stop within {Grace}; leaving the dump open for process exit.", ShutdownGrace);
			return;
		}

		AbandonPending();
		_pending.Dispose();
		_shutdown.Dispose();
		_session.Dispose();
	}

	private sealed class WorkItem(string label, Action<AnalysisSession, CancellationToken> run, Action abandon) {
		public string Label { get; } = label;

		public void Run(AnalysisSession session, CancellationToken ct) => run(session, ct);

		public void Abandon() => abandon();
	}
}