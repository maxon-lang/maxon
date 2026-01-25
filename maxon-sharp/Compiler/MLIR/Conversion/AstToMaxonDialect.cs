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
public sealed class AstToMaxonConverter(MutationAnalyzer mutationAnalyzer) {
	private readonly MlirModule _module = new();
	private readonly MutationAnalyzer _mutationAnalyzer = mutationAnalyzer;
	private MlirBlock? _currentBlock;
	private MlirFunction? _currentFunction;
	private readonly Dictionary<string, MlirValue> _namedValues = [];
	private readonly Dictionary<string, MlirType> _structTypes = [];
	private readonly Dictionary<string, MlirType> _enumTypes = [];
	private readonly Dictionary<string, MlirGlobal> _globals = [];
	private readonly Dictionary<string, MlirType> _functionReturnTypes = [];
	private readonly Stack<(MlirBlock continueBlock, MlirBlock exitBlock)> _loopContextStack = new();

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

		// Collect function return types before lowering (for call site type lookup)
		foreach (var func in program.Functions) {
			var returnType = func.ReturnType != null ? ConvertTypeRef(func.ReturnType) : NoneType.Instance;
			_functionReturnTypes[func.Name] = returnType;
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

	private void LowerGlobalVariable(GlobalVariable globalVar) =>
		LowerGlobal(globalVar.Name, globalVar.Value, isConstant: false);

	private void LowerGlobalConstant(GlobalConstant globalConst) =>
		LowerGlobal(globalConst.Name, globalConst.Value, isConstant: true);

	private void LowerGlobal(string name, Expr value, bool isConstant) {
		var (type, attr) = EvaluateConstantExpr(value);
		var global = new MlirGlobal($"global.{name}", type) {
			IsConstant = isConstant,
			InitValue = attr
		};
		_module.Globals.Add(global);
		_globals[name] = global;
	}

	private static (MlirType type, MlirAttribute? attr) EvaluateConstantExpr(Expr expr) {
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
		var parameters = func.Params.Select(p => (p.Name, GetTypeString(p.Type))).ToList();
		LowerFunctionBody(func.Name, parameters, func.ReturnType, func.Body);
	}

	private void LowerMethod(string typeName, MethodDecl method) {
		var parameters = new List<(string name, string type)>();
		if (!method.IsStatic) {
			parameters.Add(("self", typeName));
		}
		parameters.AddRange(method.Params.Select(p => (p.Name, GetTypeString(p.Type))));

		LowerFunctionBody($"{typeName}.{method.Name}", parameters, method.ReturnType, method.Body);
	}

	private void LowerFunctionBody(string name, List<(string name, string type)> parameters, TypeRef? returnType, List<Stmt> body) {
		var returnTypeStr = GetTypeString(returnType);
		BeginFunction(name, parameters, returnTypeStr);

		foreach (var stmt in body) {
			LowerStatement(stmt);
		}

		if (_currentBlock is not null && !_currentBlock.IsTerminated && returnTypeStr == "void") {
			EmitReturn();
		}

		EndFunction();
	}

	private static string GetTypeString(TypeRef? typeRef) {
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
				LowerVariableDecl(let.Name, let.Value);
				break;

			case VarDeclStmt varDecl:
				LowerVariableDecl(varDecl.Name, varDecl.Value);
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
				if (_loopContextStack.Count == 0) {
					throw new InvalidOperationException("Break statement outside of loop");
				}
				var (_, breakExit) = _loopContextStack.Peek();
				EmitBranch(breakExit);
				break;

			case ContinueStmt:
				if (_loopContextStack.Count == 0) {
					throw new InvalidOperationException("Continue statement outside of loop");
				}
				var (continueTarget, _) = _loopContextStack.Peek();
				EmitBranch(continueTarget);
				break;

			default:
				throw new NotSupportedException($"Unsupported statement: {stmt.GetType().Name}");
		}
	}

	private void LowerVariableDecl(string name, Expr value) {
		var mlirValue = LowerExpression(value);
		var slot = DeclareVariable(name, mlirValue.Type);
		EmitStore(mlirValue, slot);
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

		// Body block - push loop context for break/continue
		SetInsertionPoint(bodyBlock);
		_loopContextStack.Push((condBlock, exitBlock));

		foreach (var stmt in whileStmt.Body) {
			LowerStatement(stmt);
			// Stop if we hit a terminator (break/continue/return)
			if (_currentBlock?.IsTerminated == true) break;
		}

		_loopContextStack.Pop();

		if (_currentBlock is not null && !_currentBlock.IsTerminated) {
			EmitBranch(condBlock);
		}

		SetInsertionPoint(exitBlock);
	}

