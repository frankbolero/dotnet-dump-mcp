using System.Collections.Generic;

namespace DotNetDump.Core.Models; 
public class ObjectDetails {
	public ulong Address { get; set; }
	public string? TypeName { get; set; }
	public ulong Size { get; set; }
	public ulong MethodTable { get; set; }
	public List<ObjectField> Fields { get; set; } = new();
}

public class ObjectField {
	public string? Name { get; set; }
	public string? TypeName { get; set; }
	public string? Value { get; set; }
	public ulong Address { get; set; } // Non-zero for reference types
	public bool IsReference { get; set; }
	public int Offset { get; set; }
}