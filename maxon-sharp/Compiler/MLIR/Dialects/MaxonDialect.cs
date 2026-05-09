using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Compiler.Ir.Dialects;

public enum MaxonValueKind { Integer, Float, Float32, Bool, Byte, Short, Struct, Enum, Function, TypeParameter, ErrorUnion }

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
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValueKind ToValueKind(this IrType type) {
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
  Min,
  Max,
  CondBr,
  Br,
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
}

public abstract class MaxonOp : IPrintableOp {
  public abstract MaxonOpKind Kind { get; }
  public abstract string Mnemonic { get; }
  public virtual IReadOnlyList<string> PrintableResults => [];
  public virtual IReadOnlyList<string> PrintableOperands => [];
  public virtual IReadOnlyDictionary<string, IrAttribute> PrintableAttributes => new Dictionary<string, IrAttribute>();

}

public sealed class MaxonLiteralOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Literal;
  public override string Mnemonic => "maxon.literal";
  public MaxonValueKind ValueKind { get; }
  public long IntValue { get; }
  public double FloatValue { get; }
  public bool BoolValue { get; }
  public MaxonValue Result { get; }
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, IrAttribute> {
        ["index"] = new IntegerAttr(Index, IrType.I32),
        ["name"] = new StringAttr(Name),
      };
      if (ValueKind is not (MaxonValueKind.TypeParameter or MaxonValueKind.Struct or MaxonValueKind.Enum))
        attrs["type"] = new TypeAttr(ValueKind.ToIrType());
      return attrs;
    }
  }
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Function parameter op: represents a function pointer being received as a function parameter.
public sealed class MaxonFunctionParamOp(int index, string name, IrFunctionType functionType) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FunctionParam;
  public override string Mnemonic => $"maxon.function_param";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public IrFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Function reference op: gets a pointer to a named function
public sealed class MaxonFunctionRefOp(string functionName, IrFunctionType functionType) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FunctionRef;
  public override string Mnemonic => $"maxon.function_ref @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public IrFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [.. CapturedValues.Select(v => v.ToString())];
}

// Inside a capturing closure: loads a captured value from the environment pointer
public sealed class MaxonClosureEnvLoadOp(int index, string name, MaxonValueKind kind, string? structTypeName = null) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ClosureEnvLoad;
  public override string Mnemonic => $"maxon.closure_env_load {Name}[{Index}]";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public MaxonValueKind ValueKind { get; } = kind;
  public string? StructTypeName { get; } = structTypeName;
  public MaxonValue Result { get; } = kind switch {
    MaxonValueKind.Struct => new MaxonStruct(IrContext.Current.NextId(), structTypeName!),
    MaxonValueKind.Enum when structTypeName != null => new MaxonEnum(IrContext.Current.NextId(), structTypeName),
    _ => kind.CreateValue()
  };
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Function var ref: loads a function pointer from a variable in a different block
public sealed class MaxonFunctionVarRefOp(string varName, IrFunctionType functionType) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FunctionVarRef;
  public override string Mnemonic => $"maxon.function_var_ref {VarName}";
  public string VarName { get; } = varName;
  public IrFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Indirect call op: calls a function through a function pointer
public sealed class MaxonIndirectCallOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.IndirectCall;
  public override string Mnemonic => "maxon.indirect_call";
  public MaxonValue Callee { get; }
  public IrFunctionType CalleeType { get; }
  public List<MaxonValue> Args { get; }
  public MaxonValue? Result { get; }
  public MaxonValueKind? ResultKind { get; }
  public string? ResultStructTypeName { get; }
  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString()).Prepend(Callee.ToString())];

  public MaxonIndirectCallOp(MaxonValue callee, IrFunctionType calleeType, List<MaxonValue> args, MaxonValueKind? resultKind = null, string? resultStructTypeName = null) {
    Callee = callee;
    CalleeType = calleeType;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    if (resultKind != null) {
      Result = resultKind == MaxonValueKind.Struct
        ? new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!)
        : resultKind.Value.CreateValue();
    }
  }
}

public sealed class MaxonVarRefOp(string varName, MaxonValueKind kind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.VarRef;
  public override string Mnemonic => "maxon.var_ref";
  public string VarName { get; } = varName;
  public MaxonValueKind ValueKind { get; } = kind;
  public MaxonValue Result { get; } = kind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> {
      ["var"] = new StringAttr(VarName),
      ["type"] = new TypeAttr(ValueKind.ToIrType())
    };
}

