using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.Arith;
using MaxonSharp.Compiler.Mlir.Dialects.Builtin;
using MaxonSharp.Compiler.Mlir.Dialects.Cf;
using MaxonSharp.Compiler.Mlir.Dialects.Maxon;
using MaxonSharp.Compiler.Mlir.Dialects.MemRef;

using FuncDialectOps = MaxonSharp.Compiler.Mlir.Dialects.Func;
using MaxonDialectOps = MaxonSharp.Compiler.Mlir.Dialects.Maxon;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Converts AST nodes to Maxon dialect operations.
/// This is the entry point for the new IR pipeline.
/// </summary>
public sealed class AstToMaxonConverter {
	private readonly MlirContext _context;
	private readonly MlirModule _module;
	private readonly MutationAnalyzer _mutationAnalyzer;
	private MlirBlock? _currentBlock;
	private MlirFunction? _currentFunction;
	private int _valueId;
	private readonly Dictionary<string, MlirValue> _namedValues = new();
	private readonly Dictionary<string, MlirType> _structTypes = new();
	private readonly Dictionary<string, MlirType> _enumTypes = new();
	private readonly Dictionary<string, MlirGlobal> _globals = new();
	// Track variable mutability for ownership checking
	private readonly Dictionary<string, bool> _variableMutability = new();

	public AstToMaxonConverter(MlirContext context, MutationAnalyzer mutationAnalyzer) {
		_context = context;
		_mutationAnalyzer = mutationAnalyzer;
		_module = new MlirModule();
	}

	/// <summary>
	/// Gets the generated module.
	/// </summary>
	public MlirModule Module => _module;

	/// <summary>
	/// Converts a program AST to the Maxon dialect.
	/// </summary>
	public MlirModule ConvertProgram(ProgramAst program) {
		Logger.Debug(LogCategory.Mlir, $"AST->MLIR: Converting program with {program.Types.Count} types, {program.Functions.Count} functions");

		// First pass: collect struct and enum definitions
		foreach (var type in program.Types) {
			Logger.Trace(LogCategory.Mlir, $"  Lowering type: {type.Name}");
			LowerTypeDecl(type);
		}

		foreach (var enumDecl in program.Enums) {
			Logger.Trace(LogCategory.Mlir, $"  Lowering enum: {enumDecl.Name}");
			LowerEnumDecl(enumDecl);
		}

		// Second pass: lower global variables and constants
		foreach (var globalVar in program.GlobalVariables) {
			Logger.Trace(LogCategory.Mlir, $"  Lowering global var: {globalVar.Name}");
			LowerGlobalVariable(globalVar);
		}

		foreach (var globalConst in program.GlobalConstants) {
			Logger.Trace(LogCategory.Mlir, $"  Lowering global const: {globalConst.Name}");
			LowerGlobalConstant(globalConst);
		}

		// Third pass: lower functions
		foreach (var func in program.Functions) {
			Logger.Debug(LogCategory.Mlir, $"  Lowering function: {func.Name}");
			LowerFunction(func);
		}

		// Lower methods from types
		foreach (var type in program.Types) {
			foreach (var method in type.Methods) {
				Logger.Debug(LogCategory.Mlir, $"  Lowering method: {type.Name}.{method.Name}");
				LowerMethod(type.Name, method);
			}
		}

		Logger.Debug(LogCategory.Mlir, $"AST->MLIR: Completed with {_module.Functions.Count} functions, {_module.Globals.Count} globals");
		return _module;
	}

	private void LowerGlobalVariable(GlobalVariable globalVar) {
		// Evaluate the initial value to determine type
		var initValue = EvaluateConstantExpr(globalVar.Value);
		var type = initValue.type;

		var global = new MlirGlobal($"global.{globalVar.Name}", type) {
			IsConstant = false,
			InitValue = initValue.attr
		};

		_module.Globals.Add(global);
		_globals[globalVar.Name] = global;
	}

