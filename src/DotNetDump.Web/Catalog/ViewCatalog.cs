using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

// Deliberately not under Views/: that path is the MVC Razor convention folder and holds .cshtml
// templates only (SERVER.md §5.3). This is the inventory those templates are selected by.
namespace DotNetDump.Web.Catalog;

/// <summary>Navigation grouping from DATA_CONTRACT.md &#0167;3.1.</summary>
public enum ViewGroup {
	Overview,
	Heap,
	Threads,
	Exceptions,
	Modules,
	Metadata
}

/// <summary>
/// Whether a view is a filterable, sortable, paged table or a single rendered record. Only
/// <see cref="List"/> views get a filter bar, sortable headers and an infinite-scroll sentinel.
/// </summary>
public enum ViewKind {
	List,
	Detail
}

/// <param name="Name">URL segment: <c>/views/{Name}</c>. Identical to the CLI command name.</param>
/// <param name="Title">Human-readable name for navigation and the page heading.</param>
/// <param name="Group">Which navigation group it belongs to.</param>
/// <param name="Kind">Whether it is a table or a record.</param>
/// <param name="HonoredFilters">
/// The <see cref="FilterField"/> set the backing analyzer method applies. Only these get a control
/// in the filter bar, and anything outside them is a 400 on the JSON route.
/// </param>
public sealed record ViewDescriptor(
	string Name,
	string Title,
	ViewGroup Group,
	ViewKind Kind,
	FilterField HonoredFilters);

/// <summary>
/// The view inventory: one view per CLI command (DATA_CONTRACT.md &#0167;3.1), each carrying the filter
/// set its analyzer method honors.
/// </summary>
/// <remarks>
/// <para>
/// Every honored set is written as a reference to the <c>Honored</c> constant on the corresponding
/// <c>DotNetDump.Core.Filtering</c> predicate — never as a re-listed set of flags. The matrix in
/// DATA_CONTRACT.md &#0167;2.3 is keyed by analyzer method precisely so two consumers cannot disagree
/// about it, and a second hand-maintained copy here would be free to drift from the one the
/// analyzer actually enforces. Referencing the constant makes drift impossible: if a honored set
/// changes in Core, this changes with it.
/// </para>
/// <para>
/// <c>clrstack</c> and <c>eestack</c> are <see cref="ViewKind.Detail"/> and honor nothing. They are
/// backed by <c>ThreadAnalyzer.GetStackTraceGroups</c>, which takes no <c>QueryParameters</c> at
/// all, so there is nowhere to pass a <see cref="FilterSpec"/> — see the first correction under
/// DATA_CONTRACT.md &#0167;2.3. Giving them a filter bar would require a Core change nobody has asked
/// for; this table is where that pressure has to be refused.
/// </para>
/// </remarks>
public static class ViewCatalog {
	private static readonly ViewDescriptor[] Descriptors = [
		new("info", "Overview", ViewGroup.Overview, ViewKind.Detail, FilterField.None),

		new("dumpheap", "Heap statistics", ViewGroup.Heap, ViewKind.List, HeapStatItemFilter.Honored),
		new("listobj", "Objects", ViewGroup.Heap, ViewKind.List, HeapObjectItemFilter.Honored),
		new("gchandles", "GC handles", ViewGroup.Heap, ViewKind.List, GCHandleInfoFilter.Honored),
		new("eeheap", "Heap segments", ViewGroup.Heap, ViewKind.Detail, FilterField.None),
		new("verifyheap", "Heap verification", ViewGroup.Heap, ViewKind.Detail, FilterField.None),
		new("dumpobj", "Object", ViewGroup.Heap, ViewKind.Detail, FilterField.None),
		new("gcroot", "Retention paths", ViewGroup.Heap, ViewKind.Detail, FilterField.None),

		new("clrthreads", "Threads", ViewGroup.Threads, ViewKind.List, ThreadInfoFilter.Honored),
		new("threadstate", "Thread states", ViewGroup.Threads, ViewKind.List, ThreadStateInfoFilter.Honored),
		new("syncblk", "Sync blocks", ViewGroup.Threads, ViewKind.List, SyncBlockInfoFilter.Honored),
		new("dumpstack", "Stacks", ViewGroup.Threads, ViewKind.Detail, ThreadStackInfoFilter.Honored),
		new("clrstack", "Managed stacks", ViewGroup.Threads, ViewKind.Detail, FilterField.None),
		new("eestack", "All stacks", ViewGroup.Threads, ViewKind.Detail, FilterField.None),
		new("threadpool", "Thread pool", ViewGroup.Threads, ViewKind.Detail, FilterField.None),

		// The collection mode calls ThreadAnalyzer.GetThreadExceptions -- the in-flight-plus-heap
		// path, matching PrintExceptionCommand -- not GetHeapExceptions directly, so the owning
		// thread is populated and ManagedThreadId/OSThreadId are honored.
		new("printexception", "Exceptions", ViewGroup.Exceptions, ViewKind.List, ThreadExceptionInfoFilter.Honored),

		new("clrmodules", "Modules", ViewGroup.Modules, ViewKind.List, ModuleInfoFilter.Honored),
		new("dumpmodule", "Module", ViewGroup.Modules, ViewKind.Detail, FilterField.None),
		new("dumpassembly", "Assembly", ViewGroup.Modules, ViewKind.Detail, FilterField.None),

		new("dumpmt", "MethodTable", ViewGroup.Metadata, ViewKind.Detail, FilterField.None),
		new("dumpmd", "MethodDesc", ViewGroup.Metadata, ViewKind.Detail, FilterField.None),
		new("dumpclass", "EEClass", ViewGroup.Metadata, ViewKind.Detail, FilterField.None),
		new("name2ee", "Name to EE", ViewGroup.Metadata, ViewKind.Detail, FilterField.None),
		new("ip2md", "IP to MethodDesc", ViewGroup.Metadata, ViewKind.Detail, FilterField.None),
	];

	private static readonly Dictionary<string, ViewDescriptor> ByName =
		Descriptors.ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal);

	public static IReadOnlyList<ViewDescriptor> All => Descriptors;

	/// <summary>
	/// The descriptor for <paramref name="name"/>, or <c>null</c> if no such view exists — which the
	/// route turns into a 404 rather than guessing at a near match.
	/// </summary>
	public static ViewDescriptor? Find(string? name) =>
		name is not null && ByName.TryGetValue(name, out var descriptor) ? descriptor : null;
}