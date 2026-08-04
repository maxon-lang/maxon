using System.Globalization;

namespace MaxonSharp.Compiler.Ir.Core;

public class IrType {
  public string Name { get; }
  public virtual int SizeInBytes { get; }

  public string? SourceFilePath { get; set; }
  public int? SourceLine { get; set; }
  public int? SourceColumn { get; set; }

  /// True for types that are heap-allocated and need refcounting (structs and
  /// associated-value enums). Simple enums and primitives return false.
  public virtual bool IsHeapAllocated => false;

  public IrType(string name, int sizeInBytes) {
    Name = name;
    SizeInBytes = sizeInBytes;
  }

  protected IrType(string name) {
    Name = name;
  }

  public static IrType I8 { get; } = new("i8", 1);
  public static IrType I16 { get; } = new("i16", 2);
  public static IrType I32 { get; } = new("i32", 4);
  public static IrType I64 { get; } = new("i64", 8);
  public static IrType U8 { get; } = new("u8", 1);
  public static IrType U16 { get; } = new("u16", 2);
  public static IrType U32 { get; } = new("u32", 4);
  public static IrType U64 { get; } = new("u64", 8);
  public static IrType F32 { get; } = new("f32", 4);
  public static IrType F64 { get; } = new("f64", 8);
  public static IrType I1 { get; } = new("i1", 1);
  public static IrType Void { get; } = new("void", 0);
  // Sentinel type for function-typed parameters (higher-order functions)
  public static IrType Fn { get; } = new("fn", 8);
  // A NUL-terminated UTF-8 byte pointer. Storage is identical to i64; the
  // distinction exists at the source/type-check layer to prevent confusing
  // raw integers with cstrings (which is how the Subprocess cwd bug got past
  // review).
  public static IrType CString { get; } = new("cstring", 8);

  public static IrType? FromSizedName(string name) => name switch {
    "u8" => U8,
    "u16" => U16,
    "u32" => U32,
    "u64" => U64,
    "i8" => I8,
    "i16" => I16,
    "i32" => I32,
    "i64" => I64,
    "f32" => F32,
    "f64" => F64,
    _ => null
  };

  public bool IsFloat => this == F32 || this == F64;

  /// <summary>
  /// True for the scalars that live in a general-purpose register. This is the
  /// membership test for the two-register value ABI (see
  /// <see cref="IrStructType.IsTwoRegisterValueTuple"/>), so it is a WHITELIST and not
  /// `!IsHeapAllocated`: a placeholder, a type parameter and an unresolved named type all
  /// answer false to IsHeapAllocated while being nothing of the kind, and admitting one
  /// would hand back a register pair for a type whose layout is not yet known.
  /// Floats are excluded because they are returned in an FP register, not a GPR.
  /// </summary>
  public bool IsGprScalar =>
    this == I8 || this == I16 || this == I32 || this == I64
    || this == U8 || this == U16 || this == U32 || this == U64
    || this == I1 || this == CString;

  // Bare primitives cannot be used as type arguments in `with` clauses — users must create a ranged typealias first.
  // Excludes bool (I1) since it's already a constrained type.
  public bool IsBarePrimitive => this == I8 || this == I64 || this == F64;
  public bool IsUnsigned => this == U8 || this == U16 || this == U32 || this == U64;
  public IrType ToSigned() => this == U8 ? I8 : this == U16 ? I16 : this == U32 ? I32 : this == U64 ? I64 : this;
  public IrType ToUnsigned() => this == I8 ? U8 : this == I16 ? U16 : this == I32 ? U32 : this == I64 ? U64 : this;

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

  /// <summary>
  /// Returns the element_size value for __ManagedMemory structs.
  /// Bool (I1) returns 0 (the bit-packed sentinel); all other types return ElementSize.
  /// </summary>
  public int ManagedMemoryElementSize => this == I1 ? 0 : ElementSize;

  public override string ToString() => Name;

  /// Unwrap IrRangedPrimitiveType to its BaseType for lowering.
  public static IrType Resolve(IrType type) =>
    type is IrRangedPrimitiveType rpt ? rpt.BaseType : type;