// Struct var ref: loads a struct from a variable in a different block
public sealed class MaxonStructVarRefOp(string varName, string structTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.StructVarRef;
  public override string Mnemonic => $"maxon.struct_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string StructTypeName { get; set; } = structTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), structTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
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

  protected static MaxonValue? CreateResult(MaxonValueKind? resultKind, string? resultStructTypeName) {
    if (resultKind == MaxonValueKind.Struct)
      return new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!);
    if (resultKind == MaxonValueKind.Enum)
      return new MaxonEnum(IrContext.Current.NextId(), resultStructTypeName!);
    return resultKind?.CreateValue();
  }

  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
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

  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString(), ErrorFlag.ToString()] : [ErrorFlag.ToString()];
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

  public override IReadOnlyList<string> PrintableResults => [ErrorFlag.ToString()];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
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

  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
}

public sealed class MaxonTruncOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Trunc;
  public override string Mnemonic => "maxon.trunc";
  public MaxonValue Input { get; } = input;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public sealed class MaxonIntToFloatOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.IntToFloat;
  public override string Mnemonic => "maxon.int_to_float";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["target"] = new TypeAttr(TargetKind.ToIrType()) };
}

public sealed class MaxonSizeofOp(string typeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Sizeof;
  public override string Mnemonic => "maxon.sizeof";
  public string TypeName { get; set; } = typeName;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["type"] = new StringAttr(TypeName) };
}

public sealed class MaxonAbsOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Abs;
  public override string Mnemonic => "maxon.abs";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public sealed class MaxonSqrtOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Sqrt;
  public override string Mnemonic => "maxon.sqrt";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public sealed class MaxonFloorOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Floor;
  public override string Mnemonic => "maxon.floor";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public sealed class MaxonCeilOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Ceil;
  public override string Mnemonic => "maxon.ceil";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public sealed class MaxonRoundOp(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Round;
  public override string Mnemonic => "maxon.round";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public sealed class MaxonBitcastF64ToI64Op(MaxonValue input) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.BitcastF64ToI64;
  public override string Mnemonic => "maxon.bitcast_f64_to_i64";
  public MaxonValue Input { get; } = input;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public sealed class MaxonMinOp(MaxonValue lhs, MaxonValue rhs) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Min;
  public override string Mnemonic => "maxon.min";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
}

public sealed class MaxonMaxOp(MaxonValue lhs, MaxonValue rhs) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Max;
  public override string Mnemonic => "maxon.max";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonFloat Result { get; } = new MaxonFloat(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
}

public sealed class MaxonCondBrOp(MaxonValue condition, string thenBlock, string elseBlock) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CondBr;
  public override string Mnemonic => $"maxon.cond_br {Condition} [then: {ThenBlock}, else: {ElseBlock}]";
  public MaxonValue Condition { get; } = condition;
  public string ThenBlock { get; } = thenBlock;
  public string ElseBlock { get; } = elseBlock;
}

public sealed class MaxonBrOp(string target) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Br;
  public override string Mnemonic => $"maxon.br {Target}";
  public string Target { get; } = target;
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
}

public sealed class MaxonReturnOp(MaxonValue? value = null, bool isErrorPropagation = false) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Return;
  public override string Mnemonic => "maxon.return";
  public MaxonValue? Value { get; } = value;
  public bool IsErrorPropagation { get; } = isErrorPropagation;
  public override IReadOnlyList<string> PrintableOperands =>
    Value != null ? [Value.ToString()] : [];
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
  public override IReadOnlyList<string> PrintableOperands => [ErrorValue.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public MaxonValue Result { get; } = resultKind switch {
    MaxonValueKind.Struct => new MaxonStruct(IrContext.Current.NextId(), resultStructTypeName!),
    MaxonValueKind.Enum => new MaxonEnum(IrContext.Current.NextId(), resultStructTypeName!),
    _ => resultKind.CreateValue()
  };
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [StructValue.ToString()];
}

