using System.Reflection;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// Decodes the <see cref="TypeAttributes"/> that <c>ClrType.TypeAttributes</c> carries. These are the
/// bits <c>dumpmt</c> reports; they were previously hardcoded to <c>false</c>.
/// </summary>
public static class TypeFlagsDecoder {
	public static bool IsInterface(TypeAttributes attributes) =>
		(attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface;

	public static bool IsAbstract(TypeAttributes attributes) =>
		(attributes & TypeAttributes.Abstract) != 0;

	public static bool IsSealed(TypeAttributes attributes) =>
		(attributes & TypeAttributes.Sealed) != 0;

	public static bool IsNested(TypeAttributes attributes) =>
		(attributes & TypeAttributes.VisibilityMask) >= TypeAttributes.NestedPublic;

	public static string Visibility(TypeAttributes attributes) =>
		(attributes & TypeAttributes.VisibilityMask) switch {
			TypeAttributes.Public => "public",
			TypeAttributes.NotPublic => "internal",
			TypeAttributes.NestedPublic => "nested public",
			TypeAttributes.NestedPrivate => "nested private",
			TypeAttributes.NestedFamily => "nested protected",
			TypeAttributes.NestedAssembly => "nested internal",
			TypeAttributes.NestedFamANDAssem => "nested private protected",
			TypeAttributes.NestedFamORAssem => "nested protected internal",
			_ => "unknown"
		};
}