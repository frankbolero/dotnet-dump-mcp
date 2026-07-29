using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// Maps ClrMD's <see cref="Generation"/> — reported per object by <c>ClrSegment.GetGeneration</c> —
/// to the <see cref="GenerationFilter"/> <see cref="FilterSpec.Generation"/> filters on. Captured
/// during the heap walk in <c>HeapAnalyzer.ComputeObjects</c> onto <c>HeapObjectItem.Generation</c>
/// (DATA_CONTRACT.md &#0167;2.3: "Generation on listobj requires a model change").
/// </summary>
public static class GenerationClassifier {
	/// <summary><see cref="Generation.Unknown"/> — corruption, or a segment kind the walk did not
	/// recognize — maps to <c>null</c> rather than to any <see cref="GenerationFilter"/> value, so it
	/// never matches a specific generation filter and is never mistaken for one.</summary>
	public static GenerationFilter? ToFilter(Generation generation) => generation switch {
		Generation.Generation0 => GenerationFilter.Gen0,
		Generation.Generation1 => GenerationFilter.Gen1,
		Generation.Generation2 => GenerationFilter.Gen2,
		Generation.Large => GenerationFilter.Loh,
		Generation.Pinned => GenerationFilter.Poh,
		Generation.Frozen => GenerationFilter.Frozen,
		_ => null
	};
}