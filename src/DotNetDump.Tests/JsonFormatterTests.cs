using System.Text.Json;

using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// JSON is an API contract (CLI_DESIGN.md §10.3), not a `jq` convenience, so these tests assert on
/// the actual envelope shape and the actual camelCase field names — a rename of the JSON output
/// would break a consumer, and these tests exist to catch that.
/// </summary>
public class JsonFormatterTests {
	/// <summary>Wraps items as a single, un-split page — total/offset/limit all agree with what's given.</summary>
	private static PagedResult<T> Page<T>(params T[] items) => new(items, items.Length, 0, items.Length);

	[Fact]
	public void CollectionMethods_WrapRowsInDataAndPagination() {
		string output = JsonFormatter.FormatHeapStatistics(Page(
			new HeapStatItem { TypeName = "System.String", MethodTable = 0x10AE47598, Count = 4928, TotalSize = 199724 }
		));

		using var doc = JsonDocument.Parse(output);
		var root = doc.RootElement;

		Assert.Equal(JsonValueKind.Array, root.GetProperty("data").ValueKind);
		Assert.Equal(1, root.GetProperty("data").GetArrayLength());
		Assert.Equal(1, root.GetProperty("pagination").GetProperty("total").GetInt32());
		Assert.Equal(0, root.GetProperty("pagination").GetProperty("offset").GetInt32());
		Assert.Equal(1, root.GetProperty("pagination").GetProperty("limit").GetInt32());
		Assert.False(root.GetProperty("pagination").GetProperty("hasMore").GetBoolean());
	}

	[Fact]
	public void CollectionMethods_ReportRealTotalsFromAPagedResult() {
		// A page that is short of the full result must say so: total exceeds what's in `data`, and
		// hasMore is true (CLI_DESIGN.md §10.3) -- this is exactly what Phase 2's placeholder
		// (`{ "itemCount": n }`) could never distinguish from "this is all there is."
		var page = new PagedResult<HeapStatItem>(
			new[] { new HeapStatItem { TypeName = "System.String", MethodTable = 0x1, Count = 1, TotalSize = 1 } },
			totalAvailable: 500,
			offset: 10,
			limit: 1);

		string output = JsonFormatter.FormatHeapStatistics(page);

		using var doc = JsonDocument.Parse(output);
		var pagination = doc.RootElement.GetProperty("pagination");

		Assert.Equal(500, pagination.GetProperty("total").GetInt32());
		Assert.Equal(10, pagination.GetProperty("offset").GetInt32());
		Assert.Equal(1, pagination.GetProperty("limit").GetInt32());
		Assert.True(pagination.GetProperty("hasMore").GetBoolean());
	}

	[Fact]
	public void SingleItemMethods_WrapTheResultInData() {
		string output = JsonFormatter.FormatThreadPool(new ThreadPoolInfo { Type = "Portable", TotalThreads = 4 });

		using var doc = JsonDocument.Parse(output);
		var root = doc.RootElement;

		Assert.Equal(JsonValueKind.Object, root.GetProperty("data").ValueKind);
		Assert.Equal("Portable", root.GetProperty("data").GetProperty("type").GetString());
		Assert.False(root.TryGetProperty("pagination", out _));
	}

	[Fact]
	public void EmptyCollection_ReportsZeroCount() {
		string output = JsonFormatter.FormatHeapStatistics(Page(Array.Empty<HeapStatItem>()));

		using var doc = JsonDocument.Parse(output);
		Assert.Equal(0, doc.RootElement.GetProperty("data").GetArrayLength());
		Assert.Equal(0, doc.RootElement.GetProperty("pagination").GetProperty("total").GetInt32());
		Assert.False(doc.RootElement.GetProperty("pagination").GetProperty("hasMore").GetBoolean());
	}

	[Fact]
	public void HeapStatistics_FieldNamesAndAddressFormatting() {
		string output = JsonFormatter.FormatHeapStatistics(Page(
			new HeapStatItem { TypeName = "System.String", MethodTable = 0x10AE47598, Count = 4928, TotalSize = 199724 }
		));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("000000010AE47598", row.GetProperty("methodTable").GetString());
		Assert.Equal(4928, row.GetProperty("count").GetInt32());
		Assert.Equal(199724, row.GetProperty("totalSize").GetInt64());
		Assert.Equal("System.String", row.GetProperty("typeName").GetString());
	}

