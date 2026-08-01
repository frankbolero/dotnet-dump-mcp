namespace DotNetDump.Web.Catalog;

/// <param name="Name">URL segment: <c>/trees/{Name}</c>.</param>
/// <param name="Title">Nav label and page heading.</param>
/// <param name="Group">Which <see cref="ViewGroup"/> it is filed under in navigation, alongside the
/// views it is a rendering of.</param>
/// <param name="Command">
/// Shown next to <see cref="Description"/> in the view header, matching how a
/// <see cref="ViewDescriptor"/> does it -- a tree maps to no single CLI command
/// (DATA_CONTRACT.md &#0167;3.1 does not hold for it), so this names the closest one instead of
/// leaving the header's SOS-command slot empty.
/// </param>
public sealed record TreeDescriptor(string Name, string Title, ViewGroup Group, string Command, string Description);

/// <summary>
/// The two trees reachable from top-level navigation with no address in hand
/// (DATA_CONTRACT.md &#0167;4.2, &#0167;4.4). <c>object</c> (&#0167;4.5) needs an address and is linked to
/// contextually from an object's own <c>dumpobj</c> page instead. <c>gcroot</c> (&#0167;4.3) also needs
/// an address and keeps its existing <see cref="ViewCatalog"/> entry for CLI-coverage parity;
/// <c>/views/gcroot/{address}</c> redirects into <c>/trees/gcroot/{address}</c> rather than this
/// catalog carrying a second, address-shaped entry for it.
/// </summary>
public static class TreeCatalog {
	private static readonly TreeDescriptor[] Descriptors = [
		new("heap", "Heap composition", ViewGroup.Heap, "dumpheap -stat (rollup)",
			"heap statistics grouped by namespace, so where the memory actually lives is visible at a glance."),
		new("threads", "Call tree", ViewGroup.Threads, "clrthreads + dumpstack (grouped)",
			"every managed thread with its stack frames nested underneath, identical stacks grouped together."),
	];

	private static readonly Dictionary<string, TreeDescriptor> ByName =
		Descriptors.ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal);

	public static IReadOnlyList<TreeDescriptor> All => Descriptors;

	public static TreeDescriptor? Find(string? name) =>
		name is not null && ByName.TryGetValue(name, out var descriptor) ? descriptor : null;
}