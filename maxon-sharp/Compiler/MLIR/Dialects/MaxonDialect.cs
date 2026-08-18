using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Compiler.Ir.Dialects;

public enum MaxonValueKind { Integer, Float, Float32, Bool, Byte, Short, Struct, Enum, Function, TypeParameter, ErrorUnion, CString }

public static class MaxonValueKindExtensions {
  public static IrType ToIrType(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => IrType.I64,
    MaxonValueKind.Float => IrType.F64,
    MaxonValueKind.Float32 => IrType.F32,
    MaxonValueKind.Bool => IrType.I1,
    MaxonValueKind.Byte => IrType.I8,
    MaxonValueKind.Short => IrType.I16,
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require lookup via type registry, not ToIrType()"),
    MaxonValueKind.Enum => throw new InvalidOperationException("Enum kinds require lookup via type registry, not ToIrType()"),
    MaxonValueKind.Function => throw new InvalidOperationException("Function kinds require lookup via function type, not ToIrType()"),
    MaxonValueKind.TypeParameter => IrType.I64, // unresolved type parameter stored as i64
    MaxonValueKind.ErrorUnion => IrType.I64, // backed by an i64 (the error flag); the discriminant lives in a sibling slot
    MaxonValueKind.CString => IrType.CString,
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  /// <summary>
  /// Returns the element size in bytes for the value kind.
  /// Bool and Byte use 1 byte; Integer, Float, Struct refs, Enum, Function use 8 bytes.
  /// </summary>
  public static int ElementSize(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Bool => 1,
    MaxonValueKind.Byte => 1,
    MaxonValueKind.Short => 2,
    MaxonValueKind.Integer => 8,
    MaxonValueKind.Float => 8,
    MaxonValueKind.Float32 => 4,
    MaxonValueKind.Struct => 8, // Struct references are pointers (8 bytes)
    MaxonValueKind.Enum => 8,   // Enums stored as i64
    MaxonValueKind.Function => 8, // Function pointers are 8 bytes
    MaxonValueKind.TypeParameter => 8, // Placeholder size before monomorphization
    MaxonValueKind.ErrorUnion => 8,    // Stored as i64 error-flag slot
    MaxonValueKind.CString => 8,       // Pointer-sized
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValue CreateValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new MaxonInteger(IrContext.Current.NextId()),
    MaxonValueKind.Float => new MaxonFloat(IrContext.Current.NextId()),
    MaxonValueKind.Float32 => new MaxonFloat(IrContext.Current.NextId()),
    MaxonValueKind.Bool => new MaxonBool(IrContext.Current.NextId()),
    MaxonValueKind.Byte => new MaxonByte(IrContext.Current.NextId()),
    MaxonValueKind.Short => new MaxonShort(IrContext.Current.NextId()),
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require a type name, use CreateStructValue() instead"),
    MaxonValueKind.Enum => new MaxonInteger(IrContext.Current.NextId()),
    MaxonValueKind.Function => new MaxonFunctionPtr(IrContext.Current.NextId()),
    MaxonValueKind.TypeParameter => new MaxonInteger(IrContext.Current.NextId()),
    MaxonValueKind.ErrorUnion => new MaxonInteger(IrContext.Current.NextId()),
    MaxonValueKind.CString => new MaxonCString(IrContext.Current.NextId()),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValueKind ToValueKind(this IrType type) {
    if (type == IrType.CString) return MaxonValueKind.CString;
    if (type == IrType.I64) return MaxonValueKind.Integer;
    if (type == IrType.F64) return MaxonValueKind.Float;
    if (type == IrType.F32) return MaxonValueKind.Float32;
    if (type == IrType.I1) return MaxonValueKind.Bool;
    if (type == IrType.I8) return MaxonValueKind.Byte;
    // Unsigned/narrowed integer types from ranged type optimal storage
    if (type == IrType.U8) return MaxonValueKind.Byte;
    if (type == IrType.I16 || type == IrType.U16) return MaxonValueKind.Short;
    if (type == IrType.I32 || type == IrType.U32 || type == IrType.U64) return MaxonValueKind.Integer;
    if (type is IrRangedPrimitiveType rpt) return rpt.BaseType.ToValueKind();
    if (type is IrEnumType) return MaxonValueKind.Enum;
    if (type is IrTypeParameterType) return MaxonValueKind.TypeParameter;
    if (type is IrStructType) return MaxonValueKind.Struct;
    if (type is IrFunctionType) return MaxonValueKind.Function;
    if (type is IrInterfaceType) return MaxonValueKind.Struct;
    throw new ArgumentOutOfRangeException(nameof(type), $"No MaxonValueKind for IrType: {type}");
  }

  public static StdValue CreateStdValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new StdI64(IrContext.Current.NextStdId()),
    MaxonValueKind.Float => new StdF64(IrContext.Current.NextStdId()),
    MaxonValueKind.Float32 => new StdF32(IrContext.Current.NextStdId()),
    MaxonValueKind.Bool => new StdBool(IrContext.Current.NextStdId()),
    MaxonValueKind.Byte => new StdI64(IrContext.Current.NextStdId()),
    MaxonValueKind.Short => new StdI64(IrContext.Current.NextStdId()),
    MaxonValueKind.Struct => new StdPtr(IrContext.Current.NextStdId()),
    MaxonValueKind.Enum => new StdI64(IrContext.Current.NextStdId()),
    MaxonValueKind.Function => new StdPtr(IrContext.Current.NextStdId()),
    MaxonValueKind.TypeParameter => new StdI64(IrContext.Current.NextStdId()),
    MaxonValueKind.CString => new StdI64(IrContext.Current.NextStdId()),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };
}

public enum MaxonBinOperator {
  Add, Sub, Mul, Div, Mod,
  Eq, Ne, Lt, Gt, Le, Ge,
  And, Or,
  BitAnd, BitOr, BitXor, Shl, Shr
}

public enum MaxonOpKind {
  Literal,
  Assign,
  Param,
  StructParam,
  FunctionParam,
  FunctionRef,
  ClosureCreate,
  ClosureEnvLoad,
  FunctionVarRef,
  IndirectCall,
  VarRef,
  StructVarRef,
  Bin,
  RefEq,
  Call,
  TryCall,
  ManagedMemCreateTryCall,
  CheckedDivTryCall,
  IteratorAdvance,
  IteratorCurrent,
  Trunc,
  IntToFloat,
  Cast,
  Sizeof,
  Abs,
  Sqrt,
  Floor,
  Ceil,
  Round,
  BitcastF64ToI64,
  BitcastI64ToF64,
  Min,
  Max,
  CondBr,
  Br,
  Switch,
  ScopeEnd,
  Return,
  Throw,
  StructLiteral,
  FieldAccess,
  FieldAssign,
  GlobalLoad,
  EnumLiteral,
  EnumConstruct,
  EnumTag,
  EnumPayload,
  EnumParam,
  EnumPayloadAssign,
  EnumVarRef,
  ErrorFlagToEnum,
  EnumRawValue,
  EnumStringRawValue,
  EnumStructRawValue,
  EnumStructRawField,
  EnumFunctionRawValue,
  EnumName,
  EnumOrdinal,
  GlobalStore,
  ManagedMemGet,
  ManagedMemSet,
  ManagedMemCreate,
  ManagedMemGrow,
  ManagedMemSetLength,
  ManagedMemClear,
  ManagedMemShift,
  ManagedMemRemove,
  ManagedMemByteGet,
  ManagedMemByteSet,
  ByteRangePanic,
  UcdByteLoad,
  UcdI64Load,
  StringLiteral,
  ByteStringLiteral,
  CharLiteral,
  ManagedMemAppend,
  StringInterp,
  ManagedMemSlice,
  ManagedMemCreateCursor,
  CursorCurrent,
  CursorIndex,
  CursorPeek,
  CStringToManaged,
  ManagedToCString,
  ManagedWriteStdout,
  ManagedWriteStderr,
  ManagedReadStdin,
  Panic,
  PanicDynamic,
  CallRuntime,
  MakeCharFromBytes,
  ManagedListCreate,
  ManagedListInsertValue,
  ManagedListInsertRelativeValue,
  ManagedListDetach,
  ManagedListRemove,
  ManagedListCount,
  ManagedListNodeValue,
  ManagedListNodeSetValue,
  ManagedListClear,
  ManagedListCursorReset,
  ManagedListCursorValue,
  ManagedListHeadPtr,
  ManagedListNodePtrNext,
  ManagedListNodePtrValue,
  AsyncCall,
  Await,
  TryAwait,
  CancelPromise,
  DebugStreamEnabled,
  DebugStreamNameId,
  DebugStreamPhase,
  DebugStreamEvent,
  DebugStreamText,
  CoveragePoint,
}

public abstract class MaxonOp : IPrintableOp {
  public abstract MaxonOpKind Kind { get; }
  public abstract string Mnemonic { get; }

  /// The values this op DEFINES — the exact dual of `Operands`, and THE single source of
  /// truth for what an op produces. `PrintableResults` renders it; the parser's ternary
  /// arm-move consults it to learn which SSA values CHANGED BLOCK when a region was
  /// relocated; and `VerifyOperandsAreDominated` consults it to learn where each value is
  /// defined.
  ///
  /// ABSTRACT ON PURPOSE, for the same reason `Operands` is: an op that defines nothing must
  /// say `=> []` out loud. A virtual `=> []` default means "defines nothing", so an author
  /// who simply forgets gets silence — and a definition invisible to the verifier makes every
  /// USE of that value look undominated, while a definition invisible to the arm-move leaves
  /// a VarInfo pointing at a block the value no longer lives in. That second one is not a
  /// diagnostic bug, it is a MISCOMPILE: it is exactly how a self-field read in a ternary's
  /// condition came to reference a value defined inside the ternary's true arm.
  public abstract IReadOnlyList<MaxonValue> Results { get; }

  /// Defaults to rendering `Results`. There is no reason to override it: unlike operands,
  /// no op names its results inside its `Mnemonic`.
  public IReadOnlyList<string> PrintableResults => [.. Results.Select(r => r.ToString())];

  /// The values this op READS. THE single source of truth for what an op consumes:
  /// `PrintableOperands` renders it, and DeadFunctionElimination's liveness scan walks it.
  ///
  /// ABSTRACT ON PURPOSE — an op with no operands must say `=> []` out loud. This is the
  /// Maxon tier's counterpart to `StandardOp.ReadValues`, which is abstract for the same
  /// reason, and the difference between them is what this bug was: a virtual `=> []` default
  /// means "reads nothing", so an op author who simply forgets gets silence instead of a
  /// compile error. An op whose reads are invisible to liveness is not an optimizer bug, it
  /// is a MISCOMPILE — the producer looks dead, gets deleted, and the op is left pointing at
  /// a value nothing defines. `maxon.enum_construct` was exactly that: a union case built in
  /// a global initializer (`var g = [Op.add(1)]`) lost the literal `1`, and `__module_init`
  /// failed to lower.
  public abstract IReadOnlyList<MaxonValue> Operands { get; }

  /// Defaults to rendering `Operands`. Override ONLY to change how operands are DISPLAYED
  /// (e.g. an op that already names its operand inside its `Mnemonic`) — never to change
  /// which values the op is understood to read. That fact has one home, above.
  public virtual IReadOnlyList<string> PrintableOperands => [.. Operands.Select(o => o.ToString())];

  public virtual IReadOnlyDictionary<string, IrAttribute> PrintableAttributes => new Dictionary<string, IrAttribute>();

