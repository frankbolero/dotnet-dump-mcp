using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using DotNetDump.Core.Caching;
using DotNetDump.Web.Analysis;

namespace DotNetDump.Tests;

/// <summary>
/// Behavioural tests for <see cref="AnalysisQueue"/>, the single serialized analysis worker of
/// SERVER.md &#0167;3.
/// </summary>
/// <remarks>
/// <para>
/// The property these tests exist for is that <c>ClrRuntime</c> and <c>ClrHeap</c> are never touched
/// concurrently. That failure does not present as an obvious bug -- it presents as DAC corruption or
/// a crash under load -- so it has to be proven by observing execution rather than by reading the
/// implementation. Hence <see cref="ConcurrentSubmissions_NeverOverlapOnTheWorker"/> watches jobs
/// enter and leave rather than asserting that a queue exists.
/// </para>
/// <para>
/// No test here touches ClrMD. <see cref="NoDumpContext"/> stands in for the dump and the work
/// delegates ignore the session, so the queue's threading is measured on its own.
/// </para>
/// <para>
/// Every wait is bounded by <see cref="WaitLimit"/>. A broken implementation must fail the test
/// rather than hang the suite, which is why no wait here is unbounded and no assertion is written as
/// a bare <c>Thread.Sleep</c> followed by a check.
/// </para>
/// </remarks>
public class AnalysisQueueTests {
	/// <summary>
	/// Bound on every wait. Generous, because it is only ever reached when the implementation is
	/// broken -- a correct queue satisfies each wait in microseconds to milliseconds, so a long limit
	/// costs nothing on a passing run and prevents a loaded CI machine from failing spuriously.
	/// </summary>
	private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

	/// <summary>The dump path handed to the session; nothing opens it, so it need not exist.</summary>
	private const string FakeDumpPath = "/nonexistent/analysis-queue-tests.core";

	private static AnalysisQueue CreateQueue() =>
		new(FakeDumpPath, new NoDumpContext(), NullAnalysisCache.Instance);

	#region 1. Serialized execution -- the headline property

	/// <summary>
	/// Many submissions racing from many threads never overlap on the worker.
	/// </summary>
	/// <remarks>
	/// This is the outstanding half of the Phase 2 exit criterion: "two concurrent requests are
	/// provably serialized through the queue (test, not inspection)". It is proven twice over, because
	/// each proof covers a hole in the other:
	/// <list type="bullet">
	/// <item>An occupancy counter incremented on entry and decremented on exit, whose peak must never
	/// exceed 1. This catches overlap that happens at all.</item>
	/// <item>Recorded enter/exit timestamps, which after sorting must not intersect. This catches
	/// overlap that the counter could miss if the increment and decrement were themselves
	/// interleaved.</item>
	/// </list>
	/// The jobs hold the worker for a few milliseconds each so that an implementation which dispatched
	/// to the thread pool would reliably be caught. Note the direction of that sensitivity: the sleep
	/// makes a *broken* implementation fail more reliably, and cannot make a *correct* one fail, since
	/// both assertions are exact rather than timing-derived.
	/// </remarks>
	[Fact]
	public async Task ConcurrentSubmissions_NeverOverlapOnTheWorker() {
		const int Submitters = 24;

		using var queue = CreateQueue();
		var occupancy = new OccupancyMeter();
		var intervals = new ConcurrentBag<(long Enter, long Exit)>();

		// All submitters are released at once so the Enqueue calls genuinely race each other, rather
		// than trickling in one at a time and being serialized by the submission side.
		using var startLine = new ManualResetEventSlim();

		var submissions = Enumerable.Range(0, Submitters).Select(i => Task.Run(async () => {
			Assert.True(startLine.Wait(WaitLimit));

			return await queue.Enqueue((_, _) => {
				occupancy.Enter();
				long enter = Stopwatch.GetTimestamp();

				// Wide enough that an unserialized worker would overlap on any real machine.
				Thread.Sleep(3);

				long exit = Stopwatch.GetTimestamp();
				intervals.Add((enter, exit));
				occupancy.Exit();
				return i;
			}, $"job-{i}", CancellationToken.None).WaitAsync(WaitLimit);
		})).ToArray();

		startLine.Set();
		int[] results = await Task.WhenAll(submissions).WaitAsync(WaitLimit);

		Assert.Equal(Submitters, results.Length);
		Assert.Equal(Enumerable.Range(0, Submitters), results.OrderBy(r => r));

		Assert.Equal(1, occupancy.Peak);

		var ordered = intervals.OrderBy(x => x.Enter).ToArray();
		Assert.Equal(Submitters, ordered.Length);
		for (int i = 1; i < ordered.Length; i++) {
			Assert.True(
				ordered[i].Enter >= ordered[i - 1].Exit,
				$"Job intervals intersect: [{ordered[i - 1].Enter}, {ordered[i - 1].Exit}] overlaps [{ordered[i].Enter}, {ordered[i].Exit}].");
		}
	}

