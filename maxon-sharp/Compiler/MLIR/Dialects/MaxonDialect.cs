using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public enum MaxonValueKind { Integer, Float, Float32, Bool, Byte, Short, Struct, Enum, Function, TypeParameter }

public static class MaxonValueKindExtensions {
  public static MlirType ToMlirType(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => MlirType.I64,
    MaxonValueKind.Float => MlirType.F64,
    MaxonValueKind.Float32 => MlirType.F32,
    MaxonValueKind.Bool => MlirType.I1,
    MaxonValueKind.Byte => MlirType.I8,
    MaxonValueKind.Short => MlirType.I16,
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require lookup via type registry, not ToMlirType()"),
    MaxonValueKind.Enum => throw new InvalidOperationException("Enum kinds require lookup via type registry, not ToMlirType()"),
    MaxonValueKind.Function => throw new InvalidOperationException("Function kinds require lookup via function type, not ToMlirType()"),
    MaxonValueKind.TypeParameter => MlirType.I64, // unresolved type parameter stored as i64
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
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValue CreateValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
    MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
    MaxonValueKind.Float32 => new MaxonFloat(MlirContext.Current.NextId()),
    MaxonValueKind.Bool => new MaxonBool(MlirContext.Current.NextId()),
    MaxonValueKind.Byte => new MaxonByte(MlirContext.Current.NextId()),
    MaxonValueKind.Short => new MaxonShort(MlirContext.Current.NextId()),
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require a type name, use CreateStructValue() instead"),
    MaxonValueKind.Enum => new MaxonInteger(MlirContext.Current.NextId()),
    MaxonValueKind.Function => new MaxonFunctionPtr(MlirContext.Current.NextId()),
    MaxonValueKind.TypeParameter => new MaxonInteger(MlirContext.Current.NextId()),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValueKind ToValueKind(this MlirType type) {
    if (type == MlirType.I64) return MaxonValueKind.Integer;
    if (type == MlirType.F64) return MaxonValueKind.Float;
    if (type == MlirType.F32) return MaxonValueKind.Float32;
    if (type == MlirType.I1) return MaxonValueKind.Bool;
    if (type == MlirType.I8) return MaxonValueKind.Byte;
    // Unsigned/narrowed integer types from ranged type optimal storage
    if (type == MlirType.U8) return MaxonValueKind.Byte;
    if (type == MlirType.I16 || type == MlirType.U16) return MaxonValueKind.Short;
    if (type == MlirType.I32 || type == MlirType.U32 || type == MlirType.U64) return MaxonValueKind.Integer;
    if (type is MlirRangedPrimitiveType rpt) return rpt.BaseType.ToValueKind();
    if (type is MlirUnionType) return MaxonValueKind.Enum;
    if (type is MlirTypeParameterType) return MaxonValueKind.TypeParameter;
    if (type is MlirStructType) return MaxonValueKind.Struct;
    if (type is MlirFunctionType) return MaxonValueKind.Function;
    throw new ArgumentOutOfRangeException(nameof(type), $"No MaxonValueKind for MlirType: {type}");
  }

  public static StdValue CreateStdValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Float => new StdF64(MlirContext.Current.NextId()),
    MaxonValueKind.Float32 => new StdF32(MlirContext.Current.NextId()),
    MaxonValueKind.Bool => new StdBool(MlirContext.Current.NextId()),
    MaxonValueKind.Byte => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Short => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Struct => new StdPtr(MlirContext.Current.NextId()),
    MaxonValueKind.Enum => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Function => new StdPtr(MlirContext.Current.NextId()),
    MaxonValueKind.TypeParameter => new StdI64(MlirContext.Current.NextId()),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };
}

public enum MaxonBinOperator {
  Add, Sub, Mul, Div, Mod,
  Eq, Ne, Lt, Gt, Le, Ge,
  And, Or,
  BitAnd, BitOr, BitXor, Shl, Shr
}

public abstract class MaxonOp : IPrintableOp {
  public abstract string Mnemonic { get; }
  public virtual IReadOnlyList<string> PrintableResults => [];
  public virtual IReadOnlyList<string> PrintableOperands => [];
  public virtual IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
}