  /// <summary>
  /// A field-for-field copy, for <see cref="Core.OpGraphCopier"/> — which then rebinds the copy's
  /// value AND type references. It is here, on the base, rather than 112 hand-written clone methods on the
  /// subclasses, because a hand-written one can FORGET a field and forgetting is silent: the copy
  /// would keep pointing at the template's value and the leak this exists to close would reopen for
  /// exactly one op. `MemberwiseClone` cannot forget.
  /// </summary>
  internal MaxonOp ShallowCopy() => (MaxonOp)MemberwiseClone();
}

/// Implemented by every op that READS A VARIABLE BY NAME. THE single source of truth for that
/// fact, in the same sense `MaxonOp.Operands` is for SSA values — and for the same reason: a var
/// read that liveness cannot see is a MISCOMPILE, not an optimizer wart. The assign feeding the
/// var looks dead, gets dropped, and its value is deleted out from under a live reader.
///
/// **The declaration belongs on the OP because that is where the var name already is.** It used
/// to be a `switch` inside DeadFunctionElimination naming four op types — and it was WRONG, in
/// precisely the way a list kept somewhere else always eventually is: `MaxonEnumPayloadAssignOp`
/// reads its enum var (its lowering `EmitLoad`s it, twice) and was not on the list. It was missed
/// because it spells the property `EnumVarName` rather than `VarName`, so it did not look like its
/// four siblings to anyone reading the list — which is exactly the kind of thing an author sees
/// when the contract is on the class in front of them and cannot see from another directory.
///
/// Contrast `Operands`, which is ABSTRACT so that an op with none must say `=> []` out loud. That
/// bar is right there and wrong here: every op has operands to declare, while five of ~100 read a
/// var — making the other ~95 write `=> null` would bury the five real answers in boilerplate
/// nobody reads, and a declaration nobody reads is not a safeguard. An interface is the same
/// contract scoped to the ops it means something for: implement it, or you do not read a var.
public interface IReadsVarByName {
  /// The variable this op reads. Not optional: an op implements this because it HAS one.
  string ReadVarName { get; }
}

public sealed class MaxonLiteralOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Literal;
  public override string Mnemonic => "maxon.literal";
  public MaxonValueKind ValueKind { get; }
  public long IntValue { get; }
  public double FloatValue { get; }
  public bool BoolValue { get; }
  public MaxonValue Result { get; }
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    ValueKind switch {
      MaxonValueKind.Integer => new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(IntValue, IrType.I64) },
      MaxonValueKind.Float => new Dictionary<string, IrAttribute> { ["value"] = new FloatAttr(FloatValue, IrType.F64) },
      MaxonValueKind.Float32 => new Dictionary<string, IrAttribute> { ["value"] = new FloatAttr(FloatValue, IrType.F32) },
      MaxonValueKind.Bool => new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(BoolValue ? 1 : 0, IrType.I1) },
      MaxonValueKind.Byte => new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(IntValue, IrType.I8) },
      MaxonValueKind.Short => new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(IntValue, IrType.I16) },
      MaxonValueKind.Struct => throw new InvalidOperationException("Struct literals are not MaxonLiteralOp"),
      MaxonValueKind.Enum => throw new InvalidOperationException("Enum literals are not MaxonLiteralOp"),
      _ => throw new ArgumentOutOfRangeException(),
    };

  public MaxonLiteralOp(long value) {
    ValueKind = MaxonValueKind.Integer;
    IntValue = value;
    Result = new MaxonInteger(IrContext.Current.NextId());
  }

  public MaxonLiteralOp(double value) {
    ValueKind = MaxonValueKind.Float;
    FloatValue = value;
    Result = new MaxonFloat(IrContext.Current.NextId());
  }

  public MaxonLiteralOp(double value, MaxonValueKind floatKind) {
    ValueKind = floatKind;
    FloatValue = value;
    Result = new MaxonFloat(IrContext.Current.NextId());
  }

  public MaxonLiteralOp(bool value) {
    ValueKind = MaxonValueKind.Bool;
    BoolValue = value;
    Result = new MaxonBool(IrContext.Current.NextId());
  }
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

public sealed class MaxonAssignOp(string varName, MaxonValue value, bool isDeclaration, bool isMutable, MaxonValueKind valueKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Assign;
  public override string Mnemonic => "maxon.assign";
  public string VarName { get; } = varName;
  public MaxonValue Value { get; } = value;
  public bool IsDeclaration { get; } = isDeclaration;
  public bool IsMutable { get; } = isMutable;
  public MaxonValueKind ValueKind { get; } = valueKind;
  public OwnershipFlags? OwnerFlags { get; set; }
  /// Allocator tests need deterministic heap traces; @heap opts out of stack promotion for that variable.
  public bool ForceHeap { get; set; }
  /// Reads the value being stored.
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [Value];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, IrAttribute> {
        ["var"] = new StringAttr(VarName),
      };
      if (ValueKind is not (MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.Function or MaxonValueKind.TypeParameter)) {
        attrs["kind"] = new TypeAttr(ValueKind.ToIrType());
      }
      if (IsDeclaration) attrs["decl"] = new IntegerAttr(1, IrType.I1);
      if (IsMutable) attrs["mut"] = new IntegerAttr(1, IrType.I1);
      return attrs;
    }
  }
}

public sealed class MaxonParamOp(int index, string name, MaxonValueKind kind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Param;
  public override string Mnemonic => "maxon.param";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public MaxonValueKind ValueKind { get; } = kind;
  public MaxonValue Result { get; } = kind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, IrAttribute> {
        ["index"] = new IntegerAttr(Index, IrType.I32),
        ["name"] = new StringAttr(Name),
      };
      // Function kinds carry no IrFunctionType payload, so ToIrType() cannot
      // reconstruct a printable type for them (it throws). Skip the type attr
      // for function-typed params, as we already do for type-parameter/struct/enum.
      if (ValueKind is not (MaxonValueKind.TypeParameter or MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.Function))
        attrs["type"] = new TypeAttr(ValueKind.ToIrType());
      return attrs;
    }
  }
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Struct parameter op: represents a struct being received as a function parameter.
// At the Maxon level the struct is a single logical param; at the Standard level
// it is flattened into individual scalar params per field.
public sealed class MaxonStructParamOp(int index, string name, string structTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.StructParam;
  public override string Mnemonic => $"maxon.struct_param @{StructTypeName}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public string StructTypeName { get; set; } = structTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), structTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Function parameter op: represents a function pointer being received as a function parameter.
public sealed class MaxonFunctionParamOp(int index, string name, IrFunctionType functionType) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FunctionParam;
  public override string Mnemonic => $"maxon.function_param";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public IrFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId(), functionType);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Function reference op: gets a pointer to a named function
public sealed class MaxonFunctionRefOp(string functionName, IrFunctionType functionType) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FunctionRef;
  public override string Mnemonic => $"maxon.function_ref @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public IrFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId(), functionType);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Creates a closure with captured values from the enclosing scope
public sealed class MaxonClosureCreateOp(string functionName, IrFunctionType functionType,
    List<MaxonValue> capturedValues, List<string> capturedNames,
    List<MaxonValueKind> capturedKinds, List<string?> capturedStructTypes) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ClosureCreate;
  public override string Mnemonic => $"maxon.closure_create @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public IrFunctionType FunctionType { get; } = functionType;
  public List<MaxonValue> CapturedValues { get; } = capturedValues;
  public List<string> CapturedNames { get; } = capturedNames;
  public List<MaxonValueKind> CapturedKinds { get; } = capturedKinds;
  public List<string?> CapturedStructTypes { get; } = capturedStructTypes;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId(), functionType);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => CapturedValues;
}

// Inside a capturing closure: loads a captured value from the environment pointer
//
// ⚠ THE LOAD MUST REPRODUCE THE CAPTURED VALUE'S IDENTITY, not merely its kind. A capture is the
// same value seen from inside the closure, so every rule that keys off what a value IS keys off it
// here too — which is why the Struct and Enum arms below carry the declared name. `functionType` is
// the third of them, and it was missing: a captured FUNCTION value arrived carrying no signature,
// the declared-type doors read null (their permissive answer, correctly — see MaxonFunctionPtr) and
// stopped applying, and `apply(g, ...)` with a `fn(Shade) returns Shade` in a `fn(Color) returns
// Color` parameter compiled and RAN from inside a closure while the identical line outside one was
// E3005.
public sealed class MaxonClosureEnvLoadOp(int index, string name, MaxonValueKind kind, string? structTypeName = null,
    Core.IrFunctionType? functionType = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ClosureEnvLoad;
  public override string Mnemonic => $"maxon.closure_env_load {Name}[{Index}]";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public MaxonValueKind ValueKind { get; } = kind;
  public string? StructTypeName { get; } = structTypeName;
  // `functionType` is deliberately not surfaced as a property: the lowering reads StructTypeName,
  // but nothing reads a signature off the OP — the readers are the declared-type doors, and they
  // hold the VALUE. It reaches them on Result, which is the only place it is wanted.
  public MaxonValue Result { get; } = kind switch {
    MaxonValueKind.Struct => new MaxonStruct(IrContext.Current.NextId(), structTypeName!),
    MaxonValueKind.Enum when structTypeName != null => new MaxonEnum(IrContext.Current.NextId(), structTypeName),
    MaxonValueKind.Function => new MaxonFunctionPtr(IrContext.Current.NextId(), functionType),
    _ => kind.CreateValue()
  };
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Function var ref: loads a function pointer from a variable in a different block
public sealed class MaxonFunctionVarRefOp(string varName, IrFunctionType functionType) : MaxonOp, IReadsVarByName {
  public override MaxonOpKind Kind => MaxonOpKind.FunctionVarRef;
  public override string Mnemonic => $"maxon.function_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string ReadVarName => VarName;
  public IrFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId(), functionType);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Indirect call op: calls a function through a function pointer
public sealed class MaxonIndirectCallOp(MaxonValue callee, IrFunctionType calleeType, List<MaxonValue> args,
    MaxonValueKind? resultKind = null, string? resultStructTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.IndirectCall;
  public override string Mnemonic => "maxon.indirect_call";
  public MaxonValue Callee { get; } = callee;
  public IrFunctionType CalleeType { get; } = calleeType;
  public List<MaxonValue> Args { get; } = args;
  // The SAME result-construction a direct call uses. This used to be a second,
  // hand-rolled copy that named only the Struct case, so an indirect call whose
  // return type was an enum or a union produced a bare MaxonInteger with the type
  // name dropped: `report(handler(x))` was rejected as "expected 'Outcome', got
  // 'int'", and a `match` on the result died with "Expected pattern value".
  // An indirect call knows the signature it returns without being told: it is the callee type's
  // own ReturnType. A DIRECT call has to be told (MaxonCallOp.ResultFnType), because the callee
  // is a name whose declared return may be an alias or a generic that only resolves later.
  public MaxonValue? Result { get; } =
    MaxonCallOp.CreateResult(resultKind, resultStructTypeName, calleeType.ReturnType as IrFunctionType);
  public MaxonValueKind? ResultKind { get; } = resultKind;
  public string? ResultStructTypeName { get; } = resultStructTypeName;
  public override IReadOnlyList<MaxonValue> Results => Result != null ? [Result] : [];
  public override IReadOnlyList<MaxonValue> Operands => [Callee, .. Args];
}

public sealed class MaxonVarRefOp(string varName, MaxonValueKind kind) : MaxonOp, IReadsVarByName {
  public override MaxonOpKind Kind => MaxonOpKind.VarRef;
  public override string Mnemonic => "maxon.var_ref";
  public string VarName { get; } = varName;
  public string ReadVarName => VarName;
  public MaxonValueKind ValueKind { get; } = kind;
  public MaxonValue Result { get; } = kind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, IrAttribute> { ["var"] = new StringAttr(VarName) };
      // Function kinds don't have a single IrType — the signature is carried
      // separately (on the var's declaration / call op). Print just the kind.
      if (ValueKind == MaxonValueKind.Function)
        attrs["kind"] = new StringAttr("fn");
      else
        attrs["type"] = new TypeAttr(ValueKind.ToIrType());
      return attrs;
    }
  }
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Struct var ref: loads a struct from a variable in a different block
public sealed class MaxonStructVarRefOp(string varName, string structTypeName) : MaxonOp, IReadsVarByName {
  public override MaxonOpKind Kind => MaxonOpKind.StructVarRef;
  public override string Mnemonic => $"maxon.struct_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string ReadVarName => VarName;
  public string StructTypeName { get; set; } = structTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), structTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

public sealed class MaxonBinOp(MaxonBinOperator op, MaxonValue lhs, MaxonValue rhs, MaxonValueKind operandKind,
    IrType? optimalType = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Bin;
  public override string Mnemonic => "maxon.binop";
  public MaxonBinOperator Operator { get; } = op;
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonValueKind OperandKind { get; } = operandKind;
  public IrType? OptimalType { get; } = optimalType;
  public bool IsUnsigned => OptimalType?.IsUnsigned ?? false;
  public MaxonValue Result { get; } = IsComparison(op)
      ? new MaxonBool(IrContext.Current.NextId())
      : operandKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Lhs, Rhs];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, IrAttribute> {
        ["op"] = new StringAttr(Operator.ToString().ToLowerInvariant()),
      };
      if (OperandKind is MaxonValueKind.Float or MaxonValueKind.Float32)
        attrs["kind"] = new TypeAttr(OperandKind.ToIrType());
      if (OptimalType != null)
        attrs["optimalType"] = new TypeAttr(OptimalType);
      return attrs;
    }
  }

  private static bool IsComparison(MaxonBinOperator op) =>
    op is MaxonBinOperator.Eq or MaxonBinOperator.Ne or MaxonBinOperator.Lt
      or MaxonBinOperator.Gt or MaxonBinOperator.Le or MaxonBinOperator.Ge;
}

/// Compares two struct references for identity (same heap address).
public sealed class MaxonRefEqOp(MaxonValue lhs, MaxonValue rhs, bool negate) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.RefEq;
  public override string Mnemonic => Negate ? "maxon.ref_ne" : "maxon.ref_eq";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public bool Negate { get; } = negate;
  public MaxonBool Result { get; } = new MaxonBool(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Lhs, Rhs];
}

