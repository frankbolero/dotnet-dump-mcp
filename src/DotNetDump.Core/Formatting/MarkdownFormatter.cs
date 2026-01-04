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

		public static string FormatMethodTable(MethodTableInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**MethodTable:** {info.MethodTable:X16}");
			sb.AppendLine($"**EEClass:** {info.EEClass:X16}");
			sb.AppendLine($"**Type:** {info.TypeName}");
			if (!string.IsNullOrEmpty(info.ModuleName)) {
				sb.AppendLine($"**Module:** {info.ModuleName}");
			}
			sb.AppendLine($"**BaseSize:** {info.BaseSize} bytes");
			sb.AppendLine($"**Method Count:** {info.MethodCount}");
			sb.AppendLine();
			sb.AppendLine("**Flags:**");
			sb.AppendLine($"- ValueType: {info.IsValueType}");
			sb.AppendLine($"- Interface: {info.IsInterface}");
			sb.AppendLine($"- Abstract: {info.IsAbstract}");
			sb.AppendLine($"- Sealed: {info.IsSealed}");

			if (!string.IsNullOrEmpty(info.BaseTypeName)) {
				sb.AppendLine();
				sb.AppendLine($"**Base Type:** {info.BaseTypeName}");
			}

			if (info.Interfaces.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("**Interfaces:**");
				foreach (var iface in info.Interfaces) {
					sb.AppendLine($"- {iface}");
				}
			}

			return sb.ToString();
		}

		public static string FormatMethodDesc(MethodDescInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**MethodDesc:** {info.MethodDesc:X16}");
			sb.AppendLine($"**MethodTable:** {info.MethodTable:X16}");
			sb.AppendLine($"**Method:** {info.MethodName}");
			if (!string.IsNullOrEmpty(info.TypeName)) {
				sb.AppendLine($"**Type:** {info.TypeName}");
			}
			if (!string.IsNullOrEmpty(info.ModuleName)) {
				sb.AppendLine($"**Module:** {info.ModuleName}");
			}
			if (!string.IsNullOrEmpty(info.Signature)) {
				sb.AppendLine($"**Signature:** {info.Signature}");
			}
			sb.AppendLine($"**Metadata Token:** 0x{info.MetadataToken:X8}");
			sb.AppendLine();
			sb.AppendLine("**Code Information:**");
			sb.AppendLine($"- Native Code: {info.NativeCode:X16}");
			sb.AppendLine($"- Is Jitted: {info.IsJitted}");
			sb.AppendLine($"- Is Generic: {info.IsGeneric}");

			return sb.ToString();
		}

		public static string FormatClass(ClassInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**EEClass:** {info.EEClass:X16}");
			sb.AppendLine($"**MethodTable:** {info.MethodTable:X16}");
			sb.AppendLine($"**Type:** {info.TypeName}");
			if (!string.IsNullOrEmpty(info.ModuleName)) {
				sb.AppendLine($"**Module:** {info.ModuleName}");
			}
			sb.AppendLine();
			sb.AppendLine($"**Field Count:** {info.FieldCount} instance, {info.StaticFieldCount} static");
			sb.AppendLine($"**Method Count:** {info.MethodCount}");

			if (info.Fields.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("**Fields:**");
				sb.AppendLine("| Offset | Name | Type | Size | Static |");
				sb.AppendLine("|--------|------|------|------|--------|");
				foreach (var field in info.Fields) {
					string offset = field.IsStatic ? "static" : $"{field.Offset:X}";
					sb.AppendLine($"| {offset} | {field.Name} | {field.TypeName} | {field.Size} | {field.IsStatic} |");
				}
			}

			if (info.Methods.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("**Methods:**");
				foreach (var method in info.Methods) {
					sb.AppendLine($"- {method}");
				}
			}

			return sb.ToString();
		}

		public static string FormatModuleDetails(ModuleDetails info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Module:** {info.Name}");
			sb.AppendLine($"**Assembly:** {info.AssemblyName}");
			sb.AppendLine();
			sb.AppendLine("**Addresses:**");
			sb.AppendLine($"- ImageBase: {info.ImageBase:X16}");
			sb.AppendLine($"- MetadataAddress: {info.MetadataAddress:X16}");
			sb.AppendLine($"- AssemblyId: {info.AssemblyId:X16}");
			sb.AppendLine();
			sb.AppendLine($"**Size:** {info.Size:N0} bytes");
			sb.AppendLine($"**Metadata Length:** {info.MetadataLength:N0} bytes");
			sb.AppendLine($"**Type Count (sampled):** ~{info.TypeCount}");
			sb.AppendLine();
			sb.AppendLine("**Flags:**");
			sb.AppendLine($"- IsDynamic: {info.IsDynamic}");
			sb.AppendLine($"- IsFileLayout: {info.IsFileLayout}");

			return sb.ToString();
		}

		public static string FormatAssemblyDetails(AssemblyDetails info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Assembly:** {info.Name}");
			sb.AppendLine($"**AssemblyId:** {info.AssemblyId:X16}");
			sb.AppendLine($"**IsDynamic:** {info.IsDynamic}");
			sb.AppendLine();
			sb.AppendLine($"**Module Count:** {info.Modules.Count}");

			if (info.Modules.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("**Modules:**");
				foreach (var module in info.Modules) {
					sb.AppendLine($"- {module}");
				}
			}

			return sb.ToString();
		}

		public static string FormatName2EE(Name2EEResult info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Module:** {info.ModuleName}");
			sb.AppendLine($"**Type:** {info.TypeName}");
			sb.AppendLine();
			sb.AppendLine($"**MethodTable:** {info.MethodTable:X16}");
			sb.AppendLine($"**EEClass:** {info.EEClass:X16}");

			if (!string.IsNullOrEmpty(info.MethodName)) {
				sb.AppendLine();
				sb.AppendLine($"**Method:** {info.MethodName}");

				if (info.Methods.Count > 0) {
					sb.AppendLine();
					sb.AppendLine("**Overloads:**");
					sb.AppendLine("| MethodDesc | Signature | Jitted |");
					sb.AppendLine("|------------|-----------|--------|");
					foreach (var method in info.Methods) {
						sb.AppendLine($"| {method.MethodDesc:X16} | {method.Signature} | {method.IsJitted} |");
					}
				}
			}

			return sb.ToString();
		}
	}
}