	private void LowerGlobalConstant(GlobalConstant globalConst) {
		// Evaluate the initial value to determine type
		var initValue = EvaluateConstantExpr(globalConst.Value);
		var type = initValue.type;

		var global = new MlirGlobal($"global.{globalConst.Name}", type) {
			IsConstant = true,
			InitValue = initValue.attr
		};

		_module.Globals.Add(global);
		_globals[globalConst.Name] = global;
	}

	private (MlirType type, MlirAttribute? attr) EvaluateConstantExpr(Expr expr) {
		return expr switch {
			IntLiteralExpr intLit => (IntegerType.I64, new IntegerAttr(intLit.Value)),
			FloatLiteralExpr floatLit => (FloatType.F64, new FloatAttr(floatLit.Value)),
			BoolLiteralExpr boolLit => (IntegerType.I1, new IntegerAttr(boolLit.Value ? 1 : 0)),
			_ => throw new NotSupportedException($"Global initializer must be a constant, got: {expr.GetType().Name}")
		};
	}

	private void LowerTypeDecl(TypeDecl type) {
		var fields = new List<(string name, MlirType type, bool isMutable)>();
		foreach (var field in type.Fields) {
			var fieldType = ConvertTypeRef(field.Type);
			fields.Add((field.Name, fieldType, field.IsMutable));
		}
		RegisterStruct(type.Name, fields);
	}

	private void LowerEnumDecl(EnumDecl enumDecl) {
		var variants = new List<MaxonVariantInfo>();
		var tag = 0;

		foreach (var member in enumDecl.Members) {
			var payloadFields = new List<MaxonFieldInfo>();
			var offset = 0;

			foreach (var assoc in member.AssociatedValues) {
				var fieldType = ConvertTypeRef(assoc.Type);
				// Enum associated values are immutable by default
				payloadFields.Add(new MaxonFieldInfo(assoc.Name, fieldType, offset, IsMutable: false));
				offset += fieldType.SizeInBytes;
			}

			variants.Add(new MaxonVariantInfo(member.Name, tag++, payloadFields));
		}

		var enumType = new MaxonEnumType(enumDecl.Name, variants);
		_enumTypes[enumDecl.Name] = enumType;

		// Add to module
		var def = new MlirEnumDef(enumDecl.Name);
		foreach (var variant in variants) {
			var vdef = new MlirVariantDef(variant.Name, variant.Tag);
			foreach (var field in variant.PayloadFields) {
				vdef.PayloadFields.Add(new MlirFieldDef(field.Name, field.Type, field.Offset, field.IsMutable));
			}
			def.Variants.Add(vdef);
		}
		_module.EnumDefs.Add(def);
	}

	private void LowerFunction(FunctionDecl func) {
		var parameters = new List<(string name, string type)>();
		foreach (var param in func.Params) {
			parameters.Add((param.Name, GetTypeString(param.Type)));
		}

		var returnType = GetTypeString(func.ReturnType);
		BeginFunction(func.Name, parameters, returnType);

		// Lower function body
		foreach (var stmt in func.Body) {
			LowerStatement(stmt);
		}

		// If the block isn't terminated, add a return
		if (_currentBlock is not null && !_currentBlock.IsTerminated) {
			if (func.ReturnType is null || GetTypeString(func.ReturnType) == "void") {
				EmitReturn();
			}
		}

		EndFunction();
	}

	private void LowerMethod(string typeName, MethodDecl method) {
		var parameters = new List<(string name, string type)>();

		// Add 'self' parameter for instance methods
		if (!method.IsStatic) {
			parameters.Add(("self", typeName));
		}

		foreach (var param in method.Params) {
			parameters.Add((param.Name, GetTypeString(param.Type)));
		}

		var returnType = GetTypeString(method.ReturnType);
		var funcName = $"{typeName}.{method.Name}";
		BeginFunction(funcName, parameters, returnType);

		// Lower method body
		foreach (var stmt in method.Body) {
			LowerStatement(stmt);
		}

		// If the block isn't terminated, add a return
		if (_currentBlock is not null && !_currentBlock.IsTerminated) {
			if (method.ReturnType is null || GetTypeString(method.ReturnType) == "void") {
				EmitReturn();
			}
		}

		EndFunction();
	}