	private void LowerForStatement(ForStmt forStmt) {
		// TODO: Implement for-range loop lowering properly
		// For now, create the structure with loop context for break/continue
		var condBlock = CreateBlock("for.cond");
		var bodyBlock = CreateBlock("for.body");
		var stepBlock = CreateBlock("for.step");
		var exitBlock = CreateBlock("for.exit");

		EmitBranch(condBlock);

		// Condition block (placeholder - jumps to exit)
		SetInsertionPoint(condBlock);
		EmitBranch(exitBlock);

		// Body block with loop context
		SetInsertionPoint(bodyBlock);
		_loopContextStack.Push((stepBlock, exitBlock));

		foreach (var stmt in forStmt.Body) {
			LowerStatement(stmt);
			if (_currentBlock?.IsTerminated == true) break;
		}

		_loopContextStack.Pop();

		if (_currentBlock is not null && !_currentBlock.IsTerminated) {
			EmitBranch(stepBlock);
		}

		// Step block
		SetInsertionPoint(stepBlock);
		EmitBranch(condBlock);

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
		var (left, right, isFloat) = LowerBinaryOperands(binary.Left, binary.Right);

		if (isFloat) {
			return binary.Op switch {
				BinaryOp.Add => Emit<AddFOp>(left, right),
				BinaryOp.Sub => Emit<SubFOp>(left, right),
				BinaryOp.Mul => Emit<MulFOp>(left, right),
				BinaryOp.Div => Emit<DivFOp>(left, right),
				_ => throw new NotSupportedException($"Unsupported binary op for float: {binary.Op}")
			};
		}

		return binary.Op switch {
			BinaryOp.Add => Emit<AddIOp>(left, right),
			BinaryOp.Sub => Emit<SubIOp>(left, right),
			BinaryOp.Mul => Emit<MulIOp>(left, right),
			BinaryOp.Div => Emit<DivSIOp>(left, right),
			BinaryOp.Mod => Emit<RemSIOp>(left, right),
			BinaryOp.Band => Emit<AndIOp>(left, right),
			BinaryOp.Bor => Emit<OrIOp>(left, right),
			BinaryOp.Bxor => Emit<XOrIOp>(left, right),
			BinaryOp.Shl => Emit<ShLIOp>(left, right),
			BinaryOp.Shr => Emit<ShRSIOp>(left, right),
			_ => throw new NotSupportedException($"Unsupported binary op: {binary.Op}")
		};
	}

	private MlirValue LowerCompareExpr(CompareExpr compare) {
		var (left, right, isFloat) = LowerBinaryOperands(compare.Left, compare.Right);

		if (isFloat) {
			var floatPredicate = compare.Op switch {
				CompareOp.Eq => CmpFPredicate.Oeq,
				CompareOp.Ne => CmpFPredicate.One,
				CompareOp.Lt => CmpFPredicate.Olt,
				CompareOp.Le => CmpFPredicate.Ole,
				CompareOp.Gt => CmpFPredicate.Ogt,
				CompareOp.Ge => CmpFPredicate.Oge,
				_ => throw new NotSupportedException($"Unsupported float compare op: {compare.Op}")
			};
			return EmitCmpF(floatPredicate, left, right);
		}

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

	/// <summary>
	/// Lowers binary operands with automatic type promotion to float if needed.
	/// </summary>
	private (MlirValue left, MlirValue right, bool isFloat) LowerBinaryOperands(Expr leftExpr, Expr rightExpr) {
		var left = LowerExpression(leftExpr);
		var right = LowerExpression(rightExpr);

		bool isFloat = left.Type is FloatType || right.Type is FloatType;
		if (isFloat) {
			if (left.Type is not FloatType) {
				left = Emit<SIToFPOp>(left, FloatType.F64);
			}
			if (right.Type is not FloatType) {
				right = Emit<SIToFPOp>(right, FloatType.F64);
			}
		}
		return (left, right, isFloat);
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

		// Look up the function's return type
		MlirType? returnType = _functionReturnTypes.TryGetValue(call.FuncName, out var rt) ? rt : IntegerType.I64;

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
				return Emit<FPToSIOp>(floatArg, IntegerType.I64);

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
			"void" or "Void" => NoneType.Instance,
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
	/// Emits a binary operation of type T. The operation must have a constructor (MlirValue, MlirValue).
	/// </summary>
	private MlirValue Emit<T>(MlirValue lhs, MlirValue rhs) where T : MlirOperation {
		var op = (T)Activator.CreateInstance(typeof(T), lhs, rhs)!;
		InsertOp(op);
		return op.Results[0];
	}

	/// <summary>
	/// Emits a conversion operation of type T. The operation must have a constructor (MlirValue, MlirType).
	/// </summary>
	private MlirValue Emit<T>(MlirValue operand, MlirType targetType) where T : MlirOperation {
		var op = (T)Activator.CreateInstance(typeof(T), operand, targetType)!;
		InsertOp(op);
		return op.Results[0];
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
	/// Emits a floating-point comparison.
	/// </summary>
	public MlirValue EmitCmpF(CmpFPredicate predicate, MlirValue lhs, MlirValue rhs) {
		var op = new CmpFOp(predicate, lhs, rhs);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits integer negation (0 - value).
	/// </summary>
	public MlirValue EmitNegI(MlirValue operand) {
		var zero = EmitConstantInt(0, operand.Type);
		return Emit<SubIOp>(zero, operand);
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
		return Emit<XOrIOp>(operand, allOnes);
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
}