public class MaxonLiteralOp : MaxonOp {
  public override string Mnemonic => "maxon.literal";
  public MaxonValueKind ValueKind { get; }
  public long IntValue { get; }
  public double FloatValue { get; }
  public bool BoolValue { get; }
  public MaxonValue Result { get; }
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    ValueKind switch {
      MaxonValueKind.Integer => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(IntValue, MlirType.I64) },
      MaxonValueKind.Float => new Dictionary<string, MlirAttribute> { ["value"] = new FloatAttr(FloatValue, MlirType.F64) },
      MaxonValueKind.Float32 => new Dictionary<string, MlirAttribute> { ["value"] = new FloatAttr(FloatValue, MlirType.F32) },
      MaxonValueKind.Bool => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(BoolValue ? 1 : 0, MlirType.I1) },
      MaxonValueKind.Byte => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(IntValue, MlirType.I8) },
      MaxonValueKind.Short => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(IntValue, MlirType.I16) },
      MaxonValueKind.Struct => throw new InvalidOperationException("Struct literals are not MaxonLiteralOp"),
      MaxonValueKind.Enum => throw new InvalidOperationException("Enum literals are not MaxonLiteralOp"),
      _ => throw new ArgumentOutOfRangeException(),
    };

  public MaxonLiteralOp(long value) {
    ValueKind = MaxonValueKind.Integer;
    IntValue = value;
    Result = new MaxonInteger(MlirContext.Current.NextId());
  }

  public MaxonLiteralOp(double value) {
    ValueKind = MaxonValueKind.Float;
    FloatValue = value;
    Result = new MaxonFloat(MlirContext.Current.NextId());
  }

  public MaxonLiteralOp(double value, MaxonValueKind floatKind) {
    ValueKind = floatKind;
    FloatValue = value;
    Result = new MaxonFloat(MlirContext.Current.NextId());
  }

  public MaxonLiteralOp(bool value) {
    ValueKind = MaxonValueKind.Bool;
    BoolValue = value;
    Result = new MaxonBool(MlirContext.Current.NextId());
  }
}

public class MaxonAssignOp(string varName, MaxonValue value, bool isDeclaration, bool isMutable, MaxonValueKind valueKind) : MaxonOp {
  public override string Mnemonic => "maxon.assign";
  public string VarName { get; } = varName;
  public MaxonValue Value { get; } = value;
  public bool IsDeclaration { get; } = isDeclaration;
  public bool IsMutable { get; } = isMutable;
  public MaxonValueKind ValueKind { get; } = valueKind;
  public OwnershipFlags? OwnerFlags { get; set; }
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, MlirAttribute> {
        ["var"] = new StringAttr(VarName),
      };
      if (ValueKind is not (MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.Function or MaxonValueKind.TypeParameter)) {
        attrs["kind"] = new TypeAttr(ValueKind.ToMlirType());
      }
      if (IsDeclaration) attrs["decl"] = new IntegerAttr(1, MlirType.I1);
      if (IsMutable) attrs["mut"] = new IntegerAttr(1, MlirType.I1);
      return attrs;
    }
  }
}

public class MaxonParamOp(int index, string name, MaxonValueKind kind) : MaxonOp {
  public override string Mnemonic => "maxon.param";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public MaxonValueKind ValueKind { get; } = kind;
  public MaxonValue Result { get; } = kind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, MlirAttribute> {
        ["index"] = new IntegerAttr(Index, MlirType.I32),
        ["name"] = new StringAttr(Name),
      };
      if (ValueKind is not (MaxonValueKind.TypeParameter or MaxonValueKind.Struct or MaxonValueKind.Enum))
        attrs["type"] = new TypeAttr(ValueKind.ToMlirType());
      return attrs;
    }
  }
}

// Struct parameter op: represents a struct being received as a function parameter.
// At the Maxon level the struct is a single logical param; at the Standard level
// it is flattened into individual scalar params per field.
public class MaxonStructParamOp(int index, string name, string structTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.struct_param @{StructTypeName}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public string StructTypeName { get; } = structTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), structTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Function parameter op: represents a function pointer being received as a function parameter.
public class MaxonFunctionParamOp(int index, string name, MlirFunctionType functionType) : MaxonOp {
  public override string Mnemonic => $"maxon.function_param";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public MlirFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Function reference op: gets a pointer to a named function
public class MaxonFunctionRefOp(string functionName, MlirFunctionType functionType) : MaxonOp {
  public override string Mnemonic => $"maxon.function_ref @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public MlirFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Creates a closure with captured values from the enclosing scope
public class MaxonClosureCreateOp(string functionName, MlirFunctionType functionType,
    List<MaxonValue> capturedValues, List<string> capturedNames,
    List<MaxonValueKind> capturedKinds, List<string?> capturedStructTypes) : MaxonOp {
  public override string Mnemonic => $"maxon.closure_create @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public MlirFunctionType FunctionType { get; } = functionType;
  public List<MaxonValue> CapturedValues { get; } = capturedValues;
  public List<string> CapturedNames { get; } = capturedNames;
  public List<MaxonValueKind> CapturedKinds { get; } = capturedKinds;
  public List<string?> CapturedStructTypes { get; } = capturedStructTypes;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [.. CapturedValues.Select(v => v.ToString())];
}

// Inside a capturing closure: loads a captured value from the environment pointer
public class MaxonClosureEnvLoadOp(int index, string name, MaxonValueKind kind, string? structTypeName = null) : MaxonOp {
  public override string Mnemonic => $"maxon.closure_env_load {Name}[{Index}]";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public MaxonValueKind Kind { get; } = kind;
  public string? StructTypeName { get; } = structTypeName;
  public MaxonValue Result { get; } = kind == MaxonValueKind.Struct
      ? new MaxonStruct(MlirContext.Current.NextId(), structTypeName!)
      : kind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Function var ref: loads a function pointer from a variable in a different block
public class MaxonFunctionVarRefOp(string varName, MlirFunctionType functionType) : MaxonOp {
  public override string Mnemonic => $"maxon.function_var_ref {VarName}";
  public string VarName { get; } = varName;
  public MlirFunctionType FunctionType { get; } = functionType;
  public MaxonFunctionPtr Result { get; } = new MaxonFunctionPtr(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Indirect call op: calls a function through a function pointer
public class MaxonIndirectCallOp : MaxonOp {
  public override string Mnemonic => "maxon.indirect_call";
  public MaxonValue Callee { get; }
  public MlirFunctionType CalleeType { get; }
  public List<MaxonValue> Args { get; }
  public MaxonValue? Result { get; }
  public MaxonValueKind? ResultKind { get; }
  public string? ResultStructTypeName { get; }
  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString()).Prepend(Callee.ToString())];

