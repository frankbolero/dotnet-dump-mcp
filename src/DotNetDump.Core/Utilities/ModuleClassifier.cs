using System;

namespace DotNetDump.Core.Utilities;

/// <summary>Classifies modules as framework or application code.</summary>
public static class ModuleClassifier {
/// <summary>
/// Whether a module is part of the runtime rather than the application.
/// <para>
/// Matched on the assembly's simple name, not the full path: a substring test over the path hides
/// any application assembly that merely lives under a directory containing "System." or
/// "Microsoft.", and hides first-party assemblies legitimately named <c>Microsoft.*</c>.
/// </para>
/// </summary>
public static bool IsSystemModule(string? path) {
	if (string.IsNullOrEmpty(path))
		return false;

	string fileName = System.IO.Path.GetFileName(path);
	if (string.IsNullOrEmpty(fileName))
		fileName = path;

	if (fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
		 fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase))
		return true;

	// The shared framework ships as System.* / Microsoft.* assemblies from the runtime directory.
	// Requiring both the name prefix and the runtime location avoids catching an application
	// assembly that simply happens to be called Microsoft.Something.dll.
	bool frameworkName = fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
		|| fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);

	if (!frameworkName)
		return false;

	string directory = (System.IO.Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
	bool runtimeDirectory = directory.Contains("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)
		|| directory.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase)
		|| directory.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase)
		|| directory.Contains("/shared/", StringComparison.OrdinalIgnoreCase);

	// A dynamic or path-less module keeps the old name-only behaviour.
	return runtimeDirectory || directory.Length == 0;
}
}
