using DotNetDump.Core.Utilities;

namespace DotNetDump.Tests;

public class AddressParserTests {
	[Theory]
	[InlineData("13A611F10", 0x13A611F10UL)]
	[InlineData("13a611f10", 0x13A611F10UL)]
	[InlineData("000000013A611F10", 0x13A611F10UL)]
	[InlineData("0x13A611F10", 0x13A611F10UL)]
	[InlineData("0X13A611F10", 0x13A611F10UL)]
	[InlineData("0x13a611f10", 0x13A611F10UL)]
	[InlineData("  13A611F10  ", 0x13A611F10UL)]
	[InlineData("\t0x13A611F10\n", 0x13A611F10UL)]
	[InlineData("0", 0UL)]
	[InlineData("FFFFFFFFFFFFFFFF", ulong.MaxValue)]
	public void TryParse_AcceptsSupportedForms(string input, ulong expected) {
		Assert.True(AddressParser.TryParse(input, out ulong actual), $"expected '{input}' to parse");
		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData("00000001`3A611F10", 0x13A611F10UL)]
	[InlineData("0x00000001`3A611F10", 0x13A611F10UL)]
	public void TryParse_AcceptsWinDbgBacktickForm(string input, ulong expected) {
		Assert.True(AddressParser.TryParse(input, out ulong actual));
		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("0x")]
	[InlineData("nonsense")]
	[InlineData("13A611F10Z")]
	[InlineData("-1")]
	[InlineData("1FFFFFFFFFFFFFFFF")] // wider than 64 bits
	public void TryParse_RejectsUnusableInput(string? input) {
		Assert.False(AddressParser.TryParse(input, out ulong actual));
		Assert.Equal(0UL, actual);
	}

	[Fact]
	public void Parse_ThrowsWithAGuidingMessage() {
		var ex = Assert.Throws<ArgumentException>(() => AddressParser.Parse("nope", "objectAddress"));

		Assert.Contains("nope", ex.Message);
		Assert.Contains("objectAddress", ex.Message);
		// The message should show the caller what a good value looks like.
		Assert.Contains("0x", ex.Message);
	}

	[Fact]
	public void Parse_ReturnsValueForValidInput() {
		Assert.Equal(0x13A611F10UL, AddressParser.Parse("0x13A611F10"));
	}
}