	/// <summary>
	/// Tracks how many jobs are inside the worker at once. Lock-free so that the meter itself cannot
	/// serialize what it is measuring and manufacture a passing result.
	/// </summary>
	private sealed class OccupancyMeter {
		private int _current;
		private int _peak;

		public int Peak => Volatile.Read(ref _peak);

		public void Enter() {
			int now = Interlocked.Increment(ref _current);
			int seen = Volatile.Read(ref _peak);
			while (now > seen) {
				int prior = Interlocked.CompareExchange(ref _peak, now, seen);
				if (prior == seen) {
					return;
				}

				seen = prior;
			}
		}

		public void Exit() => Interlocked.Decrement(ref _current);
	}

	#endregion

	#region 2. FIFO

	/// <summary>
	/// Work submitted from one thread runs in submission order. Single-threaded submission is the
	/// case where FIFO is actually meaningful -- with concurrent submitters there is no defined
	/// arrival order to preserve.
	/// </summary>
	[Fact]
	public async Task SingleThreadedSubmissions_RunInFifoOrder() {
		const int Jobs = 30;

		using var queue = CreateQueue();
		var order = new ConcurrentQueue<int>();

		// The first job blocks the worker until every other job is queued behind it. Without this the
		// queue could drain as fast as it fills and the test would pass without FIFO ever being
		// exercised: only one item would ever be pending at a time.
		using var release = new ManualResetEventSlim();
		using var occupied = new ManualResetEventSlim();

		var blocker = queue.Enqueue((_, _) => {
			occupied.Set();
			Assert.True(release.Wait(WaitLimit));
			order.Enqueue(0);
			return 0;
		}, "blocker", CancellationToken.None);

		Assert.True(occupied.Wait(WaitLimit));

		var rest = new List<Task<int>>();
		for (int i = 1; i < Jobs; i++) {
			int captured = i;
			rest.Add(queue.Enqueue((_, _) => {
				order.Enqueue(captured);
				return captured;
			}, $"job-{captured}", CancellationToken.None));
		}

		release.Set();
		await blocker.WaitAsync(WaitLimit);
		await Task.WhenAll(rest).WaitAsync(WaitLimit);

		Assert.Equal(Enumerable.Range(0, Jobs), order);
	}

	#endregion

	#region 3. Thread affinity

	/// <summary>
	/// Every job runs on one and the same thread, and that thread is not the caller's. The dedicated
	/// thread -- rather than a pool thread that would only give "one at a time" -- is the whole point
	/// of the class doc's first paragraph: the DAC is a single-threaded interface, so an
	/// implementation that satisfies serialization but not affinity would hide a future affinity
	/// requirement behind an intermittent failure.
	/// </summary>
	[Fact]
	public async Task EveryJob_RunsOnTheSameNonCallingThread() {
		using var queue = CreateQueue();

		int callerThreadId = Environment.CurrentManagedThreadId;
		var observed = new ConcurrentBag<int>();
		var sessions = new ConcurrentBag<AnalysisSession>();

		var jobs = new List<Task<int>>();
		for (int i = 0; i < 12; i++) {
			jobs.Add(queue.Enqueue((session, _) => {
				observed.Add(Environment.CurrentManagedThreadId);
				sessions.Add(session);
				return Environment.CurrentManagedThreadId;
			}, $"job-{i}", CancellationToken.None));
		}

		await Task.WhenAll(jobs).WaitAsync(WaitLimit);

		int workerThreadId = Assert.Single(observed.Distinct());
		Assert.NotEqual(callerThreadId, workerThreadId);

		// The same session instance throughout: the queue owns exactly one, and it carries the dump
		// path it was constructed with.
		var session = Assert.Single(sessions.Distinct());
		Assert.Equal(FakeDumpPath, session.DumpPath);
	}

