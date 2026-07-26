using DotNetDump.Core.Formatting;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// Output tests. The formatters are pure functions over models, so every one of these runs without a
/// dump — which is where most output defects live.
/// </summary>
public class MarkdownFormatterTests {
	[Fact]
	public void HeapStatistics_IncludeTheMethodTable() {
		// Without the MethodTable an agent cannot pivot from a dump_heap row to dump_mt / list_objects.
		string output = MarkdownFormatter.FormatHeapStatistics(new[] {
			new HeapStatItem { TypeName = "System.String", MethodTable = 0x10AE47598, Count = 4928, TotalSize = 199724 }
		});

		Assert.Contains("MethodTable", output);
		Assert.Contains("000000010AE47598", output);
		Assert.Contains("4,928", output);
		Assert.Contains("199,724", output);
		Assert.Contains("System.String", output);
	}

	[Fact]
	public void Modules_ReportSizeInDecimalBytes() {
		// Size used to render as bare hex next to decimal sizes elsewhere, with nothing marking it.
		string output = MarkdownFormatter.FormatModules(new[] {
			new ModuleInfo { Name = "app.dll", ImageBase = 0x102FF8000, Size = 6656 }
		});

		Assert.Contains("6,656", output);
		Assert.DoesNotContain("| 1A00 |", output);
		Assert.Contains("0000000102FF8000", output);
	}

	[Fact]
	public void GCRootPaths_SayPlainlyWhenNothingIsRooted() {
		string output = MarkdownFormatter.FormatGCRootPaths(Array.Empty<GCRootPathInfo>(), 0x13A611F10);

		Assert.Contains("No GC root path found", output);
		Assert.Contains("000000013A611F10", output);
		// An empty table is indistinguishable from a broken query; the reason must be stated.
		Assert.Contains("unrooted", output);
	}

	[Fact]
	public void GCRootPaths_RenderTheWholeChain() {
		var path = new GCRootPathInfo {
			RootAddress = 0x16D24DBE8,
			RootKind = "Stack",
			ManagedThreadId = 1,
			TargetAddress = 0x13A611F10,
			Path = new List<GCRootPathNode> {
				new() { Address = 0x13A611F50, TypeName = "System.Collections.Generic.List<Leaf>", Size = 32 },
				new() { Address = 0x13A630CA8, TypeName = "Leaf[]", Size = 3000 },
				new() { Address = 0x13A611F10, TypeName = "Leaf", Size = 32 }
			}
		};

		string output = MarkdownFormatter.FormatGCRootPaths(new[] { path }, 0x13A611F10);

		Assert.Contains("Stack", output);
		Assert.Contains("thread 1", output);
		Assert.Contains("List<Leaf>", output);
		Assert.Contains("Leaf[]", output);
		Assert.Contains("target", output);
		Assert.Contains("**Depth:** 2", output);
	}

	[Fact]
	public void GCRootPaths_MarkPinnedAndInteriorRoots() {
		var path = new GCRootPathInfo {
			RootKind = "PinnedHandle",
			IsPinned = true,
			IsInterior = true,
			Path = new List<GCRootPathNode> { new() { Address = 0x10, TypeName = "System.Object" } }
		};

		string output = MarkdownFormatter.FormatGCRootPaths(new[] { path }, 0x10);

		Assert.Contains("pinned", output);
		Assert.Contains("interior", output);
	}

	[Fact]
	public void HeapSegments_NeverLabelAModernSegmentAsGenerationMinusOne() {
		var summary = new HeapSummaryInfo {
			IsServerGC = false,
			SubHeapCount = 1,
			CanWalkHeap = true,
			DynamicAdaptationMode = null,
			Segments = new List<HeapSegmentInfo> {
				new() { Start = 0x109294008, End = 0x109297050, Size = 12360, Kind = "Frozen", Generation = 2, CommittedSize = 65536, ReservedSize = 4128768 },
				new() { Start = 0x13A608000, End = 0x13A674268, Size = 442984, Kind = "Ephemeral", Generation = null, CommittedSize = 540672, ReservedSize = 267878400, Gen0Size = 442936, Gen1Size = 24, Gen2Size = 24 }
			}
		};

		string output = MarkdownFormatter.FormatHeapSegments(summary);

		Assert.DoesNotContain("-1", output);
		Assert.Contains("Frozen", output);
		Assert.Contains("Ephemeral", output);
		Assert.Contains("mixed", output);          // ephemeral spans generations
		Assert.Contains("Workstation", output);
		Assert.Contains("540,672", output);        // committed
		Assert.Contains("267,878,400", output);    // reserved
		Assert.Contains("442,936", output);        // per-generation breakdown
	}

