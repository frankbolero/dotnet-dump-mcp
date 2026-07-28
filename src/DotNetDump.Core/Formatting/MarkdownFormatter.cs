using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using DotNetDump.Core.Models;

namespace DotNetDump.Core.Formatting {
	public static class MarkdownFormatter {
		/// <summary>Addresses render bare and uppercase so they can be pasted straight back into a tool call.</summary>
		private static string Addr(ulong value) => value.ToString("X16");

		// Invariant culture: output must be stable regardless of the host's locale (e.g. some locales use
		// "." or " " as the thousands separator instead of ",", which would break these tests intermittently
		// depending on the runner's culture).
		private static string Bytes(ulong value) => value.ToString("N0", CultureInfo.InvariantCulture);

		private static string Bytes(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

		private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

		private static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

		public static string FormatHeapStatistics(IEnumerable<HeapStatItem> stats) {
			var sb = new StringBuilder();
			// MethodTable is included so a row can be fed straight to dump_mt / list_objects.
			sb.AppendLine("| MethodTable | Count | Total Size | Type |");
			sb.AppendLine("|-------------|-------|------------|------|");
			foreach (var item in stats) {
				sb.AppendLine($"| {Addr(item.MethodTable)} | {Num(item.Count)} | {Bytes(item.TotalSize)} | {item.TypeName} |");
			}
			return sb.ToString();
		}

		public static string FormatHeapObjects(IEnumerable<HeapObjectItem> objects) {
			var sb = new StringBuilder();
			sb.AppendLine("| Address | MethodTable | Size | Type |");
			sb.AppendLine("|---------|-------------|------|------|");
			foreach (var item in objects) {
				sb.AppendLine($"| {Addr(item.Address)} | {Addr(item.MethodTable)} | {Bytes(item.Size)} | {item.TypeName} |");
			}
			return sb.ToString();
		}

		public static string FormatThreads(IEnumerable<ThreadInfo> threads) {
			var sb = new StringBuilder();
			sb.AppendLine("| Mgd ID | OS ID (hex) | State | Exception |");
			sb.AppendLine("|--------|-------------|-------|-----------|");
			foreach (var item in threads) {
				string state = item.IsAlive ? "Alive" : "Dead";
				string exception = item.ExceptionType ?? "(none)";
				sb.AppendLine($"| {item.ManagedThreadId} | {item.OSThreadId:X} | {state} | {exception} |");
			}
			return sb.ToString();
		}

		public static string FormatModules(IEnumerable<ModuleInfo> modules) {
			var sb = new StringBuilder();
			sb.AppendLine("| ImageBase | Size (bytes) | Name |");
			sb.AppendLine("|-----------|--------------|------|");
			foreach (var item in modules) {
				sb.AppendLine($"| {Addr(item.ImageBase)} | {Bytes(item.Size)} | {item.Name} |");
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

		/// <summary>
		/// Renders retention chains. The chain is what answers "why is this alive?" — a bare list of
		/// root addresses does not.
		/// <para>
		/// Never asserts the object is unrooted without checking <see cref="GCRootSearchInfo.Truncated"/>
		/// first — an empty result and a truncated search used to be indistinguishable, which is the
		/// defect tracked in docs/GCROOT_TRUNCATION.md.
		/// </para>
		/// </summary>
		public static string FormatGCRootPaths(GCRootSearchInfo result) {
			var list = result.Paths;
			var sb = new StringBuilder();

			if (list.Count == 0) {
				if (result.Truncated) {
					sb.AppendLine($"**Search truncated before finding any root path to `{Addr(result.TargetAddress)}`.**");
					sb.AppendLine();
					sb.AppendLine($"The traversal budget was exhausted after visiting {Num(result.NodesVisited)} node(s), " +
						"before any path was found. This result is **inconclusive** — it does not show whether the " +
						"object is reachable from a GC root, only that the search did not finish. Re-run with " +
						"`maxNodes: 0` (unlimited) for a conclusive answer; be aware this can use substantial memory " +
						"on a very large heap (roughly 40 bytes per node visited, e.g. ~4 GB at 100,000,000 nodes).");
					return sb.ToString();
				}

				sb.AppendLine($"**No GC root path found to `{Addr(result.TargetAddress)}`.**");
				sb.AppendLine();
				sb.AppendLine($"The search completed ({Num(result.NodesVisited)} node(s) visited): the object is not " +
					"reachable from any GC root, which means it is unrooted and eligible for collection (or was " +
					"already collected and the address is stale).");
				return sb.ToString();
			}

			sb.AppendLine($"Found {list.Count} root path(s) to `{Addr(result.TargetAddress)}` ({Num(result.NodesVisited)} node(s) visited).");
			if (result.Truncated) {
				sb.AppendLine();
				sb.AppendLine("**Search truncated:** the traversal budget was exhausted while looking for further " +
					"node-disjoint paths. The path(s) below are confirmed, but more may exist than are shown. " +
					"Re-run with `maxNodes: 0` (unlimited) for a conclusive answer.");
			}
			sb.AppendLine();

			int index = 1;
			foreach (var path in list) {
				var qualifiers = new List<string>();
				if (path.ManagedThreadId.HasValue) qualifiers.Add($"thread {path.ManagedThreadId}");
				if (path.IsPinned) qualifiers.Add("pinned");
				if (path.IsInterior) qualifiers.Add("interior");
				string suffix = qualifiers.Count > 0 ? $" ({string.Join(", ", qualifiers)})" : string.Empty;

				sb.AppendLine($"### Path {index++}: {path.RootKind} root at `{Addr(path.RootAddress)}`{suffix}");
				sb.AppendLine($"**Depth:** {path.Depth} reference(s) from the root object to the target.");
				sb.AppendLine();
				sb.AppendLine("| # | Address | Size | Type |");
				sb.AppendLine("|---|---------|------|------|");

				for (int i = 0; i < path.Path.Count; i++) {
					var node = path.Path[i];
					string marker = i == path.Path.Count - 1 ? "target" : i.ToString();
					sb.AppendLine($"| {marker} | {Addr(node.Address)} | {Bytes(node.Size)} | {node.TypeName ?? "<unknown>"} |");
				}
				sb.AppendLine();
			}

			return sb.ToString();
		}

		public static string FormatObjectDetails(ObjectDetails details) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Object:** `{Addr(details.Address)}`");
			sb.AppendLine($"**Type:** `{details.TypeName}`");
			sb.AppendLine($"**Size:** {Bytes(details.Size)} bytes");
			sb.AppendLine($"**MethodTable:** `{Addr(details.MethodTable)}`");

			if (!string.IsNullOrEmpty(details.Value)) {
				sb.AppendLine($"**Value:** {details.Value}");
			}

			if (details.Fields.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("| Offset (hex) | Name | Type | Value | Address |");
				sb.AppendLine("|--------------|------|------|-------|---------|");
				foreach (var field in details.Fields) {
					string addr = field.Address != 0 ? $"`{Addr(field.Address)}`" : "";
					string offset = field.Offset != -1 ? $"{field.Offset:X}" : "-";
					sb.AppendLine($"| {offset} | `{field.Name}` | `{field.TypeName}` | {field.Value} | {addr} |");
				}
			}
			return sb.ToString();
		}

		public static string FormatHeapSegments(HeapSummaryInfo summary) {
			var sb = new StringBuilder();

			sb.AppendLine($"**GC Flavour:** {(summary.IsServerGC ? "Server" : "Workstation")}");
			sb.AppendLine($"**GC Heaps:** {summary.SubHeapCount}");
			sb.AppendLine($"**Heap Walkable:** {summary.CanWalkHeap}");
			sb.AppendLine($"**DATAS (dynamic adaptation):** {(summary.DynamicAdaptationMode.HasValue ? summary.DynamicAdaptationMode.Value.ToString() : "off / not reported")}");
			sb.AppendLine();

			sb.AppendLine("| Segment Range | Kind | Gen | Size | Committed | Reserved | Heap |");
			sb.AppendLine("|---------------|------|-----|------|-----------|----------|------|");
			foreach (var item in summary.Segments) {
				// A regions-based GC reports Frozen/Ephemeral rather than a single generation, so an
				// ephemeral region has no one generation to name.
				string gen = item.Generation.HasValue ? item.Generation.Value.ToString() : "mixed";
				sb.AppendLine($"| {Addr(item.Start)} - {Addr(item.End)} | {item.Kind} | {gen} | {Bytes(item.Size)} | {Bytes(item.CommittedSize)} | {Bytes(item.ReservedSize)} | {item.SubHeapIndex} |");
			}

			var ephemeral = summary.Segments.Where(s => s.Gen0Size + s.Gen1Size + s.Gen2Size > 0).ToList();
			if (ephemeral.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("**Per-generation bytes within each segment:**");
				sb.AppendLine();
				sb.AppendLine("| Segment | Gen0 | Gen1 | Gen2 |");
				sb.AppendLine("|---------|------|------|------|");
				foreach (var item in ephemeral) {
					sb.AppendLine($"| {Addr(item.Start)} | {Bytes(item.Gen0Size)} | {Bytes(item.Gen1Size)} | {Bytes(item.Gen2Size)} |");
				}
			}

			var totals = summary.Segments;
			sb.AppendLine();
			sb.AppendLine($"**Total:** {Bytes(totals.Aggregate(0UL, (a, s) => a + s.Size))} bytes in {totals.Count} segment(s); " +
				$"{Bytes(totals.Aggregate(0UL, (a, s) => a + s.CommittedSize))} committed, " +
				$"{Bytes(totals.Aggregate(0UL, (a, s) => a + s.ReservedSize))} reserved.");

			return sb.ToString();
		}

		public static string FormatThreadPool(ThreadPoolInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Type:** {info.Type}");
			sb.AppendLine($"**Total Threads:** {info.TotalThreads}");
			sb.AppendLine($"**Active Threads:** {info.ActiveThreads}");
			sb.AppendLine($"**Idle Threads:** {info.IdleThreads}");
			sb.AppendLine($"**Retired Threads:** {info.RetiredThreads}");
			sb.AppendLine($"**Min Threads:** {info.MinThreads}");
			sb.AppendLine($"**Max Threads:** {info.MaxThreads}");

			if (info.CpuUtilization.HasValue) {
				sb.AppendLine($"**CPU Utilization:** {info.CpuUtilization}%");
			}

			if (info.HasCompletionPortData) {
				sb.AppendLine();
				sb.AppendLine("**Completion Ports:**");
				sb.AppendLine($"- Total: {info.TotalCompletionPorts}");
				sb.AppendLine($"- Free: {info.FreeCompletionPorts} (max free {info.MaxFreeCompletionPorts})");
				sb.AppendLine($"- Current limit: {info.CompletionPortCurrentLimit}");
				sb.AppendLine($"- Min / Max: {info.MinCompletionPorts} / {info.MaxCompletionPorts}");
			}

			return sb.ToString();
		}

		public static string FormatSyncBlocks(IEnumerable<SyncBlockInfo> blocks) {
			var list = blocks.ToList();
			var sb = new StringBuilder();

			if (list.Count == 0) {
				sb.AppendLine("**No held monitors found** (no sync blocks and no thin locks).");
				return sb.ToString();
			}

			sb.AppendLine("| Object | Type | Lock | Held | Owner Thread | Recursion | Waiting |");
			sb.AppendLine("|--------|------|------|------|--------------|-----------|---------|");
			foreach (var block in list) {
				string owner = block.ManagedThreadId.HasValue
					? block.ManagedThreadId.Value.ToString()
					: (block.HoldingThreadAddress != 0 ? $"@{block.HoldingThreadAddress:X}" : "none");
				string kind = block.IsThinLock ? "thin" : "syncblk";
				sb.AppendLine($"| {Addr(block.ObjectAddress)} | {block.TypeName ?? "<unknown>"} | {kind} | {block.IsMonitorHeld} | {owner} | {block.RecursionCount} | {block.WaitingThreadCount} |");
			}

			if (list.Any(b => b.IsThinLock)) {
				sb.AppendLine();
				sb.AppendLine("_Thin locks are uncontended monitors stored in the object header; the runtime " +
					"allocates no sync block for them._");
			}

			return sb.ToString();
		}

		public static string FormatGCHandles(IEnumerable<GCHandleInfo> handles) {
			var sb = new StringBuilder();
			sb.AppendLine("| Handle | Object | Kind | Strong | RefCount | Dependent | Type |");
			sb.AppendLine("|--------|--------|------|--------|----------|-----------|------|");
			foreach (var handle in handles) {
				string dependent = handle.DependentTarget != 0 ? Addr(handle.DependentTarget) : "-";
				string refCount = handle.ReferenceCount != 0 ? handle.ReferenceCount.ToString() : "-";
				sb.AppendLine($"| {Addr(handle.Address)} | {Addr(handle.Object)} | {handle.Kind} | {handle.IsStrong} | {refCount} | {dependent} | {handle.TypeName} |");
			}
			return sb.ToString();
		}

		public static string FormatGCHandleStatistics(IEnumerable<GCHandleStatItem> stats) {
			var sb = new StringBuilder();
			sb.AppendLine("| Kind | Count | Strong | Total Size of Targets |");
			sb.AppendLine("|------|-------|--------|-----------------------|");
			foreach (var item in stats) {
				sb.AppendLine($"| {item.Kind} | {Num(item.Count)} | {Num(item.StrongCount)} | {Bytes(item.TotalSize)} |");
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

			sb.AppendLine("**Heap Verification Result:** FAILED");
			sb.AppendLine();
			sb.AppendLine($"**Corruption Count:** {corruptionList.Count}");
			sb.AppendLine();
			sb.AppendLine("| Object | Kind | Offset (hex) | Type | Detail |");
			sb.AppendLine("|--------|------|--------------|------|--------|");
			foreach (var corruption in corruptionList) {
				sb.AppendLine($"| {Addr(corruption.Object)} | {corruption.Kind} | {corruption.Offset:X} | {corruption.TypeName ?? "<unknown>"} | {corruption.Message} |");
			}
			return sb.ToString();
		}

		public static string FormatDetailedStacks(IEnumerable<ThreadStackInfo> stacks) {
			var sb = new StringBuilder();

			foreach (var stack in stacks) {
				// Header names the actual thread; a positional counter reads as a different thread id.
				sb.AppendLine($"### Thread {stack.ManagedThreadId} (OS ID {stack.OSThreadId:X})");

				if (!string.IsNullOrEmpty(stack.ExceptionType)) {
					sb.AppendLine($"**Exception:** {stack.ExceptionType}");
				}

				sb.AppendLine();
				sb.AppendLine("| IP | SP | Kind | Method |");
				sb.AppendLine("|----|----|------|--------|");

				foreach (var frame in stack.Frames) {
					string method = frame.MethodName ?? "(unknown)";
					if (!string.IsNullOrEmpty(frame.ModuleName)) {
						method = $"{frame.ModuleName}!{method}";
					}
					sb.AppendLine($"| {Addr(frame.InstructionPointer)} | {Addr(frame.StackPointer)} | {frame.FrameKind} | {method} |");
				}

				sb.AppendLine();
			}

			return sb.ToString();
		}

		public static string FormatMethodTable(MethodTableInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**MethodTable:** {Addr(info.MethodTable)}");
			sb.AppendLine($"**Type:** {info.TypeName}");
			if (!string.IsNullOrEmpty(info.ModuleName)) {
				sb.AppendLine($"**Module:** {info.ModuleName}");
			}
			sb.AppendLine($"**BaseSize:** {Bytes(info.BaseSize)} bytes");
			if (info.ComponentSize > 0) {
				sb.AppendLine($"**ComponentSize:** {info.ComponentSize} bytes");
			}
			sb.AppendLine($"**Method Count:** {info.MethodCount}");
			sb.AppendLine($"**Metadata Token:** 0x{info.MetadataToken:X8}");
			sb.AppendLine($"**Visibility:** {info.Visibility}");
			sb.AppendLine();
			sb.AppendLine("**Flags:**");
			sb.AppendLine($"- ValueType: {info.IsValueType}");
			sb.AppendLine($"- Interface: {info.IsInterface}");
			sb.AppendLine($"- Abstract: {info.IsAbstract}");
			sb.AppendLine($"- Sealed: {info.IsSealed}");
			sb.AppendLine($"- Enum: {info.IsEnum}");
			sb.AppendLine($"- Array: {info.IsArray}");
			sb.AppendLine($"- String: {info.IsString}");
			sb.AppendLine($"- Finalizable: {info.IsFinalizable}");
			sb.AppendLine($"- ContainsPointers: {info.ContainsPointers}");

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

			sb.AppendLine();
			sb.AppendLine("_ClrMD does not expose the EEClass address separately from the MethodTable._");

			return sb.ToString();
		}

		public static string FormatMethodDesc(MethodDescInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**MethodDesc:** {Addr(info.MethodDesc)}");
			sb.AppendLine($"**MethodTable:** {Addr(info.MethodTable)}");
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
			sb.AppendLine($"- Native Code: {Addr(info.NativeCode)}");
			sb.AppendLine($"- Is Jitted: {info.IsJitted}");
			sb.AppendLine($"- Is Generic: {info.IsGeneric}");

			return sb.ToString();
		}

		public static string FormatClass(ClassInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**MethodTable:** {Addr(info.MethodTable)}");
			sb.AppendLine($"**Type:** {info.TypeName}");
			if (!string.IsNullOrEmpty(info.ModuleName)) {
				sb.AppendLine($"**Module:** {info.ModuleName}");
			}
			sb.AppendLine();
			sb.AppendLine($"**Field Count:** {info.FieldCount} instance, {info.StaticFieldCount} static, {info.ThreadStaticFieldCount} thread-static");
			sb.AppendLine($"**Method Count:** {info.MethodCount}");

			if (info.Fields.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("**Fields:**");
				sb.AppendLine("| Offset (hex) | Name | Type | Size | Static | Value |");
				sb.AppendLine("|--------------|------|------|------|--------|-------|");
				foreach (var field in info.Fields) {
					sb.AppendLine($"| {field.Offset:X} | {field.Name} | {field.TypeName} | {field.Size} | {field.IsStatic} | {field.Value ?? "-"} |");
				}
			}

			if (info.Methods.Count > 0) {
				sb.AppendLine();
				sb.AppendLine("**Methods:**");
				foreach (var method in info.Methods) {
					sb.AppendLine($"- {method}");
				}
			}

			if (info.IsTruncated) {
				sb.AppendLine();
				sb.AppendLine("_Field and method lists are truncated; see the counts above for the true totals._");
			}

			sb.AppendLine();
			sb.AppendLine("_ClrMD does not expose the EEClass address separately from the MethodTable, so this " +
				"tool takes a MethodTable address._");

			return sb.ToString();
		}

		public static string FormatModuleDetails(ModuleDetails info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Module:** {info.Name}");
			sb.AppendLine($"**Assembly:** {info.AssemblyName}");
			if (!string.IsNullOrEmpty(info.AppDomainName)) {
				sb.AppendLine($"**AppDomain:** {info.AppDomainName}");
			}
			sb.AppendLine();
			sb.AppendLine("**Addresses:**");
			sb.AppendLine($"- ImageBase: {Addr(info.ImageBase)}");
			sb.AppendLine($"- MetadataAddress: {Addr(info.MetadataAddress)}");
			sb.AppendLine($"- Assembly: {Addr(info.AssemblyAddress)}");
			sb.AppendLine();
			sb.AppendLine($"**Size:** {Bytes(info.Size)} bytes");
			sb.AppendLine($"**Metadata Length:** {Num(info.MetadataLength)} bytes");
			sb.AppendLine($"**Type Count:** {Num(info.TypeCount)}");
			sb.AppendLine($"**Types With Static Fields:** {Num(info.TypesWithStaticFieldsCount)}");
			sb.AppendLine();
			sb.AppendLine("**Flags:**");
			sb.AppendLine($"- IsDynamic: {info.IsDynamic}");
			sb.AppendLine($"- IsPEFile: {info.IsPEFile}");
			sb.AppendLine($"- Layout: {info.Layout}");

			return sb.ToString();
		}

		public static string FormatAssemblyDetails(AssemblyDetails info) {
			var sb = new StringBuilder();
			sb.AppendLine($"**Assembly:** {info.Name}");
			sb.AppendLine($"**Address:** {Addr(info.AssemblyAddress)}");
			sb.AppendLine($"**IsDynamic:** {info.IsDynamic}");
			if (!string.IsNullOrEmpty(info.AppDomainName)) {
				sb.AppendLine($"**AppDomain:** {info.AppDomainName}");
			}
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
			sb.AppendLine($"**MethodTable:** {Addr(info.MethodTable)}");

			if (!string.IsNullOrEmpty(info.MethodName)) {
				sb.AppendLine();
				sb.AppendLine($"**Method:** {info.MethodName}");

				if (info.Methods.Count > 0) {
					sb.AppendLine();
					sb.AppendLine("**Overloads:**");
					sb.AppendLine("| MethodDesc | Signature | Jitted |");
					sb.AppendLine("|------------|-----------|--------|");
					foreach (var method in info.Methods) {
						sb.AppendLine($"| {Addr(method.MethodDesc)} | {method.Signature} | {method.IsJitted} |");
					}
				}
			}

			return sb.ToString();
		}

		public static string FormatThreadStates(IEnumerable<ThreadStateInfo> states) {
			var sb = new StringBuilder();
			sb.AppendLine("| Mgd ID | OS ID (hex) | Address | GC Mode | Apartment | Locks | Flags |");
			sb.AppendLine("|--------|-------------|---------|---------|-----------|-------|-------|");
			foreach (var state in states) {
				var flags = new List<string>();
				if (state.IsGC) flags.Add("GC");
				if (state.IsFinalizer) flags.Add("Finalizer");
				if (state.IsThreadPoolThread) flags.Add("ThreadPool");
				if (state.IsBackground) flags.Add("Background");
				if (state.IsUnstarted) flags.Add("Unstarted");
				if (state.IsDead) flags.Add("Dead");
				if (state.IsAborted) flags.Add("Aborted");
				if (state.IsSuspendPending) flags.Add("SuspendPending");
				if (state.ExceptionType != null) flags.Add("Exception");
				string flagsStr = flags.Count > 0 ? string.Join(", ", flags) : "-";

				// A null lock count means the runtime did not report one — not zero, and not -1.
				string locks = state.LockCount.HasValue ? state.LockCount.Value.ToString() : "unknown";

				sb.AppendLine($"| {state.ManagedThreadId} | {state.OSThreadId:X} | {Addr(state.Address)} | {state.GcMode} | {state.ApartmentState} | {locks} | {flagsStr} |");
			}
			return sb.ToString();
		}

		public static string FormatThreadExceptions(IEnumerable<ThreadExceptionInfo> exceptionInfos) {
			var sb = new StringBuilder();
			var infoList = exceptionInfos.Where(i => i.Exception != null).ToList();

			if (infoList.Count == 0) {
				sb.AppendLine("**No exceptions found** — none in flight on a thread, and none on the heap.");
				return sb.ToString();
			}

			int inFlight = infoList.Count(i => i.Source == ExceptionSource.ThreadCurrentException);
			sb.AppendLine($"Found {infoList.Count} exception(s): {inFlight} in flight on a thread, " +
				$"{infoList.Count - inFlight} reachable on the heap.");
			sb.AppendLine();

			foreach (var info in infoList) {
				string heading = info.ManagedThreadId.HasValue
					? $"### Thread {info.ManagedThreadId} (OS ID {info.OSThreadId:X}) — in flight"
					: $"### Heap exception at {Addr(info.Exception!.Address)}";

				sb.AppendLine(heading);
				sb.AppendLine();
				FormatExceptionDetails(sb, info.Exception!, 0);
				sb.AppendLine();
			}

			return sb.ToString();
		}

		private static void FormatExceptionDetails(StringBuilder sb, ExceptionDetails exception, int depth) {
			string indent = new string(' ', depth * 2);
			string prefix = depth == 0 ? "**Exception:**" : "**Inner Exception:**";

			sb.AppendLine($"{indent}{prefix}");
			sb.AppendLine($"{indent}- **Address:** {Addr(exception.Address)}");
			sb.AppendLine($"{indent}- **Type:** {exception.TypeName}");
			if (!string.IsNullOrEmpty(exception.Message)) {
				sb.AppendLine($"{indent}- **Message:** {exception.Message}");
			}
			sb.AppendLine($"{indent}- **HResult:** 0x{exception.HResult:X8}");

			if (exception.StackTrace.Count > 0) {
				sb.AppendLine($"{indent}- **Stack Trace:**");
				sb.AppendLine($"{indent}```text");
				foreach (var frame in exception.StackTrace.Take(20)) {
					sb.AppendLine($"{indent}{frame}");
				}
				if (exception.StackTrace.Count > 20) {
					sb.AppendLine($"{indent}... ({exception.StackTrace.Count - 20} more frames)");
				}
				sb.AppendLine($"{indent}```");
			}

			foreach (var inner in exception.InnerExceptions) {
				sb.AppendLine();
				FormatExceptionDetails(sb, inner, depth + 1);
			}
		}
	}
}