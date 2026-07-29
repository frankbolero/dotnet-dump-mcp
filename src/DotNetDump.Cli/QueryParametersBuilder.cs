using System;

using DotNetDump.Core.Models;

namespace DotNetDump.Cli;

/// <summary>
/// Builds a Core <see cref="QueryParameters"/> from raw CLI option values. Mirrors the tolerant
/// parsing <c>DumpAnalyzerTools.CreateParameters</c> already uses on the MCP side: an unrecognized
/// <c>--order</c> value quietly falls back to <see cref="SortDirection.Desc"/> rather than
/// producing a parse error, and an unrecognized <c>--sort</c> field is left to the analyzer, which
/// already falls back to its own default. Keeping this identical to the MCP path means the two
/// front ends behave the same for the same inputs.
/// </summary>
internal static class QueryParametersBuilder {
	/// <summary><paramref name="filter"/> defaults to null (mapped to <see cref="FilterSpec.None"/>),
	/// so every existing caller -- commands that never gained a <c>--filter</c> option -- compiles
	/// and behaves unchanged.</summary>
	public static QueryParameters Build(string? sortBy, string? order, int limit, int offset, FilterSpec? filter = null) => new QueryParameters {
		SortBy = sortBy,
		SortDirection = string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Asc : SortDirection.Desc,
		Limit = limit,
		Offset = offset,
		Filter = filter ?? FilterSpec.None,
	};
}