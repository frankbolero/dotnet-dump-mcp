using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Caching;
using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers {
	public class ThreadAnalyzer {

		private readonly IDumpContext _context;
		private readonly IAnalysisCache _cache;

		public ThreadAnalyzer(IDumpContext context, IAnalysisCache? cache = null) {
			_context = context;
			_cache = cache ?? NullAnalysisCache.Instance;
		}

		private ClrRuntime GetRuntime() {
			if (!_context.IsLoaded || _context.Runtime == null)
				throw new InvalidOperationException("No dump loaded. Please use 'load_dump' tool first.");
			return _context.Runtime;
		}

		public PagedResult<ThreadInfo> GetThreads(QueryParameters parameters) {
			parameters.Filter.EnsureSupported("clrthreads", ThreadInfoFilter.Honored);

			var runtime = GetRuntime();
			var allThreads = runtime.Threads.Select(t => new ThreadInfo {
				ManagedThreadId = t.ManagedThreadId,
				OSThreadId = t.OSThreadId,
				IsAlive = t.IsAlive,
				ExceptionType = t.CurrentException?.Type?.Name,
				ExceptionMessage = t.CurrentException?.Message
			}).ToList();

			List<ThreadInfo> filtered = allThreads.Where(t => ThreadInfoFilter.Matches(t, parameters.Filter)).ToList();

			// Sorting
			IEnumerable<ThreadInfo> sorted = filtered;
			if (parameters.SortBy?.ToLower() == "exception") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.ExceptionType == null) : sorted.OrderByDescending(t => t.ExceptionType != null);
			} else if (parameters.SortBy?.ToLower() == "osthreadid") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.OSThreadId) : sorted.OrderByDescending(t => t.OSThreadId);
			} else {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.ManagedThreadId) : sorted.OrderByDescending(t => t.ManagedThreadId);
			}

			var page = sorted.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<ThreadInfo>(page, filtered.Count, allThreads.Count, parameters.Offset, parameters.Limit);
		}

		public IEnumerable<StackGroup> GetStackTraceGroups(int maxFrames = 20) {
			var runtime = GetRuntime();
			var groups = new Dictionary<string, StackGroup>();

			foreach (var thread in runtime.Threads) {
				if (!thread.IsAlive) continue;

				// Push the frame limit into the DAC rather than over-walking and truncating here.
				var frames = thread.EnumerateStackTrace(includeContext: false, maxFrames: maxFrames)
					.Select(f => f.ToString() ?? "")
					.ToList();
				var stackKey = string.Join("\n", frames);

				if (!groups.TryGetValue(stackKey, out var group)) {
					group = new StackGroup { Frames = frames };
					groups[stackKey] = group;
				}
				group.ManagedThreadIds.Add(thread.ManagedThreadId);
			}

			return groups.Values.OrderByDescending(g => g.ThreadCount);
		}

		public ThreadPoolInfo GetThreadPoolInfo() {
			var runtime = GetRuntime();
			var pool = runtime.ThreadPool;

			if (pool == null) {
				return new ThreadPoolInfo {
					Type = "Unavailable"
				};
			}

			int total = 0;
			string type = "Unknown";

			if (pool.UsingWindowsThreadPool) {
				type = "Windows";
				total = pool.WindowsThreadPoolThreadCount;
			} else if (pool.UsingPortableThreadPool) {
				type = "Portable";
				total = pool.ActiveWorkerThreads + pool.IdleWorkerThreads;
			} else if (pool.HasLegacyData) {
				type = "Legacy";
				total = pool.ActiveWorkerThreads + pool.IdleWorkerThreads;
			}

			return new ThreadPoolInfo {
				TotalThreads = total,
				ActiveThreads = pool.ActiveWorkerThreads,
				IdleThreads = pool.IdleWorkerThreads,
				RetiredThreads = pool.RetiredWorkerThreads,
				MinThreads = pool.MinThreads,
				MaxThreads = pool.MaxThreads,
				Type = type,
				CpuUtilization = pool.CpuUtilization,
				HasCompletionPortData = pool.HasLegacyData,
				TotalCompletionPorts = pool.TotalCompletionPorts,
				FreeCompletionPorts = pool.FreeCompletionPorts,
				MaxFreeCompletionPorts = pool.MaxFreeCompletionPorts,
				CompletionPortCurrentLimit = pool.CompletionPortCurrentLimit,
				MinCompletionPorts = pool.MinCompletionPorts,
				MaxCompletionPorts = pool.MaxCompletionPorts
			};
		}

		/// <summary>
		/// Stack traces, one per live thread.
		/// </summary>
		/// <remarks>
		/// Sorting and paging happen on the threads, and stacks are walked only for the resulting page.
		/// Walking a stack is by far the expensive part, and a hung process can carry thousands of
		/// threads — materialising every stack to return fifty would make this the slowest command in
		/// the tool. <see cref="PagedResult{T}.TotalAvailable"/> is the live thread count, which is known
		/// without walking anything.
		/// <para>
		/// <c>Text</c> (frame method name) is the one honored filter field that cannot be tested without
		/// a walk. <see cref="ThreadStackInfoFilter.MatchesThread"/> narrows on
		/// <c>ManagedThreadId</c>/<c>OSThreadId</c>/<c>HasException</c> straight off <see cref="ClrThread"/>
		/// before any walk, so those stay free; only when <see cref="FilterSpec.Text"/> is set does this
		/// method walk every remaining candidate's stack to test it, rather than only the requested page.
		/// </para>
		/// </remarks>
		public PagedResult<ThreadStackInfo> GetDetailedStacks(QueryParameters parameters, int maxFrames = 100) {
			parameters.Filter.EnsureSupported("dumpstack", ThreadStackInfoFilter.Honored);
			FilterSpec filter = parameters.Filter;

			var runtime = GetRuntime();
			var allAlive = runtime.Threads.Where(t => t.IsAlive).ToList();

			IEnumerable<ClrThread> filtered = allAlive;
			if (!filter.IsEmpty) {
				filtered = filtered.Where(t => ThreadStackInfoFilter.MatchesThread(t.ManagedThreadId, t.OSThreadId, t.CurrentException?.Type?.Name, filter));
			}

			var filteredList = filtered.ToList();

			// Sort by thread ID. Both keys come straight off the thread, so this is the same ordering
			// the previous implementation produced from the projected rows.
			IEnumerable<ClrThread> sorted = filteredList;
			if (parameters.SortBy?.ToLower() == "osthreadid") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.OSThreadId) : sorted.OrderByDescending(t => t.OSThreadId);
			} else {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.ManagedThreadId) : sorted.OrderByDescending(t => t.ManagedThreadId);
			}

			if (filter.Text != null) {
				// Text matches frame method name, which is only known once a thread's stack is walked.
				// Every remaining candidate has to be walked to test the filter, not just the requested
				// page -- the walk-avoidance below no longer applies once Text is in play.
				List<ThreadStackInfo> matched = sorted
					.Select(t => BuildThreadStackInfo(t, maxFrames))
					.Where(s => ThreadStackInfoFilter.MatchesFrameText(s.Frames, filter.Text))
					.ToList();

				var textFilteredPage = matched.Skip(parameters.Offset).Take(parameters.Limit).ToList();
				return new PagedResult<ThreadStackInfo>(textFilteredPage, matched.Count, allAlive.Count, parameters.Offset, parameters.Limit);
			}

			var page = sorted
				.Skip(parameters.Offset)
				.Take(parameters.Limit)
				.Select(t => BuildThreadStackInfo(t, maxFrames))
				.ToList();

			return new PagedResult<ThreadStackInfo>(page, filteredList.Count, allAlive.Count, parameters.Offset, parameters.Limit);
		}

		private static ThreadStackInfo BuildThreadStackInfo(ClrThread t, int maxFrames) => new() {
			ManagedThreadId = t.ManagedThreadId,
			OSThreadId = t.OSThreadId,
			IsAlive = t.IsAlive,
			ExceptionType = t.CurrentException?.Type?.Name,
			Frames = t.EnumerateStackTrace(includeContext: false, maxFrames: maxFrames)
				.Select(f => new StackFrameInfo {
					InstructionPointer = f.InstructionPointer,
					StackPointer = f.StackPointer,
					FrameKind = f.Kind.ToString(),
					MethodName = DescribeFrame(f),
					ModuleName = SimpleModuleName(f.Method?.Type?.Module?.Name),
					IsManaged = f.Kind == ClrStackFrameKind.ManagedMethod
				})
				.ToList()
		};

		/// <summary>
		/// Names a stack frame. Managed frames are qualified with their declaring type — a bare method
		/// name such as "Sleep" identifies nothing, and the module's absolute path is noise rather than
		/// information. Runtime frames keep the bracketed form SOS uses, so they stay visually distinct
		/// from real managed calls.
		/// </summary>
		private static string DescribeFrame(ClrStackFrame frame) {
			if (frame.Method != null) {
				string? typeName = frame.Method.Type?.Name;
				return string.IsNullOrEmpty(typeName)
					? frame.Method.Name ?? "(unknown)"
					: $"{typeName}.{frame.Method.Name}";
			}

			string name = frame.FrameName ?? frame.ToString() ?? "(unknown)";
			return name.StartsWith('[') ? name : $"[{name}]";
		}

		/// <summary>
		/// Module file name without its directory. Stack output repeats the module on every frame, so a
		/// full path costs a great deal and identifies nothing extra.
		/// </summary>
		private static string? SimpleModuleName(string? path) {
			if (string.IsNullOrEmpty(path))
				return path;

			int slash = path.LastIndexOfAny(new[] { '/', '\\' });
			return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
		}

		public PagedResult<ThreadStateInfo> GetThreadStates(QueryParameters parameters) {
			parameters.Filter.EnsureSupported("threadstate", ThreadStateInfoFilter.Honored);

			var runtime = GetRuntime();
			var allStates = runtime.Threads.Select(t => new ThreadStateInfo {
				ManagedThreadId = t.ManagedThreadId,
				OSThreadId = t.OSThreadId,
				IsAlive = t.IsAlive,
				ExceptionType = t.CurrentException?.Type?.Name,
				Address = t.Address,
				GcMode = t.GCMode.ToString(),
				LockCount = ThreadStateDecoder.NormalizeLockCount(t.LockCount),
				ApartmentState = ThreadStateDecoder.ApartmentState(t.State),
				IsThreadPoolThread = ThreadStateDecoder.IsThreadPoolThread(t.State),
				IsGC = t.IsGc,
				IsFinalizer = t.IsFinalizer,
				IsBackground = ThreadStateDecoder.IsBackground(t.State),
				IsUnstarted = ThreadStateDecoder.IsUnstarted(t.State),
				IsDead = ThreadStateDecoder.IsDead(t.State),
				IsAborted = ThreadStateDecoder.IsAborted(t.State),
				IsSuspendPending = ThreadStateDecoder.IsSuspendPending(t.State),
				StateFlags = ThreadStateDecoder.FlagNames(t.State).ToList()
			}).ToList();

			List<ThreadStateInfo> filtered = allStates.Where(t => ThreadStateInfoFilter.Matches(t, parameters.Filter)).ToList();

			// Sorting
			IEnumerable<ThreadStateInfo> sorted = filtered;
			if (parameters.SortBy?.ToLower() == "osthreadid") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.OSThreadId) : sorted.OrderByDescending(t => t.OSThreadId);
			} else if (parameters.SortBy?.ToLower() == "lockcount") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.LockCount ?? 0) : sorted.OrderByDescending(t => t.LockCount ?? 0);
			} else {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(t => t.ManagedThreadId) : sorted.OrderByDescending(t => t.ManagedThreadId);
			}

			var page = sorted.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<ThreadStateInfo>(page, filtered.Count, allStates.Count, parameters.Offset, parameters.Limit);
		}

		/// <summary>
		/// Exceptions in flight on a thread. Note that a dump collected after the fact usually has
		/// none: once an exception is caught, <c>CurrentException</c> is null and the exception object
		/// survives only on the heap. Use <paramref name="includeHeapExceptions"/> (or
		/// <see cref="GetExceptionByAddress"/>) to find those.
		/// </summary>
		public PagedResult<ThreadExceptionInfo> GetThreadExceptions(
			QueryParameters parameters,
			bool onlyWithExceptions = true,
			bool includeHeapExceptions = true) {

			// The in-flight path: rows carry an owning thread, unlike HeapAnalyzer.GetHeapExceptions'
			// heap-scan path, so ManagedThreadId/OSThreadId are honored here (DATA_CONTRACT.md §2.3).
			parameters.Filter.EnsureSupported("printexception (in flight)", ThreadExceptionInfoFilter.Honored);
			var typeNameMatcher = TypeNameMatcher.Create(parameters.Filter);

			var runtime = GetRuntime();
			var threads = runtime.Threads.AsEnumerable();

			if (onlyWithExceptions) {
				threads = threads.Where(t => t.CurrentException != null);
			}

			var exceptionInfos = threads.Select(t => new ThreadExceptionInfo {
				ManagedThreadId = t.ManagedThreadId,
				OSThreadId = t.OSThreadId,
				Source = ExceptionSource.ThreadCurrentException,
				Exception = t.CurrentException != null ? ExceptionMapper.Map(t.CurrentException) : null
			});

			// Sorting -- applies only to the (cheap) per-thread portion, matching prior behavior:
			// heap exceptions are appended afterwards in whatever order the walk produced them.
			if (parameters.SortBy?.ToLower() == "osthreadid") {
				exceptionInfos = parameters.SortDirection == SortDirection.Asc ? exceptionInfos.OrderBy(t => t.OSThreadId) : exceptionInfos.OrderByDescending(t => t.OSThreadId);
			} else {
				exceptionInfos = parameters.SortDirection == SortDirection.Asc ? exceptionInfos.OrderBy(t => t.ManagedThreadId) : exceptionInfos.OrderByDescending(t => t.ManagedThreadId);
			}

			var results = exceptionInfos.ToList();

			if (includeHeapExceptions) {
				// Exclude anything already reported as a thread's current exception.
				var seen = results
					.Where(r => r.Exception != null)
					.Select(r => r.Exception!.Address)
					.ToHashSet();

				var key = new CacheKey(_context.Identity, HeapAnalyzer.HeapExceptionsCacheOperation, "", HeapAnalyzer.CacheSchemaVersion);
				List<ExceptionDetails> heapExceptions = _cache.GetOrCompute(key, ComputeHeapExceptions);

				foreach (var details in heapExceptions) {
					if (seen.Add(details.Address)) {
						results.Add(new ThreadExceptionInfo {
							Source = ExceptionSource.Heap,
							Exception = details
						});
					}
				}
			}

			// Filtered after both sources are merged, so TotalAvailable is honest about the combined
			// result -- not just the (already-sorted) thread portion.
			List<ThreadExceptionInfo> filtered = results.Where(r => ThreadExceptionInfoFilter.Matches(r, parameters.Filter, typeNameMatcher)).ToList();

			var page = filtered.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<ThreadExceptionInfo>(page, filtered.Count, results.Count, parameters.Offset, parameters.Limit);
		}

		/// <summary>Reads a specific exception object by address, as SOS's <c>pe &lt;address&gt;</c> does.</summary>
		public ThreadExceptionInfo GetExceptionByAddress(ulong address) {
			var runtime = GetRuntime();
			var heap = runtime.Heap;

			var obj = heap.GetObject(address);
			if (obj.IsNull)
				throw new ArgumentException($"No object found at {address:X}.");

			if (obj.Type?.IsException != true)
				throw new ArgumentException($"Object at {address:X} is a {obj.Type?.Name ?? "<unknown type>"}, not an exception.");

			var exception = obj.AsException();
			if (exception == null)
				throw new ArgumentException($"Object at {address:X} could not be read as an exception.");

			// Attribute it to a thread if it happens to be that thread's in-flight exception.
			var owner = runtime.Threads.FirstOrDefault(t => t.CurrentException?.Address == address);

			return new ThreadExceptionInfo {
				ManagedThreadId = owner?.ManagedThreadId,
				OSThreadId = owner?.OSThreadId,
				Source = owner != null ? ExceptionSource.ThreadCurrentException : ExceptionSource.Address,
				Exception = ExceptionMapper.Map(exception)
			};
		}

		/// <summary>
		/// The walk-scale part of <see cref="GetThreadExceptions"/>. Shares its cache entry with
		/// <see cref="HeapAnalyzer.GetHeapExceptions"/> (see <see cref="HeapAnalyzer.HeapExceptionsCacheOperation"/>)
		/// — both perform the identical full-heap exception scan.
		/// </summary>
		private List<ExceptionDetails> ComputeHeapExceptions() {
			var heap = GetRuntime().Heap;
			var found = new List<ExceptionDetails>();

			foreach (var obj in heap.EnumerateObjects()) {
				if (obj.Type?.IsException != true)
					continue;

				ClrException? exception;
				try {
					exception = obj.AsException();
				} catch (Exception) {
					continue;
				}

				if (exception != null)
					found.Add(ExceptionMapper.Map(exception));
			}

			return found;
		}
	}
}