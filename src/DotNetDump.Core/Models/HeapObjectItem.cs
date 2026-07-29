namespace DotNetDump.Core.Models;

public class HeapObjectItem {
	public ulong Address { get; set; }
	public ulong MethodTable { get; set; }
	public ulong Size { get; set; }
	public string? TypeName { get; set; }

	/// <summary>
	/// The generation the object was found in during the walk, or <c>null</c> when ClrMD could not
	/// place it (<c>Microsoft.Diagnostics.Runtime.Generation.Unknown</c> — a sign of heap corruption
	/// or a segment kind the walk did not recognize). Required to honor <c>FilterSpec.Generation</c>
	/// on <c>listobj</c> (DATA_CONTRACT.md &#0167;2.3).
	/// </summary>
	public GenerationFilter? Generation { get; set; }
}