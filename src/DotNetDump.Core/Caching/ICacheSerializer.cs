using System.IO;

namespace DotNetDump.Core.Caching;

/// <summary>
/// Encodes and decodes cached payloads. Separated from <see cref="IAnalysisCache"/> because
/// tier 1 (heap statistics, GC root candidates -- KB to MB, JSON) and a future tier 2 (a
/// GB-scale object index -- compact binary) have incompatible needs; see CLI_DESIGN.md
/// &#0167;6.3-6.4. Only <see cref="JsonCacheSerializer"/> ships today.
/// </summary>
public interface ICacheSerializer {
	void Write<T>(Stream destination, T value);

	T? Read<T>(Stream source);
}