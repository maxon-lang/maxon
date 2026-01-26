using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

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
	private readonly HashSet<string> _immutableVariables = [];
	private readonly Dictionary<string, MlirType> _structTypes = [];
	private readonly Dictionary<string, MlirType> _enumTypes = [];
	private readonly Dictionary<string, MlirGlobal> _globals = [];
	private readonly Dictionary<string, MlirType> _functionReturnTypes = [];
	private readonly Dictionary<string, List<ParamDecl>> _functionParams = [];
	private readonly Stack<(MlirBlock continueBlock, MlirBlock exitBlock, string? label)> _loopContextStack = new();
	private MlirValue? _structReturnPtr; // Hidden sret parameter for struct returns
	private MaxonStructType? _currentMethodStructType; // The struct type when inside a method body

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

		// Collect function return types and parameters before lowering (for call site type lookup)
		foreach (var func in program.Functions) {
			var returnType = func.ReturnType != null ? ConvertTypeRef(func.ReturnType) : NoneType.Instance;
			_functionReturnTypes[func.Name] = returnType;
			_functionParams[func.Name] = func.Params;
		}

		// Collect method return types and parameters
		foreach (var type in program.Types) {
			foreach (var method in type.Methods) {
				var methodName = $"{type.Name}.{method.Name}";
				var returnType = method.ReturnType != null ? ConvertTypeRef(method.ReturnType) : NoneType.Instance;
				_functionReturnTypes[methodName] = returnType;
				// For methods, add 'self' as first parameter (unless static)
				var methodParams = new List<ParamDecl>();
				if (!method.IsStatic) {
					methodParams.Add(new ParamDecl("self", new SimpleTypeRef(type.Name)));
				}
				methodParams.AddRange(method.Params);
				_functionParams[methodName] = methodParams;
			}
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

	/// <summary>
	/// Infers the MLIR type from an expression AST node.
	/// Used for type inference when no explicit type annotation is provided.
	/// </summary>
	private static MlirType InferTypeFromExpr(Expr expr) {
		return expr switch {
			IntLiteralExpr => IntegerType.I64,
			FloatLiteralExpr => FloatType.F64,
			BoolLiteralExpr => IntegerType.I1,
			_ => throw new NotSupportedException($"Cannot infer type from expression: {expr.GetType().Name}. Please provide an explicit type annotation.")
		};
	}

	private void LowerTypeDecl(TypeDecl type) {
		var fields = new List<(string name, MlirType type, bool isMutable, Expr? defaultValue)>();

		foreach (var field in type.Fields) {
			MlirType fieldType;
			if (field.Type != null) {
				fieldType = ConvertTypeRef(field.Type);
			} else if (field.DefaultValue != null) {
				fieldType = InferTypeFromExpr(field.DefaultValue);
			} else {
				throw new InvalidOperationException($"Field '{field.Name}' has no type annotation and no default value");
			}
			fields.Add((field.Name, fieldType, field.IsMutable, field.DefaultValue));
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
			// Set the current method struct type for field resolution
			if (_structTypes.TryGetValue(typeName, out var structType) && structType is MaxonStructType mst) {
				_currentMethodStructType = mst;
			}
		}
		parameters.AddRange(method.Params.Select(p => (p.Name, GetTypeString(p.Type))));

		try {
			LowerFunctionBody($"{typeName}.{method.Name}", parameters, method.ReturnType, method.Body);
		} finally {
			_currentMethodStructType = null;
		}
	}

	private void LowerFunctionBody(string name, List<(string name, string type)> parameters, TypeRef? returnType, List<Stmt> body) {
		var returnTypeStr = GetTypeString(returnType);

		// Check if return type is a struct (needs sret parameter)
		MlirType? returnMlirType = null;
		if (returnType != null) {
			try {
				returnMlirType = ConvertTypeRef(returnType);
			} catch {
				// If conversion fails, treat as non-struct
			}
		}

		// For struct returns, use sret calling convention (hidden pointer parameter)
		if (returnMlirType is MaxonStructType structType) {
			BeginFunction(name, parameters, "void", sretType: structType);
		} else {
			BeginFunction(name, parameters, returnTypeStr);
		}

		foreach (var stmt in body) {
			LowerStatement(stmt);
		}

		// For void functions (or struct returns using sret), add implicit return if needed
		bool isEffectivelyVoid = returnTypeStr == "void" || returnMlirType is MaxonStructType;
		if (_currentBlock is not null && !_currentBlock.IsTerminated && isEffectivelyVoid) {
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
					// For struct returns with sret, get the expected struct type
					MlirType? expectedType;
					if (_structReturnPtr != null && _structReturnPtr.Type is PtrType pt) {
						expectedType = pt.ElementType;
					} else {
						expectedType = _currentFunction?.ResultTypes.FirstOrDefault();
					}

					// If sret return with struct literal, store fields directly to sret pointer
					if (_structReturnPtr != null && ret.Value is StructInitExpr structInit) {
						var structType = expectedType as MaxonStructType
							?? throw new InvalidOperationException("Expected struct type for sret return");

						// Store each field value directly to the sret pointer
						foreach (var fieldInit in structInit.Fields) {
							var fieldValue = LowerExpression(fieldInit.Value);
							var fieldInfo = structType.GetField(fieldInit.Name)
								?? throw new InvalidOperationException($"Field '{fieldInit.Name}' not found");

							var fieldPtr = new FieldPtrOp(_structReturnPtr, fieldInit.Name, fieldInfo.Offset, fieldInfo.Type);
							InsertOp(fieldPtr);
							var storeOp = new StoreOp(fieldValue, fieldPtr.Result);
							InsertOp(storeOp);
						}
						EmitReturn();
					} else if (_structReturnPtr != null) {
						// sret return with existing struct variable - copy it
						var value = LowerExpression(ret.Value, expectedType);
						EmitStructCopy(_structReturnPtr, value);
						EmitReturn();
					} else {
						var value = LowerExpression(ret.Value, expectedType);
						EmitReturn(value);
					}
				} else {
					EmitReturn();
				}
				break;

			case LetDeclStmt let:
				LowerVariableDecl(let.Name, let.Value, isImmutable: true);
				break;

			case VarDeclStmt varDecl:
				LowerVariableDecl(varDecl.Name, varDecl.Value, isImmutable: false);
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

			case BreakStmt breakStmt:
				if (_loopContextStack.Count == 0) {
					throw new InvalidOperationException("Break statement outside of loop");
				}
				var breakExit = FindLoopContext(breakStmt.Label).exitBlock;
				EmitBranch(breakExit);
				break;

			case ContinueStmt continueStmt:
				if (_loopContextStack.Count == 0) {
					throw new InvalidOperationException("Continue statement outside of loop");
				}
				var continueTarget = FindLoopContext(continueStmt.Label).continueBlock;
				EmitBranch(continueTarget);
				break;

			case FieldAssignStmt fieldAssign:
				LowerFieldAssign(fieldAssign);
				break;

			default:
				throw new NotSupportedException($"Unsupported statement: {stmt.GetType().Name}");
		}
	}

	private void LowerVariableDecl(string name, Expr value, bool isImmutable) {
		var mlirValue = LowerExpression(value);
		var slot = DeclareVariable(name, mlirValue.Type);
		EmitStore(mlirValue, slot);
		if (isImmutable) {
			_immutableVariables.Add(name);
		}
	}

	/// <summary>
	/// Finds the loop context for the given label. If label is null, returns the innermost loop.
	/// </summary>
	private (MlirBlock continueBlock, MlirBlock exitBlock, string? label) FindLoopContext(string? targetLabel) {
		if (targetLabel is null) {
			// No label - use innermost loop
			return _loopContextStack.Peek();
		}

		// Search for matching label
		foreach (var ctx in _loopContextStack) {
			if (ctx.label == targetLabel) {
				return ctx;
			}
		}

		throw new InvalidOperationException($"No loop found with label '{targetLabel}'");
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

		// Body block - push loop context for break/continue (including label)
		SetInsertionPoint(bodyBlock);
		_loopContextStack.Push((condBlock, exitBlock, whileStmt.Block.Identifier));

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

		// Body block with loop context (including label)
		SetInsertionPoint(bodyBlock);
		_loopContextStack.Push((stepBlock, exitBlock, forStmt.Block.Identifier));

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

	private MlirValue LowerExpression(Expr expr, MlirType? expectedType = null) {
		Logger.Trace(LogCategory.Mlir, $"LowerExpression: {expr.GetType().Name}");
		var result = expr switch {
			IntLiteralExpr intLit => EmitConstantInt(intLit.Value),
			FloatLiteralExpr floatLit => EmitConstantFloat(floatLit.Value),
			BoolLiteralExpr boolLit => EmitConstantBool(boolLit.Value),
			IdentifierExpr ident => GetVariable(ident.Name),
			BinaryExpr binary => LowerBinaryExpr(binary),
			CompareExpr compare => LowerCompareExpr(compare),
			LogicalExpr logical => LowerLogicalExpr(logical),
			UnaryExpr unary => LowerUnaryExpr(unary),
			CallExpr call => LowerCallExpr(call),
			MethodCallExpr methodCall => LowerMethodCallExpr(methodCall),
			StaticCallExpr staticCall => LowerStaticCallExpr(staticCall),
			StructInitExpr structInit => LowerStructInitExpr(structInit, expectedType),
			FieldAccessExpr fieldAccess => LowerFieldAccessExpr(fieldAccess),
			_ => throw new NotSupportedException($"Unsupported expression: {expr.GetType().Name}")
		};
		return result!;
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
	/// Lowers a logical expression with short-circuit evaluation.
	/// For `a and b`: if a is false, skip evaluating b and return false.
	/// For `a or b`: if a is true, skip evaluating b and return true.
	///
	/// Uses a stack variable for the result since phi elimination isn't implemented yet.
	/// Pattern: result = left; if (should_eval_right) result = right;
	/// </summary>
	private MlirValue LowerLogicalExpr(LogicalExpr logical) {
		// Allocate a stack slot for the result
		var resultSlot = EmitAlloca(IntegerType.I1);

		// Evaluate left operand in the current block
		var left = LowerExpression(logical.Left);

		// Store left value as initial result
		EmitStore(left, resultSlot);

		// Create blocks for short-circuit evaluation
		var evalRightBlock = CreateBlock(logical.Op == LogicalOp.And ? "and.right" : "or.right");
		var mergeBlock = CreateBlock(logical.Op == LogicalOp.And ? "and.merge" : "or.merge");

		// For 'and': if left is true, evaluate right and use that as result
		// For 'or': if left is false, evaluate right and use that as result
		if (logical.Op == LogicalOp.And) {
			// and: only evaluate right if left is true
			EmitCondBranch(left, evalRightBlock, mergeBlock);
		} else {
			// or: only evaluate right if left is false
			EmitCondBranch(left, mergeBlock, evalRightBlock);
		}

		// Evaluate right operand and store result
		SetInsertionPoint(evalRightBlock);
		var right = LowerExpression(logical.Right);
		EmitStore(right, resultSlot);
		EmitBranch(mergeBlock);

		// Continue in merge block, load the result
		SetInsertionPoint(mergeBlock);
		return EmitLoad(resultSlot);
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

		// Check for sibling method call (calling another method of same type without self.)
		// If we're inside a method and the function name matches a method of the current type,
		// treat it as self.methodName()
		if (_currentMethodStructType != null && _namedValues.TryGetValue("self", out var selfValue)) {
			var siblingMethodName = $"{_currentMethodStructType.Name}.{call.FuncName}";
			if (_functionParams.ContainsKey(siblingMethodName)) {
				Logger.Trace(LogCategory.Mlir, $"LowerCallExpr: resolved sibling method call {call.FuncName} -> {siblingMethodName}");

				// Get parameter info for this method
				_functionParams.TryGetValue(siblingMethodName, out List<ParamDecl>? methodParamDecls);

				// Build argument list: self + method arguments
				var methodArgs = new List<MlirValue> { selfValue };
				var methodOwnership = new List<ArgumentOwnership> { ArgumentOwnership.Borrow };

				// Use positional args directly for sibling calls
				for (int i = 0; i < call.Args.Count; i++) {
					var arg = call.Args[i];

					// Get expected type from parameter declaration (offset by 1 for 'self')
					MlirType? expectedType = null;
					if (methodParamDecls != null && i + 1 < methodParamDecls.Count) {
						try {
							expectedType = ConvertTypeRef(methodParamDecls[i + 1].Type);
						} catch {
							// If type conversion fails, continue without expected type
						}
					}

					methodArgs.Add(LowerExpression(arg, expectedType));

					// Determine ownership based on mutation analysis (offset by 1 for 'self')
					if (_mutationAnalyzer.IsMutated(siblingMethodName, i + 1)) {
						methodOwnership.Add(ArgumentOwnership.Move);
					} else {
						methodOwnership.Add(ArgumentOwnership.Borrow);
					}
				}

				// Look up the method's return type
				MlirType? methodReturnType = _functionReturnTypes.TryGetValue(siblingMethodName, out var mrt) ? mrt : IntegerType.I64;

				return EmitMaxonCall(siblingMethodName, methodArgs, methodOwnership, methodReturnType);
			}
		}

		// Regular function call
		// Resolve arguments: combine positional args with named args in the correct order
		var resolvedArgs = ResolveCallArguments(call);

		// Get parameter types for expected type inference
		_functionParams.TryGetValue(call.FuncName, out List<ParamDecl>? paramDecls);

		var args = new List<MlirValue>();
		var ownership = new List<ArgumentOwnership>();

		for (int i = 0; i < resolvedArgs.Count; i++) {
			var arg = resolvedArgs[i];

			// Get expected type from parameter declaration for anonymous struct inference
			MlirType? expectedType = null;
			if (paramDecls != null && i < paramDecls.Count) {
				try {
					expectedType = ConvertTypeRef(paramDecls[i].Type);
				} catch {
					// If type conversion fails, continue without expected type
				}
			}

			args.Add(LowerExpression(arg, expectedType));

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
	/// Lowers a method call expression (e.g., obj.method(args)).
	/// The base expression is evaluated and passed as the first argument (self).
	/// </summary>
	private MlirValue? LowerMethodCallExpr(MethodCallExpr methodCall) {
		// Evaluate the base expression to get the receiver
		var baseValue = LowerExpression(methodCall.Base);

		Logger.Trace(LogCategory.Mlir, $"LowerMethodCallExpr: base type = {baseValue.Type}, methodName = {methodCall.MethodName}");

		// Get the struct type from the base value to determine the method name
		if (baseValue.Type is not MaxonStructType structType) {
			throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Cannot call method '{methodCall.MethodName}' on non-struct type: {baseValue.Type}",
				methodCall.Location?.Line, methodCall.Location?.Column);
		}

		// Build the fully qualified method name
		var methodName = $"{structType.Name}.{methodCall.MethodName}";
		Logger.Trace(LogCategory.Mlir, $"LowerMethodCallExpr: qualified method name = {methodName}");

		// Get parameter info for this method
		_functionParams.TryGetValue(methodName, out List<ParamDecl>? paramDecls);

		// Build argument list: self + method arguments
		var args = new List<MlirValue> { baseValue };
		var ownership = new List<ArgumentOwnership> { ArgumentOwnership.Borrow };

		// Resolve remaining arguments (skip first param which is 'self')
		var resolvedArgs = ResolveMethodCallArguments(methodCall, paramDecls);
		for (int i = 0; i < resolvedArgs.Count; i++) {
			var arg = resolvedArgs[i];

			// Get expected type from parameter declaration (offset by 1 for 'self')
			MlirType? expectedType = null;
			if (paramDecls != null && i + 1 < paramDecls.Count) {
				try {
					expectedType = ConvertTypeRef(paramDecls[i + 1].Type);
				} catch {
					// If type conversion fails, continue without expected type
				}
			}

			args.Add(LowerExpression(arg, expectedType));

			// Determine ownership based on mutation analysis (offset by 1 for 'self')
			if (_mutationAnalyzer.IsMutated(methodName, i + 1)) {
				ownership.Add(ArgumentOwnership.Move);
			} else {
				ownership.Add(ArgumentOwnership.Borrow);
			}
		}

		// Look up the method's return type
		MlirType? returnType = _functionReturnTypes.TryGetValue(methodName, out var rt) ? rt : IntegerType.I64;

		return EmitMaxonCall(methodName, args, ownership, returnType);
	}

	/// <summary>
	/// Lowers a static call expression (e.g., Type.method(args)).
	/// The parser creates StaticCallExpr for any "name.member(args)" pattern.
	/// At lowering time, we check if "name" refers to a variable (instance method call)
	/// or a type (true static call).
	/// </summary>
	private MlirValue? LowerStaticCallExpr(StaticCallExpr staticCall) {
		// Check if TypeName is actually a variable (instance method call)
		// If so, treat it as a method call on that variable
		MlirValue? baseValue = null;
		MaxonStructType? structType = null;
		if (_namedValues.TryGetValue(staticCall.TypeName, out _)) {
			// This is actually an instance method call: variable.method(args)
			baseValue = GetVariable(staticCall.TypeName);
			if (baseValue.Type is MaxonStructType st) {
				structType = st;
			}
		} else if (_currentMethodStructType != null) {
			// Check if TypeName is a field of the current method's struct (e.g., inner.get() inside a method)
			var fieldInfo = _currentMethodStructType.GetField(staticCall.TypeName);
			if (fieldInfo != null && _namedValues.TryGetValue("self", out var selfValue)) {
				// Access the field from self
				baseValue = EmitGetField(selfValue, staticCall.TypeName, fieldInfo.Type);
				if (baseValue.Type is MaxonStructType st) {
					structType = st;
				}
				Logger.Trace(LogCategory.Mlir, $"LowerStaticCallExpr: resolved field '{staticCall.TypeName}' of self to instance method call");
			}
		}

		if (baseValue != null && structType != null) {

			// Build the fully qualified method name
			var methodName = $"{structType.Name}.{staticCall.MemberName}";
			Logger.Trace(LogCategory.Mlir, $"LowerStaticCallExpr: resolved variable '{staticCall.TypeName}' to instance method call {methodName}");

			// Get parameter info for this method
			_functionParams.TryGetValue(methodName, out List<ParamDecl>? paramDecls);

			// Build argument list: self + method arguments
			var args = new List<MlirValue> { baseValue };
			var ownership = new List<ArgumentOwnership> { ArgumentOwnership.Borrow };

			// Resolve arguments (skip first param which is 'self') - validates named args requirement
			var resolvedArgs = ResolveInstanceMethodCallArguments(staticCall, paramDecls);
			for (int i = 0; i < resolvedArgs.Count; i++) {
				var arg = resolvedArgs[i];

				// Get expected type from parameter declaration (offset by 1 for 'self')
				MlirType? expectedType = null;
				if (paramDecls != null && i + 1 < paramDecls.Count) {
					try {
						expectedType = ConvertTypeRef(paramDecls[i + 1].Type);
					} catch {
						// If type conversion fails, continue without expected type
					}
				}

				args.Add(LowerExpression(arg, expectedType));

				// Determine ownership based on mutation analysis (offset by 1 for 'self')
				if (_mutationAnalyzer.IsMutated(methodName, i + 1)) {
					ownership.Add(ArgumentOwnership.Move);
				} else {
					ownership.Add(ArgumentOwnership.Borrow);
				}
			}

			// Look up the method's return type
			MlirType? returnType = _functionReturnTypes.TryGetValue(methodName, out var rt) ? rt : IntegerType.I64;

			return EmitMaxonCall(methodName, args, ownership, returnType);
		}

		// True static call: Type.method(args)
		var staticMethodName = $"{staticCall.TypeName}.{staticCall.MemberName}";
		Logger.Trace(LogCategory.Mlir, $"LowerStaticCallExpr: static call {staticMethodName}");

		// Get parameter info for this method
		_functionParams.TryGetValue(staticMethodName, out List<ParamDecl>? staticParamDecls);

		// Build argument list
		var staticArgs = new List<MlirValue>();
		var staticOwnership = new List<ArgumentOwnership>();


		// Resolve arguments
		var staticResolvedArgs = ResolveStaticCallArguments(staticCall, staticParamDecls);
		for (int i = 0; i < staticResolvedArgs.Count; i++) {
			var arg = staticResolvedArgs[i];

			// Get expected type from parameter declaration
			MlirType? expectedType = null;
			if (staticParamDecls != null && i < staticParamDecls.Count) {
				try {
					expectedType = ConvertTypeRef(staticParamDecls[i].Type);
				} catch {
					// If type conversion fails, continue without expected type
				}
			}

			staticArgs.Add(LowerExpression(arg, expectedType));

			// Determine ownership based on mutation analysis
			if (_mutationAnalyzer.IsMutated(staticMethodName, i)) {
				staticOwnership.Add(ArgumentOwnership.Move);
			} else {
				staticOwnership.Add(ArgumentOwnership.Borrow);
			}
		}

		// Look up the method's return type
		MlirType? staticReturnType = _functionReturnTypes.TryGetValue(staticMethodName, out var srt) ? srt : IntegerType.I64;

		return EmitMaxonCall(staticMethodName, staticArgs, staticOwnership, staticReturnType);
	}

	/// <summary>
	/// Core argument resolution logic shared by all call types.
	/// Validates named argument requirements and resolves arguments in parameter order.
	/// </summary>
	private static List<Expr> ResolveArgumentsCore(
		List<Expr> positionalArgs,
		List<NamedArg> namedArgs,
		List<ParamDecl> paramDecls,
		int? line,
		int? column) {

		// Validate: only the first argument can be positional, rest must be named
		if (positionalArgs.Count > 1) {
			throw new CompileError(
				ErrorCode.SemanticTypeMismatch,
				"Second and subsequent arguments must be named. Use 'name: value' syntax",
				line ?? 0,
				column ?? 0);
		}

		// Build the resolved argument list in parameter order
		var resolvedArgs = new Expr?[paramDecls.Count];

		// First, place positional arguments
		for (int i = 0; i < positionalArgs.Count && i < paramDecls.Count; i++) {
			resolvedArgs[i] = positionalArgs[i];
		}

		// Then, place named arguments in their correct positions
		foreach (var namedArg in namedArgs) {
			int paramIndex = -1;
			for (int i = 0; i < paramDecls.Count; i++) {
				if (paramDecls[i].Name == namedArg.Name) {
					paramIndex = i;
					break;
				}
			}

			if (paramIndex < 0) {
				throw new CompileError(
					ErrorCode.SemanticUndefinedVariable,
					$"unknown parameter name: '{namedArg.Name}'",
					namedArg.Value.Location?.Line ?? 0,
					namedArg.Value.Location?.Column ?? 0);
			}

			resolvedArgs[paramIndex] = namedArg.Value;
		}

		// Fill in default values for any missing arguments
		var result = new List<Expr>();
		for (int i = 0; i < paramDecls.Count; i++) {
			if (resolvedArgs[i] != null) {
				result.Add(resolvedArgs[i]!);
			} else if (paramDecls[i].DefaultValue != null) {
				result.Add(paramDecls[i].DefaultValue!);
			} else {
				throw new CompileError(
					ErrorCode.SemanticTypeMismatch,
					$"missing argument for parameter '{paramDecls[i].Name}'",
					line ?? 0,
					column ?? 0);
			}
		}

		return result;
	}

	/// <summary>
	/// Resolves method call arguments by combining positional and named arguments.
	/// </summary>
	private static List<Expr> ResolveMethodCallArguments(MethodCallExpr methodCall, List<ParamDecl>? paramDecls) {
		if (paramDecls == null || paramDecls.Count <= 1) {
			return methodCall.Args;
		}
		// Skip 'self' parameter for argument resolution
		var methodParamDecls = paramDecls.Skip(1).ToList();
		return ResolveArgumentsCore(methodCall.Args, methodCall.NamedArgs, methodParamDecls, methodCall.Location?.Line, methodCall.Location?.Column);
	}

	/// <summary>
	/// Resolves instance method call arguments when called via StaticCallExpr syntax (var.method(args)).
	/// </summary>
	private static List<Expr> ResolveInstanceMethodCallArguments(StaticCallExpr staticCall, List<ParamDecl>? paramDecls) {
		if (paramDecls == null || paramDecls.Count <= 1) {
			return staticCall.Args;
		}
		// Skip 'self' parameter for argument resolution
		var methodParamDecls = paramDecls.Skip(1).ToList();
		return ResolveArgumentsCore(staticCall.Args, staticCall.NamedArgs, methodParamDecls, staticCall.Location?.Line, staticCall.Location?.Column);
	}

	/// <summary>
	/// Resolves static call arguments by combining positional and named arguments.
	/// </summary>
	private static List<Expr> ResolveStaticCallArguments(StaticCallExpr staticCall, List<ParamDecl>? paramDecls) {
		if (paramDecls == null || paramDecls.Count == 0) {
			return staticCall.Args;
		}
		return ResolveArgumentsCore(staticCall.Args, staticCall.NamedArgs, paramDecls, staticCall.Location?.Line, staticCall.Location?.Column);
	}

	/// <summary>
	/// Resolves call arguments by combining positional and named arguments in the correct parameter order.
	/// </summary>
	private List<Expr> ResolveCallArguments(CallExpr call) {
		if (!_functionParams.TryGetValue(call.FuncName, out var paramDecls)) {
			Logger.Trace(LogCategory.Mlir, $"No param info for {call.FuncName}, using positional args only");
			return call.Args;
		}
		return ResolveArgumentsCore(call.Args, call.NamedArgs, paramDecls, call.Location?.Line, call.Location?.Column);
	}

	/// <summary>
	/// Attempts to lower a builtin function call. Returns null if not a builtin.
	/// </summary>
	private MlirValue? TryLowerBuiltinCall(CallExpr call) {
		return call.Builtin switch {
			BuiltinOp.None => null,
			BuiltinOp.Trunc => LowerTrunc(call),
			BuiltinOp.Sqrt => LowerUnaryMath<SqrtOp>(call),
			BuiltinOp.Floor => LowerUnaryMath<FloorOp>(call),
			BuiltinOp.Ceil => LowerUnaryMath<CeilOp>(call),
			BuiltinOp.Round => LowerUnaryMath<RoundOp>(call),
			BuiltinOp.Abs => LowerUnaryMath<AbsFOp>(call),
			BuiltinOp.Min => LowerBinaryMath<MinFOp>(call),
			BuiltinOp.Max => LowerBinaryMath<MaxFOp>(call),
			_ => throw new InvalidOperationException($"Unknown builtin: {call.Builtin}")
		};
	}

	private MlirValue LowerTrunc(CallExpr call) {
		if (call.Args.Count != 1)
			throw new ArgumentException($"{call.FuncName}() requires exactly 1 argument");
		var arg = LowerExpression(call.Args[0]);
		return Emit<FPToSIOp>(arg, IntegerType.I64);
	}

	private MlirValue LowerUnaryMath<T>(CallExpr call) where T : MlirOperation {
		if (call.Args.Count != 1)
			throw new ArgumentException($"{call.FuncName}() requires exactly 1 argument");
		var arg = LowerExpression(call.Args[0]);
		return EmitUnary<T>(arg);
	}

	private MlirValue LowerBinaryMath<T>(CallExpr call) where T : MlirOperation {
		if (call.Args.Count != 2)
			throw new ArgumentException($"{call.FuncName}() requires exactly 2 arguments");
		var (lhs, rhs, _) = LowerBinaryOperands(call.Args[0], call.Args[1]);
		return Emit<T>(lhs, rhs);
	}

	/// <summary>
	/// Emits a unary operation of type T. The operation must have a constructor (MlirValue).
	/// </summary>
	private MlirValue EmitUnary<T>(MlirValue operand) where T : MlirOperation {
		var op = (T)Activator.CreateInstance(typeof(T), operand)!;
		InsertOp(op);
		return op.Results[0];
	}

	private MlirValue LowerStructInitExpr(StructInitExpr structInit, MlirType? expectedType = null) {
		MaxonStructType? structType;
		if (structInit.TypeName != null) {
			// Explicit type name provided
			if (!_structTypes.TryGetValue(structInit.TypeName, out var mlirType) || mlirType is not MaxonStructType st) {
				throw new CompileError(ErrorCode.MlirUndefinedType, $"Unknown struct type: {structInit.TypeName}",
					structInit.Location?.Line, structInit.Location?.Column);
			}
			structType = st;
		} else if (expectedType is MaxonStructType expected) {
			// Anonymous struct literal - infer type from context
			structType = expected;
		} else {
			throw new CompileError(ErrorCode.MlirUndefinedType,
				"Struct initialization requires a type name or type context",
				structInit.Location?.Line, structInit.Location?.Column);
		}

		// Lower each field initializer expression
		var fieldValues = new Dictionary<string, MlirValue>();
		foreach (var field in structInit.Fields) {
			var value = LowerExpression(field.Value);
			fieldValues[field.Name] = value;
		}

		// Fill in default values for any missing fields
		foreach (var fieldInfo in structType.Fields) {
			if (!fieldValues.ContainsKey(fieldInfo.Name) && fieldInfo.DefaultValue != null) {
				fieldValues[fieldInfo.Name] = LowerExpression(fieldInfo.DefaultValue);
			}
		}

		return EmitStructInit(structType, fieldValues);
	}

	private MlirValue LowerFieldAccessExpr(FieldAccessExpr fieldAccess) {
		// Lower the base expression to get the struct value
		var baseValue = LowerExpression(fieldAccess.Base);

		// Get the struct type from the base value
		if (baseValue.Type is not MaxonStructType structType) {
			throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Cannot access field '{fieldAccess.FieldName}' on non-struct type: {baseValue.Type}",
				fieldAccess.Location?.Line, fieldAccess.Location?.Column);
		}

		// Look up the field info to get the field type
		var fieldInfo = structType.GetField(fieldAccess.FieldName) ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Struct '{structType.Name}' does not have a field '{fieldAccess.FieldName}'",
				fieldAccess.Location?.Line, fieldAccess.Location?.Column);
		return EmitGetField(baseValue, fieldAccess.FieldName, fieldInfo.Type);
	}

	private void LowerFieldAssign(FieldAssignStmt fieldAssign) {
		// Check if the base is an immutable variable
		if (fieldAssign.Base is IdentifierExpr idExpr && _immutableVariables.Contains(idExpr.Name)) {
			throw new CompileError(ErrorCode.ImmutableVariable,
				$"cannot assign to immutable variable: '{idExpr.Name}'",
				fieldAssign.Location?.Line, fieldAssign.Location?.Column);
		}

		// Lower the base expression to get the struct value
		var baseValue = LowerExpression(fieldAssign.Base);

		// Get the struct type from the base value
		if (baseValue.Type is not MaxonStructType structType) {
			throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Cannot assign to field '{fieldAssign.FieldName}' on non-struct type: {baseValue.Type}",
				fieldAssign.Location?.Line, fieldAssign.Location?.Column);
		}

		// Look up the field info
		var fieldInfo = structType.GetField(fieldAssign.FieldName) ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Struct '{structType.Name}' does not have a field '{fieldAssign.FieldName}'",
				fieldAssign.Location?.Line, fieldAssign.Location?.Column);

		// Check if the field is immutable (declared with 'let')
		if (!fieldInfo.IsMutable) {
			throw new CompileError(ErrorCode.ImmutableVariable,
				$"cannot assign to immutable field: '{fieldAssign.FieldName}'",
				fieldAssign.Location?.Line, fieldAssign.Location?.Column);
		}

		// Lower the value being assigned
		var value = LowerExpression(fieldAssign.Value);

		// Emit the field set operation
		EmitSetField(baseValue, fieldAssign.FieldName, value);
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
	public MlirFunction BeginFunction(string name, List<(string name, string type)> parameters, string returnType, MlirType? sretType = null) {
		var func = new MlirFunction(name);

		// If we have a struct return type, add hidden sret parameter first
		if (sretType != null) {
			var sretPtrType = new PtrType(sretType);
			func.AddParam("__sret", sretPtrType);
		}

		foreach (var (pname, ptype) in parameters) {
			func.AddParam(pname, ConvertType(ptype));
		}

		if (returnType != "void") {
			func.ResultTypes.Add(ConvertType(returnType));
		}

		_currentFunction = func;
		_namedValues.Clear();
		_immutableVariables.Clear();

		// Create entry block with parameters
		var entry = func.CreateEntryBlock();
		_currentBlock = entry;

		// Map parameter names to values
		var paramValues = func.GetParamValues();
		int paramIndex = 0;

		// Handle sret parameter
		if (sretType != null) {
			_structReturnPtr = paramValues[paramIndex++];
			// Don't add sret to named values - it's internal
		} else {
			_structReturnPtr = null;
		}

		for (int i = 0; i < parameters.Count; i++) {
			_namedValues[parameters[i].name] = paramValues[paramIndex++];
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
	public MaxonStructType RegisterStruct(string name, List<(string name, MlirType type, bool isMutable, Expr? defaultValue)> fields) {
		// Convert tuples to MaxonFieldInfo with calculated offsets
		var fieldInfos = new List<MaxonFieldInfo>();
		int offset = 0;
		foreach (var (fname, ftype, isMutable, defaultValue) in fields) {
			fieldInfos.Add(new MaxonFieldInfo(fname, ftype, offset, isMutable, defaultValue));
			offset += ftype.SizeInBytes;
		}

		var structType = new MaxonStructType(name, fieldInfos);
		_structTypes[name] = structType;

		// Add to module
		var def = new MlirStructDef(name);
		offset = 0;
		foreach (var (fname, ftype, isMutable, _) in fields) {
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
		var op = new ReturnOp([.. values]);
		InsertOp(op);
	}

	/// <summary>
	/// Copies a struct from source pointer to destination pointer.
	/// </summary>
	private void EmitStructCopy(MlirValue destPtr, MlirValue srcPtr) {
		// Get the struct type to know the fields
		MaxonStructType? structType = null;
		if (destPtr.Type is PtrType pt && pt.ElementType is MaxonStructType st) {
			structType = st;
		} else if (srcPtr.Type is MemRefType mrt && mrt.ElementType is MaxonStructType st2) {
			structType = st2;
		}

		if (structType == null) {
			throw new InvalidOperationException("EmitStructCopy requires struct type pointers");
		}

		// Copy each field
		foreach (var field in structType.Fields) {
			// Load from source
			var srcFieldPtr = new FieldPtrOp(srcPtr, field.Name, field.Offset, field.Type);
			InsertOp(srcFieldPtr);
			var loadOp = new LoadOp(srcFieldPtr.Result);
			InsertOp(loadOp);

			// Store to destination
			var dstFieldPtr = new FieldPtrOp(destPtr, field.Name, field.Offset, field.Type);
			InsertOp(dstFieldPtr);
			var storeOp = new StoreOp(loadOp.Result, dstFieldPtr.Result);
			InsertOp(storeOp);
		}
	}

	// ========================================================================
	// Function Calls
	// ========================================================================

	/// <summary>
	/// Emits a function call.
	/// </summary>
	public MlirValue? EmitCall(string callee, List<MlirValue> args, MlirType? returnType = null) {
		var op = new FuncCallOp(callee, args, returnType);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Emits a function call with ownership annotations.
	/// </summary>
	public MlirValue? EmitMaxonCall(string callee, List<MlirValue> args, List<ArgumentOwnership> ownership, MlirType? returnType = null) {
		var op = new MaxonCallOp(callee, args, ownership, returnType);
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

		// Note: This throws here because callers expect an address.
		// Field access via self is handled separately in GetVariable/SetVariable.
		throw new InvalidOperationException($"Undefined variable: {name}");
	}

	/// <summary>
	/// Tries to get a field from self when inside a method.
	/// </summary>
	private MlirValue? TryGetFieldFromSelf(string name) {
		// Check if we're inside a method and the name is a field of the current type
		if (_currentMethodStructType != null) {
			var fieldInfo = _currentMethodStructType.GetField(name);
			if (fieldInfo != null && _namedValues.TryGetValue("self", out var selfValue)) {
				return EmitGetField(selfValue, name, fieldInfo.Type);
			}
		}
		return null;
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
		// First check if this is a field access on self (when inside a method)
		var selfField = TryGetFieldFromSelf(name);
		if (selfField != null) {
			return selfField;
		}

		var addr = GetVariableAddress(name);
		if (addr.Type is MemRefType)
			return EmitLoad(addr);
		return addr; // Already a value (e.g., function parameter)
	}

	/// <summary>
	/// Sets a variable's value (stores it).
	/// </summary>
	public void SetVariable(string name, MlirValue value) {
		// First check if this is a field assignment on self (when inside a method)
		if (TrySetFieldOnSelf(name, value)) {
			return;
		}

		var addr = GetVariableAddress(name);
		EmitStore(value, addr);
	}

	/// <summary>
	/// Tries to set a field on self when inside a method.
	/// </summary>
	private bool TrySetFieldOnSelf(string name, MlirValue value) {
		// Check if we're inside a method and the name is a field of the current type
		if (_currentMethodStructType != null) {
			var fieldInfo = _currentMethodStructType.GetField(name);
			if (fieldInfo != null && _namedValues.TryGetValue("self", out var selfValue)) {
				EmitSetField(selfValue, name, value);
				return true;
			}
		}
		return false;
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
