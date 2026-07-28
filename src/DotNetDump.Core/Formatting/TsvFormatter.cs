using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using DotNetDump.Core.Models;

namespace DotNetDump.Core.Formatting {
	/// <summary>
	/// Renders the same strongly-typed models as <see cref="MarkdownFormatter"/>, one method per
	/// method, as tab-separated values for `grep`/`awk`/`cut` pipelines (CLI_DESIGN.md §3.3).
	///
	/// Deliberately dumb: a header row, then one data row per item, tab-separated. No padding, no
	/// alignment, no narrative text ("PASSED", "no exceptions found", etc.) — an empty result is
	/// simply a header with no rows beneath it.
	///
	/// A handful of methods return a single object that nests a list (<c>HeapSummaryInfo.Segments</c>,
	/// <c>ClassInfo.Fields</c>, <c>ThreadStackInfo.Frames</c>, ...). TSV has no nested-row concept, so:
	/// <list type="bullet">
	/// <item>Where the nested list is the naturally tabular part of the result (heap segments), each
	/// element becomes its own row, with the parent's scalar fields repeated on every row.</item>
	/// <item>Otherwise (a single detail object with an incidental list field, e.g. `MethodTable`'s
	/// interfaces, `Class`'s fields/methods), the list is flattened into one cell, semicolon-joined,
	/// then escaped like any other value.</item>
	/// </list>
	/// </summary>
	public static class TsvFormatter {
		/// <summary>Addresses render bare and uppercase, matching <see cref="MarkdownFormatter"/>'s <c>Addr</c>.</summary>
		private static string Addr(ulong value) => value.ToString("X16");

		private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

		private static string Bool(bool value) => value ? "true" : "false";

		/// <summary>Escapes backslashes first, then the characters that would otherwise break the
		/// tabular structure (a literal tab would add a column; a literal newline would add a row).</summary>
		private static string Escape(string? value) {
			if (string.IsNullOrEmpty(value)) {
				return string.Empty;
			}

			return value
				.Replace("\\", "\\\\")
				.Replace("\t", "\\t")
				.Replace("\r", "\\r")
				.Replace("\n", "\\n");
		}

		private static string Join(IEnumerable<string> values) => Escape(string.Join("; ", values));

		private static string Header(params string[] names) => string.Join("\t", names);

		private static string Row(params string?[] values) => string.Join("\t", values.Select(v => Escape(v ?? string.Empty)));

		private static string SourceName(ExceptionSource source) => source switch {
			ExceptionSource.ThreadCurrentException => "threadCurrentException",
			ExceptionSource.Heap => "heap",
			ExceptionSource.Address => "address",
			_ => "unknown",
		};

		/// <summary>Backs the CLI's `info` command (CLI_DESIGN.md &#0167;4.1); no MCP tool equivalent.</summary>
		public static string FormatInfo(DumpInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("runtimeVersion", "runtimeFlavor", "architecture", "operatingSystem", "expectedDacFileName", "explicitDacPath", "dacMatchVerified", "isServerGC", "subHeapCount", "heapSizeBytes", "segmentCount", "managedThreadCount"));
			sb.AppendLine(Row(
				info.RuntimeVersion,
				info.RuntimeFlavor,
				info.Architecture,
				info.OperatingSystem,
				info.ExpectedDacFileName,
				info.ExplicitDacPath,
				Bool(info.DacMatchVerified),
				Bool(info.IsServerGC),
				Num(info.SubHeapCount),
				info.HeapSizeBytes.ToString(CultureInfo.InvariantCulture),
				Num(info.SegmentCount),
				Num(info.ManagedThreadCount)));
			return sb.ToString();
		}

