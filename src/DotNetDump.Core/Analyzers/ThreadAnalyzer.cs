using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers {
	public class ThreadAnalyzer {
		private readonly IDumpContext _context;

		public ThreadAnalyzer(IDumpContext context) {
			_context = context;
		}

		private ClrRuntime GetRuntime() {
			if (!_context.IsLoaded || _context.Runtime == null)
				throw new InvalidOperationException("No dump loaded. Please use 'load_dump' tool first.");
			return _context.Runtime;
		}

		public IEnumerable<ThreadInfo> GetThreads(QueryParameters parameters) {
			var runtime = GetRuntime();
			var threads = runtime.Threads.Select(t => new ThreadInfo {
				ManagedThreadId = t.ManagedThreadId,
				OSThreadId = t.OSThreadId,
				IsAlive = t.IsAlive,
				ExceptionType = t.CurrentException?.Type?.Name,
				ExceptionMessage = t.CurrentException?.Message
			});

			// Sorting
			if (parameters.SortBy?.ToLower() == "exception") {
				threads = parameters.SortDirection == SortDirection.Asc ? threads.OrderBy(t => t.ExceptionType == null) : threads.OrderByDescending(t => t.ExceptionType != null);
			} else if (parameters.SortBy?.ToLower() == "osthreadid") {
				threads = parameters.SortDirection == SortDirection.Asc ? threads.OrderBy(t => t.OSThreadId) : threads.OrderByDescending(t => t.OSThreadId);
			} else {
				threads = parameters.SortDirection == SortDirection.Asc ? threads.OrderBy(t => t.ManagedThreadId) : threads.OrderByDescending(t => t.ManagedThreadId);
			}

			return threads.Skip(parameters.Offset).Take(parameters.Limit);
		}

		public IEnumerable<StackGroup> GetStackTraceGroups(int maxFrames = 20) {
			var runtime = GetRuntime();
			var groups = new Dictionary<string, StackGroup>();

			foreach (var thread in runtime.Threads) {
				if (!thread.IsAlive) continue;

				var frames = thread.EnumerateStackTrace().Take(maxFrames).Select(f => f.ToString() ?? "").ToList();
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
				MinThreads = pool.MinThreads,
				MaxThreads = pool.MaxThreads,
				Type = type
			};
		}

		public IEnumerable<ThreadStackInfo> GetDetailedStacks(QueryParameters parameters, int maxFrames = 100) {
			var runtime = GetRuntime();
			var stacks = runtime.Threads
				.Where(t => t.IsAlive)
				.Select(t => new ThreadStackInfo {
					ManagedThreadId = t.ManagedThreadId,
					OSThreadId = t.OSThreadId,
					IsAlive = t.IsAlive,
					ExceptionType = t.CurrentException?.Type?.Name,
					Frames = t.EnumerateStackTrace()
						.Take(maxFrames)
						.Select(f => new StackFrameInfo {
							InstructionPointer = f.InstructionPointer,
							StackPointer = f.StackPointer,
							FrameKind = f.Kind.ToString(),
							MethodName = f.Method?.Name ?? f.ToString(),
							ModuleName = f.Method?.Type?.Module?.Name,
							IsManaged = f.Kind == ClrStackFrameKind.ManagedMethod
						})
						.ToList()
				});

			// Sort by thread ID
			if (parameters.SortBy?.ToLower() == "osthreadid") {
				stacks = parameters.SortDirection == SortDirection.Asc ? stacks.OrderBy(t => t.OSThreadId) : stacks.OrderByDescending(t => t.OSThreadId);
			} else {
				stacks = parameters.SortDirection == SortDirection.Asc ? stacks.OrderBy(t => t.ManagedThreadId) : stacks.OrderByDescending(t => t.ManagedThreadId);
			}

			return stacks.Skip(parameters.Offset).Take(parameters.Limit);
		}

		public IEnumerable<ThreadStateInfo> GetThreadStates(QueryParameters parameters) {
			var runtime = GetRuntime();
			var threadStates = runtime.Threads.Select(t => new ThreadStateInfo {
				ManagedThreadId = t.ManagedThreadId,
				OSThreadId = t.OSThreadId,
				IsAlive = t.IsAlive,
				ExceptionType = t.CurrentException?.Type?.Name,
				Address = t.Address,
				GcMode = "Unknown", // IsGCMode not available in ClrMD v3
				LockCount = (int)t.LockCount,
				ApartmentState = "Unknown", // Not available in ClrMD v3
				IsThreadPoolThread = false, // Not directly available, would need to check against ThreadPool threads
				IsGC = false, // Not available in ClrMD v3
				IsFinalizer = t.IsFinalizer,
				IsBackground = false, // Not available in ClrMD v3
				IsUnstarted = !t.IsAlive && t.Address != 0,
				IsAborted = false // Not available in ClrMD v3
			});

			// Sorting
			if (parameters.SortBy?.ToLower() == "osthreadid") {
				threadStates = parameters.SortDirection == SortDirection.Asc ? threadStates.OrderBy(t => t.OSThreadId) : threadStates.OrderByDescending(t => t.OSThreadId);
			} else if (parameters.SortBy?.ToLower() == "lockcount") {
				threadStates = parameters.SortDirection == SortDirection.Asc ? threadStates.OrderBy(t => t.LockCount) : threadStates.OrderByDescending(t => t.LockCount);
			} else {
				threadStates = parameters.SortDirection == SortDirection.Asc ? threadStates.OrderBy(t => t.ManagedThreadId) : threadStates.OrderByDescending(t => t.ManagedThreadId);
			}

			return threadStates.Skip(parameters.Offset).Take(parameters.Limit);
		}

		public IEnumerable<ThreadExceptionInfo> GetThreadExceptions(QueryParameters parameters, bool onlyWithExceptions = true) {
			var runtime = GetRuntime();
			var threads = runtime.Threads.AsEnumerable();

			if (onlyWithExceptions) {
				threads = threads.Where(t => t.CurrentException != null);
			}

			var exceptionInfos = threads.Select(t => new ThreadExceptionInfo {
				ManagedThreadId = t.ManagedThreadId,
				OSThreadId = t.OSThreadId,
				Exception = t.CurrentException != null ? BuildExceptionDetails(t.CurrentException) : null
			});

			// Sorting
			if (parameters.SortBy?.ToLower() == "osthreadid") {
				exceptionInfos = parameters.SortDirection == SortDirection.Asc ? exceptionInfos.OrderBy(t => t.OSThreadId) : exceptionInfos.OrderByDescending(t => t.OSThreadId);
			} else {
				exceptionInfos = parameters.SortDirection == SortDirection.Asc ? exceptionInfos.OrderBy(t => t.ManagedThreadId) : exceptionInfos.OrderByDescending(t => t.ManagedThreadId);
			}

			return exceptionInfos.Skip(parameters.Offset).Take(parameters.Limit);
		}

		private ExceptionDetails BuildExceptionDetails(ClrException exception, int maxDepth = 5) {
			if (maxDepth <= 0) {
				return new ExceptionDetails {
					Address = exception.Address,
					TypeName = exception.Type?.Name ?? "<unknown>",
					Message = "(max depth reached)"
				};
			}

			var details = new ExceptionDetails {
				Address = exception.Address,
				TypeName = exception.Type?.Name ?? "<unknown>",
				Message = exception.Message,
				HResult = exception.HResult,
				StackTrace = new List<string>()
			};

			// Get stack trace from exception object
			foreach (var frame in exception.StackTrace) {
				var frameName = frame.Method?.Name ?? frame.ToString();
				if (!string.IsNullOrEmpty(frameName)) {
					details.StackTrace.Add(frameName);
				}
			}

			// Get inner exception
			if (exception.Inner != null) {
				details.InnerExceptions.Add(BuildExceptionDetails(exception.Inner, maxDepth - 1));
			}

			return details;
		}
	}
}