using System.Collections.Generic;

namespace DotNetDump.Core.Models;

/// <summary>
/// One page of a larger, already-sorted result set, plus enough bookkeeping to describe the page
/// honestly: how many rows exist in total, where this page starts, and how big it was asked to be.
/// </summary>
/// <remarks>
/// Introduced so analyzers can split a walk-scale computation (cacheable, per CLI_DESIGN.md
/// §6.2 — one entry must serve every <c>limit</c>/<c>offset</c>/<c>sort</c>/<c>order</c> variant)
/// from the pagination applied afterwards. <see cref="TotalAvailable"/> is the count of the full,
/// unpaginated (but already filtered/sorted) result — the number <c>Skip</c>/<c>Take</c> would
/// otherwise discard before a formatter ever saw it (CLI_DESIGN.md §10.3).
/// </remarks>
public sealed class PagedResult<T> {
	/// <summary>The rows for this page only — already sorted and sliced.</summary>
	public IReadOnlyList<T> Items { get; }

	/// <summary>Total rows in the full result, before <see cref="Offset"/>/<see cref="Limit"/> were applied.</summary>
	public int TotalAvailable { get; }

	/// <summary>Rows skipped before this page started.</summary>
	public int Offset { get; }

	/// <summary>The page size that was requested (not necessarily <see cref="Items"/>.Count — the last page is shorter).</summary>
	public int Limit { get; }

	/// <summary>Whether rows exist past the end of this page. Derived, not stored, so it can never disagree with the other three.</summary>
	public bool HasMore => Offset + Items.Count < TotalAvailable;

	public PagedResult(IReadOnlyList<T> items, int totalAvailable, int offset, int limit) {
		Items = items;
		TotalAvailable = totalAvailable;
		Offset = offset;
		Limit = limit;
	}
}