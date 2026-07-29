using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;

namespace DotNetDump.Tests;

public class SyncBlockInfoFilterTests {
	private static readonly SyncBlockInfo Item = new() {
		ObjectAddress = 0x1,
		TypeName = "System.Net.Http.HttpClient",
		IsMonitorHeld = true,
		ManagedThreadId = 7
	};

	[Fact]
	public void EmptySpec_Matches() {
		Assert.True(SyncBlockInfoFilter.Matches(Item, FilterSpec.None));
	}

	[Fact]
	public void TypeName_SubstringMatch() {
		Assert.True(SyncBlockInfoFilter.Matches(Item, new FilterSpec { TypeName = "http" }));
		Assert.False(SyncBlockInfoFilter.Matches(Item, new FilterSpec { TypeName = "Socket" }));
	}

	[Fact]
	public void ManagedThreadId_ExactMatch() {
		Assert.True(SyncBlockInfoFilter.Matches(Item, new FilterSpec { ManagedThreadId = 7 }));
		Assert.False(SyncBlockInfoFilter.Matches(Item, new FilterSpec { ManagedThreadId = 8 }));
	}

	[Fact]
	public void ManagedThreadId_NullOnItemNeverMatches() {
		var noOwner = new SyncBlockInfo { TypeName = "X", ManagedThreadId = null };
		Assert.False(SyncBlockInfoFilter.Matches(noOwner, new FilterSpec { ManagedThreadId = 7 }));
	}

	[Fact]
	public void Text_MatchesTypeNameOnly() {
		Assert.True(SyncBlockInfoFilter.Matches(Item, new FilterSpec { Text = "HttpClient" }));
		Assert.False(SyncBlockInfoFilter.Matches(Item, new FilterSpec { Text = "nope" }));
	}

	[Fact]
	public void Honored_ExcludesTypeNameRegex_UnlikeDumpheapAndListobj() {
		// syncblk honors plain TypeName only, per DATA_CONTRACT.md §2.3 -- not the AnyTypeName
		// composite dumpheap/listobj honor.
		Assert.Equal(FilterField.TypeName | FilterField.ManagedThreadId | FilterField.Text, SyncBlockInfoFilter.Honored);
		Assert.False(SyncBlockInfoFilter.Honored.HasFlag(FilterField.TypeNameRegex));
	}
}