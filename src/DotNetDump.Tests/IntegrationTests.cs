using DotNetDump.Core;
using DotNetDump.Core.Analyzers;
using DotNetDump.Core.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DotNetDump.Tests
{
    public class IntegrationTests : IDisposable
    {
        private readonly string _dumpPath;
        private readonly DumpContext _context;

        public IntegrationTests()
        {
            // Use the provided sample dump
            _dumpPath = Path.GetFullPath("../../../../../dumps/core_20251212_112511");
            _context = new DumpContext();
            
            if (File.Exists(_dumpPath))
            {
                _context.Initialize(_dumpPath);
            }
        }

        [Fact]
        public void DumpContext_InitializesCorrecty()
        {
            if (!File.Exists(_dumpPath)) return; // Skip if dump missing

            Assert.NotNull(_context.DataTarget);
            Assert.NotNull(_context.Runtime);
            Assert.NotNull(_context.Heap);
        }

        [Fact]
        public void HeapAnalyzer_ReturnsStatistics()
        {
            if (!File.Exists(_dumpPath)) return;

            var analyzer = new HeapAnalyzer(_context);
            var stats = analyzer.GetHeapStatistics(new QueryParameters { Limit = 10 }).ToList();

            Assert.NotEmpty(stats);
            Assert.Contains(stats, s => s.TypeName == "System.String");
        }

        [Fact]
        public void ThreadAnalyzer_GroupsStacks()
        {
            if (!File.Exists(_dumpPath)) return;

            var analyzer = new ThreadAnalyzer(_context);
            var groups = analyzer.GetStackTraceGroups().ToList();

            Assert.NotEmpty(groups);
            Assert.True(groups.First().ThreadCount > 0);
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
