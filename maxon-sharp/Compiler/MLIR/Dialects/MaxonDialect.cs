using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public enum MaxonValueKind { Integer, Float, Bool, Byte, Struct }

public static class MaxonValueKindExtensions {
  public static MlirType ToMlirType(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => MlirType.I64,
    MaxonValueKind.Float => MlirType.F64,
    MaxonValueKind.Bool => MlirType.I1,
    MaxonValueKind.Byte => MlirType.I8,
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require lookup via type registry, not ToMlirType()"),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValue CreateValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
    MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
    MaxonValueKind.Bool => new MaxonBool(MlirContext.Current.NextId()),
    MaxonValueKind.Byte => new MaxonByte(MlirContext.Current.NextId()),
    MaxonValueKind.Struct => throw new InvalidOperationException("Struct kinds require a type name, use CreateStructValue() instead"),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  public static MaxonValueKind ToValueKind(this MlirType type) {
    if (type == MlirType.I64) return MaxonValueKind.Integer;
    if (type == MlirType.F64) return MaxonValueKind.Float;
    if (type == MlirType.I1) return MaxonValueKind.Bool;
    if (type == MlirType.I8) return MaxonValueKind.Byte;
    if (type is MlirStructType) return MaxonValueKind.Struct;
    throw new ArgumentOutOfRangeException(nameof(type), $"No MaxonValueKind for MlirType: {type}");
  }

  public static StdValue CreateStdValue(this MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Float => new StdF64(MlirContext.Current.NextId()),
    MaxonValueKind.Bool => new StdBool(MlirContext.Current.NextId()),
    MaxonValueKind.Byte => new StdI64(MlirContext.Current.NextId()),
    MaxonValueKind.Struct => new StdPtr(MlirContext.Current.NextId()),
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
      _ => throw new ArgumentOutOfRangeException(nameof(ValueKind), ValueKind, null),
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
      if (ValueKind != MaxonValueKind.Struct) {
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
    } else {
      Result = resultKind?.CreateValue();
    }
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

public class MaxonReturnOp(MaxonValue? value = null) : MaxonOp {
  public override string Mnemonic => "maxon.return";
  public MaxonValue? Value { get; } = value;
  public override IReadOnlyList<string> PrintableOperands =>
    Value != null ? [Value.ToString()] : [];
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
