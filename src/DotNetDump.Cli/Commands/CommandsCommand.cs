using System.CommandLine;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DotNetDump.Cli.Commands;

/// <summary>
/// <c>dndump commands</c> -- CLI_DESIGN.md &#0167;4.1. Cheap-to-consult, static self-description of
/// what this build of the CLI implements; not backed by a Core analyzer since it describes the CLI
/// itself rather than a dump. Phase 6 extends <see cref="Entries"/> as the remaining commands from
/// &#0167;4.2-&#0167;4.5 land -- this phase wires only <c>use</c>, <c>info</c>, <c>commands</c> and
/// <c>dumpheap</c>.
/// </summary>
public static class CommandsCommand {
	private static readonly (string Name, string Description)[] Entries = {
		("use", "Select a dump for subsequent commands; writes .dndump/session.json."),
		("info", "Runtime version, architecture, OS, DAC status, heap size, segment and thread counts."),
		("commands", "List available commands (this command)."),
		("dumpheap", "Heap statistics by type: count, total size, MethodTable."),
	};

	public static Command Create() {
		var command = new Command("commands", "List available commands.");

		command.SetAction((ParseResult parseResult) => {
			string format = parseResult.GetValue(GlobalOptions.Format)!;
			System.Console.WriteLine(Render(format));
			return ExitCodes.Success;
		});

		return command;
	}

	private static string Render(string format) => format switch {
		"json" => RenderJson(),
		"tsv" => RenderTsv(),
		_ => RenderMarkdown(),
	};

	private static string RenderMarkdown() {
		var sb = new StringBuilder();
		sb.AppendLine("| Command | Description |");
		sb.AppendLine("|---------|-------------|");
		foreach (var (name, description) in Entries) {
			sb.AppendLine($"| {name} | {description} |");
		}
		sb.AppendLine();
		sb.AppendLine("More commands land in Phase 6 (CLI_DESIGN.md §4.2-4.5).");
		return sb.ToString();
	}

	private static string RenderTsv() {
		var sb = new StringBuilder();
		sb.AppendLine("name\tdescription");
		foreach (var (name, description) in Entries) {
			sb.AppendLine($"{name}\t{description}");
		}
		return sb.ToString();
	}

	private static string RenderJson() {
		var data = Entries.Select(e => new { name = e.Name, description = e.Description });
		var envelope = new {
			data,
			note = "More commands land in Phase 6 (CLI_DESIGN.md §4.2-4.5).",
		};
		return JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
	}
}