	[Fact]
	public void HeapSegments_ReportDatasWhenPresent() {
		var summary = new HeapSummaryInfo {
			IsServerGC = true,
			SubHeapCount = 4,
			DynamicAdaptationMode = 1,
			Segments = new List<HeapSegmentInfo>()
		};

		string output = MarkdownFormatter.FormatHeapSegments(summary);

		Assert.Contains("Server", output);
		Assert.Contains("**GC Heaps:** 4", output);
		Assert.Contains("DATAS", output);
		Assert.DoesNotContain("off / not reported", output);
	}

	[Fact]
	public void ThreadStates_RenderAnUnknownLockCountAsUnknown() {
		// The DAC reports 0xFFFFFFFF for "no data"; it must not surface as -1 or 0.
		string output = MarkdownFormatter.FormatThreadStates(new[] {
			new ThreadStateInfo { ManagedThreadId = 1, OSThreadId = 0x1280B6, LockCount = null, GcMode = "Preemptive", ApartmentState = "None" }
		});

		Assert.Contains("unknown", output);
		Assert.DoesNotContain("| -1 |", output);
		Assert.Contains("Preemptive", output);
	}

	[Fact]
	public void ThreadStates_RenderEveryRealFlag() {
		string output = MarkdownFormatter.FormatThreadStates(new[] {
			new ThreadStateInfo {
				ManagedThreadId = 6,
				GcMode = "Cooperative",
				ApartmentState = "MTA",
				LockCount = 2,
				IsThreadPoolThread = true,
				IsBackground = true,
				IsFinalizer = true,
				IsGC = true,
				IsAborted = true,
				IsSuspendPending = true
			}
		});

		Assert.Contains("ThreadPool", output);
		Assert.Contains("Background", output);
		Assert.Contains("Finalizer", output);
		Assert.Contains("GC", output);
		Assert.Contains("Aborted", output);
		Assert.Contains("SuspendPending", output);
		Assert.Contains("MTA", output);
		Assert.Contains("| 2 |", output);
	}

	[Fact]
	public void DetailedStacks_HeaderNamesTheRealThread() {
		// The header used to show a positional counter, so thread 10 appeared as "Thread 1".
		string output = MarkdownFormatter.FormatDetailedStacks(new[] {
			new ThreadStackInfo { ManagedThreadId = 10, OSThreadId = 0x1280CF, Frames = new List<StackFrameInfo>() }
		});

		Assert.Contains("Thread 10", output);
		Assert.DoesNotContain("Thread 1:", output);
		Assert.Contains("1280CF", output);
	}

	[Fact]
	public void DetailedStacks_KeepTheDeclaringTypeAndDropThePath() {
		string output = MarkdownFormatter.FormatDetailedStacks(new[] {
			new ThreadStackInfo {
				ManagedThreadId = 1,
				Frames = new List<StackFrameInfo> {
					new() {
						InstructionPointer = 0x109F6C440,
						StackPointer = 0x16E5B2A40,
						FrameKind = "ManagedMethod",
						MethodName = "System.Threading.Thread.Sleep",
						ModuleName = "System.Private.CoreLib.dll"
					}
				}
			}
		});

		Assert.Contains("System.Threading.Thread.Sleep", output);
		Assert.Contains("System.Private.CoreLib.dll", output);
		// An absolute path on every frame is noise, not information.
		Assert.DoesNotContain("/usr/local/share", output);
	}

	[Fact]
	public void HeapVerification_ReportsTheCorruptionKindAndRealOffset() {
		string output = MarkdownFormatter.FormatHeapVerification(new[] {
			new HeapCorruptionInfo {
				Object = 0x13A611F10,
				Kind = "InvalidMethodTable",
				Offset = 0x18,
				Message = "invalid method table",
				TypeName = "Leaf"
			}
		});

		Assert.Contains("FAILED", output);
		Assert.Contains("InvalidMethodTable", output);
		Assert.Contains("18", output);
		Assert.Contains("Leaf", output);
	}

