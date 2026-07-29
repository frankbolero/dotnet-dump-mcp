using DotNetDump.Core.Filtering;

namespace DotNetDump.Tests;

public class FilterTextTests {
	[Fact]
	public void ContainsSubstring_IsCaseInsensitive() {
		Assert.True(FilterText.ContainsSubstring("System.Http.HttpClient", "http"));
		Assert.True(FilterText.ContainsSubstring("System.Http.HttpClient", "HTTP"));
	}

	[Fact]
	public void ContainsSubstring_FalseWhenAbsent() {
		Assert.False(FilterText.ContainsSubstring("System.String", "Http"));
	}

	[Fact]
	public void ContainsSubstring_NullHaystackNeverMatches() {
		Assert.False(FilterText.ContainsSubstring(null, ""));
	}

	[Fact]
	public void ContainsSubstring_EmptyNeedleMatchesAnyNonNullHaystack() {
		Assert.True(FilterText.ContainsSubstring("anything", ""));
	}

	[Fact]
	public void ContainsInAny_MatchesWhenAnyColumnContains() {
		Assert.True(FilterText.ContainsInAny("bar", "foo", "foobar", null));
	}

	[Fact]
	public void ContainsInAny_FalseWhenNoColumnContains() {
		Assert.False(FilterText.ContainsInAny("bar", "foo", null));
	}

	[Fact]
	public void ContainsInAny_ToleratesAllNullColumns() {
		Assert.False(FilterText.ContainsInAny("bar", null, null));
	}
}