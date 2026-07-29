using DotNetDump.Core.Caching;

namespace DotNetDump.Cli;

/// <summary>
/// The disk cache shared by every command in this process. CLI_DESIGN.md §6's whole premise is
/// that a per-invocation CLI stays competitive with a long-lived server by persisting *derived
/// results* to disk -- a command that builds its analyzer without one gets none of that benefit
/// and simply re-walks the heap every time, which defeats the point of Phases 1 and 4.
///
/// Root resolution (<c>DNDUMP_CACHE</c>, else an XDG-style default) lives in
/// <see cref="FileSystemAnalysisCache"/> itself. No memory tier here: unlike the MCP server
/// (long-lived, see its Program.cs), this process exits after one command, so there is never a
/// second <c>GetOrCompute</c> call in the same run for RAM to serve.
/// </summary>
public static class AnalysisCacheProvider {
	public static readonly IAnalysisCache Default = new FileSystemAnalysisCache();
}