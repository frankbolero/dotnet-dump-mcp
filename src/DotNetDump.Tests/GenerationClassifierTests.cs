using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Tests;

public class GenerationClassifierTests {
	[Theory]
	[InlineData(Generation.Generation0, GenerationFilter.Gen0)]
	[InlineData(Generation.Generation1, GenerationFilter.Gen1)]
	[InlineData(Generation.Generation2, GenerationFilter.Gen2)]
	[InlineData(Generation.Large, GenerationFilter.Loh)]
	[InlineData(Generation.Pinned, GenerationFilter.Poh)]
	[InlineData(Generation.Frozen, GenerationFilter.Frozen)]
	public void ToFilter_MapsEveryKnownGeneration(Generation generation, GenerationFilter expected) {
		Assert.Equal(expected, GenerationClassifier.ToFilter(generation));
	}

	[Fact]
	public void ToFilter_UnknownMapsToNull() {
		Assert.Null(GenerationClassifier.ToFilter(Generation.Unknown));
	}
}