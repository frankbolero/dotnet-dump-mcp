namespace DotNetDump.Core.Models;

public class MethodTableInfo {
	public ulong MethodTable { get; set; }
	public ulong EEClass { get; set; }
	public string TypeName { get; set; } = string.Empty;
	public string? ModuleName { get; set; }
	public ulong BaseSize { get; set; }
	public int MethodCount { get; set; }
	public bool IsValueType { get; set; }
	public bool IsInterface { get; set; }
	public bool IsAbstract { get; set; }
	public bool IsSealed { get; set; }
	public string? BaseTypeName { get; set; }
	public List<string> Interfaces { get; set; } = new();
}