	private string GetTypeString(TypeRef? typeRef) {
		return typeRef switch {
			null => "void",
			SimpleTypeRef simple => simple.Name,
			GenericTypeRef generic => generic.BaseName,
			_ => throw new NotSupportedException($"Unsupported type ref: {typeRef}")
		};
	}

	private MlirType ConvertTypeRef(TypeRef typeRef) {
		return typeRef switch {
			SimpleTypeRef simple => ConvertType(simple.Name),
			GenericTypeRef generic => ConvertType(generic.BaseName),
			_ => throw new NotSupportedException($"Unsupported type ref: {typeRef}")
		};
	}

	private void LowerStatement(Stmt stmt) {
		switch (stmt) {
			case ReturnStmt ret:
				if (ret.Value is not null) {
					var value = LowerExpression(ret.Value);
					EmitReturn(value);
				} else {
					EmitReturn();
				}
				break;

			case LetDeclStmt let:
				var letValue = LowerExpression(let.Value);
				var letType = letValue.Type;
				var letSlot = DeclareVariable(let.Name, letType);
				EmitStore(letValue, letSlot);
				break;

			case VarDeclStmt varDecl:
				var varValue = LowerExpression(varDecl.Value);
				var varType = varValue.Type;
				var varSlot = DeclareVariable(varDecl.Name, varType);
				EmitStore(varValue, varSlot);
				break;

			case AssignStmt assign:
				var assignValue = LowerExpression(assign.Value);
				SetVariable(assign.Target, assignValue);
				break;

			case ExprStmt exprStmt:
				LowerExpression(exprStmt.Expression);
				break;

			case IfStmt ifStmt:
				LowerIfStatement(ifStmt);
				break;

			case WhileStmt whileStmt:
				LowerWhileStatement(whileStmt);
				break;

			case ForStmt forStmt:
				LowerForStatement(forStmt);
				break;

			case BreakStmt:
				// TODO: Handle labeled breaks and loop break targets
				break;

			case ContinueStmt:
				// TODO: Handle labeled continues and loop continue targets
				break;

			default:
				throw new NotSupportedException($"Unsupported statement: {stmt.GetType().Name}");
		}
	}

	private void LowerIfStatement(IfStmt ifStmt) {
		var condition = LowerExpression(ifStmt.Condition);

		var thenBlock = CreateBlock("then");
		var elseBlock = ifStmt.ElseBody is not null ? CreateBlock("else") : null;
		var mergeBlock = CreateBlock("merge");

		EmitCondBranch(condition, thenBlock, elseBlock ?? mergeBlock);

		// Then block
		SetInsertionPoint(thenBlock);
		foreach (var stmt in ifStmt.ThenBody) {
			LowerStatement(stmt);
		}
		if (!thenBlock.IsTerminated) {
			EmitBranch(mergeBlock);
		}

		// Else block
		if (ifStmt.ElseBody is not null && elseBlock is not null) {
			SetInsertionPoint(elseBlock);
			foreach (var stmt in ifStmt.ElseBody) {
				LowerStatement(stmt);
			}
			if (!elseBlock.IsTerminated) {
				EmitBranch(mergeBlock);
			}
		}

		SetInsertionPoint(mergeBlock);
	}

