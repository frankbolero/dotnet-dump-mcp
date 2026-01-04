namespace DotNetDump.Core.Models {
	public class HeapStatItem {
		public string? TypeName { get; set; }
		public ulong MethodTable { get; set; }
		public int Count { get; set; }
		public long TotalSize { get; set; }
	}
}