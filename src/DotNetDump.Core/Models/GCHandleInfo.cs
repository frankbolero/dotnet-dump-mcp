namespace DotNetDump.Core.Models {
	public class GCHandleInfo {
		public ulong Address { get; set; }
		public ulong Object { get; set; }
		public string? Kind { get; set; }
		public string? TypeName { get; set; }
	}
}