		public static string FormatHeapStatistics(IEnumerable<HeapStatItem> stats) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("methodTable", "count", "totalSize", "typeName"));
			foreach (var item in stats) {
				sb.AppendLine(Row(Addr(item.MethodTable), Num(item.Count), item.TotalSize.ToString(CultureInfo.InvariantCulture), item.TypeName));
			}
			return sb.ToString();
		}

		public static string FormatHeapObjects(IEnumerable<HeapObjectItem> objects) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("address", "methodTable", "size", "typeName"));
			foreach (var item in objects) {
				sb.AppendLine(Row(Addr(item.Address), Addr(item.MethodTable), item.Size.ToString(CultureInfo.InvariantCulture), item.TypeName));
			}
			return sb.ToString();
		}

		public static string FormatThreads(IEnumerable<ThreadInfo> threads) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("managedThreadId", "osThreadId", "isAlive", "exceptionType", "exceptionMessage"));
			foreach (var item in threads) {
				sb.AppendLine(Row(Num(item.ManagedThreadId), item.OSThreadId.ToString(CultureInfo.InvariantCulture), Bool(item.IsAlive), item.ExceptionType, item.ExceptionMessage));
			}
			return sb.ToString();
		}

		public static string FormatModules(IEnumerable<ModuleInfo> modules) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("name", "imageBase", "size", "isUserCode"));
			foreach (var item in modules) {
				sb.AppendLine(Row(item.Name, Addr(item.ImageBase), item.Size.ToString(CultureInfo.InvariantCulture), Bool(item.IsUserCode)));
			}
			return sb.ToString();
		}

		public static string FormatStackGroups(IEnumerable<StackGroup> groups) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("managedThreadIds", "frames", "threadCount"));
			foreach (var group in groups) {
				sb.AppendLine(Row(Join(group.ManagedThreadIds.Select(id => id.ToString(CultureInfo.InvariantCulture))), Join(group.Frames), Num(group.ThreadCount)));
			}
			return sb.ToString();
		}

		/// <summary>One row per node-disjoint path. <c>targetAddress</c> repeats the function
		/// argument on every row, matching the model's own per-path <c>TargetAddress</c> field.</summary>
		public static string FormatGCRootPaths(IEnumerable<GCRootPathInfo> paths, ulong targetAddress) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("rootAddress", "rootKind", "rootName", "managedThreadId", "osThreadId", "isPinned", "isInterior", "targetAddress", "depth", "path"));
			foreach (var path in paths) {
				string pathCells = Join(path.Path.Select(node => $"{Addr(node.Address)}|{node.TypeName}|{node.Size}"));
				sb.AppendLine(Row(
					Addr(path.RootAddress),
					path.RootKind,
					path.RootName,
					path.ManagedThreadId?.ToString(CultureInfo.InvariantCulture),
					path.OSThreadId?.ToString(CultureInfo.InvariantCulture),
					Bool(path.IsPinned),
					Bool(path.IsInterior),
					Addr(path.TargetAddress),
					Num(path.Depth),
					pathCells));
			}
			return sb.ToString();
		}

		public static string FormatObjectDetails(ObjectDetails details) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("address", "typeName", "size", "methodTable", "value", "fields"));
			string fieldCells = Join(details.Fields.Select(f => $"{f.Name}|{f.TypeName}|{f.Value}|{Addr(f.Address)}|{Bool(f.IsReference)}|{f.Offset}"));
			sb.AppendLine(Row(Addr(details.Address), details.TypeName, details.Size.ToString(CultureInfo.InvariantCulture), Addr(details.MethodTable), details.Value, fieldCells));
			return sb.ToString();
		}

		/// <summary>Segments are the tabular part of a heap-segments result; the summary's own
		/// scalar fields repeat on every row so each row stays independently meaningful.</summary>
		public static string FormatHeapSegments(HeapSummaryInfo summary) {
			var sb = new StringBuilder();
			sb.AppendLine(Header(
				"isServerGC", "subHeapCount", "canWalkHeap", "dynamicAdaptationMode",
				"start", "end", "size", "generation", "kind", "isLargeObjectHeap", "isPinnedObjectHeap",
				"committedSize", "reservedSize", "gen0Size", "gen1Size", "gen2Size", "subHeapIndex"));

			foreach (var segment in summary.Segments) {
				sb.AppendLine(Row(
					Bool(summary.IsServerGC),
					Num(summary.SubHeapCount),
					Bool(summary.CanWalkHeap),
					summary.DynamicAdaptationMode?.ToString(CultureInfo.InvariantCulture),
					Addr(segment.Start),
					Addr(segment.End),
					segment.Size.ToString(CultureInfo.InvariantCulture),
					segment.Generation?.ToString(CultureInfo.InvariantCulture),
					segment.Kind,
					Bool(segment.IsLargeObjectHeap),
					Bool(segment.IsPinnedObjectHeap),
					segment.CommittedSize.ToString(CultureInfo.InvariantCulture),
					segment.ReservedSize.ToString(CultureInfo.InvariantCulture),
					segment.Gen0Size.ToString(CultureInfo.InvariantCulture),
					segment.Gen1Size.ToString(CultureInfo.InvariantCulture),
					segment.Gen2Size.ToString(CultureInfo.InvariantCulture),
					Num(segment.SubHeapIndex)));
			}

			return sb.ToString();
		}

		public static string FormatThreadPool(ThreadPoolInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header(
				"totalThreads", "activeThreads", "idleThreads", "retiredThreads", "minThreads", "maxThreads",
				"type", "cpuUtilization", "hasCompletionPortData", "totalCompletionPorts", "freeCompletionPorts",
				"maxFreeCompletionPorts", "completionPortCurrentLimit", "minCompletionPorts", "maxCompletionPorts"));

			sb.AppendLine(Row(
				Num(info.TotalThreads),
				Num(info.ActiveThreads),
				Num(info.IdleThreads),
				Num(info.RetiredThreads),
				Num(info.MinThreads),
				Num(info.MaxThreads),
				info.Type,
				info.CpuUtilization?.ToString(CultureInfo.InvariantCulture),
				Bool(info.HasCompletionPortData),
				Num(info.TotalCompletionPorts),
				Num(info.FreeCompletionPorts),
				Num(info.MaxFreeCompletionPorts),
				Num(info.CompletionPortCurrentLimit),
				Num(info.MinCompletionPorts),
				Num(info.MaxCompletionPorts)));

			return sb.ToString();
		}

		public static string FormatSyncBlocks(IEnumerable<SyncBlockInfo> blocks) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("objectAddress", "typeName", "isMonitorHeld", "holdingThreadAddress", "recursionCount", "waitingThreadCount", "managedThreadId", "osThreadId", "isThinLock"));
			foreach (var block in blocks) {
				sb.AppendLine(Row(
					Addr(block.ObjectAddress),
					block.TypeName,
					Bool(block.IsMonitorHeld),
					Addr(block.HoldingThreadAddress),
					Num(block.RecursionCount),
					Num(block.WaitingThreadCount),
					block.ManagedThreadId?.ToString(CultureInfo.InvariantCulture),
					block.OSThreadId?.ToString(CultureInfo.InvariantCulture),
					Bool(block.IsThinLock)));
			}
			return sb.ToString();
		}

		public static string FormatGCHandles(IEnumerable<GCHandleInfo> handles) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("address", "object", "kind", "typeName", "isStrong", "referenceCount", "dependentTarget", "appDomainName", "size"));
			foreach (var handle in handles) {
				sb.AppendLine(Row(
					Addr(handle.Address),
					Addr(handle.Object),
					handle.Kind,
					handle.TypeName,
					Bool(handle.IsStrong),
					handle.ReferenceCount.ToString(CultureInfo.InvariantCulture),
					Addr(handle.DependentTarget),
					handle.AppDomainName,
					handle.Size.ToString(CultureInfo.InvariantCulture)));
			}
			return sb.ToString();
		}

		public static string FormatGCHandleStatistics(IEnumerable<GCHandleStatItem> stats) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("kind", "count", "strongCount", "totalSize"));
			foreach (var item in stats) {
				sb.AppendLine(Row(item.Kind, Num(item.Count), Num(item.StrongCount), item.TotalSize.ToString(CultureInfo.InvariantCulture)));
			}
			return sb.ToString();
		}

		public static string FormatHeapVerification(IEnumerable<HeapCorruptionInfo> corruptions) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("address", "object", "kind", "message", "offset", "typeName"));
			foreach (var item in corruptions) {
				sb.AppendLine(Row(Addr(item.Address), Addr(item.Object), item.Kind, item.Message, Num(item.Offset), item.TypeName));
			}
			return sb.ToString();
		}

		public static string FormatDetailedStacks(IEnumerable<ThreadStackInfo> stacks) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("managedThreadId", "osThreadId", "isAlive", "exceptionType", "frames"));
			foreach (var stack in stacks) {
				string frameCells = Join(stack.Frames.Select(f => $"{Addr(f.InstructionPointer)}|{Addr(f.StackPointer)}|{f.FrameKind}|{f.MethodName}|{f.ModuleName}|{Bool(f.IsManaged)}"));
				sb.AppendLine(Row(Num(stack.ManagedThreadId), stack.OSThreadId.ToString(CultureInfo.InvariantCulture), Bool(stack.IsAlive), stack.ExceptionType, frameCells));
			}
			return sb.ToString();
		}

		public static string FormatMethodTable(MethodTableInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header(
				"methodTable", "eeClass", "typeName", "moduleName", "baseSize", "componentSize", "methodCount",
				"metadataToken", "isValueType", "isInterface", "isAbstract", "isSealed", "isEnum", "isArray",
				"isString", "isFinalizable", "containsPointers", "visibility", "baseTypeName", "interfaces"));

			sb.AppendLine(Row(
				Addr(info.MethodTable),
				Addr(info.EEClass),
				info.TypeName,
				info.ModuleName,
				info.BaseSize.ToString(CultureInfo.InvariantCulture),
				Num(info.ComponentSize),
				Num(info.MethodCount),
				Num(info.MetadataToken),
				Bool(info.IsValueType),
				Bool(info.IsInterface),
				Bool(info.IsAbstract),
				Bool(info.IsSealed),
				Bool(info.IsEnum),
				Bool(info.IsArray),
				Bool(info.IsString),
				Bool(info.IsFinalizable),
				Bool(info.ContainsPointers),
				info.Visibility,
				info.BaseTypeName,
				Join(info.Interfaces)));

			return sb.ToString();
		}

		public static string FormatMethodDesc(MethodDescInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("methodDesc", "methodTable", "methodName", "typeName", "moduleName", "signature", "nativeCode", "isJitted", "isGeneric", "metadataToken"));
			sb.AppendLine(Row(
				Addr(info.MethodDesc),
				Addr(info.MethodTable),
				info.MethodName,
				info.TypeName,
				info.ModuleName,
				info.Signature,
				Addr(info.NativeCode),
				Bool(info.IsJitted),
				Bool(info.IsGeneric),
				Num(info.MetadataToken)));
			return sb.ToString();
		}

		public static string FormatClass(ClassInfo info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("eeClass", "methodTable", "typeName", "moduleName", "fieldCount", "staticFieldCount", "threadStaticFieldCount", "methodCount", "isTruncated", "fields", "methods"));

			string fieldCells = Join(info.Fields.Select(f => $"{f.Name}|{f.TypeName}|{f.Offset}|{Bool(f.IsStatic)}|{Bool(f.IsThreadStatic)}|{f.Size}|{f.Value}"));
			sb.AppendLine(Row(
				Addr(info.EEClass),
				Addr(info.MethodTable),
				info.TypeName,
				info.ModuleName,
				Num(info.FieldCount),
				Num(info.StaticFieldCount),
				Num(info.ThreadStaticFieldCount),
				Num(info.MethodCount),
				Bool(info.IsTruncated),
				fieldCells,
				Join(info.Methods)));

			return sb.ToString();
		}

		public static string FormatModuleDetails(ModuleDetails info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("address", "name", "assemblyName", "imageBase", "size", "metadataAddress", "metadataLength", "assemblyAddress", "isDynamic", "isPEFile", "layout", "appDomainName", "typeCount", "typesWithStaticFieldsCount"));
			sb.AppendLine(Row(
				Addr(info.Address),
				info.Name,
				info.AssemblyName,
				Addr(info.ImageBase),
				info.Size.ToString(CultureInfo.InvariantCulture),
				Addr(info.MetadataAddress),
				Num(info.MetadataLength),
				Addr(info.AssemblyAddress),
				Bool(info.IsDynamic),
				Bool(info.IsPEFile),
				info.Layout,
				info.AppDomainName,
				Num(info.TypeCount),
				Num(info.TypesWithStaticFieldsCount)));
			return sb.ToString();
		}

		public static string FormatAssemblyDetails(AssemblyDetails info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("assemblyAddress", "name", "isDynamic", "appDomainName", "modules"));
			sb.AppendLine(Row(Addr(info.AssemblyAddress), info.Name, Bool(info.IsDynamic), info.AppDomainName, Join(info.Modules)));
			return sb.ToString();
		}

		public static string FormatName2EE(Name2EEResult info) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("moduleName", "typeName", "methodName", "methodTable", "eeClass", "methods"));
			string methodCells = Join(info.Methods.Select(m => $"{Addr(m.MethodDesc)}|{m.Signature}|{Bool(m.IsJitted)}"));
			sb.AppendLine(Row(info.ModuleName, info.TypeName, info.MethodName, Addr(info.MethodTable), Addr(info.EEClass), methodCells));
			return sb.ToString();
		}

		public static string FormatThreadStates(IEnumerable<ThreadStateInfo> states) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("managedThreadId", "osThreadId", "isAlive", "exceptionType", "address", "gcMode", "lockCount", "apartmentState", "isThreadPoolThread", "isGC", "isFinalizer", "isBackground", "isUnstarted", "isDead", "isAborted", "isSuspendPending", "stateFlags"));
			foreach (var state in states) {
				sb.AppendLine(Row(
					Num(state.ManagedThreadId),
					state.OSThreadId.ToString(CultureInfo.InvariantCulture),
					Bool(state.IsAlive),
					state.ExceptionType,
					Addr(state.Address),
					state.GcMode,
					state.LockCount?.ToString(CultureInfo.InvariantCulture),
					state.ApartmentState,
					Bool(state.IsThreadPoolThread),
					Bool(state.IsGC),
					Bool(state.IsFinalizer),
					Bool(state.IsBackground),
					Bool(state.IsUnstarted),
					Bool(state.IsDead),
					Bool(state.IsAborted),
					Bool(state.IsSuspendPending),
					Join(state.StateFlags)));
			}
			return sb.ToString();
		}

		/// <summary>Matches MarkdownFormatter: entries without an exception are dropped rather than
		/// emitted as a row of blanks.</summary>
		public static string FormatThreadExceptions(IEnumerable<ThreadExceptionInfo> exceptionInfos) {
			var sb = new StringBuilder();
			sb.AppendLine(Header("managedThreadId", "osThreadId", "source", "exceptionAddress", "typeName", "message", "hResult", "stackTrace", "innerExceptionCount"));
			foreach (var info in exceptionInfos.Where(i => i.Exception != null)) {
				var exception = info.Exception!;
				sb.AppendLine(Row(
					info.ManagedThreadId?.ToString(CultureInfo.InvariantCulture),
					info.OSThreadId?.ToString(CultureInfo.InvariantCulture),
					SourceName(info.Source),
					Addr(exception.Address),
					exception.TypeName,
					exception.Message,
					exception.HResult.ToString(CultureInfo.InvariantCulture),
					Join(exception.StackTrace),
					Num(exception.InnerExceptions.Count)));
			}
			return sb.ToString();
		}
	}
}