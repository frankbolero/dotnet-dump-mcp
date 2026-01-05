namespace DotNetDump.Core.Models;

public class HeapVerificationResult {
	public bool IsValid { get; set; }
	public int ObjectsChecked { get; set; }
	public int ErrorsFound { get; set; }
	public List<HeapVerificationError> Errors { get; set; } = new List<HeapVerificationError>();
	public string Summary { get; set; } = string.Empty;
}

public class HeapVerificationError {
	public ulong Address { get; set; }
	public string ErrorType { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string? ObjectType { get; set; }
}