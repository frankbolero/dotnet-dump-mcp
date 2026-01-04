namespace DotNetDump.Core.Models;

public class Name2EEResult {
	public string? ModuleName { get; set; }
	public string? TypeName { get; set; }
	public string? MethodName { get; set; }
	public ulong MethodTable { get; set; }
	public ulong EEClass { get; set; }
	public List<MethodDescInfo> Methods { get; set; } = new();
}