	/// <summary>
	/// Submitting from a pool thread is still not the analysis thread. This guards the re-entrancy
	/// check in <c>Enqueue</c> from being satisfied by accident -- if the worker were a pool thread,
	/// a pool-thread caller could collide with it.
	/// </summary>
	[Fact]
	public async Task WorkerThread_IsNotAPoolThread() {
		using var queue = CreateQueue();

		// Task.Run unwraps the inner task, so this awaits the job rather than the submission.
		bool workerIsPoolThread = await Task.Run(() =>
			queue.Enqueue((_, _) => Thread.CurrentThread.IsThreadPoolThread, "probe", CancellationToken.None))
			.WaitAsync(WaitLimit);

		Assert.False(workerIsPoolThread);
	}

	#endregion

	#region 4. Cancellation abandons the wait, not the work

	/// <summary>
	/// Cancelling the token passed to <c>Enqueue</c> cancels the returned task while the work still
	/// runs to completion.
	/// </summary>
	/// <remarks>
	/// <para>
	/// SERVER.md &#0167;3: "abandoning a 12-second walk at second 11 because a user clicked elsewhere is the
	/// wrong trade". The job must keep running so its cache entry lands.
	/// </para>
	/// <para>
	/// The ordering is structural, not timed. The worker is held by a blocking job, so the abandoned
	/// job provably cannot have started before the cancellation was observed; the gate is only
	/// released afterwards. The subsequent completion signal therefore proves the work ran *after*
	/// its waiter had already given up.
	/// </para>
	/// </remarks>
	[Fact]
	public async Task Cancellation_AbandonsTheWaitButTheWorkStillCompletes() {
		using var queue = CreateQueue();

		using var occupied = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var workRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool workSawCancelledToken = true;

		var blocker = queue.Enqueue((_, _) => {
			occupied.Set();
			Assert.True(release.Wait(WaitLimit));
			return 0;
		}, "blocker", CancellationToken.None);

		Assert.True(occupied.Wait(WaitLimit));

		using var cts = new CancellationTokenSource();
		var abandoned = queue.Enqueue((_, workToken) => {
			// The work receives the queue's shutdown token, never the caller's -- that is what makes
			// cancellation abandon the wait rather than the work.
			workSawCancelledToken = workToken.IsCancellationRequested;
			workRan.TrySetResult(true);
			return 42;
		}, "abandoned", cts.Token);

		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.WaitAsync(WaitLimit));
		Assert.True(abandoned.IsCanceled);

		// Only now can the abandoned job possibly run.
		Assert.False(workRan.Task.IsCompleted);
		release.Set();

		Assert.True(await workRan.Task.WaitAsync(WaitLimit));
		Assert.False(workSawCancelledToken);