	[Fact]
	public void HeapVerification_PassesCleanly() {
		string output = MarkdownFormatter.FormatHeapVerification(Array.Empty<HeapCorruptionInfo>());

		Assert.Contains("PASSED", output);
		Assert.Contains("No corruption detected", output);
	}

	[Fact]
	public void MethodTable_RendersRealFlagsAndInterfaces() {
		var info = new MethodTableInfo {
			MethodTable = 0x10AF147B8,
			TypeName = "MyList",
			BaseSize = 32,
			ComponentSize = 8,
			MethodCount = 5,
			MetadataToken = 0x02000004,
			IsInterface = false,
			IsAbstract = true,
			IsSealed = true,
			IsFinalizable = true,
			ContainsPointers = true,
			Visibility = "public",
			BaseTypeName = "System.Object",
			Interfaces = new List<string> { "System.Collections.IEnumerable", "System.IDisposable" }
		};

		string output = MarkdownFormatter.FormatMethodTable(info);

		Assert.Contains("Abstract: True", output);
		Assert.Contains("Sealed: True", output);
		Assert.Contains("Finalizable: True", output);
		Assert.Contains("ContainsPointers: True", output);
		Assert.Contains("ComponentSize", output);
		Assert.Contains("System.IDisposable", output);
		Assert.Contains("public", output);
		// EEClass is not a distinct address in ClrMD; saying so beats printing the MT twice.
		Assert.Contains("does not expose the EEClass", output);
	}

	[Fact]
	public void Class_ShowsStaticFieldValuesAndTrueCounts() {
		var info = new ClassInfo {
			MethodTable = 0x10AF147B8,
			TypeName = "Roots",
			FieldCount = 2,
			StaticFieldCount = 1,
			ThreadStaticFieldCount = 1,
			MethodCount = 3,
			IsTruncated = true,
			Fields = new List<FieldMetadata> {
				new() { Name = "Name", TypeName = "System.String", Offset = 0, Size = 8 },
				new() { Name = "StaticHolder", TypeName = "Holder", IsStatic = true, Size = 8, Value = "13A611F30 <Holder>" }
			}
		};

		string output = MarkdownFormatter.FormatClass(info);

		Assert.Contains("2 instance, 1 static, 1 thread-static", output);
		Assert.Contains("13A611F30 <Holder>", output);
		Assert.Contains("truncated", output);
	}

	[Fact]
	public void SyncBlocks_DistinguishThinLocksAndSayWhenNoneAreHeld() {
		string empty = MarkdownFormatter.FormatSyncBlocks(Array.Empty<SyncBlockInfo>());
		Assert.Contains("No held monitors", empty);
		Assert.Contains("thin locks", empty);

		string output = MarkdownFormatter.FormatSyncBlocks(new[] {
			new SyncBlockInfo { ObjectAddress = 0x13A6621D0, TypeName = "System.Object", IsMonitorHeld = true, IsThinLock = true, ManagedThreadId = 4 }
		});

		Assert.Contains("thin", output);
		Assert.Contains("System.Object", output);
		Assert.Contains("| 4 |", output);
	}

	[Fact]
	public void SyncBlocks_FallBackToTheThreadAddressWhenUnmapped() {
		string output = MarkdownFormatter.FormatSyncBlocks(new[] {
			new SyncBlockInfo { ObjectAddress = 0x10, ManagedThreadId = null, HoldingThreadAddress = 0xC46D69500 }
		});

		Assert.Contains("@C46D69500", output);
		// A missing thread id must not render as a real thread numbered -1.
		Assert.DoesNotContain("| -1 |", output);
	}

	[Fact]
	public void GCHandles_ExposeStrengthAndDependentTargets() {
		string output = MarkdownFormatter.FormatGCHandles(new[] {
			new GCHandleInfo { Address = 0x1033E1178, Object = 0x13A66E560, Kind = "WeakShort", TypeName = "System.Threading.Thread", IsStrong = false },
			new GCHandleInfo { Address = 0x1033E1350, Object = 0x13A66C538, Kind = "Dependent", TypeName = "System.Object", IsStrong = true, ReferenceCount = 3, DependentTarget = 0x13A611F10 }
		});

		Assert.Contains("Strong", output);
		Assert.Contains("RefCount", output);
		Assert.Contains("Dependent", output);
		Assert.Contains("000000013A611F10", output);
		Assert.Contains("| 3 |", output);
	}