  /// <summary>
  /// Maps an IrType back to its source-level name for error messages.
  /// </summary>
  public static string FormatAsSourceName(IrType type) {
    if (type is IrRangedPrimitiveType ranged) return ranged.Name;
    if (type == I64 || type == U64) return "int";
    if (type == F64) return "float";
    if (type == F32) return "float";
    if (type == I1) return "bool";
    if (type == I8 || type == U8) return "byte";
    if (type == I16 || type == U16) return "int";
    if (type == I32 || type == U32) return "int";
    if (type == Void) return "void";
    if (type is IrStructType st && st.IsTuple) {
      var elems = st.Fields.Select(f => FormatAsSourceName(f.Type));
      return $"({string.Join(", ", elems)})";
    }
    return type.Name;
  }
}

public class IrStructField(string name, IrType type, bool isExported, bool isMutable, IrAttribute? defaultValue = null, bool isModuleVisible = false) {
  public string Name { get; } = name;
  public IrType Type { get; set; } = type;
  public bool IsExported { get; } = isExported;
  // True for `module var` / `module function` inside a type body.
  // Mutually exclusive with IsExported (enforced at the parser).
  public bool IsModuleVisible { get; } = isModuleVisible;
  public bool IsMutable { get; } = isMutable;
  public IrAttribute? DefaultValue { get; } = defaultValue;
  public int Offset { get; set; }
}

/// <summary>
/// Sentinel placeholder for type names registered during pre-scanning before
/// the full type definition is available. Unlike IrStructType (which returns
/// IsHeapAllocated=true), placeholders return false for all semantic queries,
/// preventing incorrect destructor generation or refcounting decisions based
/// on unresolved types.
/// </summary>
public class IrPlaceholderType(string name) : IrType(name, 8) {
  public override bool IsHeapAllocated => false;
}

public class IrStructType : IrType {
  public override bool IsHeapAllocated => true;
  public string? DocString { get; set; }
  public List<IrStructField> Fields { get; }
  public List<string> AssociatedTypeNames { get; }
  // HashSet for O(1) Contains — this is queried per-call-site during parsing
  // and lowering ("does this type conform to Equatable / BuiltinArrayLiteral /
  // ..."). For interface-alias types we still rely on a single-element
  // invariant; callers that need that element use .First().
  public HashSet<string> ConformingInterfaces { get; }

  /// The one const parameter the language has: the element count of the `with N Type` form.
  /// Spelled at six sites as a bare literal before it was named here, one of which was a
  /// key-building site that simply omitted it — see Parser.ConstArgSegments.
  public const string CapacityConstParamName = "__capacity";

  /// The CONST arguments this instance was applied to, keyed by const parameter name.
  /// Part of the instance's IDENTITY, exactly as TypeParams is: a `Vector with 3 Int` is a
  /// different type from a `Vector with 4 Int` (specs/vector.md).
  public Dictionary<string, long> ConstParams { get; }

  /// <summary>
  /// The CONST arguments of a generic instance, in ordinal order of their parameter names — today
  /// just the element count of the <c>with N Type</c> form.
  ///
  /// ⭐ THE ONE SPELLING OF AN INSTANCE'S CONST ARGUMENTS. Every written form of an instance's
  /// identity is built from it: <see cref="InstanceKey"/> and every synthesized instance name
  /// (<see cref="InstanceNameSuffix"/>), in the parser AND in monomorphization. A generic instance
  /// is its source type, its TYPE arguments AND its const arguments — <c>Vector with 3 Int</c> is a
  /// different type from <c>Vector with 4 Int</c>, which specs/vector.md states outright — and
  /// omitting them is never a compile error at any of those sites. It silently gives one instance
  /// two capacities: a capacity-4 field adopted a declared capacity-3 alias, two capacities of one
  /// element type minted the SAME structural name, and a 3-vector was accepted for a declared
  /// 4-vector. It lives here, on the type that OWNS <see cref="ConstParams"/>, because the parser's
  /// registry and the module's alias table are two tables describing one thing and had drifted.
  ///
  /// The VALUES alone are complete, without their parameter names, because every consumer already
  /// carries the SOURCE TYPE and a source type's const parameter list is fixed by its declaration.
  ///
  /// An ABSENT dictionary and an EMPTY one are the same instance and produce the same segments, so
  /// no caller has to normalize one into the other before asking.
  /// </summary>
  public static IEnumerable<string> ConstArgSegments(IReadOnlyDictionary<string, long>? constArgs) =>
    constArgs is null
      ? []
      : constArgs.OrderBy(kv => kv.Key, StringComparer.Ordinal)
          .Select(kv => kv.Value.ToString(CultureInfo.InvariantCulture));

