using System.IO;
using System.Text.Json;

namespace DotNetDump.Core.Caching;

/// <summary>
/// Tier-1 payload encoding. Fine for the KB-to-few-MB entries produced by heap statistics, GC
/// root candidates and similar walk-scale results (CLI_DESIGN.md &#0167;6.3); a future
/// binary/memory-mapped serializer would be used for the GB-scale tier-2 object index instead.
/// </summary>
public sealed class JsonCacheSerializer : ICacheSerializer {
	private static readonly JsonSerializerOptions Options = new() {
		WriteIndented = false
	};

	public void Write<T>(Stream destination, T value) {
		JsonSerializer.Serialize(destination, value, Options);
	}

	public T? Read<T>(Stream source) {
		return JsonSerializer.Deserialize<T>(source, Options);
	}
}