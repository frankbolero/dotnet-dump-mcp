using System.CommandLine;

using DotNetDump.Cli.Commands;

namespace DotNetDump.Cli;

/// <summary>
/// Builds the command graph. Kept separate from <see cref="Program"/>'s entry point so tests can
/// parse arguments and assert on bound option values without spawning a process or going through
/// <see cref="CliRunner"/>'s exit-code handling.
/// </summary>
public static class RootCommandFactory {
	public static RootCommand Create() {
		var root = new RootCommand("dndump - CLI front end for DotNetDump.Core (docs/CLI_DESIGN.md).");

		root.Options.Add(GlobalOptions.Dump);
		root.Options.Add(GlobalOptions.Dac);
		root.Options.Add(GlobalOptions.Format);
		root.Options.Add(GlobalOptions.Quiet);

		root.Subcommands.Add(UseCommand.Create());
		root.Subcommands.Add(InfoCommand.Create());
		root.Subcommands.Add(CommandsCommand.Create());
		root.Subcommands.Add(DumpHeapCommand.Create());

		return root;
	}
}