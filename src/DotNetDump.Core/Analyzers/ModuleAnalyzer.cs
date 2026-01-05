using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers;

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

	public ModuleDetails GetModuleDetails(ulong address) {
		var runtime = GetRuntime();
		var module = runtime.EnumerateModules().FirstOrDefault(m => m.ImageBase == address || m.MetadataAddress == address);

		if (module == null)
			throw new ArgumentException($"No module found at address {address:X}");

		// Count types in this module by checking heap objects
		int typeCount = 0;
		var seenTypes = new HashSet<string>();
		foreach (var obj in runtime.Heap.EnumerateObjects().Take(10000)) { // Sample first 10k objects
			if (obj.Type?.Module == module) {
				if (obj.Type.Name != null && seenTypes.Add(obj.Type.Name)) {
					typeCount++;
				}
			}
		}

		return new ModuleDetails {
			Address = address,
			Name = module.Name ?? "<unknown>",
			AssemblyName = module.AssemblyName,
			ImageBase = module.ImageBase,
			Size = module.Size,
			MetadataAddress = module.MetadataAddress,
			MetadataLength = (int)module.MetadataLength,
			AssemblyId = module.ImageBase, // Use ImageBase as pseudo-AssemblyId since AssemblyId not available in ClrMD
			IsDynamic = module.IsDynamic,
			IsFileLayout = false, // Not available in ClrMD v3
			TypeCount = typeCount
		};
	}

	public AssemblyDetails GetAssemblyDetails(ulong assemblyId) {
		var runtime = GetRuntime();

		// Since AssemblyId is not available in ClrMD, we use ImageBase as the identifier
		// Find the module with this ImageBase first
		var targetModule = runtime.EnumerateModules().FirstOrDefault(m => m.ImageBase == assemblyId);

		if (targetModule == null)
			throw new ArgumentException($"No module found with address {assemblyId:X}");

		// Get all modules with the same assembly name
		var assemblyName = targetModule.AssemblyName;
		var modules = runtime.EnumerateModules()
			.Where(m => m.AssemblyName == assemblyName)
			.ToList();

		return new AssemblyDetails {
			AssemblyId = assemblyId,
			Name = assemblyName ?? "<unknown>",
			IsDynamic = targetModule.IsDynamic,
			Modules = modules.Select(m => m.Name ?? "<unknown>").ToList()
		};
	}

	public Name2EEResult Name2EE(string moduleName, string typeName) {
		var runtime = GetRuntime();

		// Find module
		var module = runtime.EnumerateModules()
			.FirstOrDefault(m => m.Name?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false);

		if (module == null)
			throw new ArgumentException($"Module '{moduleName}' not found");

		// Try to find type
		var type = module.GetTypeByName(typeName);

		if (type == null)
			throw new ArgumentException($"Type '{typeName}' not found in module '{moduleName}'");

		var result = new Name2EEResult {
			ModuleName = module.Name,
			TypeName = type.Name,
			MethodTable = type.MethodTable,
			EEClass = type.MethodTable // In ClrMD these are same
		};

		// If typeName contains a method (e.g., "MyClass.MyMethod"), try to find methods
		if (typeName.Contains('.') || typeName.Contains("::")) {
			var methodName = typeName.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
			if (methodName != null) {
				foreach (var method in type.Methods.Where(m => m.Name?.Equals(methodName, StringComparison.OrdinalIgnoreCase) ?? false)) {
					result.Methods.Add(new MethodDescInfo {
						MethodDesc = method.MethodDesc,
						MethodTable = type.MethodTable,
						MethodName = method.Name ?? "<unknown>",
						TypeName = type.Name,
						ModuleName = module.Name,
						Signature = method.Signature,
						NativeCode = method.NativeCode,
						IsJitted = method.NativeCode != 0,
						IsGeneric = method.Name?.Contains('<') ?? false,
						MetadataToken = method.MetadataToken
					});
				}
				result.MethodName = methodName;
			}
		}

		return result;
	}

	public MethodDescInfo GetMethodByIP(ulong instructionPointer) {
		var runtime = GetRuntime();
		var method = runtime.GetMethodByInstructionPointer(instructionPointer);

		if (method == null)
			throw new ArgumentException($"No method found at instruction pointer {instructionPointer:X}");

		return new MethodDescInfo {
			MethodDesc = method.MethodDesc,
			MethodTable = method.Type?.MethodTable ?? 0,
			MethodName = method.Name ?? "<unknown>",
			TypeName = method.Type?.Name,
			ModuleName = method.Type?.Module?.Name,
			Signature = method.Signature,
			NativeCode = method.NativeCode,
			IsJitted = method.NativeCode != 0,
			IsGeneric = method.Name?.Contains('<') ?? false,
			MetadataToken = method.MetadataToken
		};
	}
}