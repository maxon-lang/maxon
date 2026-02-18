namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirType {
  public string Name { get; }
  public virtual int SizeInBytes { get; }

  public string? SourceFilePath { get; set; }
  public int? SourceLine { get; set; }
  public int? SourceColumn { get; set; }

  public MlirType(string name, int sizeInBytes) {
    Name = name;
    SizeInBytes = sizeInBytes;
  }

  protected MlirType(string name) {
    Name = name;
  }

  public static MlirType I8 { get; } = new("i8", 1);
  public static MlirType I16 { get; } = new("i16", 2);
  public static MlirType I32 { get; } = new("i32", 4);
  public static MlirType I64 { get; } = new("i64", 8);
  public static MlirType U8 { get; } = new("u8", 1);
  public static MlirType U16 { get; } = new("u16", 2);
  public static MlirType U32 { get; } = new("u32", 4);
  public static MlirType U64 { get; } = new("u64", 8);
  public static MlirType F32 { get; } = new("f32", 4);
  public static MlirType F64 { get; } = new("f64", 8);
  public static MlirType I1 { get; } = new("i1", 1);
  public static MlirType Void { get; } = new("void", 0);
  // Sentinel type for function-typed parameters (higher-order functions)
  public static MlirType Fn { get; } = new("fn", 8);

  public bool IsUnsigned => this == U8 || this == U16 || this == U32 || this == U64;
  public MlirType ToSigned() => this == U8 ? I8 : this == U16 ? I16 : this == U32 ? I32 : this == U64 ? I64 : this;
  public MlirType ToUnsigned() => this == I8 ? U8 : this == I16 ? U16 : this == I32 ? U32 : this == I64 ? U64 : this;

  /// <summary>
  /// Returns the element size in bytes for the type.
  /// </summary>
  public virtual int ElementSize => SizeInBytes;

  public override string ToString() => Name;

  /// Unwrap MlirRangedPrimitiveType to its BaseType for lowering.
  public static MlirType Resolve(MlirType type) =>
    type is MlirRangedPrimitiveType rpt ? rpt.BaseType : type;

  /// <summary>
  /// Maps an MlirType back to its source-level name for error messages.
  /// </summary>
  public static string FormatAsSourceName(MlirType type) {
    if (type is MlirRangedPrimitiveType ranged) return ranged.Name;
    if (type == I64 || type == U64) return "int";
    if (type == F64) return "float";
    if (type == F32) return "float";
    if (type == I1) return "bool";
    if (type == I8 || type == U8) return "byte";
    if (type == I16 || type == U16) return "int";
    if (type == I32 || type == U32) return "int";
    if (type == Void) return "void";
    if (type is MlirStructType st && st.IsTuple) {
      var elems = st.Fields.Select(f => FormatAsSourceName(f.Type));
      return $"({string.Join(", ", elems)})";
    }
    return type.Name;
  }
}

public class MlirStructField(string name, MlirType type, bool isExported, bool isMutable, MlirAttribute? defaultValue = null) {
  public string Name { get; } = name;
  public MlirType Type { get; set; } = type;
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
  public bool IsTuple { get; }
  // True when this type represents a typealias of an interface (e.g., typealias ElementIterable = Iterable with Element)
  public bool IsInterfaceAlias { get; }
  // Maps type parameter names to required interface names (from where clauses)
  public Dictionary<string, List<string>> WhereConstraints { get; }

  public MlirStructType(string name, List<MlirStructField> fields, List<string>? associatedTypeNames = null, List<string>? conformingInterfaces = null, Dictionary<string, long>? constParams = null, Dictionary<string, MlirType>? typeParams = null, bool isTuple = false, Dictionary<string, List<string>>? whereConstraints = null, bool isInterfaceAlias = false) : base(name, ComputeSize(fields)) {
    Fields = fields;
    AssociatedTypeNames = associatedTypeNames ?? [];
    ConformingInterfaces = conformingInterfaces ?? [];
    ConstParams = constParams ?? [];
    TypeParams = typeParams ?? [];
    IsTuple = isTuple;
    IsInterfaceAlias = isInterfaceAlias;
    WhereConstraints = whereConstraints ?? [];
    int offset = 0;
    foreach (var field in Fields) {
      field.Offset = offset;
      // All fields are 8 bytes: scalars use 64-bit slots, struct fields store heap pointers
      offset += 8;
    }
  }

  private static int ComputeSize(List<MlirStructField> fields) {
    return fields.Count * 8;
  }

  // When stored as array elements, structs are heap pointers (8 bytes)
  public override int ElementSize => 8;

  public MlirStructField? GetField(string name) => Fields.FirstOrDefault(f => f.Name == name);

  // Element buffers up to this size are stack-allocated instead of heap-allocated
  public const int MaxStackAllocBufferBytes = 16384;

