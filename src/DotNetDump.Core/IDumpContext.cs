using System;

using DotNetDump.Core.Caching;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core;

public interface IDumpContext : IDisposable {
	DataTarget? DataTarget { get; }
	ClrRuntime? Runtime { get; }
	ClrHeap? Heap { get; }
	bool IsLoaded { get; }

	/// <summary>
	/// Identity of the currently loaded dump and its resolved DAC, for use as an
	/// <see cref="IAnalysisCache"/> key component. <see cref="DumpIdentity.None"/> when
	/// <see cref="IsLoaded"/> is <c>false</c>.
	/// </summary>
	DumpIdentity Identity { get; }

	/// <summary>
	/// Loads a memory dump. If a dump is already loaded, it will be unloaded first.
	/// </summary>
	void Load(string dumpPath, string? dacPath = null);

	/// <summary>
	/// Unloads the current dump and releases resources.
	/// </summary>
	void Unload();
}