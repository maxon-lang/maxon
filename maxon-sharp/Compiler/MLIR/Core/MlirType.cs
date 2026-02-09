namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirType {
  public string Name { get; }
  public virtual int SizeInBytes { get; }

  public MlirType(string name, int sizeInBytes) {
    Name = name;
    SizeInBytes = sizeInBytes;
  }

  protected MlirType(string name) {
    Name = name;
  }

  public static MlirType I8 { get; } = new("i8", 1);
  public static MlirType I32 { get; } = new("i32", 4);
  public static MlirType I64 { get; } = new("i64", 8);
  public static MlirType F64 { get; } = new("f64", 8);
  public static MlirType I1 { get; } = new("i1", 1);
  public static MlirType Void { get; } = new("void", 0);
  // Sentinel type for function-typed parameters (higher-order functions)
  public static MlirType Fn { get; } = new("fn", 8);

  /// <summary>
  /// Returns the element size in bytes for the type.
  /// </summary>
  public virtual int ElementSize => SizeInBytes;

  public override string ToString() => Name;

  /// <summary>
  /// Maps an MlirType back to its source-level name for error messages.
  /// </summary>
  public static string FormatAsSourceName(MlirType type) {
    if (type == I64) return "int";
    if (type == F64) return "float";
    if (type == I1) return "bool";
    if (type == I8) return "byte";
    if (type == Void) return "void";
    return type.Name;
  }
}

public class MlirStructField(string name, MlirType type, bool isExported, bool isMutable, MlirAttribute? defaultValue = null) {
  public string Name { get; } = name;
  public MlirType Type { get; } = type;
  public bool IsExported { get; } = isExported;
  public bool IsMutable { get; } = isMutable;
  public MlirAttribute? DefaultValue { get; } = defaultValue;
  public int Offset { get; set; }
}

public class MlirStructType : MlirType {
  public List<MlirStructField> Fields { get; }
  public List<string> AssociatedTypeNames { get; }
  public List<string> ConformingInterfaces { get; }
  public Dictionary<string, long> ConstParams { get; }
  public Dictionary<string, MlirType> TypeParams { get; }

  public MlirStructType(string name, List<MlirStructField> fields, List<string>? associatedTypeNames = null, List<string>? conformingInterfaces = null, Dictionary<string, long>? constParams = null, Dictionary<string, MlirType>? typeParams = null) : base(name, ComputeSize(fields)) {
    Fields = fields;
    AssociatedTypeNames = associatedTypeNames ?? [];
    ConformingInterfaces = conformingInterfaces ?? [];
    ConstParams = constParams ?? [];
    TypeParams = typeParams ?? [];
    int offset = 0;
    foreach (var field in Fields) {
      field.Offset = offset;
      // Nested structs use their actual size; scalar types always use 8 bytes (64-bit slots)
      offset += field.Type is MlirStructType ? field.Type.SizeInBytes : 8;
    }
  }

  private static int ComputeSize(List<MlirStructField> fields) {
    int size = 0;
    foreach (var field in fields)
      size += field.Type is MlirStructType ? field.Type.SizeInBytes : 8;
    return size;
  }

  public MlirStructField? GetField(string name) => Fields.FirstOrDefault(f => f.Name == name);
}

public class MlirInterfaceMethodSignature(string name, List<string> paramTypeNames, List<string> paramNames, string? returnTypeName, bool isStatic = false, string? throwsTypeName = null) {
  public string Name { get; } = name;
  public List<string> ParamTypeNames { get; } = paramTypeNames;
  public List<string> ParamNames { get; } = paramNames;
  public string? ReturnTypeName { get; } = returnTypeName;
  public bool IsStatic { get; } = isStatic;
  public string? ThrowsTypeName { get; } = throwsTypeName;

