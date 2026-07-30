using DotNetDump.Core.Models;

namespace DotNetDump.Web.Analysis;

/// <summary>
/// Memoizes <c>SessionAnalyzer.GetInfo</c> behind a single <see cref="IAnalysisQueue.Enqueue{T}"/>
/// call, so the dump header bar -- rendered on every page -- never enqueues a fresh analyzer call.
/// </summary>
/// <remarks>
/// <para>
/// <c>SessionAnalyzer</c> is an analyzer like <c>HeapAnalyzer</c> or <c>ThreadAnalyzer</c>: it wraps
/// ClrMD state that is not thread-safe, so it may only run on the single analysis thread, reached
/// exclusively through <see cref="IAnalysisQueue.Enqueue{T}"/> (SERVER.md &#0167;3).
/// <c>AnalysisSession</c> is deliberately unreachable from DI for exactly that reason -- see its own
/// remarks -- so this type cannot hold a reference to the session or the analyzer between calls, only
/// to the <see cref="DumpInfo"/> result, which is a plain, immutable-enough POCO safe to share.
/// </para>
/// <para>
/// The dump this process was started against never changes for the life of the process, so the
/// answer on request one and request ten-thousand is identical. Enqueueing a fresh call per request
/// would serialize every page load behind the single analysis thread for a value that cannot have
/// changed since the first call -- needless contention for a header that renders unconditionally.
/// <see cref="Lazy{T}"/> over one <see cref="IAnalysisQueue.Enqueue{T}"/> call makes the analyzer run
/// at most once; its default thread-safety mode (<c>ExecutionAndPublication</c>) is what keeps two
/// concurrent first requests from both winning the race and enqueueing twice.
/// </para>
/// </remarks>
public sealed class DumpInfoService {
	private readonly Lazy<Task<DumpInfo>> _info;

	public DumpInfoService(IAnalysisQueue queue) {
		// CancellationToken.None: this call belongs to the singleton, not to whichever request
		// happens to trigger it first. A caller's request being cancelled must not poison the one
		// shared result for every request that follows.
		//
		// explicitDacPath is null: the web host is not given the --dac value a caller may have
		// passed to 'dndump serve' (DumpWebHostOptions carries no such field today), and the header
		// bar this feeds does not display DAC information -- only runtime version, architecture and
		// OS -- so there is nothing here for a real value to change.
		_info = new Lazy<Task<DumpInfo>>(() => queue.Enqueue(
			(session, _) => session.Info.GetInfo(explicitDacPath: null),
			"reading dump info",
			CancellationToken.None));
	}

	/// <summary>The dump's runtime/architecture/OS summary. Instant after the first call.</summary>
	public Task<DumpInfo> GetAsync() => _info.Value;
}