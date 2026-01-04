using DotNetDump.Core.Models;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetDump.Core.Analyzers
{
    public class ThreadAnalyzer
    {
        private readonly IDumpContext _context;

        public ThreadAnalyzer(IDumpContext context)
        {
            _context = context;
        }

        public IEnumerable<ThreadInfo> GetThreads(QueryParameters parameters)
        {
            var threads = _context.Runtime.Threads.Select(t => new ThreadInfo
            {
                ManagedThreadId = t.ManagedThreadId,
                OSThreadId = t.OSThreadId,
                IsAlive = t.IsAlive,
                ExceptionType = t.CurrentException?.Type?.Name,
                ExceptionMessage = t.CurrentException?.Message
            });

            // Sorting
            if (parameters.SortBy?.ToLower() == "exception")
            {
                threads = parameters.SortDirection == SortDirection.Asc ? threads.OrderBy(t => t.ExceptionType == null) : threads.OrderByDescending(t => t.ExceptionType != null);
            }
            else if (parameters.SortBy?.ToLower() == "osthreadid")
            {
                threads = parameters.SortDirection == SortDirection.Asc ? threads.OrderBy(t => t.OSThreadId) : threads.OrderByDescending(t => t.OSThreadId);
            }
            else
            {
                threads = parameters.SortDirection == SortDirection.Asc ? threads.OrderBy(t => t.ManagedThreadId) : threads.OrderByDescending(t => t.ManagedThreadId);
            }

            return threads.Skip(parameters.Offset).Take(parameters.Limit);
        }

        public IEnumerable<StackGroup> GetStackTraceGroups(int maxFrames = 20)
        {
            var groups = new Dictionary<string, StackGroup>();

            foreach (var thread in _context.Runtime.Threads)
            {
                if (!thread.IsAlive) continue;

                var frames = thread.EnumerateStackTrace().Take(maxFrames).Select(f => f.ToString() ?? "").ToList();
                var stackKey = string.Join("\n", frames);

                if (!groups.TryGetValue(stackKey, out var group))
                {
                    group = new StackGroup { Frames = frames };
                    groups[stackKey] = group;
                }
                group.ManagedThreadIds.Add(thread.ManagedThreadId);
            }

            return groups.Values.OrderByDescending(g => g.ThreadCount);
        }
    }
}