  /// <summary>
  /// What distinguishes one instance of a source type from another, as a name fragment: its const
  /// arguments first, then its type arguments — mirroring the source form <c>Vector with 4 Int</c>,
  /// and matching the one mint that already spelled a count (ParseFromExpression's
  /// <c>__Vector_3_Int</c>) so an array literal and a declared alias of the same size agree.
  /// </summary>
  public static string InstanceNameSuffix(IReadOnlyDictionary<string, long>? constArgs,
      IEnumerable<string> paramTypeNames) =>
    string.Join("_", ConstArgSegments(constArgs).Concat(paramTypeNames));

  /// <summary>
  /// The generic INSTANCE a type name denotes: its source type plus its type and const arguments BY
  /// NAME. Two spellings of one instance — a declared <c>ValueIdArray</c> and the structural
  /// <c>Array_ValueId</c> the field-alias mint would otherwise invent — produce the same key, which
  /// is what lets the declaration index answer "does this project already name this?" in O(1).
  ///
  /// By name rather than by IrType identity because the same instance is asked about from parsers
  /// holding different objects for one type: a pre-registered placeholder in the declaration phase,
  /// the real ranged type later. Ordered so the key does not depend on the order a substitution
  /// dictionary happens to enumerate in.
  ///
  /// ⭐ THIS IS THE ONLY SPELLING OF "the same generic instance". Six sites ask the question — the
  /// declaration index's key, the already-registered test in TryRegisterDeclaredAlias, the
  /// parser-local reuse scan and the extension-alias reuse guard in RegisterConcreteTypeAlias, the
  /// return-type search in ResolveStructReturnTypeThroughSelf, and monomorphization's
  /// TypeSubstitution.FindConcreteAlias — and they must agree, because every one of them decides
  /// whether to adopt a name or mint one. Each carried its own hand-written comparison until this
  /// was consolidated; three of them were the same count-plus-per-parameter-name loop written out
  /// again. A divergence between them is not a compile error at any site: it either adopts a name
  /// for an instance that is NOT the one in hand (a wrong answer, silently) or mints a name beside
  /// an existing one, which is the defect this key exists to close.
  ///
  /// The const arguments form a SEPARATE group after the <c>|</c> rather than more entries in the
  /// same list, so a type parameter and a const parameter that happen to share a name cannot produce
  /// one key between them.
  /// </summary>
  public static string InstanceKey(string sourceName, IReadOnlyDictionary<string, IrType> typeArgs,
      IReadOnlyDictionary<string, long>? constArgs) {
    var args = typeArgs.Select(kv => $"{kv.Key}={kv.Value.Name}").ToList();
    args.Sort(StringComparer.Ordinal);
    return $"{sourceName}<{string.Join(",", args)}|{string.Join(",", ConstArgSegments(constArgs))}>";
  }
  public Dictionary<string, IrType> TypeParams { get; }
  public bool IsTuple { get; }
  // True when this type represents a typealias of an interface (e.g., typealias ElementIterable = Iterable with Element)
  public bool IsInterfaceAlias { get; }
  // Maps type parameter names to required interface names (from where clauses)
  public Dictionary<string, List<string>> WhereConstraints { get; }
  // Inner ranged primitive typealiases declared inside this generic type body.
  // Each concrete instantiation gets a nominally distinct copy of these aliases.
  public Dictionary<string, IrRangedPrimitiveType> InnerRangedAliases { get; } = [];
  // How many of the TRAILING entries of AssociatedTypeNames may be omitted at a use site.
  // Zero for every user type: `Map with Key` is an arity error, and should be.
  //
  // It is 1 for exactly one type, `Promise`, whose trailing parameter is the error its thunk
  // throws (see PromiseType). `Promise with T` is the type of a NON-throwing promise and
  // `Promise with (T, E)` of a throwing one, so the parameter is present precisely when there
  // is an error to name. An omitted optional parameter is left ABSENT from TypeParams rather
  // than bound to a placeholder: "this promise has no error type" and "this promise's error
  // type is some stand-in" are different claims, and only the first one is true.
  public int OptionalTrailingTypeParamCount { get; set; }
  public IrStructType(string name, List<IrStructField> fields, List<string>? associatedTypeNames = null, IEnumerable<string>? conformingInterfaces = null, Dictionary<string, long>? constParams = null, Dictionary<string, IrType>? typeParams = null, bool isTuple = false, Dictionary<string, List<string>>? whereConstraints = null, bool isInterfaceAlias = false) : base(name) {
    Fields = fields;
    AssociatedTypeNames = associatedTypeNames ?? [];
    ConformingInterfaces = conformingInterfaces is null ? [] : [.. conformingInterfaces];
    ConstParams = constParams ?? [];
    TypeParams = typeParams ?? [];
    IsTuple = isTuple;
    IsInterfaceAlias = isInterfaceAlias;
    WhereConstraints = whereConstraints ?? [];
    int offset = 0;
    foreach (var field in Fields) {
      field.Offset = offset;
      offset += FieldSlotSize(field);
    }
    // Minimum 8 bytes so zero-field structs can still be heap-allocated. Computed once here
    // (as the original ComputeSize did) rather than per access.
    _sizeInBytes = Math.Max(offset, 8);
  }

