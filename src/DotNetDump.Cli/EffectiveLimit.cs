namespace DotNetDump.Cli;

/// <summary>
/// <c>--top &lt;n&gt;</c> is accepted as an alias for <c>--limit &lt;n&gt;</c> on summary commands
/// (CLI_DESIGN.md &#0167;3.2), because that is how people actually describe the operation ("top 20
/// types by size"). When both are given, <c>--top</c> wins as the more specific, more recently
/// typed intent.
/// </summary>
internal static class EffectiveLimit {
	public static int Resolve(int limit, int? top) => top ?? limit;
}