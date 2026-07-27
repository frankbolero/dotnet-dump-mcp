using System;
using System.Security.Cryptography;
using System.Text;

namespace DotNetDump.Core.Caching;

/// <summary>
/// Identity of the dump and DAC a result was derived from.
/// </summary>
/// <remarks>
/// Dumps are immutable, so any result derived from a given dump-plus-DAC pairing is valid
/// forever; the only thing that can go wrong is treating two different pairings as the same one.
/// <see cref="FromComponents"/> is built from cheap metadata (paths, sizes, timestamps) and
/// never from dump content -- hashing a 6-20 GB file to identify it would cost more than the
/// analysis the cache protects (see CLI_DESIGN.md, section 6.1).
/// </remarks>
public readonly record struct DumpIdentity(string Fingerprint) {
	/// <summary>
	/// Sentinel identity for "no dump loaded", mirroring <c>IDumpContext.IsLoaded</c> -- callers
	/// get a stable, comparable value instead of a null or an exception.
	/// </summary>
	public static readonly DumpIdentity None = new(string.Empty);

	// ASCII unit separator (0x1F): chosen because it cannot occur in a normal path, size or
	// timestamp component, so joining components with it cannot create a collision between
	// e.g. ("ab", "c") and ("a", "bc").
	private const char ComponentSeparator = '';

	/// <summary>
	/// Builds a stable identity from an ordered set of components (e.g. dump path/size/mtime,
	/// DAC path/size/mtime or DAC build signature). Components are joined with a separator that
	/// should never appear in any of them and hashed, so the result is stable across runs but
	/// changes whenever any input component changes.
	/// </summary>
	public static DumpIdentity FromComponents(params string[] components) {
		string combined = string.Join(ComponentSeparator, components);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
		return new DumpIdentity(Convert.ToHexString(hash));
	}
}