  private readonly int _sizeInBytes;

  // The 40-byte __ManagedMemory embedded whole at offset 0 of a fused String/Character:
  // buffer, length, capacity, element_size, parent_ptr. Matches ManagedMemoryStructSize
  // in the lowering. The record IS its own __ManagedMemory (envelope-collapse change).
  private const int InlineManagedMemoryBytes = 40;

  // The three types whose `managed` __ManagedMemory is collapsed inline (envelope collapse):
  // String / Character (BuiltinStringLiteral / BuiltinCharLiteral) and Array / Vector
  // (BuiltinArrayLiteral). Each IS its own __ManagedMemory; the record's first 40 bytes ARE
  // a valid __ManagedMemory. Detected by conformance so plain-pointer `managed` fields (e.g.
  // StringIterator.managed, ArrayIterator's cursor source) are unaffected.
  public bool ConformsToBuiltinManagedWrapper =>
    ConformingInterfaces.Contains("BuiltinStringLiteral")
    || ConformingInterfaces.Contains("BuiltinCharLiteral")
    || ConformingInterfaces.Contains("BuiltinArrayLiteral");

  // The `managed` field of such a wrapper is stored inline (the __ManagedMemory embedded at
  // offset 0), not as an 8-byte heap pointer, so that a String's `singleByteGraphemesFlag` lands at offset
  // 40 and an Array is exactly a 40-byte __ManagedMemory.
  private bool IsInlineManagedField(IrStructField field) =>
    field.Name == "managed" && ConformsToBuiltinManagedWrapper;

  // Storage slot a field occupies: 8-byte 64-bit slots (scalars inline, heap types as
  // pointers) except the inline `managed` __ManagedMemory, which is embedded whole.
  private int FieldSlotSize(IrStructField field) =>
    IsInlineManagedField(field) ? InlineManagedMemoryBytes : 8;

  public override int SizeInBytes => _sizeInBytes;

  // When stored as array elements, structs are heap pointers (8 bytes)
  public override int ElementSize => 8;

  public IrStructField? GetField(string name) => Fields.FirstOrDefault(f => f.Name == name);

  // Element buffers up to this size are stack-allocated instead of heap-allocated
  public const int MaxStackAllocBufferBytes = 16384;

  /// Whether this type has a fixed-capacity __ManagedMemory buffer small enough to stack-allocate.
  public bool HasStackAllocatableBuffer =>
    ConstParams.TryGetValue(CapacityConstParamName, out var capacity)
    && TypeParams.TryGetValue("Element", out var elemType)
    && capacity * elemType.ElementSize <= MaxStackAllocBufferBytes;

