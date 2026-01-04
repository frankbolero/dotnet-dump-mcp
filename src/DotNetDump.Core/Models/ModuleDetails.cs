namespace DotNetDump.Core.Models;

public class ModuleDetails {
	public ulong Address { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? AssemblyName { get; set; }
	public ulong ImageBase { get; set; }
	public ulong Size { get; set; }
	public ulong MetadataAddress { get; set; }
	public int MetadataLength { get; set; }
	public ulong AssemblyId { get; set; }
	public bool IsDynamic { get; set; }
	public bool IsFileLayout { get; set; }
	public int TypeCount { get; set; }
}
