namespace DotNetDump.Core.Models;

public class ClassInfo {
	public ulong EEClass { get; set; }
	public ulong MethodTable { get; set; }
	public string TypeName { get; set; } = string.Empty;
	public string? ModuleName { get; set; }
	public int FieldCount { get; set; }
	public int StaticFieldCount { get; set; }
	public int MethodCount { get; set; }
	public List<FieldMetadata> Fields { get; set; } = new();
	public List<string> Methods { get; set; } = new();
}

public class FieldMetadata {
	public string Name { get; set; } = string.Empty;
	public string TypeName { get; set; } = string.Empty;
	public int Offset { get; set; }
	public bool IsStatic { get; set; }
	public int Size { get; set; }
}