  /// Number of GPRs the value-return ABI hands a small tuple back in, and the widest
  /// record that fits them. Two is not an arbitrary limit: it is what the ABI reserves
  /// a second return register for (RAX+R10 on x64, X0+X13 on arm64).
  public const int ValueReturnRegisterCount = 2;
  public const int ValueReturnMaxBytes = ValueReturnRegisterCount * 8;

  /// <summary>
  /// True when a function returning this type hands it back in two registers instead of a
  /// heap record. Deliberately narrow — every tuple outside the gate keeps today's heap
  /// lowering, so being outside it is slower and never unsafe.
  ///
  /// Only a TUPLE qualifies. A named struct is excluded even when its layout would fit,
  /// because a struct has reference identity a user can observe (`is`, aliasing, mutation
  /// through an alias); a tuple returned by value has no prior identity to break, since the
  /// only way to obtain one is the call that just produced it.
  /// </summary>
  public bool IsTwoRegisterValueTuple =>
    IsTuple
    && Fields.Count == ValueReturnRegisterCount
    && SizeInBytes <= ValueReturnMaxBytes
    && Fields.TrueForAll(f => IrType.Resolve(f.Type).IsGprScalar);

  /// <summary>
  /// The value-return gate, resolved through the module's type table so a named tuple type is
  /// judged on its canonical definition rather than on whatever partially-specialised copy the
  /// use site happens to hold. Returns the tuple when a function returning it uses the
  /// two-register ABI, and null for every type that keeps the heap-record convention.
  ///
  /// This is the ONE place the gate is decided. The escape analysis and the lowering both ask
  /// it, so they cannot drift into disagreeing about which functions have which ABI — a
  /// disagreement that would not be a missed optimisation but a miscompile.
  /// </summary>
  public static IrStructType? AsTwoRegisterValueTuple(IrType? type, Dictionary<string, IrType> typeDefs) {
    if (type is not IrStructType structType) return null;
    if (typeDefs.TryGetValue(structType.Name, out var canonical) && canonical is IrStructType canonicalStruct)
      structType = canonicalStruct;
    return structType.IsTwoRegisterValueTuple ? structType : null;
  }

  public static IrStructType CreateTupleType(List<IrType> elementTypes) {
    // Resolve ranged primitive types to base types for consistent tuple struct layout.
    var resolved = elementTypes.Select(t => IrType.Resolve(t)).ToList();
    var fields = resolved.Select((t, i) =>
      new IrStructField($"_{i}", t, isExported: true, isMutable: true)).ToList();
    var name = TupleMangledName(elementTypes);
    return new IrStructType(name, fields, isTuple: true);
  }

  public static string TupleMangledName(List<IrType> elementTypes) {
    // Resolve ranged primitive types to their base types so that e.g.
    // (Integer, Integer) and (i64, i64) produce the same mangled name.
    return "__Tuple_" + string.Join("_", elementTypes.Select(t => IrType.Resolve(t).Name));
  }
}

public class IrInterfaceMethodSignature(string name, List<string> paramTypeNames, List<string> paramNames, string? returnTypeName, bool isStatic = false, string? throwsTypeName = null) {
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
  public string FormatResolved(Dictionary<string, IrType> typeParams) {
    string Resolve(string typeName) =>
      typeParams.TryGetValue(typeName, out var resolved) ? IrType.FormatAsSourceName(resolved) : typeName;

    var paramsStr = string.Join(", ", ParamNames.Zip(ParamTypeNames, (n, t) => $"{n} {Resolve(t)}"));
    var returnStr = ReturnTypeName != null ? $" returns {Resolve(ReturnTypeName)}" : " returns void";
    var throwsStr = ThrowsTypeName != null ? $" throws {ThrowsTypeName}" : "";
    return $"{(IsStatic ? "static " : "")}{Name}({paramsStr}){returnStr}{throwsStr}";
  }
}

