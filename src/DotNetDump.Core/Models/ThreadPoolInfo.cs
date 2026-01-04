namespace DotNetDump.Core.Models {
	public class ThreadPoolInfo {
		public int TotalThreads { get; set; }
		public int ActiveThreads { get; set; }
		public int IdleThreads { get; set; }
		public int MinThreads { get; set; }
		public int MaxThreads { get; set; }
		public string? Type { get; set; } // "Portable", "Windows", "Legacy"
	}
}