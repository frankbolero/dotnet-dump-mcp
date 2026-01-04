namespace DotNetDump.Core.Models
{
    public class GCRootInfo
    {
        public ulong Address { get; set; }
        public string? Kind { get; set; }
        public string? RootName { get; set; }
        public ulong ObjectAddress { get; set; }
        public int ManagedThreadId { get; set; }
        public uint OSThreadId { get; set; }
    }
}