public class MaxonCallOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Call;
  public override string Mnemonic => $"maxon.call @{Callee}";
  public string Callee { get; set; }
  public List<MaxonValue> Args { get; }
  public MaxonValue? Result { get; internal set; }
  public MaxonValueKind? ResultKind { get; }
  // The struct type name for calls returning a struct
  public string? ResultStructTypeName { get; set; }
  // The resolved function type when ResultKind == Function. Required by the
  // parser to recover the signature at `let f = call(...)` sites where the
  // callee's declared return is a function-type alias or a generic Value that
  // resolves to a function type after instantiation.
  //
  // Setting it also stamps the RESULT VALUE, because a declared-type door holds a value, not an
  // op: `apply(pick(), ...)` compares the shape of what `pick` returned, and by then the op that
  // produced it is no longer the one being looked at. One assignment, both readers.
  private IrFunctionType? _resultFnType;
  public IrFunctionType? ResultFnType {
    get => _resultFnType;
    set {
      _resultFnType = value;
      if (Result is MaxonFunctionPtr resultFnPtr) resultFnPtr.FunctionType = value;
    }
  }

  /// ⭐⭐ The interface (or `where`-constrained type-parameter) method signature this call
  /// dispatches through, for a call whose callee NAME — `ChunkMaker.makeChunk` — names no
  /// module function. Monomorphization devirtualizes such a callee later; until it does,
  /// `FindFunctionByExactName` answers null, and every question the parser asks about a call's
  /// `throws` is keyed off exactly that lookup. Answering them from the registry alone made all
  /// of them read "does not throw": the caught box leaked, `(e)` bound a raw int, a `try` on a
  /// non-throwing requirement was accepted (and branched on an error register nobody wrote),
  /// and a throwing one called WITHOUT `try` dropped the error.
  ///
  /// This is the only place those answers exist at parse time, which is where the bootstrap
  /// decides all of them. Null for an ordinary direct call, whose signature IS in the registry —
  /// so non-null also means "the requirement is authoritative", and a null `ThrowsTypeName`
  /// inside it means the method genuinely does not throw rather than that nobody knows.
  public IrInterfaceMethodSignature? DispatchedSignature { get; set; }

  // Whether each argument at the call site came from a mutable variable
  public List<bool>? ArgMutabilities { get; set; }
  // The variable name each argument came from (null for literals/expressions)
  public List<string?>? ArgVarNames { get; set; }
  // Set when a call appears as a statement with its result unused
  public bool IsDiscardedResult { get; set; }
  // Set when a call result is explicitly discarded via `let _ = func()`
  public bool IsLetDiscardResult { get; set; }
  public int? CallLine { get; set; }
  public int? CallColumn { get; set; }

  public MaxonCallOp(string callee, List<MaxonValue> args, MaxonValueKind? resultKind = null, string? resultStructTypeName = null) {
    Callee = callee;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    Result = CreateResult(resultKind, resultStructTypeName);
  }

  // Internal constructor preserving existing result for call site rewriting
  internal MaxonCallOp(string callee, List<MaxonValue> args, MaxonValue? existingResult, MaxonValueKind? resultKind, string? resultStructTypeName) {
    Callee = callee;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    Result = existingResult;
  }

  // Shared by every call op — direct, try, and indirect. A call's result value must
  // carry the IDENTITY of what it returns, not just its kind: the NAME of the struct/enum/union,
  // or — for a function — its SIGNATURE, which is the identity a `fn` kind cannot express. That
  // identity is what the argument checker, the declared-type doors and the `match` patterns
  // downstream key off.
  internal static MaxonValue? CreateResult(MaxonValueKind? resultKind, string? resultStructTypeName,
      IrFunctionType? resultFnType = null) {
    if (resultKind == MaxonValueKind.Struct)
      return new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!);
    if (resultKind == MaxonValueKind.Enum)
      return new MaxonEnum(IrContext.Current.NextId(), resultStructTypeName!);
    if (resultKind == MaxonValueKind.Function)
      return new MaxonFunctionPtr(IrContext.Current.NextId(), resultFnType);
    return resultKind?.CreateValue();
  }

  public override IReadOnlyList<MaxonValue> Results => Result != null ? [Result] : [];
  public override IReadOnlyList<MaxonValue> Operands => Args;
}

// Calls a throwing function and captures both the result and error flag.
// ErrorFlag is non-zero if the callee threw an error.
public class MaxonTryCallOp : MaxonCallOp {
  public override MaxonOpKind Kind => MaxonOpKind.TryCall;
  public override string Mnemonic => $"maxon.try_call @{Callee}";
  public MaxonInteger ErrorFlag { get; }
  /// Optional: the throws type for synthetic builtin callees whose name doesn't
  /// appear in the function registry (e.g. "__managed_socket_tcp_connect").
  /// When set, ParseTryExpression uses it directly instead of looking up the callee.
  public IrType? ThrowsType { get; set; }

  public MaxonTryCallOp(string callee, List<MaxonValue> args, MaxonValueKind? resultKind = null, string? resultStructTypeName = null)
    : base(callee, args, (MaxonValue?)null, resultKind, resultStructTypeName) {
    ErrorFlag = new MaxonInteger(IrContext.Current.NextId());
    Result = CreateResult(resultKind, resultStructTypeName);
  }

  // Internal constructor preserving existing result/errorFlag for call site rewriting
  internal MaxonTryCallOp(string callee, List<MaxonValue> args, MaxonValue? existingResult, MaxonInteger existingErrorFlag, MaxonValueKind? resultKind, string? resultStructTypeName)
    : base(callee, args, existingResult, resultKind, resultStructTypeName) {
    ErrorFlag = existingErrorFlag;
  }

  public override IReadOnlyList<MaxonValue> Results => Result != null ? [Result, ErrorFlag] : [ErrorFlag];
}

/// <summary>
/// MaxonTryCallOp variant for __ManagedMemory.create(count, elementSize).
/// Carries the compile-time element metadata needed by the lowering to compute
/// byte sizes. The callee is always "__managed_mem_create" and create is always
/// called via try (it throws __ManagedMemoryError.invalidAllocation), so there
/// is no plain MaxonCallOp variant.
/// </summary>
public sealed class MaxonManagedMemCreateTryCallOp(MaxonValue count, int elementSize, bool isBitPacked)
  : MaxonTryCallOp("__managed_mem_create", [count], MaxonValueKind.Struct, "__ManagedMemory") {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemCreateTryCall;
  public int ElementSize { get; } = elementSize;
  public bool IsBitPacked { get; } = isBitPacked;
}

/// <summary>
/// MaxonTryCallOp variant for an integer `a / b` / `a mod b` whose divisor the compiler could not
/// prove non-zero. `/` and `mod` are throwing at the language level: a possibly-zero divisor is
/// desugared to one of these, which throw <c>__DivisionByZeroError</c> when the divisor is 0, so
/// they are always reached via `try` (the parser enforces this, reusing E3057). A divisor proven
/// non-zero — a non-zero constant, or a ranged type excluding 0 — stays a bare MaxonBinOp.Div/.Rem
/// and never comes here.
///
/// Carries the operand signedness so the lowering picks the matching signed/unsigned division,
/// exactly as the bare MaxonBinOp path selects it from OptimalType (the ranged type is discarded by
/// lowering, so it must ride on the op).
/// </summary>
public sealed class MaxonCheckedDivTryCallOp : MaxonTryCallOp {
  public override MaxonOpKind Kind => MaxonOpKind.CheckedDivTryCall;
  public override string Mnemonic => $"maxon.checked_div @{Callee}";
  public bool IsMod { get; }
  public bool IsUnsigned { get; }

  public MaxonCheckedDivTryCallOp(MaxonValue dividend, MaxonValue divisor, bool isMod, bool isUnsigned,
      MaxonValueKind resultKind, IrType throwsType)
    : base(isMod ? "__checked_mod" : "__checked_div", [dividend, divisor], resultKind, null) {
    IsMod = isMod;
    IsUnsigned = isUnsigned;
    ThrowsType = throwsType;
  }
}

/// <summary>
/// Deferred iterator advance() call for for-in loops. Emitted by the parser when the concrete
/// iterator advance() function isn't known yet (the iterator type is a typealias that gets resolved
/// during monomorphization). Lowered to a MaxonTryCallOp by MonomorphizationPass.
/// advance() throws IterationError.exhausted when called past the last element; the error flag
/// is used by the for-loop header to exit the loop.
/// </summary>
public sealed class MaxonIteratorAdvanceOp(string iterableTypeName, string iteratorAliasName, List<MaxonValue> args) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.IteratorAdvance;
  public override string Mnemonic => $"maxon.iterator_advance @{IterableTypeName}";
  public string IterableTypeName { get; } = iterableTypeName;
  public string IteratorAliasName { get; } = iteratorAliasName;
  public List<MaxonValue> Args { get; } = args;
  public MaxonInteger ErrorFlag { get; } = new MaxonInteger(IrContext.Current.NextId());

  public override IReadOnlyList<MaxonValue> Results => [ErrorFlag];
  public override IReadOnlyList<MaxonValue> Operands => Args;
}

/// <summary>
/// Deferred iterator current() call for for-in loops. Emitted by the parser when the concrete
/// iterator current() function isn't known yet. Lowered to a MaxonCallOp by MonomorphizationPass.
/// current() is infallible — the iterator invariant guarantees the current position is valid.
/// </summary>
public sealed class MaxonIteratorCurrentOp(string iterableTypeName, string iteratorAliasName, List<MaxonValue> args,
    MaxonValueKind? elementKind = null, string? elementStructTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.IteratorCurrent;
  public override string Mnemonic => $"maxon.iterator_current @{IterableTypeName}";
  public string IterableTypeName { get; } = iterableTypeName;
  public string IteratorAliasName { get; } = iteratorAliasName;
  public List<MaxonValue> Args { get; } = args;
  public MaxonValue? Result { get; } = elementKind switch {
    MaxonValueKind.Integer => new MaxonInteger(IrContext.Current.NextId()),
    MaxonValueKind.Float or MaxonValueKind.Float32 => new MaxonFloat(IrContext.Current.NextId()),
    MaxonValueKind.Bool => new MaxonBool(IrContext.Current.NextId()),
    MaxonValueKind.Byte => new MaxonInteger(IrContext.Current.NextId()),
    MaxonValueKind.Short => new MaxonInteger(IrContext.Current.NextId()),
    MaxonValueKind.Struct => new MaxonStruct(IrContext.Current.NextId(), elementStructTypeName ?? "?"),
    MaxonValueKind.Enum => new MaxonEnum(IrContext.Current.NextId(), elementStructTypeName ?? "?"),
    MaxonValueKind.Function => throw new InvalidOperationException("Function values cannot be iterator elements"),
    // TypeParameter: treated as struct — monomorphization resolves the concrete type later
    MaxonValueKind.TypeParameter => new MaxonStruct(IrContext.Current.NextId(), elementStructTypeName ?? "Element"),
    null => null,
    _ => throw new ArgumentOutOfRangeException(nameof(elementKind), elementKind, "Unsupported element kind for iterator current")
  };
  public MaxonValueKind? ElementKind { get; } = elementKind;
  public string? ElementStructTypeName { get; } = elementStructTypeName;

  public override IReadOnlyList<MaxonValue> Results => Result != null ? [Result] : [];
  public override IReadOnlyList<MaxonValue> Operands => Args;
}

public sealed class MaxonTruncOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Trunc;
  public override string Mnemonic => "maxon.trunc";
  public MaxonValue Input { get; } = input;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonIntToFloatOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.IntToFloat;
  public override string Mnemonic => "maxon.int_to_float";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonCastOp(MaxonValue input, MaxonValueKind targetKind,
    IrType? sourceOptimalType = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Cast;
  public override string Mnemonic => $"maxon.cast";
  public MaxonValue Input { get; } = input;
  public MaxonValueKind TargetKind { get; } = targetKind;
  public IrType? SourceOptimalType { get; } = sourceOptimalType;
  public bool SourceIsUnsigned => SourceOptimalType?.IsUnsigned ?? false;
  public MaxonValue Result { get; } = targetKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["target"] = new TypeAttr(TargetKind.ToIrType()) };
}

public sealed class MaxonSizeofOp(string typeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Sizeof;
  public override string Mnemonic => "maxon.sizeof";
  public string TypeName { get; set; } = typeName;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["type"] = new StringAttr(TypeName) };
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

