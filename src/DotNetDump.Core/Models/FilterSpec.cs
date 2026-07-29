namespace DotNetDump.Core.Models;

/// <summary>Which part of the heap an object lives in. Mirrors the generations ClrMD reports.</summary>
public enum GenerationFilter {
	Gen0,
	Gen1,
	Gen2,
	Loh,
	Poh,
	Frozen
}

/// <summary>
/// A filter applied to an analyzer result after computation and before pagination.
/// Every field is optional; set fields are ANDed.
/// </summary>
/// <remarks>
/// The position in the pipeline is the whole design (DATA_CONTRACT.md §2.1):
/// <c>cached walk → filter → sort → page</c>. Filtering after the cached computation means one
/// cache entry serves every filter, exactly as it already serves every sort and page, so the cache
/// key must continue to exclude the filter along with limit/offset/sort/order/format.
/// <para>
/// Not every analyzer honors every field. Each declares its set and calls
/// <see cref="EnsureSupported"/>; see the matrix in DATA_CONTRACT.md §2.3.
/// </para>
/// </remarks>
public sealed class FilterSpec {
	/// <summary>Case-insensitive substring of the type name.</summary>
	public string? TypeName { get; init; }

	/// <summary>Regular expression over the type name. Anchored nowhere; the caller supplies anchors.</summary>
	public string? TypeNameRegex { get; init; }

	/// <summary>Case-insensitive substring of the module or assembly name.</summary>
	public string? Module { get; init; }

	/// <summary>Minimum size in bytes, inclusive.</summary>
	public ulong? MinSize { get; init; }

	/// <summary>Maximum size in bytes, inclusive.</summary>
	public ulong? MaxSize { get; init; }

	/// <summary>Minimum instance count, inclusive.</summary>
	public int? MinCount { get; init; }

	/// <summary>Maximum instance count, inclusive.</summary>
	public int? MaxCount { get; init; }

	public GenerationFilter? Generation { get; init; }

	public int? ManagedThreadId { get; init; }

	public uint? OSThreadId { get; init; }

	public bool? HasException { get; init; }

	/// <summary>
	/// Case-insensitive substring matched across whichever columns the view renders as text. What
	/// the web UI's single search box binds to.
	/// </summary>
	public string? Text { get; init; }

	/// <summary>The empty filter. Existing callers get this and are unaffected.</summary>
	public static readonly FilterSpec None = new();

	/// <summary>
	/// The fields actually set on this instance. Computed rather than stored so it cannot drift from
	/// the properties.
	/// </summary>
	public FilterField SetFields {
		get {
			FilterField fields = FilterField.None;
			if (TypeName != null) fields |= FilterField.TypeName;
			if (TypeNameRegex != null) fields |= FilterField.TypeNameRegex;
			if (Module != null) fields |= FilterField.Module;
			if (MinSize != null) fields |= FilterField.MinSize;
			if (MaxSize != null) fields |= FilterField.MaxSize;
			if (MinCount != null) fields |= FilterField.MinCount;
			if (MaxCount != null) fields |= FilterField.MaxCount;
			if (Generation != null) fields |= FilterField.Generation;
			if (ManagedThreadId != null) fields |= FilterField.ManagedThreadId;
			if (OSThreadId != null) fields |= FilterField.OSThreadId;
			if (HasException != null) fields |= FilterField.HasException;
			if (Text != null) fields |= FilterField.Text;
			return fields;
		}
	}

	public bool IsEmpty => SetFields == FilterField.None;

	/// <summary>
	/// Throws <see cref="UnsupportedFilterException"/> if any set field is outside
	/// <paramref name="honored"/>. Call this before doing any work, so an unsupported filter costs
	/// nothing and fails identically whether or not the result was cached.
	/// </summary>
	/// <param name="target">The view or command name, for the message — e.g. <c>clrmodules</c>.</param>
	/// <param name="honored">The fields this analyzer method applies.</param>
	public void EnsureSupported(string target, FilterField honored) {
		FilterField unsupported = SetFields & ~honored;
		if (unsupported != FilterField.None) {
			throw new UnsupportedFilterException(target, unsupported, honored);
		}
	}
}