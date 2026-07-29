using DotNetDump.Core;
using DotNetDump.Core.Caching;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Tests;

/// <summary>
/// An <see cref="IDumpContext"/> that never has a dump loaded. Used to exercise analyzer-method
/// behavior that must not touch the dump at all — most importantly, that
/// <c>FilterSpec.EnsureSupported</c> rejects an unsupported filter before any dump access, per
/// DATA_CONTRACT.md &#0167;2.1: "so an unsupported filter costs nothing and fails identically cached
/// or not." Any analyzer call that reaches past that check throws
/// <see cref="InvalidOperationException"/> from <c>GetHeap</c>/<c>GetRuntime</c> instead of an
/// <c>UnsupportedFilterException</c> — which is itself the signal that the ordering was violated.
/// </summary>
public sealed class NoDumpContext : IDumpContext {
	public DataTarget? DataTarget => null;
	public ClrRuntime? Runtime => null;
	public ClrHeap? Heap => null;
	public bool IsLoaded => false;
	public DumpIdentity Identity => DumpIdentity.None;

	public void Load(string dumpPath, string? dacPath = null) =>
		throw new NotSupportedException("NoDumpContext never loads a dump.");

	public void Unload() { }

	public void Dispose() { }
}