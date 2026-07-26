namespace DotNetDump.Core.Models;

public class AssemblyDetails {
	/// <summary>The runtime's Assembly address — what SOS calls the assembly id.</summary>
	public ulong AssemblyAddress { get; set; }

	public string Name { get; set; } = string.Empty;
	public bool IsDynamic { get; set; }
	public string? AppDomainName { get; set; }
	public List<string> Modules { get; set; } = new();
}