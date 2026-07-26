namespace DotNetDump.Core.Models;

public class ModuleDetails {
	public ulong Address { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? AssemblyName { get; set; }
	public ulong ImageBase { get; set; }
	public ulong Size { get; set; }
	public ulong MetadataAddress { get; set; }
	public int MetadataLength { get; set; }

	/// <summary>The runtime's Assembly address — what SOS calls the assembly id.</summary>
	public ulong AssemblyAddress { get; set; }

	public bool IsDynamic { get; set; }
	public bool IsPEFile { get; set; }

	/// <summary>Flat, Unknown, or Mapped, from <c>ClrModule.Layout</c>.</summary>
	public string Layout { get; set; } = string.Empty;

	public string? AppDomainName { get; set; }

	/// <summary>Exact count from the module's TypeDef-to-MethodTable map.</summary>
	public int TypeCount { get; set; }

	/// <summary>Types in this module that declare static fields — candidate static roots.</summary>
	public int TypesWithStaticFieldsCount { get; set; }
}
