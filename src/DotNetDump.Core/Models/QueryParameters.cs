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
}