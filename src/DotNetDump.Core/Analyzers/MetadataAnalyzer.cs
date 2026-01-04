using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Models;

using Microsoft.Diagnostics.Runtime;

namespace DotNetDump.Core.Analyzers;

public class MetadataAnalyzer {
	private readonly IDumpContext _context;

	public MetadataAnalyzer(IDumpContext context) {
		_context = context;
	}

	private ClrRuntime GetRuntime() {
		if (!_context.IsLoaded || _context.Runtime == null)
			throw new InvalidOperationException("No dump loaded. Please use 'load_dump' tool first.");
		return _context.Runtime;
	}

	public MethodTableInfo GetMethodTable(ulong methodTable) {
		var runtime = GetRuntime();
		var type = runtime.GetTypeByMethodTable(methodTable);

		if (type == null)
			throw new ArgumentException($"No type found for MethodTable {methodTable:X}");

		var info = new MethodTableInfo {
			MethodTable = methodTable,
			EEClass = type.MethodTable, // In ClrMD, this is the MT itself
			TypeName = type.Name ?? "<unknown>",
			ModuleName = type.Module?.Name,
			BaseSize = (ulong)type.StaticSize,
			MethodCount = type.Methods.Count(),
			IsValueType = type.IsValueType,
			IsInterface = false, // Not directly available in ClrMD
			IsAbstract = false, // Not directly available in ClrMD
			IsSealed = false, // Not directly available in ClrMD
			BaseTypeName = type.BaseType?.Name
		};

		return info;
	}

	public MethodDescInfo GetMethodDesc(ulong methodDesc) {
		var runtime = GetRuntime();

		// Search through modules and types to find the matching MethodDesc
		foreach (var module in runtime.EnumerateModules()) {
			// Get types from the heap for this module
			foreach (var obj in runtime.Heap.EnumerateObjects()) {
				var type = obj.Type;
				if (type?.Module == module) {
					foreach (var method in type.Methods) {
						if (method.MethodDesc == methodDesc) {
							return new MethodDescInfo {
								MethodDesc = methodDesc,
								MethodTable = method.Type?.MethodTable ?? 0,
								MethodName = method.Name ?? "<unknown>",
								TypeName = method.Type?.Name,
								ModuleName = method.Type?.Module?.Name,
								Signature = method.Signature,
								NativeCode = method.NativeCode,
								IsJitted = method.NativeCode != 0,
								IsGeneric = method.Name?.Contains('<') ?? false, // Approximation
								MetadataToken = method.MetadataToken
							};
						}
					}
				}
			}
		}

		throw new ArgumentException($"No method found for MethodDesc {methodDesc:X}. This operation scans types in the heap which may take time.");
	}

	public ClassInfo GetClass(ulong eeClass) {
		var runtime = GetRuntime();

		// In ClrMD, EEClass and MethodTable are closely related
		// Try to find the type by treating the address as a MethodTable
		var type = runtime.GetTypeByMethodTable(eeClass);

		if (type == null) {
			throw new ArgumentException($"No class found for EEClass {eeClass:X}");
		}

		// Count instance vs static fields
		int instanceFieldCount = 0;
		int staticFieldCount = 0;
		foreach (var field in type.Fields) {
			if (field.IsObjectReference || field.IsValueType || field.IsPrimitive) {
				instanceFieldCount++;
			}
		}
		foreach (var field in type.StaticFields) {
			staticFieldCount++;
		}

		var info = new ClassInfo {
			EEClass = eeClass,
			MethodTable = type.MethodTable,
			TypeName = type.Name ?? "<unknown>",
			ModuleName = type.Module?.Name,
			FieldCount = instanceFieldCount,
			StaticFieldCount = staticFieldCount,
			MethodCount = type.Methods.Count()
		};

		// Add instance field information
		foreach (var field in type.Fields.Take(50)) { // Limit to first 50 fields
			info.Fields.Add(new FieldMetadata {
				Name = field.Name ?? "<unnamed>",
				TypeName = field.Type?.Name ?? "Unknown",
				Offset = field.Offset,
				IsStatic = false,
				Size = field.Size
			});
		}

		// Add static field information
		foreach (var field in type.StaticFields.Take(20)) {
			info.Fields.Add(new FieldMetadata {
				Name = field.Name ?? "<unnamed>",
				TypeName = field.Type?.Name ?? "Unknown",
				Offset = 0,
				IsStatic = true,
				Size = field.Size
			});
		}

		// Add method names
		foreach (var method in type.Methods.Take(50)) { // Limit to first 50 methods
			if (method.Name != null) {
				info.Methods.Add(method.Name);
			}
		}

		return info;
	}
}