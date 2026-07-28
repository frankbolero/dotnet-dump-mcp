namespace DotNetDump.Core.Models;

public class ClassInfo {
	public ulong EEClass { get; set; }
	public ulong MethodTable { get; set; }
	public string TypeName { get; set; } = string.Empty;
	public string? ModuleName { get; set; }

	/// <summary>Instance field count — the real total, not a filtered subset.</summary>
	public int FieldCount { get; set; }

	public int StaticFieldCount { get; set; }
	public int ThreadStaticFieldCount { get; set; }
	public int MethodCount { get; set; }

	/// <summary>True when <see cref="Fields"/> or <see cref="Methods"/> were capped.</summary>
	public bool IsTruncated { get; set; }

	public List<FieldMetadata> Fields { get; set; } = new();
	public List<string> Methods { get; set; } = new();
}

public class FieldMetadata {
	public string Name { get; set; } = string.Empty;
	public string TypeName { get; set; } = string.Empty;
	public int Offset { get; set; }
	public bool IsStatic { get; set; }
	public bool IsThreadStatic { get; set; }
	public int Size { get; set; }

	/// <summary>Current value, read for static fields where the runtime can supply one.</summary>
	public string? Value { get; set; }
}