// Assigns to a field: p.x = 30
public sealed class MaxonFieldAssignOp(MaxonValue structValue, string typeName, string fieldName, MaxonValue newValue) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.FieldAssign;
  public override string Mnemonic => $"maxon.field_assign .{FieldName}";
  public MaxonValue StructValue { get; } = structValue;
  public string TypeName { get; set; } = typeName;
  public string FieldName { get; } = fieldName;
  public MaxonValue NewValue { get; } = newValue;
  public override IReadOnlyList<string> PrintableOperands => [StructValue.ToString(), NewValue.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> {
      ["global"] = new StringAttr(GlobalName),
      ["type"] = new TypeAttr(ValueKind is MaxonValueKind.Enum or MaxonValueKind.Struct ? IrType.I64 : ValueKind.ToIrType())
    };
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];

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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Writes a value back to a specific payload slot in an associated-value enum's heap block
public sealed class MaxonEnumPayloadAssignOp(string enumVarName, string enumTypeName, int payloadIndex, MaxonValue newValue) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumPayloadAssign;
  public override string Mnemonic => $"maxon.enum_payload_assign @{EnumTypeName}[{PayloadIndex}]";
  public string EnumVarName { get; } = enumVarName;
  public string EnumTypeName { get; } = enumTypeName;
  public int PayloadIndex { get; } = payloadIndex;
  public MaxonValue NewValue { get; } = newValue;
  public override IReadOnlyList<string> PrintableOperands => [NewValue.ToString()];
}

// Enum var ref: loads an enum from a variable in a different block
public sealed class MaxonEnumVarRefOp(string varName, string enumTypeName, MaxonValueKind backingKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumVarRef;
  public override string Mnemonic => $"maxon.enum_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public MaxonEnum Result { get; } = new MaxonEnum(IrContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ErrorFlag.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Accesses .rawValue on a string or char-backed enum, returning String or Character
public sealed class MaxonEnumStringRawValueOp(MaxonValue enumValue, string enumTypeName, bool isChar) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumStringRawValue;
  public override string Mnemonic => $"maxon.enum_string_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public bool IsChar { get; } = isChar;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), isChar ? "Character" : "String");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Accesses .rawValue on a struct-backed enum, returning the backing struct type
public sealed class MaxonEnumStructRawValueOp(MaxonValue enumValue, string enumTypeName, string structTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumStructRawValue;
  public override string Mnemonic => $"maxon.enum_struct_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public string StructTypeName { get; } = structTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), structTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Accesses .name on an enum value, returning the case name as a String
public sealed class MaxonEnumNameOp(MaxonValue enumValue, string enumTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumName;
  public override string Mnemonic => $"maxon.enum_name @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "String");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Accesses .ordinal on an enum value, returning the zero-based declaration position as i64
public sealed class MaxonEnumOrdinalOp(MaxonValue enumValue, string enumTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.EnumOrdinal;
  public override string Mnemonic => $"maxon.enum_ordinal @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

public sealed class MaxonGlobalStoreOp(string globalName, MaxonValue value, MaxonValueKind kind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.GlobalStore;
  public override string Mnemonic => $"maxon.global_store @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public MaxonValue Value { get; } = value;
  public MaxonValueKind ValueKind { get; } = kind;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> {
      ["global"] = new StringAttr(GlobalName),
      ["type"] = new TypeAttr(ValueKind is MaxonValueKind.Enum or MaxonValueKind.Struct ? IrType.I64 : ValueKind.ToIrType())
    };
}

// ============================================================================
// Managed memory operations (for __ManagedMemory builtin intrinsics)
// ============================================================================

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
  /// Set by ForLoopIteratorElisionPass when rewriting a for-loop whose header already enforces i < length.
  public bool IsBoundsCheckSafe { get; init; }
  /// Optional precise element storage type for narrow ranged primitives — distinguishes
  /// signed (I8/I16) from unsigned (U8/U16) bytes/words so the codegen picks the right
  /// movsx vs movzx variant. When null, lowering falls back to ResultKind-based dispatch.
  public IrType? ElementStorageType { get; init; }
  // Result is always a scalar or pointer — struct/enum elements produce a pointer to inline data
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.TypeParameter
    ? new MaxonInteger(IrContext.Current.NextId()) : resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Value.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Count.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), NewCapacity.ToString()];
}

// Set length of managed memory with capacity validation
public sealed class MaxonManagedMemSetLengthOp(MaxonValue managedStruct, MaxonValue newLength) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemSetLength;
  public override string Mnemonic => "maxon.managed_mem_set_length";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue NewLength { get; } = newLength;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), NewLength.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Count.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
}

