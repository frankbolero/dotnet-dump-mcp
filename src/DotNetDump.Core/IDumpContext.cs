using Microsoft.Diagnostics.Runtime;
using System;

namespace DotNetDump.Core
{
    public interface IDumpContext : IDisposable
    {
        DataTarget DataTarget { get; }
        ClrRuntime Runtime { get; }
        ClrHeap Heap { get; }
        void Initialize(string dumpPath, string? dacPath = null);
    }
}