	private void LowerWhileStatement(WhileStmt whileStmt) {
		var condBlock = CreateBlock("while.cond");
		var bodyBlock = CreateBlock("while.body");
		var exitBlock = CreateBlock("while.exit");

		EmitBranch(condBlock);

		// Condition block
		SetInsertionPoint(condBlock);
		var condition = LowerExpression(whileStmt.Condition);
		EmitCondBranch(condition, bodyBlock, exitBlock);

		// Body block
		SetInsertionPoint(bodyBlock);
		foreach (var stmt in whileStmt.Body) {
			LowerStatement(stmt);
		}
		if (!bodyBlock.IsTerminated) {
			EmitBranch(condBlock);
		}

		SetInsertionPoint(exitBlock);
	}

	private void LowerForStatement(ForStmt forStmt) {
		// For now, just create the structure
		// TODO: Lower for-range loops properly
		var bodyBlock = CreateBlock("for.body");
		var exitBlock = CreateBlock("for.exit");

		EmitBranch(exitBlock); // Skip for now

		SetInsertionPoint(exitBlock);
	}

	private MlirValue LowerExpression(Expr expr) {
		return expr switch {
			IntLiteralExpr intLit => EmitConstantInt(intLit.Value),
			FloatLiteralExpr floatLit => EmitConstantFloat(floatLit.Value),
			BoolLiteralExpr boolLit => EmitConstantBool(boolLit.Value),
			IdentifierExpr ident => GetVariable(ident.Name),
			BinaryExpr binary => LowerBinaryExpr(binary),
			CompareExpr compare => LowerCompareExpr(compare),
			UnaryExpr unary => LowerUnaryExpr(unary),
			CallExpr call => LowerCallExpr(call) ?? throw new InvalidOperationException("Call must return a value"),
			_ => throw new NotSupportedException($"Unsupported expression: {expr.GetType().Name}")
		};
	}

	private MlirValue LowerBinaryExpr(BinaryExpr binary) {
		var left = LowerExpression(binary.Left);
		var right = LowerExpression(binary.Right);

		// Check if floating point
		if (left.Type is FloatType) {
			return binary.Op switch {
				BinaryOp.Add => EmitAddF(left, right),
				BinaryOp.Sub => EmitSubF(left, right),
				BinaryOp.Mul => EmitMulF(left, right),
				BinaryOp.Div => EmitDivF(left, right),
				_ => throw new NotSupportedException($"Unsupported binary op for float: {binary.Op}")
			};
		}

		return binary.Op switch {
			BinaryOp.Add => EmitAddI(left, right),
			BinaryOp.Sub => EmitSubI(left, right),
			BinaryOp.Mul => EmitMulI(left, right),
			BinaryOp.Div => EmitDivSI(left, right),
			BinaryOp.Mod => EmitRemSI(left, right),
			BinaryOp.Band => EmitAndI(left, right),
			BinaryOp.Bor => EmitOrI(left, right),
			BinaryOp.Bxor => EmitXorI(left, right),
			BinaryOp.Shl => EmitShlI(left, right),
			BinaryOp.Shr => EmitShrSI(left, right),
			_ => throw new NotSupportedException($"Unsupported binary op: {binary.Op}")
		};
	}

	private MlirValue LowerCompareExpr(CompareExpr compare) {
		var left = LowerExpression(compare.Left);
		var right = LowerExpression(compare.Right);

		var predicate = compare.Op switch {
			CompareOp.Eq => CmpIPredicate.Eq,
			CompareOp.Ne => CmpIPredicate.Ne,
			CompareOp.Lt => CmpIPredicate.Slt,
			CompareOp.Le => CmpIPredicate.Sle,
			CompareOp.Gt => CmpIPredicate.Sgt,
			CompareOp.Ge => CmpIPredicate.Sge,
			_ => throw new NotSupportedException($"Unsupported compare op: {compare.Op}")
		};

		return EmitCmpI(predicate, left, right);
	}

	private MlirValue LowerUnaryExpr(UnaryExpr unary) {
		var operand = LowerExpression(unary.Operand);

		return unary.Op switch {
			UnaryOp.Negate when operand.Type is FloatType => EmitNegF(operand),
			UnaryOp.Negate => EmitNegI(operand),
			UnaryOp.Not => EmitNotI(operand),
			_ => throw new NotSupportedException($"Unsupported unary op: {unary.Op}")
		};
	}

