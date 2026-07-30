using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Caching;

namespace DotNetDump.Web.Analysis;

/// <summary>
/// The loaded dump and every analyzer over it, owned exclusively by the analysis thread.
/// </summary>
/// <remarks>
/// <para>
/// This type is deliberately <em>not</em> registered in dependency injection. <c>ClrRuntime</c> and
/// <c>ClrHeap</c> are not thread-safe (SERVER.md &#0167;3), so the property that has to hold is that a
/// request handler cannot reach an analyzer at all except from the analysis thread. Registering the
/// analyzers as singletons would leave that to discipline; handing the session to the work delegate
/// instead — and constructing it only inside <see cref="AnalysisQueue"/> — makes it structural. The
/// single instance is reachable only from a <see cref="IAnalysisQueue.Enqueue{T}"/> callback, and
/// those only ever run on the one worker thread.
/// </para>
/// <para>
/// The dump is loaded before the session is built, on whichever thread started the host. That is
/// safe: ClrMD's constraint is concurrency, not thread affinity — it has no apartment or
/// thread-local state — and it keeps <c>dndump serve</c>'s load failures on the same
/// resolve-and-map path as every other command.
/// </para>
/// </remarks>
public sealed class AnalysisSession : IDisposable {
	/// <summary>The resolved path of the loaded dump, for the header bar and for logging.</summary>
	public string DumpPath { get; }

	public IDumpContext Context { get; }

	public HeapAnalyzer Heap { get; }

	public ThreadAnalyzer Threads { get; }

	public ModuleAnalyzer Modules { get; }

	public MetadataAnalyzer Metadata { get; }

	public SessionAnalyzer Info { get; }

	internal AnalysisSession(string dumpPath, IDumpContext context, IAnalysisCache cache) {
		DumpPath = dumpPath;
		Context = context;
		Heap = new HeapAnalyzer(context, cache);
		Threads = new ThreadAnalyzer(context, cache);
		Modules = new ModuleAnalyzer(context);
		Metadata = new MetadataAnalyzer(context);
		Info = new SessionAnalyzer(context);
	}

	/// <summary>
	/// Disposes the dump context. The queue owns the session's lifetime, so this runs during host
	/// shutdown after the worker thread has stopped — never concurrently with analysis.
	/// </summary>
	public void Dispose() => Context.Dispose();
}