  /// Whether this type has a fixed-capacity __ManagedMemory buffer small enough to stack-allocate.
  public bool HasStackAllocatableBuffer =>
    ConstParams.TryGetValue("__capacity", out var capacity)
    && TypeParams.TryGetValue("Element", out var elemType)
    && capacity * elemType.ElementSize <= MaxStackAllocBufferBytes;

  public static MlirStructType CreateTupleType(List<MlirType> elementTypes) {
    // Resolve ranged primitive types to base types for consistent tuple struct layout.
    var resolved = elementTypes.Select(t => MlirType.Resolve(t)).ToList();
    var fields = resolved.Select((t, i) =>
      new MlirStructField($"_{i}", t, isExported: true, isMutable: true)).ToList();
    var name = TupleMangledName(elementTypes);
    return new MlirStructType(name, fields, isTuple: true);
  }

  public static string TupleMangledName(List<MlirType> elementTypes) {
    // Resolve ranged primitive types to their base types so that e.g.
    // (Integer, Integer) and (i64, i64) produce the same mangled name.
    return "__Tuple_" + string.Join("_", elementTypes.Select(t => MlirType.Resolve(t).Name));
  }
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

/// Represents a primitive type (int, float, byte) with mandatory range constraints.
/// At the source level this is the alias name (e.g., "Age"); at codegen it lowers to OptimalType.
public class MlirRangedPrimitiveType(string aliasName, MlirType baseType, double lowerBound, double upperBound, bool upperInclusive)
    : MlirType(aliasName, ComputeOptimalSize(baseType, lowerBound, upperBound)) {
  public MlirType BaseType { get; } = baseType;
  public double LowerBound { get; } = lowerBound;
  public double UpperBound { get; } = upperBound;
  public bool UpperInclusive { get; } = upperInclusive;
  public MlirType OptimalType { get; } = ComputeOptimalType(baseType, lowerBound, upperBound);

  /// True when the range is entirely non-negative — derived from OptimalType.
  public new bool IsUnsigned => OptimalType.IsUnsigned;

  public override int ElementSize => OptimalType.SizeInBytes;

  /// Pick the smallest x86-64-optimal type that can represent the range.
  /// Returns unsigned types (U8/U16/U32/U64) when range is non-negative.
  /// i16/u16 are storage-only; all arithmetic uses 32-bit ops.
  private static MlirType ComputeOptimalType(MlirType baseType, double lower, double upper) {
    if (baseType == F64) return F64;
    if (baseType == F32) return F32;
    bool unsigned = lower >= 0;
    if (lower >= -128 && upper <= 127) return unsigned ? U8 : I8;
    if (lower >= 0 && upper <= 255) return U8;
    if (lower >= -32768 && upper <= 32767) return unsigned ? U16 : I16;
    if (lower >= 0 && upper <= 65535) return U16;
    if (lower >= -2147483648 && upper <= 2147483647) return unsigned ? U32 : I32;
    if (lower >= 0 && upper <= 4294967295) return U32;
    return unsigned ? U64 : I64;
  }

  private static int ComputeOptimalSize(MlirType baseType, double lower, double upper) {
    return ComputeOptimalType(baseType, lower, upper).SizeInBytes;
  }

  /// Returns true if this type's range is entirely contained within other's range.
  public bool IsSubsetOf(MlirRangedPrimitiveType other) {
    if (BaseType != other.BaseType) return false;
    var thisUpper = UpperInclusive ? UpperBound : UpperBound - 1;
    var otherUpper = other.UpperInclusive ? other.UpperBound : other.UpperBound - 1;
    return LowerBound >= other.LowerBound && thisUpper <= otherUpper;
  }

  /// Returns the type with the wider range, or null if ranges are incompatible (different base types).
  public static MlirRangedPrimitiveType? Wider(MlirRangedPrimitiveType a, MlirRangedPrimitiveType b) {
    if (a.BaseType != b.BaseType) return null;
    if (a.IsSubsetOf(b)) return b;
    if (b.IsSubsetOf(a)) return a;
    return null;
  }

  /// True when the range covers the full representable range of the base type,
  /// making runtime range checks unnecessary. Checks against the base type
  /// (not the optimal type) because values arrive as full-width base type
  /// values that could be outside a narrower optimal type's range.
  public bool IsFullBaseRange {
    get {
      var effectiveUpper = UpperInclusive ? UpperBound : UpperBound - 1;
      if (BaseType == I64) return LowerBound <= (double)long.MinValue && effectiveUpper >= (double)long.MaxValue;
      if (BaseType == F64) return LowerBound <= double.MinValue && effectiveUpper >= double.MaxValue;
      if (BaseType == F32) return LowerBound <= (double)-float.MaxValue && effectiveUpper >= (double)float.MaxValue;
      if (BaseType == I8) return LowerBound <= 0 && effectiveUpper >= 255;
      return false;
    }
  }

  public string FormatRange() {
    var upperOp = UpperInclusive ? "to" : "upto";
    return $"{FormatAsSourceName(BaseType)}({LowerBound} {upperOp} {UpperBound})";
  }
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
