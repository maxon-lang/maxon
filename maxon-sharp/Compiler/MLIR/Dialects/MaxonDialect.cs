using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public enum MaxonValueKind { Integer, Float, Bool, Byte, Struct, Enum, Function }

public static class MaxonValueKindExtensions {
  public static MlirType ToMlirType(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => MlirType.I64,
    MaxonValueKind.Float => MlirType.F64,
    MaxonValueKind.Bool => MlirType.I1,
    MaxonValueKind.Byte => MlirType.I8,
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require lookup via type registry, not ToMlirType()"),
    MaxonValueKind.Enum => throw new InvalidOperationException("Enum kinds require lookup via type registry, not ToMlirType()"),
    MaxonValueKind.Function => throw new InvalidOperationException("Function kinds require lookup via function type, not ToMlirType()"),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValue CreateValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
    MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
    MaxonValueKind.Bool => new MaxonBool(MlirContext.Current.NextId()),
    MaxonValueKind.Byte => new MaxonByte(MlirContext.Current.NextId()),
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require a type name, use CreateStructValue() instead"),
    MaxonValueKind.Enum => throw new InvalidOperationException("Enum kinds require a type name, use MaxonEnumLiteralOp instead"),
    MaxonValueKind.Function => new MaxonFunctionPtr(MlirContext.Current.NextId()),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValueKind ToValueKind(this MlirType type) {
    if (type == MlirType.I64) return MaxonValueKind.Integer;
    if (type == MlirType.F64) return MaxonValueKind.Float;
    if (type == MlirType.I1) return MaxonValueKind.Bool;
    if (type == MlirType.I8) return MaxonValueKind.Byte;
    if (type is MlirEnumType) return MaxonValueKind.Enum;
    if (type is MlirStructType) return MaxonValueKind.Struct;
    if (type is MlirFunctionType) return MaxonValueKind.Function;
    throw new ArgumentOutOfRangeException(nameof(type), $"No MaxonValueKind for MlirType: {type}");
  }

  public static StdValue CreateStdValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Float => new StdF64(MlirContext.Current.NextId()),
    MaxonValueKind.Bool => new StdBool(MlirContext.Current.NextId()),
    MaxonValueKind.Byte => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Struct => new StdPtr(MlirContext.Current.NextId()),
    MaxonValueKind.Enum => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Function => new StdPtr(MlirContext.Current.NextId()),
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
      MaxonValueKind.Bool => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(BoolValue ? 1 : 0, MlirType.I1) },
      MaxonValueKind.Byte => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(IntValue, MlirType.I8) },
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
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes {
    get {
      var attrs = new Dictionary<string, MlirAttribute> {
        ["var"] = new StringAttr(VarName),
      };
      if (ValueKind != MaxonValueKind.Struct && ValueKind != MaxonValueKind.Enum && ValueKind != MaxonValueKind.Function) {
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
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> {
      ["index"] = new IntegerAttr(Index, MlirType.I32),
      ["name"] = new StringAttr(Name),
      ["type"] = new TypeAttr(ValueKind.ToMlirType())
    };
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

public class MaxonBinOp(MaxonBinOperator op, MaxonValue lhs, MaxonValue rhs, MaxonValueKind operandKind) : MaxonOp {
  public override string Mnemonic => "maxon.binop";
  public MaxonBinOperator Operator { get; } = op;
  public MaxonValue Lhs { get; } = lhs;
  public MaxonValue Rhs { get; } = rhs;
  public MaxonValueKind OperandKind { get; } = operandKind;
  public MaxonValue Result { get; } = IsComparison(op)
      ? new MaxonBool(MlirContext.Current.NextId())
      : operandKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> {
      ["op"] = new StringAttr(Operator.ToString().ToLowerInvariant()),
      ["kind"] = new TypeAttr(OperandKind.ToMlirType())
    };

  private static bool IsComparison(MaxonBinOperator op) =>
    op is MaxonBinOperator.Eq or MaxonBinOperator.Ne or MaxonBinOperator.Lt
      or MaxonBinOperator.Gt or MaxonBinOperator.Le or MaxonBinOperator.Ge;
}

public class MaxonCallOp : MaxonOp {
  public override string Mnemonic => $"maxon.call @{Callee}";
  public string Callee { get; }
  public List<MaxonValue> Args { get; }
  public MaxonValue? Result { get; }
  public MaxonValueKind? ResultKind { get; }
  // The struct type name for calls returning a struct
  public string? ResultStructTypeName { get; }

  public MaxonCallOp(string callee, List<MaxonValue> args, MaxonValueKind? resultKind = null, string? resultStructTypeName = null) {
    Callee = callee;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    if (resultKind == MaxonValueKind.Struct) {
      Result = new MaxonStruct(MlirContext.Current.NextId(), resultStructTypeName!);
    } else if (resultKind == MaxonValueKind.Enum) {
      Result = new MaxonEnum(MlirContext.Current.NextId(), resultStructTypeName!);
    } else {
      Result = resultKind?.CreateValue();
    }
  }

  // Internal constructor preserving existing result for call site rewriting
  internal MaxonCallOp(string callee, List<MaxonValue> args, MaxonValue? existingResult, MaxonValueKind? resultKind, string? resultStructTypeName) {
    Callee = callee;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    Result = existingResult;
  }

  public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
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

public class MaxonCastOp(MaxonValue input, MaxonValueKind targetKind) : MaxonOp {
  public override string Mnemonic => $"maxon.cast";
  public MaxonValue Input { get; } = input;
  public MaxonValueKind TargetKind { get; } = targetKind;
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

// Calls a throwing function and captures both the result and error flag.
// ErrorFlag is non-zero if the callee threw an error.
public class MaxonTryCallOp : MaxonOp {
  public override string Mnemonic => $"maxon.try_call @{Callee}";
  public string Callee { get; }
  public List<MaxonValue> Args { get; }
  public MaxonValue? Result { get; }
  public MaxonValueKind? ResultKind { get; }
  public string? ResultStructTypeName { get; }
  public MaxonInteger ErrorFlag { get; }

  public MaxonTryCallOp(string callee, List<MaxonValue> args, MaxonValueKind? resultKind = null, string? resultStructTypeName = null) {
    Callee = callee;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    ErrorFlag = new MaxonInteger(MlirContext.Current.NextId());
    if (resultKind == MaxonValueKind.Struct) {
      Result = new MaxonStruct(MlirContext.Current.NextId(), resultStructTypeName!);
    } else if (resultKind == MaxonValueKind.Enum) {
      Result = new MaxonEnum(MlirContext.Current.NextId(), resultStructTypeName!);
    } else {
      Result = resultKind?.CreateValue();
    }
  }

  // Internal constructor preserving existing result/errorFlag for call site rewriting
  internal MaxonTryCallOp(string callee, List<MaxonValue> args, MaxonValue? existingResult, MaxonInteger existingErrorFlag, MaxonValueKind? resultKind, string? resultStructTypeName) {
    Callee = callee;
    Args = args;
    ResultKind = resultKind;
    ResultStructTypeName = resultStructTypeName;
    Result = existingResult;
    ErrorFlag = existingErrorFlag;
  }

  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString(), ErrorFlag.ToString()] : [ErrorFlag.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
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
}

// Reads a field: p.x
public class MaxonFieldAccessOp(MaxonValue structValue, string typeName, string fieldName, MaxonValueKind resultKind, string? resultStructTypeName = null) : MaxonOp {
  public override string Mnemonic => $"maxon.field_access .{FieldName}";
  public MaxonValue StructValue { get; } = structValue;
  public string TypeName { get; } = typeName;
  public string FieldName { get; } = fieldName;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public string? ResultStructTypeName { get; } = resultStructTypeName;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Struct
      ? new MaxonStruct(MlirContext.Current.NextId(), resultStructTypeName!)
      : resultKind.CreateValue();
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

public class MaxonGlobalLoadOp(string globalName, MaxonValueKind kind) : MaxonOp {
  public override string Mnemonic => $"maxon.global_load @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public MaxonValueKind ValueKind { get; } = kind;
  public MaxonValue Result { get; } = kind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> {
      ["global"] = new StringAttr(GlobalName),
      ["type"] = new TypeAttr(ValueKind.ToMlirType())
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

// Enum var ref: loads an enum from a variable in a different block
public class MaxonEnumVarRefOp(string varName, string enumTypeName, MaxonValueKind backingKind) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_var_ref {VarName}";
  public string VarName { get; } = varName;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind BackingKind { get; } = backingKind;
  public MaxonEnum Result { get; } = new MaxonEnum(MlirContext.Current.NextId(), enumTypeName);
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Accesses .rawValue on an enum value
public class MaxonEnumRawValueOp(MaxonValue enumValue, string enumTypeName, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => $"maxon.enum_rawvalue @{EnumTypeName}";
  public MaxonValue EnumValue { get; } = enumValue;
  public string EnumTypeName { get; } = enumTypeName;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind == MaxonValueKind.Float
    ? new MaxonFloat(MlirContext.Current.NextId())
    : new MaxonInteger(MlirContext.Current.NextId());
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
      ["type"] = new TypeAttr(ValueKind.ToMlirType())
    };
}

// ============================================================================
// Managed memory operations (for __ManagedMemory builtin intrinsics)
// ============================================================================

// Get element at index from managed buffer: __managed_memory_get_unchecked(managed, index)
public class MaxonManagedMemGetOp(MaxonValue managedStruct, MaxonValue index, int elementSize, MaxonValueKind resultKind) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_get";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public int ElementSize { get; } = elementSize;
  public MaxonValueKind ResultKind { get; } = resultKind;
  public MaxonValue Result { get; } = resultKind.CreateValue();
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
}

// Set element at index in managed buffer: __managed_memory_set_at(managed, index, value)
public class MaxonManagedMemSetOp(MaxonValue managedStruct, MaxonValue index, MaxonValue value, int elementSize, MaxonValueKind elementKind = MaxonValueKind.Integer) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_set";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Value { get; } = value;
  public int ElementSize { get; } = elementSize;
  public MaxonValueKind ElementKind { get; } = elementKind;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Value.ToString()];
}

// Create a new heap-allocated managed memory: __managed_memory_create(count, elemSize)
public class MaxonManagedMemCreateOp(MaxonValue count, int elementSize) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_create";
  public MaxonValue Count { get; } = count;
  public int ElementSize { get; } = elementSize;
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Count.ToString()];
}

// Grow managed memory to new capacity: __managed_memory_grow(managed, newCap)
public class MaxonManagedMemGrowOp(MaxonValue managedStruct, MaxonValue newCapacity, int elementSize) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_grow";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue NewCapacity { get; } = newCapacity;
  public int ElementSize { get; } = elementSize;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), NewCapacity.ToString()];
}