  public MaxonIndirectCallOp(MaxonValue callee, MlirFunctionType calleeType, List<MaxonValue> args, MaxonValueKind? resultKind = null, string? resultStructTypeName = null) {
    Callee = callee;
    CalleeType = calleeType;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    if (resultKind != null) {
      Result = resultKind == MaxonValueKind.Struct
        ? new MaxonStruct(MlirContext.Current.NextId(), resultStructTypeName!)
        : resultKind.Value.CreateValue();
    }
  }
}

public class MaxonVarRefOp(string varName, MaxonValueKind kind) : MaxonOp {
  public override string Mnemonic => "maxon.var_ref";
  public string VarName { get; } = varName;
  public MaxonValueKind ValueKind { get; } = kind;
  public MaxonValue Result { get; } = kind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> {
      ["var"] = new StringAttr(VarName),
      ["type"] = new TypeAttr(ValueKind.ToMlirType())
    };
}

// Struct var ref: loads a struct from a variable in a different block
public class MaxonStructVarRefOp(string varName, string structTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.struct_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string StructTypeName { get; } = structTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), structTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

public class MaxonBinOp(MaxonBinOperator op, MaxonValue lhs, MaxonValue rhs, MaxonValueKind operandKind,
    MlirType? optimalType = null) : MaxonOp {
  public override string Mnemonic => "maxon.binop";
  public MaxonBinOperator Operator { get; } = op;
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonValueKind OperandKind { get; } = operandKind;
  public MlirType? OptimalType { get; } = optimalType;
  public bool IsUnsigned => OptimalType?.IsUnsigned ?? false;
  public MaxonValue Result { get; } = IsComparison(op)
      ? new MaxonBool(MlirContext.Current.NextId())
      : operandKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, MlirAttribute> {
        ["op"] = new StringAttr(Operator.ToString().ToLowerInvariant()),
      };
      if (OperandKind is MaxonValueKind.Float or MaxonValueKind.Float32)
        attrs["kind"] = new TypeAttr(OperandKind.ToMlirType());
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
public class MaxonRefEqOp(MaxonValue lhs, MaxonValue rhs, bool negate) : MaxonOp {
  public override string Mnemonic => Negate ? "maxon.ref_ne" : "maxon.ref_eq";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public bool Negate { get; } = negate;
  public MaxonBool Result { get; } = new MaxonBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
}

public class MaxonCallOp : MaxonOp {
  public override string Mnemonic => $"maxon.call @{Callee}";
  public string Callee { get; set; }
  public List<MaxonValue> Args { get; }
  public MaxonValue? Result { get; protected set; }
  public MaxonValueKind? ResultKind { get; }
  // The struct type name for calls returning a struct
  public string? ResultStructTypeName { get; }
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
      return new MaxonStruct(MlirContext.Current.NextId(), resultStructTypeName!);
    if (resultKind == MaxonValueKind.Enum)
      return new MaxonEnum(MlirContext.Current.NextId(), resultStructTypeName!);
    return resultKind?.CreateValue();
  }

  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
}

// Calls a throwing function and captures both the result and error flag.
// ErrorFlag is non-zero if the callee threw an error.
public class MaxonTryCallOp : MaxonCallOp {
  public override string Mnemonic => $"maxon.try_call @{Callee}";
  public MaxonInteger ErrorFlag { get; }

