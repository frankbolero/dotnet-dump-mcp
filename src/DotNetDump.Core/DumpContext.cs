using Microsoft.Diagnostics.Runtime;
using System;
using System.IO;
using System.Linq;

namespace DotNetDump.Core
{
    public class DumpContext : IDumpContext
    {
        private DataTarget? _dataTarget;
        private ClrRuntime? _runtime;

        public DataTarget DataTarget => _dataTarget ?? throw new InvalidOperationException("Context not initialized.");
        public ClrRuntime Runtime => _runtime ?? throw new InvalidOperationException("Context not initialized.");
        public ClrHeap Heap => Runtime.Heap;

        public void Initialize(string dumpPath, string? dacPath = null)
        {
            if (!File.Exists(dumpPath))
                throw new FileNotFoundException("Dump file not found.", dumpPath);

            _dataTarget = DataTarget.LoadDump(dumpPath);
            
            ClrInfo? clrInfo = _dataTarget.ClrVersions.FirstOrDefault();
            if (clrInfo == null)
                throw new InvalidOperationException("No CLR Runtime found in dump.");

            try
            {
                if (!string.IsNullOrEmpty(dacPath))
                {
                    _runtime = clrInfo.CreateRuntime(dacPath, ignoreMismatch: true);
                }
                else
                {
                    _runtime = clrInfo.CreateRuntime();
                }
            }
            catch (Exception)
            {
                // Fallback logic for local development if DAC is missing
                // In production (Docker), entrypoint.sh should have fetched it.
                // This is a safety valve.
                string fallbackDac = "/usr/local/share/dotnet/shared/Microsoft.NETCore.App/9.0.11/libmscordaccore.dylib";
                if (File.Exists(fallbackDac))
                {
                    _runtime = clrInfo.CreateRuntime(fallbackDac, ignoreMismatch: true);
                }
                else
                {
                    throw;
                }
            }
        }

        public void Dispose()
        {
            _runtime?.Dispose();
            _dataTarget?.Dispose();
        }
    }
}