public sealed class MaxonAbsOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Abs;
  public override string Mnemonic => "maxon.abs";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonSqrtOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Sqrt;
  public override string Mnemonic => "maxon.sqrt";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonFloorOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Floor;
  public override string Mnemonic => "maxon.floor";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonCeilOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Ceil;
  public override string Mnemonic => "maxon.ceil";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonRoundOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Round;
  public override string Mnemonic => "maxon.round";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonBitcastF64ToI64Op(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.BitcastF64ToI64;
  public override string Mnemonic => "maxon.bitcast_f64_to_i64";
  public MaxonValue Input { get; } = input;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonBitcastI64ToF64Op(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.BitcastI64ToF64;
  public override string Mnemonic => "maxon.bitcast_i64_to_f64";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Input];
}

public sealed class MaxonMinOp(MaxonValue lhs, MaxonValue rhs) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Min;
  public override string Mnemonic => "maxon.min";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Lhs, Rhs];
}

public sealed class MaxonMaxOp(MaxonValue lhs, MaxonValue rhs) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Max;
  public override string Mnemonic => "maxon.max";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Lhs, Rhs];
}

public sealed class MaxonCondBrOp(MaxonValue condition, string thenBlock, string elseBlock) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CondBr;
  public override string Mnemonic => $"maxon.cond_br {Condition} [then: {ThenBlock}, else: {ElseBlock}]";
  public MaxonValue Condition { get; } = condition;
  public string ThenBlock { get; } = thenBlock;
  public string ElseBlock { get; } = elseBlock;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [Condition];
  // The mnemonic already names the condition, so rendering it again as an operand would
  // print it twice. The READ is still declared above, which is what liveness consumes.
  public override IReadOnlyList<string> PrintableOperands => [];
}

/// <summary>
/// "Execution reached coverage point N" — one increment of counter N in `__cov_image`, emitted only
/// under `--coverage`. Minted by the parser at each user statement and each `if` arm (see
/// <see cref="MaxonSharp.Compiler.Ir.Core.CoveragePointTable"/>), and carried unchanged through both
/// lowerings so the emitter can record where each point's code actually landed.
///
/// It reads and writes no IR value: its whole effect is a memory write outside the IR's value graph.
/// Both dead-code sweeps decide by ALLOW-LIST (`DeadFunctionElimination.PureProducerResultId` names
/// the removable producers; `DeadStoreEliminationPass` removes only ops with a non-negative
/// `PureResultId`), so this op survives them by construction rather than by a guard that could be
/// forgotten. It is minted BEFORE they run, so a statement whose code they delete still reports the
/// count its line really achieved — the line table, built from the code that survived, cannot.
/// </summary>
public sealed class MaxonCovPointOp(int pointId) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CoveragePoint;
  public override string Mnemonic => $"maxon.cov_point {PointId}";
  public int PointId { get; } = pointId;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [];
}

public sealed class MaxonBrOp(string target) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Br;
  public override string Mnemonic => $"maxon.br {Target}";
  public string Target { get; } = target;
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [];
}

/// One closed interval of a match's switch plan and the arm that owns it. Both ends are
/// inclusive; <see cref="long.MinValue"/> and <see cref="long.MaxValue"/> are the open ends of
/// a `min to x` / `x to max` arm rather than sentinels — they are the values those arms match.
public sealed record MaxonSwitchInterval(long Lo, long Hi, string TargetBlock) {
  public override string ToString() => Lo == Hi ? $"{Lo}:{TargetBlock}" : $"{Lo}..{Hi}:{TargetBlock}";
}

/// <summary>
/// The whole dispatch of a `match` whose every arm tests an integer — an exact value, an enum
/// case's tag, or an integer range. It carries the plan the LOWERING knows rather than leaving
/// a compare chain for a later pass to recognize back into a switch: the chain and the plan
/// would then be the same fact written twice, and the recognizer's guards (zero-based ordinals
/// only, `eq` predicates only) were that duplication's symptom.
///
/// <see cref="Intervals"/> is sorted by <see cref="MaxonSwitchInterval.Lo"/> and pairwise
/// DISJOINT: arm priority is already resolved into it, so an earlier arm owns any value two
/// arms both name and the strategy below is free to test the intervals in any order.
///
/// The scrutinee is named rather than passed as a value so the dispatch owns exactly ONE load
/// of it, wherever the plan's comparisons end up. A plan is only ever built for an i64
/// scrutinee (see Parser.TryBuildSwitchPlan), which is why no kind is carried here.
/// </summary>
public sealed class MaxonSwitchOp(string scrutineeVarName, List<MaxonSwitchInterval> intervals,
    string defaultBlock, string dispatchLabelPrefix) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Switch;
  public override string Mnemonic =>
    $"maxon.switch {ScrutineeVarName} [{string.Join(", ", Intervals)}] default={DefaultBlock}";
  public string ScrutineeVarName { get; } = scrutineeVarName;
  public List<MaxonSwitchInterval> Intervals { get; } = intervals;
  public string DefaultBlock { get; } = defaultBlock;
  /// Unique prefix (the match's label) for the block names the dispatch lowering mints.
  public string DispatchLabelPrefix { get; } = dispatchLabelPrefix;
  /// Reads nothing as an SSA value — the scrutinee is reached by name, like every var_ref.
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [];
}

/// <summary>
/// Emitted at every scope exit, before the terminating branch/return/throw.
/// Emitted at every scope exit, before the terminating branch/return/throw.
/// VarsToClean = managed struct vars introduced in this scope that the converter must decref.
/// KeepVars = vars to skip (e.g. the returned value that the caller takes ownership of).
/// The converter decrefs each var in VarsToClean that is actually managed (in varTypes),
/// zeros its stack slot (so other paths see NULL), and that's it — no external tracking needed.
/// </summary>
public sealed class MaxonScopeEndOp(IReadOnlyList<string> varsToClean, HashSet<string>? keepVars = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ScopeEnd;
  public override string Mnemonic => $"maxon.scope_end [{string.Join(", ", VarsToClean)}]";
  public IReadOnlyList<string> VarsToClean { get; } = varsToClean;
  public HashSet<string>? KeepVars { get; } = keepVars;

  /// <summary>
  /// Maps var name → (OwnershipFlags, StructTypeName) for each variable in VarsToClean.
  /// Populated by the parser so the lowering layer has ownership/type metadata
  /// without needing to infer it from string prefixes.
  /// </summary>
  public IReadOnlyDictionary<string, (OwnershipFlags Flags, string? StructTypeName)>? VarMetadata { get; init; }
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [];
}

public sealed class MaxonReturnOp(MaxonValue? value = null, bool isErrorPropagation = false) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Return;
  public override string Mnemonic => "maxon.return";
  public MaxonValue? Value { get; } = value;
  public bool IsErrorPropagation { get; } = isErrorPropagation;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => Value != null ? [Value] : [];
}

// ============================================================================
// Error handling operations
// ============================================================================

// Throws an error value and returns from the function
public sealed class MaxonThrowOp(MaxonValue errorValue, string errorTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Throw;
  public override string Mnemonic => $"maxon.throw @{ErrorTypeName}";
  public MaxonValue ErrorValue { get; } = errorValue;
  public string ErrorTypeName { get; } = errorTypeName;
  // True when the thrown value is a plain OWNED LOCAL binding (`throw e`, or a re-thrown caught error):
  // it owns its reference at rc=1 and scope-end TRANSFERS it (keepVars), so LowerThrow must NOT incref
  // it a second time (OPEN #63 — that would leak). Decided in the parser, which has the binding's
  // VarInfo; a self-field / parameter / fresh construct / call-result is NOT this and still needs the
  // transfer-incref. The lowering cannot re-derive it (an owned local and a borrowed self-field look
  // identical there — both non-temps).
  public bool IsOwnedLocalTransfer { get; init; }
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ErrorValue];
}

// ============================================================================
// Struct operations
// ============================================================================

// Creates a struct instance from field values: Point{x: 3, y: 4}
public sealed class MaxonStructLiteralOp(string typeName, List<(string FieldName, MaxonValue Value)> fieldValues) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.StructLiteral;
  public override string Mnemonic => $"maxon.struct_literal @{TypeName}";
  public string TypeName { get; set; } = typeName;
  public List<(string FieldName, MaxonValue Value)> FieldValues { get; } = fieldValues;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), typeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [.. FieldValues.Select(f => f.Value)];
  // The field values are rendered as part of the struct's attributes, not as operands, so
  // the operand line stays empty. The READS are declared above, which is what liveness uses.
  public override IReadOnlyList<string> PrintableOperands => [];
  // For array literals: tag prefix and count of sequential element variables
  public string? ArrayLiteralTag { get; set; }
  public int ArrayLiteralCount { get; set; }
  // Skip element zero-initialization (stack space reserved but not cleared)
  public bool SkipZeroInit { get; set; }
  /// When true, elements are bit-packed bools (elementSize stored as 0 sentinel in __ManagedMemory).
  public bool IsBitPacked { get; set; }
  // Source location for trace output (e.g. "main.maxon:12")
  public string? SourceLocation { get; set; }
}

// Reads a field: p.x
public sealed class MaxonFieldAccessOp(MaxonValue structValue, string typeName, string fieldName, MaxonValueKind resultKind, string? resultStructTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FieldAccess;
  public override string Mnemonic => $"maxon.field_access .{FieldName}";
  public MaxonValue StructValue { get; } = structValue;
  public string TypeName { get; set; } = typeName;
  public string FieldName { get; } = fieldName;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public string? ResultStructTypeName { get; set; } = resultStructTypeName;
  /// This read hands the field's value to MAXON SOURCE, so a negative internal sentinel must be
  /// clamped to 0 on the way out. Set only for `__ManagedMemory.capacity()`, whose result is a
  /// declared `int(0 to u64.max)` and is then done ARITHMETIC on by `Array.reserve` /
  /// `ensureCapacity`. The compiler's own reads of the same field are NOT this: they read the
  /// sentinel deliberately (the COW check), or they want the buffer's addressable extent rather
  /// than its owned slot count (`__ManagedSocket.sendFrom`'s range pre-check, which legitimately
  /// sends out of a read-only string literal).
  public bool ClampNegativeSentinel { get; init; }
  public MaxonValue Result { get; } = resultKind switch {
    MaxonValueKind.Struct => new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!),
    MaxonValueKind.Enum => new MaxonEnum(IrContext.Current.NextId(), resultStructTypeName!),
    _ => resultKind.CreateValue()
  };

  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [StructValue];

  /// Rebuild this read against a substituted operand and type names — THE way a clone of this op
  /// is assembled, for FunctionCloner and MonomorphizationPass alike.
  ///
  /// It exists because the two of them are two copies of one fact. `ClampNegativeSentinel` does
  /// not go through the constructor, so each cloner had to carry its own line copying it, and a
  /// clone that silently dropped it would restore the capacity-sentinel leak in exactly the
  /// programs that matter (`Array.capacity()` is generic, so EVERY array a program uses is a
  /// monomorphized clone). Nothing made the two agree; now there is only one of them, and it sits
  /// in the class that owns the property, so the next property added here is one edit rather than
  /// a remembered pair.
  public MaxonFieldAccessOp CloneWith(MaxonValue structValue, string typeName, string? resultStructTypeName) =>
    new(structValue, typeName, FieldName, ResultKind, resultStructTypeName) {
      ClampNegativeSentinel = ClampNegativeSentinel
    };
}

// Assigns to a field: p.x = 30
public sealed class MaxonFieldAssignOp(MaxonValue structValue, string typeName, string fieldName, MaxonValue newValue) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FieldAssign;
  public override string Mnemonic => $"maxon.field_assign .{FieldName}";
  public MaxonValue StructValue { get; } = structValue;
  public string TypeName { get; set; } = typeName;
  public string FieldName { get; } = fieldName;
  public MaxonValue NewValue { get; } = newValue;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [StructValue, NewValue];
}

// ============================================================================
// Global variable operations (for top-level var and static var)
// ============================================================================

