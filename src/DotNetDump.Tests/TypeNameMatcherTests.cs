using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class TypeNameMatcherTests {
	[Fact]
	public void Create_WithNeitherFieldSet_MatchesEverything() {
		var matcher = TypeNameMatcher.Create(FilterSpec.None);
		Assert.True(matcher.Matches("System.String"));
		Assert.True(matcher.Matches(null));
	}

	[Fact]
	public void TypeName_IsCaseInsensitiveSubstring() {
		var matcher = TypeNameMatcher.Create(new FilterSpec { TypeName = "http" });
		Assert.True(matcher.Matches("System.Net.Http.HttpClient"));
		Assert.False(matcher.Matches("System.String"));
	}

	[Fact]
	public void TypeNameRegex_MatchesAnywhereWhenUnanchored() {
		var matcher = TypeNameMatcher.Create(new FilterSpec { TypeNameRegex = "Cache" });
		Assert.True(matcher.Matches("MyApp.Domain.CacheEntry"));
		Assert.False(matcher.Matches("MyApp.Domain.Widget"));
	}

	[Fact]
	public void TypeNameRegex_RespectsCallerSuppliedAnchors() {
		var matcher = TypeNameMatcher.Create(new FilterSpec { TypeNameRegex = "^MyApp\\.Cache" });
		Assert.True(matcher.Matches("MyApp.CacheEntry"));
		Assert.False(matcher.Matches("Other.MyApp.CacheEntry"));
	}

	[Fact]
	public void TypeNameRegex_IsCaseSensitiveByDefault() {
		// TypeName is documented as case-insensitive; the regex form is not forced to match that —
		// the caller has full control and can opt in with an inline (?i) if they want it.
		var matcher = TypeNameMatcher.Create(new FilterSpec { TypeNameRegex = "^http" });
		Assert.False(matcher.Matches("HttpClient"));
		Assert.True(matcher.Matches("httpClient"));
	}

	[Fact]
	public void BothFields_AreAnded() {
		var matcher = TypeNameMatcher.Create(new FilterSpec { TypeName = "Cache", TypeNameRegex = "^MyApp\\." });
		Assert.True(matcher.Matches("MyApp.CacheEntry"));
		// Matches the regex but not the substring.
		Assert.False(matcher.Matches("MyApp.Widget"));
		// Matches the substring but not the regex.
		Assert.False(matcher.Matches("Other.CacheEntry"));
	}

	[Fact]
	public void Create_ThrowsArgumentExceptionOnMalformedRegex() {
		var ex = Assert.Throws<ArgumentException>(() => TypeNameMatcher.Create(new FilterSpec { TypeNameRegex = "(unclosed" }));
		Assert.Contains("Invalid type name regular expression", ex.Message);
		Assert.Equal(nameof(FilterSpec.TypeNameRegex), ex.ParamName);
	}
}