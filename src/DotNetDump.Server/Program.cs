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
            string? dumpPath = Environment.GetEnvironmentVariable("DUMP_PATH") ?? args.FirstOrDefault();

            if (string.IsNullOrEmpty(dumpPath))
            {
                Console.Error.WriteLine("Error: Dump path must be provided via DUMP_PATH environment variable or first argument.");
                Environment.Exit(1);
            }

            var builder = Host.CreateApplicationBuilder(args);

            // Configure logging to stderr to avoid interfering with stdio transport
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            // Register Core Services
            builder.Services.AddSingleton<IDumpContext, DumpContext>(sp =>
            {
                var context = new DumpContext();
                string? dacPath = Environment.GetEnvironmentVariable("DAC_PATH");
                context.Initialize(dumpPath, dacPath);
                return context;
            });

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

            // Force initialization of DumpContext before starting the server
            // to ensure DAC is valid and dump is accessible.
            try 
            {
                _ = app.Services.GetRequiredService<IDumpContext>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize dump context: {ex.Message}");
                Environment.Exit(1);
            }

            await app.RunAsync();
        }
    }
}