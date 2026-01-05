namespace DotNetDump.Core.Models;

public class AssemblyDetails {
	public ulong AssemblyId { get; set; }
	public string Name { get; set; } = string.Empty;
	public bool IsDynamic { get; set; }
	public List<string> Modules { get; set; } = new();
}