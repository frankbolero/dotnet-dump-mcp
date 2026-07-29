namespace DotNetDump.Core.Models;

/// <summary>
/// One flag per <see cref="FilterSpec"/> field, so an analyzer method can declare the set it honors
/// as a single value and <see cref="FilterSpec.EnsureSupported"/> can name the offending field in
/// the error.
/// </summary>
/// <remarks>
/// The alternative — matching on field names as strings — puts the honored-field matrix
/// (DATA_CONTRACT.md §2.3) a typo away from silently honoring nothing, which is the exact failure
/// the matrix exists to prevent.
/// </remarks>
[Flags]
public enum FilterField {
	None = 0,
	TypeName = 1 << 0,
	TypeNameRegex = 1 << 1,
	Module = 1 << 2,
	MinSize = 1 << 3,
	MaxSize = 1 << 4,
	MinCount = 1 << 5,
	MaxCount = 1 << 6,
	Generation = 1 << 7,
	ManagedThreadId = 1 << 8,
	OSThreadId = 1 << 9,
	HasException = 1 << 10,
	Text = 1 << 11,

	/// <summary>Both ends of the size range — the pair is always honored together.</summary>
	Size = MinSize | MaxSize,

	/// <summary>Both ends of the count range.</summary>
	Count = MinCount | MaxCount,

	/// <summary>Either form of type matching.</summary>
	AnyTypeName = TypeName | TypeNameRegex
}