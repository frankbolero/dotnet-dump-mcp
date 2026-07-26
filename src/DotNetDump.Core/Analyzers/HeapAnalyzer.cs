using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers {
	public class HeapAnalyzer {
		private const int MaxStringPreviewLength = 200;
		private const int MaxArrayPreviewSize = 10;
		private readonly IDumpContext _context;
		private IEnumerable<HeapStatItem>? _cachedStats;

		public HeapAnalyzer(IDumpContext context) {
			_context = context;
		}

		private ClrHeap GetHeap() {
			if (!_context.IsLoaded || _context.Heap == null)
				throw new InvalidOperationException("No dump loaded. Please use 'load_dump' tool first.");
			return _context.Heap;
		}

		public IEnumerable<HeapStatItem> GetHeapStatistics(QueryParameters parameters) {
			// Build cache on first call
			if (_cachedStats == null) {
				var heap = GetHeap();
				_cachedStats = (from obj in heap.EnumerateObjects()
									 let type = obj.Type
									 where type != null
									 group obj by new { type.Name, type.MethodTable } into g
									 select new HeapStatItem {
										 TypeName = g.Key.Name,
										 MethodTable = g.Key.MethodTable,
										 Count = g.Count(),
										 TotalSize = g.Sum(p => (long)p.Size)
									 }).ToList(); // Materialize once
			}

			// Sort and page from cache
			var stats = _cachedStats;

			if (parameters.SortBy?.ToLower() == "count") {
				stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.Count) : stats.OrderByDescending(s => s.Count);
			} else if (parameters.SortBy?.ToLower() == "typename") {
				stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.TypeName) : stats.OrderByDescending(s => s.TypeName);
			} else {
				stats = parameters.SortDirection == SortDirection.Asc ? stats.OrderBy(s => s.TotalSize) : stats.OrderByDescending(s => s.TotalSize);
			}

			return stats.Skip(parameters.Offset).Take(parameters.Limit);
		}

		public IEnumerable<HeapObjectItem> GetObjects(QueryParameters parameters, string? typeFilter = null) {
			var heap = GetHeap();
			var objects = heap.EnumerateObjects()
				 .Where(obj => typeFilter == null || (obj.Type?.Name?.Contains(typeFilter, StringComparison.OrdinalIgnoreCase) ?? false))
				 .Select(obj => new HeapObjectItem {
					 Address = obj.Address,
					 MethodTable = obj.Type?.MethodTable ?? 0,
					 Size = obj.Size,
					 TypeName = obj.Type?.Name
				 });

			if (parameters.SortBy?.ToLower() == "size") {
				objects = parameters.SortDirection == SortDirection.Asc ? objects.OrderBy(o => o.Size) : objects.OrderByDescending(o => o.Size);
			} else if (parameters.SortBy?.ToLower() == "address") {
				objects = parameters.SortDirection == SortDirection.Asc ? objects.OrderBy(o => o.Address) : objects.OrderByDescending(o => o.Address);
			}

			return objects.Skip(parameters.Offset).Take(parameters.Limit);
		}

		/// <summary>
		/// Finds retention paths from GC roots to <paramref name="targetAddress"/>.
		/// <para>
		/// Note that <c>ClrHeap.EnumerateRoots()</c> already includes stack roots and static/thread-static
		/// roots, so it is the single source here — enumerating per-thread stack roots as well would
		/// report every stack root twice.
		/// </para>
		/// </summary>
		public IEnumerable<GCRootPathInfo> GetGCRoots(ulong targetAddress, QueryParameters parameters, int maxPaths = 4) {
			var runtime = _context.Runtime;
			if (runtime == null) return Enumerable.Empty<GCRootPathInfo>();

			var heap = GetHeap();

			// Map thread stack-slot addresses back to their owning thread so a stack root can say
			// which thread it belongs to.
			// Slot address 0 shows up for roots the runtime cannot place, so it identifies nothing.
			var stackRootOwners = new Dictionary<ulong, ClrThread>();
			foreach (var thread in runtime.Threads) {
				foreach (var stackRoot in thread.EnumerateStackRoots()) {
					if (stackRoot.Address != 0)
						stackRootOwners[stackRoot.Address] = thread;
				}
			}

			var candidates = heap.EnumerateRoots()
				.Where(r => r.Object.Address != 0)
				.Select(r => {
					stackRootOwners.TryGetValue(r.Address, out var owner);
					return new RootCandidate(
						ObjectAddress: r.Object.Address,
						Kind: r.RootKind.ToString(),
						RootAddress: r.Address,
						ManagedThreadId: owner?.ManagedThreadId,
						OSThreadId: owner?.OSThreadId,
						IsPinned: r.IsPinned,
						IsInterior: r.IsInterior);
				});

			var paths = RootPathFinder.FindPaths(
				targetAddress,
				candidates,
				address => heap.GetObject(address)
					.EnumerateReferenceAddresses(carefully: true, considerDependantHandles: true),
				maxPaths);

			var results = paths.Select(p => new GCRootPathInfo {
				RootAddress = p.Root.RootAddress,
				RootKind = p.Root.Kind,
				ManagedThreadId = p.Root.ManagedThreadId,
				OSThreadId = p.Root.OSThreadId,
				IsPinned = p.Root.IsPinned,
				IsInterior = p.Root.IsInterior,
				TargetAddress = targetAddress,
				Path = p.Path.Select(address => {
					var obj = heap.GetObject(address);
					return new GCRootPathNode {
						Address = address,
						TypeName = obj.Type?.Name,
						Size = obj.IsNull ? 0 : obj.Size
					};
				}).ToList()
			});

			return results.Skip(parameters.Offset).Take(parameters.Limit).ToList();
		}

		public ObjectDetails GetObjectDetails(ulong address) {
			var heap = GetHeap();
			var obj = heap.GetObject(address);
			if (obj.IsNull)
				throw new ArgumentException($"Object at {address:X} is null or invalid.");
			var details = new ObjectDetails {
				Address = obj.Address,
				TypeName = obj.Type?.Name ?? "<unknown>",
				Size = obj.Size,
				MethodTable = obj.Type?.MethodTable ?? 0
			};
			// Handle strings
			if (obj.Type?.Name == "System.String") {
				details.Value = GetObjectValue(obj);
			}
			// Handle collections
			if (obj.Type != null && obj.Type.IsArray) {
				details.Value = $"Array of {obj.Type.ComponentType?.Name}, Count: {obj.AsArray().Length}";
				var array = obj.AsArray();
				var limit = Math.Min(array.Length, MaxArrayPreviewSize);
				for (int i = 0; i < limit; i++) {
					var element = array.GetObjectValue(i);
					details.Fields.Add(new ObjectField {
						Name = $"[{i}]",
						TypeName = element.Type?.Name ?? "<unknown>",
						Value = GetObjectValue(element),
						Address = element.Address,
						IsReference = element.Type?.IsObjectReference ?? false,
						Offset = -1
					});
				}
				if (array.Length > MaxArrayPreviewSize) {
					details.Fields.Add(new ObjectField { Name = $"... ({array.Length - MaxArrayPreviewSize} more items)" });
				}
				return details;
			}
			// Handle regular objects
			if (obj.Type != null) {
				foreach (var field in obj.Type.Fields) {
					string fieldName = field.Name ?? $"<field_{field.Offset:X}>";
					var fieldModel = new ObjectField {
						Name = fieldName,
						TypeName = field.Type?.Name ?? "Unknown",
						Offset = field.Offset,
						IsReference = field.IsObjectReference
					};
					try {
						if (field.IsObjectReference) {
							var refObj = obj.ReadObjectField(field);
							fieldModel.Address = refObj.Address;
							fieldModel.Value = GetObjectValue(refObj);
						} else {
							fieldModel.Value = ReadPrimitiveValue(obj, field)?.ToString() ?? "{error}";
						}
					} catch (Exception) {
						fieldModel.Value = "{error reading}";
					}
					details.Fields.Add(fieldModel);
				}
			}
			return details;
		}

		private string GetObjectValue(ClrObject obj) {
			if (obj.IsNull) return "null";

			// For strings, show truncated value
			if (obj.Type?.Name == "System.String") {
				var s = obj.AsString(MaxStringPreviewLength + 1);
				if (s?.Length > MaxStringPreviewLength) {
					return $"\"{s.Substring(0, MaxStringPreviewLength)}...\" (truncated)";
				}
				return $"\"{s}\"";
			}

			// For other objects, show type
			return $"<{obj.Type?.Name}>";
		}

		/// <summary>
		/// Reads a primitive field value. Uses the field-object overloads rather than the name-based
		/// ones: the name lookup costs a round-trip per field per object, and returns nothing at all
		/// for a field the metadata leaves unnamed.
		/// </summary>
		private object? ReadPrimitiveValue(ClrObject obj, ClrInstanceField field) {
			switch (field.ElementType) {
				case ClrElementType.Boolean: return obj.ReadField<bool>(field);
				case ClrElementType.UInt8: return obj.ReadField<byte>(field);
				case ClrElementType.Int8: return obj.ReadField<sbyte>(field);
				case ClrElementType.Char: return obj.ReadField<char>(field);
				case ClrElementType.Int16: return obj.ReadField<short>(field);
				case ClrElementType.UInt16: return obj.ReadField<ushort>(field);
				case ClrElementType.Int32: return obj.ReadField<int>(field);
				case ClrElementType.UInt32: return obj.ReadField<uint>(field);
				case ClrElementType.Int64: return obj.ReadField<long>(field);
				case ClrElementType.UInt64: return obj.ReadField<ulong>(field);
				case ClrElementType.Float: return obj.ReadField<float>(field);
				case ClrElementType.Double: return obj.ReadField<double>(field);
				case ClrElementType.Pointer:
				case ClrElementType.NativeInt: return obj.ReadField<IntPtr>(field);
				case ClrElementType.NativeUInt: return obj.ReadField<UIntPtr>(field);
				case ClrElementType.Struct: return $"<struct {field.Type?.Name}>";
				default: return null;
			}
		}

		public HeapSummaryInfo GetHeapSegments() {
			var heap = GetHeap();

			return new HeapSummaryInfo {
				IsServerGC = heap.IsServer,
				SubHeapCount = heap.SubHeaps.Length,
				CanWalkHeap = heap.CanWalkHeap,
				DynamicAdaptationMode = heap.DynamicAdaptationMode,
				Segments = heap.Segments.Select(s => new HeapSegmentInfo {
					Start = s.Start,
					End = s.End,
					Size = s.Length,
					Kind = SegmentClassifier.Label(s.Kind),
					Generation = SegmentClassifier.Generation(s.Kind),
					IsLargeObjectHeap = SegmentClassifier.IsLargeObjectHeap(s.Kind),
					IsPinnedObjectHeap = SegmentClassifier.IsPinnedObjectHeap(s.Kind),
					CommittedSize = s.CommittedMemory.Length,
					ReservedSize = s.ReservedMemory.Length,
					Gen0Size = s.Generation0.Length,
					Gen1Size = s.Generation1.Length,
					Gen2Size = s.Generation2.Length,
					SubHeapIndex = s.SubHeap.Index
				}).ToList()
			};
		}

		/// <summary>
		/// Lists monitors. An uncontended <c>lock</c> is stored as a thin lock in the object header and
		/// never allocates a sync block, so sync-block enumeration alone reports an empty table for a
		/// process that plainly holds a lock. <paramref name="includeThinLocks"/> walks the heap to find
		/// those — it costs a full heap pass, hence opt-in.
		/// </summary>
		public IEnumerable<SyncBlockInfo> GetSyncBlocks(QueryParameters parameters, bool includeThinLocks = true) {
			var heap = GetHeap();
			var runtime = _context.Runtime;

			var threadMap = runtime?.Threads.ToDictionary(t => t.Address, t => t) ?? new Dictionary<ulong, ClrThread>();

			var blocks = heap.EnumerateSyncBlocks().Select(b => {
				threadMap.TryGetValue(b.HoldingThreadAddress, out var holder);
				return new SyncBlockInfo {
					ObjectAddress = b.Object,
					TypeName = heap.GetObject(b.Object).Type?.Name,
					IsMonitorHeld = b.IsMonitorHeld,
					HoldingThreadAddress = b.HoldingThreadAddress,
					RecursionCount = b.RecursionCount,
					WaitingThreadCount = b.WaitingThreadCount,
					ManagedThreadId = holder?.ManagedThreadId,
					OSThreadId = holder?.OSThreadId,
					IsThinLock = false
				};
			});

			if (includeThinLocks) {
				var thinLocks = EnumerateThinLocks(heap);
				blocks = blocks.Concat(thinLocks);
			}

			if (parameters.SortBy?.ToLower() == "recursion") {
				blocks = parameters.SortDirection == SortDirection.Asc ? blocks.OrderBy(b => b.RecursionCount) : blocks.OrderByDescending(b => b.RecursionCount);
			} else if (parameters.SortBy?.ToLower() == "waiting") {
				blocks = parameters.SortDirection == SortDirection.Asc ? blocks.OrderBy(b => b.WaitingThreadCount) : blocks.OrderByDescending(b => b.WaitingThreadCount);
			} else {
				blocks = parameters.SortDirection == SortDirection.Asc ? blocks.OrderBy(b => b.ObjectAddress) : blocks.OrderByDescending(b => b.ObjectAddress);
			}

			return blocks.Skip(parameters.Offset).Take(parameters.Limit).ToList();
		}

		private static IEnumerable<SyncBlockInfo> EnumerateThinLocks(ClrHeap heap) {
			foreach (var obj in heap.EnumerateObjects()) {
				ClrThinLock? thinLock;
				try {
					thinLock = obj.GetThinLock();
				} catch (Exception) {
					continue;
				}

				if (thinLock == null)
					continue;

				yield return new SyncBlockInfo {
					ObjectAddress = obj.Address,
					TypeName = obj.Type?.Name,
					IsMonitorHeld = true,
					HoldingThreadAddress = thinLock.Thread?.Address ?? 0,
					RecursionCount = thinLock.Recursion,
					WaitingThreadCount = 0,
					ManagedThreadId = thinLock.Thread?.ManagedThreadId,
					OSThreadId = thinLock.Thread?.OSThreadId,
					IsThinLock = true
				};
			}
		}

		public IEnumerable<GCHandleInfo> GetGCHandles(QueryParameters parameters) {
			var runtime = _context.Runtime;
			if (runtime == null) return Enumerable.Empty<GCHandleInfo>();

			var handles = runtime.EnumerateHandles().Select(h => new GCHandleInfo {
				Address = h.Address,
				Object = h.Object.Address,
				Kind = h.HandleKind.ToString(),
				TypeName = h.Object.Type?.Name ?? "<unknown>",
				IsStrong = h.IsStrong,
				ReferenceCount = h.ReferenceCount,
				DependentTarget = h.Dependent.Address,
				AppDomainName = h.AppDomain?.Name,
				Size = h.Object.IsNull ? 0 : h.Object.Size
			});

			if (parameters.SortBy?.ToLower() == "kind") {
				handles = parameters.SortDirection == SortDirection.Asc ? handles.OrderBy(h => h.Kind) : handles.OrderByDescending(h => h.Kind);
			} else if (parameters.SortBy?.ToLower() == "typename") {
				handles = parameters.SortDirection == SortDirection.Asc ? handles.OrderBy(h => h.TypeName) : handles.OrderByDescending(h => h.TypeName);
			} else {
				handles = parameters.SortDirection == SortDirection.Asc ? handles.OrderBy(h => h.Address) : handles.OrderByDescending(h => h.Address);
			}

			return handles.Skip(parameters.Offset).Take(parameters.Limit);
		}

		public IEnumerable<HeapCorruptionInfo> VerifyHeap() {
			var heap = GetHeap();
			return heap.VerifyHeap().Select(c => new HeapCorruptionInfo {
				Address = c.Object.Address + (ulong)(c.Offset > 0 ? c.Offset : 0),
				Object = c.Object.Address,
				Kind = c.Kind.ToString(),
				Message = c.ToString(),
				Offset = c.Offset,
				TypeName = c.Object.Type?.Name
			});
		}

		/// <summary>Verifies a single object, for following up on a suspect address.</summary>
		public IEnumerable<HeapCorruptionInfo> VerifyObject(ulong address) {
			var heap = GetHeap();

			if (!heap.FullyVerifyObject(address, out var corruptions))
				return Enumerable.Empty<HeapCorruptionInfo>();

			return corruptions.Select(c => new HeapCorruptionInfo {
				Address = c.Object.Address + (ulong)(c.Offset > 0 ? c.Offset : 0),
				Object = c.Object.Address,
				Kind = c.Kind.ToString(),
				Message = c.ToString(),
				Offset = c.Offset,
				TypeName = c.Object.Type?.Name
			}).ToList();
		}

		/// <summary>
		/// Exception objects living on the heap. In a collected dump most exceptions have already been
		/// caught, so they are not in flight on any thread and are only findable this way.
		/// </summary>
		public IEnumerable<ExceptionDetails> GetHeapExceptions(QueryParameters parameters) {
			var heap = GetHeap();

			var found = heap.EnumerateObjects()
				.Where(o => o.Type?.IsException == true)
				.Select(o => o.AsException())
				.Where(e => e != null)
				.Select(e => ExceptionMapper.Map(e!))
				.Skip(parameters.Offset)
				.Take(parameters.Limit);

			return found.ToList();
		}
	}
}