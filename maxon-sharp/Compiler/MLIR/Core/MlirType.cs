namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirType {
  public string Name { get; }
  public virtual int SizeInBytes { get; }

  public string? SourceFilePath { get; set; }
  public int? SourceLine { get; set; }
  public int? SourceColumn { get; set; }

  /// True for types that are heap-allocated and need refcounting (structs and
  /// associated-value enums). Simple enums and primitives return false.
  public virtual bool IsHeapAllocated => false;

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

  public static MlirType? FromSizedName(string name) => name switch {
    "u8" => U8, "u16" => U16, "u32" => U32, "u64" => U64,
    "i8" => I8, "i16" => I16, "i32" => I32, "i64" => I64,
    "f32" => F32, "f64" => F64,
    _ => null
  };

  public bool IsFloat => this == F32 || this == F64;

  // Bare primitives cannot be used as type arguments in `with` clauses — users must create a ranged typealias first.
  // Excludes bool (I1) since it's already a constrained type.
  public bool IsBarePrimitive => this == I8 || this == I64 || this == F64;
  public bool IsUnsigned => this == U8 || this == U16 || this == U32 || this == U64;
  public MlirType ToSigned() => this == U8 ? I8 : this == U16 ? I16 : this == U32 ? I32 : this == U64 ? I64 : this;
  public MlirType ToUnsigned() => this == I8 ? U8 : this == I16 ? U16 : this == I32 ? U32 : this == I64 ? U64 : this;

  /// <summary>
  /// Returns the element size in bytes for the type.
  /// Must be > 0 for any type used as an array element.
  /// </summary>
  public virtual int ElementSize {
    get {
      var size = SizeInBytes;
      if (size <= 0)
        throw new InvalidOperationException($"ElementSize is {size} for type '{Name}' — cannot be used as an array element");
      return size;
    }
  }

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
  public override bool IsHeapAllocated => true;
  public string? DocString { get; set; }
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
  // Inner ranged primitive typealiases declared inside this generic type body.
  // Each concrete instantiation gets a nominally distinct copy of these aliases.
  public Dictionary<string, MlirRangedPrimitiveType> InnerRangedAliases { get; } = [];
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
    // Minimum 8 bytes so zero-field structs can still be heap-allocated
    return Math.Max(fields.Count * 8, 8);
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

public class MlirEnumType(string name, List<MlirEnumCase> cases, MlirType? backingType = null, List<string>? conformingInterfaces = null, List<string>? associatedTypeNames = null, Dictionary<string, MlirType>? typeParams = null, Dictionary<string, List<string>>? whereConstraints = null) : MlirType(name) {
  public List<MlirEnumCase> Cases { get; } = cases;
  public MlirType? BackingType { get; } = backingType;
  public List<string> ConformingInterfaces { get; } = conformingInterfaces ?? [];
  public List<string> AssociatedTypeNames { get; } = associatedTypeNames ?? [];
  public Dictionary<string, MlirType> TypeParams { get; } = typeParams ?? [];
  public Dictionary<string, List<string>> WhereConstraints { get; } = whereConstraints ?? [];

  public bool HasAssociatedValues => Cases.Any(c => c.AssociatedValues is { Count: > 0 });
  /// True when the user explicitly provided raw values (e.g. `ok = 200`).
  /// False for auto-incremented enums (bare case names).
  public bool HasExplicitBackingValues { get; init; }
  public override bool IsHeapAllocated => HasAssociatedValues;

  /// For associated value enums: 8 (tag) + max payload size across all cases.
  /// Each payload field occupies 8 bytes (64-bit slots).
  /// For simple enums: 8 bytes (single i64).
  public override int SizeInBytes => HasAssociatedValues
    ? 8 + Cases.Max(c => c.AssociatedValues?.Count ?? 0) * 8
    : 8;

  // Associated-value enums are heap-allocated; array elements store 8-byte pointers
  public override int ElementSize => HasAssociatedValues ? 8 : SizeInBytes;

  public MlirEnumCase? GetCase(string name) => Cases.FirstOrDefault(c => c.Name == name);
}

/// Marker type for string-backed enum backing types. At runtime, string-backed enums
/// are stored as ordinals (i64), but their display value is the associated string.
public class MlirStringBackingType() : MlirType("string_enum", 8);

/// Marker type for character-backed enum backing types. At runtime, char-backed enums
/// are stored as ordinals (i64), but their display value is the associated character.
public class MlirCharBackingType() : MlirType("char_enum", 8);

/// Stores compile-time constant field values for a struct-backed enum case.
public record StructRawValue(string StructTypeName, List<(string FieldName, long Value)> Fields);

/// Marker type for struct-backed enum backing types. At runtime, struct-backed enums
/// are stored as ordinals (i64). Each case has an associated struct value accessible via .rawValue.
public class MlirStructBackingType(string structTypeName) : MlirType("struct_enum", 8) {
  public string StructTypeName { get; } = structTypeName;
}

public class MlirTypeParameterType(string parameterName) : MlirType(parameterName) {
  public string ParameterName { get; } = parameterName;
  public override int SizeInBytes => throw new InvalidOperationException($"Type parameter '{ParameterName}' has no size");
}

/// Represents a primitive type (int, float, byte) with mandatory range constraints.
/// At the source level this is the alias name (e.g., "Age"); at codegen it lowers to OptimalType.
/// Integer bounds use long; float bounds use double.
public class MlirRangedPrimitiveType : MlirType {
  public MlirType BaseType { get; }
  public long IntLower { get; }
  public long IntUpper { get; }
  public double FloatLower { get; }
  public double FloatUpper { get; }
  public bool UpperInclusive { get; }
  public MlirType OptimalType { get; }

  /// Constructor for integer-based ranges (int, byte).
  public MlirRangedPrimitiveType(string aliasName, MlirType baseType, long lower, long upper, bool upperInclusive)
      : base(aliasName, ComputeOptimalIntType(lower, upper).SizeInBytes) {
    BaseType = baseType;
    IntLower = lower;
    IntUpper = upper;
    UpperInclusive = upperInclusive;
    OptimalType = ComputeOptimalIntType(lower, upper);
  }

  /// Constructor for float-based ranges.
  public MlirRangedPrimitiveType(string aliasName, MlirType baseType, double lower, double upper, bool upperInclusive)
      : base(aliasName, baseType.SizeInBytes) {
    BaseType = baseType;
    FloatLower = lower;
    FloatUpper = upper;
    UpperInclusive = upperInclusive;
    OptimalType = baseType; // F32 or F64
  }

  public bool IsFloatBased => BaseType.IsFloat;

  /// True when the range is entirely non-negative — derived from OptimalType.
  public new bool IsUnsigned => OptimalType.IsUnsigned;

  public override int ElementSize => OptimalType.SizeInBytes;

  /// Pick the smallest x86-64-optimal type that can represent the range.
  /// Returns unsigned types (U8/U16/U32/U64) when range is non-negative.
  private static MlirType ComputeOptimalIntType(long lower, long upper) {
    if (lower >= 0) {
      // Unsigned path: compare as unsigned to handle u64.max (-1 as signed)
      var u = (ulong)upper;
      if (u <= 127) return U8;
      if (u <= 255) return U8;
      if (u <= 32767) return U16;
      if (u <= 65535) return U16;
      if (u <= 2147483647) return U32;
      if (u <= 4294967295) return U32;
      return U64;
    }
    // Signed path
    if (lower >= -128 && upper <= 127) return I8;
    if (lower >= -32768 && upper <= 32767) return I16;
    if (lower >= -2147483648 && upper <= 2147483647) return I32;
    return I64;
  }

  /// Returns true if this type's range is entirely contained within other's range.
  public bool IsSubsetOf(MlirRangedPrimitiveType other) {
    if (BaseType != other.BaseType) return false;
    if (IsFloatBased) {
      var thisUpper = UpperInclusive ? FloatUpper : FloatUpper - 1;
      var otherUpper = other.UpperInclusive ? other.FloatUpper : other.FloatUpper - 1;
      return FloatLower >= other.FloatLower && thisUpper <= otherUpper;
    } else if (IntLower >= 0 && other.IntLower >= 0) {
      // Both unsigned: compare as unsigned
      var thisUpper = UpperInclusive ? (ulong)IntUpper : (ulong)IntUpper - 1;
      var otherUpper = other.UpperInclusive ? (ulong)other.IntUpper : (ulong)other.IntUpper - 1;
      return (ulong)IntLower >= (ulong)other.IntLower && thisUpper <= otherUpper;
    } else {
      var thisUpper = UpperInclusive ? IntUpper : IntUpper - 1;
      var otherUpper = other.UpperInclusive ? other.IntUpper : other.IntUpper - 1;
      return IntLower >= other.IntLower && thisUpper <= otherUpper;
    }
  }

  /// Returns the type with the wider range, or null if ranges are incompatible (different base types).
  public static MlirRangedPrimitiveType? Wider(MlirRangedPrimitiveType a, MlirRangedPrimitiveType b) {
    if (a.BaseType != b.BaseType) return null;
    if (a.IsSubsetOf(b)) return b;
    if (b.IsSubsetOf(a)) return a;
    return null;
  }

  /// True when the range covers the full representable range of the base type,
  /// making runtime range checks unnecessary.
  public bool IsFullBaseRange {
    get {
      if (IsFloatBased) {
        var effectiveUpper = UpperInclusive ? FloatUpper : FloatUpper - 1;
        if (BaseType == F64) return FloatLower <= double.MinValue && effectiveUpper >= double.MaxValue;
        if (BaseType == F32) return FloatLower <= (double)-float.MaxValue && effectiveUpper >= (double)float.MaxValue;
        return false;
      } else {
        // Check against the base type, not optimal type — values arrive as full-width base type.
        // Full range means ALL possible bit patterns of the base type are covered.
        // Both signed (i64.min to i64.max) and unsigned (0 to u64.max) cover all i64 bits.
        if (BaseType == I64) {
          var effectiveUpper = UpperInclusive ? IntUpper : IntUpper - 1;
          // Signed full range
          if (IntLower <= long.MinValue && effectiveUpper >= long.MaxValue) return true;
          // Unsigned full range: 0 to u64.max (-1 as signed) covers all bit patterns
          if (IntLower == 0 && (ulong)effectiveUpper >= ulong.MaxValue) return true;
          return false;
        }
        if (BaseType == I8) {
          var effectiveUpper = UpperInclusive ? IntUpper : IntUpper - 1;
          return IntLower <= 0 && effectiveUpper >= 255;
        }
        return false;
      }
    }
  }

  public string FormatRange() {
    var upperOp = UpperInclusive ? "to" : "upto";
    if (IsFloatBased)
      return $"{FormatAsSourceName(BaseType)}({FloatLower} {upperOp} {FloatUpper})";
    if (IntLower >= 0)
      return $"{FormatAsSourceName(BaseType)}({(ulong)IntLower} {upperOp} {(ulong)IntUpper})";
    return $"{FormatAsSourceName(BaseType)}({IntLower} {upperOp} {IntUpper})";
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
