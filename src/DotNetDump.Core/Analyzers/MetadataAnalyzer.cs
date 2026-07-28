using System;
using System.Collections.Generic;
using System.Linq;

using DotNetDump.Core.Models;
using DotNetDump.Core.Utilities;

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
			// ClrMD does not surface the EEClass address separately.
			EEClass = type.MethodTable,
			TypeName = type.Name ?? "<unknown>",
			ModuleName = type.Module?.Name,
			BaseSize = (ulong)type.StaticSize,
			ComponentSize = type.ComponentSize,
			MethodCount = type.Methods.Length,
			MetadataToken = type.MetadataToken,
			IsValueType = type.IsValueType,
			IsInterface = TypeFlagsDecoder.IsInterface(type.TypeAttributes),
			IsAbstract = TypeFlagsDecoder.IsAbstract(type.TypeAttributes),
			IsSealed = TypeFlagsDecoder.IsSealed(type.TypeAttributes),
			IsEnum = type.IsEnum,
			IsArray = type.IsArray,
			IsString = type.IsString,
			IsFinalizable = type.IsFinalizable,
			ContainsPointers = type.ContainsPointers,
			Visibility = TypeFlagsDecoder.Visibility(type.TypeAttributes),
			BaseTypeName = type.BaseType?.Name
		};

		foreach (var iface in type.EnumerateInterfaces()) {
			if (!string.IsNullOrEmpty(iface.Name))
				info.Interfaces.Add(iface.Name!);
		}

		return info;
	}

	/// <summary>
	/// Resolves a MethodDesc handle. <c>ClrRuntime.GetMethodByHandle</c> is a direct DAC lookup; the
	/// alternative of scanning heap objects for a type that declares the method only works when the
	/// declaring type happens to have a live instance, which excludes static classes, entry points and
	/// most services.
	/// </summary>
	public MethodDescInfo GetMethodDesc(ulong methodDesc) {
		var runtime = GetRuntime();
		var method = runtime.GetMethodByHandle(methodDesc);

		if (method == null)
			throw new ArgumentException($"No method found for MethodDesc {methodDesc:X}.");

		return new MethodDescInfo {
			MethodDesc = methodDesc,
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

	public ClassInfo GetClass(ulong eeClass) {
		var runtime = GetRuntime();

		// In ClrMD, EEClass and MethodTable are closely related
		// Try to find the type by treating the address as a MethodTable
		var type = runtime.GetTypeByMethodTable(eeClass);

		if (type == null) {
			throw new ArgumentException($"No class found for EEClass {eeClass:X}");
		}

		const int MaxInstanceFields = 50;
		const int MaxStaticFields = 20;
		const int MaxMethods = 50;

		var info = new ClassInfo {
			EEClass = eeClass,
			MethodTable = type.MethodTable,
			TypeName = type.Name ?? "<unknown>",
			ModuleName = type.Module?.Name,
			FieldCount = type.Fields.Length,
			StaticFieldCount = type.StaticFields.Length,
			ThreadStaticFieldCount = type.ThreadStaticFields.Length,
			MethodCount = type.Methods.Length
		};

		info.IsTruncated = type.Fields.Length > MaxInstanceFields
			|| type.StaticFields.Length > MaxStaticFields
			|| type.Methods.Length > MaxMethods;

		foreach (var field in type.Fields.Take(MaxInstanceFields)) {
			info.Fields.Add(new FieldMetadata {
				Name = field.Name ?? "<unnamed>",
				TypeName = field.Type?.Name ?? "Unknown",
				Offset = field.Offset,
				IsStatic = false,
				Size = field.Size
			});
		}

		// Static fields carry a current value, which is usually the reason to look at a static at all.
		var appDomain = _context.Runtime?.AppDomains.FirstOrDefault();
		foreach (var field in type.StaticFields.Take(MaxStaticFields)) {
			info.Fields.Add(new FieldMetadata {
				Name = field.Name ?? "<unnamed>",
				TypeName = field.Type?.Name ?? "Unknown",
				Offset = field.Offset,
				IsStatic = true,
				Size = field.Size,
				Value = ReadStaticValue(field, appDomain)
			});
		}

		foreach (var method in type.Methods.Take(MaxMethods)) {
			if (method.Name != null) {
				info.Methods.Add(method.Name);
			}
		}

		return info;
	}

	private static string? ReadStaticValue(ClrStaticField field, ClrAppDomain? appDomain) {
		if (appDomain == null)
			return null;

		try {
			if (!field.IsInitialized(appDomain))
				return "(not initialized)";

			if (field.Type?.IsString == true)
				return field.ReadString(appDomain) is { } s ? $"\"{s}\"" : "null";

			if (field.IsObjectReference) {
				var obj = field.ReadObject(appDomain);
				return obj.IsNull ? "null" : $"{obj.Address:X} <{obj.Type?.Name}>";
			}

			return field.ElementType switch {
				ClrElementType.Boolean => field.Read<bool>(appDomain).ToString(),
				ClrElementType.Char => field.Read<char>(appDomain).ToString(),
				ClrElementType.Int8 => field.Read<sbyte>(appDomain).ToString(),
				ClrElementType.UInt8 => field.Read<byte>(appDomain).ToString(),
				ClrElementType.Int16 => field.Read<short>(appDomain).ToString(),
				ClrElementType.UInt16 => field.Read<ushort>(appDomain).ToString(),
				ClrElementType.Int32 => field.Read<int>(appDomain).ToString(),
				ClrElementType.UInt32 => field.Read<uint>(appDomain).ToString(),
				ClrElementType.Int64 => field.Read<long>(appDomain).ToString(),
				ClrElementType.UInt64 => field.Read<ulong>(appDomain).ToString(),
				ClrElementType.Float => field.Read<float>(appDomain).ToString(),
				ClrElementType.Double => field.Read<double>(appDomain).ToString(),
				_ => null
			};
		} catch (Exception) {
			return "(unreadable)";
		}
	}
}