  public MaxonTryCallOp(string callee, List<MaxonValue> args, MaxonValueKind? resultKind = null, string? resultStructTypeName = null)
    : base(callee, args, (MaxonValue?)null, resultKind, resultStructTypeName) {
    ErrorFlag = new MaxonInteger(MlirContext.Current.NextId());
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

public class MaxonTruncOp(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.trunc";
  public MaxonValue Input { get; } = input;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonIntToFloatOp(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.int_to_float";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonCastOp(MaxonValue input, MaxonValueKind targetKind,
    MlirType? sourceOptimalType = null) : MaxonOp {
  public override string Mnemonic => $"maxon.cast";
  public MaxonValue Input { get; } = input;
  public MaxonValueKind TargetKind { get; } = targetKind;
  public MlirType? SourceOptimalType { get; } = sourceOptimalType;
  public bool SourceIsUnsigned => SourceOptimalType?.IsUnsigned ?? false;
  public MaxonValue Result { get; } = targetKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> { ["target"] = new TypeAttr(TargetKind.ToMlirType()) };
}

public class MaxonAbsOp(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.abs";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonSqrtOp(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.sqrt";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonFloorOp(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.floor";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonCeilOp(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.ceil";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonRoundOp(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.round";
  public MaxonValue Input { get; } = input;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonBitcastF64ToI64Op(MaxonValue input) : MaxonOp {
  public override string Mnemonic => "maxon.bitcast_f64_to_i64";
  public MaxonValue Input { get; } = input;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
}

public class MaxonMinOp(MaxonValue lhs, MaxonValue rhs) : MaxonOp {
  public override string Mnemonic => "maxon.min";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
}

public class MaxonMaxOp(MaxonValue lhs, MaxonValue rhs) : MaxonOp {
  public override string Mnemonic => "maxon.max";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonFloat Result { get; } = new MaxonFloat(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
}

public class MaxonCondBrOp(MaxonValue condition, string thenBlock, string elseBlock) : MaxonOp {
  public override string Mnemonic => $"maxon.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
  public MaxonValue Condition { get; } = condition;
  public string ThenBlock { get; } = thenBlock;
  public string ElseBlock { get; } = elseBlock;
}

public class MaxonBrOp(string target) : MaxonOp {
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
public class MaxonScopeEndOp(IReadOnlyList<string> varsToClean, HashSet<string>? keepVars = null) : MaxonOp {
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

public class MaxonReturnOp(MaxonValue? value = null, bool isErrorPropagation = false) : MaxonOp {
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
public class MaxonThrowOp(MaxonValue errorValue, string errorTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.throw @{ErrorTypeName}";
  public MaxonValue ErrorValue { get; } = errorValue;
  public string ErrorTypeName { get; } = errorTypeName;
  public override IReadOnlyList<string> PrintableOperands => [ErrorValue.ToString()];
}

// ============================================================================
// Struct operations
// ============================================================================

// Creates a struct instance from field values: Point{x: 3, y: 4}
public class MaxonStructLiteralOp(string typeName, List<(string FieldName, MaxonValue Value)> fieldValues) : MaxonOp {
  public override string Mnemonic => $"maxon.struct_literal @{TypeName}";
  public string TypeName { get; } = typeName;
  public List<(string FieldName, MaxonValue Value)> FieldValues { get; } = fieldValues;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), typeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  // For array literals: tag prefix and count of sequential element variables
  public string? ArrayLiteralTag { get; set; }
  public int ArrayLiteralCount { get; set; }
  // Skip element zero-initialization (stack space reserved but not cleared)
  public bool SkipZeroInit { get; set; }
  // Source location for trace output (e.g. "main.maxon:12")
  public string? SourceLocation { get; set; }
}

// Reads a field: p.x
public class MaxonFieldAccessOp(MaxonValue structValue, string typeName, string fieldName, MaxonValueKind resultKind, string? resultStructTypeName = null) : MaxonOp {
  public override string Mnemonic => $"maxon.field_access .{FieldName}";
  public MaxonValue StructValue { get; } = structValue;
  public string TypeName { get; } = typeName;
  public string FieldName { get; } = fieldName;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public string? ResultStructTypeName { get; } = resultStructTypeName;
  public MaxonValue Result { get; } = resultKind switch {
    MaxonValueKind.Struct => new MaxonStruct(MlirContext.Current.NextId(), resultStructTypeName!),
    MaxonValueKind.Enum => new MaxonEnum(MlirContext.Current.NextId(), resultStructTypeName!),
    _ => resultKind.CreateValue()
  };
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [StructValue.ToString()];
}

// Assigns to a field: p.x = 30
public class MaxonFieldAssignOp(MaxonValue structValue, string typeName, string fieldName, MaxonValue newValue) : MaxonOp {
  public override string Mnemonic => $"maxon.field_assign .{FieldName}";
  public MaxonValue StructValue { get; } = structValue;
  public string TypeName { get; } = typeName;
  public string FieldName { get; } = fieldName;
  public MaxonValue NewValue { get; } = newValue;
  public override IReadOnlyList<string> PrintableOperands => [StructValue.ToString(), NewValue.ToString()];
}

// ============================================================================
// Global variable operations (for top-level var and static var)
// ============================================================================

public class MaxonGlobalLoadOp(string globalName, MaxonValueKind kind, string? enumTypeName = null, string? structTypeName = null) : MaxonOp {
  public override string Mnemonic => $"maxon.global_load @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public MaxonValueKind ValueKind { get; } = kind;
  public string? EnumTypeName { get; } = enumTypeName;
  public string? StructTypeName { get; } = structTypeName;
  public MaxonValue Result { get; } = structTypeName != null ? new MaxonStruct(MlirContext.Current.NextId(), structTypeName)
    : enumTypeName != null ? new MaxonEnum(MlirContext.Current.NextId(), enumTypeName)
    : kind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> {
      ["global"] = new StringAttr(GlobalName),
      ["type"] = new TypeAttr(ValueKind is MaxonValueKind.Enum or MaxonValueKind.Struct ? MlirType.I64 : ValueKind.ToMlirType())
    };
}

// Creates an enum value for a specific case
public class MaxonEnumLiteralOp : MaxonOp {
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
    Result = new MaxonEnum(MlirContext.Current.NextId(), enumTypeName);
  }

  public MaxonEnumLiteralOp(string enumTypeName, string caseName, double floatValue) {
    EnumTypeName = enumTypeName;
    CaseName = caseName;
    BackingKind = MaxonValueKind.Float;
    FloatValue = floatValue;
    Result = new MaxonEnum(MlirContext.Current.NextId(), enumTypeName);
  }
}

// Constructs an associated-value enum case: Container.value(42)
// For cases without associated values (e.g. Container.empty), Args is empty.
public class MaxonEnumConstructOp(string enumTypeName, string caseName, int ordinal, List<MaxonValue> args) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_construct @{EnumTypeName}.{CaseName}";
  public string EnumTypeName { get; } = enumTypeName;
  public string CaseName { get; } = caseName;
  public int Ordinal { get; } = ordinal;
  public List<MaxonValue> Args { get; } = args;
  public MaxonEnum Result { get; } = new MaxonEnum(MlirContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
  // Source location for trace output (e.g. "main.maxon:12")
  public string? SourceLocation { get; set; }
}

// Extracts the tag (ordinal) from an associated-value enum
public class MaxonEnumTagOp(MaxonValue enumValue, string enumTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_tag @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Extracts a payload value at a given index from an associated-value enum
public class MaxonEnumPayloadOp(MaxonValue enumValue, string enumTypeName, int payloadIndex, MaxonValueKind resultKind, string? resultStructTypeName = null) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_payload @{EnumTypeName}[{PayloadIndex}]";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public int PayloadIndex { get; } = payloadIndex;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public string? ResultStructTypeName { get; } = resultStructTypeName;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(MlirContext.Current.NextId(), resultStructTypeName!)
    : resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Enum parameter op: represents an enum being received as a function parameter
public class MaxonEnumParamOp(int index, string name, string enumTypeName, MaxonValueKind backingKind) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_param @{EnumTypeName}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public MaxonEnum Result { get; } = new MaxonEnum(MlirContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Writes a value back to a specific payload slot in an associated-value enum's heap block
public class MaxonEnumPayloadAssignOp(string enumVarName, string enumTypeName, int payloadIndex, MaxonValue newValue) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_payload_assign @{EnumTypeName}[{PayloadIndex}]";
  public string EnumVarName { get; } = enumVarName;
  public string EnumTypeName { get; } = enumTypeName;
  public int PayloadIndex { get; } = payloadIndex;
  public MaxonValue NewValue { get; } = newValue;
  public override IReadOnlyList<string> PrintableOperands => [NewValue.ToString()];
}

// Enum var ref: loads an enum from a variable in a different block
public class MaxonEnumVarRefOp(string varName, string enumTypeName, MaxonValueKind backingKind) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public MaxonEnum Result { get; } = new MaxonEnum(MlirContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Converts an error flag (ordinal+1) back to a typed enum value (ordinal)
// For simple error enums, subtracts 1 from the flag to recover the ordinal.
// For associated-value error enums, the flag is a heap pointer (no arithmetic needed).
public class MaxonErrorFlagToEnumOp(MaxonValue errorFlag, string enumTypeName, MaxonValueKind backingKind, bool hasAssociatedValues) : MaxonOp {
  public override string Mnemonic => $"maxon.error_flag_to_enum @{EnumTypeName}";
  public MaxonValue ErrorFlag { get; } = errorFlag;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public bool HasAssociatedValues { get; } = hasAssociatedValues;
  public MaxonEnum Result { get; } = new MaxonEnum(MlirContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ErrorFlag.ToString()];
}

// Accesses .rawValue on an enum value
public class MaxonEnumRawValueOp(MaxonValue enumValue, string enumTypeName, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Float or MaxonValueKind.Float32
    ? new MaxonFloat(MlirContext.Current.NextId())
    : new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Accesses .rawValue on a string or char-backed enum, returning String or Character
public class MaxonEnumStringRawValueOp(MaxonValue enumValue, string enumTypeName, bool isChar) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_string_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public bool IsChar { get; } = isChar;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), isChar ? "Character" : "String");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

// Accesses .name on an enum value, returning the case name as a String
public class MaxonEnumNameOp(MaxonValue enumValue, string enumTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_name @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "String");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [EnumValue.ToString()];
}

public class MaxonGlobalStoreOp(string globalName, MaxonValue value, MaxonValueKind kind) : MaxonOp {
  public override string Mnemonic => $"maxon.global_store @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public MaxonValue Value { get; } = value;
  public MaxonValueKind ValueKind { get; } = kind;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> {
      ["global"] = new StringAttr(GlobalName),
      ["type"] = new TypeAttr(ValueKind is MaxonValueKind.Enum or MaxonValueKind.Struct ? MlirType.I64 : ValueKind.ToMlirType())
    };
}

// ============================================================================
// Managed memory operations (for __ManagedMemory builtin intrinsics)
// ============================================================================

// Get element at index from managed buffer: managed.get(index)
// Element size is read from the managed struct's element_size field at runtime.
// When IsStructElement is true, the element data is stored inline in the buffer
// and the result is a pointer to the element's location (not a loaded value).
public class MaxonManagedMemGetOp(MaxonValue managedStruct, MaxonValue index, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_get";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public bool IsStructElement { get; init; }
  /// The concrete struct type name when IsStructElement is true
  public string? StructElementTypeName { get; init; }
  /// When ResultKind is TypeParameter, this identifies which type param (e.g., "Key", "Value", "Element")
  public string? TypeParamName { get; init; }
  // Result is always a scalar or pointer — struct/enum elements produce a pointer to inline data
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.TypeParameter
    ? new MaxonInteger(MlirContext.Current.NextId()) : resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
}

// Set element at index in managed buffer: managed.set(index, value)
// Element size is read from the managed struct's element_size field at runtime.
// When IsStructElement is true, the value is a pointer to struct data and the
// full struct is copied inline into the buffer (not just the pointer).
public class MaxonManagedMemSetOp(MaxonValue managedStruct, MaxonValue index, MaxonValue value, MaxonValueKind elementKind = MaxonValueKind.Integer) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_set";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Value { get; } = value;
  public MaxonValueKind ElementKind { get; } = elementKind;
  public bool IsStructElement { get; init; }
  public string? TypeParamName { get; init; }
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Value.ToString()];
}

// Create a new heap-allocated managed memory: __ManagedMemory.create(count, elemSize)
public class MaxonManagedMemCreateOp(MaxonValue count, int elementSize) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_create";
  public MaxonValue Count { get; } = count;
  public int ElementSize { get; } = elementSize;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Count.ToString()];
}

// Grow managed memory to new capacity: managed.grow(newCap)
// Element size is read from the managed struct's element_size field at runtime.
public class MaxonManagedMemGrowOp(MaxonValue managedStruct, MaxonValue newCapacity) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_grow";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue NewCapacity { get; } = newCapacity;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), NewCapacity.ToString()];
}

// Set length of managed memory with capacity validation
public class MaxonManagedMemSetLengthOp(MaxonValue managedStruct, MaxonValue newLength) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_set_length";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue NewLength { get; } = newLength;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), NewLength.ToString()];
}

// Clear all elements from managed memory, decrementing struct element refcounts
public class MaxonManagedMemClearOp(MaxonValue managedStruct) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_clear";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public bool IsStructElement { get; init; }
  public string? StructElementTypeName { get; init; }
  public string? TypeParamName { get; init; }
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString()];
}

// Shift elements right/left in managed buffer
// Element size is read from the managed struct's element_size field at runtime.
public class MaxonManagedMemShiftOp(MaxonValue managedStruct, MaxonValue index, MaxonValue count, bool shiftRight) : MaxonOp {
  public override string Mnemonic => ShiftRight ? "maxon.managed_mem_shift_right" : "maxon.managed_mem_shift_left";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Count { get; } = count;
  public bool ShiftRight { get; } = shiftRight;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Count.ToString()];
}

// Remove element at index: load element (ownership transfer), shift left, shrink length.
// For struct elements the loaded pointer is NOT incref'd — the buffer's reference is
// transferred to the caller. The slot is zeroed after loading to prevent double-free
// if mm_decref_managed_elements walks the buffer before the shift completes.
public class MaxonManagedMemRemoveOp(MaxonValue managedStruct, MaxonValue index, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_remove";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public bool IsStructElement { get; init; }
  public string? StructElementTypeName { get; init; }
  public string? TypeParamName { get; init; }
  public MaxonValue Result { get; } = resultKind is MaxonValueKind.Struct or MaxonValueKind.Enum or MaxonValueKind.TypeParameter
    ? new MaxonInteger(MlirContext.Current.NextId()) : resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
}

// Get byte at index in managed buffer: managed.byteAt(index)
public class MaxonManagedMemByteGetOp(MaxonValue managedStruct, MaxonValue index) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_byte_get";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonByte Result { get; } = new MaxonByte(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
}

// Set byte at index in managed buffer: managed.setByte(index, value)
public class MaxonManagedMemByteSetOp(MaxonValue managedStruct, MaxonValue index, MaxonValue value) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_byte_set";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Value { get; } = value;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Value.ToString()];
}

