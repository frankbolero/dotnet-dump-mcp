using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DotNetDump.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configure logging to stderr to avoid interfering with stdio transport
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            // Register Core Services
            builder.Services.AddSingleton<IDumpContext, DumpContext>();
            builder.Services.AddTransient<HeapAnalyzer>();
            builder.Services.AddTransient<ThreadAnalyzer>();
            builder.Services.AddTransient<ModuleAnalyzer>();

            // Add MCP Server
            builder.Services
                .AddMcpServer(options => 
                {
                    options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                    {
                        Name = "dotnet-dump-mcp-server",
                        Version = "1.0.0"
                    };
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var app = builder.Build();

            // Optional: If DUMP_PATH is provided, try to load it immediately for convenience
            string? dumpPath = Environment.GetEnvironmentVariable("DUMP_PATH") ?? args.FirstOrDefault();
            if (!string.IsNullOrEmpty(dumpPath))
            {
                try
                {
                    var context = app.Services.GetRequiredService<IDumpContext>();
                    context.Load(dumpPath, Environment.GetEnvironmentVariable("DAC_PATH"));
                    // Log to stderr so it doesn't break JSON-RPC
                    Console.Error.WriteLine($"[Info] Auto-loaded dump: {dumpPath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Warning] Failed to auto-load dump '{dumpPath}': {ex.Message}");
                }
            }

            await app.RunAsync();
        }
    }
}
