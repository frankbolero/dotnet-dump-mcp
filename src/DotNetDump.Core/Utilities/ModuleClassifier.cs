using System;

namespace DotNetDump.Core.Utilities;

/// <summary>Classifies modules as framework or application code.</summary>
public static class ModuleClassifier {
	/// <summary>
	/// Whether a module is part of the runtime rather than the application.
	/// <para>
	/// Matched on the assembly's simple name plus its location, not on a substring of the whole path.
	/// A substring test over the path hides any application assembly that merely lives under a
	/// directory containing "System." or "Microsoft.", and hides first-party assemblies legitimately
	/// named <c>Microsoft.*</c>.
	/// </para>
	/// <para>
	/// Path splitting is done on both separators explicitly rather than through <c>System.IO.Path</c>:
	/// this server routinely analyses dumps captured on a different OS from the one it runs on, so a
	/// Windows path can arrive while running on Linux, where <c>Path</c> does not treat <c>\</c> as a
	/// separator.
	/// </para>
	/// </summary>
	public static bool IsSystemModule(string? path) {
		if (string.IsNullOrEmpty(path))
			return false;

		var (directory, fileName) = SplitPath(path);

		if (fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
			 fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase))
			return true;

		// The shared framework ships as System.* / Microsoft.* assemblies out of the runtime
		// directory. Requiring both the name prefix and the location avoids catching an application
		// assembly that simply happens to be called Microsoft.Something.dll.
		bool frameworkName = fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
			|| fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);

		if (!frameworkName)
			return false;

		bool runtimeDirectory = directory.Contains("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)
			|| directory.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase)
			|| directory.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase)
			|| directory.Contains("/shared/", StringComparison.OrdinalIgnoreCase)
			|| directory.EndsWith("/shared", StringComparison.OrdinalIgnoreCase);

		// A dynamic or path-less module keeps name-only classification.
		return runtimeDirectory || directory.Length == 0;
	}

	/// <summary>
	/// Splits into directory (normalised to forward slashes) and file name, treating both <c>/</c> and
	/// <c>\</c> as separators regardless of the host OS.
	/// </summary>
	private static (string Directory, string FileName) SplitPath(string path) {
		int separator = path.LastIndexOfAny(new[] { '/', '\\' });

		if (separator < 0)
			return (string.Empty, path);

		string directory = path.Substring(0, separator).Replace('\\', '/');
		string fileName = path.Substring(separator + 1);

		// A path ending in a separator has no file name; fall back to the whole value.
		return (directory, fileName.Length > 0 ? fileName : path);
	}
}