public class IrInterfaceType(string name, List<IrInterfaceMethodSignature> methods, List<string>? extendedInterfaces = null) : IrType(name, 0) {
  public List<IrInterfaceMethodSignature> Methods { get; } = methods;
  public List<string> ExtendedInterfaces { get; } = extendedInterfaces ?? [];
  // An interface value at runtime is a heap pointer to the implementing
  // struct's allocation. Fields of interface type therefore need decref
  // when their owning struct is destructed, the same way struct-typed
  // fields do.
  public override bool IsHeapAllocated => true;
}

public class IrEnumCase(string name, int ordinal, object? rawValue = null,
    List<(string Name, IrType Type)>? associatedValues = null) {
  public string Name { get; } = name;
  public int Ordinal { get; } = ordinal;
  // Settable so the post-prescan function-backed-enum resolution pass can
  // rewrite the placeholder short-name into the fully qualified function name
  // once it's been looked up against the module's function registry.
  public object? RawValue { get; set; } = rawValue;

  /// The discriminant the runtime actually stores for this case: an int-backed enum stores its RAW
  /// value, every other backing (and every associated-value case) stores the ORDINAL. This is the ONE
  /// source of that fact — both the tag the compiler emits (see the parser's GetCaseTagValue) and the
  /// tag the debug-info sidecar records read it here, so the discriminant a stopped value is compared
  /// against can never drift from the one codegen wrote (the "one fact written twice" bug otherwise:
  /// storing the ordinal in the sidecar while codegen stored the raw value silently mislabels a case).
  public long TagValue => RawValue is long rv ? rv : Ordinal;

  public List<(string Name, IrType Type)>? AssociatedValues { get; } = associatedValues;
  // Source position for diagnostics produced by deferred resolution passes
  // (e.g. function-backed enum lookups). Null on synthesized cases.
  public int? SourceLine { get; set; }
  public int? SourceColumn { get; set; }
}

public class IrEnumType(string name, List<IrEnumCase> cases, IrType? backingType = null, IEnumerable<string>? conformingInterfaces = null, List<string>? associatedTypeNames = null, Dictionary<string, IrType>? typeParams = null, Dictionary<string, List<string>>? whereConstraints = null) : IrType(name) {
  public List<IrEnumCase> Cases { get; } = cases;
  // BackingType is settable so the post-prescan resolution pass for
  // function-backed enums can replace a pre-resolution placeholder
  // IrFunctionBackingType with the concrete signature once every file's
  // top-level functions have been pre-scanned.
  public IrType? BackingType { get; set; } = backingType;
  public HashSet<string> ConformingInterfaces { get; } = conformingInterfaces is null ? [] : [.. conformingInterfaces];
  public List<string> AssociatedTypeNames { get; } = associatedTypeNames ?? [];
  public Dictionary<string, IrType> TypeParams { get; } = typeParams ?? [];
  public Dictionary<string, List<string>> WhereConstraints { get; } = whereConstraints ?? [];

  public bool HasAssociatedValues => Cases.Any(c => c.AssociatedValues is { Count: > 0 });
  /// True when this type was declared with the 'union' keyword.
  public bool IsUnion { get; init; }
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

  public IrEnumCase? GetCase(string name) => Cases.FirstOrDefault(c => c.Name == name);
}

/// Marker type for string-backed enum backing types. At runtime, string-backed enums
/// are stored as ordinals (i64), but their display value is the associated string.
public class IrStringBackingType() : IrType("string_enum", 8);

/// Marker type for character-backed enum backing types. At runtime, char-backed enums
/// are stored as ordinals (i64), but their display value is the associated character.
public class IrCharBackingType() : IrType("char_enum", 8);

/// Stores compile-time constant field values for a struct-backed enum case.
public record StructRawValue(string StructTypeName, List<(string FieldName, long Value)> Fields) {
  /// Enum member references that couldn't be resolved during pre-scan (cross-file forward refs).
  /// Resolved after all files are pre-scanned via ResolveStructRawValueEnumRefs().
  public List<(string FieldName, string EnumTypeName, string CaseName, int Line, int Column)> UnresolvedEnumRefs { get; } = [];
  /// Constant references that couldn't be resolved during pre-scan (constants evaluated after enums).
  /// Resolved after all files are pre-scanned via ResolveStructRawValueEnumRefs().
  public List<(string FieldName, string ConstName, int Line, int Column)> UnresolvedConstRefs { get; } = [];
}

