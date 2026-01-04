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

		public static string FormatGCHandles(IEnumerable<GCHandleInfo> handles) {
			var sb = new StringBuilder();
			sb.AppendLine("| Handle Address | Object Address | Kind | Type |");
			sb.AppendLine("|----------------|----------------|------|------|");
			foreach (var handle in handles) {
				sb.AppendLine($"| {handle.Address:X16} | {handle.Object:X16} | {handle.Kind} | {handle.TypeName} |");
			}
			return sb.ToString();
		}

		public static string FormatHeapVerification(IEnumerable<HeapCorruptionInfo> corruptions) {
			var sb = new StringBuilder();
			var corruptionList = corruptions.ToList();

			if (corruptionList.Count == 0) {
				sb.AppendLine("**Heap Verification Result:** PASSED");
				sb.AppendLine();
				sb.AppendLine("No corruption detected. The managed heap is valid.");
				return sb.ToString();
			}

			sb.AppendLine($"**Heap Verification Result:** FAILED");
			sb.AppendLine();
			sb.AppendLine($"**Corruption Count:** {corruptionList.Count}");
			sb.AppendLine();
			sb.AppendLine("| Address | Object | Offset | Message |");
			sb.AppendLine("|---------|--------|--------|---------|");
			foreach (var corruption in corruptionList) {
				string message = corruption.Message ?? "Unknown corruption";
				sb.AppendLine($"| {corruption.Address:X16} | {corruption.Object:X16} | {corruption.Offset:X} | {message} |");
			}
			return sb.ToString();
		}

		public static string FormatDetailedStacks(IEnumerable<ThreadStackInfo> stacks) {
			var sb = new StringBuilder();
			int threadNum = 0;

			foreach (var stack in stacks) {
				threadNum++;
				sb.AppendLine($"### Thread {threadNum}: Managed ID {stack.ManagedThreadId}, OS ID {stack.OSThreadId:X}");

				if (!string.IsNullOrEmpty(stack.ExceptionType)) {
					sb.AppendLine($"**Exception:** {stack.ExceptionType}");
				}

				sb.AppendLine();
				sb.AppendLine("| IP | SP | Kind | Method |");
				sb.AppendLine("|----|----| -----|--------|");

				foreach (var frame in stack.Frames) {
					string method = frame.MethodName ?? "(unknown)";
					if (!string.IsNullOrEmpty(frame.ModuleName)) {
						method = $"{frame.ModuleName}!{method}";
					}
					sb.AppendLine($"| {frame.InstructionPointer:X16} | {frame.StackPointer:X16} | {frame.FrameKind} | {method} |");
				}

				sb.AppendLine();
			}

			return sb.ToString();
		}
	}
}