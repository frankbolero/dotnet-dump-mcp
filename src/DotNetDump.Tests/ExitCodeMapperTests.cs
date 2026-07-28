using DotNetDump.Cli;

namespace DotNetDump.Tests;

/// <summary>Pins the exception-to-exit-code mapping from CLI_DESIGN.md &#0167;3.4 as a pure function,
/// independent of any real command invocation.</summary>
public class ExitCodeMapperTests {
	[Fact]
	public void CliUsageException_MapsToUsageError() {
		Assert.Equal(2, ExitCodeMapper.Map(new CliUsageException("no dump")));
	}

	[Fact]
	public void DumpLoadException_MapsToDumpLoadFailure() {
		Assert.Equal(3, ExitCodeMapper.Map(new DumpLoadException("could not load", new InvalidOperationException())));
	}

	[Theory]
	[InlineData(typeof(ArgumentException))]
	[InlineData(typeof(InvalidOperationException))]
	[InlineData(typeof(FileNotFoundException))]
	public void OtherExceptionTypes_MapToAnalysisError(Type exceptionType) {
		var ex = (Exception)Activator.CreateInstance(exceptionType)!;

		Assert.Equal(1, ExitCodeMapper.Map(ex));
	}
}