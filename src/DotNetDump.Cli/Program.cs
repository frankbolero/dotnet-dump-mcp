using System.Threading.Tasks;

namespace DotNetDump.Cli;

internal class Program {
	private static async Task<int> Main(string[] args) {
		return await CliRunner.RunAsync(args);
	}
}