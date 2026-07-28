namespace DotNetDump.Cli;

/// <summary>The four exit codes from CLI_DESIGN.md &#0167;3.4.</summary>
internal static class ExitCodes {
	public const int Success = 0;
	public const int AnalysisError = 1;
	public const int UsageError = 2;
	public const int DumpLoadFailure = 3;
}