public sealed class MaxonGlobalLoadOp(string globalName, MaxonValueKind kind, string? enumTypeName = null, string? structTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.GlobalLoad;
  public override string Mnemonic => $"maxon.global_load @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public MaxonValueKind ValueKind { get; } = kind;
  public string? EnumTypeName { get; } = enumTypeName;
  public string? StructTypeName { get; } = structTypeName;
  /// When set, indicates this is a lazy static field that needs guard-check before access.
  public string? LazyGuardName { get; set; }
  /// The init function to call when the lazy field has not been initialized yet.
  public string? LazyInitFuncName { get; set; }
  public MaxonValue Result { get; } = structTypeName != null ? new MaxonStruct(IrContext.Current.NextId(), structTypeName)
    : enumTypeName != null ? new MaxonEnum(IrContext.Current.NextId(), enumTypeName)
    : kind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> {
      ["global"] = new StringAttr(GlobalName),
      ["type"] = new TypeAttr(ValueKind is MaxonValueKind.Enum or MaxonValueKind.Struct ? IrType.I64 : ValueKind.ToIrType())
    };
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Creates an enum value for a specific case
public sealed class MaxonEnumLiteralOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumLiteral;
  public override string Mnemonic => $"maxon.enum_literal @{EnumTypeName}.{CaseName}";
  public string EnumTypeName { get; }
  public string CaseName { get; }
  public MaxonValueKind BackingKind { get; }
  public long IntValue { get; }
  public double FloatValue { get; }
  public MaxonEnum Result { get; }
  public override IReadOnlyList<MaxonValue> Results => [Result];

  public MaxonEnumLiteralOp(string enumTypeName, string caseName, long intValue) {
    EnumTypeName = enumTypeName;
    CaseName = caseName;
    BackingKind = MaxonValueKind.Integer;
    IntValue = intValue;
    Result = new MaxonEnum(IrContext.Current.NextId(), enumTypeName);
  }

  public MaxonEnumLiteralOp(string enumTypeName, string caseName, double floatValue) {
    EnumTypeName = enumTypeName;
    CaseName = caseName;
    BackingKind = MaxonValueKind.Float;
    FloatValue = floatValue;
    Result = new MaxonEnum(IrContext.Current.NextId(), enumTypeName);
  }
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Constructs an associated-value enum case: Container.value(42)
// For cases without associated values (e.g. Container.empty), Args is empty.
public sealed class MaxonEnumConstructOp(string enumTypeName, string caseName, long tagValue, List<MaxonValue> args) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumConstruct;
  public override string Mnemonic => $"maxon.enum_construct @{EnumTypeName}.{CaseName}";
  public string EnumTypeName { get; } = enumTypeName;
  public string CaseName { get; } = caseName;
  public long TagValue { get; } = tagValue;
  public List<MaxonValue> Args { get; } = args;
  public MaxonEnum Result { get; } = new MaxonEnum(IrContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => Args;
  // Source location for trace output (e.g. "main.maxon:12")
  public string? SourceLocation { get; set; }
}

// Extracts the tag (ordinal) from an associated-value enum
public sealed class MaxonEnumTagOp(MaxonValue enumValue, string enumTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumTag;
  public override string Mnemonic => $"maxon.enum_tag @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

// Extracts a payload value at a given index from an associated-value enum
public sealed class MaxonEnumPayloadOp(MaxonValue enumValue, string enumTypeName, int payloadIndex, MaxonValueKind resultKind, string? resultStructTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumPayload;
  public override string Mnemonic => $"maxon.enum_payload @{EnumTypeName}[{PayloadIndex}]";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public int PayloadIndex { get; } = payloadIndex;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public string? ResultStructTypeName { get; } = resultStructTypeName;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!)
    : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

// Enum parameter op: represents an enum being received as a function parameter
public sealed class MaxonEnumParamOp(int index, string name, string enumTypeName, MaxonValueKind backingKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumParam;
  public override string Mnemonic => $"maxon.enum_param @{EnumTypeName}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public MaxonEnum Result { get; } = new MaxonEnum(IrContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Writes a value back to a specific payload slot in an associated-value enum's heap block
public sealed class MaxonEnumPayloadAssignOp(string enumVarName, string enumTypeName, int payloadIndex, MaxonValue newValue) : MaxonOp, IReadsVarByName {
  public override MaxonOpKind Kind => MaxonOpKind.EnumPayloadAssign;
  public override string Mnemonic => $"maxon.enum_payload_assign @{EnumTypeName}[{PayloadIndex}]";
  public string EnumVarName { get; } = enumVarName;
  /// READ, not merely written: the lowering `EmitLoad`s this var to reach the enum's heap
  /// pointer before storing the payload into it. THE FIFTH FLAVOUR — and the one
  /// DeadFunctionElimination's switch missed for the reason this line now removes: the
  /// property is `EnumVarName`, so it never looked like its four `VarName` siblings.
  public string ReadVarName => EnumVarName;
  public string EnumTypeName { get; } = enumTypeName;
  public int PayloadIndex { get; } = payloadIndex;
  public MaxonValue NewValue { get; } = newValue;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [NewValue];
}

// Enum var ref: loads an enum from a variable in a different block
public sealed class MaxonEnumVarRefOp(string varName, string enumTypeName, MaxonValueKind backingKind) : MaxonOp, IReadsVarByName {
  public override MaxonOpKind Kind => MaxonOpKind.EnumVarRef;
  public override string Mnemonic => $"maxon.enum_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string ReadVarName => VarName;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public MaxonEnum Result { get; } = new MaxonEnum(IrContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Converts an error flag (ordinal+1) back to a typed enum value (ordinal)
// For simple error enums, subtracts 1 from the flag to recover the ordinal.
// For associated-value error enums, the flag is a heap pointer (no arithmetic needed).
public sealed class MaxonErrorFlagToEnumOp(MaxonValue errorFlag, string enumTypeName, MaxonValueKind backingKind, bool hasAssociatedValues) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ErrorFlagToEnum;
  public override string Mnemonic => $"maxon.error_flag_to_enum @{EnumTypeName}";
  public MaxonValue ErrorFlag { get; } = errorFlag;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public bool HasAssociatedValues { get; } = hasAssociatedValues;
  public MaxonEnum Result { get; } = new MaxonEnum(IrContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ErrorFlag];
}

// Accesses .rawValue on an enum value
public sealed class MaxonEnumRawValueOp(MaxonValue enumValue, string enumTypeName, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumRawValue;
  public override string Mnemonic => $"maxon.enum_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Float or MaxonValueKind.Float32
    ? new MaxonFloat(IrContext.Current.NextId())
    : new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

// Accesses .rawValue on a string or char-backed enum, returning String or Character
public sealed class MaxonEnumStringRawValueOp(MaxonValue enumValue, string enumTypeName, bool isChar) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumStringRawValue;
  public override string Mnemonic => $"maxon.enum_string_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public bool IsChar { get; } = isChar;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), isChar ? "Character" : "String");
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

// Accesses .rawValue on a struct-backed enum, returning the backing struct type
public sealed class MaxonEnumStructRawValueOp(MaxonValue enumValue, string enumTypeName, string structTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumStructRawValue;
  public override string Mnemonic => $"maxon.enum_struct_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public string StructTypeName { get; } = structTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), structTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

/// <summary>
/// `e.rawValue.field` on a struct-backed enum, read as ONE scalar.
///
/// A struct-backed enum's raw value is a per-variant COMPILE-TIME CONSTANT — the struct
/// literal written on each case — so reading a field of it is a pure ordinal-to-constant
/// lookup. Materializing the struct to get there costs a heap allocation plus a select
/// chain PER FIELD (ordinal → that field's constant), and the caller wanted one field: on
/// shv2's `TargetOp`, whose backing struct has six fields across ~40 variants, a single
/// `op.rawValue.implicitDefs` paid an `mm_alloc` and roughly 960 ops to produce one
/// integer. It was the second-largest allocating type in that whole compiler.
///
/// So the pair is emitted FUSED, at the point the parser sees it, rather than built and
/// then pattern-matched apart later. Only a LEAF field fuses; a nested-struct field still
/// needs the struct materialized and falls back to MaxonEnumStructRawValueOp.
/// </summary>
public sealed class MaxonEnumStructRawFieldOp(
    MaxonValue enumValue, string enumTypeName, string structTypeName, string fieldName,
    MaxonValueKind resultKind, string? resultTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumStructRawField;
  public override string Mnemonic => $"maxon.enum_struct_rawfield @{EnumTypeName}.{FieldName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public string StructTypeName { get; } = structTypeName;
  public string FieldName { get; } = fieldName;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public string? ResultTypeName { get; } = resultTypeName;
  public MaxonValue Result { get; } = resultKind switch {
    MaxonValueKind.Struct => new MaxonStruct(IrContext.Current.NextId(), resultTypeName!),
    MaxonValueKind.Enum => new MaxonEnum(IrContext.Current.NextId(), resultTypeName!),
    _ => resultKind.CreateValue()
  };
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

// Function-backed enum .rawValue: lowers to a select chain mapping the case
// ordinal to one of N function pointers. The signature is carried so callers
// can dispatch the resulting MaxonFunctionPtr via the usual indirect-call path.
public sealed class MaxonEnumFunctionRawValueOp(MaxonValue enumValue, string enumTypeName, IrFunctionType signature) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumFunctionRawValue;
  public override string Mnemonic => $"maxon.enum_function_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public IrFunctionType Signature { get; } = signature;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId(), signature);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

// Accesses .name on an enum value, returning the case name as a String
public sealed class MaxonEnumNameOp(MaxonValue enumValue, string enumTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumName;
  public override string Mnemonic => $"maxon.enum_name @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "String");
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

// Accesses .ordinal on an enum value, returning the zero-based declaration position as i64
public sealed class MaxonEnumOrdinalOp(MaxonValue enumValue, string enumTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumOrdinal;
  public override string Mnemonic => $"maxon.enum_ordinal @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [EnumValue];
}

public sealed class MaxonGlobalStoreOp(string globalName, MaxonValue value, MaxonValueKind kind, string? enumTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.GlobalStore;
  public override string Mnemonic => $"maxon.global_store @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public MaxonValue Value { get; } = value;
  public MaxonValueKind ValueKind { get; } = kind;
  /// The union type this slot holds, mirroring <see cref="MaxonGlobalLoadOp.EnumTypeName"/>.
  /// Lowering needs it to tell a BOXED union (a refcounted heap record the slot owns, so the
  /// store must release the old occupant and retain the new one) from a scalar enum (a bare
  /// discriminant written straight into the slot). The store op used to carry no type at all,
  /// which is why lowering had to guess from the kind and got the boxed case wrong.
  public string? EnumTypeName { get; } = enumTypeName;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [Value];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> {
      ["global"] = new StringAttr(GlobalName),
      ["type"] = new TypeAttr(ValueKind is MaxonValueKind.Enum or MaxonValueKind.Struct ? IrType.I64 : ValueKind.ToIrType())
    };
}

// ============================================================================
// Managed memory operations (for __ManagedMemory builtin intrinsics)
// ============================================================================

/// How an Array/__ManagedMemory element type is represented inside the backing buffer.
///
/// Three places must agree on this: the parser (which lowers `for x in arr` to an
/// index loop), MonomorphizationPass (which re-derives it once a generic Element is
/// bound), and MaxonToStandardConversion (which emits the load). They must share one
/// implementation, because the disagreements are silent and severe: classify a struct
/// element as a scalar and its heap pointer is loaded as an integer; classify a simple
/// enum as a struct and its *ordinal* gets passed to mm_incref as if it were a pointer.
public readonly record struct ManagedElementInfo(
  MaxonValueKind Kind,
  bool IsStructElement,
  string? StructElementTypeName,
  IrType? ElementStorageType) {

  /// Unbound type parameter — nothing can be decided until monomorphization binds it.
  /// Callers must carry TypeParamName on the op and re-derive after substitution.
  public static readonly ManagedElementInfo Unresolved =
    new(MaxonValueKind.TypeParameter, false, null, null);

  /// Re-wrap a cloned managed_mem_get's result in the value class that NAMES the concrete
  /// element type.
  ///
  /// MaxonManagedMemGetOp.Result is a bare MaxonInteger even for heap elements (the buffer
  /// holds a pointer). Monomorphization's CloneAssignOp resolves a TypeParameter assign's kind
  /// from the mapped value's CLASS — so handing it that bare integer makes a String element
  /// come out as MaxonValueKind.Integer, and `item == element` silently degrades from
  /// String.equals into a pointer comparison. Simple enums are genuinely raw ordinals and
  /// correctly stay unwrapped.
  public MaxonValue WrapResult(MaxonValue result) => (Kind, StructElementTypeName) switch {
    (MaxonValueKind.Struct, not null) => new MaxonStruct(result.Id, StructElementTypeName),
    (MaxonValueKind.Enum, not null) => new MaxonEnum(result.Id, StructElementTypeName),
    _ => result
  };

  /// Re-derive the representation for a managed_mem_get being cloned by monomorphization.
  /// `boundElement` is the concrete type the op's TypeParamName resolved to, or null when the
  /// element was already concrete at parse time — in which case there is nothing to re-derive
  /// and only the type's NAME can move under substitution.
  ///
  /// Both cloners route through here. They carry different substitution types, so this is the
  /// only place the rules exist; letting them drift means a struct element gets loaded as a
  /// raw integer in one path and as a heap pointer in the other.
  public static ManagedElementInfo ForSubstitutedOp(
    MaxonManagedMemGetOp op, IrType? boundElement, Func<string, string> substituteName) {
    if (boundElement != null) return FromElementType(boundElement);

    return new ManagedElementInfo(op.ResultKind, op.IsStructElement,
      op.StructElementTypeName == null ? null : substituteName(op.StructElementTypeName),
      op.ElementStorageType);
  }

  public static ManagedElementInfo FromElementType(IrType elementType) {
    if (elementType is IrTypeParameterType) return Unresolved;

    // Ranged primitives (e.g. `Score = int(0 to 100)`) are laid out at their OPTIMAL
    // width, not their source-level base width — the buffer's element_size was computed
    // from it. Loading such a slot as i64 would read across into the next element.
    //
    // Bare I8/I16 elements (byte-string literals emit Element = I8 to mean "unsigned
    // byte buffer") promote to the unsigned variant so codegen zero-extends: without
    // this a byte-string read sign-extends and turns 0xFF into -1.
    var loadType = elementType switch {
      IrRangedPrimitiveType rpt => rpt.OptimalType,
      _ when elementType == IrType.I8 => IrType.U8,
      _ when elementType == IrType.I16 => IrType.U16,
      _ => elementType
    };
    var kind = loadType.ToValueKind();

    // Unions (enums carrying associated values) are heap-allocated and refcounted.
    // Simple enums are raw i64 ordinals and must NEVER be treated as pointers.
    var isUnion = elementType is IrEnumType et && et.Cases.Any(c => c.AssociatedValues?.Count > 0);
    var isStruct = kind == MaxonValueKind.Struct || isUnion;

    // Only meaningful for scalars: preserves signedness that ToValueKind collapses
    // (it maps both I8 and U8 to Byte), so codegen can pick movsx vs movzx.
    var storageType = !isStruct && loadType is not IrEnumType ? loadType : null;

    return new ManagedElementInfo(kind, isStruct, isStruct ? elementType.Name : null, storageType);
  }
}

// Get element at index from managed buffer: managed.get(index)
// Element size is read from the managed struct's element_size field at runtime.
// When IsStructElement is true, the element data is stored inline in the buffer
// and the result is a pointer to the element's location (not a loaded value).
public sealed class MaxonManagedMemGetOp(MaxonValue managedStruct, MaxonValue index, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemGet;
  public override string Mnemonic => "maxon.managed_mem_get";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public bool IsStructElement { get; init; }
  /// The concrete struct type name when IsStructElement is true
  public string? StructElementTypeName { get; init; }
  /// When ResultKind is TypeParameter, this identifies which type param (e.g., "Key", "Value", "Element")
  public string? TypeParamName { get; init; }
  /// When true, the caller guarantees 0 <= index < length so lowering skips the bounds check.
  /// Set by the parser's for-in lowering, whose header re-reads length and tests i < length
  /// on every iteration — that test IS the bounds check, so repeating it here is dead weight.
  public bool IsBoundsCheckSafe { get; init; }
  /// Optional precise element storage type for narrow ranged primitives — distinguishes
  /// signed (I8/I16) from unsigned (U8/U16) bytes/words so the codegen picks the right
  /// movsx vs movzx variant. When null, lowering falls back to ResultKind-based dispatch.
  public IrType? ElementStorageType { get; init; }
  // Result is always a scalar or pointer — struct/enum elements produce a pointer to inline data
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.TypeParameter
    ? new MaxonInteger(IrContext.Current.NextId()) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, Index];
}

// Set element at index in managed buffer: managed.set(index, value)
// Element size is read from the managed struct's element_size field at runtime.
// When IsStructElement is true, the value is a pointer to struct data and the
// full struct is copied inline into the buffer (not just the pointer).
public sealed class MaxonManagedMemSetOp(MaxonValue managedStruct, MaxonValue index, MaxonValue value, MaxonValueKind elementKind = MaxonValueKind.Integer) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemSet;
  public override string Mnemonic => "maxon.managed_mem_set";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Value { get; } = value;
  public MaxonValueKind ElementKind { get; } = elementKind;
  public bool IsStructElement { get; init; }
  public string? TypeParamName { get; init; }
  /// Optional precise element storage type for narrow ranged primitives — see MaxonManagedMemGetOp.ElementStorageType.
  public IrType? ElementStorageType { get; init; }
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, Index, Value];
}

// Create a new heap-allocated managed memory: __ManagedMemory.create(count, elemSize)
public sealed class MaxonManagedMemCreateOp(MaxonValue count, int elementSize) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemCreate;
  public override string Mnemonic => "maxon.managed_mem_create";
  public MaxonValue Count { get; } = count;
  public int ElementSize { get; } = elementSize;
  /// When true, elements are bit-packed bools (elementSize stored as 0 sentinel).
  public bool IsBitPacked { get; set; }
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Count];
}

// Grow managed memory to new capacity: managed.grow(newCap)
// Element size is read from the managed struct's element_size field at runtime.
public sealed class MaxonManagedMemGrowOp(MaxonValue managedStruct, MaxonValue newCapacity) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemGrow;
  public override string Mnemonic => "maxon.managed_mem_grow";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue NewCapacity { get; } = newCapacity;
  /// When true, elements are bit-packed bools (byte size = (cap+7)/8 instead of cap*elemSize).
  public bool IsBitPacked { get; set; }
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, NewCapacity];
}