	private MlirValue? LowerCallExpr(CallExpr call) {
		// Handle builtin functions first
		var builtinResult = TryLowerBuiltinCall(call);
		if (builtinResult != null) {
			return builtinResult;
		}

		var args = new List<MlirValue>();
		var ownership = new List<ArgumentOwnership>();

		for (int i = 0; i < call.Args.Count; i++) {
			var arg = call.Args[i];
			args.Add(LowerExpression(arg));

			// Determine ownership based on mutation analysis
			if (_mutationAnalyzer.IsMutated(call.FuncName, i)) {
				ownership.Add(ArgumentOwnership.Move);
			} else {
				ownership.Add(ArgumentOwnership.Borrow);
			}
		}

		// Try to infer return type - for now assume i64
		MlirType? returnType = IntegerType.I64;

		return EmitMaxonCall(call.FuncName, args, ownership, returnType);
	}

	/// <summary>
	/// Attempts to lower a builtin function call. Returns null if not a builtin.
	/// </summary>
	private MlirValue? TryLowerBuiltinCall(CallExpr call) {
		switch (call.FuncName) {
			case "trunc":
				// trunc(float) -> int: truncate float to integer
				if (call.Args.Count != 1) {
					throw new ArgumentException("trunc() requires exactly 1 argument");
				}
				var floatArg = LowerExpression(call.Args[0]);
				return EmitFPToSI(floatArg, IntegerType.I64);

			default:
				return null;
		}
	}

	// ========================================================================
	// Type Conversion
	// ========================================================================

	/// <summary>
	/// Converts an AST type to an MLIR type.
	/// </summary>
	public MlirType ConvertType(string typeName) {
		return typeName switch {
			"int" or "Int" or "i64" => IntegerType.I64,
			"int32" or "Int32" or "i32" => IntegerType.I32,
			"int16" or "Int16" or "i16" => IntegerType.I16,
			"int8" or "Int8" or "i8" => IntegerType.I8,
			"uint" or "UInt" or "u64" => IntegerType.UI64,
			"uint32" or "UInt32" or "u32" => IntegerType.UI32,
			"uint16" or "UInt16" or "u16" => IntegerType.UI16,
			"uint8" or "UInt8" or "u8" => IntegerType.UI8,
			"bool" or "Bool" => IntegerType.I1,
			"float" or "Float" or "f64" => FloatType.F64,
			"float32" or "Float32" or "f32" => FloatType.F32,
			"char" or "Char" => IntegerType.I32,
			"void" or "Void" => NoneType.Instance,
			"string" or "String" => new PtrType(IntegerType.I8),
			_ when _structTypes.TryGetValue(typeName, out var st) => st,
			_ when _enumTypes.TryGetValue(typeName, out var et) => et,
			_ => throw new NotSupportedException($"Unknown type: {typeName}")
		};
	}

	// ========================================================================
	// Function Conversion
	// ========================================================================

	/// <summary>
	/// Starts conversion of a function.
	/// </summary>
	public MlirFunction BeginFunction(string name, List<(string name, string type)> parameters, string returnType) {
		var func = new MlirFunction(name);

		foreach (var (pname, ptype) in parameters) {
			func.AddParam(pname, ConvertType(ptype));
		}

		if (returnType != "void") {
			func.ResultTypes.Add(ConvertType(returnType));
		}

		_currentFunction = func;
		_namedValues.Clear();

		// Create entry block with parameters
		var entry = func.CreateEntryBlock();
		_currentBlock = entry;

		// Map parameter names to values
		var paramValues = func.GetParamValues();
		for (int i = 0; i < parameters.Count; i++) {
			_namedValues[parameters[i].name] = paramValues[i];
		}

		_module.Functions.Add(func);
		return func;
	}