	[Fact]
	public void HeapObjects_FieldNames() {
		string output = JsonFormatter.FormatHeapObjects(Page(
			new HeapObjectItem { Address = 0x13A611F10, MethodTable = 0x10AE47598, Size = 32, TypeName = "Leaf" }
		));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("000000013A611F10", row.GetProperty("address").GetString());
		Assert.Equal("000000010AE47598", row.GetProperty("methodTable").GetString());
		Assert.Equal(32u, row.GetProperty("size").GetUInt64());
		Assert.Equal("Leaf", row.GetProperty("typeName").GetString());
	}

	[Fact]
	public void Threads_EveryFieldIsPresent() {
		string output = JsonFormatter.FormatThreads(Page(new[] {
			new ThreadInfo { ManagedThreadId = 1, OSThreadId = 0x1280B6, IsAlive = true, ExceptionType = "System.Exception", ExceptionMessage = "boom" }
		}));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal(1, row.GetProperty("managedThreadId").GetInt32());
		Assert.Equal(0x1280B6u, row.GetProperty("osThreadId").GetUInt32());
		Assert.True(row.GetProperty("isAlive").GetBoolean());
		Assert.Equal("System.Exception", row.GetProperty("exceptionType").GetString());
		Assert.Equal("boom", row.GetProperty("exceptionMessage").GetString());
	}

	[Fact]
	public void Threads_NullFieldsAreOmittedNotNull() {
		string output = JsonFormatter.FormatThreads(Page(new[] {
			new ThreadInfo { ManagedThreadId = 1, OSThreadId = 1, IsAlive = true }
		}));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.False(row.TryGetProperty("exceptionType", out _));
		Assert.False(row.TryGetProperty("exceptionMessage", out _));
	}

	[Fact]
	public void Modules_FieldNames() {
		string output = JsonFormatter.FormatModules(Page(new[] {
			new ModuleInfo { Name = "app.dll", ImageBase = 0x102FF8000, Size = 6656, IsUserCode = true }
		}));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("app.dll", row.GetProperty("name").GetString());
		Assert.Equal("0000000102FF8000", row.GetProperty("imageBase").GetString());
		Assert.Equal(6656u, row.GetProperty("size").GetUInt64());
		Assert.True(row.GetProperty("isUserCode").GetBoolean());
	}

	[Fact]
	public void StackGroups_FieldNames() {
		string output = JsonFormatter.FormatStackGroups(new[] {
			new StackGroup { ManagedThreadIds = new List<int> { 1, 2 }, Frames = new List<string> { "frame1", "frame2" } }
		});

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal(2, row.GetProperty("managedThreadIds").GetArrayLength());
		Assert.Equal(2, row.GetProperty("frames").GetArrayLength());
		Assert.Equal(2, row.GetProperty("threadCount").GetInt32());
	}

	[Fact]
	public void GCRootPaths_CarryTargetAddressAsEnvelopeSibling() {
		var path = new GCRootPathInfo {
			RootAddress = 0x16D24DBE8,
			RootKind = "Stack",
			ManagedThreadId = 1,
			IsPinned = true,
			IsInterior = true,
			TargetAddress = 0x13A611F10,
			Path = new List<GCRootPathNode> {
				new() { Address = 0x13A611F50, TypeName = "List<Leaf>", Size = 32 },
				new() { Address = 0x13A611F10, TypeName = "Leaf", Size = 16 },
			}
		};

		string output = JsonFormatter.FormatGCRootPaths(new GCRootSearchInfo {
			TargetAddress = 0x13A611F10,
			Paths = new List<GCRootPathInfo> { path },
			NodesVisited = 42,
			Truncated = false,
		});

		using var doc = JsonDocument.Parse(output);
		var root = doc.RootElement;

		Assert.Equal("000000013A611F10", root.GetProperty("targetAddress").GetString());
		Assert.Equal(42, root.GetProperty("nodesVisited").GetInt64());
		Assert.False(root.GetProperty("truncated").GetBoolean());

		var row = root.GetProperty("data")[0];
		Assert.Equal("000000016D24DBE8", row.GetProperty("rootAddress").GetString());
		Assert.Equal("Stack", row.GetProperty("rootKind").GetString());
		Assert.Equal(1, row.GetProperty("managedThreadId").GetInt32());
		Assert.True(row.GetProperty("isPinned").GetBoolean());
		Assert.True(row.GetProperty("isInterior").GetBoolean());
		Assert.Equal(1, row.GetProperty("depth").GetInt32());
		Assert.Equal("000000013A611F10", row.GetProperty("targetAddress").GetString());

		var pathArray = row.GetProperty("path");
		Assert.Equal(2, pathArray.GetArrayLength());
		Assert.Equal("List<Leaf>", pathArray[0].GetProperty("typeName").GetString());
		Assert.Equal(32u, pathArray[0].GetProperty("size").GetUInt64());
	}

