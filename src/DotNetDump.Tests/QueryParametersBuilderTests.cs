using DotNetDump.Cli;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class QueryParametersBuilderTests {
	[Theory]
	[InlineData("asc", SortDirection.Asc)]
	[InlineData("ASC", SortDirection.Asc)]
	[InlineData("Asc", SortDirection.Asc)]
	public void Order_AcceptsAscCaseInsensitively(string order, SortDirection expected) {
		var parameters = QueryParametersBuilder.Build("Count", order, 10, 0);

		Assert.Equal(expected, parameters.SortDirection);
	}

	[Theory]
	[InlineData("desc")]
	[InlineData(null)]
	[InlineData("bogus")]
	public void Order_DefaultsToDesc_ForAnythingOtherThanAsc(string? order) {
		var parameters = QueryParametersBuilder.Build(null, order, 10, 0);

		Assert.Equal(SortDirection.Desc, parameters.SortDirection);
	}

	[Fact]
	public void SortField_PassesThroughUnvalidated_ForTheAnalyzerToInterpret() {
		var parameters = QueryParametersBuilder.Build("TypeName", "asc", 10, 0);

		Assert.Equal("TypeName", parameters.SortBy);
	}

	[Fact]
	public void Limit_And_Offset_PassThrough() {
		var parameters = QueryParametersBuilder.Build(null, null, 25, 5);

		Assert.Equal(25, parameters.Limit);
		Assert.Equal(5, parameters.Offset);
	}
}