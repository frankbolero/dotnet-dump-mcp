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

            if (parameters.SortBy?.ToLower() == "count")
            {
                stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.Count) : stats.OrderByDescending(s => s.Count);
            }
            else if (parameters.SortBy?.ToLower() == "typename")
            {
                stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.TypeName) : stats.OrderByDescending(s => s.TypeName);
            }
            else
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

            foreach (var root in heap.EnumerateRoots())
            {
                if (root.Object.Address == targetAddress)
                {
                    roots.Add(new GCRootInfo
                    {
                        Address = root.Address,
                        Kind = root.RootKind.ToString(),
                        RootName = null,
                        ObjectAddress = root.Object.Address,
                        ManagedThreadId = -1,
                        OSThreadId = 0
                    });
                }
            }

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

        public ObjectDetails GetObjectDetails(ulong address)
        {
            var heap = GetHeap();
            var obj = heap.GetObject(address);

            if (obj.IsNull)
                throw new ArgumentException($"Object at {address:X} is null or invalid.");

            var details = new ObjectDetails
            {
                Address = obj.Address,
                TypeName = obj.Type?.Name ?? "<unknown>",
                Size = obj.Size,
                MethodTable = obj.Type?.MethodTable ?? 0
            };

            if (obj.Type != null)
            {
                foreach (var field in obj.Type.Fields)
                {
                    string fieldName = field.Name ?? $"<field_{field.Offset:X}>";

                    var fieldModel = new ObjectField
                    {
                        Name = fieldName,
                        TypeName = field.Type?.Name ?? "Unknown",
                        Offset = field.Offset,
                        IsReference = field.IsObjectReference
                    };

                    try
                    {
                        if (field.IsObjectReference)
                        {
                            var refObj = obj.ReadObjectField(fieldName);
                            fieldModel.Address = refObj.Address;
                            
                            if (field.ElementType == ClrElementType.String)
                            {
                                fieldModel.Value = !refObj.IsNull ? $"\"{refObj.AsString(100)}\"" : "null";
                            }
                            else
                            {
                                fieldModel.Value = refObj.IsNull ? "null" : $"<<{refObj.Type?.Name}>>";
                            }
                        }
                        else
                        {
                            fieldModel.Value = ReadPrimitiveValue(obj, field)?.ToString() ?? "{error}";
                        }
                    }
                    catch (Exception)
                    {
                        fieldModel.Value = "{error reading}";
                    }

                    details.Fields.Add(fieldModel);
                }
            }

            return details;
        }

        private object? ReadPrimitiveValue(ClrObject obj, ClrInstanceField field)
        {
            string fieldName = field.Name ?? "";
            if (string.IsNullOrEmpty(fieldName)) return null;

            if (field.ElementType == ClrElementType.Boolean) return obj.ReadField<bool>(fieldName);
            if (field.ElementType == ClrElementType.UInt8) return obj.ReadField<byte>(fieldName);
            if (field.ElementType == ClrElementType.Int8) return obj.ReadField<sbyte>(fieldName);
            if (field.ElementType == ClrElementType.Char) return obj.ReadField<char>(fieldName);
            if (field.ElementType == ClrElementType.Int16) return obj.ReadField<short>(fieldName);
            if (field.ElementType == ClrElementType.UInt16) return obj.ReadField<ushort>(fieldName);
            if (field.ElementType == ClrElementType.Int32) return obj.ReadField<int>(fieldName);
            if (field.ElementType == ClrElementType.UInt32) return obj.ReadField<uint>(fieldName);
            if (field.ElementType == ClrElementType.Int64) return obj.ReadField<long>(fieldName);
            if (field.ElementType == ClrElementType.UInt64) return obj.ReadField<ulong>(fieldName);
            if (field.ElementType == ClrElementType.Float) return obj.ReadField<float>(fieldName);
            if (field.ElementType == ClrElementType.Double) return obj.ReadField<double>(fieldName);
            if (field.ElementType == ClrElementType.Pointer || field.ElementType == ClrElementType.NativeInt) return obj.ReadField<IntPtr>(fieldName);
            if (field.ElementType == ClrElementType.NativeUInt) return obj.ReadField<UIntPtr>(fieldName);
            if (field.ElementType == ClrElementType.Struct) return $"<struct {field.Type?.Name}>";
            
            return null;
        }

        public IEnumerable<HeapSegmentInfo> GetHeapSegments()
        {
            var heap = GetHeap();
            return heap.Segments.Select(s => new HeapSegmentInfo
            {
                Start = s.Start,
                End = s.End,
                Size = s.Length,
                Generation = s.Kind switch
                {
                    GCSegmentKind.Generation0 => 0,
                    GCSegmentKind.Generation1 => 1,
                    GCSegmentKind.Generation2 => 2,
                    _ => -1
                },
                IsLargeObjectHeap = s.Kind == GCSegmentKind.Large,
                IsPinnedObjectHeap = s.Kind == GCSegmentKind.Pinned
            });
        }

        public IEnumerable<SyncBlockInfo> GetSyncBlocks(QueryParameters parameters)
        {
            var heap = GetHeap();
            var runtime = _context.Runtime;
            
            var threadMap = runtime?.Threads.ToDictionary(t => t.Address, t => t.ManagedThreadId) ?? new Dictionary<ulong, int>();

            var blocks = heap.EnumerateSyncBlocks().Select(b => new SyncBlockInfo
            {
                ObjectAddress = b.Object,
                IsMonitorHeld = b.IsMonitorHeld,
                HoldingThreadAddress = b.HoldingThreadAddress,
                RecursionCount = b.RecursionCount,
                WaitingThreadCount = b.WaitingThreadCount,
                ManagedThreadId = threadMap.TryGetValue(b.HoldingThreadAddress, out int id) ? id : -1
            });

            if (parameters.SortBy?.ToLower() == "recursion")
            {
                blocks = parameters.SortDirection == SortDirection.Asc ? blocks.OrderBy(b => b.RecursionCount) : blocks.OrderByDescending(b => b.RecursionCount);
            }
            else if (parameters.SortBy?.ToLower() == "waiting")
            {
                blocks = parameters.SortDirection == SortDirection.Asc ? blocks.OrderBy(b => b.WaitingThreadCount) : blocks.OrderByDescending(b => b.WaitingThreadCount);
            }
            else
            {
                blocks = parameters.SortDirection == SortDirection.Asc ? blocks.OrderBy(b => b.ObjectAddress) : blocks.OrderByDescending(b => b.ObjectAddress);
            }

            return blocks.Skip(parameters.Offset).Take(parameters.Limit);
        }
    }
}