// String literal: stores UTF-8 bytes in rdata and creates a String struct
public class MaxonStringLiteralOp(string value, string stringTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.string_literal \"{Value}\"";
  public string Value { get; } = value;
  public string StringTypeName { get; } = stringTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), stringTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Byte string literal: stores UTF-8 bytes in rdata and creates a ByteArray (Array with Byte)
public class MaxonByteStringLiteralOp(string value, string arrayTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.byte_string_literal \"{Value}\"";
  public string Value { get; } = value;
  public string ArrayTypeName { get; } = arrayTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), arrayTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Character literal: stores UTF-8 bytes in rdata and creates a Character struct
public class MaxonCharLiteralOp(string value, string charTypeName) : MaxonOp {
  public override string Mnemonic => $"maxon.char_literal '{Value}'";
  public string Value { get; } = value;
  public string CharTypeName { get; } = charTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), charTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Concatenate two __ManagedMemory buffers into a new one
public class MaxonManagedMemConcatOp(MaxonValue lhs, MaxonValue rhs) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_concat";
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public bool IsStructElement { get; init; }
  public string? TypeParamName { get; init; }
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// String interpolation: concatenates literal parts and expression values into a new String
public class MaxonStringInterpOp(List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)> parts, string stringTypeName) : MaxonOp {
  public override string Mnemonic => "maxon.string_interp";
  public List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)> Parts { get; } = parts;
  public string StringTypeName { get; } = stringTypeName;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), stringTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Create a slice of a __ManagedMemory buffer (start/end byte positions)
