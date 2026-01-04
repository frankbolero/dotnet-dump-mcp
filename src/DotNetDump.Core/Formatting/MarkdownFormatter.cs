using System.Collections.Generic;
using System.Linq;
using System.Text;

using DotNetDump.Core.Models;

namespace DotNetDump.Core.Formatting {
	public static class MarkdownFormatter {
		public static string FormatHeapStatistics(IEnumerable<HeapStatItem> stats) {
			var sb = new StringBuilder();
			sb.AppendLine("| Count | Total Size | Type |");
			sb.AppendLine("|-------|------------|------|");
			foreach (var item in stats) {
				sb.AppendLine($"| {item.Count:N0} | {item.TotalSize:N0} | {item.TypeName} |");
			}
			return sb.ToString();
		}

		public static string FormatHeapObjects(IEnumerable<HeapObjectItem> objects) {
			var sb = new StringBuilder();
			sb.AppendLine("| Address | Size | Type |");
			sb.AppendLine("|---------|------|------|");
			foreach (var item in objects) {
				sb.AppendLine($"| {item.Address:X16} | {item.Size:N0} | {item.TypeName} |");
			}
			return sb.ToString();
		}

		public static string FormatThreads(IEnumerable<ThreadInfo> threads) {
			var sb = new StringBuilder();
			sb.AppendLine("| Mgd ID | OS ID | State | Exception |");
			sb.AppendLine("|--------|-------|-------|-----------|");
			foreach (var item in threads) {
				string state = item.IsAlive ? "Alive" : "Dead";
				string exception = item.ExceptionType ?? "(none)";
				sb.AppendLine($"| {item.ManagedThreadId} | {item.OSThreadId:X} | {state} | {exception} |");
			}
			return sb.ToString();
		}

		public static string FormatModules(IEnumerable<ModuleInfo> modules) {
			var sb = new StringBuilder();
			sb.AppendLine("| Address | Size | Name |");
			sb.AppendLine("|---------|------|------|");
			foreach (var item in modules) {
				sb.AppendLine($"| {item.ImageBase:X16} | {item.Size:X} | {item.Name} |");
			}
			return sb.ToString();
		}

		public static string FormatStackGroups(IEnumerable<StackGroup> groups) {
			var sb = new StringBuilder();
			int i = 1;
			foreach (var group in groups) {
				sb.AppendLine($"### Group {i++} ({group.ThreadCount} Threads)");
				sb.AppendLine($"**Managed Thread IDs:** {string.Join(", ", group.ManagedThreadIds)}");
				sb.AppendLine("**Stack:**");
				sb.AppendLine("```text");
				foreach (var frame in group.Frames) {
					sb.AppendLine(frame);
				}
				sb.AppendLine("```");
				sb.AppendLine();
			}
			return sb.ToString();
		}

		public static string FormatGCRoots(IEnumerable<GCRootInfo> roots) {
			var sb = new StringBuilder();
			sb.AppendLine("| Root Addr | Kind | Thread | Name |");
			sb.AppendLine("|-----------|------|--------|------|");
			foreach (var item in roots) {
				string threadId = item.ManagedThreadId != -1 ? item.ManagedThreadId.ToString() : "-";
				sb.AppendLine($"| {item.Address:X16} | {item.Kind} | {threadId} | {item.RootName} |");
			}
			return sb.ToString();
		}

		public static string FormatObjectDetails(ObjectDetails details) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Object:** {details.Address:X16}");
			sb.AppendLine($"**Type:** {details.TypeName}");
			sb.AppendLine($"**Size:** {details.Size:N0} bytes");
			sb.AppendLine($"**MethodTable:** {details.MethodTable:X16}");
			sb.AppendLine();
			sb.AppendLine("| Offset | Name | Type | Value | Address |");
			sb.AppendLine("|--------|------|------|-------|---------|");
			foreach (var field in details.Fields) {
				string addr = field.Address != 0 ? field.Address.ToString("X16") : "";
				sb.AppendLine($"| {field.Offset:X} | {field.Name} | {field.TypeName} | {field.Value} | {addr} |");
			}
			return sb.ToString();
		}

		public static string FormatHeapSegments(IEnumerable<HeapSegmentInfo> segments) {
			var sb = new StringBuilder();
			sb.AppendLine("| Segment Range | Gen | Size | Type |");
			sb.AppendLine("|---------------|-----|------|------|");
			foreach (var item in segments) {
				string type = item.IsLargeObjectHeap ? "LOH" : item.IsPinnedObjectHeap ? "POH" : $"Gen {item.Generation}";
				sb.AppendLine($"| {item.Start:X16} - {item.End:X16} | {item.Generation} | {item.Size:N0} | {type} |");
			}
			return sb.ToString();
		}

		public static string FormatThreadPool(ThreadPoolInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Type:** {info.Type}");
			sb.AppendLine($"**Total Threads:** {info.TotalThreads}");
			sb.AppendLine($"**Active Threads:** {info.ActiveThreads}");
			sb.AppendLine($"**Idle Threads:** {info.IdleThreads}");
			sb.AppendLine($"**Min Threads:** {info.MinThreads}");
			sb.AppendLine($"**Max Threads:** {info.MaxThreads}");
			return sb.ToString();
		}

		public static string FormatSyncBlocks(IEnumerable<SyncBlockInfo> blocks) {
			var sb = new StringBuilder();
			sb.AppendLine("| Object | Monitor Held | Thread | Recursion | Waiting |");
			sb.AppendLine("|--------|--------------|--------|-----------|---------|");
			foreach (var block in blocks) {
				string thread = block.ManagedThreadId != -1 ? block.ManagedThreadId.ToString() : (block.HoldingThreadAddress != 0 ? $"{block.HoldingThreadAddress:X}" : "None");
				sb.AppendLine($"| {block.ObjectAddress:X16} | {block.IsMonitorHeld} | {thread} | {block.RecursionCount} | {block.WaitingThreadCount} |");
			}
			return sb.ToString();
		}
	}
}