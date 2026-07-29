using System;
using System.CommandLine;
using System.IO;

using DotNetDump.Core;
using DotNetDump.Core.Utilities;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump use &lt;path&gt;</c> -- CLI_DESIGN.md &#0167;4.1. Writes <c>.dndump/session.json</c> in the
/// current directory so later commands can omit <c>--dump</c>.
///
/// Validates the dump opens before writing the session (CLI_DESIGN.md &#0167;12, open question #2,
/// resolved in favour of eager validation): a clear failure at session start beats a confusing one
/// on the first real query, and it means a broken session is never written in the first place.
/// </summary>
public static class UseCommand {
	public static Command Create() {
		var pathArgument = new Argument<string>("path") {
			Description = "Path to the dump file to analyze.",
		};

		var command = new Command("use", "Select a dump for subsequent commands; writes .dndump/session.json.");
		command.Arguments.Add(pathArgument);

		command.SetAction((ParseResult parseResult) => {
			string path = parseResult.GetValue(pathArgument)!;
			string? dacOption = parseResult.GetValue(GlobalOptions.Dac);
			bool quiet = parseResult.GetValue(GlobalOptions.Quiet);

			using (var context = new DumpContext()) {
				try {
					context.Load(path, dacOption);
				} catch (Exception ex) {
					throw new DumpLoadException($"Could not load dump '{path}': {ex.Message}", ex);
				}
			}

			string currentDirectory = Directory.GetCurrentDirectory();
			var session = new SessionFile {
				DumpPath = Path.GetFullPath(path),
				DacPath = dacOption,
				Timestamp = DateTimeOffset.UtcNow,
			};
			session.Save(currentDirectory);

			if (!quiet) {
				Console.Error.WriteLine($"Dump validated: {session.DumpPath}");
			}
			Console.WriteLine($"Session written to {SessionFile.GetPath(currentDirectory)}");

			return ExitCodes.Success;
		});

		return command;
	}
}