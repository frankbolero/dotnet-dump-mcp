namespace DotNetDump.Core.Models {
	public class SyncBlockInfo {
		public ulong ObjectAddress { get; set; }
		public bool IsMonitorHeld { get; set; }
		public ulong HoldingThreadAddress { get; set; }
		public int RecursionCount { get; set; }
		public int WaitingThreadCount { get; set; }
		public int ManagedThreadId { get; set; } // If we can map it
	}
}