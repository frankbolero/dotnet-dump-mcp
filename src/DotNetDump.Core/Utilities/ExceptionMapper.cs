using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Utilities;

/// <summary>
/// Maps a <see cref="ClrException"/> onto <see cref="ExceptionDetails"/>, including its inner chain.
/// Shared so exceptions read the same whether they were found in flight on a thread, by address, or
/// by scanning the heap.
/// </summary>
public static class ExceptionMapper {
	public const int DefaultMaxDepth = 5;

	public static ExceptionDetails Map(ClrException exception, int maxDepth = DefaultMaxDepth) {
		if (maxDepth <= 0) {
			return new ExceptionDetails {
				Address = exception.Address,
				TypeName = exception.Type?.Name ?? "<unknown>",
				Message = "(inner exception chain truncated)"
			};
		}

		var details = new ExceptionDetails {
			Address = exception.Address,
			TypeName = exception.Type?.Name ?? "<unknown>",
			Message = exception.Message,
			HResult = exception.HResult
		};

		foreach (var frame in exception.EnumerateExceptionStackTrace()) {
			// Prefer the fully-qualified frame text; a bare method name loses the declaring type.
			string frameName = Describe(frame);
			if (!string.IsNullOrEmpty(frameName))
				details.StackTrace.Add(frameName);
		}

		if (exception.Inner != null)
			details.InnerExceptions.Add(Map(exception.Inner, maxDepth - 1));

		return details;
	}

	private static string Describe(ClrStackFrame frame) {
		if (frame.Method != null) {
			string? type = frame.Method.Type?.Name;
			return string.IsNullOrEmpty(type) ? frame.Method.Name ?? string.Empty : $"{type}.{frame.Method.Name}";
		}

		return frame.FrameName ?? frame.ToString() ?? string.Empty;
	}
}