  public string Format() {
    var paramsStr = string.Join(", ", ParamNames.Zip(ParamTypeNames, (n, t) => $"{n} {t}"));
    var returnStr = ReturnTypeName != null ? $" returns {ReturnTypeName}" : " returns void";
    var throwsStr = ThrowsTypeName != null ? $" throws {ThrowsTypeName}" : "";
    return $"{(IsStatic ? "static " : "")}{Name}({paramsStr}){returnStr}{throwsStr}";
  }

  /// <summary>
  /// Formats the method signature with type parameters resolved to concrete types.
  /// </summary>
  public string FormatResolved(Dictionary<string, MlirType> typeParams) {
    string Resolve(string typeName) =>
      typeParams.TryGetValue(typeName, out var resolved) ? MlirType.FormatAsSourceName(resolved) : typeName;

    var paramsStr = string.Join(", ", ParamNames.Zip(ParamTypeNames, (n, t) => $"{n} {Resolve(t)}"));
    var returnStr = ReturnTypeName != null ? $" returns {Resolve(ReturnTypeName)}" : " returns void";
    var throwsStr = ThrowsTypeName != null ? $" throws {ThrowsTypeName}" : "";
    return $"{(IsStatic ? "static " : "")}{Name}({paramsStr}){returnStr}{throwsStr}";
  }
}

public class MlirInterfaceType(string name, List<MlirInterfaceMethodSignature> methods, List<string>? extendedInterfaces = null) : MlirType(name, 0) {
  public List<MlirInterfaceMethodSignature> Methods { get; } = methods;
  public List<string> ExtendedInterfaces { get; } = extendedInterfaces ?? [];
}

public class MlirEnumCase(string name, int ordinal, object? rawValue = null,
    List<(string Name, MlirType Type)>? associatedValues = null) {
  public string Name { get; } = name;
  public int Ordinal { get; } = ordinal;
  public object? RawValue { get; } = rawValue;
  public List<(string Name, MlirType Type)>? AssociatedValues { get; } = associatedValues;
}

public class MlirEnumType(string name, List<MlirEnumCase> cases, MlirType? backingType = null, List<string>? conformingInterfaces = null) : MlirType(name) {
  public List<MlirEnumCase> Cases { get; } = cases;
  public MlirType? BackingType { get; } = backingType;
  public List<string> ConformingInterfaces { get; } = conformingInterfaces ?? [];

  public bool HasAssociatedValues => Cases.Any(c => c.AssociatedValues is { Count: > 0 });

  /// For associated value enums: 8 (tag) + max payload size across all cases.
  /// Each payload field occupies 8 bytes (64-bit slots).
  /// For simple enums: 8 bytes (single i64).
  public override int SizeInBytes => HasAssociatedValues
    ? 8 + Cases.Max(c => c.AssociatedValues?.Count ?? 0) * 8
    : 8;

  public MlirEnumCase? GetCase(string name) => Cases.FirstOrDefault(c => c.Name == name);
}

/// Marker type for string-backed enum backing types. At runtime, string-backed enums
/// are stored as ordinals (i64), but their display value is the associated string.
public class MlirStringBackingType() : MlirType("string_enum", 8);

/// Marker type for character-backed enum backing types. At runtime, char-backed enums
/// are stored as ordinals (i64), but their display value is the associated character.
public class MlirCharBackingType() : MlirType("char_enum", 8);

public class MlirTypeParameterType(string parameterName) : MlirType(parameterName) {
  public string ParameterName { get; } = parameterName;
  public override int SizeInBytes => throw new InvalidOperationException($"Type parameter '{ParameterName}' has no size");
}

public class MlirFunctionType(List<MlirType> parameterTypes, MlirType? returnType) : MlirType(FormatName(parameterTypes, returnType), 8) {
  public List<MlirType> ParameterTypes { get; } = parameterTypes;
  public MlirType? ReturnType { get; } = returnType;

  private static string FormatName(List<MlirType> parameterTypes, MlirType? returnType) {
    var paramsStr = string.Join(", ", parameterTypes.Select(t => t.Name));
    var returnStr = returnType != null ? $" returns {returnType.Name}" : "";
    return $"fn({paramsStr}){returnStr}";
  }
}