	/// <summary>
	/// Ends the current function.
	/// </summary>
	public void EndFunction() {
		_currentFunction = null;
		_currentBlock = null;
	}

	// ========================================================================
	// Block Management
	// ========================================================================

	/// <summary>
	/// Creates a new block in the current function.
	/// </summary>
	public MlirBlock CreateBlock(string name) {
		if (_currentFunction is null)
			throw new InvalidOperationException("No current function");

		var block = _currentFunction.Body.CreateBlock(name);
		return block;
	}

	/// <summary>
	/// Sets the current block for operation insertion.
	/// </summary>
	public void SetInsertionPoint(MlirBlock block) {
		_currentBlock = block;
	}

	// ========================================================================
	// Expression Conversion
	// ========================================================================

	/// <summary>
	/// Generates a constant integer.
	/// </summary>
	public MlirValue EmitConstantInt(long value, MlirType? type = null) {
		type ??= IntegerType.I64;
		var op = ConstantOp.Int(value, type as IntegerType ?? IntegerType.I64);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Generates a constant float.
	/// </summary>
	public MlirValue EmitConstantFloat(double value, MlirType? type = null) {
		type ??= FloatType.F64;
		var op = ConstantOp.Float(value, type as FloatType ?? FloatType.F64);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Generates a constant bool.
	/// </summary>
	public MlirValue EmitConstantBool(bool value) {
		var op = ConstantOp.Bool(value);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits an integer addition.
	/// </summary>
	public MlirValue EmitAddI(MlirValue lhs, MlirValue rhs) {
		var op = new AddIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits an integer subtraction.
	/// </summary>
	public MlirValue EmitSubI(MlirValue lhs, MlirValue rhs) {
		var op = new SubIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits an integer multiplication.
	/// </summary>
	public MlirValue EmitMulI(MlirValue lhs, MlirValue rhs) {
		var op = new MulIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a signed integer division.
	/// </summary>
	public MlirValue EmitDivSI(MlirValue lhs, MlirValue rhs) {
		var op = new DivSIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits an integer comparison.
	/// </summary>
	public MlirValue EmitCmpI(CmpIPredicate predicate, MlirValue lhs, MlirValue rhs) {
		var op = new CmpIOp(predicate, lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a floating point addition.
	/// </summary>
	public MlirValue EmitAddF(MlirValue lhs, MlirValue rhs) {
		var op = new AddFOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a floating point subtraction.
	/// </summary>
	public MlirValue EmitSubF(MlirValue lhs, MlirValue rhs) {
		var op = new SubFOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a floating point multiplication.
	/// </summary>
	public MlirValue EmitMulF(MlirValue lhs, MlirValue rhs) {
		var op = new MulFOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a floating point division.
	/// </summary>
	public MlirValue EmitDivF(MlirValue lhs, MlirValue rhs) {
		var op = new DivFOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a signed integer remainder.
	/// </summary>
	public MlirValue EmitRemSI(MlirValue lhs, MlirValue rhs) {
		var op = new RemSIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a bitwise AND.
	/// </summary>
	public MlirValue EmitAndI(MlirValue lhs, MlirValue rhs) {
		var op = new AndIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a bitwise OR.
	/// </summary>
	public MlirValue EmitOrI(MlirValue lhs, MlirValue rhs) {
		var op = new OrIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a bitwise XOR.
	/// </summary>
	public MlirValue EmitXorI(MlirValue lhs, MlirValue rhs) {
		var op = new XOrIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a left shift.
	/// </summary>
	public MlirValue EmitShlI(MlirValue lhs, MlirValue rhs) {
		var op = new ShLIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a signed right shift.
	/// </summary>
	public MlirValue EmitShrSI(MlirValue lhs, MlirValue rhs) {
		var op = new ShRSIOp(lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits integer negation (0 - value).
	/// </summary>
	public MlirValue EmitNegI(MlirValue operand) {
		var zero = EmitConstantInt(0, operand.Type);
		return EmitSubI(zero, operand);
	}

	/// <summary>
	/// Emits floating point negation.
	/// </summary>
	public MlirValue EmitNegF(MlirValue operand) {
		var op = new NegFOp(operand);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits bitwise NOT (xor with -1).
	/// </summary>
	public MlirValue EmitNotI(MlirValue operand) {
		var allOnes = EmitConstantInt(-1, operand.Type);
		return EmitXorI(operand, allOnes);
	}

	/// <summary>
	/// Emits a float-to-signed-integer conversion (truncation).
	/// </summary>
	public MlirValue EmitFPToSI(MlirValue operand, IntegerType targetType) {
		var op = new FPToSIOp(operand, targetType);
		InsertOp(op);
		return op.Result;
	}

	// ========================================================================
	// Memory Operations
	// ========================================================================

	/// <summary>
	/// Emits a stack allocation.
	/// </summary>
	public MlirValue EmitAlloca(MlirType elementType, string? variableName = null) {
		var memrefType = new MemRefType(elementType);
		var op = new AllocaOp(memrefType);
		InsertOp(op);
		if (variableName is not null) {
			op.Result.Name = variableName;
		}
		return op.Result;
	}

	/// <summary>
	/// Emits a heap allocation.
	/// </summary>
	public MlirValue EmitAlloc(MlirType elementType) {
		var memrefType = new MemRefType(elementType);
		var op = new AllocOp(memrefType);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a load from memory.
	/// </summary>
	public MlirValue EmitLoad(MlirValue memref) {
		if (memref.Type is not MemRefType)
			throw new ArgumentException("Expected memref type", nameof(memref));
		var op = new LoadOp(memref);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a store to memory.
	/// </summary>
	public void EmitStore(MlirValue value, MlirValue memref) {
		var op = new StoreOp(value, memref);
		InsertOp(op);
	}

	/// <summary>
	/// Emits a deallocation.
	/// </summary>
	public void EmitDealloc(MlirValue memref) {
		var op = new DeallocOp(memref);
		InsertOp(op);
	}

	// ========================================================================
	// Ownership Operations
	// ========================================================================

	/// <summary>
	/// Emits a move operation (transfer ownership).
	/// </summary>
	public MlirValue EmitMove(MlirValue source) {
		var op = new MoveOp(source);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a borrow operation.
	/// </summary>
	public MlirValue EmitBorrow(MlirValue source, bool mutable = false) {
		var op = new BorrowOp(source, mutable);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a drop operation (release ownership).
	/// </summary>
	public void EmitDrop(MlirValue value) {
		var op = new DropOp(value);
		InsertOp(op);
	}

	// ========================================================================
	// Struct Operations
	// ========================================================================

	/// <summary>
	/// Registers a struct type.
	/// </summary>
	public MaxonStructType RegisterStruct(string name, List<(string name, MlirType type, bool isMutable)> fields) {
		// Convert tuples to MaxonFieldInfo with calculated offsets
		var fieldInfos = new List<MaxonFieldInfo>();
		int offset = 0;
		foreach (var (fname, ftype, isMutable) in fields) {
			fieldInfos.Add(new MaxonFieldInfo(fname, ftype, offset, isMutable));
			offset += ftype.SizeInBytes;
		}

		var structType = new MaxonStructType(name, fieldInfos);
		_structTypes[name] = structType;

		// Add to module
		var def = new MlirStructDef(name);
		offset = 0;
		foreach (var (fname, ftype, isMutable) in fields) {
			def.Fields.Add(new MlirFieldDef(fname, ftype, offset, isMutable));
			offset += ftype.SizeInBytes;
		}
		_module.StructDefs.Add(def);

		return structType;
	}

	/// <summary>
	/// Emits a struct initialization.
	/// </summary>
	public MlirValue EmitStructInit(MaxonStructType structType, Dictionary<string, MlirValue> fieldValues) {
		var op = new StructInitOp(structType, fieldValues);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a field get operation.
	/// </summary>
	public MlirValue EmitGetField(MlirValue structValue, string fieldName, MlirType fieldType) {
		var op = new FieldGetOp(structValue, fieldName, fieldType);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a field set operation.
	/// </summary>
	public void EmitSetField(MlirValue structValue, string fieldName, MlirValue value) {
		var op = new FieldSetOp(structValue, fieldName, value);
		InsertOp(op);
	}

	// ========================================================================
	// Control Flow
	// ========================================================================

	/// <summary>
	/// Emits an unconditional branch.
	/// </summary>
	public void EmitBranch(MlirBlock target, params MlirValue[] args) {
		var op = new BranchOp(target, [.. args]);
		InsertOp(op);
	}

	/// <summary>
	/// Emits a conditional branch.
	/// </summary>
	public void EmitCondBranch(MlirValue condition, MlirBlock trueBlock, MlirBlock falseBlock,
							   List<MlirValue>? trueArgs = null, List<MlirValue>? falseArgs = null) {
		var op = new CondBranchOp(condition, trueBlock, falseBlock, trueArgs ?? [], falseArgs ?? []);
		InsertOp(op);
	}

	/// <summary>
	/// Emits a return.
	/// </summary>
	public void EmitReturn(params MlirValue[] values) {
		var op = new FuncDialectOps.ReturnOp([.. values]);
		InsertOp(op);
	}

	// ========================================================================
	// Function Calls
	// ========================================================================

	/// <summary>
	/// Emits a function call.
	/// </summary>
	public MlirValue? EmitCall(string callee, List<MlirValue> args, MlirType? returnType = null) {
		var op = new FuncDialectOps.CallOp(callee, args, returnType);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a function call with ownership annotations.
	/// </summary>
	public MlirValue? EmitMaxonCall(string callee, List<MlirValue> args, List<ArgumentOwnership> ownership, MlirType? returnType = null) {
		var op = new MaxonDialectOps.CallOp(callee, args, ownership, returnType);
		InsertOp(op);
		return op.Result;
	}

	// ========================================================================
	// Variables
	// ========================================================================

	/// <summary>
	/// Declares a local variable (allocates stack space).
	/// </summary>
	public MlirValue DeclareVariable(string name, MlirType type) {
		var alloca = EmitAlloca(type, name);
		_namedValues[name] = alloca;
		return alloca;
	}

	/// <summary>
	/// Gets a variable's address.
	/// </summary>
	public MlirValue GetVariableAddress(string name) {
		// Check local variables first
		if (_namedValues.TryGetValue(name, out var value))
			return value;

		// Check global variables
		if (_globals.TryGetValue(name, out var global)) {
			return EmitGetGlobal(global);
		}

		throw new InvalidOperationException($"Undefined variable: {name}");
	}

	/// <summary>
	/// Emits a get_global operation to get the address of a global variable.
	/// </summary>
	public MlirValue EmitGetGlobal(MlirGlobal global) {
		var memrefType = new MemRefType(global.Type);
		var op = new GetGlobalOp(global.Name, memrefType);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Gets a variable's value (loads it).
	/// </summary>
	public MlirValue GetVariable(string name) {
		var addr = GetVariableAddress(name);
		if (addr.Type is MemRefType)
			return EmitLoad(addr);
		return addr; // Already a value (e.g., function parameter)
	}

	/// <summary>
	/// Sets a variable's value (stores it).
	/// </summary>
	public void SetVariable(string name, MlirValue value) {
		var addr = GetVariableAddress(name);
		EmitStore(value, addr);
	}

	// ========================================================================
	// Helpers
	// ========================================================================

	private void InsertOp(MlirOperation op) {
		if (_currentBlock is null)
			throw new InvalidOperationException("No current block for operation insertion");
		_currentBlock.AddOp(op);
	}

	private int NextValueId() => _valueId++;
}