// Set length of managed memory with capacity validation.
// A SHRINK vacates the dropped slots, so the lowering needs the element class to
// know whether those slots hold refcounted pointers (release them) or raw values
// (just erase them). A GROW needs neither: it only publishes slots the caller has
// already staged.
public sealed class MaxonManagedMemSetLengthOp(MaxonValue managedStruct, MaxonValue newLength) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemSetLength;
  public override string Mnemonic => "maxon.managed_mem_set_length";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue NewLength { get; } = newLength;
  /// True when elements are refcounted heap pointers (struct / string / union / array).
  public bool IsStructElement { get; init; }
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, NewLength];
}

// Clear all elements from managed memory, decrementing struct element refcounts
public sealed class MaxonManagedMemClearOp(MaxonValue managedStruct) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemClear;
  public override string Mnemonic => "maxon.managed_mem_clear";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public bool IsStructElement { get; init; }
  public string? StructElementTypeName { get; init; }
  public string? TypeParamName { get; init; }
  /// When true, elements are bit-packed bools.
  public bool IsBitPacked { get; set; }
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct];
}

// Shift elements right/left in managed buffer
// Element size is read from the managed struct's element_size field at runtime.
public sealed class MaxonManagedMemShiftOp(MaxonValue managedStruct, MaxonValue index, MaxonValue count, bool shiftRight) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemShift;
  public override string Mnemonic => ShiftRight ? "maxon.managed_mem_shift_right" : "maxon.managed_mem_shift_left";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Count { get; } = count;
  public bool ShiftRight { get; } = shiftRight;
  /// When true, elements are bit-packed bools (uses bit-by-bit loop instead of memcpy).
  public bool IsBitPacked { get; set; }
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, Index, Count];
}

// Remove element at index: load element (ownership transfer), shift left, shrink length.
// For struct elements the loaded pointer is NOT incref'd — the buffer's reference is
// transferred to the caller. The slot is zeroed after loading to prevent double-free
// if mm_decref_managed_elements walks the buffer before the shift completes.
public sealed class MaxonManagedMemRemoveOp(MaxonValue managedStruct, MaxonValue index, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemRemove;
  public override string Mnemonic => "maxon.managed_mem_remove";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public bool IsStructElement { get; init; }
  public string? StructElementTypeName { get; init; }
  public string? TypeParamName { get; init; }
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.TypeParameter
    ? new MaxonInteger(IrContext.Current.NextId()) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, Index];
}

// Get byte at index in managed buffer: managed.byteAt(index)
public sealed class MaxonManagedMemByteGetOp(MaxonValue managedStruct, MaxonValue index) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemByteGet;
  public override string Mnemonic => "maxon.managed_mem_byte_get";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonByte Result { get; } = new MaxonByte(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, Index];
}

// Set byte at index in managed buffer: managed.setByte(index, value)
public sealed class MaxonManagedMemByteSetOp(MaxonValue managedStruct, MaxonValue index, MaxonValue value) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemByteSet;
  public override string Mnemonic => "maxon.managed_mem_byte_set";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Value { get; } = value;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, Index, Value];
}

// Panics if end > capacity (i.e. end+1 reads/writes past the buffer). Used by socket/file
// builtins that pass a pointer range into a raw buffer and must not read OOB.
public sealed class MaxonByteRangePanicOp(MaxonValue end, MaxonValue capacity, string panicLabel) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ByteRangePanic;
  public override string Mnemonic => $"maxon.byte_range_panic @{PanicLabel}";
  public MaxonValue End { get; } = end;
  public MaxonValue Capacity { get; } = capacity;
  public string PanicLabel { get; } = panicLabel;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [End, Capacity];
}

// Loads a single byte (zero-extended to i64) from a named .ucd section blob at the given byte offset
public sealed class MaxonUcdByteLoadOp(string ucddataLabel, MaxonValue byteOffset) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.UcdByteLoad;
  public override string Mnemonic => $"maxon.ucd_byte_load {UcddataLabel}";
  public string UcddataLabel { get; } = ucddataLabel;
  public MaxonValue ByteOffset { get; } = byteOffset;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ByteOffset];
}

// Loads a 64-bit integer from a named .ucd section blob at position index*8
public sealed class MaxonUcdI64LoadOp(string ucddataLabel, MaxonValue index) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.UcdI64Load;
  public override string Mnemonic => $"maxon.ucd_i64_load {UcddataLabel}";
  public string UcddataLabel { get; } = ucddataLabel;
  public MaxonValue Index { get; } = index;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Index];
}

// String literal: stores UTF-8 bytes in rdata and creates a String struct
public sealed class MaxonStringLiteralOp(string value, string stringTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.StringLiteral;
  public override string Mnemonic => $"maxon.string_literal \"{Value}\"";
  public string Value { get; } = value;
  public string StringTypeName { get; } = stringTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), stringTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Byte string literal: stores UTF-8 bytes in rdata and creates a ByteArray (Array with Byte)
public sealed class MaxonByteStringLiteralOp(string value, string arrayTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ByteStringLiteral;
  public override string Mnemonic => $"maxon.byte_string_literal \"{Value}\"";
  public string Value { get; } = value;
  public string ArrayTypeName { get; } = arrayTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), arrayTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Character literal: stores UTF-8 bytes in rdata and creates a Character struct
public sealed class MaxonCharLiteralOp(string value, string charTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CharLiteral;
  public override string Mnemonic => $"maxon.char_literal '{Value}'";
  public string Value { get; } = value;
  public string CharTypeName { get; } = charTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), charTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}


// Append another __ManagedMemory buffer's data to self in-place (grow if needed)
public sealed class MaxonManagedMemAppendOp(MaxonValue managedStruct, MaxonValue other) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemAppend;
  public override string Mnemonic => "maxon.managed_mem_append";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Other { get; } = other;
  public bool IsStructElement { get; init; }
  public string? TypeParamName { get; init; }
  public bool IsBitPacked { get; set; }
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct, Other];
}

// String interpolation: concatenates literal parts and expression values into a new String
public sealed class MaxonStringInterpOp(List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, IrType? OptimalType)> parts, string stringTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.StringInterp;
  public override string Mnemonic => "maxon.string_interp";
  public List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, IrType? OptimalType)> Parts { get; } = parts;
  public string StringTypeName { get; } = stringTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), stringTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  // Only the interpolated EXPRESSION parts are reads; the literal chunks carry no value.
  public override IReadOnlyList<MaxonValue> Operands =>
    [.. Parts.Where(p => p.ExprValue != null).Select(p => p.ExprValue!)];
  // Rendered inside the op's attributes rather than as an operand line — see StructLiteral.
  public override IReadOnlyList<string> PrintableOperands => [];
}

// Create a slice of a __ManagedMemory buffer (start/end element positions)
public sealed class MaxonManagedMemSliceOp(MaxonValue managed, MaxonValue start, MaxonValue end) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemSlice;
  public override string Mnemonic => "maxon.managed_mem_slice";
  public MaxonValue Managed { get; } = managed;
  public MaxonValue Start { get; } = start;
  public MaxonValue End { get; } = end;
  public bool IsStructElement { get; init; }
  public string? TypeParamName { get; init; }
  /// When true, elements are bit-packed bools.
  public bool IsBitPacked { get; set; }
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<MaxonValue> Operands => [Managed, Start, End];
  public override IReadOnlyList<MaxonValue> Results => [Result];
}

// ============================================================================
// __ManagedMemoryCursor operations
// ============================================================================

// Create a cursor from a __ManagedMemory buffer.
// Throws CursorError.exhausted if the source is empty.
public sealed class MaxonManagedMemCreateCursorOp(MaxonValue managedStruct) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemCreateCursor;
  public override string Mnemonic => "maxon.managed_mem_create_cursor";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public string? TypeParamName { get; init; }
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedMemoryCursor");
  public override IReadOnlyList<MaxonValue> Operands => [ManagedStruct];
  public override IReadOnlyList<MaxonValue> Results => [Result];
}

