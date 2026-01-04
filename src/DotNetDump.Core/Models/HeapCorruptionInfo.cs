namespace DotNetDump.Core.Models {
	public class HeapCorruptionInfo {
		public ulong Address { get; set; }
		public ulong Object { get; set; }
		public string? Message { get; set; }
		public int Offset { get; set; }
	}
}