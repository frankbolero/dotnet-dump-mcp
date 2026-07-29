using DotNetDump.Core.Models;

namespace DotNetDump.Core.Filtering;

/// <summary>
/// Predicate for <c>HeapAnalyzer.GetHeapExceptions</c> (<c>printexception</c>, heap scan). Per
/// DATA_CONTRACT.md &#0167;2.3, this is the heap-scan path — bare <see cref="ExceptionDetails"/> with
/// no owning thread — as distinct from <c>ThreadAnalyzer.GetThreadExceptions</c>
/// (<see cref="ThreadExceptionInfoFilter"/>), which also honors <c>ManagedThreadId</c>/<c>OSThreadId</c>
/// because it carries a thread. <c>Text</c> matches <see cref="ExceptionDetails.TypeName"/> or
/// <see cref="ExceptionDetails.Message"/>.
/// </summary>
public static class ExceptionDetailsFilter {
	public const FilterField Honored = FilterField.AnyTypeName | FilterField.Text;

	public static bool Matches(ExceptionDetails item, FilterSpec spec, TypeNameMatcher typeName) {
		if (!typeName.Matches(item.TypeName))
			return false;

		if (spec.Text != null && !FilterText.ContainsInAny(spec.Text, item.TypeName, item.Message))
			return false;

		return true;
	}
}