	[Fact]
	public void ObjectDetails_EveryFieldIncludingFields() {
		var details = new ObjectDetails {
			Address = 0x13A611F10,
			TypeName = "Leaf",
			Size = 32,
			MethodTable = 0x10AE47598,
			Value = "some value",
			Fields = new List<ObjectField> {
				new() { Name = "_next", TypeName = "Leaf", Value = null, Address = 0x13A611F50, IsReference = true, Offset = 8 }
			}
		};

		string output = JsonFormatter.FormatObjectDetails(details);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal("000000013A611F10", data.GetProperty("address").GetString());
		Assert.Equal("Leaf", data.GetProperty("typeName").GetString());
		Assert.Equal(32u, data.GetProperty("size").GetUInt64());
		Assert.Equal("000000010AE47598", data.GetProperty("methodTable").GetString());
		Assert.Equal("some value", data.GetProperty("value").GetString());

		var field = data.GetProperty("fields")[0];
		Assert.Equal("_next", field.GetProperty("name").GetString());
		Assert.Equal("Leaf", field.GetProperty("typeName").GetString());
		Assert.False(field.TryGetProperty("value", out _));
		Assert.Equal("000000013A611F50", field.GetProperty("address").GetString());
		Assert.True(field.GetProperty("isReference").GetBoolean());
		Assert.Equal(8, field.GetProperty("offset").GetInt32());
	}

	[Fact]
	public void HeapSegments_TopLevelSummaryAndNestedSegments() {
		var summary = new HeapSummaryInfo {
			IsServerGC = true,
			SubHeapCount = 4,
			CanWalkHeap = true,
			DynamicAdaptationMode = 1,
			Segments = new List<HeapSegmentInfo> {
				new() {
					Start = 0x13A608000, End = 0x13A674268, Size = 442984, Kind = "Ephemeral", Generation = null,
					IsLargeObjectHeap = false, IsPinnedObjectHeap = false, CommittedSize = 540672, ReservedSize = 267878400,
					Gen0Size = 442936, Gen1Size = 24, Gen2Size = 24, SubHeapIndex = 2
				}
			}
		};

		string output = JsonFormatter.FormatHeapSegments(summary);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.True(data.GetProperty("isServerGC").GetBoolean());
		Assert.Equal(4, data.GetProperty("subHeapCount").GetInt32());
		Assert.True(data.GetProperty("canWalkHeap").GetBoolean());
		Assert.Equal(1, data.GetProperty("dynamicAdaptationMode").GetInt32());

		var segment = data.GetProperty("segments")[0];
		Assert.Equal("000000013A608000", segment.GetProperty("start").GetString());
		Assert.Equal("000000013A674268", segment.GetProperty("end").GetString());
		Assert.Equal(442984u, segment.GetProperty("size").GetUInt64());
		Assert.False(segment.TryGetProperty("generation", out _)); // null generation omitted
		Assert.Equal("Ephemeral", segment.GetProperty("kind").GetString());
		Assert.Equal(540672u, segment.GetProperty("committedSize").GetUInt64());
		Assert.Equal(267878400u, segment.GetProperty("reservedSize").GetUInt64());
		Assert.Equal(442936u, segment.GetProperty("gen0Size").GetUInt64());
		Assert.Equal(2, segment.GetProperty("subHeapIndex").GetInt32());
	}

