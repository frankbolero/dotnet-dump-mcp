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
			IsUserCode = !IsSystemModule(m.Name)
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

	/// <summary>
	/// Whether a module is part of the runtime rather than the application.
	/// <para>
	/// Matched on the assembly's simple name, not the full path: a substring test over the path hides
	/// any application assembly that merely lives under a directory containing "System." or
	/// "Microsoft.", and hides first-party assemblies legitimately named <c>Microsoft.*</c>.
	/// </para>
	/// </summary>
	internal static bool IsSystemModule(string? path) {
		if (string.IsNullOrEmpty(path))
			return false;

		string fileName = System.IO.Path.GetFileName(path);
		if (string.IsNullOrEmpty(fileName))
			fileName = path;

		if (fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
			 fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase))
			return true;

		// The shared framework ships as System.* / Microsoft.* assemblies from the runtime directory.
		// Requiring both the name prefix and the runtime location avoids catching an application
		// assembly that simply happens to be called Microsoft.Something.dll.
		bool frameworkName = fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
			|| fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);

		if (!frameworkName)
			return false;

		string directory = (System.IO.Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
		bool runtimeDirectory = directory.Contains("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)
			|| directory.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase)
			|| directory.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase)
			|| directory.Contains("/shared/", StringComparison.OrdinalIgnoreCase);

		// A dynamic or path-less module keeps the old name-only behaviour.
		return runtimeDirectory || directory.Length == 0;
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