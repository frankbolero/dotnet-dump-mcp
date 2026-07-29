namespace DotNetDump.Core.Models;

public enum SortDirection {
	Asc,
	Desc
}

public class QueryParameters {
	public int Limit { get; set; } = 50;
	public int Offset { get; set; } = 0;
	public string? SortBy { get; set; }
	public SortDirection SortDirection { get; set; } = SortDirection.Desc;

	/// <summary>
	/// Applied after the cached computation and before pagination. Defaults to
	/// <see cref="FilterSpec.None"/>, so existing callers are unaffected.
	/// </summary>
	public FilterSpec Filter { get; set; } = FilterSpec.None;
}