		await blocker.WaitAsync(WaitLimit);
	}

	/// <summary>
	/// A token that can never be cancelled takes the fast path in <c>Enqueue</c> (the completion task
	/// is returned directly, unwrapped). It must still deliver the result.
	/// </summary>
	[Fact]
	public async Task NonCancellableToken_StillDeliversTheResult() {
		using var queue = CreateQueue();

		int result = await queue.Enqueue((_, _) => 7, "plain", CancellationToken.None).WaitAsync(WaitLimit);

		Assert.Equal(7, result);
	}

	/// <summary>An already-cancelled token abandons the wait immediately rather than waiting first.</summary>
	[Fact]
	public async Task AlreadyCancelledToken_AbandonsTheWaitImmediately() {
		using var queue = CreateQueue();

		using var occupied = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var blocker = queue.Enqueue((_, _) => {
			occupied.Set();
			Assert.True(release.Wait(WaitLimit));
			return 0;
		}, "blocker", CancellationToken.None);

		Assert.True(occupied.Wait(WaitLimit));

		var ran = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var abandoned = queue.Enqueue((_, _) => {
			ran.TrySetResult(true);
			return 1;
		}, "already-cancelled", new CancellationToken(canceled: true));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.WaitAsync(WaitLimit));

		release.Set();

		// Still runs: the token cancelled the wait, and the work was already queued.
		Assert.True(await ran.Task.WaitAsync(WaitLimit));
		await blocker.WaitAsync(WaitLimit);
	}

	#endregion

	#region 5. Abandoned failures are observed

	/// <summary>
	/// An abandoned job that throws produces no unobserved task exception.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the reason <c>AwaitAbandonable</c> attaches an <c>OnlyOnFaulted</c> continuation. Once
	/// the wait has been abandoned nobody is left to observe the job's failure, and the exception
	/// would otherwise surface from the finalizer thread through
	/// <see cref="TaskScheduler.UnobservedTaskException"/> -- attributed to whatever unrelated code
	/// happened to be running, which is a genuinely awful diagnostic experience.
	/// </para>
	/// <para>
	/// The test is inherently timing- and GC-sensitive, and it is asymmetric: it cannot fail
	/// spuriously (only an exception carrying this run's unique sentinel is counted, so no other test
	/// in the assembly can trip it), but it *can* pass spuriously if the faulted task is not collected
	/// and finalized within the collection loop. Two things are done to narrow that: the enqueue/await
	/// happens in a separate non-inlined method so no local keeps the task alive, and a drain job is
	/// pushed through afterwards so the worker's <c>foreach</c> loop variable no longer references the
	/// abandoned work item's closure.
	/// </para>
	/// <para>
	/// This was verified to actually fail when the continuation is deleted from
	/// <c>AwaitAbandonable</c>, on all three target frameworks. Without that check it would be a test
	/// that always passes.
	/// </para>
	/// </remarks>
	[Fact]
	public async Task AbandonedJobThatThrows_ProducesNoUnobservedTaskException() {
		string sentinel = "analysis-queue-unobserved-" + Guid.NewGuid().ToString("N");
		var unobserved = new ConcurrentBag<string>();

		void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) {
			foreach (var inner in e.Exception.InnerExceptions) {
				if (inner.Message == sentinel) {
					unobserved.Add(inner.Message);
				}
			}

			// Deliberately not calling e.SetObserved(): this handler only records. Marking observed
			// here would change the behaviour of any unrelated concurrently running test.
		}

		TaskScheduler.UnobservedTaskException += OnUnobserved;
		try {
			await AbandonAThrowingJob(sentinel);

			// Two rounds: the first finalizes the TaskExceptionHolder (which is what raises the event),
			// the second collects what the finalizer released.
			for (int i = 0; i < 3; i++) {
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}

			GC.Collect();
			GC.WaitForPendingFinalizers();

			Assert.Empty(unobserved);
		} finally {
			TaskScheduler.UnobservedTaskException -= OnUnobserved;
		}
	}

	/// <summary>
	/// Abandons a job that then throws, keeping every reference to the faulted task inside this frame
	/// so that it is unreachable by the time the caller collects.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async Task AbandonAThrowingJob(string sentinel) {
		using var queue = CreateQueue();

		using var occupied = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		using var threw = new ManualResetEventSlim();

		var blocker = queue.Enqueue((_, _) => {
			occupied.Set();
			Assert.True(release.Wait(WaitLimit));
			return 0;
		}, "blocker", CancellationToken.None);

		Assert.True(occupied.Wait(WaitLimit));

		using var cts = new CancellationTokenSource();
		var abandoned = queue.Enqueue<int>((_, _) => {
			threw.Set();
			throw new InvalidOperationException(sentinel);
		}, "abandoned-throwing", cts.Token);

		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.WaitAsync(WaitLimit));

		release.Set();
		Assert.True(threw.Wait(WaitLimit));
		await blocker.WaitAsync(WaitLimit);

		// Push a further job through so the worker's foreach variable stops referencing the abandoned
		// item's closure, which would otherwise keep its TaskCompletionSource -- and so the faulted
		// task -- reachable and make the collection below a no-op.
		await queue.Enqueue((_, _) => 0, "drain", CancellationToken.None).WaitAsync(WaitLimit);
	}

	#endregion

	#region 6. Re-entrancy

	/// <summary>
	/// <c>Enqueue</c> called from work already on the analysis thread runs inline instead of
	/// deadlocking. Queueing it could never complete: nothing can dequeue the nested item until the
	/// outer job returns, and the outer job is waiting on the nested one.
	/// </summary>
	[Fact]
	public async Task NestedEnqueue_RunsInlineWithoutDeadlocking() {
		using var queue = CreateQueue();

		int outerThreadId = 0;
		int nestedThreadId = 0;
		bool nestedCompletedSynchronously = false;

		int result = await queue.Enqueue((_, _) => {
			outerThreadId = Environment.CurrentManagedThreadId;

			var nested = queue.Enqueue((_, _) => {
				nestedThreadId = Environment.CurrentManagedThreadId;
				return 41;
			}, "nested", CancellationToken.None);

			// Already finished by the time Enqueue returned: it ran on this stack, not on the queue.
			nestedCompletedSynchronously = nested.IsCompletedSuccessfully;
			return nested.Result + 1;
		}, "outer", CancellationToken.None).WaitAsync(WaitLimit);

		Assert.Equal(42, result);
		Assert.True(nestedCompletedSynchronously);
		Assert.Equal(outerThreadId, nestedThreadId);
	}

	/// <summary>
	/// The inline path returns a faulted task rather than throwing synchronously. The doc comment on
	/// that branch requires both paths to have the same shape, so a caller that composes analyzer
	/// calls need not know whether it is nested.
	/// </summary>
	[Fact]
	public async Task NestedEnqueue_ThatThrows_ReturnsAFaultedTaskRatherThanThrowingSynchronously() {
		using var queue = CreateQueue();

		Task<int>? nested = null;
		Exception? thrownSynchronously = null;

		await queue.Enqueue((_, _) => {
			try {
				nested = queue.Enqueue<int>(
					(_, _) => throw new InvalidOperationException("nested boom"),
					"nested-throwing",
					CancellationToken.None);
			} catch (Exception ex) {
				thrownSynchronously = ex;
			}

			return 0;
		}, "outer", CancellationToken.None).WaitAsync(WaitLimit);

		Assert.Null(thrownSynchronously);
		Assert.NotNull(nested);
		Assert.True(nested!.IsFaulted);

		// Reading Exception also observes it, so this test cannot leak an unobserved exception into
		// the assembly and disturb AbandonedJobThatThrows_ProducesNoUnobservedTaskException.
		var error = Assert.IsType<InvalidOperationException>(nested.Exception!.InnerException);
		Assert.Equal("nested boom", error.Message);
	}

	/// <summary>Nested work still sees the one session, not a second one built for the occasion.</summary>
	[Fact]
	public async Task NestedEnqueue_SeesTheSameSession() {
		using var queue = CreateQueue();

		AnalysisSession? outerSession = null;
		AnalysisSession? nestedSession = null;

		await queue.Enqueue((session, _) => {
			outerSession = session;
			return queue.Enqueue((inner, _) => {
				nestedSession = inner;
				return 0;
			}, "nested", CancellationToken.None).Result;
		}, "outer", CancellationToken.None).WaitAsync(WaitLimit);

		Assert.NotNull(outerSession);
		Assert.Same(outerSession, nestedSession);
	}

	#endregion

	#region 7. Failure isolation

	/// <summary>A job's exception reaches the caller's task unwrapped.</summary>
	[Fact]
	public async Task JobException_PropagatesIntoTheReturnedTask() {
		using var queue = CreateQueue();

		var error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => queue.Enqueue<int>(
				(_, _) => throw new InvalidOperationException("boom"),
				"throwing",
				CancellationToken.None).WaitAsync(WaitLimit));

		Assert.Equal("boom", error.Message);
	}

	/// <summary>
	/// A throwing job does not kill the worker. If it did, every subsequent request for the life of
	/// the process would hang -- the worst possible failure mode for a server whose whole job is to
	/// funnel work through one thread.
	/// </summary>
	[Fact]
	public async Task ThrowingJob_DoesNotKillTheWorker() {
		using var queue = CreateQueue();

		for (int i = 0; i < 3; i++) {
			await Assert.ThrowsAsync<InvalidOperationException>(
				() => queue.Enqueue<int>(
					(_, _) => throw new InvalidOperationException("boom"),
					"throwing",
					CancellationToken.None).WaitAsync(WaitLimit));
		}

		int survived = await queue.Enqueue((_, _) => 99, "after", CancellationToken.None).WaitAsync(WaitLimit);
		Assert.Equal(99, survived);

		// And the surviving worker is still the same single thread, not a replacement.
		int threadA = await queue.Enqueue((_, _) => Environment.CurrentManagedThreadId, "a", CancellationToken.None).WaitAsync(WaitLimit);
		int threadB = await queue.Enqueue((_, _) => Environment.CurrentManagedThreadId, "b", CancellationToken.None).WaitAsync(WaitLimit);
		Assert.Equal(threadA, threadB);
	}

	#endregion

	#region 8. Shutdown

	/// <summary>
	/// Work still queued when the queue is disposed is cancelled rather than left hanging. A caller
	/// awaiting work that shutdown discarded must be told the server is going away, not wait forever.
	/// </summary>
	/// <remarks>
	/// The sequencing is deterministic rather than timed: the blocking job waits on the queue's own
	/// shutdown token, so it is <c>Dispose</c> itself that releases the worker. There is no window in
	/// which the queued job could start before <c>Dispose</c> has completed adding and cancelled.
	/// </remarks>
	[Fact]
	public async Task Dispose_CancelsWorkThatWasStillQueued() {
		var queue = CreateQueue();

		using var occupied = new ManualResetEventSlim();
		var ranAnyway = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var blocker = queue.Enqueue((_, shutdown) => {
			occupied.Set();
			// Released by Dispose cancelling the shutdown token, so the ordering below is guaranteed.
			Assert.True(shutdown.WaitHandle.WaitOne(WaitLimit));
			return 0;
		}, "blocker", CancellationToken.None);

		Assert.True(occupied.Wait(WaitLimit));

		var stranded = queue.Enqueue((_, _) => {
			ranAnyway.TrySetResult(true);
			return 1;
		}, "stranded", CancellationToken.None);

		queue.Dispose();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stranded.WaitAsync(WaitLimit));
		Assert.True(stranded.IsCanceled);
		Assert.False(ranAnyway.Task.IsCompleted);

		await blocker.WaitAsync(WaitLimit);
	}

	/// <summary>
	/// Submitting after disposal returns an already-cancelled task instead of hanging forever on a
	/// queue nothing will ever drain.
	/// </summary>
	[Fact]
	public void Enqueue_AfterDispose_ReturnsACancelledTask() {
		var queue = CreateQueue();
		queue.Dispose();

		bool ran = false;
		var task = queue.Enqueue((_, _) => {
			ran = true;
			return 1;
		}, "too-late", CancellationToken.None);

		// Cancelled synchronously: no await, so a hang here is a failure rather than a timeout.
		Assert.True(task.IsCanceled);
		Assert.False(ran);
	}

	/// <summary>Dispose is idempotent; host shutdown paths call it more than once.</summary>
	[Fact]
	public void Dispose_IsIdempotent() {
		var queue = CreateQueue();

		queue.Dispose();
		queue.Dispose();
	}

	#endregion

	#region 9. Depth and Running

	/// <summary>An idle queue reports nothing in flight.</summary>
	[Fact]
	public void IdleQueue_ReportsZeroDepthAndNoRunningJob() {
		using var queue = CreateQueue();

		Assert.Equal(0, queue.Depth);
		Assert.Null(queue.Running);
	}

	/// <summary>
	/// Depth counts the running job plus everything waiting, and <c>Running</c> names the job on the
	/// worker. This is what the depth-aware pending state of SERVER.md &#0167;3.1 keys off: work submitted
	/// while a long job runs must be able to tell that it will wait.
	/// </summary>
	[Fact]
	public async Task DepthAndRunning_ReflectWorkInFlight() {
		using var queue = CreateQueue();

		using var occupied = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();

		var before = DateTimeOffset.UtcNow;
		var blocker = queue.Enqueue((_, _) => {
			occupied.Set();
			Assert.True(release.Wait(WaitLimit));
			return 0;
		}, "walking heap", CancellationToken.None);

		// Running is assigned before the job body executes, so once the body has signalled, Running is
		// already set -- no polling needed for this half.
		Assert.True(occupied.Wait(WaitLimit));

		var running = queue.Running;
		Assert.NotNull(running);
		Assert.Equal("walking heap", running!.Label);
		Assert.True(running.StartedAt >= before);
		Assert.True(running.Elapsed >= TimeSpan.Zero);
		Assert.Equal(1, queue.Depth);

		var queued = new List<Task<int>>();
		for (int i = 0; i < 3; i++) {
			queued.Add(queue.Enqueue((_, _) => 0, $"waiting-{i}", CancellationToken.None));
		}

		// One running plus three waiting. The worker cannot have taken any of them: it is blocked.
		Assert.Equal(4, queue.Depth);

		release.Set();
		await blocker.WaitAsync(WaitLimit);
		await Task.WhenAll(queued).WaitAsync(WaitLimit);

		// The caller's task completes inside the job, a moment before the worker clears Running, so
		// this settles rather than being asserted outright. Bounded by WaitLimit, so a queue that
		// never clears its state fails instead of hanging.
		Assert.True(SpinWait.SpinUntil(() => queue.Running is null && queue.Depth == 0, WaitLimit));
	}

	#endregion
}