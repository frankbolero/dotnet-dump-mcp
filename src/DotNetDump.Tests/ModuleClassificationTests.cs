using DotNetDump.Core.Utilities;

namespace DotNetDump.Tests;

/// <summary>
/// Guards the "is this module part of the runtime?" test that decides what <c>clr_modules</c> hides
/// by default.
/// </summary>
public class ModuleClassificationTests {
	[Theory]
	[InlineData("/usr/local/share/dotnet/shared/Microsoft.NETCore.App/10.0.1/System.Private.CoreLib.dll")]
	[InlineData("/usr/local/share/dotnet/shared/Microsoft.NETCore.App/10.0.1/System.Threading.dll")]
	[InlineData("/usr/local/share/dotnet/shared/Microsoft.NETCore.App/10.0.1/Microsoft.Win32.Primitives.dll")]
	[InlineData("/usr/share/dotnet/shared/Microsoft.AspNetCore.App/8.0.0/Microsoft.AspNetCore.Http.dll")]
	[InlineData(@"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\9.0.0\System.Runtime.dll")]
	[InlineData("/app/mscorlib.dll")]
	[InlineData("netstandard.dll")]
	public void FrameworkAssemblies_AreClassifiedAsSystem(string path) {
		Assert.True(ModuleClassifier.IsSystemModule(path), $"expected '{path}' to be treated as a framework module");
	}

	[Theory]
	[InlineData("/app/MyCompany.Api.dll")]
	[InlineData("/app/target.dll")]
	[InlineData("/srv/publish/Contoso.Billing.Worker.dll")]
	public void ApplicationAssemblies_AreNotClassifiedAsSystem(string path) {
		Assert.False(ModuleClassifier.IsSystemModule(path));
	}

	[Theory]
	// A first-party assembly legitimately named Microsoft.* — extremely common inside Microsoft and
	// in any codebase following that convention. A name-only test hid these from clr_modules.
	[InlineData("/app/Microsoft.MyTeam.InternalService.dll")]
	[InlineData("/app/System.MyCompany.Extensions.dll")]
	public void FirstPartyAssembliesNamedLikeTheFramework_AreStillUserCode(string path) {
		Assert.False(ModuleClassifier.IsSystemModule(path),
			"an application assembly outside the shared framework directory must not be hidden");
	}

	[Theory]
	// The old test matched a substring anywhere in the path, so an app in a directory that merely
	// contained "Microsoft." disappeared from the default listing.
	[InlineData("/build/Microsoft.Sdk.Tools/output/MyApp.dll")]
	[InlineData("/home/dev/System.Experiments/bin/Release/MyApp.dll")]
	public void ApplicationAssembliesUnderAnUnluckyDirectory_AreStillUserCode(string path) {
		Assert.False(ModuleClassifier.IsSystemModule(path));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void MissingPaths_AreNotClassifiedAsSystem(string? path) {
		Assert.False(ModuleClassifier.IsSystemModule(path));
	}

	[Fact]
	public void BareFrameworkNameWithoutADirectory_IsTreatedAsSystem() {
		// Dynamic and path-less modules keep the name-only behaviour.
		Assert.True(ModuleClassifier.IsSystemModule("System.Private.CoreLib.dll"));
	}
}