	[Fact]
	public void ThreadExceptions_ReportHeapExceptionsNotJustInFlightOnes() {
		// The common case in a collected dump: the exception was caught, so it is on the heap only.
		string output = MarkdownFormatter.FormatThreadExceptions(new[] {
			new ThreadExceptionInfo {
				Source = ExceptionSource.Heap,
				Exception = new ExceptionDetails {
					Address = 0x13A665AE0,
					TypeName = "System.ApplicationException",
					Message = "outer-boom",
					HResult = unchecked((int)0x80131600),
					InnerExceptions = new List<ExceptionDetails> {
						new() { Address = 0x13A665A20, TypeName = "System.InvalidOperationException", Message = "inner-boom" }
					}
				}
			}
		});

		Assert.Contains("Heap exception", output);
		Assert.Contains("outer-boom", output);
		Assert.Contains("inner-boom", output);
		Assert.Contains("Inner Exception", output);
		Assert.Contains("80131600", output);
		Assert.Contains("0 in flight", output);
	}

	[Fact]
	public void ThreadExceptions_LabelInFlightExceptions() {
		string output = MarkdownFormatter.FormatThreadExceptions(new[] {
			new ThreadExceptionInfo {
				ManagedThreadId = 5,
				OSThreadId = 0x1280CA,
				Source = ExceptionSource.ThreadCurrentException,
				Exception = new ExceptionDetails { Address = 0x10, TypeName = "System.NullReferenceException" }
			}
		});

		Assert.Contains("Thread 5", output);
		Assert.Contains("in flight", output);
		Assert.Contains("1 in flight", output);
	}

	[Fact]
	public void ThreadExceptions_SayNothingWasFoundRatherThanPrintingEmptyBlocks() {
		// Previously this printed "No exception on this thread." once per thread.
		string output = MarkdownFormatter.FormatThreadExceptions(new[] {
			new ThreadExceptionInfo { ManagedThreadId = 1, Exception = null },
			new ThreadExceptionInfo { ManagedThreadId = 2, Exception = null }
		});

		Assert.Contains("No exceptions found", output);
		Assert.DoesNotContain("Thread 1", output);
	}

	[Fact]
	public void ModuleDetails_ReportTheRealAssemblyAddressAndLayout() {
		string output = MarkdownFormatter.FormatModuleDetails(new ModuleDetails {
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

		Assert.Contains("0000000C475702A0", output);
		Assert.Contains("Layout: Flat", output);
		Assert.Contains("**Type Count:** 6", output);
		// The count is exact now, so it must not be advertised as a sample.
		Assert.DoesNotContain("sampled", output);
		Assert.DoesNotContain("~", output);
	}

	[Fact]
	public void AssemblyDetails_ReportTheAssemblyAddress() {
		string output = MarkdownFormatter.FormatAssemblyDetails(new AssemblyDetails {
			Name = "app",
			AssemblyAddress = 0xC475702A0,
			Modules = new List<string> { "app.dll" }
		});

		Assert.Contains("0000000C475702A0", output);
		Assert.Contains("app.dll", output);
	}

	[Fact]
	public void ThreadPool_ShowsCpuAndCompletionPortsWhenAvailable() {
		string output = MarkdownFormatter.FormatThreadPool(new ThreadPoolInfo {
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
			FreeCompletionPorts = 1
		});

		Assert.Contains("Portable", output);
		Assert.Contains("42%", output);
		Assert.Contains("Completion Ports", output);
		Assert.Contains("Retired Threads", output);
	}

	[Fact]
	public void ThreadPool_OmitsCompletionPortsWhenAbsent() {
		string output = MarkdownFormatter.FormatThreadPool(new ThreadPoolInfo { Type = "Portable", HasCompletionPortData = false });

		Assert.DoesNotContain("Completion Ports", output);
	}

	[Theory]
	[InlineData("System.String")]
	[InlineData("")]
	public void EveryTableStaysMarkdownWellFormed(string typeName) {
		// Each rendered row must have the same column count as its header.
		string output = MarkdownFormatter.FormatHeapStatistics(new[] {
			new HeapStatItem { TypeName = typeName, MethodTable = 0x10, Count = 1, TotalSize = 1 }
		});

		var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		int headerColumns = lines[0].Count(c => c == '|');

		foreach (var line in lines) {
			Assert.Equal(headerColumns, line.Count(c => c == '|'));
		}
	}
}