// Load element at current cursor position (no bounds check).
public sealed class MaxonCursorCurrentOp(MaxonValue cursorStruct, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CursorCurrent;
  public override string Mnemonic => "maxon.cursor_current";
  public MaxonValue CursorStruct { get; } = cursorStruct;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public bool IsStructElement { get; init; }
  public string? StructElementTypeName { get; init; }
  public string? TypeParamName { get; init; }
  /// Optional precise element storage type for narrow ranged primitives — see MaxonManagedMemGetOp.ElementStorageType.
  public IrType? ElementStorageType { get; init; }
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.TypeParameter
    ? new MaxonInteger(IrContext.Current.NextId()) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [CursorStruct];
}

// Read the current position index from the cursor.
public sealed class MaxonCursorIndexOp(MaxonValue cursorStruct) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CursorIndex;
  public override string Mnemonic => "maxon.cursor_index";
  public MaxonValue CursorStruct { get; } = cursorStruct;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [CursorStruct];
}

// Peek at element ahead positions from current. Throws CursorError.exhausted if out of bounds.
public sealed class MaxonCursorPeekOp(MaxonValue cursorStruct, MaxonValue ahead, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CursorPeek;
  public override string Mnemonic => "maxon.cursor_peek";
  public MaxonValue CursorStruct { get; } = cursorStruct;
  public MaxonValue Ahead { get; } = ahead;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public bool IsStructElement { get; init; }
  public string? StructElementTypeName { get; init; }
  public string? TypeParamName { get; init; }
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.TypeParameter
    ? new MaxonInteger(IrContext.Current.NextId()) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [CursorStruct, Ahead];
}

// Convert a C string pointer to __ManagedMemory
public sealed class MaxonCStringToManagedOp(MaxonValue cstrPtr) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CStringToManaged;
  public override string Mnemonic => "maxon.cstring_to_managed";
  public MaxonValue CstrPtr { get; } = cstrPtr;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<MaxonValue> Operands => [CstrPtr];
  public override IReadOnlyList<MaxonValue> Results => [Result];
}

// Convert __ManagedMemory to a C string pointer
public sealed class MaxonManagedToCStringOp(MaxonValue managed) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedToCString;
  public override string Mnemonic => "maxon.managed_to_cstring";
  public MaxonValue Managed { get; } = managed;
  public MaxonCString Result { get; } = new MaxonCString(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Operands => [Managed];
  public override IReadOnlyList<MaxonValue> Results => [Result];
}

// Write managed memory buffer to stdout, returns number of bytes written
public sealed class MaxonManagedWriteStdoutOp(MaxonValue managed) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedWriteStdout;
  public override string Mnemonic => "maxon.managed_write_stdout";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Managed];
}

// Read up to MaxBytes bytes from stdin into a freshly-allocated __ManagedMemory,
// whose length reflects the count actually read (0 on EOF). Returns the MM.
public sealed class MaxonManagedReadStdinOp(MaxonValue maxBytes) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedReadStdin;
  public override string Mnemonic => "maxon.managed_read_stdin";
  public MaxonValue MaxBytes { get; } = maxBytes;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [MaxBytes];
}

// Write managed memory buffer to stderr, returns number of bytes written
public sealed class MaxonManagedWriteStderrOp(MaxonValue managed) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedWriteStderr;
  public override string Mnemonic => "maxon.managed_write_stderr";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Managed];
}

// Write error message to stderr and terminate with exit code 1
//
// Stdlib and user panics use separate label namespaces so that the stable,
// cached stdlib labels never collide with user-code labels whose counter
// resets each compile. A user-code panic and a stdlib-code panic could
// otherwise both get `__panic_msg_10`, and only one wins in symdata —
// the other prints the wrong message at runtime.
public sealed class MaxonPanicOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Panic;
  [ThreadStatic] private static Dictionary<string, string>? _userPanicLabelCache;
  // Written and read ONLY while a stdlib module is being parsed, which is one thread's work from
  // start to finish — so the labels in any one stdlib module were all minted against one state of
  // this dictionary, and each message therefore holds a label no other message holds. Nothing
  // downstream of the parse consults it (see CloneKeepingLabel), which is what makes that true on a
  // thread that clones a stdlib module it did not parse.
  [ThreadStatic] private static Dictionary<string, string>? _stdlibPanicLabelCache;
  // Resets the user-code cache. Stdlib labels live in the cached stdlib
  // module and are not re-assigned across user compiles, so the stdlib cache
  // is left alone.
  public static void ResetPanicLabels() => _userPanicLabelCache = null;
  public override string Mnemonic => $"maxon.panic \"{Message}\"";
  public string Message { get; }
  public bool IsStdlib { get; }
  public string SymdataLabel { get; }

  /// A panic the PARSER read out of source text. Its message is being written down for the first
  /// time, so this is where the label for it is decided.
  public MaxonPanicOp(string message, bool isStdlib)
    : this(message, isStdlib, GetOrCreateLabel(message, isStdlib)) { }

  private MaxonPanicOp(string message, bool isStdlib, string symdataLabel) {
    Message = message;
    IsStdlib = isStdlib;
    SymdataLabel = symdataLabel;
  }

  /// A copy of this panic, carrying the label this one was given — THE way a clone of this op is
  /// assembled, for FunctionCloner and MonomorphizationPass alike.
  ///
  /// It CARRIES the label rather than re-deriving it, because the two derivations answer to
  /// different state. <see cref="GetOrCreateLabel"/> reads a [ThreadStatic] cache, while the module
  /// being specialized is process-global: the stdlib is parsed once, on whichever thread asked
  /// first, and every other thread then clones a module whose labels it never minted. Re-deriving
  /// on such a thread numbers the copy from an unrelated count, so a specialized panic could take a
  /// label a CONCRETE panic in the same module already held — and symdata keeps only the first
  /// message written under a label, so the other one prints text from a function it never called.
  /// (Measured: `Array.resize`'s panic reporting `utf16.maxon:59` — see specs/panic-label-identity.)
  public MaxonPanicOp CloneKeepingLabel() => new(Message, IsStdlib, SymdataLabel);

  private static string GetOrCreateLabel(string message, bool isStdlib) {
    if (isStdlib) {
      _stdlibPanicLabelCache ??= [];
      if (_stdlibPanicLabelCache.TryGetValue(message, out var label)) return label;
      label = $"__stdlib_panic_msg_{_stdlibPanicLabelCache.Count}";
      _stdlibPanicLabelCache[message] = label;
      return label;
    } else {
      _userPanicLabelCache ??= [];
      if (_userPanicLabelCache.TryGetValue(message, out var label)) return label;
      label = $"__panic_msg_{_userPanicLabelCache.Count}";
      _userPanicLabelCache[message] = label;
      return label;
    }
  }
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Write dynamically-constructed error message (from string interpolation) to stderr and terminate
public sealed class MaxonPanicDynamicOp(MaxonStruct messageStruct) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.PanicDynamic;
  public override string Mnemonic => "maxon.panic_dynamic";
  public MaxonStruct MessageStruct { get; } = messageStruct;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [MessageStruct];
}

/// Generic runtime function call op for intrinsics that delegate to a runtime function.
public sealed class MaxonCallRuntimeOp(string functionName, List<MaxonValue> args, bool hasResult) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CallRuntime;
  public override string Mnemonic => $"maxon.call_runtime.{FunctionName}";
  public string FunctionName { get; } = functionName;
  public List<MaxonValue> Args { get; } = args;
  public MaxonInteger? Result { get; } = hasResult ? new MaxonInteger(IrContext.Current.NextId()) : null;
  public override IReadOnlyList<MaxonValue> Results => Result != null ? [Result] : [];
  public override IReadOnlyList<MaxonValue> Operands => Args;
}

// Create a Character from bytes within a managed buffer
public sealed class MaxonMakeCharFromBytesOp(MaxonValue managed, MaxonValue pos, MaxonValue len) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.MakeCharFromBytes;
  public override string Mnemonic => "maxon.make_char_from_bytes";
  public MaxonValue Managed { get; } = managed;
  public MaxonValue Pos { get; } = pos;
  public MaxonValue Len { get; } = len;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "Character");
  public override IReadOnlyList<MaxonValue> Operands => [Managed, Pos, Len];
  public override IReadOnlyList<MaxonValue> Results => [Result];
}

// ============================================================================
// ManagedList (doubly-linked list) operations
// ============================================================================

/// Creates a new empty managed list data structure.
///
/// <paramref name="listTypeName"/> is the ELEMENT-BEARING spelling of the list being created
/// (`EManagedList`, and `__ManagedList_Point` once monomorphization has bound Element) — never the
/// bare `__ManagedList`. It is not decoration: the allocation's destructor is chosen from it, and
/// only an element-bearing name can say whether the destructor must decref each node's value.
/// A bare name silently selects the primitive `maxon_managed_list_clear` and leaks every element,
/// so the name is a constructor argument rather than a field a later pass may forget to carry.
public sealed class MaxonManagedListCreateOp(string listTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListCreate;
  public override string Mnemonic => "maxon.managed_list_create";
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), listTypeName);
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

// Inserts a value at the head or tail of the managed list, creating a new node
public sealed class MaxonManagedListInsertValueOp(MaxonValue managedList, MaxonValue value, bool atHead, string valueKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListInsertValue;
  public override string Mnemonic => AtHead ? "maxon.managed_list_insert_head" : "maxon.managed_list_insert_tail";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonValue Value { get; } = value;
  public bool AtHead { get; } = atHead;
  public string ValueKind { get; set; } = valueKind;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedListNode");
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList, Value];
}

// Inserts a value relative to a target node (before or after)
public sealed class MaxonManagedListInsertRelativeValueOp(MaxonValue managedList, MaxonValue target, MaxonValue value, bool after, string valueKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListInsertRelativeValue;
  public override string Mnemonic => After ? "maxon.managed_list_insert_after" : "maxon.managed_list_insert_before";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonValue Target { get; } = target;
  public MaxonValue Value { get; } = value;
  public bool After { get; } = after;
  public string ValueKind { get; set; } = valueKind;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedListNode");
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList, Target, Value];
}

// Detaches a node from the managed list without freeing it
public sealed class MaxonManagedListDetachOp(MaxonValue managedList, MaxonValue node) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListDetach;
  public override string Mnemonic => "maxon.managed_list_detach";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonValue Node { get; } = node;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList, Node];
}

// Removes a node from the managed list: extracts value, unlinks node, frees node memory
public sealed class MaxonManagedListRemoveOp(MaxonValue managedList, MaxonValue node, string valueKind, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListRemove;
  public override string Mnemonic => "maxon.managed_list_remove";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonValue Node { get; } = node;
  public string ValueKind { get; set; } = valueKind;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(IrContext.Current.NextId(), valueKind) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList, Node];
}

// Returns the number of nodes in the managed list
public sealed class MaxonManagedListCountOp(MaxonValue managedList) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListCount;
  public override string Mnemonic => "maxon.managed_list_count";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList];
}

// Loads the value stored in a managed list node
public sealed class MaxonManagedListNodeValueOp(MaxonValue node, string valueKind, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListNodeValue;
  public override string Mnemonic => "maxon.managed_list_node_value";
  public MaxonValue Node { get; } = node;
  public string ValueKind { get; set; } = valueKind;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(IrContext.Current.NextId(), valueKind) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [Node];
}

// Replaces the value stored in a managed list node
public sealed class MaxonManagedListNodeSetValueOp(MaxonValue node, MaxonValue value, string valueKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListNodeSetValue;
  public override string Mnemonic => "maxon.managed_list_node_set_value";
  public MaxonValue Node { get; } = node;
  public MaxonValue Value { get; } = value;
  public string ValueKind { get; set; } = valueKind;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [Node, Value];
}

// Removes all nodes from the managed list, freeing each node.
// ValueKind indicates the element type — used to decide whether node values need decref.
public sealed class MaxonManagedListClearOp(MaxonValue managedList, string valueKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListClear;
  public override string Mnemonic => "maxon.managed_list_clear";
  public MaxonValue ManagedList { get; } = managedList;
  public string ValueKind { get; set; } = valueKind;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList];
}

// Resets the managed list's iteration cursor to null (0)
public sealed class MaxonManagedListCursorResetOp(MaxonValue managedList) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListCursorReset;
  public override string Mnemonic => "maxon.managed_list_cursor_reset";
  public MaxonValue ManagedList { get; } = managedList;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList];
}

// Reads the value at the managed list's current cursor position
public sealed class MaxonManagedListCursorValueOp(MaxonValue managedList, string valueKind, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListCursorValue;
  public override string Mnemonic => "maxon.managed_list_cursor_value";
  public MaxonValue ManagedList { get; } = managedList;
  public string ValueKind { get; } = valueKind;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(IrContext.Current.NextId(), valueKind) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList];
}

