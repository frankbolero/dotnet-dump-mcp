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

        public DataTarget? DataTarget => _dataTarget;
        public ClrRuntime? Runtime => _runtime;
        public ClrHeap? Heap => _runtime?.Heap;
        public bool IsLoaded => _runtime != null;

        public void Initialize(string dumpPath, string? dacPath = null) => Load(dumpPath, dacPath); // Backwards compat if needed, but we'll prefer Load

        public void Load(string dumpPath, string? dacPath = null)
        {
            if (IsLoaded)
            {
                Unload();
            }

            if (!File.Exists(dumpPath))
                throw new FileNotFoundException("Dump file not found.", dumpPath);

            // Attempt to fetch DAC if not provided and not found locally?
            // For now, we assume the environment (container) has what it needs or the user provides dacPath.
            // In the container model, 'dotnet-symbol' might need to be run *before* this method is called 
            // if we want auto-downloading inside the C# app. 
            // Ideally, we might want to shell out to dotnet-symbol here if it fails?
            // For now, let's keep the core logic simple: Load what exists.

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

        public void Unload()
        {
            _runtime?.Dispose();
            _runtime = null;
            
            _dataTarget?.Dispose();
            _dataTarget = null;
        }

        public void Dispose()
        {
            Unload();
        }
    }
}