	[Fact]
	public void ThreadPool_EveryField() {
		var info = new ThreadPoolInfo {
			Type = "Portable",
			TotalThreads = 4,
			ActiveThreads = 4,
			IdleThreads = 0,
			RetiredThreads = 1,
			MinThreads = 8,
			MaxThreads = 32767,
			CpuUtilization = 42,
			HasCompletionPortData = true,
			TotalCompletionPorts = 2,
			FreeCompletionPorts = 1,
			MaxFreeCompletionPorts = 5,
			CompletionPortCurrentLimit = 3,
			MinCompletionPorts = 1,
			MaxCompletionPorts = 10
		};

		string output = JsonFormatter.FormatThreadPool(info);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal(4, data.GetProperty("totalThreads").GetInt32());
		Assert.Equal(4, data.GetProperty("activeThreads").GetInt32());
		Assert.Equal(0, data.GetProperty("idleThreads").GetInt32());
		Assert.Equal(1, data.GetProperty("retiredThreads").GetInt32());
		Assert.Equal(8, data.GetProperty("minThreads").GetInt32());
		Assert.Equal(32767, data.GetProperty("maxThreads").GetInt32());
		Assert.Equal(42, data.GetProperty("cpuUtilization").GetInt32());
		Assert.True(data.GetProperty("hasCompletionPortData").GetBoolean());
		Assert.Equal(2, data.GetProperty("totalCompletionPorts").GetInt32());
		Assert.Equal(1, data.GetProperty("freeCompletionPorts").GetInt32());
		Assert.Equal(5, data.GetProperty("maxFreeCompletionPorts").GetInt32());
		Assert.Equal(3, data.GetProperty("completionPortCurrentLimit").GetInt32());
		Assert.Equal(1, data.GetProperty("minCompletionPorts").GetInt32());
		Assert.Equal(10, data.GetProperty("maxCompletionPorts").GetInt32());
	}

	[Fact]
	public void SyncBlocks_EveryField() {
		string output = JsonFormatter.FormatSyncBlocks(Page(
			new SyncBlockInfo {
				ObjectAddress = 0x13A6621D0,
				TypeName = "System.Object",
				IsMonitorHeld = true,
				HoldingThreadAddress = 0xC46D69500,
				RecursionCount = 2,
				WaitingThreadCount = 1,
				ManagedThreadId = 4,
				OSThreadId = 0x1280B6,
				IsThinLock = true
			}
		));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("000000013A6621D0", row.GetProperty("objectAddress").GetString());
		Assert.Equal("System.Object", row.GetProperty("typeName").GetString());
		Assert.True(row.GetProperty("isMonitorHeld").GetBoolean());
		Assert.Equal("0000000C46D69500", row.GetProperty("holdingThreadAddress").GetString());
		Assert.Equal(2, row.GetProperty("recursionCount").GetInt32());
		Assert.Equal(1, row.GetProperty("waitingThreadCount").GetInt32());
		Assert.Equal(4, row.GetProperty("managedThreadId").GetInt32());
		Assert.Equal(0x1280B6u, row.GetProperty("osThreadId").GetUInt32());
		Assert.True(row.GetProperty("isThinLock").GetBoolean());
	}

	[Fact]
	public void GCHandles_EveryField() {
		// The previous attempt at this phase dropped IsStrong, ReferenceCount, DependentTarget,
		// AppDomainName and Size. This test exists specifically to catch that regression again.
		string output = JsonFormatter.FormatGCHandles(Page(new[] {
			new GCHandleInfo {
				Address = 0x1033E1350, Object = 0x13A66C538, Kind = "Dependent", TypeName = "System.Object",
				IsStrong = true, ReferenceCount = 3, DependentTarget = 0x13A611F10, AppDomainName = "AppDomain1", Size = 24
			}
		}));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("00000001033E1350", row.GetProperty("address").GetString());
		Assert.Equal("000000013A66C538", row.GetProperty("object").GetString());
		Assert.Equal("Dependent", row.GetProperty("kind").GetString());
		Assert.Equal("System.Object", row.GetProperty("typeName").GetString());
		Assert.True(row.GetProperty("isStrong").GetBoolean());
		Assert.Equal(3u, row.GetProperty("referenceCount").GetUInt32());
		Assert.Equal("000000013A611F10", row.GetProperty("dependentTarget").GetString());
		Assert.Equal("AppDomain1", row.GetProperty("appDomainName").GetString());
		Assert.Equal(24u, row.GetProperty("size").GetUInt64());
	}