// Shift elements right/left in managed buffer
public class MaxonManagedMemShiftOp(MaxonValue managedStruct, MaxonValue index, MaxonValue count, int elementSize, bool shiftRight) : MaxonOp {
  public override string Mnemonic => ShiftRight ? "maxon.managed_mem_shift_right" : "maxon.managed_mem_shift_left";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonValue Count { get; } = count;
  public int ElementSize { get; } = elementSize;
  public bool ShiftRight { get; } = shiftRight;
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString(), Count.ToString()];
}

// Get byte at index in managed buffer: __managed_memory_byte_at(managed, index) -> byte
public class MaxonManagedMemByteGetOp(MaxonValue managedStruct, MaxonValue index) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_byte_get";
  public MaxonValue ManagedStruct { get; } = managedStruct;
  public MaxonValue Index { get; } = index;
  public MaxonByte Result { get; } = new MaxonByte(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [ManagedStruct.ToString(), Index.ToString()];
}

// Set byte at index in managed buffer: __managed_memory_set_byte(managed, index, value)
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
  public MaxonStruct Result { get; } = new MaxonStruct(MlirContext.Current.NextId(), "__ManagedMemory");
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
}

// Create a slice of a __ManagedMemory buffer (start/end byte positions)
public class MaxonManagedMemSliceOp(MaxonValue managed, MaxonValue start, MaxonValue end) : MaxonOp {
  public override string Mnemonic => "maxon.managed_mem_slice";
  public MaxonValue Managed { get; } = managed;
  public MaxonValue Start { get; } = start;
  public MaxonValue End { get; } = end;
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

// Write a C string to stdout, returns number of bytes written
public class MaxonCStringWriteStdoutOp(MaxonValue cstrPtr) : MaxonOp {
  public override string Mnemonic => "maxon.cstring_write_stdout";
  public MaxonValue CstrPtr { get; } = cstrPtr;
  public MaxonInteger Result { get; } = new MaxonInteger(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableOperands => [CstrPtr.ToString()];
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
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
