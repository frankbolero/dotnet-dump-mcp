using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotNetDump.Core.Models;

namespace DotNetDump.Core.Formatting {
	/// <summary>
	/// Renders the same strongly-typed models as <see cref="MarkdownFormatter"/>, one method per
	/// method, as a JSON API contract rather than a `jq` convenience (CLI_DESIGN.md §10.3).
	///
	/// Contract rules, applied uniformly:
	/// <list type="bullet">
	/// <item>Every public property has an explicit <see cref="JsonPropertyNameAttribute"/> — field
	/// names are chosen deliberately and do not drift if a Core model property is renamed.</item>
	/// <item>camelCase names; addresses render as 16-character uppercase hex with no <c>0x</c>
	/// prefix, matching <see cref="MarkdownFormatter"/>'s <c>Addr</c> convention.</item>
	/// <item>Null fields are omitted. A non-nullable address (e.g. <c>ulong</c> zero) is not null and
	/// always renders as its hex form; only genuinely nullable model fields disappear.</item>
	/// <item>Every collection-returning method wraps its rows in <c>{ "data": [...], "pagination":
	/// {...} }```; every single-item method wraps its result in <c>{ "data": {...} }</c>.</item>
	/// </list>
	///
	/// <see cref="PaginationInfo"/> always reports <c>total</c>/<c>offset</c>/<c>limit</c>/<c>hasMore</c>
	/// (CLI_DESIGN.md §10.3), but the numbers are only as good as what the caller can supply. The four
	/// methods backing Phase 4's cached walk-scale analyzer sites (<see cref="FormatHeapStatistics"/>,
	/// <see cref="FormatHeapObjects"/>, <see cref="FormatSyncBlocks"/>, <see cref="FormatThreadExceptions"/>)
	/// take a <c>PagedResult&lt;T&gt;</c> and report the real pre-pagination total. Every other
	/// collection method still receives an already-<c>Skip().Take()</c>'d <see cref="IEnumerable{T}"/>
	/// from an analyzer that has not been split this way, so the pre-pagination total never reaches
	/// this layer for those; <see cref="PaginationInfo.FromItemsOnly"/> reports the only honest thing
	/// available — what is actually in <c>data</c> — rather than guessing at <c>hasMore</c>.
	/// </summary>
	public static class JsonFormatter {
		private static readonly JsonSerializerOptions SerializerOptions = new() {
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			WriteIndented = true,
		};

		/// <summary>Addresses render bare and uppercase, matching <see cref="MarkdownFormatter"/>'s <c>Addr</c>.</summary>
		private static string Addr(ulong value) => value.ToString("X16");

		private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

		private static string SourceName(ExceptionSource source) => source switch {
			ExceptionSource.ThreadCurrentException => "threadCurrentException",
			ExceptionSource.Heap => "heap",
			ExceptionSource.Address => "address",
			_ => "unknown",
		};

		// ---- Envelope shapes ----------------------------------------------------------------

		private sealed class PaginationInfo {
			[JsonPropertyName("total")]
			public int Total { get; init; }

			[JsonPropertyName("offset")]
			public int Offset { get; init; }

			[JsonPropertyName("limit")]
			public int Limit { get; init; }

			[JsonPropertyName("hasMore")]
			public bool HasMore { get; init; }

			/// <summary>
			/// For a collection that reaches this layer already sliced by its analyzer, with no
			/// <c>PagedResult&lt;T&gt;</c> to report the true pre-pagination total. <c>total</c> and
			/// <c>limit</c> collapse to what is actually in <c>data</c>, and <c>hasMore</c> is reported
			/// <c>false</c> rather than guessed -- this is honestly all that is known at this layer.
			/// </summary>
			public static PaginationInfo FromItemsOnly(int count) => new() {
				Total = count,
				Offset = 0,
				Limit = count,
				HasMore = false,
			};

			/// <summary>The real thing: total/offset/limit as computed by the analyzer, hasMore derived from them.</summary>
			public static PaginationInfo FromPagedResult<T>(PagedResult<T> result) => new() {
				Total = result.TotalAvailable,
				Offset = result.Offset,
				Limit = result.Limit,
				HasMore = result.HasMore,
			};
		}

		private sealed class CollectionEnvelope<T> {
			[JsonPropertyName("data")]
			public List<T> Data { get; init; } = new();

			[JsonPropertyName("pagination")]
			public PaginationInfo Pagination { get; init; } = new();
		}

		private sealed class ItemEnvelope<T> {
			[JsonPropertyName("data")]
			public T Data { get; init; } = default!;
		}

		/// <summary>
		/// <c>targetAddress</c>, <c>nodesVisited</c> and <c>truncated</c> are search-level facts that
		/// do not belong to any one path, so they ride as siblings of <c>data</c> rather than being
		/// dropped or repeated per row. <c>truncated</c> is the field a consumer must check before
		/// treating an empty <c>data</c> as proof the object is unrooted — see
		/// docs/GCROOT_TRUNCATION.md.
		/// </summary>
		private sealed class GCRootPathsEnvelope {
			[JsonPropertyName("targetAddress")]
			public string TargetAddress { get; init; } = "";

