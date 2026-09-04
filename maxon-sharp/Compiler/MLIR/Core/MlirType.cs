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

  /// The primitive an IrType's own <see cref="Name"/> spells — the inverse of the statics above,
  /// for a reader handed a resolved type's name rather than the type. Wider than
  /// <see cref="FromSizedName"/>, which answers only for the widths source may write in a cast:
  /// `bool` resolves to `i1` and is not a sized name, so a caller asking what a return type IS
  /// needs this one.
  public static IrType? FromPrimitiveName(string name) =>
    FromSizedName(name) ?? (name == I1.Name ? I1 : name == CString.Name ? CString : null);

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
  /// ⭐ THE ONE INJECTIVE JOIN of a LIST OF TYPE NAMES into a synthesized type NAME, for every mint
  /// whose product is then used as a TABLE KEY. Two such mints exist — a tuple's structural name and
  /// an error union's — and for a keyed name, spelling alike is not a clash but a SILENT MERGE: the
  /// second type takes the first's contents, and the wrong answer arrives with no diagnostic.
  ///
  /// ⚠ THE SEPARATOR IS A CHARACTER NO TYPE NAME CAN HOLD. <c>_</c> is inside the identifier alphabet
  /// (<c>1-Lexer.ScanIdentifier</c>), so an <c>_</c> join spells <c>[A_B, C]</c> and <c>[A, B_C]</c>
  /// identically. <c>.</c> and <c>$</c> are the two other characters outside that alphabet this
  /// compiler already puts in names — <c>.</c> separates a type from its method, <c>$</c> an overload
  /// from its mangled argument list — and a third meaning for either would make an existing
  /// <c>IndexOf</c> misread a synthesized name as a qualified call. <c>-</c> means nothing here and
  /// cannot occur in an identifier.
  ///
  /// ⚠ THE COUNT RIDES IN FRONT, which is what makes a flattened list self-delimiting once a member
  /// that is ITSELF such a name spells itself in full: without it <c>[[A,B],C,D]</c> and
  /// <c>[[A,B,C],D]</c> both join to <c>__Tuple-A-B-C-D</c>.
  ///
  /// Stated ONCE because the mints must agree on the property, not merely happen to have it: each was
  /// written as its own hand-rolled <c>string.Join("_", …)</c>, both were non-injective for the same
  /// reason, and fixing one left the other reachable — measured, a program whose enums are named
  /// <c>A_B</c>/<c>C</c> and <c>A</c>/<c>B_C</c> had one <c>try</c> block's error union answer to the
  /// other's name and its handler rejected with "'A_B' is not a member of the error union".
  /// </summary>
  public const char SynthesizedNameMemberSeparator = '-';

  public static string JoinTypeNamesInjectively(string prefix, IReadOnlyCollection<string> memberNames) =>
    $"{prefix}{memberNames.Count}{SynthesizedNameMemberSeparator}"
      + string.Join(SynthesizedNameMemberSeparator, memberNames);

  /// <summary>
  /// True for a name <see cref="JoinTypeNamesInjectively"/> produced under <paramref name="prefix"/>.
  /// The ONE reader of the shape, so the count and the separator are each stated in one place.
  /// </summary>
  public static bool IsJoinedTypeName(string name, string prefix) {
    if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;

    var rest = name.AsSpan(prefix.Length);
    var digits = 0;
    while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;

    return digits > 0 && digits < rest.Length && rest[digits] == SynthesizedNameMemberSeparator;
  }

  /// <summary>
  /// True when a type ALREADY HELD in a resolved position may be replaced by whatever the
  /// whole-program table currently answers for its NAME. Three passes want that replacement, all for
  /// the same reason: a pre-scan registers an INCOMPLETE stand-in — an <c>IrPlaceholderType</c>, a
  /// field-less <c>IrStructType</c>, a case-less <c>IrEnumType</c> — and the completed declaration
  /// arrives later, under the same name, so the held object has to be swapped for it.
  ///
  /// ⚠ A RANGED PRIMITIVE IS NEVER THAT STAND-IN, and re-resolving one by bare name is how a
  /// file-private typealias came to govern another file's arithmetic. It is minted whole — range and
  /// all — in a single step, so there is no incomplete version of one to upgrade. A DIFFERENT ranged
  /// type answering to the same name is therefore not this declaration's completed form: it is
  /// another FILE's declaration of the name, and <c>TypeDefs</c> is keyed by bare name and cannot
  /// tell the two apart. Measured both ways on one flat table — a program's
  /// <c>Word32 = int(0 to 255)</c> truncated the 32-bit words of the <c>Word32</c> that
  /// <c>stdlib/Sha256.maxon</c> declares for itself, and a program's
  /// <c>DecimalDigit = int(0 to 100000)</c> had 70000 come back as 880 through the
  /// <c>int(0 to 9)</c> that <c>stdlib/Builtins.maxon</c> declares for itself. Whichever
  /// declaration reached the table last decided the other file's answer.
  ///
  /// ⚠ AN UNBOUND TYPE PARAMETER IS NEVER THAT STAND-IN EITHER: it IS the abstraction, and
  /// monomorphization — not a name lookup — is what replaces it. Its name is whatever the generic
  /// declared (<c>Key</c>, <c>Value</c>, <c>Element</c>, <c>T</c>), so a user type of that name would
  /// answer for it, which is the hazard <c>SemanticCheckPass.ResolveArrayElementType</c> states in
  /// full. That clause lived at ONE of the three call sites and not the other two — the same fact in
  /// two places, with only one of them true of the rule.
  ///
  /// Stated ONCE, because the three readers must agree: disagreeing, they would give one name two
  /// widths within a single compile, which is the shape that reaches the backend with no diagnostic.
  /// </summary>
  public static bool MayBeRefreshedByName(IrType held) =>
    held is not IrRangedPrimitiveType and not IrTypeParameterType;

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

  /// The type parameter every element-bearing container declares its element under
  /// (`Array uses Element`, `__ManagedMemory with Element`). It is the key three different
  /// passes read a concrete instance's element type back out of.
  public const string ElementTypeParamName = "Element";

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
      IReadOnlyDictionary<string, long>? constArgs) =>
    InstanceSpelling(sourceName, typeArgs, constArgs, static t => t.Name);

  /// <summary>
  /// ⭐ WHICH generic instance a fully-resolved DECLARATION denotes — <see cref="InstanceKey"/>'s
  /// question asked of declarations rather than of spellings, and the one place two files' same-named
  /// declarations of one alias are compared.
  ///
  /// Identical to the key above except that a type argument is spelled by
  /// <see cref="TypeArgIdentity"/>, so <c>Array with Cell</c> over <c>int(0 to 100000)</c> and
  /// <c>Array with Cell</c> over <c>int(0 to 255)</c> — one key, two instances — come apart.
  /// </summary>
  public static string InstanceIdentity(string sourceName, IReadOnlyDictionary<string, IrType> typeArgs,
      IReadOnlyDictionary<string, long>? constArgs) =>
    InstanceSpelling(sourceName, typeArgs, constArgs, TypeArgIdentity);

  /// <summary>
  /// The NAME a generic instance carries when the alias name declaring it is CONTESTED: the
  /// structural name the field-alias mint would have invented had no file declared one, except that
  /// each type argument is spelled by <see cref="TypeArgIdentity"/> — otherwise the two declarations
  /// the contest is ABOUT would mint one name between them, since it is precisely a type argument's
  /// NAME they agree on and its meaning they do not.
  ///
  /// It lives here rather than in the parser that mints it because a SECOND party has to recognise
  /// one: every reuse scan keyed on <see cref="InstanceKey"/> would otherwise hand a contested
  /// instance to the other file's spelling of the same by-name key, which is the defect the rename
  /// exists to close, re-entered one door along. Recognising it is asking whether a registered alias
  /// is spelled exactly this way for its own arguments — derived, so there is no second table to
  /// keep in step.
  /// </summary>
  public static string ContestedInstanceName(string sourceName,
      IReadOnlyDictionary<string, IrType> typeArgs, IReadOnlyDictionary<string, long>? constArgs) =>
    $"{sourceName}_{InstanceNameSuffix(constArgs, typeArgs.Values.Select(TypeArgIdentity))}";

  /// The shared body of the two spellings above — they differ in HOW a type argument is named and in
  /// nothing else, and a second hand-written copy of the sort-and-join is exactly the drift
  /// <see cref="InstanceKey"/>'s own header warns about, one call site further out.
  private static string InstanceSpelling(string sourceName, IReadOnlyDictionary<string, IrType> typeArgs,
      IReadOnlyDictionary<string, long>? constArgs, Func<IrType, string> spellTypeArg) {
    var args = typeArgs.Select(kv => $"{kv.Key}={spellTypeArg(kv.Value)}").ToList();
    args.Sort(StringComparer.Ordinal);
    return $"{sourceName}<{string.Join(",", args)}|{string.Join(",", ConstArgSegments(constArgs))}>";
  }

  /// <summary>
  /// ⭐ A type argument spelled so that two DECLARATIONS OF ONE NAME are told apart — the segment
  /// <see cref="InstanceIdentity"/> and the contested-alias mint are both built from.
  ///
  /// A plain <c>typealias</c> is file-local (specs/duplicate-typealias.md), so two files may each
  /// declare <c>Cell</c> over a different range and both declarations are legal and distinct. A
  /// NAME therefore cannot be a type argument's identity in a program that has such a pair, which is
  /// exactly what <see cref="InstanceKey"/> reads and deliberately cannot see — it is asked from
  /// parsers holding a placeholder for the same name, so a name is the only thing it MAY read.
  /// This is the other question: not "are these two spellings the same instance?" but "do these two
  /// declarations denote the same instance?", asked only where both are fully resolved.
  ///
  /// An INTEGER range is spelled with its inclusive upper, so <c>0 to 100</c> and <c>0 upto 101</c> —
  /// one range written two ways — cannot read as two instances. A FLOAT range has no such
  /// normalization (its exclusive upper is not a value), so the two forms keep the two spellings the
  /// source has, which is what they are.
  ///
  /// ⚠ THE BASE TYPE IS PART OF THE SPELLING, AND LEAVING IT OUT REOPENED THE WHOLE DEFECT.
  /// A name and a pair of bounds do not determine a type: <c>float(0 to 100000)</c> and
  /// <c>int(0 to 100000)</c>, declared under one alias name in two files, spelled the same
  /// <c>Cell_0to100000</c> — so the contest test read TWO instances as one, nothing was renamed, and
  /// the program printed a double's bits as an integer in one file order and <c>0</c> in the other.
  /// A projection that drops a distinguishing field does not report a difference; it reports
  /// agreement, which is the failure mode this whole record exists to remove.
  ///
  /// ⚠ THE BOUNDS ARE JOINED BY A WORD, NOT BY AN UNDERSCORE, AND THAT IS NOT COSMETIC. This
  /// segment ends up inside an emitted type name, and <c>MlirPrinter</c>'s IR dump reads a trailing
  /// <c>_&lt;digits&gt;</c> on ANY identifier as a per-scope COUNTER and renumbers it. Spelled
  /// <c>Cell_0_100000</c>, the two contested instances printed as one name — <c>Cell_0_0</c> — so a
  /// golden recorded two different destructors under one header. Spelled <c>Cell_0to100000</c>, the
  /// last underscore is not followed by digits alone and the name is left as it is.
  /// </summary>
  public static string TypeArgIdentity(IrType typeArg) {
    if (typeArg is not IrRangedPrimitiveType ranged) return typeArg.Name;

    return ranged.IsFloatBased
      ? $"{ranged.Name}_{ranged.BaseType.Name}_{RangeBoundSegment(ranged.FloatLower)}"
        + (ranged.UpperInclusive ? InclusiveRangeJoin : ExclusiveRangeJoin)
        + RangeBoundSegment(ranged.FloatUpper)
      : $"{ranged.Name}_{ranged.BaseType.Name}_{RangeBoundSegment(ranged.IntLower)}{InclusiveRangeJoin}{RangeBoundSegment(ranged.InclusiveIntUpper)}";
  }

  // How the two bounds are joined, and what stands in for the two characters a bound can carry that
  // a synthesized type NAME may not. The stand-ins are substitutions rather than deletions because
  // the segment has to stay INJECTIVE: two bounds differing only in a sign are two ranges, and a
  // name that dropped the sign would hand them one instance. The joins are the source keywords.
  private const string InclusiveRangeJoin = "to";
  private const string ExclusiveRangeJoin = "upto";
  private const string NegativeMark = "n";
  private const string DecimalPointMark = "p";

  /// A range bound as a segment of a synthesized type name. <c>long.MinValue</c> has no positive
  /// magnitude, so the minus sign is dropped from the TEXT rather than negated out of the number.
  private static string RangeBoundSegment(long bound) =>
    bound < 0
      ? NegativeMark + bound.ToString(CultureInfo.InvariantCulture)[1..]
      : bound.ToString(CultureInfo.InvariantCulture);

  /// A float bound, whose round-trip form additionally carries a decimal point and may carry an
  /// exponent. An exponent's <c>+</c> is dropped rather than stood in for: it is redundant with the
  /// bare digits that follow, so dropping it cannot merge two different bounds.
  private static string RangeBoundSegment(double bound) =>
    bound.ToString("R", CultureInfo.InvariantCulture)
      .Replace("-", NegativeMark).Replace(".", DecimalPointMark).Replace("+", "");

  public Dictionary<string, IrType> TypeParams { get; }
  public bool IsTuple { get; }
  // True when this type represents a typealias of an interface (e.g., typealias ElementIterable = Iterable with Element)
  public bool IsInterfaceAlias { get; }
  // Maps type parameter names to required interface names (from where clauses)
  public Dictionary<string, List<string>> WhereConstraints { get; }
  // Inner ranged primitive typealiases declared inside this generic type body.
  // Each concrete instantiation gets a nominally distinct copy of these aliases.
  public Dictionary<string, IrRangedPrimitiveType> InnerRangedAliases { get; } = [];
  // Ranged typealiases an `extension` block declares over this generic type. ONE type for every
  // instantiation, reachable through the `Instance.Alias` spelling as well as by its bare name.
  public Dictionary<string, IrRangedPrimitiveType> ExtensionRangedAliases { get; } = [];
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

  /// <summary>
  /// The mangled name IS a tuple's identity — <c>_typeRegistry</c> and <c>IrModule.TypeDefs</c> are
  /// keyed by it and <c>GetOrCreateTupleType</c> hands back whatever was interned under it first — so
  /// two DIFFERENT tuple types that spell alike do not clash, they silently BECOME one: the second
  /// takes the first's field table, and <c>t._0.p</c> then reads the wrong slot with no diagnostic.
  /// The join therefore has to be injective, which is why it is
  /// <see cref="IrType.JoinTypeNamesInjectively"/>'s job and not this mint's — the error-union mint
  /// needs the identical property for the identical reason.
  /// </summary>
  public const string TupleTypeNamePrefix = "__Tuple";

  public static string TupleMangledName(List<IrType> elementTypes) =>
    IrType.JoinTypeNamesInjectively(TupleTypeNamePrefix, [.. elementTypes.Select(TupleElementName)]);

  /// <summary>
  /// How one element is spelled inside a tuple's name. A tuple's identity is its ELEMENT TYPES, so an
  /// element that is itself a tuple is spelled by its own STRUCTURAL name and never by whatever alias
  /// a source file happened to give it: <c>typealias Pair = (Num, Num)</c> in element position has to
  /// join as <c>__Tuple2-i64-i64</c>, or <c>(Pair, Num)</c> and <c>((Num, Num), Num)</c> become two
  /// names for one type and a call from either to the other is rejected as a type mismatch.
  ///
  /// <c>IrType.Resolve</c> alone cannot do it: it unwraps a ranged primitive (so
  /// <c>(Integer, Integer)</c> and <c>(int, int)</c> agree) and a tuple ALIAS is not one — it is an
  /// <c>IrStructType</c> carrying the tuple's fields under the alias's own Name.
  /// </summary>
  private static string TupleElementName(IrType elementType) {
    var resolved = IrType.Resolve(elementType);
    return resolved is IrStructType { IsTuple: true } tuple
      ? TupleMangledName([.. tuple.Fields.Select(f => f.Type)])
      : resolved.Name;
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

/// The declared NAME of a type whose value KIND alone does not identify it — a struct, an
/// enum/union, or an interface — and null for a primitive, which needs none.
///
/// ⭐ ONE PLACE, because two readers ask it about the same thing: the parser stamps it on every op
/// that carries a struct/enum/interface value (`MaxonFieldAccessOp` and friends), and
/// `CloneBodySynthesis` stamps it on the field accesses it synthesizes. A second copy of the arm
/// list is how one of them comes to answer null for a kind the other names.
public static class NamedIrType {
  public static string? NameOf(IrType type) => type switch {
    IrStructType structType => structType.Name,
    IrEnumType enumType => enumType.Name,
    IrInterfaceType interfaceType => interfaceType.Name,
    _ => null
  };
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

  /// True if any member enum has associated values (so the error flag may carry a heap pointer).
  public bool AnyMemberHasAssociatedValues => Members.Any(m => m.HasAssociatedValues);

  /// <summary>
  /// This name is a TABLE KEY, not a label: <c>ParseTryBlock</c> writes the union into
  /// <c>_typeRegistry</c> under it and the handler's <c>match</c> reads the union back out by it
  /// (<c>2-Parser.EmitTryBlockBindingDeclaration</c> stores it as the binding's structTypeName). So a
  /// second union spelling the same name does not clash — it REPLACES the first, and a `try` whose
  /// handler is parsed after a nested `try` registered a same-spelled union resolves its patterns
  /// against the WRONG member list. Hence <see cref="IrType.JoinTypeNamesInjectively"/>: the tuple
  /// mint needs the identical property for the identical reason, so neither states it for itself.
  /// </summary>
  public const string ErrorUnionTypeNamePrefix = "__ErrorUnion";

  private static string FormatName(IReadOnlyList<IrEnumType> members) =>
    IrType.JoinTypeNamesInjectively(ErrorUnionTypeNamePrefix, [.. members.Select(m => m.Name)]);

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
