namespace MaxonSharp.Compiler;

/// <summary>
/// Extracts exported symbols (types, functions, enums) from a parsed AST.
/// Only symbols marked with 'export' are extracted.
/// </summary>
public static class MetadataExtractor {
	/// <summary>
	/// Extracts all exported symbols from a program AST.
	/// </summary>
	public static ExternalSymbolsCollection Extract(ProgramAst program, string sourcePath) {
		var collection = new ExternalSymbolsCollection();

		// Extract exported types
		foreach (var type in program.Types) {
			if (type.IsExport) {
				collection.Types.Add(ExtractType(type, sourcePath));
			}
		}

		// Extract exported functions
		foreach (var func in program.Functions) {
			if (func.IsExport) {
				collection.Functions.Add(ExtractFunction(func, sourcePath));
			}
		}

		// Extract exported enums
		foreach (var enumDecl in program.Enums) {
			if (enumDecl.IsExport) {
				collection.Enums.Add(ExtractEnum(enumDecl, sourcePath));
			}
		}

		return collection;
	}

	private static ExternalTypeInfo ExtractType(TypeDecl type, string sourcePath) {
		var fields = type.Fields.Select(f => new ExternalFieldInfo(
			f.Name,
			GetTypeName(f.Type)
		)).ToList();

		return new ExternalTypeInfo(type.Name, sourcePath, fields);
	}

	private static ExternalFuncSignature ExtractFunction(FunctionDecl func, string sourcePath) {
		var parameters = func.Params.Select(p => new ExternalParamInfo(
			p.Name,
			GetTypeName(p.Type)
		)).ToList();

		return new ExternalFuncSignature(
			func.Name,
			sourcePath,
			parameters,
			func.ReturnType != null ? GetTypeName(func.ReturnType) : null
		);
	}

	private static ExternalEnumInfo ExtractEnum(EnumDecl enumDecl, string sourcePath) {
		var variants = enumDecl.Members.Select(m => {
			var payloadFields = m.AssociatedValues.Select(av => new ExternalFieldInfo(
				av.Name,
				GetTypeName(av.Type)
			)).ToList();
			return new ExternalEnumVariant(m.Name, payloadFields);
		}).ToList();

		return new ExternalEnumInfo(enumDecl.Name, sourcePath, variants);
	}

	private static string GetTypeName(TypeRef typeRef) {
		return typeRef switch {
			SimpleTypeRef simple => simple.Name,
			GenericTypeRef generic => $"{generic.BaseName}<{string.Join(", ", generic.TypeArgs)}>",
			_ => "unknown"
		};
	}
}