public class MaxonManagedMemSliceOp(MaxonValue managed, MaxonValue start, MaxonValue end) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_slice";
  public MaxonValue Managed { get; } = managed;
  public MaxonValue Start { get; } = start;
  public MaxonValue End { get; } = end;
  public bool IsStructElement { get; init; }
  public string? TypeParamName { get; init; }
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString(), Start.ToString(), End.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Convert a C string pointer to __ManagedMemory
public class MaxonCStringToManagedOp(MaxonValue cstrPtr) : MaxonOp {
  public override string Mnemonic => "maxon.cstring_to_managed";
  public MaxonValue CstrPtr { get; } = cstrPtr;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<string> PrintableOperands => [CstrPtr.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Convert __ManagedMemory to a C string pointer
public class MaxonManagedToCStringOp(MaxonValue managed) : MaxonOp {
  public override string Mnemonic => "maxon.managed_to_cstring";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Write managed memory buffer to stdout, returns number of bytes written
public class MaxonManagedWriteStdoutOp(MaxonValue managed) : MaxonOp {
  public override string Mnemonic => "maxon.managed_write_stdout";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString()];
}

// Write managed memory buffer to stderr, returns number of bytes written
public class MaxonManagedWriteStderrOp(MaxonValue managed) : MaxonOp {
  public override string Mnemonic => "maxon.managed_write_stderr";
  public MaxonValue Managed { get; } = managed;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString()];
}

// Write error message to stderr and terminate with exit code 1
public class MaxonPanicOp(string message) : MaxonOp {
  public override string Mnemonic => $"maxon.panic \"{Message}\"";
  public string Message { get; } = message;
  public string SymdataLabel { get; } = $"__panic_msg_{MlirContext.Current.NextId()}";
}

// Write dynamically-constructed error message (from string interpolation) to stderr and terminate
public class MaxonPanicDynamicOp(MaxonStruct messageStruct) : MaxonOp {
  public override string Mnemonic => "maxon.panic_dynamic";
  public MaxonStruct MessageStruct { get; } = messageStruct;
  public override IReadOnlyList<string> PrintableOperands => [MessageStruct.ToString()];
}

/// Generic runtime function call op for intrinsics that delegate to a runtime function.
public class MaxonCallRuntimeOp(string functionName, List<MaxonValue> args, bool hasResult) : MaxonOp {
  public override string Mnemonic => $"maxon.call_runtime.{FunctionName}";
  public string FunctionName { get; } = functionName;
  public List<MaxonValue> Args { get; } = args;
  public MaxonInteger? Result { get; } = hasResult ? new MaxonInteger(MlirContext.Current.NextId()) : null;
  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
}

// Create a Character from bytes within a managed buffer
public class MaxonMakeCharFromBytesOp(MaxonValue managed, MaxonValue pos, MaxonValue len) : MaxonOp {
  public override string Mnemonic => "maxon.make_char_from_bytes";
  public MaxonValue Managed { get; } = managed;
  public MaxonValue Pos { get; } = pos;
  public MaxonValue Len { get; } = len;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "Character");
  public override IReadOnlyList<string> PrintableOperands => [Managed.ToString(), Pos.ToString(), Len.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// ============================================================================
// Chain (doubly-linked list) operations
// ============================================================================

// Creates a new empty chain data structure
public class MaxonChainCreateOp : MaxonOp {
  public override string Mnemonic => "maxon.chain_create";
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__Chain");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Inserts a value at the head or tail of the chain, creating a new node
public class MaxonChainInsertValueOp(MaxonValue chain, MaxonValue value, bool atHead, string valueKind) : MaxonOp {
  public override string Mnemonic => AtHead ? "maxon.chain_insert_head" : "maxon.chain_insert_tail";
  public MaxonValue Chain { get; } = chain;
  public MaxonValue Value { get; } = value;
  public bool AtHead { get; } = atHead;
  public string ValueKind { get; } = valueKind;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ChainNode");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString(), Value.ToString()];
}

// Inserts a value relative to a target node (before or after)
public class MaxonChainInsertRelativeValueOp(MaxonValue chain, MaxonValue target, MaxonValue value, bool after, string valueKind) : MaxonOp {
  public override string Mnemonic => After ? "maxon.chain_insert_after" : "maxon.chain_insert_before";
  public MaxonValue Chain { get; } = chain;
  public MaxonValue Target { get; } = target;
  public MaxonValue Value { get; } = value;
  public bool After { get; } = after;
  public string ValueKind { get; } = valueKind;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ChainNode");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString(), Target.ToString(), Value.ToString()];
}

// Reinserts an existing node at the head or tail of the chain
public class MaxonChainReinsertOp(MaxonValue chain, MaxonValue node, bool atHead) : MaxonOp {
  public override string Mnemonic => AtHead ? "maxon.chain_reinsert_head" : "maxon.chain_reinsert_tail";
  public MaxonValue Chain { get; } = chain;
  public MaxonValue Node { get; } = node;
  public bool AtHead { get; } = atHead;
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString(), Node.ToString()];
}

// Reinserts an existing node relative to a target node (before or after)
public class MaxonChainReinsertRelativeOp(MaxonValue chain, MaxonValue target, MaxonValue node, bool after) : MaxonOp {
  public override string Mnemonic => After ? "maxon.chain_reinsert_after" : "maxon.chain_reinsert_before";
  public MaxonValue Chain { get; } = chain;
  public MaxonValue Target { get; } = target;
  public MaxonValue Node { get; } = node;
  public bool After { get; } = after;
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString(), Target.ToString(), Node.ToString()];
}

// Detaches a node from the chain without freeing it
public class MaxonChainDetachOp(MaxonValue chain, MaxonValue node) : MaxonOp {
  public override string Mnemonic => "maxon.chain_detach";
  public MaxonValue Chain { get; } = chain;
  public MaxonValue Node { get; } = node;
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString(), Node.ToString()];
}

