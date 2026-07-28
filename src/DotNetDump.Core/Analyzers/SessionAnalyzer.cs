using System;
using System.Linq;

using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers {
	/// <summary>
	/// Backs the CLI's <c>info</c> session/orientation command (CLI_DESIGN.md &#0167;4.1). Unlike the
	/// other analyzers this has no MCP tool equivalent -- it exists to give an invocation something
	/// cheap to check ("what am I looking at?") before running an expensive command.
	/// </summary>
	public class SessionAnalyzer {
		private readonly IDumpContext _context;

		public SessionAnalyzer(IDumpContext context) {
			_context = context;
		}

		/// <param name="explicitDacPath">
		/// The <c>--dac</c> value the caller passed to load the dump, if any. Used only to report
		/// whether the DAC match was verified by ClrMD or bypassed via <c>ignoreMismatch: true</c> --
		/// <see cref="IDumpContext"/> does not expose the DAC path ClrMD actually resolved internally,
		/// so this reports the caller's input, not ClrMD's internal choice.
		/// </param>
		public DumpInfo GetInfo(string? explicitDacPath) {
			if (!_context.IsLoaded || _context.Runtime == null || _context.DataTarget == null)
				throw new InvalidOperationException("No dump loaded.");

			var runtime = _context.Runtime;
			var clrInfo = runtime.ClrInfo;
			var dataTarget = _context.DataTarget;
			var heap = runtime.Heap;

			ulong heapSize = 0;
			foreach (var segment in heap.Segments) {
				heapSize += segment.Length;
			}

			string? expectedDac = clrInfo.DebuggingLibraries
				.FirstOrDefault(lib => lib.Kind == DebugLibraryKind.Dac)
				?.FileName;

			return new DumpInfo {
				RuntimeVersion = clrInfo.Version.ToString(),
				RuntimeFlavor = clrInfo.Flavor.ToString(),
				Architecture = dataTarget.DataReader.Architecture.ToString(),
				OperatingSystem = dataTarget.DataReader.TargetPlatform.ToString(),
				ExpectedDacFileName = expectedDac,
				ExplicitDacPath = explicitDacPath,
				DacMatchVerified = string.IsNullOrEmpty(explicitDacPath),
				IsServerGC = heap.IsServer,
				SubHeapCount = heap.SubHeaps.Length,
				HeapSizeBytes = heapSize,
				SegmentCount = heap.Segments.Length,
				ManagedThreadCount = runtime.Threads.Length,
			};
		}
	}
}