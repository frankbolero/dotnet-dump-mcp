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

		// Heap commands
		root.Subcommands.Add(DumpHeapCommand.Create());
		root.Subcommands.Add(ListObjCommand.Create());
		root.Subcommands.Add(DumpObjCommand.Create());
		root.Subcommands.Add(GCRootCommand.Create());
		root.Subcommands.Add(EEHeapCommand.Create());
		root.Subcommands.Add(GCHandlesCommand.Create());
		root.Subcommands.Add(VerifyHeapCommand.Create());
		root.Subcommands.Add(VerifyObjCommand.Create());

		// Thread commands
		root.Subcommands.Add(ClrThreadsCommand.Create());
		root.Subcommands.Add(ThreadStateCommand.Create());
		root.Subcommands.Add(ClrStackCommand.Create());
		root.Subcommands.Add(EEStackCommand.Create());
		root.Subcommands.Add(DumpStackCommand.Create());
		root.Subcommands.Add(ThreadPoolCommand.Create());
		root.Subcommands.Add(SyncBlkCommand.Create());

		// Exception commands
		root.Subcommands.Add(PrintExceptionCommand.Create());

		// Module and metadata commands
		root.Subcommands.Add(ClrModulesCommand.Create());
		root.Subcommands.Add(DumpModuleCommand.Create());
		root.Subcommands.Add(DumpAssemblyCommand.Create());
		root.Subcommands.Add(DumpMtCommand.Create());
		root.Subcommands.Add(DumpMdCommand.Create());
		root.Subcommands.Add(DumpClassCommand.Create());
		root.Subcommands.Add(Name2EECommand.Create());
		root.Subcommands.Add(IP2MdCommand.Create());

		return root;
	}
}