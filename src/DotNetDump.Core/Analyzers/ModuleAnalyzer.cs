using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers {
	public class ModuleAnalyzer {
		private readonly IDumpContext _context;

		public ModuleAnalyzer(IDumpContext context) {
			_context = context;
		}

		private ClrRuntime GetRuntime() {
			if (!_context.IsLoaded || _context.Runtime == null)
				throw new InvalidOperationException("No dump loaded. Please use 'load_dump' tool first.");
			return _context.Runtime;
		}

		public IEnumerable<DotNetDump.Core.Models.ModuleInfo> GetModules(QueryParameters parameters, bool includeSystem = false) {
			var runtime = GetRuntime();
			var modules = runtime.EnumerateModules().Select(m => new DotNetDump.Core.Models.ModuleInfo {
				Name = m.Name,
				ImageBase = m.ImageBase,
				Size = m.Size,
				IsUserCode = !IsSystemModule(m.Name ?? "")
			});

			if (!includeSystem) {
				modules = modules.Where(m => m.IsUserCode);
			}

			// Sorting
			if (parameters.SortBy?.ToLower() == "size") {
				modules = parameters.SortDirection == SortDirection.Asc ? modules.OrderBy(m => m.Size) : modules.OrderByDescending(m => m.Size);
			} else if (parameters.SortBy?.ToLower() == "name") {
				modules = parameters.SortDirection == SortDirection.Asc ? modules.OrderBy(m => m.Name) : modules.OrderByDescending(m => m.Name);
			} else {
				modules = parameters.SortDirection == SortDirection.Asc ? modules.OrderBy(m => m.ImageBase) : modules.OrderByDescending(m => m.ImageBase);
			}

			return modules.Skip(parameters.Offset).Take(parameters.Limit);
		}

		private bool IsSystemModule(string name) {
			return name.Contains("System.", StringComparison.OrdinalIgnoreCase) ||
					 name.Contains("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
					 name.EndsWith("mscorlib.dll", StringComparison.OrdinalIgnoreCase);
		}
	}
}