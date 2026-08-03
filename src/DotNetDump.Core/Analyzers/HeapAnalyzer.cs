using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Caching;
using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers {
	public class HeapAnalyzer {
		private const int MaxStringPreviewLength = 200;
		private const int MaxArrayPreviewSize = 10;

		/// <summary>
		/// Bumped whenever one of the cached models below changes shape, so a stale on-disk entry
		/// from an older build cannot be deserialized and served as if it matched the current
		/// contract (CLI_DESIGN.md §6.1). Shared with <see cref="ThreadAnalyzer"/> since both
		/// perform the identical full-heap exception scan and must use the same schema version
		/// to avoid split cache entries.
		/// </summary>
		internal const int CacheSchemaVersion = 2;

		/// <summary>
		/// Shared with <see cref="ThreadAnalyzer"/> — both classes perform the identical full-heap
		/// exception scan, so using the same operation name lets either one populate the cache entry
		/// the other reuses.
		/// </summary>
		internal const string HeapExceptionsCacheOperation = "heap-exceptions";

		/// <summary>
		/// Overrides <see cref="RootPathFinder.DefaultMaxNodesVisited"/> when <c>gcroot</c> is not
		/// given an explicit <c>maxNodesVisited</c>. Set to <c>"0"</c> for unlimited (see the memory
		/// caveat on <see cref="GetGCRoots"/>).
		/// </summary>
		public const string GCRootMaxNodesVariable = "DNDUMP_GCROOT_MAX_NODES";

		private readonly IDumpContext _context;
		private readonly IAnalysisCache _cache;

		public HeapAnalyzer(IDumpContext context, IAnalysisCache? cache = null) {
			_context = context;
			_cache = cache ?? NullAnalysisCache.Instance;
		}

		private ClrHeap GetHeap() {
			if (!_context.IsLoaded || _context.Heap == null)
				throw new InvalidOperationException("No dump loaded. Please use 'load_dump' tool first.");
			return _context.Heap;
		}

		/// <summary>
		/// The walk: groups every heap object by type. Cached under a single, argument-independent
		/// key — one entry serves every <c>limit</c>/<c>offset</c>/<c>sort</c>/<c>order</c> variant
		/// of <c>dumpheap</c> (CLI_DESIGN.md §6.2).
		/// </summary>
		/// <param name="progress">
		/// Optional walk-progress sink (DATA_CONTRACT.md §5). Reported at a throttled interval via
		/// <see cref="WalkProgressThrottle"/>, never per object. Only consulted on a cache miss --
		/// this method runs only when there is an actual walk to report progress on.
		/// </param>
		private List<HeapStatItem> ComputeHeapStatistics(IProgress<WalkProgress>? progress) {
			var heap = GetHeap();
			var throttle = WalkProgressThrottle.ForHeap(heap, progress);

			// A foreach + Dictionary, not a LINQ GroupBy, so progress can be recorded for every
			// object visited -- including the ones the type != null check below discards -- without
			// an awkward `let` clause threading a side effect through the query. The result (grouped
			// by type name + method table, always re-sorted by the caller) is identical either way.
			var groups = new Dictionary<(string? Name, ulong MethodTable), (int Count, long TotalSize)>();
			foreach (var obj in heap.EnumerateObjects()) {
				throttle.Record((long)obj.Size);

				var type = obj.Type;
				if (type == null)
					continue;

				var key = (type.Name, type.MethodTable);
				groups.TryGetValue(key, out var current);
				groups[key] = (current.Count + 1, current.TotalSize + (long)obj.Size);
			}

			var result = new List<HeapStatItem>(groups.Count);
			foreach (var (groupKey, value) in groups) {
				result.Add(new HeapStatItem {
					TypeName = groupKey.Name,
					MethodTable = groupKey.MethodTable,
					Count = value.Count,
					TotalSize = value.TotalSize
				});
			}

			throttle.ReportFinal();
			return result;
		}

		public PagedResult<HeapStatItem> GetHeapStatistics(QueryParameters parameters, IProgress<WalkProgress>? progress = null) {
			// EnsureSupported runs before the cache lookup, so an unsupported filter is rejected
			// identically on a cold and a warm cache (DATA_CONTRACT.md §2.1). TypeNameMatcher.Create
			// follows immediately: a malformed regex is also a free rejection, not a cost paid only
			// after the (possibly first-ever) walk below.
			parameters.Filter.EnsureSupported("dumpheap", HeapStatItemFilter.Honored);
			var typeNameMatcher = TypeNameMatcher.Create(parameters.Filter);

			var key = new CacheKey(_context.Identity, "heap-statistics", "", CacheSchemaVersion);
			List<HeapStatItem> stats = _cache.GetOrCompute(key, () => ComputeHeapStatistics(progress));

			List<HeapStatItem> filtered = stats.Where(s => HeapStatItemFilter.Matches(s, parameters.Filter, typeNameMatcher)).ToList();

			IEnumerable<HeapStatItem> sorted = filtered;
			if (parameters.SortBy?.ToLower() == "count") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(s => s.Count) : sorted.OrderByDescending(s => s.Count);
			} else if (parameters.SortBy?.ToLower() == "typename") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(s => s.TypeName) : sorted.OrderByDescending(s => s.TypeName);
			} else {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(s => s.TotalSize) : sorted.OrderByDescending(s => s.TotalSize);
			}

			var page = sorted.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<HeapStatItem>(page, filtered.Count, stats.Count, parameters.Offset, parameters.Limit);
		}

		/// <summary>
		/// The walk, filtered by <paramref name="typeFilter"/>. The filter changes what is computed
		/// (unlike pagination/sort), so it is part of the cache key — a distinct filter gets its own
		/// entry, computed by its own heap walk.
		/// </summary>
		/// <param name="progress">
		/// Optional walk-progress sink (DATA_CONTRACT.md §5), reported at a throttled interval. Every
		/// object <c>EnumerateObjects()</c> visits counts toward progress, including ones
		/// <paramref name="typeFilter"/> excludes from the result -- the walk touches them regardless.
		/// </param>
		private List<HeapObjectItem> ComputeObjects(string? typeFilter, IProgress<WalkProgress>? progress) {
			var heap = GetHeap();
			var throttle = WalkProgressThrottle.ForHeap(heap, progress);

			var result = heap.EnumerateObjects()
				 .Select(obj => {
					 throttle.Record((long)obj.Size);
					 return obj;
				 })
				 .Where(obj => typeFilter == null || (obj.Type?.Name?.Contains(typeFilter, StringComparison.OrdinalIgnoreCase) ?? false))
				 .Select(obj => new HeapObjectItem {
					 Address = obj.Address,
					 MethodTable = obj.Type?.MethodTable ?? 0,
					 Size = obj.Size,
					 TypeName = obj.Type?.Name,
					 // GetSegmentByAddress keeps a last-segment fast path, and EnumerateObjects walks
					 // segment-by-segment, so this stays effectively O(1) per object rather than
					 // re-searching the segment list from scratch on every row.
					 Generation = GenerationClassifier.ToFilter(heap.GetSegmentByAddress(obj.Address)?.GetGeneration(obj.Address) ?? Generation.Unknown)
				 }).ToList();

			throttle.ReportFinal();
			return result;
		}

		public PagedResult<HeapObjectItem> GetObjects(QueryParameters parameters, string? typeFilter = null, IProgress<WalkProgress>? progress = null) {
			// typeFilter is the --type *scope* (DATA_CONTRACT.md §2.4): it narrows the walk itself and
			// is part of the cache key below. parameters.Filter is the post-walk *filter* and is
			// deliberately excluded from the cache key -- the two compose, scope at walk time and
			// filter after, per §2.1.
			parameters.Filter.EnsureSupported("listobj", HeapObjectItemFilter.Honored);
			var typeNameMatcher = TypeNameMatcher.Create(parameters.Filter);

			string argumentsHash = CacheKey.HashArguments(typeFilter?.ToLowerInvariant());
			var key = new CacheKey(_context.Identity, "heap-objects", argumentsHash, CacheSchemaVersion);
			List<HeapObjectItem> objects = _cache.GetOrCompute(key, () => ComputeObjects(typeFilter, progress));

			List<HeapObjectItem> filtered = objects.Where(o => HeapObjectItemFilter.Matches(o, parameters.Filter, typeNameMatcher)).ToList();

			IEnumerable<HeapObjectItem> sorted = filtered;
			if (parameters.SortBy?.ToLower() == "size") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(o => o.Size) : sorted.OrderByDescending(o => o.Size);
			} else if (parameters.SortBy?.ToLower() == "address") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(o => o.Address) : sorted.OrderByDescending(o => o.Address);
			}

			var page = sorted.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<HeapObjectItem>(page, filtered.Count, objects.Count, parameters.Offset, parameters.Limit);
		}

		/// <summary>
		/// Finds retention paths from GC roots to <paramref name="targetAddress"/>.
		/// <para>
		/// Note that <c>ClrHeap.EnumerateRoots()</c> already includes stack roots and static/thread-static
		/// roots, so it is the single source here — enumerating per-thread stack roots as well would
		/// report every stack root twice.
		/// </para>
		/// <para>
		/// <paramref name="maxNodesVisited"/> bounds each BFS pass (see the per-pass-vs-total discussion
		/// on <see cref="RootPathFinder.FindPaths"/>). When omitted, the budget comes from the
		/// <see cref="GCRootMaxNodesVariable"/> environment variable, else
		/// <see cref="RootPathFinder.DefaultMaxNodesVisited"/>. <c>0</c> means unlimited, which is
		/// conclusive but not free: peak memory scales with nodes visited, roughly 40 bytes/node
		/// (~4 GB at 100M). Check <see cref="GCRootSearchInfo.Truncated"/> before treating an empty
		/// result as proof the object is unrooted.
		/// </para>
		/// </summary>
		public GCRootSearchInfo GetGCRoots(ulong targetAddress, QueryParameters parameters, int maxPaths = 4, int? maxNodesVisited = null) {
			// Not in the DATA_CONTRACT.md §2.3 matrix's named rows, but explicitly called out as an
			// "everything else" method: gcroot honors no filter.
			parameters.Filter.EnsureSupported("gcroot", FilterField.None);

			var runtime = _context.Runtime;
			if (runtime == null) return new GCRootSearchInfo { TargetAddress = targetAddress };

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

			int effectiveMaxNodes = ResolveMaxNodesVisited(maxNodesVisited);

			var search = RootPathFinder.FindPaths(
				targetAddress,
				candidates,
				address => heap.GetObject(address)
					.EnumerateReferenceAddresses(carefully: true, considerDependantHandles: true),
				maxPaths,
				effectiveMaxNodes);

			var pathInfos = search.Paths.Select(p => new GCRootPathInfo {
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

			return new GCRootSearchInfo {
				TargetAddress = targetAddress,
				Paths = pathInfos.Skip(parameters.Offset).Take(parameters.Limit).ToList(),
				NodesVisited = search.NodesVisited,
				Truncated = search.Truncated,
			};
		}

		/// <summary>Resolves the <c>gcroot</c> traversal budget: an explicit argument wins, then
		/// <see cref="GCRootMaxNodesVariable"/>, then <see cref="RootPathFinder.DefaultMaxNodesVisited"/>.
		/// A malformed environment variable is ignored rather than thrown, consistent with this class
		/// not validating its other environment inputs strictly.</summary>
		private static int ResolveMaxNodesVisited(int? requested) {
			if (requested.HasValue)
				return requested.Value;

			string? env = Environment.GetEnvironmentVariable(GCRootMaxNodesVariable);
			if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out int parsed))
				return parsed;

			return RootPathFinder.DefaultMaxNodesVisited;
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
				// GetObjectValue is only valid for arrays whose elements are object references
				// (e.g. string[], object[]); it throws InvalidOperationException on primitive
				// element arrays like byte[]/int[]/double[], which must be read via GetValue<T>
				// instead (mirrors ReadPrimitiveValue's field-based counterpart below).
				bool isObjectArray = obj.Type.ComponentType?.IsObjectReference ?? false;
				for (int i = 0; i < limit; i++) {
					if (isObjectArray) {
						var element = array.GetObjectValue(i);
						details.Fields.Add(new ObjectField {
							Name = $"[{i}]",
							TypeName = element.Type?.Name ?? "<unknown>",
							Value = GetObjectValue(element),
							Address = element.Address,
							IsReference = element.Type?.IsObjectReference ?? false,
							Offset = -1
						});
					} else {
						details.Fields.Add(new ObjectField {
							Name = $"[{i}]",
							TypeName = obj.Type.ComponentType?.Name ?? "<unknown>",
							Value = ReadPrimitiveArrayValue(array, i)?.ToString() ?? "{error}",
							Address = 0,
							IsReference = false,
							Offset = -1
						});
					}
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

		// Reads a primitive element out of an array whose component type is not an object
		// reference (e.g. byte[], int[]). ClrArray.GetObjectValue throws for these arrays;
		// ClrArray.GetValue is the correct reader (mirrors ReadPrimitiveValue above, for fields).
		private object? ReadPrimitiveArrayValue(ClrArray array, int index) {
			switch (array.Type.ComponentType?.ElementType) {
				case ClrElementType.Boolean: return array.GetValue<bool>(index);
				case ClrElementType.UInt8: return array.GetValue<byte>(index);
				case ClrElementType.Int8: return array.GetValue<sbyte>(index);
				case ClrElementType.Char: return array.GetValue<char>(index);
				case ClrElementType.Int16: return array.GetValue<short>(index);
				case ClrElementType.UInt16: return array.GetValue<ushort>(index);
				case ClrElementType.Int32: return array.GetValue<int>(index);
				case ClrElementType.UInt32: return array.GetValue<uint>(index);
				case ClrElementType.Int64: return array.GetValue<long>(index);
				case ClrElementType.UInt64: return array.GetValue<ulong>(index);
				case ClrElementType.Float: return array.GetValue<float>(index);
				case ClrElementType.Double: return array.GetValue<double>(index);
				case ClrElementType.Pointer:
				case ClrElementType.NativeInt: return array.GetValue<IntPtr>(index);
				case ClrElementType.NativeUInt: return array.GetValue<UIntPtr>(index);
				case ClrElementType.Struct: return $"<struct {array.Type.ComponentType?.Name}>";
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
		/// <para>
		/// Sync blocks are cheap to enumerate; thin locks are not, so <paramref name="includeThinLocks"/>
		/// changes what is computed and is part of the cache key.
		/// </para>
		/// </summary>
		/// <param name="progress">
		/// Optional walk-progress sink (DATA_CONTRACT.md §5), forwarded to <see cref="EnumerateThinLocks"/>
		/// -- the only part of this method that walks the full heap. When <paramref name="includeThinLocks"/>
		/// is <c>false</c> there is no walk, so <paramref name="progress"/> goes unused and nothing is
		/// reported, which is correct: there is nothing to report progress on.
		/// </param>
		private List<SyncBlockInfo> ComputeSyncBlocks(bool includeThinLocks, IProgress<WalkProgress>? progress) {
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
			}).ToList();

			if (includeThinLocks) {
				blocks.AddRange(EnumerateThinLocks(heap, progress));
			}

			return blocks;
		}

		public PagedResult<SyncBlockInfo> GetSyncBlocks(QueryParameters parameters, bool includeThinLocks = true, IProgress<WalkProgress>? progress = null) {
			parameters.Filter.EnsureSupported("syncblk", SyncBlockInfoFilter.Honored);

			string argumentsHash = CacheKey.HashArguments(includeThinLocks);
			var key = new CacheKey(_context.Identity, "sync-blocks", argumentsHash, CacheSchemaVersion);
			List<SyncBlockInfo> blocks = _cache.GetOrCompute(key, () => ComputeSyncBlocks(includeThinLocks, progress));

			List<SyncBlockInfo> filtered = blocks.Where(b => SyncBlockInfoFilter.Matches(b, parameters.Filter)).ToList();

			IEnumerable<SyncBlockInfo> sorted = filtered;
			if (parameters.SortBy?.ToLower() == "recursion") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(b => b.RecursionCount) : sorted.OrderByDescending(b => b.RecursionCount);
			} else if (parameters.SortBy?.ToLower() == "waiting") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(b => b.WaitingThreadCount) : sorted.OrderByDescending(b => b.WaitingThreadCount);
			} else {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(b => b.ObjectAddress) : sorted.OrderByDescending(b => b.ObjectAddress);
			}

			var page = sorted.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<SyncBlockInfo>(page, filtered.Count, blocks.Count, parameters.Offset, parameters.Limit);
		}

		/// <summary>The walk. <paramref name="progress"/> is reported at a throttled interval, never per object.</summary>
		private static IEnumerable<SyncBlockInfo> EnumerateThinLocks(ClrHeap heap, IProgress<WalkProgress>? progress) {
			var throttle = WalkProgressThrottle.ForHeap(heap, progress);

			foreach (var obj in heap.EnumerateObjects()) {
				throttle.Record((long)obj.Size);

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

			throttle.ReportFinal();
		}

		public PagedResult<GCHandleInfo> GetGCHandles(QueryParameters parameters) {
			parameters.Filter.EnsureSupported("gchandles", GCHandleInfoFilter.Honored);
			var typeNameMatcher = TypeNameMatcher.Create(parameters.Filter);

			var runtime = _context.Runtime;
			if (runtime == null) return PagedResult<GCHandleInfo>.Empty(parameters);

			var allHandles = runtime.EnumerateHandles().Select(h => new GCHandleInfo {
				Address = h.Address,
				Object = h.Object.Address,
				Kind = h.HandleKind.ToString(),
				TypeName = h.Object.Type?.Name ?? "<unknown>",
				IsStrong = h.IsStrong,
				ReferenceCount = h.ReferenceCount,
				DependentTarget = h.Dependent.Address,
				AppDomainName = h.AppDomain?.Name,
				Size = h.Object.IsNull ? 0 : h.Object.Size
			}).ToList();

			List<GCHandleInfo> filtered = allHandles.Where(h => GCHandleInfoFilter.Matches(h, parameters.Filter, typeNameMatcher)).ToList();

			IEnumerable<GCHandleInfo> sorted = filtered;
			if (parameters.SortBy?.ToLower() == "kind") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(h => h.Kind) : sorted.OrderByDescending(h => h.Kind);
			} else if (parameters.SortBy?.ToLower() == "typename") {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(h => h.TypeName) : sorted.OrderByDescending(h => h.TypeName);
			} else {
				sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(h => h.Address) : sorted.OrderByDescending(h => h.Address);
			}

			var page = sorted.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<GCHandleInfo>(page, filtered.Count, allHandles.Count, parameters.Offset, parameters.Limit);
		}

		/// <summary>
		/// Verifies the whole heap. The verification walk has to run to completion before the corruption
		/// count is known, so unlike the paginated walks there is no cheap way to report a total without
		/// doing all the work.
		/// </summary>
		public PagedResult<HeapCorruptionInfo> VerifyHeap(QueryParameters parameters) {
			parameters.Filter.EnsureSupported("verifyheap", FilterField.None);

			var heap = GetHeap();
			var corruptions = heap.VerifyHeap().Select(c => new HeapCorruptionInfo {
				Address = c.Object.Address + (ulong)(c.Offset > 0 ? c.Offset : 0),
				Object = c.Object.Address,
				Kind = c.Kind.ToString(),
				Message = c.ToString(),
				Offset = c.Offset,
				TypeName = c.Object.Type?.Name
			}).ToList();

			var page = corruptions.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<HeapCorruptionInfo>(page, corruptions.Count, corruptions.Count, parameters.Offset, parameters.Limit);
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
		/// <remarks>
		/// Shares its cache entry with <see cref="ThreadAnalyzer"/>'s heap-exception scan (see
		/// <see cref="HeapExceptionsCacheOperation"/>) — both perform the identical full-heap walk.
		/// Whichever one runs first on a cache miss is the one whose <paramref name="progress"/> the
		/// caller sees; the other's cached read reports none, per the cache-hit rule in
		/// DATA_CONTRACT.md §5.
		/// </remarks>
		private List<ExceptionDetails> ComputeHeapExceptions(IProgress<WalkProgress>? progress) {
			var heap = GetHeap();
			var throttle = WalkProgressThrottle.ForHeap(heap, progress);

			var result = heap.EnumerateObjects()
				.Select(o => {
					throttle.Record((long)o.Size);
					return o;
				})
				.Where(o => o.Type?.IsException == true)
				.Select(o => o.AsException())
				.Where(e => e != null)
				.Select(e => ExceptionMapper.Map(e!))
				.ToList();

			throttle.ReportFinal();
			return result;
		}

		public PagedResult<ExceptionDetails> GetHeapExceptions(QueryParameters parameters, IProgress<WalkProgress>? progress = null) {
			// The heap-scan path: bare ExceptionDetails with no owning thread, so ManagedThreadId is
			// unsupported here even though ThreadAnalyzer.GetThreadExceptions honors it for the
			// in-flight path (DATA_CONTRACT.md §2.3, "printexception is two methods").
			parameters.Filter.EnsureSupported("printexception (heap scan)", ExceptionDetailsFilter.Honored);
			var typeNameMatcher = TypeNameMatcher.Create(parameters.Filter);

			var key = new CacheKey(_context.Identity, HeapExceptionsCacheOperation, "", CacheSchemaVersion);
			List<ExceptionDetails> found = _cache.GetOrCompute(key, () => ComputeHeapExceptions(progress));

			List<ExceptionDetails> filtered = found.Where(e => ExceptionDetailsFilter.Matches(e, parameters.Filter, typeNameMatcher)).ToList();

			var page = filtered.Skip(parameters.Offset).Take(parameters.Limit).ToList();
			return new PagedResult<ExceptionDetails>(page, filtered.Count, found.Count, parameters.Offset, parameters.Limit);
		}
	}
}