// Removes a node from the chain: extracts value, unlinks node, frees node memory
public class MaxonChainRemoveOp(MaxonValue chain, MaxonValue node, string valueKind, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => "maxon.chain_remove";
  public MaxonValue Chain { get; } = chain;
  public MaxonValue Node { get; } = node;
  public string ValueKind { get; } = valueKind;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(MlirContext.Current.NextId(), valueKind) : resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString(), Node.ToString()];
}

// Returns the number of nodes in the chain
public class MaxonChainCountOp(MaxonValue chain) : MaxonOp {
  public override string Mnemonic => "maxon.chain_count";
  public MaxonValue Chain { get; } = chain;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString()];
}

// Loads the value stored in a chain node
public class MaxonChainNodeValueOp(MaxonValue node, string valueKind, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => "maxon.chain_node_value";
  public MaxonValue Node { get; } = node;
  public string ValueKind { get; } = valueKind;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(MlirContext.Current.NextId(), valueKind) : resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Node.ToString()];
}

// Replaces the value stored in a chain node
public class MaxonChainNodeSetValueOp(MaxonValue node, MaxonValue value, string valueKind) : MaxonOp {
  public override string Mnemonic => "maxon.chain_node_set_value";
  public MaxonValue Node { get; } = node;
  public MaxonValue Value { get; } = value;
  public string ValueKind { get; } = valueKind;
  public override IReadOnlyList<string> PrintableOperands => [Node.ToString(), Value.ToString()];
}

// Removes all nodes from the chain, freeing each node.
// ValueKind indicates the element type — used to decide whether node values need decref.
public class MaxonChainClearOp(MaxonValue chain, string valueKind) : MaxonOp {
  public override string Mnemonic => "maxon.chain_clear";
  public MaxonValue Chain { get; } = chain;
  public string ValueKind { get; } = valueKind;
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString()];
}

// Resets the chain's iteration cursor to null (0)
public class MaxonChainCursorResetOp(MaxonValue chain) : MaxonOp {
  public override string Mnemonic => "maxon.chain_cursor_reset";
  public MaxonValue Chain { get; } = chain;
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString()];
}

// Reads the value at the chain's current cursor position
public class MaxonChainCursorValueOp(MaxonValue chain, string valueKind, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => "maxon.chain_cursor_value";
  public MaxonValue Chain { get; } = chain;
  public string ValueKind { get; } = valueKind;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
    ? new MaxonStruct(MlirContext.Current.NextId(), valueKind) : resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Chain.ToString()];
}
