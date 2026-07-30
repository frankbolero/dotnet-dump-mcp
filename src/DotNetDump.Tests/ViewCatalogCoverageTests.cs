using DotNetDump.Cli;
using DotNetDump.Web.Catalog;

namespace DotNetDump.Tests;

/// <summary>
/// That the view catalog and the CLI command surface agree about what views exist.
/// </summary>
/// <remarks>
/// <para>
/// DATA_CONTRACT.md &#0167;3.1: "Every view maps to exactly one existing command, so the command surface
/// in CLI_DESIGN.md &#0167;4 is the view inventory and needs no separate list." <see cref="ViewCatalog"/>
/// is nonetheless a second list, and a second list drifts. It drifted immediately: <c>verifyobj</c>
/// was missing from the first version of the catalog and nobody noticed, because a missing view
/// fails silently — it simply never appears in the navigation, and there is nothing to see.
/// </para>
/// <para>
/// So the CLI is the oracle. A command added to <c>RootCommandFactory</c> without a catalog entry
/// fails here rather than quietly going unreachable in the web UI.
/// </para>
/// </remarks>
public class ViewCatalogCoverageTests {
	/// <summary>
	/// Commands that deliberately have no view.
	/// </summary>
	/// <remarks>
	/// <c>use</c> writes the session file and <c>serve</c> starts this very server — both mutate or
	/// manage state, and SERVER.md &#0167;6 makes the web interface read-only, so neither can be a view.
	/// <c>commands</c> lists the command surface for an agent, which is what the navigation already
	/// is for a human.
	/// </remarks>
	private static readonly HashSet<string> NotViews = new(StringComparer.Ordinal) {
		"use",
		"serve",
		"commands",
	};

	private static IReadOnlyList<string> ViewCommandNames() =>
		RootCommandFactory.Create().Subcommands
			.Select(command => command.Name)
			.Where(name => !NotViews.Contains(name))
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

	[Fact]
	public void EveryCliCommand_HasAViewCatalogEntry() {
		var missing = ViewCommandNames()
			.Where(name => ViewCatalog.Find(name) is null)
			.ToArray();

		Assert.True(
			missing.Length == 0,
			$"CLI commands with no ViewCatalog entry: {string.Join(", ", missing)}. " +
			"Every command is a view (DATA_CONTRACT.md §3.1); add it to ViewCatalog, or to NotViews " +
			"here if it genuinely has no view.");
	}

	[Fact]
	public void EveryViewCatalogEntry_HasACliCommand() {
		var commands = ViewCommandNames().ToHashSet(StringComparer.Ordinal);

		var orphaned = ViewCatalog.All
			.Select(view => view.Name)
			.Where(name => !commands.Contains(name))
			.ToArray();

		// The other direction matters too: a view with no command behind it is a navigation entry
		// that can only ever 404 or 501, which is worse than not offering it.
		Assert.True(
			orphaned.Length == 0,
			$"ViewCatalog entries with no CLI command: {string.Join(", ", orphaned)}.");
	}

	[Fact]
	public void EveryView_CarriesACommandAndADescription() {
		// The view header renders both verbatim. An empty one is a blank line shipped to every user
		// of that view, and it would not fail anything else.
		foreach (var view in ViewCatalog.All) {
			Assert.False(string.IsNullOrWhiteSpace(view.Command), $"'{view.Name}' has no Command.");
			Assert.False(string.IsNullOrWhiteSpace(view.Description), $"'{view.Name}' has no Description.");
		}
	}

	[Fact]
	public void ViewNames_AreUnique() {
		var duplicates = ViewCatalog.All
			.GroupBy(view => view.Name, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToArray();

		Assert.True(duplicates.Length == 0, $"Duplicate view names: {string.Join(", ", duplicates)}.");
	}
}