// Get byte at index in managed buffer: managed.byteAt(index)
public sealed class MaxonManagedMemByteGetOp(MaxonValue managedStruct, MaxonValue index) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemByteGet;
  public override string Mnemonic => "maxon.managed_mem_byte_get";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonByte Result { get; } = new MaxonByte(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
}

// Set byte at index in managed buffer: managed.setByte(index, value)
public sealed class MaxonManagedMemByteSetOp(MaxonValue managedStruct, MaxonValue index, MaxonValue value) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedMemByteSet;
  public override string Mnemonic => "maxon.managed_mem_byte_set";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Value { get; } = value;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Value.ToString()];
}

// Panics if end > capacity (i.e. end+1 reads/writes past the buffer). Used by socket/file
// builtins that pass a pointer range into a raw buffer and must not read OOB.
public sealed class MaxonByteRangePanicOp(MaxonValue end, MaxonValue capacity, string panicLabel) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ByteRangePanic;
  public override string Mnemonic => $"maxon.byte_range_panic @{PanicLabel}";
  public MaxonValue End { get; } = end;
  public MaxonValue Capacity { get; } = capacity;
  public string PanicLabel { get; } = panicLabel;
  public override IReadOnlyList<string> PrintableOperands => [End.ToString(), Capacity.ToString()];
}

// Loads a single byte (zero-extended to i64) from a named .ucd section blob at the given byte offset
public sealed class MaxonUcdByteLoadOp(string ucddataLabel, MaxonValue byteOffset) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.UcdByteLoad;
  public override string Mnemonic => $"maxon.ucd_byte_load {UcddataLabel}";
  public string UcddataLabel { get; } = ucddataLabel;
  public MaxonValue ByteOffset { get; } = byteOffset;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ByteOffset.ToString()];
}

// Loads a 64-bit integer from a named .ucd section blob at position index*8
public sealed class MaxonUcdI64LoadOp(string ucddataLabel, MaxonValue index) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.UcdI64Load;
  public override string Mnemonic => $"maxon.ucd_i64_load {UcddataLabel}";
  public string UcddataLabel { get; } = ucddataLabel;
  public MaxonValue Index { get; } = index;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Index.ToString()];
}

// String literal: stores UTF-8 bytes in rdata and creates a String struct
public sealed class MaxonStringLiteralOp(string value, string stringTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.StringLiteral;
  public override string Mnemonic => $"maxon.string_literal \"{Value}\"";
  public string Value { get; } = value;
  public string StringTypeName { get; } = stringTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), stringTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Byte string literal: stores UTF-8 bytes in rdata and creates a ByteArray (Array with Byte)
public sealed class MaxonByteStringLiteralOp(string value, string arrayTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ByteStringLiteral;
  public override string Mnemonic => $"maxon.byte_string_literal \"{Value}\"";
  public string Value { get; } = value;
  public string ArrayTypeName { get; } = arrayTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), arrayTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Character literal: stores UTF-8 bytes in rdata and creates a Character struct
public sealed class MaxonCharLiteralOp(string value, string charTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CharLiteral;
  public override string Mnemonic => $"maxon.char_literal '{Value}'";
  public string Value { get; } = value;
  public string CharTypeName { get; } = charTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), charTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Other.ToString()];
}

// String interpolation: concatenates literal parts and expression values into a new String
public sealed class MaxonStringInterpOp(List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, IrType? OptimalType)> parts, string stringTypeName) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.StringInterp;
  public override string Mnemonic => "maxon.string_interp";
  public List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, IrType? OptimalType)> Parts { get; } = parts;
  public string StringTypeName { get; } = stringTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), stringTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString(), Start.ToString(), End.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [CursorStruct.ToString()];
}

// Read the current position index from the cursor.
public sealed class MaxonCursorIndexOp(MaxonValue cursorStruct) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CursorIndex;
  public override string Mnemonic => "maxon.cursor_index";
  public MaxonValue CursorStruct { get; } = cursorStruct;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [CursorStruct.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [CursorStruct.ToString(), Ahead.ToString()];
}

