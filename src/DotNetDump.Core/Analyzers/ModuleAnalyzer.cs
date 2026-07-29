using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Filtering;
using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

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

	/// <summary>
	/// Loaded modules. <paramref name="includeSystem"/> is a scope rather than a filter — it decides
	/// which modules are under consideration at all, so <see cref="PagedResult{T}.TotalAvailable"/>
	/// counts what it admitted. "12 of 40 user modules" is the honest reading, not "12 of 400".
	/// </summary>
	public PagedResult<DotNetDump.Core.Models.ModuleInfo> GetModules(QueryParameters parameters, bool includeSystem = false) {
		parameters.Filter.EnsureSupported("clrmodules", ModuleInfoFilter.Honored);

		var runtime = GetRuntime();
		IEnumerable<DotNetDump.Core.Models.ModuleInfo> modules = runtime.EnumerateModules().Select(m => new DotNetDump.Core.Models.ModuleInfo {
			Name = m.Name,
			ImageBase = m.ImageBase,
			Size = m.Size,
			IsUserCode = !ModuleClassifier.IsSystemModule(m.Name)
		});

		if (!includeSystem) {
			modules = modules.Where(m => m.IsUserCode);
		}

		// includeSystem is a scope, not a filter -- it decides what is under consideration at all
		// (see the doc comment above). FilterSpec is applied on top of that scope, and
		// TotalAvailable below reflects both together.
		var modulesInScope = modules.ToList();
		var inScope = modulesInScope.Where(m => ModuleInfoFilter.Matches(m, parameters.Filter)).ToList();

		// Sorting
		IEnumerable<DotNetDump.Core.Models.ModuleInfo> sorted = inScope;
		if (parameters.SortBy?.ToLower() == "size") {
			sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(m => m.Size) : sorted.OrderByDescending(m => m.Size);
		} else if (parameters.SortBy?.ToLower() == "name") {
			sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(m => m.Name) : sorted.OrderByDescending(m => m.Name);
		} else {
			sorted = parameters.SortDirection == SortDirection.Asc ? sorted.OrderBy(m => m.ImageBase) : sorted.OrderByDescending(m => m.ImageBase);
		}

		var page = sorted.Skip(parameters.Offset).Take(parameters.Limit).ToList();
		return new PagedResult<DotNetDump.Core.Models.ModuleInfo>(page, inScope.Count, modulesInScope.Count, parameters.Offset, parameters.Limit);
	}

	public ModuleDetails GetModuleDetails(ulong address) {
		var runtime = GetRuntime();
		var module = runtime.EnumerateModules().FirstOrDefault(m => m.ImageBase == address || m.MetadataAddress == address);

		if (module == null)
			throw new ArgumentException($"No module found at address {address:X}");

		// Exact type count from the module's TypeDef map, rather than inferring it from a sample of
		// heap objects — a sample only sees types that happen to have live instances.
		int typeCount = module.EnumerateTypeDefToMethodTableMap().Count();
		int typesWithStatics = module.EnumerateTypesWithStaticFields().Count();

		return new ModuleDetails {
			Address = address,
			Name = module.Name ?? "<unknown>",
			AssemblyName = module.AssemblyName,
			ImageBase = module.ImageBase,
			Size = module.Size,
			MetadataAddress = module.MetadataAddress,
			MetadataLength = (int)module.MetadataLength,
			AssemblyAddress = module.AssemblyAddress,
			IsDynamic = module.IsDynamic,
			IsPEFile = module.IsPEFile,
			Layout = module.Layout.ToString(),
			AppDomainName = module.AppDomain?.Name,
			TypeCount = typeCount,
			TypesWithStaticFieldsCount = typesWithStatics
		};
	}

	/// <summary>
	/// Looks up an assembly. Accepts the runtime's Assembly address (what SOS calls the assembly id)
	/// and also a module ImageBase, since dumps and tools quote both.
	/// </summary>
	public AssemblyDetails GetAssemblyDetails(ulong assemblyAddress) {
		var runtime = GetRuntime();
		var allModules = runtime.EnumerateModules().ToList();

		var targetModule = allModules.FirstOrDefault(m => m.AssemblyAddress == assemblyAddress)
			?? allModules.FirstOrDefault(m => m.ImageBase == assemblyAddress);

		if (targetModule == null)
			throw new ArgumentException(
				$"No assembly or module found at {assemblyAddress:X}. Expected an Assembly address " +
				"(from dump_module) or a module ImageBase (from clr_modules).");

		// Group by the runtime's assembly address where we have one; fall back to assembly name.
		var modules = targetModule.AssemblyAddress != 0
			? allModules.Where(m => m.AssemblyAddress == targetModule.AssemblyAddress).ToList()
			: allModules.Where(m => m.AssemblyName == targetModule.AssemblyName).ToList();

		return new AssemblyDetails {
			AssemblyAddress = targetModule.AssemblyAddress,
			Name = targetModule.AssemblyName ?? "<unknown>",
			IsDynamic = targetModule.IsDynamic,
			AppDomainName = targetModule.AppDomain?.Name,
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

		// The documented form is <module>!<Type> or <module>!<Type>.<Method>. Resolving the whole
		// string as a type first and only then splitting off a method never worked: for
		// "Program.Main" the type lookup fails and throws, and for a namespaced type such as
		// "System.String" the trailing segment is not a method at all.
		var (type, methodName) = ResolveTypeAndMethod(module, typeName);

		if (type == null) {
			throw new ArgumentException(
				$"Type '{typeName}' not found in module '{moduleName}'. Expected a fully-qualified " +
				"type name, optionally followed by .MethodName.");
		}

		var result = new Name2EEResult {
			ModuleName = module.Name,
			TypeName = type.Name,
			MethodTable = type.MethodTable,
			// ClrMD does not surface EEClass separately from the MethodTable.
			EEClass = type.MethodTable
		};

		if (methodName == null)
			return result;

		result.MethodName = methodName;

		foreach (var method in type.Methods.Where(m => m.Name?.Equals(methodName, StringComparison.Ordinal) ?? false)) {
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

		if (result.Methods.Count == 0)
			throw new ArgumentException($"Type '{type.Name}' has no method named '{methodName}'.");

		return result;
	}

	/// <summary>
	/// Resolves <paramref name="name"/> as a type, or as a type plus trailing method name. The full
	/// string is tried as a type first, so a namespaced type is never mistaken for Type.Method.
	/// </summary>
	private static (ClrType? Type, string? MethodName) ResolveTypeAndMethod(ClrModule module, string name) {
		string normalized = name.Replace("::", ".");

		var exact = module.GetTypeByName(normalized);
		if (exact != null)
			return (exact, null);

		int lastDot = normalized.LastIndexOf('.');
		if (lastDot > 0 && lastDot < normalized.Length - 1) {
			string candidateType = normalized.Substring(0, lastDot);
			string candidateMethod = normalized.Substring(lastDot + 1);

			var type = module.GetTypeByName(candidateType);
			if (type != null)
				return (type, candidateMethod);
		}

		return (null, null);
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