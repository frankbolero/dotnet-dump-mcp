using DotNetDump.Core.Models;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetDump.Core.Analyzers
{
    public class HeapAnalyzer
    {
        private readonly IDumpContext _context;

        public HeapAnalyzer(IDumpContext context)
        {
            _context = context;
        }

        private ClrHeap GetHeap()
        {
            if (!_context.IsLoaded || _context.Heap == null)
                throw new InvalidOperationException("No dump loaded. Please use 'load_dump' tool first.");
            return _context.Heap;
        }

        public IEnumerable<HeapStatItem> GetHeapStatistics(QueryParameters parameters)
        {
            var heap = GetHeap();
            var stats = from obj in heap.EnumerateObjects()
                        let type = obj.Type
                        where type != null
                        group obj by new { type.Name, type.MethodTable } into g
                        select new HeapStatItem
                        {
                            TypeName = g.Key.Name,
                            MethodTable = g.Key.MethodTable,
                            Count = g.Count(),
                            TotalSize = g.Sum(p => (long)p.Size)
                        };

            // Apply sorting
            if (parameters.SortBy?.ToLower() == "count")
            {
                stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.Count) : stats.OrderByDescending(s => s.Count);
            }
            else if (parameters.SortBy?.ToLower() == "typename")
            {
                stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.TypeName) : stats.OrderByDescending(s => s.TypeName);
            }
            else // Default: TotalSize
            {
                stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.TotalSize) : stats.OrderByDescending(s => s.TotalSize);
            }

            return stats.Skip(parameters.Offset).Take(parameters.Limit);
        }

        public IEnumerable<HeapObjectItem> GetObjects(QueryParameters parameters, string? typeFilter = null)
        {
            var heap = GetHeap();
            var objects = heap.EnumerateObjects()
                .Where(obj => typeFilter == null || (obj.Type?.Name?.Contains(typeFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(obj => new HeapObjectItem
                {
                    Address = obj.Address,
                    MethodTable = obj.Type?.MethodTable ?? 0,
                    Size = obj.Size,
                    TypeName = obj.Type?.Name
                });

            // Apply sorting
            if (parameters.SortBy?.ToLower() == "size")
            {
                objects = parameters.SortDirection == SortDirection.Asc ? objects.OrderBy(o => o.Size) : objects.OrderByDescending(o => o.Size);
            }
            else if (parameters.SortBy?.ToLower() == "address")
            {
                objects = parameters.SortDirection == SortDirection.Asc ? objects.OrderBy(o => o.Address) : objects.OrderByDescending(o => o.Address);
            }

            return objects.Skip(parameters.Offset).Take(parameters.Limit);
        }

        public IEnumerable<GCRootInfo> GetGCRoots(ulong targetAddress, QueryParameters parameters)
        {
            var runtime = _context.Runtime;
            if (runtime == null) return Enumerable.Empty<GCRootInfo>();

            var heap = GetHeap();
            var roots = new List<GCRootInfo>();

            // 1. Heap Roots (Handles, etc.)
            foreach (var root in heap.EnumerateRoots())
            {
                if (root.Object.Address == targetAddress)
                {
                    roots.Add(new GCRootInfo
                    {
                        Address = root.Address,
                        Kind = root.RootKind.ToString(),
                        RootName = null, // ClrRoot doesn't have Name
                        ObjectAddress = root.Object.Address,
                        ManagedThreadId = -1,
                        OSThreadId = 0
                    });
                }
            }

            // 2. Stack Roots
            foreach (var thread in runtime.Threads)
            {
                foreach (var root in thread.EnumerateStackRoots())
                {
                    if (root.Object.Address == targetAddress)
                    {
                        string? name = root.StackFrame?.ToString();
                        roots.Add(new GCRootInfo
                        {
                            Address = root.Address,
                            Kind = "Stack",
                            RootName = name,
                            ObjectAddress = root.Object.Address,
                            ManagedThreadId = thread.ManagedThreadId,
                            OSThreadId = thread.OSThreadId
                        });
                    }
                }
            }

            return roots.Skip(parameters.Offset).Take(parameters.Limit);
        }
    }
}