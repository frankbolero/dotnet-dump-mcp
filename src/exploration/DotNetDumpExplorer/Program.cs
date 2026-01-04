using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotNetDumpExplorer
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: dotnet run -- <dump_path> [command]");
                Console.WriteLine("Commands: clrstack, clrthreads, dumpheap, clrmodules, etc.");
                return;
            }

            string dumpPath = args[0];
            string command = args.Length > 1 ? args[1].ToLower() : "help";

            if (!File.Exists(dumpPath))
            {
                Console.WriteLine($"Error: Dump file not found at '{dumpPath}'");
                return;
            }

            try
            {
                using (DataTarget dataTarget = DataTarget.LoadDump(dumpPath))
                {
                    ClrInfo clrInfo = dataTarget.ClrVersions.FirstOrDefault();
                    if (clrInfo == null)
                    {
                        Console.WriteLine("No CLR Runtime found in dump.");
                        return;
                    }
                    
                    Console.WriteLine($"Found CLR: {clrInfo.Version}");
                    
                    ClrRuntime runtime = null;
                    try 
                    {
                        runtime = clrInfo.CreateRuntime();
                    }
                    catch 
                    {
                        Console.WriteLine("Default CreateRuntime failed. Trying local DAC with ignoreMismatch...");
                        string localDac = "/usr/local/share/dotnet/shared/Microsoft.NETCore.App/9.0.11/libmscordaccore.dylib";
                        if (File.Exists(localDac))
                        {
                            try
                            {
                                runtime = clrInfo.CreateRuntime(localDac, ignoreMismatch: true);
                            }
                            catch (Exception ex2)
                            {
                                Console.WriteLine($"Failed with local DAC: {ex2.Message}");
                            }
                        }
                    }

                    if (runtime != null)
                    {
                        using (runtime)
                        {
                            ExecuteCommand(command, runtime, args.Skip(2).ToArray());
                        }
                    }
                    else
                    {
                         Console.WriteLine("Could not create ClrRuntime.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void ExecuteCommand(string command, ClrRuntime runtime, string[] args)
        {
            switch (command)
            {
                case "help":
                    PrintHelp();
                    break;
                case "clrstack":
                    RunClrStack(runtime);
                    break;
                case "clrthreads":
                    RunClrThreads(runtime);
                    break;
                case "dumpheap":
                    RunDumpHeap(runtime, args);
                    break;
                case "clrmodules":
                    RunClrModules(runtime);
                    break;
                case "threadpool":
                    RunThreadPool(runtime);
                    break;
                case "dumpobj":
                    if (args.Length > 0 && ulong.TryParse(args[0], System.Globalization.NumberStyles.HexNumber, null, out ulong objAddr))
                        RunDumpObj(runtime, objAddr);
                    else
                        Console.WriteLine("Usage: dumpobj <hex_address>");
                    break;
                 case "eeheap":
                    RunEeHeap(runtime);
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    PrintHelp();
                    break;
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  clrstack      - Dump stack traces");
            Console.WriteLine("  clrthreads    - List managed threads");
            Console.WriteLine("  dumpheap      - List managed objects (use -stat for summary)");
            Console.WriteLine("  clrmodules    - List modules");
            Console.WriteLine("  threadpool    - ThreadPool stats");
            Console.WriteLine("  dumpobj <addr>- Inspect object");
            Console.WriteLine("  eeheap        - Inspect heap segments");
        }

        static void RunClrStack(ClrRuntime runtime)
        {
            foreach (ClrThread thread in runtime.Threads)
            {
                if (!thread.IsAlive) continue;
                Console.WriteLine($"Thread {thread.OSThreadId:X} (Managed: {thread.ManagedThreadId}):");
                foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
                {
                    Console.WriteLine($"  {frame}");
                }
                Console.WriteLine();
            }
        }

        static void RunClrThreads(ClrRuntime runtime)
        {
             Console.WriteLine("OS Thread  Managed ID  Alive  GC Mode");
             Console.WriteLine("------------------------------------------");
            foreach (ClrThread thread in runtime.Threads)
            {
                Console.WriteLine($"{thread.OSThreadId,9:X}  {thread.ManagedThreadId,10}  {thread.IsAlive,5}  {thread.GCMode,-12}");
            }
        }

        static void RunDumpHeap(ClrRuntime runtime, string[] args)
        {
            bool stat = args.Contains("-stat");

            if (stat)
            {
                 var stats = from obj in runtime.Heap.EnumerateObjects()
                            let type = obj.Type
                            group obj by type into g
                            let size = g.Sum(p => (long)p.Size)
                            let count = g.Count()
                            orderby size ascending
                            select new { Type = g.Key, Size = size, Count = count };

                Console.WriteLine("      Count       Size  Type");
                Console.WriteLine("-----------------------------------------------------");
                foreach (var item in stats)
                {
                    string typeName = item.Type?.Name ?? "<unknown>";
                    Console.WriteLine($"{item.Count,11} {item.Size,10}  {typeName}");
                }
            }
            else
            {
                // Limit to first 50 to avoid flooding console in a test tool
                Console.WriteLine("Address           Size  Type");
                Console.WriteLine("-----------------------------------------------------");
                int count = 0;
                foreach (ClrObject obj in runtime.Heap.EnumerateObjects())
                {
                    if (count++ > 50) 
                    {
                        Console.WriteLine("... (output truncated for verification tool)");
                        break;
                    }
                    Console.WriteLine($"{obj.Address:X16} {obj.Size,6}  {obj.Type?.Name}");
                }
            }
        }

        static void RunClrModules(ClrRuntime runtime)
        {
            Console.WriteLine("Address           Size      Name");
            Console.WriteLine("-----------------------------------------------------");
            foreach (ClrModule module in runtime.EnumerateModules())
            {
                Console.WriteLine($"{module.ImageBase,16:X}  {module.Size,8:X}  {module.Name}");
            }
        }

        static void RunThreadPool(ClrRuntime runtime)
        {
             if (runtime.ThreadPool != null)
             {
                Console.WriteLine($"ThreadPool data is available.");
                // Properties TotalThreads, IdleThreads, RunningThreads removed as they caused build errors.
             }
             else
             {
                 Console.WriteLine("ThreadPool info not available.");
             }
        }

        static void RunDumpObj(ClrRuntime runtime, ulong address)
        {
            ClrObject obj = runtime.Heap.GetObject(address);
            if (obj.IsNull)
            {
                Console.WriteLine($"Invalid object at {address:X}");
                return;
            }

            Console.WriteLine($"Address: {obj.Address:X}");
            Console.WriteLine($"Type:    {obj.Type?.Name}");
            Console.WriteLine($"Size:    {obj.Size}");

            if (obj.Type != null)
            {
                foreach (var field in obj.Type.Fields)
                {
                    Console.WriteLine($"Field: {field.Name}");
                    // Value reading logic can be added here
                }
            }
        }
        
        static void RunEeHeap(ClrRuntime runtime)
        {
            Console.WriteLine("Segment Start     End               Size");
            Console.WriteLine("--------------------------------------------");
            foreach (var segment in runtime.Heap.Segments)
            {
                Console.WriteLine($"{segment.Start,16:X}  {segment.End,16:X}  {segment.Length:X}");
            }
        }
    }
}