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
	private readonly Dictionary<string, VariableBinding> _namedValues = [];
	private readonly HashSet<string> _immutableVariables = [];
	private readonly Dictionary<string, MlirType> _structTypes = [];
	private readonly Dictionary<string, MlirType> _enumTypes = [];
	private readonly Dictionary<string, TypeDecl> _genericTypes = []; // Generic type declarations for monomorphization
	private readonly HashSet<string> _loweredMonomorphizedMethods = []; // Track which monomorphized methods have been lowered (by full method name)
	private readonly Dictionary<string, (TypeDecl genericType, Dictionary<string, string> paramMap)> _pendingMonomorphizedTypes = []; // For on-demand method lowering
	private readonly Dictionary<string, MlirGlobal> _globals = [];
	private readonly Dictionary<string, MlirType> _functionReturnTypes = [];
	private readonly Dictionary<string, List<ParamDecl>> _functionParams = [];
	private readonly Stack<(MlirBlock continueBlock, MlirBlock exitBlock, string? label)> _loopContextStack = new();
	private MlirValue? _structReturnPtr; // Hidden sret parameter for struct returns
	private MaxonStructType? _currentMethodStructType; // The struct type when inside a method body
	private MaxonErrorUnionType? _currentFunctionErrorUnionType; // Error union type if current function throws
	private MlirType? _currentFunctionSuccessType; // The actual return type before wrapping in error union

	/// <summary>
	/// Gets the generated module.
	/// </summary>
	public MlirModule Module => _module;

	/// <summary>
	/// Converts a program AST to the Maxon dialect.
	/// </summary>
	public MlirModule ConvertProgram(ProgramAst program) {
		Logger.Debug(LogCategory.Mlir, $"AST->MLIR: Converting program with {program.Types.Count} types, {program.Functions.Count} functions");

		// Register built-in internal types before processing user types
		RegisterBuiltinTypes();

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
			// For throwing functions, the actual return type is an error union
			if (func.ThrowsType != null && _enumTypes.TryGetValue(func.ThrowsType, out var errorEnumType) && errorEnumType is MaxonEnumType errorEnum) {
				returnType = new MaxonErrorUnionType(returnType, errorEnum);
			}
			_functionReturnTypes[func.Name] = returnType;
			_functionParams[func.Name] = func.Params;
		}

		// Collect method return types and parameters (skip generic types - they are handled during monomorphization)
		foreach (var type in program.Types) {
			// Skip generic types - methods will be created when the type is instantiated
			if (type.GenericParams.Count > 0) continue;

			foreach (var method in type.Methods) {
				var methodName = $"{type.Name}.{method.Name}";
				var returnType = method.ReturnType != null ? ConvertTypeRef(method.ReturnType) : NoneType.Instance;
				// For throwing methods, the actual return type is an error union
				if (method.ThrowsType != null && _enumTypes.TryGetValue(method.ThrowsType, out var errorEnumType) && errorEnumType is MaxonEnumType errorEnum) {
					returnType = new MaxonErrorUnionType(returnType, errorEnum);
				}
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

		// Lower methods from types (skip generic types - they are handled during monomorphization)
		foreach (var type in program.Types) {
			// Skip generic types - methods will be created when the type is instantiated
			if (type.GenericParams.Count > 0) continue;

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
		// If this is a generic type (has type parameters), store it for later monomorphization
		if (type.GenericParams.Count > 0) {
			Logger.Debug(LogCategory.Mlir, $"  Storing generic type: {type.Name} with params: {string.Join(", ", type.GenericParams)}");
			_genericTypes[type.Name] = type;
			return;
		}

		var instanceFields = new List<(string name, MlirType type, bool isMutable, Expr? defaultValue)>();

		foreach (var field in type.Fields) {
			MlirType fieldType;
			if (field.Type != null) {
				fieldType = ConvertTypeRef(field.Type);
			} else if (field.DefaultValue != null) {
				fieldType = InferTypeFromExpr(field.DefaultValue);
			} else {
				throw new InvalidOperationException($"Field '{field.Name}' has no type annotation and no default value");
			}

			if (field.IsStatic) {
				// Static fields are registered as globals with name "TypeName.fieldName"
				LowerStaticField(type.Name, field.Name, fieldType, field.DefaultValue, isConstant: !field.IsMutable);
			} else {
				instanceFields.Add((field.Name, fieldType, field.IsMutable, field.DefaultValue));
			}
		}

		RegisterStruct(type.Name, instanceFields);
	}

	private void LowerStaticField(string typeName, string fieldName, MlirType fieldType, Expr? defaultValue, bool isConstant) {
		var globalName = $"{typeName}.{fieldName}";

		MlirAttribute? initValue;
		if (defaultValue != null) {
			var (_, attr) = EvaluateConstantExpr(defaultValue);
			initValue = attr;
		} else {
			// Default initialize to zero
			initValue = fieldType switch {
				IntegerType => new IntegerAttr(0),
				FloatType => new FloatAttr(0.0),
				_ => throw new NotSupportedException($"Cannot default-initialize static field of type: {fieldType}")
			};
		}

		var global = new MlirGlobal($"global.{globalName}", fieldType) {
			IsConstant = isConstant,
			InitValue = initValue
		};
		_module.Globals.Add(global);
		_globals[globalName] = global;
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
		LowerFunctionBody(func.Name, parameters, func.ReturnType, func.ThrowsType, func.Body);
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
			LowerFunctionBody($"{typeName}.{method.Name}", parameters, method.ReturnType, method.ThrowsType, method.Body);
		} finally {
			_currentMethodStructType = null;
		}
	}

	private void LowerFunctionBody(string name, List<(string name, string type)> parameters, TypeRef? returnType, string? throwsType, List<Stmt> body) {
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

		// Set up error union tracking if this function throws
		_currentFunctionErrorUnionType = null;
		_currentFunctionSuccessType = null;
		if (throwsType != null) {
			// Get the error enum type
			if (!_enumTypes.TryGetValue(throwsType, out var errorEnumType) || errorEnumType is not MaxonEnumType errorEnum) {
				throw new CompileError(ErrorCode.MlirUndefinedType,
					$"Unknown error type: {throwsType}");
			}

			// Determine the success type (what the function returns on success)
			_currentFunctionSuccessType = returnMlirType ?? NoneType.Instance;

			// Create the error union type
			_currentFunctionErrorUnionType = new MaxonErrorUnionType(_currentFunctionSuccessType, errorEnum);

			// The actual return type of the function is now the error union
			returnMlirType = _currentFunctionErrorUnionType;
			returnTypeStr = "error_union"; // Signal that we're returning an error union
		}

		// For struct returns, use sret calling convention (hidden pointer parameter)
		if (returnMlirType is MaxonStructType structType) {
			BeginFunction(name, parameters, "void", sretType: structType);
		} else if (_currentFunctionErrorUnionType != null) {
			// Error union returns are handled via sret since they can be large
			BeginFunction(name, parameters, "void", sretType: null, errorUnionType: _currentFunctionErrorUnionType);
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

		// Clear error union tracking
		_currentFunctionErrorUnionType = null;
		_currentFunctionSuccessType = null;
	}

	private static string GetTypeString(TypeRef? typeRef) {
		return typeRef switch {
			null => "void",
			SimpleTypeRef simple => simple.Name,
			GenericTypeRef generic => $"{generic.BaseName}${string.Join("$", generic.TypeArgs)}",
			FunctionTypeRef func => $"fn({string.Join(",", func.ParamTypes.Select(GetTypeString))})->{GetTypeString(func.ReturnType)}",
			_ => throw new NotSupportedException($"Unsupported type ref: {typeRef}")
		};
	}

	private MlirType ConvertTypeRef(TypeRef typeRef) {
		return typeRef switch {
			SimpleTypeRef simple => ConvertType(simple.Name),
			GenericTypeRef generic => GetOrCreateMonomorphizedType(generic.BaseName, generic.TypeArgs),
			FunctionTypeRef func => ConvertFunctionTypeRef(func),
			_ => throw new NotSupportedException($"Unsupported type ref: {typeRef}")
		};
	}

	private FunctionPtrType ConvertFunctionTypeRef(FunctionTypeRef funcType) {
		var paramTypes = funcType.ParamTypes.Select(ConvertTypeRef).ToList();
		var returnType = funcType.ReturnType != null ? ConvertTypeRef(funcType.ReturnType) : NoneType.Instance;
		return new FunctionPtrType(paramTypes, returnType);
	}

	private void LowerStatement(Stmt stmt) {
		switch (stmt) {
			case ReturnStmt ret:
				// Handle returns in throwing functions - wrap value in error union
				if (_currentFunctionErrorUnionType != null) {
					if (_errorUnionReturnPtr != null) {
						// Write directly to the return pointer: { tag: i8, padding: [7]i8, value: T }
						// Store tag = 0 (success) at offset 0
						var tagValue = EmitConstantI8(0);
						var tagStore = new StoreOp(tagValue, _errorUnionReturnPtr);
						InsertOp(tagStore);

						// If there's a return value, store it at offset 8
						if (ret.Value is not null) {
							var value = LowerExpression(ret.Value, _currentFunctionSuccessType);
							var valueOffset = EmitConstantIndex(8);
							var valueStore = new StoreOp(value, _errorUnionReturnPtr, valueOffset);
							InsertOp(valueStore);
						}
					}
					EmitReturn();
					break;
				}

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

			case ThrowStmt throwStmt:
				LowerThrowStmt(throwStmt);
				break;

			default:
				throw new NotSupportedException($"Unsupported statement: {stmt.GetType().Name}");
		}
	}

	private void LowerThrowStmt(ThrowStmt throwStmt) {
		if (_currentFunctionErrorUnionType == null) {
			throw new CompileError(ErrorCode.SemanticTypeMismatch,
				"Cannot throw outside of a function that declares 'throws'",
				throwStmt.Location?.Line, throwStmt.Location?.Column);
		}

		// Evaluate the error expression (should be an enum variant like ArrayError.indexOutOfBounds)
		var errorValue = LowerExpression(throwStmt.ErrorExpr);

		// Write error union directly to the return pointer: { tag: i8, padding: [7]i8, error: E }
		if (_errorUnionReturnPtr != null) {
			// Store tag = 1 (error) at offset 0
			var tagValue = EmitConstantI8(1);
			var tagStore = new StoreOp(tagValue, _errorUnionReturnPtr);
			InsertOp(tagStore);

			// Store error value at offset 8
			var errorOffset = EmitConstantIndex(8);
			var errorStore = new StoreOp(errorValue, _errorUnionReturnPtr, errorOffset);
			InsertOp(errorStore);
		}

		EmitReturn();
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
		// Use _currentBlock, not thenBlock - nested constructs (while, if) change current block
		if (_currentBlock is not null && !_currentBlock.IsTerminated) {
			EmitBranch(mergeBlock);
		}

		// Else block
		if (ifStmt.ElseBody is not null && elseBlock is not null) {
			SetInsertionPoint(elseBlock);
			foreach (var stmt in ifStmt.ElseBody) {
				LowerStatement(stmt);
			}
			// Use _currentBlock, not elseBlock - nested constructs change current block
			if (_currentBlock is not null && !_currentBlock.IsTerminated) {
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
			SelfExpr => GetVariable("self"),
			BinaryExpr binary => LowerBinaryExpr(binary),
			CompareExpr compare => LowerCompareExpr(compare),
			LogicalExpr logical => LowerLogicalExpr(logical),
			UnaryExpr unary => LowerUnaryExpr(unary),
			CallExpr call => LowerCallExpr(call),
			MethodCallExpr methodCall => LowerMethodCallExpr(methodCall),
			StaticCallExpr staticCall => LowerStaticCallExpr(staticCall),
			StructInitExpr structInit => LowerStructInitExpr(structInit, expectedType),
			FieldAccessExpr fieldAccess => LowerFieldAccessExpr(fieldAccess),
			CastExpr cast => LowerCastExpr(cast),
			TryExpr tryExpr => LowerTryExpr(tryExpr),
			EnumCaseExpr enumCase => LowerEnumCaseExpr(enumCase),
			ArrayLiteralExpr arrayLit => LowerArrayLiteralExpr(arrayLit),
			_ => throw new NotSupportedException($"Unsupported expression: {expr.GetType().Name}")
		};

		// Apply implicit type coercion if needed (e.g., int to float)
		if (expectedType != null && result != null && result.Type != expectedType) {
			result = EmitTypeCoercion(result, expectedType);
		}

		return result!;
	}

	/// <summary>
	/// Emits type coercion operations for implicit conversions.
	/// </summary>
	private MlirValue EmitTypeCoercion(MlirValue value, MlirType targetType) {
		// Int to float conversion
		if (value.Type is IntegerType && targetType is FloatType floatType) {
			var op = new SIToFPOp(value, floatType);
			InsertOp(op);
			return op.Result;
		}

		// Integer width extension/truncation
		if (value.Type is IntegerType srcIntType && targetType is IntegerType dstIntType) {
			if (srcIntType.BitWidth < dstIntType.BitWidth) {
				// Extension
				var op = srcIntType.IsSigned
					? (MlirOperation)new ExtSIOp(value, dstIntType)
					: new ExtUIOp(value, dstIntType);
				InsertOp(op);
				return ((dynamic)op).Result;
			} else if (srcIntType.BitWidth > dstIntType.BitWidth) {
				// Truncation
				var op = new TruncIOp(value, dstIntType);
				InsertOp(op);
				return op.Result;
			}
		}

		// No coercion needed or not supported - return as-is
		return value;
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

	private MlirValue LowerCastExpr(CastExpr cast) {
		var operand = LowerExpression(cast.Expression);
		var targetType = ConvertType(cast.TargetType);

		if (operand.Type == targetType) {
			throw new CompileError(ErrorCode.SemanticTypeMismatch,
				$"redundant cast: expression is already of type '{cast.TargetType}'",
				cast.Expression.Location?.Line, cast.Expression.Location?.Column);
		}

		// Use existing coercion logic which handles int->float, int width changes, etc.
		return EmitTypeCoercion(operand, targetType);
	}

	private MlirValue LowerEnumCaseExpr(EnumCaseExpr enumCase) {
		// Look up the enum type
		if (!_enumTypes.TryGetValue(enumCase.EnumName, out var enumType) || enumType is not MaxonEnumType maxonEnumType) {
			throw new CompileError(ErrorCode.MlirUndefinedType,
				$"Unknown enum type: {enumCase.EnumName}",
				enumCase.Location?.Line, enumCase.Location?.Column);
		}

		// Verify the case exists
		var variant = maxonEnumType.GetVariant(enumCase.CaseName) ?? throw new CompileError(ErrorCode.SemanticUndefinedVariable,
				$"Enum '{enumCase.EnumName}' has no case '{enumCase.CaseName}'",
				enumCase.Location?.Line, enumCase.Location?.Column);

		// Lower any associated values (args)
		var payloadValues = enumCase.Args.Select(arg => LowerExpression(arg)).ToList();

		// Create the enum init op
		var enumInitOp = new EnumInitOp(maxonEnumType, enumCase.CaseName, payloadValues);
		InsertOp(enumInitOp);

		return enumInitOp.Result;
	}

	private MlirValue LowerTryExpr(TryExpr tryExpr) {
		// The inner expression must be a call to a throwing function
		// For now, we evaluate the expression which will return an error union
		var errorUnionValue = LowerExpression(tryExpr.Expression);

		if (errorUnionValue.Type is not MaxonErrorUnionType errorUnionType) {
			throw new CompileError(ErrorCode.SemanticTypeMismatch,
				"'try' can only be used with expressions that return error unions (calls to throwing functions)",
				tryExpr.Location?.Line, tryExpr.Location?.Column);
		}

		// Check if this is an error
		var isErrorOp = new ErrorUnionIsErrorOp(errorUnionValue);
		InsertOp(isErrorOp);

		// Allocate result storage BEFORE branching (so it's visible in both branches)
		var resultAlloca = EmitAlloca(errorUnionType.SuccessType, "try.result");

		var successBlock = CreateBlock("try.success");
		var errorBlock = CreateBlock("try.error");
		var mergeBlock = CreateBlock("try.merge");

		// Branch based on whether it's an error
		EmitCondBranch(isErrorOp.Result, errorBlock, successBlock);

		// Success block - extract the value
		SetInsertionPoint(successBlock);
		var getValueOp = new ErrorUnionGetValueOp(errorUnionValue, errorUnionType.SuccessType);
		InsertOp(getValueOp);

		// Store success value for merge block
		var storeSuccess = new StoreOp(getValueOp.Result, resultAlloca);
		InsertOp(storeSuccess);
		EmitBranch(mergeBlock);

		// Error block - handle the otherwise clause
		SetInsertionPoint(errorBlock);

		if (tryExpr.Otherwise is null) {
			// No otherwise clause - propagate the error by rethrowing
			// This only works if we're inside a throwing function with compatible error type
			if (_currentFunctionErrorUnionType != null) {
				// Re-throw: store the error union to return ptr and return
				if (_errorUnionReturnPtr != null) {
					var storeOp = new StoreOp(errorUnionValue, _errorUnionReturnPtr);
					InsertOp(storeOp);
				}
				EmitReturn();
			} else {
				throw new CompileError(ErrorCode.SemanticTypeMismatch,
					"'try' without 'otherwise' can only be used in functions that throw",
					tryExpr.Location?.Line, tryExpr.Location?.Column);
			}
		} else {
			// Handle the otherwise clause
			switch (tryExpr.Otherwise.Mode) {
				case OtherwiseMode.DefaultExpr:
					// otherwise defaultExpr - use the default value
					var defaultValue = LowerExpression(tryExpr.Otherwise.DefaultExpr!, errorUnionType.SuccessType);
					var storeDefault = new StoreOp(defaultValue, resultAlloca);
					InsertOp(storeDefault);
					EmitBranch(mergeBlock);
					break;

				case OtherwiseMode.Ignore:
					// otherwise ignore - return a zero/default value
					var zeroValue = EmitDefaultValue(errorUnionType.SuccessType);
					var storeZero = new StoreOp(zeroValue, resultAlloca);
					InsertOp(storeZero);
					EmitBranch(mergeBlock);
					break;

				case OtherwiseMode.Block:
					// otherwise 'block' ... end 'block' - execute the block
					foreach (var stmt in tryExpr.Otherwise.Body!) {
						LowerStatement(stmt);
						if (_currentBlock?.IsTerminated == true) break;
					}
					// After block, we need a value - this is typically used with return inside
					if (_currentBlock is not null && !_currentBlock.IsTerminated) {
						var blockZeroValue = EmitDefaultValue(errorUnionType.SuccessType);
						var storeBlockZero = new StoreOp(blockZeroValue, resultAlloca);
						InsertOp(storeBlockZero);
						EmitBranch(mergeBlock);
					}
					break;

				case OtherwiseMode.BlockWithErr:
					// otherwise (e) 'block' ... end 'block' - execute block with error binding
					var getErrorOp = new ErrorUnionGetErrorOp(errorUnionValue, errorUnionType.ErrorType);
					InsertOp(getErrorOp);

					// Bind the error to the variable name
					var errorBinding = tryExpr.Otherwise.ErrorBinding!;
					var errorSlot = DeclareVariable(errorBinding, errorUnionType.ErrorType);
					var storeError = new StoreOp(getErrorOp.Result, errorSlot);
					InsertOp(storeError);

					foreach (var stmt in tryExpr.Otherwise.Body!) {
						LowerStatement(stmt);
						if (_currentBlock?.IsTerminated == true) break;
					}
					if (_currentBlock is not null && !_currentBlock.IsTerminated) {
						var blockWithErrZeroValue = EmitDefaultValue(errorUnionType.SuccessType);
						var storeBlockWithErrZero = new StoreOp(blockWithErrZeroValue, resultAlloca);
						InsertOp(storeBlockWithErrZero);
						EmitBranch(mergeBlock);
					}
					break;
			}
		}

		// Merge block - load the result
		SetInsertionPoint(mergeBlock);
		var loadResult = new LoadOp(resultAlloca);
		InsertOp(loadResult);

		return loadResult.Result;
	}

	private MlirValue EmitDefaultValue(MlirType type) {
		return type switch {
			IntegerType => EmitConstantInt(0),
			FloatType => EmitConstantFloat(0.0),
			_ => EmitConstantInt(0) // Fallback for struct types - will need proper handling
		};
	}

	private MlirValue? LowerCallExpr(CallExpr call) {
		// Handle builtin functions first
		var builtinResult = TryLowerBuiltinCall(call);
		if (builtinResult != null) {
			return builtinResult;
		}

		// Handle managed memory intrinsics (for Array implementation)
		var intrinsicResult = TryLowerManagedMemoryIntrinsic(call);
		if (intrinsicResult != null) {
			return intrinsicResult;
		}

		// Check for sibling method call (calling another method of same type without self.)
		// If we're inside a method and the function name matches a method of the current type,
		// treat it as self.methodName()
		if (_currentMethodStructType != null && _namedValues.TryGetValue("self", out var selfBinding)) {
			var siblingMethodName = $"{_currentMethodStructType.Name}.{call.FuncName}";
			if (_functionParams.ContainsKey(siblingMethodName)) {
				Logger.Trace(LogCategory.Mlir, $"LowerCallExpr: resolved sibling method call {call.FuncName} -> {siblingMethodName}");

				// Get the self value from binding
				var selfValue = selfBinding switch {
					VariableBinding.Direct d => d.Value,
					VariableBinding.StackSlot s => s.Address,
					_ => throw new InvalidOperationException("Unknown VariableBinding variant for self")
				};

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

					var loweredArg = LowerExpression(arg, expectedType);
					methodArgs.Add(loweredArg);

					// Determine ownership based on type and mutation analysis (offset by 1 for 'self').
					var argOwnership = ArgumentSemantics.DetermineOwnership(loweredArg, siblingMethodName, i + 1, _mutationAnalyzer);
					methodOwnership.Add(argOwnership.Ownership);
				}

				// Look up the method's return type
				MlirType? methodReturnType = _functionReturnTypes.TryGetValue(siblingMethodName, out var mrt) ? mrt : IntegerType.I64;

				// Ensure monomorphized method is lowered on-demand (for sibling method calls)
				EnsureMonomorphizedMethodLowered(_currentMethodStructType.Name, call.FuncName);

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

			var loweredArg = LowerExpression(arg, expectedType);
			args.Add(loweredArg);

			// Determine ownership based on type and mutation analysis.
			var argOwnership = ArgumentSemantics.DetermineOwnership(loweredArg, call.FuncName, i, _mutationAnalyzer);
			ownership.Add(argOwnership.Ownership);
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

			var loweredArg = LowerExpression(arg, expectedType);
			args.Add(loweredArg);

			// Determine ownership based on type and mutation analysis (offset by 1 for 'self').
			var argOwnership = ArgumentSemantics.DetermineOwnership(loweredArg, methodName, i + 1, _mutationAnalyzer);
			ownership.Add(argOwnership.Ownership);
		}

		// Look up the method's return type
		MlirType? returnType = _functionReturnTypes.TryGetValue(methodName, out var rt) ? rt : IntegerType.I64;

		// Ensure monomorphized method is lowered on-demand
		EnsureMonomorphizedMethodLowered(structType.Name, methodCall.MethodName);

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
		if (_namedValues.ContainsKey(staticCall.TypeName)) {
			// This is actually an instance method call: variable.method(args)
			baseValue = GetVariable(staticCall.TypeName);
			if (baseValue.Type is MaxonStructType st) {
				structType = st;
			}
		} else if (_currentMethodStructType != null) {
			// Check if TypeName is a field of the current method's struct (e.g., inner.get() inside a method)
			var fieldInfo = _currentMethodStructType.GetField(staticCall.TypeName);
			if (fieldInfo != null && _namedValues.TryGetValue("self", out var selfBinding)) {
				// Get self value from binding
				var selfValue = selfBinding switch {
					VariableBinding.Direct d => d.Value,
					VariableBinding.StackSlot s => s.Address,
					_ => throw new InvalidOperationException("Unknown VariableBinding variant for self")
				};
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

				var loweredArg = LowerExpression(arg, expectedType);
				args.Add(loweredArg);

				// Determine ownership based on type and mutation analysis (offset by 1 for 'self').
				var argOwnership = ArgumentSemantics.DetermineOwnership(loweredArg, methodName, i + 1, _mutationAnalyzer);
				ownership.Add(argOwnership.Ownership);
			}

			// Look up the method's return type
			MlirType? returnType = _functionReturnTypes.TryGetValue(methodName, out var rt) ? rt : IntegerType.I64;

			// Ensure monomorphized method is lowered on-demand
			EnsureMonomorphizedMethodLowered(structType.Name, staticCall.MemberName);

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

			var loweredArg = LowerExpression(arg, expectedType);
			staticArgs.Add(loweredArg);

			// Determine ownership based on type and mutation analysis.
			var argOwnership = ArgumentSemantics.DetermineOwnership(loweredArg, staticMethodName, i, _mutationAnalyzer);
			staticOwnership.Add(argOwnership.Ownership);
		}

		// Look up the method's return type
		MlirType? staticReturnType = _functionReturnTypes.TryGetValue(staticMethodName, out var srt) ? srt : IntegerType.I64;

		// Ensure monomorphized method is lowered on-demand (for static methods on generic types)
		EnsureMonomorphizedMethodLowered(staticCall.TypeName, staticCall.MemberName);

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
		return Emit<TruncOp>(arg, IntegerType.I64);
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

	// Sentinel value to indicate a void intrinsic was handled (distinguished from "not an intrinsic" which is null)
	private static readonly MlirValue VoidIntrinsicHandled = new(-999, NoneType.Instance);

	/// <summary>
	/// Attempts to lower a managed memory intrinsic call. Returns null if not an intrinsic.
	/// Returns VoidIntrinsicHandled for void intrinsics.
	/// These intrinsics are used by Array.maxon for memory management.
	/// </summary>
	private MlirValue? TryLowerManagedMemoryIntrinsic(CallExpr call) {
		// Get the __ManagedMemory type
		if (!_structTypes.TryGetValue("__ManagedMemory", out var managedMemType) ||
			managedMemType is not MaxonStructType managedMemoryType) {
			return null;
		}

		// Determine element type from context (for generic Array methods)
		MlirType GetElementType() {
			// When inside a monomorphized Array method, _currentMethodStructType has the element type info
			// For Array$int, extract "int" from the name
			if (_currentMethodStructType != null && _currentMethodStructType.Name.StartsWith("Array$")) {
				var elemTypeName = _currentMethodStructType.Name["Array$".Length..];
				return ConvertType(elemTypeName);
			}
			// Default to i64 if we can't determine the type
			return IntegerType.I64;
		}

		// For mutating operations (grow, set_at, set_length, shift), get a pointer to the managed memory
		// instead of loading its value, so modifications persist.
		MlirValue GetManagedMemoryPointer(Expr arg) {
			// Case 1: Identifier that's a field on self (inside a method)
			if (arg is IdentifierExpr ident && _currentMethodStructType != null) {
				var fieldInfo = _currentMethodStructType.GetField(ident.Name);
				if (fieldInfo != null && fieldInfo.Type is MaxonStructType fieldStructType
					&& fieldStructType.Name == "__ManagedMemory"
					&& _namedValues.TryGetValue("self", out var selfBinding)) {
					// Get self value from binding
					var selfValue = selfBinding switch {
						VariableBinding.Direct d => d.Value,
						VariableBinding.StackSlot s => s.Address,
						_ => throw new InvalidOperationException("Unknown VariableBinding variant for self")
					};
					// Emit field_ptr to get the address of the managed memory field
					Logger.Debug(LogCategory.Mlir, $"GetManagedMemoryPointer: Case 1 - self.{ident.Name}");
					return EmitFieldPtr(selfValue, ident.Name, fieldInfo.Offset, fieldInfo.Type);
				}
			}

			// Case 2: Field access on a local variable (e.g., test.managed)
			if (arg is FieldAccessExpr fieldAccess) {
				// Get the struct type to find the field info
				if (fieldAccess.Base is IdentifierExpr baseIdent) {
					Logger.Debug(LogCategory.Mlir, $"GetManagedMemoryPointer: Case 2 - checking {baseIdent.Name}.{fieldAccess.FieldName}");
					// Get the base variable's address (not its value)
					if (_namedValues.TryGetValue(baseIdent.Name, out var baseBinding) && baseBinding is VariableBinding.StackSlot slot) {
						var baseAddr = slot.Address;
						Logger.Debug(LogCategory.Mlir, $"  Found {baseIdent.Name} with type {baseAddr.Type}");
						if (baseAddr.Type is MemRefType memRef) {
							Logger.Debug(LogCategory.Mlir, $"  MemRefType element: {memRef.ElementType}");
							if (memRef.ElementType is MaxonStructType structType) {
								var fieldInfo = structType.GetField(fieldAccess.FieldName);
								Logger.Debug(LogCategory.Mlir, $"  Field {fieldAccess.FieldName}: {fieldInfo?.Type}");
								if (fieldInfo != null && fieldInfo.Type is MaxonStructType fieldStructType
									&& fieldStructType.Name == "__ManagedMemory") {
									Logger.Debug(LogCategory.Mlir, $"  SUCCESS - loading struct and emitting FieldPtr at offset {fieldInfo.Offset}");
									// Load the struct value (pointer to struct) from the variable
									var structValue = EmitLoad(baseAddr);
									// Emit field_ptr to get the address of the managed memory field
									return EmitFieldPtr(structValue, fieldAccess.FieldName, fieldInfo.Offset, fieldInfo.Type);
								}
							}
						}
					} else {
						Logger.Debug(LogCategory.Mlir, $"  Variable {baseIdent.Name} not found in _namedValues or not a StackSlot");
					}
				}
			}

			// Case 3: Direct identifier for a standalone __ManagedMemory variable
			if (arg is IdentifierExpr standaloneIdent) {
				if (_namedValues.TryGetValue(standaloneIdent.Name, out var standaloneBinding) && standaloneBinding is VariableBinding.StackSlot standaloneSlot && standaloneSlot.Address.Type is MemRefType memRef) {
					if (memRef.ElementType is MaxonStructType structType && structType.Name == "__ManagedMemory") {
						Logger.Debug(LogCategory.Mlir, $"GetManagedMemoryPointer: Case 3 - loading standalone {standaloneIdent.Name}");
						// Load the pointer to ManagedMemory from the variable
						// The variable stores a pointer to the ManagedMemory struct (from __managed_memory_create)
						return EmitLoad(standaloneSlot.Address);
					}
				}
			}

			// Fallback: lower normally (but this will be a copy - may not work correctly for mutations)
			Logger.Debug(LogCategory.Mlir, $"GetManagedMemoryPointer: FALLBACK for {arg.GetType().Name}");
			return LowerExpression(arg);
		}

		return call.FuncName switch {
			"__managed_memory_create" => LowerManagedMemoryCreate(call, managedMemoryType),
			// All managed memory operations use ByRef pattern - the struct is too large to pass by value
			"__managed_memory_len" => LowerManagedMemoryLenByRef(call, GetManagedMemoryPointer),
			"__managed_memory_capacity" => LowerManagedMemoryCapacityByRef(call, GetManagedMemoryPointer),
			"__managed_memory_get_unchecked" => LowerManagedMemoryGetUncheckedByRef(call, GetElementType(), GetManagedMemoryPointer),
			"__managed_memory_set_at" => LowerManagedMemorySetAtByRef(call, GetManagedMemoryPointer),
			"__managed_memory_grow" => LowerManagedMemoryGrowByRef(call, GetElementType(), GetManagedMemoryPointer),
			"__managed_memory_set_length" => LowerManagedMemorySetLengthByRef(call, GetManagedMemoryPointer),
			"__managed_memory_shift_right" => LowerManagedMemoryShiftRightByRef(call, GetElementType(), GetManagedMemoryPointer),
			"__managed_memory_shift_left" => LowerManagedMemoryShiftLeftByRef(call, GetElementType(), GetManagedMemoryPointer),
			"__element_size" => LowerElementSize(GetElementType()),
			_ => null
		};
	}

	/// <summary>
	/// Emits a field_ptr operation to get the address of a field.
	/// </summary>
	private MlirValue EmitFieldPtr(MlirValue structValue, string fieldName, int offset, MlirType fieldType) {
		var op = new FieldPtrOp(structValue, fieldName, offset, fieldType);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemoryCreate(CallExpr call, MaxonStructType managedMemoryType) {
		if (call.Args.Count != 2)
			throw new ArgumentException("__managed_memory_create requires 2 arguments (count, elemSize)");
		var count = LowerExpression(call.Args[0]);
		var elemSize = LowerExpression(call.Args[1]);
		var op = new ManagedMemoryCreateOp(count, elemSize, managedMemoryType);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemoryLen(CallExpr call) {
		if (call.Args.Count != 1)
			throw new ArgumentException("__managed_memory_len requires 1 argument (managed)");
		var mem = LowerExpression(call.Args[0]);
		var op = new ManagedMemoryLenOp(mem);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemoryCapacity(CallExpr call) {
		if (call.Args.Count != 1)
			throw new ArgumentException("__managed_memory_capacity requires 1 argument (managed)");
		var mem = LowerExpression(call.Args[0]);
		var op = new ManagedMemoryCapacityOp(mem);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemoryGetUnchecked(CallExpr call, MlirType elementType) {
		if (call.Args.Count != 2)
			throw new ArgumentException("__managed_memory_get_unchecked requires 2 arguments (managed, index)");
		var mem = LowerExpression(call.Args[0]);
		var index = LowerExpression(call.Args[1]);
		var op = new ManagedMemoryGetUncheckedOp(mem, index, elementType);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemorySetAt(CallExpr call) {
		if (call.Args.Count != 3)
			throw new ArgumentException("__managed_memory_set_at requires 3 arguments (managed, index, value)");
		var mem = LowerExpression(call.Args[0]);
		var index = LowerExpression(call.Args[1]);
		var value = LowerExpression(call.Args[2]);
		var op = new ManagedMemorySetAtOp(mem, index, value);
		InsertOp(op);
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemoryGrow(CallExpr call, MlirType elementType) {
		if (call.Args.Count != 2)
			throw new ArgumentException("__managed_memory_grow requires 2 arguments (managed, newCapacity)");
		var mem = LowerExpression(call.Args[0]);
		var newCapacity = LowerExpression(call.Args[1]);
		var op = new ManagedMemoryGrowOp(mem, newCapacity, elementType);
		InsertOp(op);
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemorySetLength(CallExpr call) {
		if (call.Args.Count != 2)
			throw new ArgumentException("__managed_memory_set_length requires 2 arguments (managed, newLen)");
		var mem = LowerExpression(call.Args[0]);
		var newLength = LowerExpression(call.Args[1]);
		var op = new ManagedMemorySetLengthOp(mem, newLength);
		InsertOp(op);
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemoryShiftRight(CallExpr call, MlirType elementType) {
		if (call.Args.Count != 3)
			throw new ArgumentException("__managed_memory_shift_right requires 3 arguments (managed, startIndex, count)");
		var mem = LowerExpression(call.Args[0]);
		var startIndex = LowerExpression(call.Args[1]);
		var count = LowerExpression(call.Args[2]);
		var op = new ManagedMemoryShiftRightOp(mem, startIndex, count, elementType);
		InsertOp(op);
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemoryShiftLeft(CallExpr call, MlirType elementType) {
		if (call.Args.Count != 3)
			throw new ArgumentException("__managed_memory_shift_left requires 3 arguments (managed, startIndex, count)");
		var mem = LowerExpression(call.Args[0]);
		var startIndex = LowerExpression(call.Args[1]);
		var count = LowerExpression(call.Args[2]);
		var op = new ManagedMemoryShiftLeftOp(mem, startIndex, count, elementType);
		InsertOp(op);
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerElementSize(MlirType elementType) {
		var op = new ElementSizeOp(elementType);
		InsertOp(op);
		return op.Result;
	}

	// ByRef versions of managed memory intrinsics - these take a pointer to the managed memory
	// because __ManagedMemory is a 32-byte struct that cannot be passed by value in registers.

	private MlirValue LowerManagedMemoryLenByRef(CallExpr call, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 1)
			throw new ArgumentException("__managed_memory_len requires 1 argument (managed)");
		var memPtr = getManagedPointer(call.Args[0]);
		var op = new ManagedMemoryLenOp(memPtr);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemoryCapacityByRef(CallExpr call, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 1)
			throw new ArgumentException("__managed_memory_capacity requires 1 argument (managed)");
		var memPtr = getManagedPointer(call.Args[0]);
		var op = new ManagedMemoryCapacityOp(memPtr);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemoryGetUncheckedByRef(CallExpr call, MlirType elementType, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 2)
			throw new ArgumentException("__managed_memory_get_unchecked requires 2 arguments (managed, index)");
		var memPtr = getManagedPointer(call.Args[0]);
		var index = LowerExpression(call.Args[1]);
		var op = new ManagedMemoryGetUncheckedOp(memPtr, index, elementType);
		InsertOp(op);
		return op.Result;
	}

	private MlirValue LowerManagedMemorySetAtByRef(CallExpr call, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 3)
			throw new ArgumentException("__managed_memory_set_at requires 3 arguments (managed, index, value)");
		var memPtr = getManagedPointer(call.Args[0]);
		var index = LowerExpression(call.Args[1]);
		var value = LowerExpression(call.Args[2]);
		// Pass pointer directly - lowering pattern uses FieldPtrOp which expects a pointer
		var op = new ManagedMemorySetAtOp(memPtr, index, value);
		InsertOp(op);
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemoryGrowByRef(CallExpr call, MlirType elementType, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 2)
			throw new ArgumentException("__managed_memory_grow requires 2 arguments (managed, newCapacity)");
		var memPtr = getManagedPointer(call.Args[0]);
		var newCapacity = LowerExpression(call.Args[1]);

		// For grow, we need to pass the pointer directly so the lowering can update the buffer pointer
		// Create a new op type that takes a pointer
		if (memPtr.Type is PtrType) {
			var op = new ManagedMemoryGrowByRefOp(memPtr, newCapacity, elementType);
			InsertOp(op);
		} else {
			// Fallback to value-based (modifications will be lost)
			var op = new ManagedMemoryGrowOp(memPtr, newCapacity, elementType);
			InsertOp(op);
		}
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemorySetLengthByRef(CallExpr call, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 2)
			throw new ArgumentException("__managed_memory_set_length requires 2 arguments (managed, newLen)");
		var memPtr = getManagedPointer(call.Args[0]);
		var newLength = LowerExpression(call.Args[1]);

		// For set_length, we need to modify the actual struct
		if (memPtr.Type is PtrType) {
			var op = new ManagedMemorySetLengthByRefOp(memPtr, newLength);
			InsertOp(op);
		} else {
			var op = new ManagedMemorySetLengthOp(memPtr, newLength);
			InsertOp(op);
		}
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemoryShiftRightByRef(CallExpr call, MlirType elementType, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 3)
			throw new ArgumentException("__managed_memory_shift_right requires 3 arguments (managed, startIndex, count)");
		var memPtr = getManagedPointer(call.Args[0]);
		var startIndex = LowerExpression(call.Args[1]);
		var count = LowerExpression(call.Args[2]);
		// Pass pointer directly - lowering pattern uses FieldPtrOp which expects a pointer
		var op = new ManagedMemoryShiftRightOp(memPtr, startIndex, count, elementType);
		InsertOp(op);
		return VoidIntrinsicHandled;
	}

	private MlirValue LowerManagedMemoryShiftLeftByRef(CallExpr call, MlirType elementType, Func<Expr, MlirValue> getManagedPointer) {
		if (call.Args.Count != 3)
			throw new ArgumentException("__managed_memory_shift_left requires 3 arguments (managed, startIndex, count)");
		var memPtr = getManagedPointer(call.Args[0]);
		var startIndex = LowerExpression(call.Args[1]);
		var count = LowerExpression(call.Args[2]);
		// Pass pointer directly - lowering pattern uses FieldPtrOp which expects a pointer
		var op = new ManagedMemoryShiftLeftOp(memPtr, startIndex, count, elementType);
		InsertOp(op);
		return VoidIntrinsicHandled;
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
			if (_structTypes.TryGetValue(structInit.TypeName, out var mlirType) && mlirType is MaxonStructType st) {
				// Found as a concrete struct type
				structType = st;
			} else if (_genericTypes.TryGetValue(structInit.TypeName, out var genericType)) {
				// This is a generic type - infer type arguments from field values
				structType = InferAndCreateMonomorphizedType(genericType, structInit);
			} else {
				throw new CompileError(ErrorCode.MlirUndefinedType, $"Unknown struct type: {structInit.TypeName}",
					structInit.Location?.Line, structInit.Location?.Column);
			}
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

	/// <summary>
	/// Lowers an array literal like [1, 2, 3] to managed memory allocation and Array creation.
	/// </summary>
	private MlirValue LowerArrayLiteralExpr(ArrayLiteralExpr arrayLit) {
		if (arrayLit.Elements.Count == 0) {
			throw new CompileError(ErrorCode.MlirUndefinedType,
				"Empty array literals are not yet supported - use explicit type annotation",
				arrayLit.Location?.Line, arrayLit.Location?.Column);
		}

		// Get the __ManagedMemory type
		if (!_structTypes.TryGetValue("__ManagedMemory", out var managedMemType) ||
			managedMemType is not MaxonStructType managedMemoryType) {
			throw new InvalidOperationException("__ManagedMemory type not registered");
		}

		// Infer element type from the first element
		var firstElem = LowerExpression(arrayLit.Elements[0]);
		var elementType = firstElem.Type;
		var elemTypeName = elementType switch {
			IntegerType { BitWidth: 64, IsSigned: true } => "int",
			IntegerType { BitWidth: 32, IsSigned: true } => "int32",
			IntegerType { BitWidth: 1 } => "bool",
			FloatType { BitWidth: 64 } => "float",
			FloatType { BitWidth: 32 } => "float32",
			MaxonStructType st => st.Name,
			_ => throw new NotSupportedException($"Cannot infer array element type from: {elementType}")
		};

		// Get or create the monomorphized Array type for this element type
		var arrayType = GetOrCreateMonomorphizedType("Array", [elemTypeName]);

		// Create managed memory for count elements
		var countConst = EmitConstantInt(arrayLit.Elements.Count);
		var elemSizeConst = EmitConstantInt(elementType.SizeInBytes);
		var createOp = new ManagedMemoryCreateOp(countConst, elemSizeConst, managedMemoryType);
		InsertOp(createOp);
		var managedMem = createOp.Result;

		// Store each element
		for (int i = 0; i < arrayLit.Elements.Count; i++) {
			var value = i == 0 ? firstElem : LowerExpression(arrayLit.Elements[i]);
			var indexConst = EmitConstantInt(i);
			var setOp = new ManagedMemorySetAtOp(managedMem, indexConst, value);
			InsertOp(setOp);
		}

		// Set the length
		var lenOp = new ManagedMemorySetLengthOp(managedMem, countConst);
		InsertOp(lenOp);

		// Create the Array struct with the managed memory and iterIndex = 0
		var zeroConst = EmitConstantInt(0);
		var fieldValues = new Dictionary<string, MlirValue> {
			["iterIndex"] = zeroConst,
			["managed"] = managedMem
		};

		return EmitStructInit(arrayType, fieldValues);
	}

	/// <summary>
	/// Infers type arguments for a generic type from field initializer expressions
	/// and creates the monomorphized type.
	/// </summary>
	private MaxonStructType InferAndCreateMonomorphizedType(TypeDecl genericType, StructInitExpr structInit) {
		// Build a map from field name to its type parameter (if it uses one)
		var fieldToParam = new Dictionary<string, string>();
		foreach (var field in genericType.Fields) {
			if (field.Type is SimpleTypeRef simple && genericType.GenericParams.Contains(simple.Name)) {
				fieldToParam[field.Name] = simple.Name;
			}
		}

		// Infer type arguments from field values
		var typeArgs = new List<string>();
		var paramToType = new Dictionary<string, string>();

		foreach (var param in genericType.GenericParams) {
			// Find a field that uses this parameter
			var fieldName = fieldToParam.FirstOrDefault(kv => kv.Value == param).Key ?? throw new CompileError(ErrorCode.MlirUndefinedType,
					$"Cannot infer type argument for '{param}' - no field uses this type parameter",
					structInit.Location?.Line, structInit.Location?.Column);

			// Find the initializer for this field
			var fieldInit = structInit.Fields.FirstOrDefault(f => f.Name == fieldName) ?? throw new CompileError(ErrorCode.MlirUndefinedType,
						$"Cannot infer type argument for '{param}' - field '{fieldName}' not initialized",
						structInit.Location?.Line, structInit.Location?.Column);

			// Infer the type from the expression
			var inferredType = InferTypeFromExpr(fieldInit.Value);
			var typeName = MlirTypeToTypeName(inferredType);
			typeArgs.Add(typeName);
			paramToType[param] = typeName;
		}

		return GetOrCreateMonomorphizedType(genericType.Name, typeArgs);
	}

	/// <summary>
	/// Converts an MlirType back to a type name string.
	/// </summary>
	private static string MlirTypeToTypeName(MlirType type) {
		return type switch {
			IntegerType it when it == IntegerType.I64 => "int",
			IntegerType it when it == IntegerType.I32 => "int32",
			IntegerType it when it == IntegerType.I1 => "bool",
			FloatType ft when ft == FloatType.F64 => "float",
			FloatType ft when ft == FloatType.F32 => "float32",
			MaxonStructType st => st.Name,
			_ => throw new NotSupportedException($"Cannot convert type {type} to type name")
		};
	}

	/// <summary>
	/// Checks if a base expression refers to a type name (for static field access).
	/// Returns the type name if static, null otherwise.
	/// </summary>
	private string? TryGetStaticFieldTypeName(Expr baseExpr) {
		if (baseExpr is IdentifierExpr ident &&
			_structTypes.ContainsKey(ident.Name) &&
			!_namedValues.ContainsKey(ident.Name)) {
			return ident.Name;
		}
		return null;
	}

	/// <summary>
	/// Resolves an instance field access, returning the base value and field info.
	/// </summary>
	private (MlirValue baseValue, MaxonFieldInfo fieldInfo) ResolveInstanceField(
		Expr baseExpr, string fieldName, int? line, int? column) {

		var baseValue = LowerExpression(baseExpr);

		if (baseValue.Type is not MaxonStructType structType) {
			throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Cannot access field '{fieldName}' on non-struct type: {baseValue.Type}",
				line, column);
		}

		var fieldInfo = structType.GetField(fieldName)
			?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Struct '{structType.Name}' does not have a field '{fieldName}'",
				line, column);

		return (baseValue, fieldInfo);
	}

	private MlirValue LowerFieldAccessExpr(FieldAccessExpr fieldAccess) {
		// Check if this is an enum case access (e.g., TestError.SomeError)
		if (fieldAccess.Base is IdentifierExpr ident &&
			_enumTypes.TryGetValue(ident.Name, out var enumType) &&
			enumType is MaxonEnumType maxonEnumType) {
			// Verify the case exists
			_ = maxonEnumType.GetVariant(fieldAccess.FieldName) ?? throw new CompileError(ErrorCode.SemanticUndefinedVariable,
						$"Enum '{ident.Name}' has no case '{fieldAccess.FieldName}'",
						fieldAccess.Location?.Line, fieldAccess.Location?.Column);
			// Enum case without associated values
			var enumInitOp = new EnumInitOp(maxonEnumType, fieldAccess.FieldName, null);
			InsertOp(enumInitOp);
			return enumInitOp.Result;
		}

		var typeName = TryGetStaticFieldTypeName(fieldAccess.Base);
		if (typeName != null) {
			var staticFieldName = $"{typeName}.{fieldAccess.FieldName}";
			if (_globals.TryGetValue(staticFieldName, out var global)) {
				return EmitLoad(EmitGetGlobal(global));
			}
			throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Type '{typeName}' does not have a static field '{fieldAccess.FieldName}'",
				fieldAccess.Location?.Line, fieldAccess.Location?.Column);
		}

		var (baseValue, fieldInfo) = ResolveInstanceField(
			fieldAccess.Base, fieldAccess.FieldName,
			fieldAccess.Location?.Line, fieldAccess.Location?.Column);

		return EmitGetField(baseValue, fieldAccess.FieldName, fieldInfo.Type);
	}

	private void LowerFieldAssign(FieldAssignStmt fieldAssign) {
		var typeName = TryGetStaticFieldTypeName(fieldAssign.Base);
		if (typeName != null) {
			var staticFieldName = $"{typeName}.{fieldAssign.FieldName}";
			if (_globals.TryGetValue(staticFieldName, out var global)) {
				if (global.IsConstant) {
					throw new CompileError(ErrorCode.ImmutableVariable,
						$"cannot assign to static field '{staticFieldName}' because it is immutable (declare with 'var' to make it mutable)",
						fieldAssign.Location?.Line, fieldAssign.Location?.Column);
				}
				EmitStore(LowerExpression(fieldAssign.Value), EmitGetGlobal(global));
				return;
			}
			throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
				$"Type '{typeName}' does not have a static field '{fieldAssign.FieldName}'",
				fieldAssign.Location?.Line, fieldAssign.Location?.Column);
		}

		// Check if the base is an immutable variable
		if (fieldAssign.Base is IdentifierExpr ident && _immutableVariables.Contains(ident.Name)) {
			throw new CompileError(ErrorCode.ImmutableVariable,
				$"cannot assign to immutable variable: '{ident.Name}'",
				fieldAssign.Location?.Line, fieldAssign.Location?.Column);
		}

		var (baseValue, fieldInfo) = ResolveInstanceField(
			fieldAssign.Base, fieldAssign.FieldName,
			fieldAssign.Location?.Line, fieldAssign.Location?.Column);

		if (!fieldInfo.IsMutable) {
			var structType = (MaxonStructType)baseValue.Type;
			throw new CompileError(ErrorCode.ImmutableVariable,
				$"cannot assign to field '{structType.Name}.{fieldAssign.FieldName}' because it is immutable (declare with 'var' to make it mutable)",
				fieldAssign.Location?.Line, fieldAssign.Location?.Column);
		}

		EmitSetField(baseValue, fieldAssign.FieldName, LowerExpression(fieldAssign.Value));
	}

	// ========================================================================
	// Type Conversion
	// ========================================================================

	/// <summary>
	/// Converts an AST type to an MLIR type.
	/// </summary>
	public MlirType ConvertType(string typeName) {
		// Handle function pointer type strings: fn(params)->returnType
		if (typeName.StartsWith("fn(")) {
			return ParseFunctionPtrTypeString(typeName);
		}

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

	/// <summary>
	/// Parses a function pointer type string like "fn(int,int)->int" into a FunctionPtrType.
	/// </summary>
	private FunctionPtrType ParseFunctionPtrTypeString(string typeStr) {
		// Format: fn(param1,param2,...)->returnType
		var arrowIndex = typeStr.IndexOf("->");
		if (arrowIndex == -1) {
			throw new InvalidOperationException($"Invalid function type string: {typeStr}");
		}

		// Extract param types (between "fn(" and ")")
		var paramsStart = 3; // len("fn(")
		var paramsEnd = typeStr.IndexOf(')');
		var paramsStr = typeStr[paramsStart..paramsEnd];

		var paramTypes = new List<MlirType>();
		if (!string.IsNullOrEmpty(paramsStr)) {
			foreach (var paramType in paramsStr.Split(',')) {
				paramTypes.Add(ConvertType(paramType.Trim()));
			}
		}

		// Extract return type
		var returnTypeStr = typeStr[(arrowIndex + 2)..];
		var returnType = ConvertType(returnTypeStr);

		return new FunctionPtrType(paramTypes, returnType);
	}

	// ========================================================================
	// Function Conversion
	// ========================================================================

	private MlirValue? _errorUnionReturnPtr; // Hidden pointer for error union returns

	/// <summary>
	/// Starts conversion of a function.
	/// </summary>
	public MlirFunction BeginFunction(string name, List<(string name, string type)> parameters, string returnType, MlirType? sretType = null, MaxonErrorUnionType? errorUnionType = null) {
		var func = new MlirFunction(name);

		// If we have a struct return type, add hidden sret parameter first
		if (sretType != null) {
			var sretPtrType = new PtrType(sretType);
			func.AddParam("__sret", sretPtrType);
		}

		// If we have an error union return type, add hidden pointer parameter
		if (errorUnionType != null) {
			var errorUnionPtrType = new PtrType(errorUnionType);
			func.AddParam("__error_union_ret", errorUnionPtrType);
		}

		foreach (var (pname, ptype) in parameters) {
			func.AddParam(pname, ConvertType(ptype));
		}

		if (returnType != "void" && errorUnionType == null) {
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

		// Handle error union return parameter
		if (errorUnionType != null) {
			_errorUnionReturnPtr = paramValues[paramIndex++];
		} else {
			_errorUnionReturnPtr = null;
		}

		// Create allocas for scalar parameters to enable proper SSA promotion via mem2reg.
		// This is necessary because parameters may be reassigned inside the function body,
		// and without allocas, those reassignments can't be tracked through loops.
		// Struct parameters (like 'self') are already passed by reference, so they don't need allocas.
		for (int i = 0; i < parameters.Count; i++) {
			var paramValue = paramValues[paramIndex++];
			var paramType = paramValue.Type;
			var paramName = parameters[i].name;

			// Struct parameters are passed by reference - use them directly without allocas.
			// Struct parameters are passed by reference - use them directly without allocas.
			// Creating an alloca for a struct param would create a pointer-to-pointer situation.
			if (paramType is MaxonStructType) {
				_namedValues[paramName] = new VariableBinding.Direct(paramValue);
				continue;
			}

			// For scalar types (primitives), create an alloca to enable SSA promotion.
			var alloca = EmitAlloca(paramType, paramName);

			// Store the incoming parameter value into the alloca
			EmitStore(paramValue, alloca);

			// Map the parameter name to the alloca (not the raw parameter value)
			_namedValues[paramName] = new VariableBinding.StackSlot(alloca);
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
	/// Generates a constant i8.
	/// </summary>
	public MlirValue EmitConstantI8(long value) {
		var op = ConstantOp.Int(value, IntegerType.I8);
		InsertOp(op);
		return op.Result;
	}

	/// <summary>
	/// Generates a constant index (i64) for array/memory indexing.
	/// </summary>
	public MlirValue EmitConstantIndex(long value) {
		var op = ConstantOp.Index(value);
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
	/// Gets or creates a monomorphized version of a generic type.
	/// For example, Pair with (int, int) becomes Pair$int$int.
	/// </summary>
	public MaxonStructType GetOrCreateMonomorphizedType(string baseName, List<string> typeArgs) {
		// Build monomorphized name: TypeName$Arg1$Arg2
		var monoName = $"{baseName}${string.Join("$", typeArgs)}";

		// Check if already registered
		if (_structTypes.TryGetValue(monoName, out var existingType)) {
			return (MaxonStructType)existingType;
		}

		// Look up the generic type declaration
		if (!_genericTypes.TryGetValue(baseName, out var genericType)) {
			throw new InvalidOperationException($"Unknown generic type: {baseName}");
		}

		// Verify correct number of type arguments
		if (typeArgs.Count != genericType.GenericParams.Count) {
			throw new InvalidOperationException(
				$"Generic type '{baseName}' expects {genericType.GenericParams.Count} type arguments, but got {typeArgs.Count}");
		}

		// Build parameter substitution map
		var paramMap = new Dictionary<string, string>();
		for (int i = 0; i < genericType.GenericParams.Count; i++) {
			paramMap[genericType.GenericParams[i]] = typeArgs[i];
		}

		// Create fields with substituted types
		var instanceFields = new List<(string name, MlirType type, bool isMutable, Expr? defaultValue)>();

		foreach (var field in genericType.Fields) {
			if (field.IsStatic) continue; // Skip static fields for now

			MlirType fieldType;
			if (field.Type != null) {
				// Substitute type parameters
				var typeName = GetTypeString(field.Type);
				if (paramMap.TryGetValue(typeName, out var substituted)) {
					fieldType = ConvertType(substituted);
				} else {
					fieldType = ConvertTypeRef(field.Type);
				}
			} else if (field.DefaultValue != null) {
				fieldType = InferTypeFromExpr(field.DefaultValue);
			} else {
				throw new InvalidOperationException($"Field '{field.Name}' has no type annotation and no default value");
			}

			instanceFields.Add((field.Name, fieldType, field.IsMutable, field.DefaultValue));
		}

		Logger.Debug(LogCategory.Mlir, $"  Creating monomorphized type: {monoName}");
		var structType = RegisterStruct(monoName, instanceFields);

		// Register method signatures for the monomorphized type (methods are lowered on-demand)
		if (!_pendingMonomorphizedTypes.ContainsKey(monoName)) {
			RegisterMonomorphizedMethodSignatures(monoName, genericType, paramMap);
		}

		return structType;
	}

	/// <summary>
	/// Registers method signatures for a monomorphized generic type.
	/// Methods are lowered on-demand when called (not eagerly) to avoid errors from unimplemented features.
	/// </summary>
	private void RegisterMonomorphizedMethodSignatures(string monoName, TypeDecl genericType, Dictionary<string, string> paramMap) {
		// Add "Self" to the param map - it refers to the monomorphized type itself
		var fullParamMap = new Dictionary<string, string>(paramMap) {
			["Self"] = monoName
		};

		// Store type info for on-demand method lowering
		_pendingMonomorphizedTypes[monoName] = (genericType, fullParamMap);

		// Register method signatures for the monomorphized type
		foreach (var method in genericType.Methods) {
			var methodName = $"{monoName}.{method.Name}";

			// Substitute type parameters in return type
			MlirType returnType = NoneType.Instance;
			if (method.ReturnType != null) {
				returnType = SubstituteTypeRef(method.ReturnType, fullParamMap);
			}

			// Handle throwing methods
			if (method.ThrowsType != null && _enumTypes.TryGetValue(method.ThrowsType, out var errorEnumType) && errorEnumType is MaxonEnumType errorEnum) {
				returnType = new MaxonErrorUnionType(returnType, errorEnum);
			}
			_functionReturnTypes[methodName] = returnType;

			// Build parameter list with type substitution
			var methodParams = new List<ParamDecl>();
			if (!method.IsStatic) {
				// 'self' parameter uses the monomorphized type name
				methodParams.Add(new ParamDecl("self", new SimpleTypeRef(monoName)));
			}
			foreach (var param in method.Params) {
				// Create a new param with substituted type
				var substType = SubstituteTypeRefInTypeRef(param.Type, fullParamMap);
				methodParams.Add(new ParamDecl(param.Name, substType));
			}
			_functionParams[methodName] = methodParams;

			// Also register with base name (without interface qualifier) for direct method calls
			// E.g., "Sized.count" -> also register as "count"
			if (method.Name.Contains('.')) {
				var baseName = method.Name.Split('.').Last();
				var aliasMethodName = $"{monoName}.{baseName}";
				if (!_functionReturnTypes.ContainsKey(aliasMethodName)) {
					_functionReturnTypes[aliasMethodName] = returnType;
					_functionParams[aliasMethodName] = methodParams;
				}
			}
		}
		// Methods are NOT lowered here - they will be lowered on-demand when called
	}

	/// <summary>
	/// Lowers a single method from a generic type with type parameter substitution.
	/// Saves and restores function context to support on-demand lowering during another function's lowering.
	/// </summary>
	private void LowerMonomorphizedMethod(string monoTypeName, MethodDecl method, Dictionary<string, string> paramMap) {
		// Save current function context (for on-demand lowering during another function's lowering)
		var savedFunction = _currentFunction;
		var savedBlock = _currentBlock;
		var savedNamedValues = new Dictionary<string, VariableBinding>(_namedValues);
		var savedImmutableVariables = new HashSet<string>(_immutableVariables);
		var savedStructReturnPtr = _structReturnPtr;
		var savedErrorUnionReturnPtr = _errorUnionReturnPtr;
		var savedMethodStructType = _currentMethodStructType;
		var savedErrorUnionType = _currentFunctionErrorUnionType;
		var savedSuccessType = _currentFunctionSuccessType;

		var parameters = new List<(string name, string type)>();
		if (!method.IsStatic) {
			parameters.Add(("self", monoTypeName));
			// Set the current method struct type for field resolution
			if (_structTypes.TryGetValue(monoTypeName, out var structType) && structType is MaxonStructType mst) {
				_currentMethodStructType = mst;
			}
		}

		// Add parameters with type substitution
		foreach (var param in method.Params) {
			// Substitute type parameters in the param's type
			var substType = SubstituteTypeRefInTypeRef(param.Type, paramMap);
			var typeName = GetTypeString(substType);
			parameters.Add((param.Name, typeName));
		}

		try {
			// Substitute return type
			var substReturnTypeRef = method.ReturnType != null ? SubstituteTypeRefInTypeRef(method.ReturnType, paramMap) : null;
			// Use base name (without interface qualifier) for function name so it matches call sites
			// E.g., "Sized.count" -> "count"
			var baseName = method.Name.Contains('.') ? method.Name.Split('.').Last() : method.Name;
			LowerFunctionBody($"{monoTypeName}.{baseName}", parameters, substReturnTypeRef, method.ThrowsType, method.Body);
		} finally {
			// Restore previous function context
			_currentFunction = savedFunction;
			_currentBlock = savedBlock;
			_namedValues.Clear();
			foreach (var kv in savedNamedValues) {
				_namedValues[kv.Key] = kv.Value;
			}
			_immutableVariables.Clear();
			foreach (var v in savedImmutableVariables) {
				_immutableVariables.Add(v);
			}
			_structReturnPtr = savedStructReturnPtr;
			_errorUnionReturnPtr = savedErrorUnionReturnPtr;
			_currentMethodStructType = savedMethodStructType;
			_currentFunctionErrorUnionType = savedErrorUnionType;
			_currentFunctionSuccessType = savedSuccessType;
		}
	}

	/// <summary>
	/// Ensures a monomorphized method is lowered on-demand.
	/// Called before emitting a call to a monomorphized type's method.
	/// </summary>
	private void EnsureMonomorphizedMethodLowered(string monoTypeName, string methodName) {
		var fullMethodName = $"{monoTypeName}.{methodName}";

		// Already lowered?
		if (_loweredMonomorphizedMethods.Contains(fullMethodName)) {
			return;
		}

		// Not a monomorphized type?
		if (!_pendingMonomorphizedTypes.TryGetValue(monoTypeName, out var typeInfo)) {
			return;
		}

		// Find the method in the generic type
		// Try exact match first, then try matching by base name (for interface methods)
		var (genericType, paramMap) = typeInfo;
		var method = genericType.Methods.FirstOrDefault(m => m.Name == methodName) ?? genericType.Methods.FirstOrDefault(m => m.Name.EndsWith($".{methodName}"));
		if (method == null) {
			return;
		}

		// Mark as lowered first to prevent infinite recursion
		// Mark both the requested name and the actual method name
		_loweredMonomorphizedMethods.Add(fullMethodName);
		var actualFullMethodName = $"{monoTypeName}.{method.Name}";
		if (actualFullMethodName != fullMethodName) {
			_loweredMonomorphizedMethods.Add(actualFullMethodName);
		}

		Logger.Debug(LogCategory.Mlir, $"  On-demand lowering method: {actualFullMethodName}");
		LowerMonomorphizedMethod(monoTypeName, method, paramMap);
	}

	/// <summary>
	/// Substitutes type parameters in a TypeRef and converts to MlirType.
	/// </summary>
	private MlirType SubstituteTypeRef(TypeRef typeRef, Dictionary<string, string> paramMap) {
		var typeName = GetTypeString(typeRef);
		if (paramMap.TryGetValue(typeName, out var substituted)) {
			return ConvertType(substituted);
		}
		return ConvertTypeRef(typeRef);
	}

	/// <summary>
	/// Substitutes type parameters in a TypeRef, returning a new TypeRef.
	/// </summary>
	private static TypeRef SubstituteTypeRefInTypeRef(TypeRef typeRef, Dictionary<string, string> paramMap) {
		return typeRef switch {
			SimpleTypeRef simple when paramMap.TryGetValue(simple.Name, out var substituted)
				=> new SimpleTypeRef(substituted),
			SimpleTypeRef simple => simple,
			GenericTypeRef generic => new GenericTypeRef(
				generic.BaseName,
				[.. generic.TypeArgs.Select(arg => paramMap.TryGetValue(arg, out var sub) ? sub : arg)]
			),
			FunctionTypeRef func => new FunctionTypeRef(
				[.. func.ParamTypes.Select(p => SubstituteTypeRefInTypeRef(p, paramMap))],
				func.ReturnType != null ? SubstituteTypeRefInTypeRef(func.ReturnType, paramMap) : null
			),
			_ => typeRef
		};
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

		Logger.Debug(LogCategory.Mlir, $"EmitStructCopy: copying {structType.Name} ({structType.Fields.Count} fields, {structType.SizeInBytes} bytes)");

		// Copy each field
		foreach (var field in structType.Fields) {
			Logger.Trace(LogCategory.Mlir, $"  copying field {field.Name}: offset={field.Offset}, size={field.Type.SizeInBytes}");

			// For nested structs larger than 8 bytes, use memcpy
			if (field.Type.SizeInBytes > 8) {
				var srcFieldPtr = new FieldPtrOp(srcPtr, field.Name, field.Offset, field.Type);
				InsertOp(srcFieldPtr);
				var dstFieldPtr = new FieldPtrOp(destPtr, field.Name, field.Offset, field.Type);
				InsertOp(dstFieldPtr);

				// Use MemCpyOp which will be lowered to a memcpy call
				var sizeVal = EmitConstantInt(field.Type.SizeInBytes);
				var memcpyOp = new MemCpyOp(dstFieldPtr.Result, srcFieldPtr.Result, sizeVal);
				InsertOp(memcpyOp);
			} else {
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
		_namedValues[name] = new VariableBinding.StackSlot(alloca);
		return alloca;
	}

	/// <summary>
	/// Gets a variable's address.
	/// </summary>
	public MlirValue GetVariableAddress(string name) {
		// Check local variables first
		if (_namedValues.TryGetValue(name, out var binding))
			return binding.GetAddress();

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
			if (fieldInfo != null && _namedValues.TryGetValue("self", out var selfBinding)) {
				// Self is always a Direct binding (struct passed by ref)
				var selfValue = selfBinding switch {
					VariableBinding.Direct d => d.Value,
					VariableBinding.StackSlot s => s.Address,
					_ => throw new InvalidOperationException("Unknown VariableBinding variant for self")
				};
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

		// Use the VariableBinding union to determine if we need to load
		if (_namedValues.TryGetValue(name, out var binding)) {
			return binding switch {
				VariableBinding.StackSlot slot => EmitLoad(slot.Address),
				VariableBinding.Direct direct => direct.Value,
				_ => throw new InvalidOperationException($"Unknown VariableBinding variant for {name}")
			};
		}

		// Check global variables
		if (_globals.TryGetValue(name, out var global)) {
			return EmitLoad(EmitGetGlobal(global));
		}

		throw new InvalidOperationException($"Undefined variable: {name}");
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
			if (fieldInfo != null && _namedValues.TryGetValue("self", out var selfBinding)) {
				// Self is always a Direct binding (struct passed by ref)
				var selfValue = selfBinding switch {
					VariableBinding.Direct d => d.Value,
					VariableBinding.StackSlot s => s.Address,
					_ => throw new InvalidOperationException("Unknown VariableBinding variant for self")
				};
				EmitSetField(selfValue, name, value);
				return true;
			}
		}
		return false;
	}

	// ========================================================================
	// Built-in Types
	// ========================================================================

	/// <summary>
	/// Registers compiler-internal built-in types like __ManagedMemory.
	/// These are special types used by stdlib that have fixed layouts.
	/// </summary>
	private void RegisterBuiltinTypes() {
		// __ManagedMemory: compiler-managed opaque array storage
		// Layout: 32 bytes total
		//   _buffer: ptr (offset 0, 8 bytes) - pointer to element data
		//   _len: i64 (offset 8, 8 bytes) - current number of elements
		//   _capacity: i64 (offset 16, 8 bytes) - allocated slots
		//   _flags: i32 (offset 24, 4 bytes) - internal flags
		//   _parent_off: i32 (offset 28, 4 bytes) - parent struct offset
		var managedMemoryFields = new List<(string name, MlirType type, bool isMutable, Expr? defaultValue)> {
			("_buffer", new PtrType(IntegerType.I8), true, null),
			("_len", IntegerType.I64, true, null),
			("_capacity", IntegerType.I64, true, null),
			("_flags", IntegerType.I32, true, null),
			("_parent_off", IntegerType.I32, true, null)
		};
		RegisterStruct("__ManagedMemory", managedMemoryFields);
		Logger.Debug(LogCategory.Mlir, "  Registered builtin type: __ManagedMemory (32 bytes)");
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