// Convert a C string pointer to __ManagedMemory
public sealed class MaxonCStringToManagedOp(MaxonValue cstrPtr) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CStringToManaged;
  public override string Mnemonic => "maxon.cstring_to_managed";
  public MaxonValue CstrPtr { get; } = cstrPtr;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<string> PrintableOperands => [CstrPtr.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Convert __ManagedMemory to a C string pointer
public sealed class MaxonManagedToCStringOp(MaxonValue managed) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedToCString;
  public override string Mnemonic => "maxon.managed_to_cstring";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Write managed memory buffer to stdout, returns number of bytes written
public sealed class MaxonManagedWriteStdoutOp(MaxonValue managed) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedWriteStdout;
  public override string Mnemonic => "maxon.managed_write_stdout";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString()];
}

// Write managed memory buffer to stderr, returns number of bytes written
public sealed class MaxonManagedWriteStderrOp(MaxonValue managed) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedWriteStderr;
  public override string Mnemonic => "maxon.managed_write_stderr";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString()];
}

// Write error message to stderr and terminate with exit code 1
//
// Stdlib and user panics use separate label namespaces so that the stable,
// cached stdlib labels never collide with user-code labels whose counter
// resets each compile. A user-code panic and a stdlib-code panic could
// otherwise both get `__panic_msg_10`, and only one wins in symdata —
// the other prints the wrong message at runtime.
public sealed class MaxonPanicOp(string message, bool isStdlib) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Panic;
  [ThreadStatic] private static Dictionary<string, string>? _userPanicLabelCache;
  [ThreadStatic] private static Dictionary<string, string>? _stdlibPanicLabelCache;
  // Resets the user-code cache. Stdlib labels live in the cached stdlib
  // module and are not re-assigned across user compiles, so the stdlib cache
  // is left alone.
  public static void ResetPanicLabels() => _userPanicLabelCache = null;
  public override string Mnemonic => $"maxon.panic \"{Message}\"";
  public string Message { get; } = message;
  public bool IsStdlib { get; } = isStdlib;
  public string SymdataLabel { get; } = GetOrCreateLabel(message, isStdlib);
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
}

// Write dynamically-constructed error message (from string interpolation) to stderr and terminate
public sealed class MaxonPanicDynamicOp(MaxonStruct messageStruct) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.PanicDynamic;
  public override string Mnemonic => "maxon.panic_dynamic";
  public MaxonStruct MessageStruct { get; } = messageStruct;
  public override IReadOnlyList<string> PrintableOperands => [MessageStruct.ToString()];
}

/// Generic runtime function call op for intrinsics that delegate to a runtime function.
public sealed class MaxonCallRuntimeOp(string functionName, List<MaxonValue> args, bool hasResult) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.CallRuntime;
  public override string Mnemonic => $"maxon.call_runtime.{FunctionName}";
  public string FunctionName { get; } = functionName;
  public List<MaxonValue> Args { get; } = args;
  public MaxonInteger? Result { get; } = hasResult ? new MaxonInteger(IrContext.Current.NextId()) : null;
  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
}

// Create a Character from bytes within a managed buffer
public sealed class MaxonMakeCharFromBytesOp(MaxonValue managed, MaxonValue pos, MaxonValue len) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.MakeCharFromBytes;
  public override string Mnemonic => "maxon.make_char_from_bytes";
  public MaxonValue Managed { get; } = managed;
  public MaxonValue Pos { get; } = pos;
  public MaxonValue Len { get; } = len;
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "Character");
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString(), Pos.ToString(), Len.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// ============================================================================
// ManagedList (doubly-linked list) operations
// ============================================================================

// Creates a new empty managed list data structure
public sealed class MaxonManagedListCreateOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListCreate;
  public override string Mnemonic => "maxon.managed_list_create";
  public MaxonStruct Result { get; } = new MaxonStruct(IrContext.Current.NextId(), "__ManagedList");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString(), Value.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString(), Target.ToString(), Value.ToString()];
}

// Detaches a node from the managed list without freeing it
public sealed class MaxonManagedListDetachOp(MaxonValue managedList, MaxonValue node) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListDetach;
  public override string Mnemonic => "maxon.managed_list_detach";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonValue Node { get; } = node;
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString(), Node.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString(), Node.ToString()];
}

// Returns the number of nodes in the managed list
public sealed class MaxonManagedListCountOp(MaxonValue managedList) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListCount;
  public override string Mnemonic => "maxon.managed_list_count";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Node.ToString()];
}

