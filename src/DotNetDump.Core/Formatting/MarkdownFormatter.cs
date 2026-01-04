using DotNetDump.Core.Models;
using System.Collections.Generic;
using System.Text;

namespace DotNetDump.Core.Formatting
{
    public static class MarkdownFormatter
    {
        public static string FormatHeapStatistics(IEnumerable<HeapStatItem> stats)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Count | Total Size | Type |");
            sb.AppendLine("|-------|------------|------|");
            foreach (var item in stats)
            {
                sb.AppendLine($"| {item.Count:N0} | {item.TotalSize:N0} | {item.TypeName} |");
            }
            return sb.ToString();
        }

        public static string FormatHeapObjects(IEnumerable<HeapObjectItem> objects)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Address | Size | Type |");
            sb.AppendLine("|---------|------|------|");
            foreach (var item in objects)
            {
                sb.AppendLine($"| {item.Address:X16} | {item.Size:N0} | {item.TypeName} |");
            }
            return sb.ToString();
        }

        public static string FormatThreads(IEnumerable<ThreadInfo> threads)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Mgd ID | OS ID | State | Exception |");
            sb.AppendLine("|--------|-------|-------|-----------|");
            foreach (var item in threads)
            {
                string state = item.IsAlive ? "Alive" : "Dead";
                string exception = item.ExceptionType ?? "(none)";
                sb.AppendLine($"| {item.ManagedThreadId} | {item.OSThreadId:X} | {state} | {exception} |");
            }
            return sb.ToString();
        }

        public static string FormatModules(IEnumerable<ModuleInfo> modules)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Address | Size | Name |");
            sb.AppendLine("|---------|------|------|");
            foreach (var item in modules)
            {
                sb.AppendLine($"| {item.ImageBase:X16} | {item.Size:X} | {item.Name} |");
            }
            return sb.ToString();
        }

        public static string FormatStackGroups(IEnumerable<StackGroup> groups)
        {
            var sb = new StringBuilder();
            int i = 1;
            foreach (var group in groups)
            {
                sb.AppendLine($"### Group {i++} ({group.ThreadCount} Threads)");
                sb.AppendLine($"**Managed Thread IDs:** {string.Join(", ", group.ManagedThreadIds)}");
                sb.AppendLine("**Stack:**");
                sb.AppendLine("```text");
                foreach (var frame in group.Frames)
                {
                    sb.AppendLine(frame);
                }
                sb.AppendLine("```");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
