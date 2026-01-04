namespace DotNetDump.Core.Models;

public class ModuleInfo {
	public string? Name { get; set; }
	public ulong ImageBase { get; set; }
	public ulong Size { get; set; }
	public bool IsUserCode { get; set; }
}