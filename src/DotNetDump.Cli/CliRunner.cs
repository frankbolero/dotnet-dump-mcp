using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetDump.Cli;

/// <summary>
/// Parses arguments and invokes the matched command, mapping the outcome to the four exit codes
/// from CLI_DESIGN.md &#0167;3.4. Split out from <see cref="Program.Main"/> so tests can drive a full
/// parse-and-invoke cycle -- including real exit codes -- against captured output instead of the
/// process's real <see cref="Console"/> streams.
///
/// Deliberately does not port <c>DumpAnalyzerTools.ExecuteSafe</c>: analyzer exceptions propagate
/// out of the command action and are mapped here, once, rather than swallowed into a "successful"
/// string result at the call site.
/// </summary>
public static class CliRunner {
	public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null) {
		var stdout = output ?? Console.Out;
		var stderr = error ?? Console.Error;

		var parseResult = RootCommandFactory.Create().Parse(args);

		if (parseResult.Errors.Count > 0) {
			foreach (var parseError in parseResult.Errors) {
				stderr.WriteLine(parseError.Message);
			}
			stderr.WriteLine();
			stderr.WriteLine("Run 'dndump --help' for usage.");
			return ExitCodes.UsageError;
		}

		var originalOut = Console.Out;
		var originalError = Console.Error;
		Console.SetOut(stdout);
		Console.SetError(stderr);
		try {
			return parseResult.Action switch {
				SynchronousCommandLineAction sync => sync.Invoke(parseResult),
				AsynchronousCommandLineAction async => await async.InvokeAsync(parseResult, CancellationToken.None),
				_ => ExitCodes.Success,
			};
		} catch (Exception ex) {
			stderr.WriteLine($"Error: {ex.Message}");
			return ExitCodeMapper.Map(ex);
		} finally {
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}
	}
}