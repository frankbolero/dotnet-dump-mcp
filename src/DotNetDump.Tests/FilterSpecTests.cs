using DotNetDump.Core;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class FilterSpecTests {
	[Fact]
	public void None_IsEmpty() {
		Assert.True(FilterSpec.None.IsEmpty);
		Assert.Equal(FilterField.None, FilterSpec.None.SetFields);
	}

	[Fact]
	public void QueryParameters_DefaultsToNone() {
		// The point of the default: every existing caller compiles and behaves unchanged.
		Assert.True(new QueryParameters().Filter.IsEmpty);
	}

	[Fact]
	public void SetFields_ReportsEachSetField() {
		var spec = new FilterSpec { TypeName = "Http", MinSize = 1024 };
		Assert.Equal(FilterField.TypeName | FilterField.MinSize, spec.SetFields);
		Assert.False(spec.IsEmpty);
	}

	[Fact]
	public void SetFields_TreatsEmptyStringAsSet() {
		// Null means "not filtering"; an empty string is a filter the user typed. Conflating them
		// would make `--filter 'type~'` silently unfiltered.
		Assert.Equal(FilterField.TypeName, new FilterSpec { TypeName = "" }.SetFields);
	}

	[Fact]
	public void SetFields_TreatsFalseAsSet() {
		// bool? is the same trap: HasException=false is a real filter, not an absent one.
		Assert.Equal(FilterField.HasException, new FilterSpec { HasException = false }.SetFields);
	}

	[Fact]
	public void SetFields_TreatsZeroAsSet() {
		Assert.Equal(FilterField.MinSize, new FilterSpec { MinSize = 0 }.SetFields);
	}

	[Fact]
	public void SetFields_CoversEveryField() {
		// Guards the one bug this type can have: a property added without a line in SetFields, which
		// makes the field un-declarable and therefore silently unfilterable.
		var all = new FilterSpec {
			TypeName = "a",
			TypeNameRegex = "b",
			Module = "c",
			MinSize = 1,
			MaxSize = 2,
			MinCount = 3,
			MaxCount = 4,
			Generation = GenerationFilter.Gen2,
			ManagedThreadId = 5,
			OSThreadId = 6,
			HasException = true,
			Text = "d"
		};

		FilterField everySingleBit = FilterField.None;
		foreach (FilterField candidate in Enum.GetValues<FilterField>()) {
			if (candidate != FilterField.None && (candidate & (candidate - 1)) == 0) {
				everySingleBit |= candidate;
			}
		}

		Assert.Equal(everySingleBit, all.SetFields);
	}

	[Fact]
	public void EnsureSupported_PassesWhenAllFieldsHonored() {
		var spec = new FilterSpec { TypeName = "Http", MinSize = 100 };
		spec.EnsureSupported("dumpheap", FilterField.AnyTypeName | FilterField.Size | FilterField.Text);
	}

	[Fact]
	public void EnsureSupported_PassesForEmptySpecAgainstNoHonoredFields() {
		// dumpobj and friends honor nothing, but an unfiltered call must still work.
		FilterSpec.None.EnsureSupported("dumpobj", FilterField.None);
	}

	[Fact]
	public void EnsureSupported_ThrowsOnUnhonoredField() {
		var spec = new FilterSpec { MinSize = 1_000_000_000 };

		var ex = Assert.Throws<UnsupportedFilterException>(
			() => spec.EnsureSupported("clrmodules", FilterField.Module | FilterField.Text));

		Assert.Equal(FilterField.MinSize, ex.UnsupportedFields);
		Assert.Equal(FilterField.Module | FilterField.Text, ex.SupportedFields);
	}

	[Fact]
	public void EnsureSupported_ReportsOnlyTheUnhonoredFields() {
		var spec = new FilterSpec { TypeName = "Http", Generation = GenerationFilter.Gen2, OSThreadId = 9 };

		var ex = Assert.Throws<UnsupportedFilterException>(
			() => spec.EnsureSupported("dumpheap", FilterField.AnyTypeName));

		Assert.Equal(FilterField.Generation | FilterField.OSThreadId, ex.UnsupportedFields);
	}

	[Fact]
	public void Message_NamesTheRejectedFieldsAndTheAlternatives() {
		var spec = new FilterSpec { MinSize = 1 };

		var ex = Assert.Throws<UnsupportedFilterException>(
			() => spec.EnsureSupported("clrthreads", FilterField.ManagedThreadId | FilterField.Text));

		Assert.Contains("clrthreads", ex.Message);
		Assert.Contains("MinSize", ex.Message);
		Assert.Contains("ManagedThreadId", ex.Message);
		Assert.Contains("Text", ex.Message);
	}

	[Fact]
	public void Message_DoesNotNameCompositeAliases() {
		// Size is an alias for MinSize|MaxSize. Reporting "Size" would name a field the CLI grammar
		// has no spelling for.
		var spec = new FilterSpec { MinSize = 1, MaxSize = 2 };

		var ex = Assert.Throws<UnsupportedFilterException>(
			() => spec.EnsureSupported("clrthreads", FilterField.Text));

		Assert.Contains("MinSize", ex.Message);
		Assert.Contains("MaxSize", ex.Message);
		Assert.DoesNotContain("Size,", ex.Message.Replace("MinSize,", "").Replace("MaxSize,", ""));
	}

	[Fact]
	public void Message_ForATargetThatHonorsNothing_SaysSo() {
		var spec = new FilterSpec { TypeName = "Http" };

		var ex = Assert.Throws<UnsupportedFilterException>(
			() => spec.EnsureSupported("info", FilterField.None));

		Assert.Contains("does not support filtering", ex.Message);
		Assert.DoesNotContain("Supported:", ex.Message);
	}
}