	[Fact]
	public void GCHandleStatistics_IsASeparateRollupShape() {
		string output = JsonFormatter.FormatGCHandleStatistics(new[] {
			new GCHandleStatItem { Kind = "Strong", Count = 10, StrongCount = 10, TotalSize = 4096 }
		});

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("Strong", row.GetProperty("kind").GetString());
		Assert.Equal(10, row.GetProperty("count").GetInt32());
		Assert.Equal(10, row.GetProperty("strongCount").GetInt32());
		Assert.Equal(4096u, row.GetProperty("totalSize").GetUInt64());
	}

	[Fact]
	public void HeapVerification_EveryField() {
		string output = JsonFormatter.FormatHeapVerification(Page(new[] {
			new HeapCorruptionInfo { Address = 0x10, Object = 0x13A611F10, Kind = "InvalidMethodTable", Message = "bad", Offset = 0x18, TypeName = "Leaf" }
		}));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("0000000000000010", row.GetProperty("address").GetString());
		Assert.Equal("000000013A611F10", row.GetProperty("object").GetString());
		Assert.Equal("InvalidMethodTable", row.GetProperty("kind").GetString());
		Assert.Equal("bad", row.GetProperty("message").GetString());
		Assert.Equal(0x18, row.GetProperty("offset").GetInt32());
		Assert.Equal("Leaf", row.GetProperty("typeName").GetString());
	}

	[Fact]
	public void DetailedStacks_NestedFrames() {
		string output = JsonFormatter.FormatDetailedStacks(Page(new[] {
			new ThreadStackInfo {
				ManagedThreadId = 1, OSThreadId = 0x10, IsAlive = true,
				Frames = new List<StackFrameInfo> {
					new() { InstructionPointer = 0x1, StackPointer = 0x2, FrameKind = "ManagedMethod", MethodName = "Foo", ModuleName = "app.dll", IsManaged = true }
				}
			}
		}));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];
		var frame = row.GetProperty("frames")[0];

