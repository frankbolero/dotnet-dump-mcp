namespace DotNetDump.Core.Models;

public class HeapCorruptionInfo {
	public ulong Address { get; set; }
	public ulong Object { get; set; }

	/// <summary>The runtime's classification, e.g. InvalidMethodTable or SyncBlockMismatch.</summary>
	public string Kind { get; set; } = string.Empty;

	public string? Message { get; set; }

	/// <summary>Byte offset within the object where the defect was found.</summary>
	public int Offset { get; set; }

	public string? TypeName { get; set; }
}