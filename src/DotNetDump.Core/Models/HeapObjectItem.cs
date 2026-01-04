namespace DotNetDump.Core.Models {
	public class HeapObjectItem {
		public ulong Address { get; set; }
		public ulong MethodTable { get; set; }
		public ulong Size { get; set; }
		public string? TypeName { get; set; }
	}
}