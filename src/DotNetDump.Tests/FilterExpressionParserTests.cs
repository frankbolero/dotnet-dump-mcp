using DotNetDump.Cli;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

/// <summary>
/// Unit tests for <see cref="FilterExpressionParser"/> against the grammar in
/// DATA_CONTRACT.md &#0167;2.4. This is the oracle for task 0.4: every field, every operator, every
/// byte unit, quoted/bare values, the regex form, whitespace tolerance, and malformed input.
/// </summary>
public class FilterExpressionParserTests {
	private static FilterSpec Parse(params string[] expressions) => FilterExpressionParser.Parse(expressions);

	// ---- empty input --------------------------------------------------------------------------

	[Fact]
	public void NullList_ReturnsNone() {
		Assert.Same(FilterSpec.None, FilterExpressionParser.Parse(null));
	}

	[Fact]
	public void EmptyList_ReturnsNone() {
		Assert.Same(FilterSpec.None, FilterExpressionParser.Parse(Array.Empty<string>()));
	}

	[Fact]
	public void BlankExpression_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("   "));
	}

	// ---- type: ~ (substring) --------------------------------------------------------------------

	[Fact]
	public void Type_Contains_SetsTypeName() {
		FilterSpec spec = Parse("type~Http");
		Assert.Equal("Http", spec.TypeName);
		Assert.Null(spec.TypeNameRegex);
	}

	[Fact]
	public void Type_Contains_EmptyValue_SetsEmptyTypeName() {
		// An empty value is a deliberate filter per FilterSpec's documented semantics (empty string
		// is "set", not "absent") -- not a parse error.
		FilterSpec spec = Parse("type~");
		Assert.Equal("", spec.TypeName);
	}

	[Fact]
	public void Type_Contains_QuotedValueWithSpaces() {
		FilterSpec spec = Parse("type~\"My App\"");
		Assert.Equal("My App", spec.TypeName);
	}

	[Fact]
	public void Type_Contains_SingleQuotedValue() {
		FilterSpec spec = Parse("type~'My App'");
		Assert.Equal("My App", spec.TypeName);
	}

	// ---- type: =/regex/ ---------------------------------------------------------------------

	[Fact]
	public void Type_EqualsRegex_SetsTypeNameRegex() {
		FilterSpec spec = Parse(@"type=/^MyApp\.Cache/");
		Assert.Equal(@"^MyApp\.Cache", spec.TypeNameRegex);
		Assert.Null(spec.TypeName);
	}

	[Fact]
	public void Type_EqualsQuotedRegex_StripsBothLayers() {
		FilterSpec spec = Parse("type=\"/^Foo/\"");
		Assert.Equal("^Foo", spec.TypeNameRegex);
	}

	[Fact]
	public void Type_EqualsBareValue_IsUsageError() {
		// No exact-match property exists on FilterSpec for type; coercing this into TypeName
		// (substring) or TypeNameRegex (implicit anchor) would silently reinterpret intent.
		var ex = Assert.Throws<CliUsageException>(() => Parse("type=Foo"));
		Assert.Contains("regex", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("type>Foo")]
	[InlineData("type>=Foo")]
	[InlineData("type<Foo")]
	[InlineData("type<=Foo")]
	public void Type_OrderingOperators_AreUsageErrors(string expression) {
		Assert.Throws<CliUsageException>(() => Parse(expression));
	}

	[Fact]
	public void Type_ContainsAndRegex_ComposeWithoutConflict() {
		// Different FilterSpec properties -- not a duplicate-field conflict, and Core ANDs them.
		FilterSpec spec = Parse("type~Http", "type=/^Http/");
		Assert.Equal("Http", spec.TypeName);
		Assert.Equal("^Http", spec.TypeNameRegex);
	}

	[Fact]
	public void Type_TwoContainsExpressions_IsUsageError() {
		var ex = Assert.Throws<CliUsageException>(() => Parse("type~A", "type~B"));
		Assert.Contains("TypeName", ex.Message);
	}

	[Fact]
	public void Type_TwoRegexExpressions_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("type=/A/", "type=/B/"));
	}

	// ---- module -------------------------------------------------------------------------------

	[Fact]
	public void Module_Contains_SetsModule() {
		FilterSpec spec = Parse("module~MyApp.dll");
		Assert.Equal("MyApp.dll", spec.Module);
	}

	[Fact]
	public void Module_Equals_IsUsageError() {
		// Module has no exact-match or regex property, so only '~' is honored.
		Assert.Throws<CliUsageException>(() => Parse("module=MyApp.dll"));
	}

	[Fact]
	public void Module_TwoExpressions_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("module~A", "module~B"));
	}

	// ---- text -----------------------------------------------------------------------------------

	[Fact]
	public void Text_Contains_SetsText() {
		FilterSpec spec = Parse("text~timeout");
		Assert.Equal("timeout", spec.Text);
	}

	[Fact]
	public void Text_Equals_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("text=timeout"));
	}

	// ---- size -----------------------------------------------------------------------------------

	[Fact]
	public void Size_BareInteger_IsBytes() {
		FilterSpec spec = Parse("size>=512");
		Assert.Equal(512UL, spec.MinSize);
	}

	[Theory]
	[InlineData("size>=1kb", 1024UL)]
	[InlineData("size>=1KB", 1024UL)]
	[InlineData("size>=1mb", 1024UL * 1024UL)]
	[InlineData("size>=1MB", 1024UL * 1024UL)]
	[InlineData("size>=1gb", 1024UL * 1024UL * 1024UL)]
	[InlineData("size>=2gb", 2UL * 1024UL * 1024UL * 1024UL)]
	[InlineData("size>=1b", 1UL)]
	public void Size_ByteUnits_AreBinaryAndCaseInsensitive(string expression, ulong expectedBytes) {
		FilterSpec spec = Parse(expression);
		Assert.Equal(expectedBytes, spec.MinSize);
	}

	[Fact]
	public void Size_GreaterOrEqual_SetsMinSizeToExactValue() {
		FilterSpec spec = Parse("size>=100");
		Assert.Equal(100UL, spec.MinSize);
	}

	[Fact]
	public void Size_GreaterThan_SetsMinSizeToValuePlusOne() {
		// The resolution stated in FilterExpressionParser's remarks: '>' on an inclusive Min bound
		// is Min = value + 1, distinct from '>=' which is Min = value. This is the test that proves
		// '>' is not silently aliased to '>='.
		FilterSpec spec = Parse("size>100");
		Assert.Equal(101UL, spec.MinSize);
	}

	[Fact]
	public void Size_GreaterThan_And_GreaterOrEqual_ProduceDifferentBounds() {
		FilterSpec gt = Parse("size>100");
		FilterSpec ge = Parse("size>=100");
		Assert.NotEqual(gt.MinSize, ge.MinSize);
	}

	[Fact]
	public void Size_LessOrEqual_SetsMaxSizeToExactValue() {
		FilterSpec spec = Parse("size<=100mb");
		Assert.Equal(100UL * 1024UL * 1024UL, spec.MaxSize);
	}

	[Fact]
	public void Size_LessThan_SetsMaxSizeToValueMinusOne() {
		FilterSpec spec = Parse("size<100");
		Assert.Equal(99UL, spec.MaxSize);
	}

	[Fact]
	public void Size_Equals_SetsBothMinAndMax() {
		FilterSpec spec = Parse("size=100mb");
		ulong expected = 100UL * 1024UL * 1024UL;
		Assert.Equal(expected, spec.MinSize);
		Assert.Equal(expected, spec.MaxSize);
	}

	[Fact]
	public void Size_MinAndMax_ComposeFromTwoExpressions() {
		FilterSpec spec = Parse("size>100mb", "size<200mb");
		Assert.Equal(100UL * 1024UL * 1024UL + 1, spec.MinSize);
		Assert.Equal(200UL * 1024UL * 1024UL - 1, spec.MaxSize);
	}

	[Fact]
	public void Size_TwoLowerBounds_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("size>100mb", "size>=200mb"));
	}

	[Fact]
	public void Size_ContainsOperator_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("size~100mb"));
	}

	[Theory]
	[InlineData("size>100xb")]
	[InlineData("size>abc")]
	[InlineData("size>-5mb")]
	[InlineData("size>1.5mb")]
	[InlineData("size>")]
	public void Size_MalformedValue_IsUsageError(string expression) {
		Assert.Throws<CliUsageException>(() => Parse(expression));
	}

	[Fact]
	public void Size_LessThanZero_IsUsageErrorNotUnderflow() {
		// A naive `value - 1` on ulong wraps 0 to ulong.MaxValue, which would silently mean
		// "unbounded" -- the opposite of what was asked. This must be rejected instead.
		var ex = Assert.Throws<CliUsageException>(() => Parse("size<0"));
		Assert.DoesNotContain("18446744073709551615", ex.Message);
	}

	[Fact]
	public void Size_GreaterThanMaxValue_IsUsageErrorNotOverflow() {
		Assert.Throws<CliUsageException>(() => Parse($"size>{ulong.MaxValue}"));
	}

	// ---- count ----------------------------------------------------------------------------------

	[Fact]
	public void Count_GreaterOrEqual_SetsMinCount() {
		FilterSpec spec = Parse("count>=5");
		Assert.Equal(5, spec.MinCount);
	}

	[Fact]
	public void Count_GreaterThan_SetsMinCountToValuePlusOne() {
		FilterSpec spec = Parse("count>5");
		Assert.Equal(6, spec.MinCount);
	}

	[Fact]
	public void Count_LessThan_SetsMaxCountToValueMinusOne() {
		FilterSpec spec = Parse("count<10");
		Assert.Equal(9, spec.MaxCount);
	}

	[Fact]
	public void Count_LessOrEqual_SetsMaxCountToExactValue() {
		FilterSpec spec = Parse("count<=10");
		Assert.Equal(10, spec.MaxCount);
	}

	[Fact]
	public void Count_Equals_SetsBothBounds() {
		FilterSpec spec = Parse("count=5");
		Assert.Equal(5, spec.MinCount);
		Assert.Equal(5, spec.MaxCount);
	}

	[Fact]
	public void Count_DoesNotAcceptByteUnits() {
		Assert.Throws<CliUsageException>(() => Parse("count>5mb"));
	}

	[Fact]
	public void Count_ContainsOperator_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("count~5"));
	}

	// ---- gen ------------------------------------------------------------------------------------

	[Theory]
	[InlineData("gen=0", GenerationFilter.Gen0)]
	[InlineData("gen=1", GenerationFilter.Gen1)]
	[InlineData("gen=2", GenerationFilter.Gen2)]
	[InlineData("gen=gen2", GenerationFilter.Gen2)]
	[InlineData("gen=loh", GenerationFilter.Loh)]
	[InlineData("gen=LOH", GenerationFilter.Loh)]
	[InlineData("gen=poh", GenerationFilter.Poh)]
	[InlineData("gen=frozen", GenerationFilter.Frozen)]
	public void Gen_Equals_SetsGeneration(string expression, GenerationFilter expected) {
		FilterSpec spec = Parse(expression);
		Assert.Equal(expected, spec.Generation);
	}

	[Fact]
	public void Gen_UnknownValue_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("gen=3"));
	}

	[Fact]
	public void Gen_ContainsOperator_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("gen~2"));
	}

	// ---- thread / osthread ---------------------------------------------------------------------

	[Fact]
	public void Thread_Equals_SetsManagedThreadId() {
		FilterSpec spec = Parse("thread=7");
		Assert.Equal(7, spec.ManagedThreadId);
	}

	[Fact]
	public void OSThread_Equals_SetsOSThreadId() {
		FilterSpec spec = Parse("osthread=4200");
		Assert.Equal(4200u, spec.OSThreadId);
	}

	[Fact]
	public void OSThread_NegativeValue_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("osthread=-1"));
	}

	[Fact]
	public void Thread_NonNumeric_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("thread=main"));
	}

	[Fact]
	public void Thread_And_OSThread_AreIndependentFields() {
		// Regression guard: "osthread" must be recognized as its own field, not parsed as "thread"
		// with a stray "os" prefix left dangling.
		FilterSpec spec = Parse("thread=1", "osthread=2");
		Assert.Equal(1, spec.ManagedThreadId);
		Assert.Equal(2u, spec.OSThreadId);
	}

	// ---- exception ------------------------------------------------------------------------------

	[Theory]
	[InlineData("exception=true", true)]
	[InlineData("exception=TRUE", true)]
	[InlineData("exception=false", false)]
	public void Exception_Equals_SetsHasException(string expression, bool expected) {
		FilterSpec spec = Parse(expression);
		Assert.Equal(expected, spec.HasException);
	}

	[Fact]
	public void Exception_NonBooleanValue_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("exception=yes"));
	}

	// ---- negation is rejected on every field ----------------------------------------------------

	[Theory]
	[InlineData("type!~Http")]
	[InlineData("type!=/Foo/")]
	[InlineData("module!~Foo")]
	[InlineData("text!~Foo")]
	[InlineData("size!=100")]
	[InlineData("count!=5")]
	[InlineData("gen!=2")]
	[InlineData("thread!=1")]
	[InlineData("osthread!=1")]
	[InlineData("exception!=true")]
	public void NegatingOperators_AreRejectedOnEveryField(string expression) {
		var ex = Assert.Throws<CliUsageException>(() => Parse(expression));
		Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	// ---- grammar-level malformed input ------------------------------------------------------------

	[Fact]
	public void UnknownField_IsUsageError() {
		var ex = Assert.Throws<CliUsageException>(() => Parse("bogus~Foo"));
		Assert.Contains("bogus", ex.Message);
	}

	[Fact]
	public void MissingOperator_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("typeHttp"));
	}

	[Fact]
	public void MissingValue_ButValidOperator_IsUsageErrorForNumericField() {
		Assert.Throws<CliUsageException>(() => Parse("size>"));
	}

	[Fact]
	public void UnrecognizedOperatorCharacter_IsUsageError() {
		Assert.Throws<CliUsageException>(() => Parse("type#Http"));
	}

	// ---- whitespace tolerance --------------------------------------------------------------------

	[Theory]
	[InlineData("type ~ Http")]
	[InlineData("type~ Http")]
	[InlineData("type ~Http")]
	[InlineData("  type~Http  ")]
	public void Whitespace_AroundFieldOperatorValue_IsTolerated(string expression) {
		FilterSpec spec = Parse(expression);
		Assert.Equal("Http", spec.TypeName);
	}

	[Fact]
	public void Whitespace_AroundSizeOperator_IsTolerated() {
		FilterSpec spec = Parse("size >= 100mb");
		Assert.Equal(100UL * 1024UL * 1024UL, spec.MinSize);
	}

	// ---- repeats across different fields AND -------------------------------------------------

	[Fact]
	public void MultipleFilters_OnDifferentFields_AreAnded() {
		FilterSpec spec = Parse("type~Http", "size>100mb");
		Assert.Equal("Http", spec.TypeName);
		Assert.Equal(100UL * 1024UL * 1024UL + 1, spec.MinSize);
	}

	[Fact]
	public void RealWorldExample_DumpheapFilter() {
		FilterSpec spec = Parse("type~Http", "size>100mb");
		Assert.False(spec.IsEmpty);
		Assert.Equal(FilterField.TypeName | FilterField.MinSize, spec.SetFields);
	}

	[Fact]
	public void RealWorldExample_ListobjFilter() {
		FilterSpec spec = Parse(@"type=/^MyApp\.Cache/", "gen=2");
		Assert.Equal(@"^MyApp\.Cache", spec.TypeNameRegex);
		Assert.Equal(GenerationFilter.Gen2, spec.Generation);
	}

	[Fact]
	public void RealWorldExample_ClrthreadsFilter() {
		FilterSpec spec = Parse("exception=true");
		Assert.True(spec.HasException);
	}
}