// Replaces the value stored in a managed list node
public sealed class MaxonManagedListNodeSetValueOp(MaxonValue node, MaxonValue value, string valueKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListNodeSetValue;
  public override string Mnemonic => "maxon.managed_list_node_set_value";
  public MaxonValue Node { get; } = node;
  public MaxonValue Value { get; } = value;
  public string ValueKind { get; set; } = valueKind;
  public override IReadOnlyList<string> PrintableOperands => [Node.ToString(), Value.ToString()];
}

// Removes all nodes from the managed list, freeing each node.
// ValueKind indicates the element type — used to decide whether node values need decref.
public sealed class MaxonManagedListClearOp(MaxonValue managedList, string valueKind) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListClear;
  public override string Mnemonic => "maxon.managed_list_clear";
  public MaxonValue ManagedList { get; } = managedList;
  public string ValueKind { get; set; } = valueKind;
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString()];
}

// Resets the managed list's iteration cursor to null (0)
public sealed class MaxonManagedListCursorResetOp(MaxonValue managedList) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListCursorReset;
  public override string Mnemonic => "maxon.managed_list_cursor_reset";
  public MaxonValue ManagedList { get; } = managedList;
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString()];
}

// Returns the head node pointer as a raw int (no refcounting)
public sealed class MaxonManagedListHeadPtrOp(MaxonValue managedList) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListHeadPtr;
  public override string Mnemonic => "maxon.managed_list_head_ptr";
  public MaxonValue ManagedList { get; } = managedList;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedList.ToString()];
}

// Returns cursor->next as a raw int (no refcounting). Caller must check for null.
public sealed class MaxonManagedListNodePtrNextOp(MaxonValue cursorPtr) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.ManagedListNodePtrNext;
  public override string Mnemonic => "maxon.managed_list_node_ptr_next";
  public MaxonValue CursorPtr { get; } = cursorPtr;
  public MaxonInteger Result { get; } = new MaxonInteger(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [CursorPtr.ToString()];
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
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [CursorPtr.ToString()];
}

// ========== Async/Await ops ==========

/// Spawns a green thread to execute a function call.
/// The result is a MaxonPromise that can be awaited.
public sealed class MaxonAsyncCallOp(string callee, List<MaxonValue> args, MaxonValueKind? innerResultKind, string? innerStructTypeName, bool throws = false) : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.AsyncCall;
  public override string Mnemonic => $"maxon.async_call @{Callee}";
  public string Callee { get; } = callee;
  public List<MaxonValue> Args { get; } = args;
  public MaxonPromise Result { get; } = new MaxonPromise(IrContext.Current.NextId(), innerResultKind, innerStructTypeName, throws);
  /// The return type of the spawned function (what await will produce)
  public MaxonValueKind? InnerResultKind { get; } = innerResultKind;
  public string? InnerStructTypeName { get; } = innerStructTypeName;
  /// Whether the spawned function is a throwing function.
  public bool Throws { get; } = throws;
  public List<bool>? ArgMutabilities { get; set; }
  public List<string?>? ArgVarNames { get; set; }
  /// Source location for error reporting (line of the 'async' keyword)
  public int? CallLine { get; set; }
  public int? CallColumn { get; set; }
  /// The source text of the async call expression (for error messages)
  public string? CallSourceText { get; set; }
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
}

/// Waits for a green thread (promise) to complete and extracts its result.
public sealed class MaxonAwaitOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.Await;
  public override string Mnemonic => "maxon.await";
  public MaxonValue Promise { get; }
  public MaxonValue? Result { get; }
  public MaxonValueKind? ResultKind { get; }
  public string? ResultStructTypeName { get; }
  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [Promise.ToString()];

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
public sealed class MaxonTryAwaitOp : MaxonOp {
  public override MaxonOpKind Kind => MaxonOpKind.TryAwait;
  public override string Mnemonic => "maxon.try_await";
  public MaxonValue Promise { get; }
  public MaxonValue? Result { get; }
  public MaxonInteger ErrorFlag { get; }
  public MaxonValueKind? ResultKind { get; }
  public string? ResultStructTypeName { get; }
  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString(), ErrorFlag.ToString()] : [ErrorFlag.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Promise.ToString()];

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
  public override IReadOnlyList<string> PrintableOperands => [Promise.ToString()];
}
