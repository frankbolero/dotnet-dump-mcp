using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// TSV stays deliberately dumb (CLI_DESIGN.md §3.3): header row, then tab-separated values, no
/// padding or alignment. These tests check the one thing that actually matters for a
/// `grep`/`awk`/`cut` pipeline: the column count never drifts between the header and any row, and
/// values containing tabs/newlines/backslashes are escaped rather than corrupting the structure.
/// </summary>
public class TsvFormatterTests {
	private static string[] Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

	private static void AssertConsistentColumnCount(string output) {
		var lines = Lines(output);
		Assert.True(lines.Length >= 1, "Expected at least a header row.");
		int headerColumns = lines[0].Split('\t').Length;
		foreach (var line in lines) {
			Assert.Equal(headerColumns, line.Split('\t').Length);
		}
	}

	[Fact]
	public void HeapStatistics_HeaderAndRowMatchColumnCount() {
		string output = TsvFormatter.FormatHeapStatistics(new[] {
			new HeapStatItem { TypeName = "System.String", MethodTable = 0x10AE47598, Count = 4928, TotalSize = 199724 }
		});

		AssertConsistentColumnCount(output);
		var lines = Lines(output);
		Assert.Equal("methodTable\tcount\ttotalSize\ttypeName", lines[0]);
		Assert.Equal("000000010AE47598\t4928\t199724\tSystem.String", lines[1]);
	}

	[Fact]
	public void HeapStatistics_EmptyInput_IsHeaderOnly() {
		string output = TsvFormatter.FormatHeapStatistics(Array.Empty<HeapStatItem>());
		var lines = Lines(output);
		Assert.Single(lines);
	}

	[Fact]
	public void HeapObjects_RowValues() {
		string output = TsvFormatter.FormatHeapObjects(new[] {
			new HeapObjectItem { Address = 0x13A611F10, MethodTable = 0x10AE47598, Size = 32, TypeName = "Leaf" }
		});

		AssertConsistentColumnCount(output);
		Assert.Equal("000000013A611F10\t000000010AE47598\t32\tLeaf", Lines(output)[1]);
	}