/// Marker type for struct-backed enum backing types. At runtime, struct-backed enums
/// are stored as ordinals (i64). Each case has an associated struct value accessible via .rawValue.
public class IrStructBackingType(string structTypeName) : IrType("struct_enum", 8) {
  public string StructTypeName { get; } = structTypeName;
}

/// Marker type for function-backed enum backing types. At runtime, function-backed
/// enums are stored as ordinals (i64). Each case's raw value is a function whose
/// pointer is recovered via .rawValue (a select chain over the ordinal). All cases
/// in a fn-backed enum share the same IrFunctionType signature.
public class IrFunctionBackingType(IrFunctionType signature) : IrType("function_enum", 8) {
  public IrFunctionType Signature { get; } = signature;
}

public class IrTypeParameterType(string parameterName) : IrType(parameterName) {
  public string ParameterName { get; } = parameterName;
  public override int SizeInBytes => throw new InvalidOperationException($"Type parameter '{ParameterName}' has no size");
}

/// Represents a primitive type (int, float, byte) with mandatory range constraints.
/// At the source level this is the alias name (e.g., "Age"); at codegen it lowers to OptimalType.
/// Integer bounds use long; float bounds use double.
public class IrRangedPrimitiveType : IrType {
  public IrType BaseType { get; }
  public long IntLower { get; }
  public long IntUpper { get; }
  public double FloatLower { get; }
  public double FloatUpper { get; }
  public bool UpperInclusive { get; }
  public IrType OptimalType { get; }

  /// Constructor for integer-based ranges (int, byte).
  public IrRangedPrimitiveType(string aliasName, IrType baseType, long lower, long upper, bool upperInclusive)
      : base(aliasName, ComputeOptimalIntType(lower, upper).SizeInBytes) {
    BaseType = baseType;
    IntLower = lower;
    IntUpper = upper;
    UpperInclusive = upperInclusive;
    OptimalType = ComputeOptimalIntType(lower, upper);
  }

  /// Constructor for float-based ranges.
  public IrRangedPrimitiveType(string aliasName, IrType baseType, double lower, double upper, bool upperInclusive)
      : base(aliasName, baseType.SizeInBytes) {
    BaseType = baseType;
    FloatLower = lower;
    FloatUpper = upper;
    UpperInclusive = upperInclusive;
    OptimalType = baseType; // F32 or F64
  }

  public bool IsFloatBased => BaseType.IsFloat;

  /// ⭐ THE one place `upto`'s EXCLUSIVE upper becomes the largest value an integer range ADMITS.
  ///
  /// ⚠ It was spelled out at twelve call sites, and the doc block on `Parser.DeclaredIntRange` already
  /// names why that is the dangerous kind of copy: nothing made the twelve agree, a change reaching
  /// some and not the rest is a range wrongly widened or narrowed by exactly one, and every symptom of
  /// that — a bound check that admits one value too many, a divisor cleared of a hazard it has, a
  /// division narrowed one bit too far — is a wrong answer or a fault at run time and a compile error
  /// nowhere.
  ///
  /// UNSIGNED readers cast the result: `(ulong)InclusiveIntUpper` is bit-identical to the
  /// `(ulong)IntUpper - 1` they used to write, because both wrap in the same two's-complement width.
  public long InclusiveIntUpper => UpperInclusive ? IntUpper : IntUpper - 1;

  /// True when the range is entirely non-negative — derived from OptimalType.
  public new bool IsUnsigned => OptimalType.IsUnsigned;

  public override int ElementSize => OptimalType.SizeInBytes;

