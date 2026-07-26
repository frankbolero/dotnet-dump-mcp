namespace DotNetDump.Core.Models;

public class MethodTableInfo {
	public ulong MethodTable { get; set; }

	/// <summary>
	/// ClrMD does not expose the EEClass address; this mirrors <see cref="MethodTable"/>. Callers
	/// should not present it as a distinct address the way SOS does.
	/// </summary>
	public ulong EEClass { get; set; }

	public string TypeName { get; set; } = string.Empty;
	public string? ModuleName { get; set; }
	public ulong BaseSize { get; set; }

	/// <summary>Per-element size for arrays and strings; 0 for ordinary types.</summary>
	public int ComponentSize { get; set; }

	public int MethodCount { get; set; }
	public int MetadataToken { get; set; }
	public bool IsValueType { get; set; }
	public bool IsInterface { get; set; }
	public bool IsAbstract { get; set; }
	public bool IsSealed { get; set; }
	public bool IsEnum { get; set; }
	public bool IsArray { get; set; }
	public bool IsString { get; set; }
	public bool IsFinalizable { get; set; }
	public bool ContainsPointers { get; set; }
	public string Visibility { get; set; } = string.Empty;
	public string? BaseTypeName { get; set; }
	public List<string> Interfaces { get; set; } = new();
}