	[Fact]
	public void Threads_HandlesNullExceptionFieldsAsEmptyCells() {
		string output = TsvFormatter.FormatThreads(new[] {
			new ThreadInfo { ManagedThreadId = 1, OSThreadId = 0x10, IsAlive = true }
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(5, row.Length);
		Assert.Equal("", row[3]); // exceptionType
		Assert.Equal("", row[4]); // exceptionMessage
	}

	[Fact]
	public void EscapesTabsNewlinesAndBackslashesInValues() {
		string output = TsvFormatter.FormatHeapObjects(new[] {
			new HeapObjectItem { Address = 1, MethodTable = 1, Size = 1, TypeName = "Weird\tType\\With\nNewline" }
		});

		AssertConsistentColumnCount(output);
		var lines = Lines(output);
		Assert.Equal(2, lines.Length); // the embedded newline must not create a spurious third line
		Assert.Contains("Weird\\tType\\\\With\\nNewline", lines[1]);

		// The raw control characters must not survive into the row.
		Assert.DoesNotContain("Weird\tType", lines[1]);
	}

	[Fact]
	public void EscapesBackslashBeforeOtherCharactersToAvoidDoubleEscaping() {
		string output = TsvFormatter.FormatHeapObjects(new[] {
			new HeapObjectItem { Address = 1, MethodTable = 1, Size = 1, TypeName = "back\\slash" }
		});

		var lines = Lines(output);
		Assert.Contains("back\\\\slash", lines[1]);
	}

	[Fact]
	public void Modules_RowValues() {
		string output = TsvFormatter.FormatModules(new[] {
			new ModuleInfo { Name = "app.dll", ImageBase = 0x102FF8000, Size = 6656, IsUserCode = true }
		});

		AssertConsistentColumnCount(output);
		Assert.Equal("app.dll\t0000000102FF8000\t6656\ttrue", Lines(output)[1]);
	}

	[Fact]
	public void StackGroups_FlattenNestedListsIntoOneCellEach() {
		string output = TsvFormatter.FormatStackGroups(new[] {
			new StackGroup { ManagedThreadIds = new List<int> { 1, 2, 3 }, Frames = new List<string> { "frame1", "frame2" } }
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(3, row.Length);
		Assert.Equal("1; 2; 3", row[0]);
		Assert.Equal("frame1; frame2", row[1]);
		Assert.Equal("3", row[2]);
	}

	[Fact]
	public void GCRootPaths_FlattenPathNodesAndRepeatTargetAddress() {
		var path = new GCRootPathInfo {
			RootAddress = 0x16D24DBE8,
			RootKind = "Stack",
			ManagedThreadId = 1,
			TargetAddress = 0x13A611F10,
			Path = new List<GCRootPathNode> {
				new() { Address = 0x13A611F50, TypeName = "List<Leaf>", Size = 32 },
				new() { Address = 0x13A611F10, TypeName = "Leaf", Size = 16 },
			}
		};

		string output = TsvFormatter.FormatGCRootPaths(new[] { path }, 0x13A611F10);

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(10, row.Length);
		Assert.Equal("000000016D24DBE8", row[0]);
		Assert.Equal("1", row[8]); // depth
		Assert.Contains("List<Leaf>", row[9]);
		Assert.Contains("Leaf", row[9]);
	}

	[Fact]
	public void ObjectDetails_SingleRowWithFlattenedFields() {
		var details = new ObjectDetails {
			Address = 0x13A611F10,
			TypeName = "Leaf",
			Size = 32,
			MethodTable = 0x10AE47598,
			Fields = new List<ObjectField> {
				new() { Name = "_next", TypeName = "Leaf", Address = 0x13A611F50, IsReference = true, Offset = 8 }
			}
		};

		string output = TsvFormatter.FormatObjectDetails(details);

		AssertConsistentColumnCount(output);
		var lines = Lines(output);
		Assert.Equal(2, lines.Length); // header + exactly one data row
		Assert.Contains("_next", lines[1]);
	}

	[Fact]
	public void HeapSegments_RepeatsSummaryFieldsOnEverySegmentRow() {
		var summary = new HeapSummaryInfo {
			IsServerGC = true,
			SubHeapCount = 4,
			CanWalkHeap = true,
			DynamicAdaptationMode = 1,
			Segments = new List<HeapSegmentInfo> {
				new() { Start = 0x1, End = 0x2, Size = 10, Kind = "Gen0", Generation = 0, SubHeapIndex = 0 },
				new() { Start = 0x3, End = 0x4, Size = 20, Kind = "Gen1", Generation = 1, SubHeapIndex = 1 },
			}
		};

		string output = TsvFormatter.FormatHeapSegments(summary);

		AssertConsistentColumnCount(output);
		var lines = Lines(output);
		Assert.Equal(3, lines.Length); // header + 2 segment rows
		Assert.StartsWith("true\t4\ttrue\t1\t", lines[1]);
		Assert.StartsWith("true\t4\ttrue\t1\t", lines[2]);
	}

	[Fact]
	public void HeapSegments_EmptySegments_IsHeaderOnly() {
		string output = TsvFormatter.FormatHeapSegments(new HeapSummaryInfo { Segments = new List<HeapSegmentInfo>() });
		Assert.Single(Lines(output));
	}

	[Fact]
	public void ThreadPool_SingleRow() {
		string output = TsvFormatter.FormatThreadPool(new ThreadPoolInfo {
			Type = "Portable",
			TotalThreads = 4,
			ActiveThreads = 4,
			IdleThreads = 0,
			RetiredThreads = 1,
			MinThreads = 8,
			MaxThreads = 32767,
			CpuUtilization = 42
		});

		AssertConsistentColumnCount(output);
		var lines = Lines(output);
		Assert.Equal(2, lines.Length);
		Assert.Contains("Portable", lines[1]);
		Assert.Contains("42", lines[1]);
	}

	[Fact]
	public void SyncBlocks_RowValues() {
		string output = TsvFormatter.FormatSyncBlocks(new[] {
			new SyncBlockInfo { ObjectAddress = 0x13A6621D0, TypeName = "System.Object", IsMonitorHeld = true, IsThinLock = true, ManagedThreadId = 4 }
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(9, row.Length);
		Assert.Equal("4", row[6]); // managedThreadId
		Assert.Equal("true", row[8]); // isThinLock
	}

	[Fact]
	public void GCHandles_EveryColumnIsPresent() {
		// Mirrors the JSON test: this is exactly where the previous attempt at this phase dropped fields.
		string output = TsvFormatter.FormatGCHandles(new[] {
			new GCHandleInfo {
				Address = 0x1033E1350, Object = 0x13A66C538, Kind = "Dependent", TypeName = "System.Object",
				IsStrong = true, ReferenceCount = 3, DependentTarget = 0x13A611F10, AppDomainName = "AppDomain1", Size = 24
			}
		});

		AssertConsistentColumnCount(output);
		var header = Lines(output)[0].Split('\t');
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(9, header.Length);
		Assert.Equal("00000001033E1350", row[0]);
		Assert.Equal("000000013A66C538", row[1]);
		Assert.Equal("Dependent", row[2]);
		Assert.Equal("System.Object", row[3]);
		Assert.Equal("true", row[4]);
		Assert.Equal("3", row[5]);
		Assert.Equal("000000013A611F10", row[6]);
		Assert.Equal("AppDomain1", row[7]);
		Assert.Equal("24", row[8]);
	}

	[Fact]
	public void GCHandleStatistics_IsASeparateRollupShape() {
		string output = TsvFormatter.FormatGCHandleStatistics(new[] {
			new GCHandleStatItem { Kind = "Strong", Count = 10, StrongCount = 10, TotalSize = 4096 }
		});

		AssertConsistentColumnCount(output);
		Assert.Equal("Strong\t10\t10\t4096", Lines(output)[1]);
	}

	[Fact]
	public void HeapVerification_RowValues() {
		string output = TsvFormatter.FormatHeapVerification(new[] {
			new HeapCorruptionInfo { Address = 0x10, Object = 0x13A611F10, Kind = "InvalidMethodTable", Message = "bad", Offset = 0x18, TypeName = "Leaf" }
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(6, row.Length);
		Assert.Equal("24", row[4]); // offset as decimal, not hex
	}

	[Fact]
	public void DetailedStacks_FlattenFramesIntoOneCell() {
		string output = TsvFormatter.FormatDetailedStacks(new[] {
			new ThreadStackInfo {
				ManagedThreadId = 1, OSThreadId = 0x10, IsAlive = true,
				Frames = new List<StackFrameInfo> {
					new() { InstructionPointer = 0x1, StackPointer = 0x2, FrameKind = "ManagedMethod", MethodName = "Foo", ModuleName = "app.dll", IsManaged = true }
				}
			}
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(5, row.Length);
		Assert.Contains("Foo", row[4]);
		Assert.Contains("app.dll", row[4]);
	}

	[Fact]
	public void DetailedStacks_EmptyFrames_StillProducesRow() {
		string output = TsvFormatter.FormatDetailedStacks(new[] {
			new ThreadStackInfo { ManagedThreadId = 10, OSThreadId = 0x10, Frames = new List<StackFrameInfo>() }
		});

		AssertConsistentColumnCount(output);
		Assert.Equal(2, Lines(output).Length);
	}

	[Fact]
	public void MethodTable_AllFlagsAndFlattenedInterfaces() {
		var info = new MethodTableInfo {
			MethodTable = 0x10AF147B8,
			TypeName = "MyList",
			BaseSize = 32,
			ComponentSize = 8,
			MethodCount = 5,
			MetadataToken = 0x02000004,
			IsAbstract = true,
			IsSealed = true,
			IsFinalizable = true,
			ContainsPointers = true,
			Visibility = "public",
			BaseTypeName = "System.Object",
			Interfaces = new List<string> { "System.Collections.IEnumerable", "System.IDisposable" }
		};

		string output = TsvFormatter.FormatMethodTable(info);

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(20, row.Length);
		Assert.Contains("System.IDisposable", row[19]);
	}

	[Fact]
	public void MethodDesc_RowValues() {
		string output = TsvFormatter.FormatMethodDesc(new MethodDescInfo {
			MethodDesc = 1,
			MethodTable = 2,
			MethodName = "Foo",
			TypeName = "MyType",
			Signature = "void Foo()",
			NativeCode = 3,
			IsJitted = true,
			MetadataToken = 0x06000001
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(10, row.Length);
		Assert.Equal("Foo", row[2]);
	}

	[Fact]
	public void Class_FlattensFieldsAndMethods() {
		var info = new ClassInfo {
			EEClass = 1,
			MethodTable = 2,
			TypeName = "Roots",
			FieldCount = 2,
			StaticFieldCount = 1,
			ThreadStaticFieldCount = 1,
			MethodCount = 3,
			IsTruncated = true,
			Fields = new List<FieldMetadata> {
				new() { Name = "Name", TypeName = "System.String", Offset = 0, Size = 8 },
				new() { Name = "StaticHolder", TypeName = "Holder", IsStatic = true, Size = 8, Value = "13A611F30" }
			},
			Methods = new List<string> { "Foo", "Bar" }
		};

		string output = TsvFormatter.FormatClass(info);

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(11, row.Length);
		Assert.Contains("StaticHolder", row[9]);
		Assert.Equal("Foo; Bar", row[10]);
	}

	[Fact]
	public void ModuleDetails_RowValues() {
		string output = TsvFormatter.FormatModuleDetails(new ModuleDetails {
			Address = 1,
			Name = "app.dll",
			AssemblyName = "app",
			ImageBase = 0x102FF8000,
			AssemblyAddress = 0xC475702A0,
			Layout = "Flat",
			IsPEFile = true,
			TypeCount = 6,
			TypesWithStaticFieldsCount = 4,
			Size = 6656,
			MetadataLength = 2952
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(14, row.Length);
		Assert.Equal("Flat", row[10]);
	}

	[Fact]
	public void AssemblyDetails_FlattensModules() {
		string output = TsvFormatter.FormatAssemblyDetails(new AssemblyDetails {
			AssemblyAddress = 0xC475702A0,
			Name = "app",
			Modules = new List<string> { "app.dll", "app.core.dll" }
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(5, row.Length);
		Assert.Equal("app.dll; app.core.dll", row[4]);
	}

	[Fact]
	public void Name2EE_FlattensMethodOverloads() {
		var info = new Name2EEResult {
			ModuleName = "app.dll",
			TypeName = "MyType",
			MethodName = "Foo",
			MethodTable = 1,
			EEClass = 1,
			Methods = new List<MethodDescInfo> {
				new() { MethodDesc = 2, MethodTable = 1, MethodName = "Foo", Signature = "void Foo()", IsJitted = true }
			}
		};

		string output = TsvFormatter.FormatName2EE(info);

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(6, row.Length);
		Assert.Contains("void Foo()", row[5]);
	}

	[Fact]
	public void ThreadStates_AllFlagsAndFlattenedStateFlags() {
		string output = TsvFormatter.FormatThreadStates(new[] {
			new ThreadStateInfo {
				ManagedThreadId = 6, GcMode = "Cooperative", ApartmentState = "MTA", LockCount = 2,
				IsThreadPoolThread = true, IsBackground = true, IsFinalizer = true, IsGC = true,
				IsAborted = true, IsSuspendPending = true, StateFlags = new List<string> { "GC_ON_TRANSITIONS", "DEBUG_SUSPEND" }
			}
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(17, row.Length);
		Assert.Equal("GC_ON_TRANSITIONS; DEBUG_SUSPEND", row[16]);
		Assert.Equal("2", row[6]); // lockCount
	}

	[Fact]
	public void ThreadStates_UnknownLockCountIsAnEmptyCellNotMinusOne() {
		string output = TsvFormatter.FormatThreadStates(new[] {
			new ThreadStateInfo { ManagedThreadId = 1, LockCount = null }
		});

		var row = Lines(output)[1].Split('\t');
		Assert.Equal("", row[6]);
	}

	[Fact]
	public void ThreadExceptions_DropsEntriesWithoutAnException() {
		string output = TsvFormatter.FormatThreadExceptions(new[] {
			new ThreadExceptionInfo { ManagedThreadId = 1, Exception = null },
			new ThreadExceptionInfo { ManagedThreadId = 2, Exception = new ExceptionDetails { TypeName = "X", Message = "boom" } }
		});

		AssertConsistentColumnCount(output);
		var lines = Lines(output);
		Assert.Equal(2, lines.Length); // header + exactly the one real exception
		Assert.Contains("2", lines[1]);
	}

	[Fact]
	public void ThreadExceptions_RowValues() {
		string output = TsvFormatter.FormatThreadExceptions(new[] {
			new ThreadExceptionInfo {
				ManagedThreadId = 5,
				Source = ExceptionSource.ThreadCurrentException,
				Exception = new ExceptionDetails {
					Address = 0x10, TypeName = "System.NullReferenceException", Message = "boom", HResult = unchecked((int)0x80131600),
					InnerExceptions = new List<ExceptionDetails> { new() { TypeName = "Inner" } }
				}
			}
		});

		AssertConsistentColumnCount(output);
		var row = Lines(output)[1].Split('\t');
		Assert.Equal(9, row.Length);
		Assert.Equal("threadCurrentException", row[2]);
		Assert.Equal("System.NullReferenceException", row[4]);
		Assert.Equal("1", row[8]); // innerExceptionCount
	}
}