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
/// <param name="Command">
/// The literal SOS invocation shown in the view header, e.g. <c>dumpheap -stat</c>. Equal to
/// <see cref="Name"/> for most views; differs where the view splits one SOS command into modes
/// (<c>dumpheap -stat</c> vs. the bare per-object <c>dumpheap</c> that backs <c>listobj</c>) or
/// renames it (<c>pe</c> for <c>printexception</c>).
/// </param>
/// <param name="Description">
/// One-line, present-tense statement of what the command does, sourced from <c>docs/CLI_DESIGN.md</c>
/// &#0167;4 and <c>docs/commands/*.md</c> rather than invented — the view header renders it verbatim
/// next to <see cref="Command"/>, so a description that overstates or misdescribes the analyzer would
/// be a documentation bug shown to every user of the view.
/// </param>
public sealed record ViewDescriptor(
	string Name,
	string Title,
	ViewGroup Group,
	ViewKind Kind,
	FilterField HonoredFilters,
	string Command,
	string Description);

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
		new("info", "Overview", ViewGroup.Overview, ViewKind.Detail, FilterField.None,
			"info", "runtime version, architecture, OS, DAC status, heap size and thread count."),

		new("dumpheap", "Heap statistics", ViewGroup.Heap, ViewKind.List, HeapStatItemFilter.Honored,
			"dumpheap -stat", "object types on the managed heap, sorted by total size."),
		new("listobj", "Objects", ViewGroup.Heap, ViewKind.List, HeapObjectItemFilter.Honored,
			"dumpheap", "every live object on the managed heap, one row per instance."),
		new("gchandles", "GC handles", ViewGroup.Heap, ViewKind.List, GCHandleInfoFilter.Honored,
			"gchandles", "GC handles and what each one points to, including which are pinned."),
		new("eeheap", "Heap segments", ViewGroup.Heap, ViewKind.Detail, FilterField.None,
			"eeheap -gc", "generation sizes and segment layout of the managed heap."),
		new("verifyheap", "Heap verification", ViewGroup.Heap, ViewKind.Detail, FilterField.None,
			"verifyheap", "walks the heap checking for corruption and reports the first inconsistency found."),
		new("dumpobj", "Object", ViewGroup.Heap, ViewKind.Detail, FilterField.None,
			"dumpobj <address>", "one object's fields, types and values at a given address."),
		new("gcroot", "Retention paths", ViewGroup.Heap, ViewKind.Detail, FilterField.None,
			"gcroot <address>", "the chain of references keeping an object alive, from a GC root down to it."),

		new("clrthreads", "Threads", ViewGroup.Threads, ViewKind.List, ThreadInfoFilter.Honored,
			"clrthreads", "every managed thread, its OS id, and any exception in flight."),
		new("threadstate", "Thread states", ViewGroup.Threads, ViewKind.List, ThreadStateInfoFilter.Honored,
			"threadstate", "per-thread state flags -- suspended, background, aborting, and the like."),
		new("syncblk", "Sync blocks", ViewGroup.Threads, ViewKind.List, SyncBlockInfoFilter.Honored,
			"syncblk", "Monitor locks and thin locks: who holds each one and who is waiting."),
		new("dumpstack", "Stacks", ViewGroup.Threads, ViewKind.Detail, ThreadStackInfoFilter.Honored,
			"dumpstack", "native and managed frames for every thread's stack in one pass."),
		new("clrstack", "Managed stacks", ViewGroup.Threads, ViewKind.Detail, FilterField.None,
			"clrstack", "the managed call stack of a single thread."),
		new("eestack", "All stacks", ViewGroup.Threads, ViewKind.Detail, FilterField.None,
			"eestack", "every thread's managed stack merged into one view, like Parallel Stacks."),
		new("threadpool", "Thread pool", ViewGroup.Threads, ViewKind.Detail, FilterField.None,
			"threadpool", "worker and I/O thread counts, queue length, and the configured min/max."),

		// The collection mode calls ThreadAnalyzer.GetThreadExceptions -- the in-flight-plus-heap
		// path, matching PrintExceptionCommand -- not GetHeapExceptions directly, so the owning
		// thread is populated and ManagedThreadId/OSThreadId are honored.
		new("printexception", "Exceptions", ViewGroup.Exceptions, ViewKind.List, ThreadExceptionInfoFilter.Honored,
			"pe", "in-flight and heap-resident exception objects, with type, message and owning thread."),

		new("clrmodules", "Modules", ViewGroup.Modules, ViewKind.List, ModuleInfoFilter.Honored,
			"clrmodules", "every managed module loaded into the process, with base address and version."),
		new("dumpmodule", "Module", ViewGroup.Modules, ViewKind.Detail, FilterField.None,
			"dumpmodule <address>", "one module's metadata: path, version, and type count."),
		new("dumpassembly", "Assembly", ViewGroup.Modules, ViewKind.Detail, FilterField.None,
			"dumpassembly <address>", "one assembly's identity and the modules that make it up."),

		new("dumpmt", "MethodTable", ViewGroup.Metadata, ViewKind.Detail, FilterField.None,
			"dumpmt <address>", "a type's MethodTable: name, base size, and method count."),
		new("dumpmd", "MethodDesc", ViewGroup.Metadata, ViewKind.Detail, FilterField.None,
			"dumpmd <address>", "a method's MethodDesc: signature, JIT state, and native code address."),
		new("dumpclass", "EEClass", ViewGroup.Metadata, ViewKind.Detail, FilterField.None,
			"dumpclass <address>", "a type's EEClass: fields, layout, and the MethodTable it backs."),
		new("name2ee", "Name to EE", ViewGroup.Metadata, ViewKind.Detail, FilterField.None,
			"name2ee <module> <type>", "resolves a type or method name to its MethodTable and MethodDesc."),
		new("ip2md", "IP to MethodDesc", ViewGroup.Metadata, ViewKind.Detail, FilterField.None,
			"ip2md <address>", "resolves a native instruction pointer back to the managed method it falls within."),
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