// Returns the head node pointer as a raw int (no refcounting)
public sealed class MaxonManagedListHeadPtrOp(MaxonValue managedList) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListHeadPtr;
  public override string Mnemonic => "maxon.managed_list_head_ptr";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [ManagedList];
}

// Returns cursor->next as a raw int (no refcounting). Caller must check for null.
public sealed class MaxonManagedListNodePtrNextOp(MaxonValue cursorPtr) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListNodePtrNext;
  public override string Mnemonic => "maxon.managed_list_node_ptr_next";
  public MaxonValue CursorPtr { get; } = cursorPtr;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [CursorPtr];
}

// Reads the value from a node given its raw pointer. No refcounting on the node.
public sealed class MaxonManagedListNodePtrValueOp(MaxonValue cursorPtr, string valueKind, MaxonValueKind resultKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListNodePtrValue;
  public override string Mnemonic => "maxon.managed_list_node_ptr_value";
  public MaxonValue CursorPtr { get; } = cursorPtr;
  public string ValueKind { get; set; } = valueKind;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(IrContext.Current.NextId(), valueKind) : resultKind.CreateValue();
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => [CursorPtr];
}

// ========== Async/Await ops ==========

/// Spawns a green thread to execute a function call.
/// The result is a MaxonPromise that can be awaited.
public sealed class MaxonAsyncCallOp(string callee, List<MaxonValue> args, MaxonValueKind? innerResultKind, string? innerStructTypeName, IrType? errorType = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.AsyncCall;
  public override string Mnemonic => $"maxon.async_call @{Callee}";
  public string Callee { get; } = callee;
  public List<MaxonValue> Args { get; } = args;
  /// Everything the spawned call's type says about the promise — the result kind, the
  /// result struct name, and the `throws` TYPE (from which throws-ness is derived) — lives
  /// on this one value. The op deliberately does not re-publish any of it as its own
  /// properties: the ones it used to expose were write-only, and a second copy of a fact is
  /// a second thing to forget (which is how `errorIsHeapPtr` came to be dropped on the
  /// cross-block re-tag in the first place). Read `asyncOp.Result.ErrorType`, not an
  /// op-level echo of it.
  public MaxonPromise Result { get; } = new MaxonPromise(IrContext.Current.NextId(), innerResultKind, innerStructTypeName, errorType);
  public List<bool>? ArgMutabilities { get; set; }
  public List<string?>? ArgVarNames { get; set; }
  /// Source location for error reporting (line of the 'async' keyword)
  public int? CallLine { get; set; }
  public int? CallColumn { get; set; }
  /// The source text of the async call expression (for error messages)
  public string? CallSourceText { get; set; }
  public override IReadOnlyList<MaxonValue> Results => [Result];
  public override IReadOnlyList<MaxonValue> Operands => Args;
}

/// What an AWAIT — of either form — must tell the linear-await check (E3100).
///
/// There are two await ops (`await` and `try await`), and both consume the promise's result in
/// exactly the same way, so both are subject to linearity. This interface is what MAKES them agree:
/// the check matches on it, not on the two classes, so a fact added here and forgotten in one op is
/// a COMPILE ERROR rather than a silently-defaulted field the check then reads as null. E3100 exists
/// because one fact about a promise was written down twice and the copies drifted; this is that
/// lesson applied to the op that carries it.
public interface IMaxonAwaitOp {
  /// Source location of the `await` keyword — where a linearity diagnostic points.
  int? AwaitLine { get; }
  int? AwaitColumn { get; }

  /// The GREEN THREAD this await consumes — the identity in which `await` is LINEAR (E3100).
  ///
  /// This is the KEY the check matches on, and it is the promise VALUE's thread, NOT the
  /// identifier text. It has to be: `let q = p` gives one green thread two names, so keying on
  /// the name saw `p` and `q` as unrelated and let `await p; await q` compile — a DOUBLE FREE,
  /// which is the entire thing E3100 exists to prevent. Nor is it the promise value's SSA `Id`:
  /// a cross-block read re-tags a fresh MaxonPromise around the same thread, so `Id` would miss
  /// a second await in another block. MaxonPromise.GreenThreadId is the id that survives both,
  /// because it is minted once at the `async` spawn and carried by every value derived from it.
  ///
  /// Null when the awaited expression is not a named binding (an inline `await async f()`, or an
  /// `await h.pr`) — see CheckLinearAwait for what that does and does not cover.
  int? PromiseGreenThreadId { get; }

  /// The BINDING this await read the promise from — what RE-ARMS it. A distinct fact from the
  /// green thread, and not derivable from it: assigning this name puts a DIFFERENT thread in it,
  /// so the linearity walk stops there. That is what makes `for p in promises 'each' … await p …
  /// end` legal — the loop re-arms `p` every iteration, so its single `await` is one await per
  /// promise, not N awaits of one.
  ///
  /// Null exactly when PromiseGreenThreadId is: both come from the same binding lookup.
  string? PromiseVarName { get; }
}

/// Waits for a green thread (promise) to complete and extracts its result.
public sealed class MaxonAwaitOp : MaxonOp, IMaxonAwaitOp {
  public override MaxonOpKind Kind => MaxonOpKind.Await;
  public override string Mnemonic => "maxon.await";
  public MaxonValue Promise { get; }
  public MaxonValue? Result { get; }
  public MaxonValueKind? ResultKind { get; }
  public string? ResultStructTypeName { get; }
  /// Source location of the `await` keyword. A MaxonAwaitOp that SURVIVES parsing is by
  /// construction a PLAIN await — `try await` deletes it and emits a MaxonTryAwaitOp in
  /// its place — so SemanticCheckPass can reject a plain await of a throwing thunk, and
  /// needs somewhere to point when it does.
  public int? AwaitLine { get; set; }
  public int? AwaitColumn { get; set; }
  /// <inheritdoc cref="IMaxonAwaitOp.PromiseGreenThreadId"/>
  public int? PromiseGreenThreadId { get; set; }
  /// <inheritdoc cref="IMaxonAwaitOp.PromiseVarName"/>
  public string? PromiseVarName { get; set; }
  public override IReadOnlyList<MaxonValue> Results => Result != null ? [Result] : [];
  public override IReadOnlyList<MaxonValue> Operands => [Promise];

  public MaxonAwaitOp(MaxonValue promise, MaxonValueKind? resultKind, string? resultStructTypeName) {
    Promise = promise;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    if (resultKind != null) {
      Result = resultKind == MaxonValueKind.Struct
        ? new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!)
        : resultKind.Value.CreateValue();
    }
  }
}

/// Waits for a throwing green thread (promise) to complete. Extracts both the result and error flag.
/// Mirrors MaxonTryCallOp but for async/await: the error flag comes from gt.threw.
public sealed class MaxonTryAwaitOp : MaxonOp, IMaxonAwaitOp {
  public override MaxonOpKind Kind => MaxonOpKind.TryAwait;
  public override string Mnemonic => "maxon.try_await";
  public MaxonValue Promise { get; }
  public MaxonValue? Result { get; }
  public MaxonInteger ErrorFlag { get; }
  public MaxonValueKind? ResultKind { get; }
  public string? ResultStructTypeName { get; }
  /// <inheritdoc cref="IMaxonAwaitOp.AwaitLine"/>
  public int? AwaitLine { get; set; }
  public int? AwaitColumn { get; set; }
  /// <inheritdoc cref="IMaxonAwaitOp.PromiseGreenThreadId"/>
  public int? PromiseGreenThreadId { get; set; }
  /// <inheritdoc cref="IMaxonAwaitOp.PromiseVarName"/>
  public string? PromiseVarName { get; set; }
  public override IReadOnlyList<MaxonValue> Results => Result != null ? [Result, ErrorFlag] : [ErrorFlag];
  public override IReadOnlyList<MaxonValue> Operands => [Promise];

  // The error-flag disposition (typed binding, decref-or-not) is decided entirely
  // at the try/otherwise emitters from the awaited MaxonPromise's ErrorType, which
  // is where the awaited thunk's `throws` clause actually lives. This op used to
  // carry `errorIsHeapPtr` / `errorIsHeapPtrRuntime` copies of that decision which
  // nothing ever read — neither the lowering nor the printer — so they are gone.
  public MaxonTryAwaitOp(MaxonValue promise, MaxonValueKind? resultKind, string? resultStructTypeName) {
    Promise = promise;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    ErrorFlag = new MaxonInteger(IrContext.Current.NextId());
    if (resultKind != null) {
      Result = resultKind == MaxonValueKind.Struct
        ? new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!)
        : resultKind.Value.CreateValue();
    }
  }
}

/// Cancels a green thread associated with a promise.
public sealed class MaxonCancelPromiseOp(MaxonValue promise) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CancelPromise;
  public override string Mnemonic => "maxon.cancel_promise";
  public MaxonValue Promise { get; } = promise;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [Promise];
}

// ============================================================================
// __DebugStream — the builtin that lets USER MAXON SOURCE put a structured event
// into the shared-memory ring (Workstream O). Every other DebugStream event is
// emitted by the runtime; these are the only ones a compiled program authors.
//
// TWO GATES, both load-bearing, and both enforced in the lowering:
//   * COMPILE-TIME: with `--debugstream` off, every op below lowers to NOTHING.
//     Not a branch that is never taken — no instructions at all.
//   * RUNTIME: with the ring detached (`__ds_base == 0`), the emitting ops bail
//     INLINE, before any CALL (StdCallRuntimeIfNonzeroOp).
// ============================================================================

/// True when the DebugStream ring is attached (`__ds_base != 0`). Lets a caller skip
/// building a Tier-2 message that nothing would read. Folds to a constant `false`
/// when DebugStream is off at compile time.
public sealed class MaxonDebugStreamEnabledOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.DebugStreamEnabled;
  public override string Mnemonic => "maxon.debugstream_enabled";
  public MaxonBool Result { get; } = new MaxonBool(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

/// A name INTERNED AT COMPILE TIME into the MXDS_STRS blob. Lowers to the u16 index —
/// so the event carries a number, the monitor prints the name, and the emitting program
/// never builds a string. This is what makes the structured tier zero-alloc.
public sealed class MaxonDebugStreamNameIdOp(string name) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.DebugStreamNameId;
  public override string Mnemonic => $"maxon.debugstream_name_id \"{Name}\"";
  public string Name { get; } = name;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<MaxonValue> Results => [Result];
  /// Reads nothing — a leaf op (a literal, a parameter, a var reference, or a jump).
  public override IReadOnlyList<MaxonValue> Operands => [];
}

/// LOG_PHASE_BEGIN / LOG_PHASE_END: one end of a nested, per-worker, per-unit span.
/// `IsBegin` picks the event code; the payload is identical either way.
public sealed class MaxonDebugStreamPhaseOp(bool isBegin, MaxonValue nameId, MaxonValue unitId) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.DebugStreamPhase;
  public override string Mnemonic => IsBegin ? "maxon.debugstream_phase_begin" : "maxon.debugstream_phase_end";
  public bool IsBegin { get; } = isBegin;
  public MaxonValue NameId { get; } = nameId;
  public MaxonValue UnitId { get; } = unitId;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [NameId, UnitId];
}

/// LOG_EVENT: the structured tier. An interned name plus two numeric args — no String,
/// no closure, no allocation, so a pass can emit one from inside the register allocator
/// without polluting the very `mm` stream the trace exists to read.
public sealed class MaxonDebugStreamEventOp(MaxonValue nameId, MaxonValue category, MaxonValue level,
    MaxonValue unitId, MaxonValue arg0, MaxonValue arg1) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.DebugStreamEvent;
  public override string Mnemonic => "maxon.debugstream_event";
  public MaxonValue NameId { get; } = nameId;
  public MaxonValue Category { get; } = category;
  public MaxonValue Level { get; } = level;
  public MaxonValue UnitId { get; } = unitId;
  public MaxonValue Arg0 { get; } = arg0;
  public MaxonValue Arg1 { get; } = arg1;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [NameId, Category, Level, UnitId, Arg0, Arg1];
}

/// LOG_TEXT: the rare human message, as a length-prefixed UTF-8 tail read out of a
/// __ManagedMemory. Allocating (the caller built the string), and it says so.
public sealed class MaxonDebugStreamTextOp(MaxonValue category, MaxonValue level, MaxonValue unitId,
    MaxonValue managed) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.DebugStreamText;
  public override string Mnemonic => "maxon.debugstream_text";
  public MaxonValue Category { get; } = category;
  public MaxonValue Level { get; } = level;
  public MaxonValue UnitId { get; } = unitId;
  public MaxonValue Managed { get; } = managed;
  public override IReadOnlyList<MaxonValue> Results => [];
  public override IReadOnlyList<MaxonValue> Operands => [Category, Level, UnitId, Managed];
}