		Assert.Equal("0000000000000001", frame.GetProperty("instructionPointer").GetString());
		Assert.Equal("0000000000000002", frame.GetProperty("stackPointer").GetString());
		Assert.Equal("ManagedMethod", frame.GetProperty("frameKind").GetString());
		Assert.Equal("Foo", frame.GetProperty("methodName").GetString());
		Assert.Equal("app.dll", frame.GetProperty("moduleName").GetString());
		Assert.True(frame.GetProperty("isManaged").GetBoolean());
	}

	[Fact]
	public void MethodTable_EveryFlagAndInterfaces() {
		var info = new MethodTableInfo {
			MethodTable = 0x10AF147B8,
			EEClass = 0x10AF147B8,
			TypeName = "MyList",
			ModuleName = "app.dll",
			BaseSize = 32,
			ComponentSize = 8,
			MethodCount = 5,
			MetadataToken = 0x02000004,
			IsValueType = false,
			IsInterface = false,
			IsAbstract = true,
			IsSealed = true,
			IsEnum = false,
			IsArray = false,
			IsString = false,
			IsFinalizable = true,
			ContainsPointers = true,
			Visibility = "public",
			BaseTypeName = "System.Object",
			Interfaces = new List<string> { "System.Collections.IEnumerable", "System.IDisposable" }
		};

		string output = JsonFormatter.FormatMethodTable(info);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal("000000010AF147B8", data.GetProperty("methodTable").GetString());
		Assert.Equal("MyList", data.GetProperty("typeName").GetString());
		Assert.Equal("app.dll", data.GetProperty("moduleName").GetString());
		Assert.Equal(32u, data.GetProperty("baseSize").GetUInt64());
		Assert.Equal(8, data.GetProperty("componentSize").GetInt32());
		Assert.Equal(5, data.GetProperty("methodCount").GetInt32());
		Assert.Equal(0x02000004, data.GetProperty("metadataToken").GetInt32());
		Assert.False(data.GetProperty("isValueType").GetBoolean());
		Assert.False(data.GetProperty("isInterface").GetBoolean());
		Assert.True(data.GetProperty("isAbstract").GetBoolean());
		Assert.True(data.GetProperty("isSealed").GetBoolean());
		Assert.False(data.GetProperty("isEnum").GetBoolean());
		Assert.False(data.GetProperty("isArray").GetBoolean());
		Assert.False(data.GetProperty("isString").GetBoolean());
		Assert.True(data.GetProperty("isFinalizable").GetBoolean());
		Assert.True(data.GetProperty("containsPointers").GetBoolean());
		Assert.Equal("public", data.GetProperty("visibility").GetString());
		Assert.Equal("System.Object", data.GetProperty("baseTypeName").GetString());
		Assert.Equal(2, data.GetProperty("interfaces").GetArrayLength());
	}

	[Fact]
	public void MethodDesc_EveryField() {
		var info = new MethodDescInfo {
			MethodDesc = 0x1,
			MethodTable = 0x2,
			MethodName = "Foo",
			TypeName = "MyType",
			ModuleName = "app.dll",
			Signature = "void Foo()",
			NativeCode = 0x3,
			IsJitted = true,
			IsGeneric = false,
			MetadataToken = 0x06000001
		};

		string output = JsonFormatter.FormatMethodDesc(info);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal("0000000000000001", data.GetProperty("methodDesc").GetString());
		Assert.Equal("0000000000000002", data.GetProperty("methodTable").GetString());
		Assert.Equal("Foo", data.GetProperty("methodName").GetString());
		Assert.Equal("MyType", data.GetProperty("typeName").GetString());
		Assert.Equal("app.dll", data.GetProperty("moduleName").GetString());
		Assert.Equal("void Foo()", data.GetProperty("signature").GetString());
		Assert.Equal("0000000000000003", data.GetProperty("nativeCode").GetString());
		Assert.True(data.GetProperty("isJitted").GetBoolean());
		Assert.False(data.GetProperty("isGeneric").GetBoolean());
		Assert.Equal(0x06000001, data.GetProperty("metadataToken").GetInt32());
	}

	[Fact]
	public void Class_NestedFieldsAndMethods() {
		var info = new ClassInfo {
			EEClass = 0x1,
			MethodTable = 0x2,
			TypeName = "Roots",
			ModuleName = "app.dll",
			FieldCount = 2,
			StaticFieldCount = 1,
			ThreadStaticFieldCount = 1,
			MethodCount = 3,
			IsTruncated = true,
			Fields = new List<FieldMetadata> {
				new() { Name = "Name", TypeName = "System.String", Offset = 0, IsStatic = false, IsThreadStatic = false, Size = 8, Value = null },
				new() { Name = "StaticHolder", TypeName = "Holder", Offset = 8, IsStatic = true, IsThreadStatic = false, Size = 8, Value = "13A611F30" }
			},
			Methods = new List<string> { "Foo", "Bar" }
		};

		string output = JsonFormatter.FormatClass(info);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal(2, data.GetProperty("fieldCount").GetInt32());
		Assert.Equal(1, data.GetProperty("staticFieldCount").GetInt32());
		Assert.Equal(1, data.GetProperty("threadStaticFieldCount").GetInt32());
		Assert.Equal(3, data.GetProperty("methodCount").GetInt32());
		Assert.True(data.GetProperty("isTruncated").GetBoolean());

		var fields = data.GetProperty("fields");
		Assert.Equal(2, fields.GetArrayLength());
		Assert.Equal("StaticHolder", fields[1].GetProperty("name").GetString());
		Assert.True(fields[1].GetProperty("isStatic").GetBoolean());
		Assert.Equal("13A611F30", fields[1].GetProperty("value").GetString());

		Assert.Equal(2, data.GetProperty("methods").GetArrayLength());
	}

	[Fact]
	public void ModuleDetails_EveryField() {
		var info = new ModuleDetails {
			Address = 0x1,
			Name = "app.dll",
			AssemblyName = "app",
			ImageBase = 0x102FF8000,
			AssemblyAddress = 0xC475702A0,
			Layout = "Flat",
			IsPEFile = true,
			IsDynamic = false,
			TypeCount = 6,
			TypesWithStaticFieldsCount = 4,
			Size = 6656,
			MetadataAddress = 0x5,
			MetadataLength = 2952,
			AppDomainName = "AppDomain1"
		};

		string output = JsonFormatter.FormatModuleDetails(info);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal("0000000000000001", data.GetProperty("address").GetString());
		Assert.Equal("app.dll", data.GetProperty("name").GetString());
		Assert.Equal("app", data.GetProperty("assemblyName").GetString());
		Assert.Equal("0000000102FF8000", data.GetProperty("imageBase").GetString());
		Assert.Equal("0000000C475702A0", data.GetProperty("assemblyAddress").GetString());
		Assert.Equal("Flat", data.GetProperty("layout").GetString());
		Assert.True(data.GetProperty("isPEFile").GetBoolean());
		Assert.False(data.GetProperty("isDynamic").GetBoolean());
		Assert.Equal(6, data.GetProperty("typeCount").GetInt32());
		Assert.Equal(4, data.GetProperty("typesWithStaticFieldsCount").GetInt32());
		Assert.Equal(6656u, data.GetProperty("size").GetUInt64());
		Assert.Equal("0000000000000005", data.GetProperty("metadataAddress").GetString());
		Assert.Equal(2952, data.GetProperty("metadataLength").GetInt32());
		Assert.Equal("AppDomain1", data.GetProperty("appDomainName").GetString());
	}

	[Fact]
	public void AssemblyDetails_EveryField() {
		string output = JsonFormatter.FormatAssemblyDetails(new AssemblyDetails {
			AssemblyAddress = 0xC475702A0,
			Name = "app",
			IsDynamic = true,
			AppDomainName = "AppDomain1",
			Modules = new List<string> { "app.dll", "app.core.dll" }
		});

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal("0000000C475702A0", data.GetProperty("assemblyAddress").GetString());
		Assert.Equal("app", data.GetProperty("name").GetString());
		Assert.True(data.GetProperty("isDynamic").GetBoolean());
		Assert.Equal("AppDomain1", data.GetProperty("appDomainName").GetString());
		Assert.Equal(2, data.GetProperty("modules").GetArrayLength());
	}

	[Fact]
	public void Name2EE_NestedMethodDescs() {
		var info = new Name2EEResult {
			ModuleName = "app.dll",
			TypeName = "MyType",
			MethodName = "Foo",
			MethodTable = 0x1,
			EEClass = 0x1,
			Methods = new List<MethodDescInfo> {
				new() { MethodDesc = 0x2, MethodTable = 0x1, MethodName = "Foo", Signature = "void Foo()", IsJitted = true }
			}
		};

		string output = JsonFormatter.FormatName2EE(info);

		using var doc = JsonDocument.Parse(output);
		var data = doc.RootElement.GetProperty("data");

		Assert.Equal("app.dll", data.GetProperty("moduleName").GetString());
		Assert.Equal("MyType", data.GetProperty("typeName").GetString());
		Assert.Equal("Foo", data.GetProperty("methodName").GetString());

		var method = data.GetProperty("methods")[0];
		Assert.Equal("0000000000000002", method.GetProperty("methodDesc").GetString());
		Assert.Equal("void Foo()", method.GetProperty("signature").GetString());
		Assert.True(method.GetProperty("isJitted").GetBoolean());
	}

	[Fact]
	public void ThreadStates_EveryFlag() {
		var state = new ThreadStateInfo {
			ManagedThreadId = 6,
			OSThreadId = 0x10,
			Address = 0x20,
			GcMode = "Cooperative",
			ApartmentState = "MTA",
			LockCount = 2,
			IsThreadPoolThread = true,
			IsBackground = true,
			IsFinalizer = true,
			IsGC = true,
			IsUnstarted = false,
			IsDead = false,
			IsAborted = true,
			IsSuspendPending = true,
			StateFlags = new List<string> { "GC_ON_TRANSITIONS" }
		};

		string output = JsonFormatter.FormatThreadStates(Page(state));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal(6, row.GetProperty("managedThreadId").GetInt32());
		Assert.Equal("0000000000000020", row.GetProperty("address").GetString());
		Assert.Equal("Cooperative", row.GetProperty("gcMode").GetString());
		Assert.Equal(2u, row.GetProperty("lockCount").GetUInt32());
		Assert.Equal("MTA", row.GetProperty("apartmentState").GetString());
		Assert.True(row.GetProperty("isThreadPoolThread").GetBoolean());
		Assert.True(row.GetProperty("isGC").GetBoolean());
		Assert.True(row.GetProperty("isFinalizer").GetBoolean());
		Assert.True(row.GetProperty("isBackground").GetBoolean());
		Assert.False(row.GetProperty("isUnstarted").GetBoolean());
		Assert.False(row.GetProperty("isDead").GetBoolean());
		Assert.True(row.GetProperty("isAborted").GetBoolean());
		Assert.True(row.GetProperty("isSuspendPending").GetBoolean());
		Assert.Equal(1, row.GetProperty("stateFlags").GetArrayLength());
	}

	[Fact]
	public void ThreadStates_OmitsNullLockCount() {
		string output = JsonFormatter.FormatThreadStates(Page(new[] {
			new ThreadStateInfo { ManagedThreadId = 1, LockCount = null }
		}));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.False(row.TryGetProperty("lockCount", out _));
	}

	[Fact]
	public void ThreadExceptions_SourceIsAnExplicitString() {
		string output = JsonFormatter.FormatThreadExceptions(Page(
			new ThreadExceptionInfo {
				Source = ExceptionSource.Heap,
				Exception = new ExceptionDetails { Address = 0x10, TypeName = "System.Exception", HResult = unchecked((int)0x80131600) }
			}
		));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal("heap", row.GetProperty("source").GetString());
		Assert.Equal("0000000000000010", row.GetProperty("exception").GetProperty("address").GetString());
		Assert.Equal(unchecked((int)0x80131600), row.GetProperty("exception").GetProperty("hResult").GetInt32());
	}

	[Fact]
	public void ThreadExceptions_NestInnerExceptionsRecursively() {
		string output = JsonFormatter.FormatThreadExceptions(Page(
			new ThreadExceptionInfo {
				Source = ExceptionSource.ThreadCurrentException,
				ManagedThreadId = 5,
				Exception = new ExceptionDetails {
					Address = 0x1,
					TypeName = "Outer",
					Message = "outer-boom",
					InnerExceptions = new List<ExceptionDetails> {
						new() { Address = 0x2, TypeName = "Inner", Message = "inner-boom" }
					}
				}
			}
		));

		using var doc = JsonDocument.Parse(output);
		var row = doc.RootElement.GetProperty("data")[0];

		Assert.Equal(5, row.GetProperty("managedThreadId").GetInt32());
		Assert.Equal("threadCurrentException", row.GetProperty("source").GetString());

		var inner = row.GetProperty("exception").GetProperty("innerExceptions")[0];
		Assert.Equal("Inner", inner.GetProperty("typeName").GetString());
		Assert.Equal("inner-boom", inner.GetProperty("message").GetString());
	}

	[Fact]
	public void ThreadExceptions_DropsEntriesWithoutAnException() {
		// Matches MarkdownFormatter's behavior: an entry with no Exception carries no information.
		string output = JsonFormatter.FormatThreadExceptions(Page(
			new ThreadExceptionInfo { ManagedThreadId = 1, Exception = null },
			new ThreadExceptionInfo { ManagedThreadId = 2, Exception = new ExceptionDetails { TypeName = "X" } }
		));

		using var doc = JsonDocument.Parse(output);
		Assert.Equal(1, doc.RootElement.GetProperty("data").GetArrayLength());
		Assert.Equal(2, doc.RootElement.GetProperty("data")[0].GetProperty("managedThreadId").GetInt32());
	}
}