  /// ⭐ THE one place an integer range becomes a WIDTH and a SIGNEDNESS. Picks the smallest
  /// x86-64-optimal type that can represent the range, unsigned (U8/U16/U32/U64) when the range is
  /// entirely non-negative.
  ///
  /// Public because a ranged typealias is not the only thing that has a range: a `/` must run at a
  /// type representing BOTH its operands, and the parser computes that union's type from here rather
  /// than growing a second copy of this ladder — see `Parser.DivisionOptimalType`.
  public static IrType ComputeOptimalIntType(long lower, long upper) {
    if (lower >= 0) {
      // Unsigned path: compare as unsigned to handle u64.max (-1 as signed)
      var u = (ulong)upper;
      if (u <= 255) return U8;
      if (u <= 65535) return U16;
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
  public bool IsSubsetOf(IrRangedPrimitiveType other) {
    if (BaseType != other.BaseType) return false;
    if (IsFloatBased) {
      var thisUpper = UpperInclusive ? FloatUpper : FloatUpper - 1;
      var otherUpper = other.UpperInclusive ? other.FloatUpper : other.FloatUpper - 1;
      return FloatLower >= other.FloatLower && thisUpper <= otherUpper;
    } else if (IntLower >= 0 && other.IntLower >= 0) {
      // Both unsigned: compare as unsigned
      return (ulong)IntLower >= (ulong)other.IntLower
        && (ulong)InclusiveIntUpper <= (ulong)other.InclusiveIntUpper;
    } else {
      return IntLower >= other.IntLower && InclusiveIntUpper <= other.InclusiveIntUpper;
    }
  }

  /// Returns the type with the wider range, or null if ranges are incompatible (different base types).
  public static IrRangedPrimitiveType? Wider(IrRangedPrimitiveType a, IrRangedPrimitiveType b) {
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
          // Signed full range
          if (IntLower <= long.MinValue && InclusiveIntUpper >= long.MaxValue) return true;
          // Unsigned full range: 0 to u64.max (-1 as signed) covers all bit patterns
          if (IntLower == 0 && (ulong)InclusiveIntUpper >= ulong.MaxValue) return true;
          return false;
        }
        if (BaseType == I8) return IntLower <= 0 && InclusiveIntUpper >= 255;
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

public class IrFunctionType(List<IrType> parameterTypes, IrType? returnType) : IrType(FormatName(parameterTypes, returnType), 8) {
  public List<IrType> ParameterTypes { get; } = parameterTypes;
  public IrType? ReturnType { get; } = returnType;

  private static string FormatName(List<IrType> parameterTypes, IrType? returnType) {
    var paramsStr = string.Join(", ", parameterTypes.Select(t => t.Name));
    var returnStr = returnType != null ? $" returns {returnType.Name}" : "";
    return $"fn({paramsStr}){returnStr}";
  }
}

/// Synthesized error-union type used by the `try { } otherwise (e) { match e { } }`
/// block construct. Members is the deterministic list of distinct enum types thrown
/// by bare calls inside the try body. The discriminant index of a member is its
/// position in Members. Equality is by member-set (insertion order is preserved for
/// stable discriminants but does not participate in equality).
public class IrErrorUnionType : IrType {
  public IReadOnlyList<IrEnumType> Members { get; }

  public IrErrorUnionType(IReadOnlyList<IrEnumType> members) : base(FormatName(members), 8) {
    if (members.Count < 2)
      throw new ArgumentException($"IrErrorUnionType requires at least 2 members, got {members.Count}", nameof(members));
    Members = members;
  }

  public int IndexOfMember(IrEnumType member) {
    for (int i = 0; i < Members.Count; i++) {
      if (Members[i].Name == member.Name) return i;
    }
    return -1;
  }

  /// True if any member enum has associated values (so the error flag may carry a heap pointer).
  public bool AnyMemberHasAssociatedValues => Members.Any(m => m.HasAssociatedValues);

  private static string FormatName(IReadOnlyList<IrEnumType> members) =>
    "__ErrorUnion_" + string.Join("_", members.Select(m => m.Name));

  public override bool Equals(object? obj) {
    if (obj is not IrErrorUnionType other) return false;
    if (other.Members.Count != Members.Count) return false;
    var aNames = Members.Select(m => m.Name).ToHashSet();
    var bNames = other.Members.Select(m => m.Name).ToHashSet();
    return aNames.SetEquals(bNames);
  }

  public override int GetHashCode() {
    int h = 0;
    foreach (var m in Members.OrderBy(m => m.Name, StringComparer.Ordinal)) {
      h = HashCode.Combine(h, m.Name);
    }
    return h;
  }
}
