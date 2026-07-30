using DotNetDump.Core.Models;
using DotNetDump.Web.Binding;
using DotNetDump.Web.Catalog;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace DotNetDump.Tests;

/// <summary>
/// Covers <see cref="ViewRequestBinder"/> against DATA_CONTRACT.md &#0167;3.2 (the query-string
/// contract) and &#0167;2.3 (the honored-filter matrix), and SERVER.md &#0167;2.1.
/// </summary>
public class ViewRequestBinderTests {
	private static IQueryCollection Query(params (string Key, string Value)[] pairs) {
		var values = new Dictionary<string, StringValues>();
		foreach ((string key, string value) in pairs) {
			values[key] = value;
		}

		return new QueryCollection(values);
	}

	private static ViewDescriptor View(string name) {
		ViewDescriptor? view = ViewCatalog.Find(name);
		Assert.NotNull(view);
		return view!;
	}

	// ---- Every parameter maps to the right field ----
	// One view per field, chosen from the honored-field matrix (DATA_CONTRACT.md §2.3) so the
	// mapping is actually exercised rather than short-circuited by EnsureSupported.

	[Fact]
	public void Type_MapsToFilterSpecTypeName() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("type", "Http")), View("dumpheap"));
		Assert.Equal("Http", request.Filter.TypeName);
	}

	[Fact]
	public void TypeRegex_MapsToFilterSpecTypeNameRegex() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("typeRegex", "^MyApp\\.")), View("dumpheap"));
		Assert.Equal("^MyApp\\.", request.Filter.TypeNameRegex);
	}

	[Fact]
	public void Module_MapsToFilterSpecModule() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("module", "System.Private.CoreLib")), View("clrmodules"));
		Assert.Equal("System.Private.CoreLib", request.Filter.Module);
	}

	[Fact]
	public void Text_MapsToFilterSpecText() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("text", "OutOfMemory")), View("dumpheap"));
		Assert.Equal("OutOfMemory", request.Filter.Text);
	}

	[Fact]
	public void MinSize_MapsToFilterSpecMinSize() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("minSize", "1048576")), View("dumpheap"));
		Assert.Equal(1048576UL, request.Filter.MinSize);
	}

	[Fact]
	public void MaxSize_MapsToFilterSpecMaxSize() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("maxSize", "2097152")), View("dumpheap"));
		Assert.Equal(2097152UL, request.Filter.MaxSize);
	}

	[Fact]
	public void MinCount_MapsToFilterSpecMinCount() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("minCount", "5")), View("dumpheap"));
		Assert.Equal(5, request.Filter.MinCount);
	}

	[Fact]
	public void MaxCount_MapsToFilterSpecMaxCount() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("maxCount", "500")), View("dumpheap"));
		Assert.Equal(500, request.Filter.MaxCount);
	}

	[Fact]
	public void Gen_MapsToFilterSpecGeneration() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("gen", "2")), View("listobj"));
		Assert.Equal(GenerationFilter.Gen2, request.Filter.Generation);
	}

	[Fact]
	public void Thread_MapsToFilterSpecManagedThreadId() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("thread", "7")), View("clrthreads"));
		Assert.Equal(7, request.Filter.ManagedThreadId);
	}

	[Fact]
	public void OsThread_MapsToFilterSpecOSThreadId() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("osthread", "4200")), View("clrthreads"));
		Assert.Equal(4200u, request.Filter.OSThreadId);
	}

	[Fact]
	public void HasException_MapsToFilterSpecHasException() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("hasException", "true")), View("clrthreads"));
		Assert.Equal(true, request.Filter.HasException);
	}

	[Fact]
	public void Sort_MapsToQueryParametersSortBy() {
		// Not a FilterSpec field, so it is exercised against a Detail view that honors nothing --
		// SortBy passes through unvalidated for the analyzer to interpret, same as the CLI side
		// (QueryParametersBuilderTests.SortField_PassesThroughUnvalidated_ForTheAnalyzerToInterpret).
		ViewRequest request = ViewRequestBinder.Bind(Query(("sort", "TotalSize")), View("info"));
		Assert.Equal("TotalSize", request.Parameters.SortBy);
	}

	[Fact]
	public void Order_MapsToQueryParametersSortDirection() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("order", "asc")), View("info"));
		Assert.Equal(SortDirection.Asc, request.Parameters.SortDirection);
	}

	[Fact]
	public void Offset_MapsToQueryParametersOffset() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("offset", "100")), View("info"));
		Assert.Equal(100, request.Parameters.Offset);
	}

	[Fact]
	public void Limit_MapsToQueryParametersLimit() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("limit", "25")), View("info"));
		Assert.Equal(25, request.Parameters.Limit);
	}

	// ---- Empty and whitespace-only values read as unset, not as an empty filter ----
	// This is the load-bearing behaviour the binder's own remarks call out: hx-include="closest
	// form" submits every input on every keystroke, so "?type=&text=Http" is the normal shape of a
	// request, not an attempt to filter on an empty type name.

	[Fact]
	public void EmptyType_OnAViewThatDoesNotHonorTypeFiltering_StillBinds() {
		// clrmodules honors Module | Size | Text (ModuleInfoFilter.Honored) -- not TypeName. If
		// "type=" bound to TypeName = "", EnsureSupported would reject this as a 400 the user could
		// neither cause nor clear by editing the URL.
		ViewRequest request = ViewRequestBinder.Bind(Query(("type", "")), View("clrmodules"));
		Assert.Null(request.Filter.TypeName);
		Assert.True(request.Filter.IsEmpty);
	}

	[Fact]
	public void WhitespaceOnlyType_ReadsAsUnset() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("type", "   ")), View("dumpheap"));
		Assert.Null(request.Filter.TypeName);
	}

	[Fact]
	public void AllEmptyQuery_ProducesAnEmptyFilterSpec() {
		// Every FilterSpec-bound query key present but blank, on a Detail view that honors nothing --
		// this is the shape hx-include="closest form" actually submits for a view whose filter bar
		// exposes no controls at all, and it must still bind without EnsureSupported ever seeing a
		// single field as "set".
		ViewRequest request = ViewRequestBinder.Bind(
			Query(
				("type", ""), ("typeRegex", ""), ("module", ""), ("text", ""),
				("minSize", ""), ("maxSize", ""), ("minCount", ""), ("maxCount", ""),
				("gen", ""), ("thread", ""), ("osthread", ""), ("hasException", "")),
			View("info"));

		Assert.True(request.Filter.IsEmpty);
		Assert.Equal(FilterField.None, request.Filter.SetFields);
	}

	// ---- limit / offset ----

	[Fact]
	public void Limit_DefaultsTo50WhenAbsent() {
		ViewRequest request = ViewRequestBinder.Bind(Query(), View("info"));
		Assert.Equal(50, request.Parameters.Limit);
		Assert.Equal(ViewRequestBinder.DefaultLimit, request.Parameters.Limit);
	}

	[Fact]
	public void Limit_ClampsToAMaximumOf500() {
		ViewRequest request = ViewRequestBinder.Bind(Query(("limit", "100000")), View("info"));
		Assert.Equal(500, request.Parameters.Limit);
		Assert.Equal(ViewRequestBinder.MaxLimit, request.Parameters.Limit);
	}

	[Fact]
	public void Limit_RejectsZero() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("limit", "0")), View("info")));
		Assert.Equal("limit", ex.Parameter);
	}

	[Fact]
	public void Limit_RejectsNegative() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("limit", "-1")), View("info")));
		Assert.Equal("limit", ex.Parameter);
	}

	[Fact]
	public void Offset_DefaultsTo0WhenAbsent() {
		ViewRequest request = ViewRequestBinder.Bind(Query(), View("info"));
		Assert.Equal(0, request.Parameters.Offset);
	}

	[Fact]
	public void Offset_RejectsNegative() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("offset", "-1")), View("info")));
		Assert.Equal("offset", ex.Parameter);
	}

	// ---- Unsupported-field rejection ----

	[Fact]
	public void Gen_OnClrmodules_ThrowsNamingTheFieldAndTheSupportedSet() {
		// ModuleInfoFilter.Honored = Module | Size | Text -- Generation is not in it.
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("gen", "2")), View("clrmodules")));

		// Wrapped as ViewRequestException("filter", ...) by the binder, so the parameter names the
		// wrapping concept, not the individual query key -- the message is what names the field.
		Assert.Equal("filter", ex.Parameter);
		Assert.Contains("clrmodules", ex.Message);
		Assert.Contains("Generation", ex.Message);
		Assert.Contains("Module", ex.Message);
	}

	[Fact]
	public void MinCount_OnListobj_ThrowsNamingTheFieldAndTheSupportedSet() {
		// HeapObjectItemFilter.Honored = AnyTypeName | Size | Generation | Text -- MinCount/MaxCount
		// are not in it (dumpheap's aggregate Count has no per-instance equivalent on listobj).
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("minCount", "5")), View("listobj")));

		Assert.Equal("filter", ex.Parameter);
		Assert.Contains("listobj", ex.Message);
		Assert.Contains("MinCount", ex.Message);
	}

	// ---- Malformed values ----

	[Fact]
	public void NonNumericMinSize_ThrowsNamingMinSize() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("minSize", "bogus")), View("dumpheap")));
		Assert.Equal("minSize", ex.Parameter);
	}

	[Fact]
	public void NegativeThread_ThrowsNamingThread() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("thread", "-1")), View("clrthreads")));
		Assert.Equal("thread", ex.Parameter);
	}

	[Fact]
	public void GenOutsideTheAllowedSet_ThrowsNamingGen() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("gen", "gen3")), View("listobj")));
		Assert.Equal("gen", ex.Parameter);
	}

	[Fact]
	public void HasExceptionNotTrueFalseOrOn_ThrowsNamingHasException() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("hasException", "maybe")), View("clrthreads")));
		Assert.Equal("hasException", ex.Parameter);
	}

	[Fact]
	public void OrderNotAscOrDesc_ThrowsNamingOrder() {
		// Deliberate divergence from DotNetDump.Cli.QueryParametersBuilder, which is tolerant and
		// falls back to SortDirection.Desc for any unrecognized value so the CLI and MCP front ends
		// behave identically (see QueryParametersBuilderTests.Order_DefaultsToDesc_ForAnythingOtherThanAsc).
		// The web host does not: the query string is view state written by hx-push-url and replayed
		// by the back button, and silently reinterpreting it is the class of bug Phase 4 exists to
		// catch (ViewRequestBinder.Order remarks).
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("order", "bogus")), View("info")));
		Assert.Equal("order", ex.Parameter);
	}

	// ---- typeRegex is rejected by the binder, not left to the analyzer ----

	[Fact]
	public void InvalidTypeRegex_IsRejectedByTheBinder_NamingTypeRegex() {
		var ex = Assert.Throws<ViewRequestException>(() => ViewRequestBinder.Bind(Query(("typeRegex", "(unclosed")), View("dumpheap")));
		Assert.Equal("typeRegex", ex.Parameter);
	}

	// ---- gen accepts both bare digits and enum-style names, case-insensitively ----

	[Theory]
	[InlineData("0", GenerationFilter.Gen0)]
	[InlineData("gen0", GenerationFilter.Gen0)]
	[InlineData("GEN0", GenerationFilter.Gen0)]
	[InlineData("1", GenerationFilter.Gen1)]
	[InlineData("gen1", GenerationFilter.Gen1)]
	[InlineData("2", GenerationFilter.Gen2)]
	[InlineData("gen2", GenerationFilter.Gen2)]
	[InlineData("loh", GenerationFilter.Loh)]
	[InlineData("LOH", GenerationFilter.Loh)]
	[InlineData("poh", GenerationFilter.Poh)]
	[InlineData("frozen", GenerationFilter.Frozen)]
	public void Gen_AcceptsBareDigitsAndEnumStyleNamesCaseInsensitively(string raw, GenerationFilter expected) {
		ViewRequest request = ViewRequestBinder.Bind(Query(("gen", raw)), View("listobj"));
		Assert.Equal(expected, request.Filter.Generation);
	}

	// ---- Repeated parameters resolve deterministically ----

	[Fact]
	public void RepeatedLimit_TheLastValueWins() {
		// ViewRequestBinder.Text reads values[^1], so the last occurrence in the query string wins.
		// Pinning this so a future change to "first wins" is a deliberate, visible diff rather than
		// an accidental one -- the query string being replayed verbatim by the back button
		// (DATA_CONTRACT.md §3.2) means this is observable behaviour, not an implementation detail.
		var values = new Dictionary<string, StringValues> {
			["limit"] = new StringValues(["10", "20"]),
		};

		ViewRequest request = ViewRequestBinder.Bind(new QueryCollection(values), View("info"));

		Assert.Equal(20, request.Parameters.Limit);
	}

	[Fact]
	public void RepeatedType_TheLastValueWins() {
		var values = new Dictionary<string, StringValues> {
			["type"] = new StringValues(["Foo", "Bar"]),
		};

		ViewRequest request = ViewRequestBinder.Bind(new QueryCollection(values), View("dumpheap"));

		Assert.Equal("Bar", request.Filter.TypeName);
	}
}