			[JsonPropertyName("nodesVisited")]
			public long NodesVisited { get; init; }

			[JsonPropertyName("truncated")]
			public bool Truncated { get; init; }

			[JsonPropertyName("data")]
			public List<GCRootPathInfoDto> Data { get; init; } = new();

			[JsonPropertyName("pagination")]
			public PaginationInfo Pagination { get; init; } = new();
		}

		// ---- DTOs -----------------------------------------------------------------------------

		private sealed class HeapStatItemDto {
			[JsonPropertyName("methodTable")]
			public string MethodTable { get; init; } = "";

			[JsonPropertyName("count")]
			public int Count { get; init; }

			[JsonPropertyName("totalSize")]
			public long TotalSize { get; init; }

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }
		}

		private sealed class HeapObjectItemDto {
			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("methodTable")]
			public string MethodTable { get; init; } = "";

			[JsonPropertyName("size")]
			public ulong Size { get; init; }

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }
		}

		private sealed class ThreadInfoDto {
			[JsonPropertyName("managedThreadId")]
			public int ManagedThreadId { get; init; }

			[JsonPropertyName("osThreadId")]
			public uint OSThreadId { get; init; }

			[JsonPropertyName("isAlive")]
			public bool IsAlive { get; init; }

			[JsonPropertyName("exceptionType")]
			public string? ExceptionType { get; init; }

			[JsonPropertyName("exceptionMessage")]
			public string? ExceptionMessage { get; init; }
		}

		private sealed class ModuleInfoDto {
			[JsonPropertyName("name")]
			public string? Name { get; init; }

			[JsonPropertyName("imageBase")]
			public string ImageBase { get; init; } = "";

			[JsonPropertyName("size")]
			public ulong Size { get; init; }

			[JsonPropertyName("isUserCode")]
			public bool IsUserCode { get; init; }
		}

		private sealed class StackGroupDto {
			[JsonPropertyName("managedThreadIds")]
			public List<int> ManagedThreadIds { get; init; } = new();

			[JsonPropertyName("frames")]
			public List<string> Frames { get; init; } = new();

			[JsonPropertyName("threadCount")]
			public int ThreadCount { get; init; }
		}

		private sealed class GCRootPathNodeDto {
			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }

			[JsonPropertyName("size")]
			public ulong Size { get; init; }
		}

		private sealed class GCRootPathInfoDto {
			[JsonPropertyName("rootAddress")]
			public string RootAddress { get; init; } = "";

			[JsonPropertyName("rootKind")]
			public string? RootKind { get; init; }

			[JsonPropertyName("rootName")]
			public string? RootName { get; init; }

			[JsonPropertyName("managedThreadId")]
			public int? ManagedThreadId { get; init; }

			[JsonPropertyName("osThreadId")]
			public uint? OSThreadId { get; init; }

			[JsonPropertyName("isPinned")]
			public bool IsPinned { get; init; }

			[JsonPropertyName("isInterior")]
			public bool IsInterior { get; init; }

			[JsonPropertyName("targetAddress")]
			public string TargetAddress { get; init; } = "";

			[JsonPropertyName("depth")]
			public int Depth { get; init; }

			[JsonPropertyName("path")]
			public List<GCRootPathNodeDto> Path { get; init; } = new();
		}

		private sealed class ObjectFieldDto {
			[JsonPropertyName("name")]
			public string? Name { get; init; }

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }

			[JsonPropertyName("value")]
			public string? Value { get; init; }

			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("isReference")]
			public bool IsReference { get; init; }

			[JsonPropertyName("offset")]
			public int Offset { get; init; }
		}

		private sealed class ObjectDetailsDto {
			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }

			[JsonPropertyName("size")]
			public ulong Size { get; init; }

			[JsonPropertyName("methodTable")]
			public string MethodTable { get; init; } = "";

			[JsonPropertyName("value")]
			public string? Value { get; init; }

			[JsonPropertyName("fields")]
			public List<ObjectFieldDto> Fields { get; init; } = new();
		}

		private sealed class HeapSegmentInfoDto {
			[JsonPropertyName("start")]
			public string Start { get; init; } = "";

			[JsonPropertyName("end")]
			public string End { get; init; } = "";

			[JsonPropertyName("size")]
			public ulong Size { get; init; }

			[JsonPropertyName("generation")]
			public int? Generation { get; init; }

			[JsonPropertyName("kind")]
			public string Kind { get; init; } = "";

			[JsonPropertyName("isLargeObjectHeap")]
			public bool IsLargeObjectHeap { get; init; }

			[JsonPropertyName("isPinnedObjectHeap")]
			public bool IsPinnedObjectHeap { get; init; }

			[JsonPropertyName("committedSize")]
			public ulong CommittedSize { get; init; }

			[JsonPropertyName("reservedSize")]
			public ulong ReservedSize { get; init; }

			[JsonPropertyName("gen0Size")]
			public ulong Gen0Size { get; init; }

			[JsonPropertyName("gen1Size")]
			public ulong Gen1Size { get; init; }

			[JsonPropertyName("gen2Size")]
			public ulong Gen2Size { get; init; }

			[JsonPropertyName("subHeapIndex")]
			public int SubHeapIndex { get; init; }
		}

		private sealed class HeapSummaryInfoDto {
			[JsonPropertyName("isServerGC")]
			public bool IsServerGC { get; init; }

			[JsonPropertyName("subHeapCount")]
			public int SubHeapCount { get; init; }

			[JsonPropertyName("canWalkHeap")]
			public bool CanWalkHeap { get; init; }

			[JsonPropertyName("dynamicAdaptationMode")]
			public int? DynamicAdaptationMode { get; init; }

			[JsonPropertyName("segments")]
			public List<HeapSegmentInfoDto> Segments { get; init; } = new();
		}

		private sealed class ThreadPoolInfoDto {
			[JsonPropertyName("totalThreads")]
			public int TotalThreads { get; init; }

			[JsonPropertyName("activeThreads")]
			public int ActiveThreads { get; init; }

			[JsonPropertyName("idleThreads")]
			public int IdleThreads { get; init; }

			[JsonPropertyName("retiredThreads")]
			public int RetiredThreads { get; init; }

			[JsonPropertyName("minThreads")]
			public int MinThreads { get; init; }

			[JsonPropertyName("maxThreads")]
			public int MaxThreads { get; init; }

			[JsonPropertyName("type")]
			public string? Type { get; init; }

			[JsonPropertyName("cpuUtilization")]
			public int? CpuUtilization { get; init; }

			[JsonPropertyName("hasCompletionPortData")]
			public bool HasCompletionPortData { get; init; }

			[JsonPropertyName("totalCompletionPorts")]
			public int TotalCompletionPorts { get; init; }

			[JsonPropertyName("freeCompletionPorts")]
			public int FreeCompletionPorts { get; init; }

			[JsonPropertyName("maxFreeCompletionPorts")]
			public int MaxFreeCompletionPorts { get; init; }

			[JsonPropertyName("completionPortCurrentLimit")]
			public int CompletionPortCurrentLimit { get; init; }

			[JsonPropertyName("minCompletionPorts")]
			public int MinCompletionPorts { get; init; }

			[JsonPropertyName("maxCompletionPorts")]
			public int MaxCompletionPorts { get; init; }
		}

		private sealed class SyncBlockInfoDto {
			[JsonPropertyName("objectAddress")]
			public string ObjectAddress { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }

			[JsonPropertyName("isMonitorHeld")]
			public bool IsMonitorHeld { get; init; }

			[JsonPropertyName("holdingThreadAddress")]
			public string HoldingThreadAddress { get; init; } = "";

			[JsonPropertyName("recursionCount")]
			public int RecursionCount { get; init; }

			[JsonPropertyName("waitingThreadCount")]
			public int WaitingThreadCount { get; init; }

			[JsonPropertyName("managedThreadId")]
			public int? ManagedThreadId { get; init; }

			[JsonPropertyName("osThreadId")]
			public uint? OSThreadId { get; init; }

			[JsonPropertyName("isThinLock")]
			public bool IsThinLock { get; init; }
		}

		private sealed class GCHandleInfoDto {
			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("object")]
			public string Object { get; init; } = "";

			[JsonPropertyName("kind")]
			public string? Kind { get; init; }

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }

			[JsonPropertyName("isStrong")]
			public bool IsStrong { get; init; }

			[JsonPropertyName("referenceCount")]
			public uint ReferenceCount { get; init; }

			[JsonPropertyName("dependentTarget")]
			public string DependentTarget { get; init; } = "";

			[JsonPropertyName("appDomainName")]
			public string? AppDomainName { get; init; }

			[JsonPropertyName("size")]
			public ulong Size { get; init; }
		}

		private sealed class GCHandleStatItemDto {
			[JsonPropertyName("kind")]
			public string Kind { get; init; } = "";

			[JsonPropertyName("count")]
			public int Count { get; init; }

			[JsonPropertyName("strongCount")]
			public int StrongCount { get; init; }

			[JsonPropertyName("totalSize")]
			public ulong TotalSize { get; init; }
		}

		private sealed class HeapCorruptionInfoDto {
			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("object")]
			public string Object { get; init; } = "";

			[JsonPropertyName("kind")]
			public string Kind { get; init; } = "";

			[JsonPropertyName("message")]
			public string? Message { get; init; }

			[JsonPropertyName("offset")]
			public int Offset { get; init; }

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }
		}

		private sealed class StackFrameInfoDto {
			[JsonPropertyName("instructionPointer")]
			public string InstructionPointer { get; init; } = "";

			[JsonPropertyName("stackPointer")]
			public string StackPointer { get; init; } = "";

			[JsonPropertyName("frameKind")]
			public string FrameKind { get; init; } = "";

			[JsonPropertyName("methodName")]
			public string? MethodName { get; init; }

			[JsonPropertyName("moduleName")]
			public string? ModuleName { get; init; }

			[JsonPropertyName("isManaged")]
			public bool IsManaged { get; init; }
		}

		private sealed class ThreadStackInfoDto {
			[JsonPropertyName("managedThreadId")]
			public int ManagedThreadId { get; init; }

			[JsonPropertyName("osThreadId")]
			public uint OSThreadId { get; init; }

			[JsonPropertyName("isAlive")]
			public bool IsAlive { get; init; }

			[JsonPropertyName("exceptionType")]
			public string? ExceptionType { get; init; }

			[JsonPropertyName("frames")]
			public List<StackFrameInfoDto> Frames { get; init; } = new();
		}

		private sealed class MethodTableInfoDto {
			[JsonPropertyName("methodTable")]
			public string MethodTable { get; init; } = "";

			[JsonPropertyName("eeClass")]
			public string EEClass { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string TypeName { get; init; } = "";

			[JsonPropertyName("moduleName")]
			public string? ModuleName { get; init; }

			[JsonPropertyName("baseSize")]
			public ulong BaseSize { get; init; }

			[JsonPropertyName("componentSize")]
			public int ComponentSize { get; init; }

			[JsonPropertyName("methodCount")]
			public int MethodCount { get; init; }

			[JsonPropertyName("metadataToken")]
			public int MetadataToken { get; init; }

			[JsonPropertyName("isValueType")]
			public bool IsValueType { get; init; }

			[JsonPropertyName("isInterface")]
			public bool IsInterface { get; init; }

			[JsonPropertyName("isAbstract")]
			public bool IsAbstract { get; init; }

			[JsonPropertyName("isSealed")]
			public bool IsSealed { get; init; }

			[JsonPropertyName("isEnum")]
			public bool IsEnum { get; init; }

			[JsonPropertyName("isArray")]
			public bool IsArray { get; init; }

			[JsonPropertyName("isString")]
			public bool IsString { get; init; }

			[JsonPropertyName("isFinalizable")]
			public bool IsFinalizable { get; init; }

			[JsonPropertyName("containsPointers")]
			public bool ContainsPointers { get; init; }

			[JsonPropertyName("visibility")]
			public string Visibility { get; init; } = "";

			[JsonPropertyName("baseTypeName")]
			public string? BaseTypeName { get; init; }

			[JsonPropertyName("interfaces")]
			public List<string> Interfaces { get; init; } = new();
		}

		private sealed class MethodDescInfoDto {
			[JsonPropertyName("methodDesc")]
			public string MethodDesc { get; init; } = "";

			[JsonPropertyName("methodTable")]
			public string MethodTable { get; init; } = "";

			[JsonPropertyName("methodName")]
			public string MethodName { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }

			[JsonPropertyName("moduleName")]
			public string? ModuleName { get; init; }

			[JsonPropertyName("signature")]
			public string? Signature { get; init; }

			[JsonPropertyName("nativeCode")]
			public string NativeCode { get; init; } = "";

			[JsonPropertyName("isJitted")]
			public bool IsJitted { get; init; }

			[JsonPropertyName("isGeneric")]
			public bool IsGeneric { get; init; }

			[JsonPropertyName("metadataToken")]
			public int MetadataToken { get; init; }
		}

		private sealed class FieldMetadataDto {
			[JsonPropertyName("name")]
			public string Name { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string TypeName { get; init; } = "";

			[JsonPropertyName("offset")]
			public int Offset { get; init; }

			[JsonPropertyName("isStatic")]
			public bool IsStatic { get; init; }

			[JsonPropertyName("isThreadStatic")]
			public bool IsThreadStatic { get; init; }

			[JsonPropertyName("size")]
			public int Size { get; init; }

			[JsonPropertyName("value")]
			public string? Value { get; init; }
		}

		private sealed class ClassInfoDto {
			[JsonPropertyName("eeClass")]
			public string EEClass { get; init; } = "";

			[JsonPropertyName("methodTable")]
			public string MethodTable { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string TypeName { get; init; } = "";

			[JsonPropertyName("moduleName")]
			public string? ModuleName { get; init; }

			[JsonPropertyName("fieldCount")]
			public int FieldCount { get; init; }

			[JsonPropertyName("staticFieldCount")]
			public int StaticFieldCount { get; init; }

			[JsonPropertyName("threadStaticFieldCount")]
			public int ThreadStaticFieldCount { get; init; }

			[JsonPropertyName("methodCount")]
			public int MethodCount { get; init; }

			[JsonPropertyName("isTruncated")]
			public bool IsTruncated { get; init; }

			[JsonPropertyName("fields")]
			public List<FieldMetadataDto> Fields { get; init; } = new();

			[JsonPropertyName("methods")]
			public List<string> Methods { get; init; } = new();
		}

		private sealed class ModuleDetailsDto {
			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("name")]
			public string Name { get; init; } = "";

			[JsonPropertyName("assemblyName")]
			public string? AssemblyName { get; init; }

			[JsonPropertyName("imageBase")]
			public string ImageBase { get; init; } = "";

			[JsonPropertyName("size")]
			public ulong Size { get; init; }

			[JsonPropertyName("metadataAddress")]
			public string MetadataAddress { get; init; } = "";

			[JsonPropertyName("metadataLength")]
			public int MetadataLength { get; init; }

			[JsonPropertyName("assemblyAddress")]
			public string AssemblyAddress { get; init; } = "";

			[JsonPropertyName("isDynamic")]
			public bool IsDynamic { get; init; }

			[JsonPropertyName("isPEFile")]
			public bool IsPEFile { get; init; }

			[JsonPropertyName("layout")]
			public string Layout { get; init; } = "";

			[JsonPropertyName("appDomainName")]
			public string? AppDomainName { get; init; }

			[JsonPropertyName("typeCount")]
			public int TypeCount { get; init; }

			[JsonPropertyName("typesWithStaticFieldsCount")]
			public int TypesWithStaticFieldsCount { get; init; }
		}

		private sealed class AssemblyDetailsDto {
			[JsonPropertyName("assemblyAddress")]
			public string AssemblyAddress { get; init; } = "";

			[JsonPropertyName("name")]
			public string Name { get; init; } = "";

			[JsonPropertyName("isDynamic")]
			public bool IsDynamic { get; init; }

			[JsonPropertyName("appDomainName")]
			public string? AppDomainName { get; init; }

			[JsonPropertyName("modules")]
			public List<string> Modules { get; init; } = new();
		}

		private sealed class Name2EEResultDto {
			[JsonPropertyName("moduleName")]
			public string? ModuleName { get; init; }

			[JsonPropertyName("typeName")]
			public string? TypeName { get; init; }

			[JsonPropertyName("methodName")]
			public string? MethodName { get; init; }

			[JsonPropertyName("methodTable")]
			public string MethodTable { get; init; } = "";

			[JsonPropertyName("eeClass")]
			public string EEClass { get; init; } = "";

			[JsonPropertyName("methods")]
			public List<MethodDescInfoDto> Methods { get; init; } = new();
		}

		private sealed class ThreadStateInfoDto {
			[JsonPropertyName("managedThreadId")]
			public int ManagedThreadId { get; init; }

			[JsonPropertyName("osThreadId")]
			public uint OSThreadId { get; init; }

			[JsonPropertyName("isAlive")]
			public bool IsAlive { get; init; }

			[JsonPropertyName("exceptionType")]
			public string? ExceptionType { get; init; }

			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("gcMode")]
			public string GcMode { get; init; } = "";

			[JsonPropertyName("lockCount")]
			public uint? LockCount { get; init; }

			[JsonPropertyName("apartmentState")]
			public string ApartmentState { get; init; } = "";

			[JsonPropertyName("isThreadPoolThread")]
			public bool IsThreadPoolThread { get; init; }

			[JsonPropertyName("isGC")]
			public bool IsGC { get; init; }

			[JsonPropertyName("isFinalizer")]
			public bool IsFinalizer { get; init; }

			[JsonPropertyName("isBackground")]
			public bool IsBackground { get; init; }

			[JsonPropertyName("isUnstarted")]
			public bool IsUnstarted { get; init; }

			[JsonPropertyName("isDead")]
			public bool IsDead { get; init; }

			[JsonPropertyName("isAborted")]
			public bool IsAborted { get; init; }

			[JsonPropertyName("isSuspendPending")]
			public bool IsSuspendPending { get; init; }

			[JsonPropertyName("stateFlags")]
			public List<string> StateFlags { get; init; } = new();
		}

		private sealed class ExceptionDetailsDto {
			[JsonPropertyName("address")]
			public string Address { get; init; } = "";

			[JsonPropertyName("typeName")]
			public string TypeName { get; init; } = "";

			[JsonPropertyName("message")]
			public string? Message { get; init; }

			[JsonPropertyName("hResult")]
			public int HResult { get; init; }

			[JsonPropertyName("stackTrace")]
			public List<string> StackTrace { get; init; } = new();

			[JsonPropertyName("innerExceptions")]
			public List<ExceptionDetailsDto> InnerExceptions { get; init; } = new();
		}

		private sealed class ThreadExceptionInfoDto {
			[JsonPropertyName("managedThreadId")]
			public int? ManagedThreadId { get; init; }

			[JsonPropertyName("osThreadId")]
			public uint? OSThreadId { get; init; }

			[JsonPropertyName("source")]
			public string Source { get; init; } = "";

			[JsonPropertyName("exception")]
			public ExceptionDetailsDto? Exception { get; init; }
		}

		// ---- Mapping helpers --------------------------------------------------------------------

		private static GCRootPathNodeDto ToDto(GCRootPathNode node) => new() {
			Address = Addr(node.Address),
			TypeName = node.TypeName,
			Size = node.Size,
		};

		private static GCRootPathInfoDto ToDto(GCRootPathInfo path) => new() {
			RootAddress = Addr(path.RootAddress),
			RootKind = path.RootKind,
			RootName = path.RootName,
			ManagedThreadId = path.ManagedThreadId,
			OSThreadId = path.OSThreadId,
			IsPinned = path.IsPinned,
			IsInterior = path.IsInterior,
			TargetAddress = Addr(path.TargetAddress),
			Depth = path.Depth,
			Path = path.Path.Select(ToDto).ToList(),
		};

		private static ObjectFieldDto ToDto(ObjectField field) => new() {
			Name = field.Name,
			TypeName = field.TypeName,
			Value = field.Value,
			Address = Addr(field.Address),
			IsReference = field.IsReference,
			Offset = field.Offset,
		};

		private static HeapSegmentInfoDto ToDto(HeapSegmentInfo segment) => new() {
			Start = Addr(segment.Start),
			End = Addr(segment.End),
			Size = segment.Size,
			Generation = segment.Generation,
			Kind = segment.Kind,
			IsLargeObjectHeap = segment.IsLargeObjectHeap,
			IsPinnedObjectHeap = segment.IsPinnedObjectHeap,
			CommittedSize = segment.CommittedSize,
			ReservedSize = segment.ReservedSize,
			Gen0Size = segment.Gen0Size,
			Gen1Size = segment.Gen1Size,
			Gen2Size = segment.Gen2Size,
			SubHeapIndex = segment.SubHeapIndex,
		};

		private static StackFrameInfoDto ToDto(StackFrameInfo frame) => new() {
			InstructionPointer = Addr(frame.InstructionPointer),
			StackPointer = Addr(frame.StackPointer),
			FrameKind = frame.FrameKind,
			MethodName = frame.MethodName,
			ModuleName = frame.ModuleName,
			IsManaged = frame.IsManaged,
		};

		private static FieldMetadataDto ToDto(FieldMetadata field) => new() {
			Name = field.Name,
			TypeName = field.TypeName,
			Offset = field.Offset,
			IsStatic = field.IsStatic,
			IsThreadStatic = field.IsThreadStatic,
			Size = field.Size,
			Value = field.Value,
		};

		private static MethodDescInfoDto ToDto(MethodDescInfo info) => new() {
			MethodDesc = Addr(info.MethodDesc),
			MethodTable = Addr(info.MethodTable),
			MethodName = info.MethodName,
			TypeName = info.TypeName,
			ModuleName = info.ModuleName,
			Signature = info.Signature,
			NativeCode = Addr(info.NativeCode),
			IsJitted = info.IsJitted,
			IsGeneric = info.IsGeneric,
			MetadataToken = info.MetadataToken,
		};

		private static ExceptionDetailsDto ToDto(ExceptionDetails exception) => new() {
			Address = Addr(exception.Address),
			TypeName = exception.TypeName,
			Message = exception.Message,
			HResult = exception.HResult,
			StackTrace = exception.StackTrace,
			InnerExceptions = exception.InnerExceptions.Select(ToDto).ToList(),
		};

		// ---- Public surface, mirroring MarkdownFormatter method-for-method ----------------------

		public static string FormatHeapStatistics(PagedResult<HeapStatItem> stats) {
			var data = stats.Items.Select(item => new HeapStatItemDto {
				MethodTable = Addr(item.MethodTable),
				Count = item.Count,
				TotalSize = item.TotalSize,
				TypeName = item.TypeName,
			}).ToList();

			return Serialize(new CollectionEnvelope<HeapStatItemDto> {
				Data = data,
				Pagination = PaginationInfo.FromPagedResult(stats),
			});
		}

		public static string FormatHeapObjects(PagedResult<HeapObjectItem> objects) {
			var data = objects.Items.Select(item => new HeapObjectItemDto {
				Address = Addr(item.Address),
				MethodTable = Addr(item.MethodTable),
				Size = item.Size,
				TypeName = item.TypeName,
			}).ToList();

			return Serialize(new CollectionEnvelope<HeapObjectItemDto> {
				Data = data,
				Pagination = PaginationInfo.FromPagedResult(objects),
			});
		}

		public static string FormatThreads(IEnumerable<ThreadInfo> threads) {
			var data = threads.Select(item => new ThreadInfoDto {
				ManagedThreadId = item.ManagedThreadId,
				OSThreadId = item.OSThreadId,
				IsAlive = item.IsAlive,
				ExceptionType = item.ExceptionType,
				ExceptionMessage = item.ExceptionMessage,
			}).ToList();

			return Serialize(new CollectionEnvelope<ThreadInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatModules(IEnumerable<ModuleInfo> modules) {
			var data = modules.Select(item => new ModuleInfoDto {
				Name = item.Name,
				ImageBase = Addr(item.ImageBase),
				Size = item.Size,
				IsUserCode = item.IsUserCode,
			}).ToList();

			return Serialize(new CollectionEnvelope<ModuleInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatStackGroups(IEnumerable<StackGroup> groups) {
			var data = groups.Select(group => new StackGroupDto {
				ManagedThreadIds = group.ManagedThreadIds,
				Frames = group.Frames,
				ThreadCount = group.ThreadCount,
			}).ToList();

			return Serialize(new CollectionEnvelope<StackGroupDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatGCRootPaths(GCRootSearchInfo result) {
			var data = result.Paths.Select(ToDto).ToList();

			return Serialize(new GCRootPathsEnvelope {
				TargetAddress = Addr(result.TargetAddress),
				NodesVisited = result.NodesVisited,
				Truncated = result.Truncated,
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatObjectDetails(ObjectDetails details) {
			return Serialize(new ItemEnvelope<ObjectDetailsDto> {
				Data = new ObjectDetailsDto {
					Address = Addr(details.Address),
					TypeName = details.TypeName,
					Size = details.Size,
					MethodTable = Addr(details.MethodTable),
					Value = details.Value,
					Fields = details.Fields.Select(ToDto).ToList(),
				},
			});
		}

		public static string FormatHeapSegments(HeapSummaryInfo summary) {
			return Serialize(new ItemEnvelope<HeapSummaryInfoDto> {
				Data = new HeapSummaryInfoDto {
					IsServerGC = summary.IsServerGC,
					SubHeapCount = summary.SubHeapCount,
					CanWalkHeap = summary.CanWalkHeap,
					DynamicAdaptationMode = summary.DynamicAdaptationMode,
					Segments = summary.Segments.Select(ToDto).ToList(),
				},
			});
		}

		public static string FormatThreadPool(ThreadPoolInfo info) {
			return Serialize(new ItemEnvelope<ThreadPoolInfoDto> {
				Data = new ThreadPoolInfoDto {
					TotalThreads = info.TotalThreads,
					ActiveThreads = info.ActiveThreads,
					IdleThreads = info.IdleThreads,
					RetiredThreads = info.RetiredThreads,
					MinThreads = info.MinThreads,
					MaxThreads = info.MaxThreads,
					Type = info.Type,
					CpuUtilization = info.CpuUtilization,
					HasCompletionPortData = info.HasCompletionPortData,
					TotalCompletionPorts = info.TotalCompletionPorts,
					FreeCompletionPorts = info.FreeCompletionPorts,
					MaxFreeCompletionPorts = info.MaxFreeCompletionPorts,
					CompletionPortCurrentLimit = info.CompletionPortCurrentLimit,
					MinCompletionPorts = info.MinCompletionPorts,
					MaxCompletionPorts = info.MaxCompletionPorts,
				},
			});
		}

		public static string FormatSyncBlocks(PagedResult<SyncBlockInfo> blocks) {
			var data = blocks.Items.Select(block => new SyncBlockInfoDto {
				ObjectAddress = Addr(block.ObjectAddress),
				TypeName = block.TypeName,
				IsMonitorHeld = block.IsMonitorHeld,
				HoldingThreadAddress = Addr(block.HoldingThreadAddress),
				RecursionCount = block.RecursionCount,
				WaitingThreadCount = block.WaitingThreadCount,
				ManagedThreadId = block.ManagedThreadId,
				OSThreadId = block.OSThreadId,
				IsThinLock = block.IsThinLock,
			}).ToList();

			return Serialize(new CollectionEnvelope<SyncBlockInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromPagedResult(blocks),
			});
		}

		public static string FormatGCHandles(IEnumerable<GCHandleInfo> handles) {
			var data = handles.Select(handle => new GCHandleInfoDto {
				Address = Addr(handle.Address),
				Object = Addr(handle.Object),
				Kind = handle.Kind,
				TypeName = handle.TypeName,
				IsStrong = handle.IsStrong,
				ReferenceCount = handle.ReferenceCount,
				DependentTarget = Addr(handle.DependentTarget),
				AppDomainName = handle.AppDomainName,
				Size = handle.Size,
			}).ToList();

			return Serialize(new CollectionEnvelope<GCHandleInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatGCHandleStatistics(IEnumerable<GCHandleStatItem> stats) {
			var data = stats.Select(item => new GCHandleStatItemDto {
				Kind = item.Kind,
				Count = item.Count,
				StrongCount = item.StrongCount,
				TotalSize = item.TotalSize,
			}).ToList();

			return Serialize(new CollectionEnvelope<GCHandleStatItemDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatHeapVerification(IEnumerable<HeapCorruptionInfo> corruptions) {
			var data = corruptions.Select(item => new HeapCorruptionInfoDto {
				Address = Addr(item.Address),
				Object = Addr(item.Object),
				Kind = item.Kind,
				Message = item.Message,
				Offset = item.Offset,
				TypeName = item.TypeName,
			}).ToList();

			return Serialize(new CollectionEnvelope<HeapCorruptionInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatDetailedStacks(IEnumerable<ThreadStackInfo> stacks) {
			var data = stacks.Select(stack => new ThreadStackInfoDto {
				ManagedThreadId = stack.ManagedThreadId,
				OSThreadId = stack.OSThreadId,
				IsAlive = stack.IsAlive,
				ExceptionType = stack.ExceptionType,
				Frames = stack.Frames.Select(ToDto).ToList(),
			}).ToList();

			return Serialize(new CollectionEnvelope<ThreadStackInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatMethodTable(MethodTableInfo info) {
			return Serialize(new ItemEnvelope<MethodTableInfoDto> {
				Data = new MethodTableInfoDto {
					MethodTable = Addr(info.MethodTable),
					EEClass = Addr(info.EEClass),
					TypeName = info.TypeName,
					ModuleName = info.ModuleName,
					BaseSize = info.BaseSize,
					ComponentSize = info.ComponentSize,
					MethodCount = info.MethodCount,
					MetadataToken = info.MetadataToken,
					IsValueType = info.IsValueType,
					IsInterface = info.IsInterface,
					IsAbstract = info.IsAbstract,
					IsSealed = info.IsSealed,
					IsEnum = info.IsEnum,
					IsArray = info.IsArray,
					IsString = info.IsString,
					IsFinalizable = info.IsFinalizable,
					ContainsPointers = info.ContainsPointers,
					Visibility = info.Visibility,
					BaseTypeName = info.BaseTypeName,
					Interfaces = info.Interfaces,
				},
			});
		}

		public static string FormatMethodDesc(MethodDescInfo info) {
			return Serialize(new ItemEnvelope<MethodDescInfoDto> {
				Data = ToDto(info),
			});
		}

		public static string FormatClass(ClassInfo info) {
			return Serialize(new ItemEnvelope<ClassInfoDto> {
				Data = new ClassInfoDto {
					EEClass = Addr(info.EEClass),
					MethodTable = Addr(info.MethodTable),
					TypeName = info.TypeName,
					ModuleName = info.ModuleName,
					FieldCount = info.FieldCount,
					StaticFieldCount = info.StaticFieldCount,
					ThreadStaticFieldCount = info.ThreadStaticFieldCount,
					MethodCount = info.MethodCount,
					IsTruncated = info.IsTruncated,
					Fields = info.Fields.Select(ToDto).ToList(),
					Methods = info.Methods,
				},
			});
		}

		public static string FormatModuleDetails(ModuleDetails info) {
			return Serialize(new ItemEnvelope<ModuleDetailsDto> {
				Data = new ModuleDetailsDto {
					Address = Addr(info.Address),
					Name = info.Name,
					AssemblyName = info.AssemblyName,
					ImageBase = Addr(info.ImageBase),
					Size = info.Size,
					MetadataAddress = Addr(info.MetadataAddress),
					MetadataLength = info.MetadataLength,
					AssemblyAddress = Addr(info.AssemblyAddress),
					IsDynamic = info.IsDynamic,
					IsPEFile = info.IsPEFile,
					Layout = info.Layout,
					AppDomainName = info.AppDomainName,
					TypeCount = info.TypeCount,
					TypesWithStaticFieldsCount = info.TypesWithStaticFieldsCount,
				},
			});
		}

		public static string FormatAssemblyDetails(AssemblyDetails info) {
			return Serialize(new ItemEnvelope<AssemblyDetailsDto> {
				Data = new AssemblyDetailsDto {
					AssemblyAddress = Addr(info.AssemblyAddress),
					Name = info.Name,
					IsDynamic = info.IsDynamic,
					AppDomainName = info.AppDomainName,
					Modules = info.Modules,
				},
			});
		}

		public static string FormatName2EE(Name2EEResult info) {
			return Serialize(new ItemEnvelope<Name2EEResultDto> {
				Data = new Name2EEResultDto {
					ModuleName = info.ModuleName,
					TypeName = info.TypeName,
					MethodName = info.MethodName,
					MethodTable = Addr(info.MethodTable),
					EEClass = Addr(info.EEClass),
					Methods = info.Methods.Select(ToDto).ToList(),
				},
			});
		}

		public static string FormatThreadStates(IEnumerable<ThreadStateInfo> states) {
			var data = states.Select(state => new ThreadStateInfoDto {
				ManagedThreadId = state.ManagedThreadId,
				OSThreadId = state.OSThreadId,
				IsAlive = state.IsAlive,
				ExceptionType = state.ExceptionType,
				Address = Addr(state.Address),
				GcMode = state.GcMode,
				LockCount = state.LockCount,
				ApartmentState = state.ApartmentState,
				IsThreadPoolThread = state.IsThreadPoolThread,
				IsGC = state.IsGC,
				IsFinalizer = state.IsFinalizer,
				IsBackground = state.IsBackground,
				IsUnstarted = state.IsUnstarted,
				IsDead = state.IsDead,
				IsAborted = state.IsAborted,
				IsSuspendPending = state.IsSuspendPending,
				StateFlags = state.StateFlags,
			}).ToList();

			return Serialize(new CollectionEnvelope<ThreadStateInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromItemsOnly(data.Count),
			});
		}

		public static string FormatThreadExceptions(PagedResult<ThreadExceptionInfo> exceptionInfos) {
			// Matches MarkdownFormatter: entries without an exception carry no information and are
			// dropped rather than serialized as rows with a null "exception" field. Pagination is
			// still reported against the pre-drop page, matching what the analyzer actually paginated.
			var data = exceptionInfos.Items.Where(i => i.Exception != null).Select(info => new ThreadExceptionInfoDto {
				ManagedThreadId = info.ManagedThreadId,
				OSThreadId = info.OSThreadId,
				Source = SourceName(info.Source),
				Exception = ToDto(info.Exception!),
			}).ToList();

			return Serialize(new CollectionEnvelope<ThreadExceptionInfoDto> {
				Data = data,
				Pagination = PaginationInfo.FromPagedResult(exceptionInfos),
			});
		}
	}
}