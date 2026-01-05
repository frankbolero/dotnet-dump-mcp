namespace DotNetDump.Core.Models;

public class MethodDescInfo {
	public ulong MethodDesc { get; set; }
	public ulong MethodTable { get; set; }
	public string MethodName { get; set; } = string.Empty;
	public string? TypeName { get; set; }
	public string? ModuleName { get; set; }
	public string? Signature { get; set; }
	public ulong NativeCode { get; set; }
	public bool IsJitted { get; set; }
	public bool IsGeneric { get; set; }
	public int MetadataToken { get; set; }
}