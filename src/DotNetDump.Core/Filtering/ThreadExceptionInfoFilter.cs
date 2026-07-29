using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>ThreadAnalyzer.GetThreadExceptions</c> (<c>printexception</c>, in-flight). Unlike
/// the heap-scan path (<see cref="ExceptionDetailsFilter"/>), rows here carry an owning thread, so
/// <c>ManagedThreadId</c>/<c>OSThreadId</c> are honored. The type name and text fields refer to the
/// nested <see cref="ThreadExceptionInfo.Exception"/>, which is null only in states this method does
/// not produce when filtering is in play; a null exception simply fails the type/text checks rather
/// than throwing.
/// </summary>
public static class ThreadExceptionInfoFilter {
	public const FilterField Honored =
		FilterField.AnyTypeName | FilterField.ManagedThreadId | FilterField.OSThreadId | FilterField.Text;

	public static bool Matches(ThreadExceptionInfo item, FilterSpec spec, TypeNameMatcher typeName) {
		if (!typeName.Matches(item.Exception?.TypeName))
			return false;

		if (spec.ManagedThreadId.HasValue && item.ManagedThreadId != spec.ManagedThreadId.Value)
			return false;

		if (spec.OSThreadId.HasValue && item.OSThreadId != spec.OSThreadId.Value)
			return false;

		if (spec.Text != null && !FilterText.ContainsInAny(spec.Text, item.Exception?.TypeName, item.Exception?.Message))
			return false;

		return true;
	}
}