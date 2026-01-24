namespace MaxonSharp.Compiler;

public class AstToHir {
	private int _nextValueId;
	private int _nextLabelId;
	private readonly Dictionary<string, HirValue> _variableSlots = []; // Maps var name to its stack slot pointer
	private readonly Dictionary<string, HirType> _variableTypes = [];
	private readonly Dictionary<string, HirStructDef> _structs = [];
	private readonly Dictionary<string, HirEnumDef> _enums = [];
	private readonly Dictionary<string, (int ParamIndex, HirType Type)> _params = [];
	private readonly Dictionary<string, HirType> _functionReturnTypes = []; // Function name -> return type
	private string? _currentFunctionName;
	private HirType? _currentReturnType;
	private string? _currentTypeName; // For resolving implicit field accesses

	// Ownership and memory management
	private ResourceManager? _resources;
	private BlockManager? _blocks;
	private MutationAnalyzer _mutationAnalyzer = new();

	public HirModule Lower(ProgramAst program, MutationAnalyzer? mutationAnalyzer = null) {
		Logger.Debug(LogCategory.Hir, "Starting AST to HIR lowering");

		// Use provided mutation analyzer or create a new one
		if (mutationAnalyzer != null) {
			_mutationAnalyzer = mutationAnalyzer;
		}

		var structs = new List<HirStructDef>();
		var enums = new List<HirEnumDef>();
		var globals = new List<HirGlobalVar>();
		var functions = new List<HirFunction>();

		// First pass: collect struct and enum definitions
		Logger.Debug(LogCategory.Hir, "Pass 1: Collecting struct/enum definitions");
		foreach (var type in program.Types) {
			Logger.Debug(LogCategory.Hir, $"Lowering type: {type.Name}");
			var structDef = LowerTypeDecl(type);
			structs.Add(structDef);
			_structs[type.Name] = structDef;
		}

		foreach (var enumDecl in program.Enums) {
			Logger.Debug(LogCategory.Hir, $"Lowering enum: {enumDecl.Name}");
			var enumDef = LowerEnumDecl(enumDecl);
			enums.Add(enumDef);
			_enums[enumDecl.Name] = enumDef;
		}

		// Second pass: collect function signatures (return types)
		Logger.Debug(LogCategory.Hir, "Pass 2: Collecting function signatures");
		foreach (var func in program.Functions) {
			_functionReturnTypes[func.Name] = LowerType(func.ReturnType);
		}
		foreach (var type in program.Types) {
			foreach (var method in type.Methods) {
				var funcName = $"{type.Name}.{method.Name}";
				_functionReturnTypes[funcName] = LowerType(method.ReturnType);
			}
		}

		// Collect global constants and variables
		foreach (var gc in program.GlobalConstants) {
			var type = InferExprType(gc.Value);
			globals.Add(new HirGlobalVar(gc.Name, type, null));
		}
		foreach (var gv in program.GlobalVariables) {
			var type = InferExprType(gv.Value);
			globals.Add(new HirGlobalVar(gv.Name, type, null));
		}

		// Lower functions
		Logger.Debug(LogCategory.Hir, "Pass 3: Lowering functions");
		foreach (var func in program.Functions) {
			Logger.Debug(LogCategory.Hir, $"Lowering function: {func.Name}");
			functions.Add(LowerFunction(func));
		}

		// Lower methods from types
		foreach (var type in program.Types) {
			foreach (var method in type.Methods) {
				Logger.Debug(LogCategory.Hir, $"Lowering method: {type.Name}.{method.Name}");
				functions.Add(LowerMethod(type.Name, method));
			}
		}

		Logger.Debug(LogCategory.Hir, $"HIR complete: {functions.Count} functions");
		return new HirModule(structs, enums, globals, functions);
	}

	private HirStructDef LowerTypeDecl(TypeDecl type) {
		var fields = new List<HirStructField>();
		var offset = 0;

		foreach (var field in type.Fields) {
			var fieldType = LowerType(field.Type);
			fields.Add(new HirStructField(field.Name, fieldType, offset));
			offset += fieldType.SizeInBytes;
		}

		return new HirStructDef(type.Name, fields);
	}

	private HirEnumDef LowerEnumDecl(EnumDecl enumDecl) {
		var variants = new List<HirEnumVariant>();
		var maxPayloadSize = 0;
		var tag = 0;

		foreach (var member in enumDecl.Members) {
			var payloadFields = new List<HirStructField>();
			var payloadOffset = 0;

			foreach (var assoc in member.AssociatedValues) {
				var fieldType = LowerType(assoc.Type);
				payloadFields.Add(new HirStructField(assoc.Name, fieldType, payloadOffset));
				payloadOffset += fieldType.SizeInBytes;
			}

			variants.Add(new HirEnumVariant(member.Name, tag++, payloadFields));
			maxPayloadSize = Math.Max(maxPayloadSize, payloadOffset);
		}

		return new HirEnumDef(enumDecl.Name, variants, 8, maxPayloadSize);
	}

	private HirFunction LowerFunction(FunctionDecl func) {
		_nextValueId = 0;
		// NOTE: _nextLabelId is NOT reset - labels must be globally unique across all functions
		_variableSlots.Clear();
		_variableTypes.Clear();
		_params.Clear();
		_currentFunctionName = func.Name;
		_currentReturnType = LowerType(func.ReturnType);
		_currentTypeName = null; // No implicit field access in top-level functions

		// Initialize ownership managers for this function
		_blocks = new BlockManager();
		_resources = new ResourceManager(_mutationAnalyzer, _blocks);

		var parameters = new List<HirParam2>();
		var entryInstrs = new List<HirInstr>();

		// Set current block for resource manager cleanup emission
		_resources.SetCurrentBlock(entryInstrs);

		// Begin function scope
		_resources.BeginScope();
		_blocks.EnterBlock(BlockKind.Function, _resources.Snapshot());

		// Lower parameters - allocate slots and store param values
		for (var i = 0; i < func.Params.Count; i++) {
			var param = func.Params[i];
			var paramType = LowerType(param.Type);
			parameters.Add(new HirParam2(param.Name, paramType));

			var paramValue = NewValue();
			entryInstrs.Add(new HirParam(paramValue, i, paramType));

			// Allocate slot and store parameter
			var slotPtr = NewValue();
			entryInstrs.Add(new HirAlloca(slotPtr, paramType));
			// For struct/enum params, the paramValue is a pointer to the caller's copy - use memcpy
			if (paramType is HirStructType structType) {
				entryInstrs.Add(new HirMemcpy(slotPtr, paramValue, structType.SizeInBytes));
			} else if (paramType is HirEnumType enumType) {
				entryInstrs.Add(new HirMemcpy(slotPtr, paramValue, enumType.SizeInBytes));
			} else {
				entryInstrs.Add(new HirStore(slotPtr, paramValue, paramType));
			}
			_variableSlots[param.Name] = slotPtr;
			_variableTypes[param.Name] = paramType;
			_params[param.Name] = (i, paramType);

			// Register parameter with resource manager (params are owned)
			_resources.DeclareVariable(param.Name, paramType, isMutable: true, slotPtr);
		}

		// Lower body
		var blocks = new List<HirBlock>();
		LowerStatements(func.Body, entryInstrs);

		// End function scope - cleanup owned resources
		_blocks.ExitBlock();
		_resources.EndScope();

		// Ensure we have a return
		if (entryInstrs.Count == 0 || entryInstrs[^1] is not HirRet) {
			if (_currentReturnType is HirVoidType) {
				entryInstrs.Add(new HirRet(null));
			}
		}

		blocks.Add(new HirBlock("entry", entryInstrs));

		return new HirFunction(func.Name, func.IsExport, parameters, _currentReturnType, blocks);
	}

	private HirFunction LowerMethod(string typeName, MethodDecl method) {
		_nextValueId = 0;
		// NOTE: _nextLabelId is NOT reset - labels must be globally unique across all functions
		_variableSlots.Clear();
		_variableTypes.Clear();
		_params.Clear();
		_currentFunctionName = $"{typeName}.{method.Name}";
		_currentReturnType = LowerType(method.ReturnType);
		_currentTypeName = method.IsStatic ? null : typeName; // Track type for implicit field access

		// Initialize ownership managers for this method
		_blocks = new BlockManager();
		_resources = new ResourceManager(_mutationAnalyzer, _blocks);

		var parameters = new List<HirParam2>();
		var entryInstrs = new List<HirInstr>();

		// Set current block for resource manager cleanup emission
		_resources.SetCurrentBlock(entryInstrs);

		// Begin function scope
		_resources.BeginScope();
		_blocks.EnterBlock(BlockKind.Function, _resources.Snapshot());

		// Add implicit 'self' parameter for instance methods
		var paramIndex = 0;
		if (!method.IsStatic) {
			var selfType = new HirPtrType();
			parameters.Add(new HirParam2("self", selfType));
			var selfValue = NewValue();
			entryInstrs.Add(new HirParam(selfValue, paramIndex++, selfType));

			// Allocate slot for self
			var selfSlot = NewValue();
			entryInstrs.Add(new HirAlloca(selfSlot, selfType));
			entryInstrs.Add(new HirStore(selfSlot, selfValue, selfType));
			_variableSlots["self"] = selfSlot;
			_variableTypes["self"] = selfType;

			// Register self with resource manager
			_resources.DeclareVariable("self", selfType, isMutable: false, selfSlot);
		}

		// Lower parameters
		for (var i = 0; i < method.Params.Count; i++) {
			var param = method.Params[i];
			var paramType = LowerType(param.Type);
			parameters.Add(new HirParam2(param.Name, paramType));

			var paramValue = NewValue();
			entryInstrs.Add(new HirParam(paramValue, paramIndex++, paramType));

			// Allocate slot and store parameter
			var slotPtr = NewValue();
			entryInstrs.Add(new HirAlloca(slotPtr, paramType));
			// For struct/enum params, the paramValue is a pointer to the caller's copy - use memcpy
			if (paramType is HirStructType structType) {
				entryInstrs.Add(new HirMemcpy(slotPtr, paramValue, structType.SizeInBytes));
			} else if (paramType is HirEnumType enumType) {
				entryInstrs.Add(new HirMemcpy(slotPtr, paramValue, enumType.SizeInBytes));
			} else {
				entryInstrs.Add(new HirStore(slotPtr, paramValue, paramType));
			}
			_variableSlots[param.Name] = slotPtr;
			_variableTypes[param.Name] = paramType;
			_params[param.Name] = (paramIndex - 1, paramType);

			// Register parameter with resource manager
			_resources.DeclareVariable(param.Name, paramType, isMutable: true, slotPtr);
		}

		// Lower body
		LowerStatements(method.Body, entryInstrs);

		// End function scope - cleanup owned resources
		_blocks.ExitBlock();
		_resources.EndScope();

		// Ensure we have a return
		if (entryInstrs.Count == 0 || entryInstrs[^1] is not HirRet) {
			if (_currentReturnType is HirVoidType) {
				entryInstrs.Add(new HirRet(null));
			}
		}

		var blocks = new List<HirBlock> { new("entry", entryInstrs) };

		return new HirFunction(_currentFunctionName, method.IsExport, parameters, _currentReturnType, blocks);
	}

	private void LowerStatements(List<Stmt> statements, List<HirInstr> instructions) {
		foreach (var stmt in statements) {
			LowerStatement(stmt, instructions);
		}
	}

	private void LowerStatement(Stmt stmt, List<HirInstr> instructions) {
		switch (stmt) {
			case ReturnStmt ret:
				HirValue? retValue = null;
				if (ret.Value != null) {
					retValue = LowerExpression(ret.Value, instructions);
				}
				// Mark block as terminating for ownership tracking
				_blocks?.MarkTerminates();
				instructions.Add(new HirRet(retValue));
				break;

			case LetDeclStmt:
			case VarDeclStmt: {
					var name = stmt is LetDeclStmt l ? l.Name : ((VarDeclStmt)stmt).Name;
					var value = stmt is LetDeclStmt ld ? ld.Value : ((VarDeclStmt)stmt).Value;
					var isMutable = stmt is VarDeclStmt;

					var inferredType = InferExprType(value);
					_variableTypes[name] = inferredType;

					// Allocate stack slot for variable
					var slotPtr = NewValue();
					instructions.Add(new HirAlloca(slotPtr, inferredType));
					_variableSlots[name] = slotPtr;

					// Register with resource manager
					_resources?.DeclareVariable(name, inferredType, isMutable, slotPtr);

					// For struct and enum types, use memcpy to copy the contents
					if (inferredType is HirStructType structType) {
						var srcPtr = LowerExpressionAsAddress(value, instructions);
						instructions.Add(new HirMemcpy(slotPtr, srcPtr, structType.SizeInBytes));
					} else if (inferredType is HirEnumType enumType) {
						var srcPtr = LowerExpressionAsAddress(value, instructions);
						instructions.Add(new HirMemcpy(slotPtr, srcPtr, enumType.SizeInBytes));
					} else {
						var exprValue = LowerExpression(value, instructions);
						instructions.Add(new HirStore(slotPtr, exprValue, inferredType));
					}
					break;
				}

			case AssignStmt assign: {
					if (_variableSlots.TryGetValue(assign.Target, out var slotPtr)) {
						// Record reassignment for ownership tracking
						_resources?.Reassign(assign.Target);

						var varType = _variableTypes.GetValueOrDefault(assign.Target, new HirIntType());
						if (varType is HirStructType structType) {
							var srcPtr = LowerExpressionAsAddress(assign.Value, instructions);
							instructions.Add(new HirMemcpy(slotPtr, srcPtr, structType.SizeInBytes));
						} else if (varType is HirEnumType enumType) {
							var srcPtr = LowerExpressionAsAddress(assign.Value, instructions);
							instructions.Add(new HirMemcpy(slotPtr, srcPtr, enumType.SizeInBytes));
						} else {
							var value = LowerExpression(assign.Value, instructions);
							instructions.Add(new HirStore(slotPtr, value, varType));
						}
					} else {
						throw new CompileError(ErrorCode.HirUndefinedVariable, $"Unknown variable: {assign.Target}");
					}
					break;
				}

			case FieldAssignStmt fieldAssign: {
					var basePtr = LowerExpression(fieldAssign.Base, instructions);
					var value = LowerExpression(fieldAssign.Value, instructions);
					var offset = GetFieldOffset(fieldAssign.Base, fieldAssign.FieldName);
					var fieldType = GetFieldType(fieldAssign.Base, fieldAssign.FieldName);
					var fieldPtr = NewValue();
					instructions.Add(new HirGetFieldPtr(fieldPtr, basePtr, fieldAssign.FieldName, offset));
					instructions.Add(new HirStore(fieldPtr, value, fieldType));
					break;
				}

			case ExprStmt exprStmt:
				LowerExpression(exprStmt.Expression, instructions);
				break;

			case IfStmt ifStmt: {
					var thenLabel = NewLabel("then");
					var elseLabel = NewLabel("else");
					var endLabel = NewLabel("endif");

					var cond = LowerExpression(ifStmt.Condition, instructions);
					instructions.Add(new HirBrCond(cond, thenLabel, ifStmt.ElseBody != null ? elseLabel : endLabel));

					// Snapshot ownership state before branches
					var beforeBranch = _resources?.Snapshot();

					// Lower then branch
					instructions.Add(new HirLabel(thenLabel));
					_blocks?.EnterBlock(BlockKind.IfThen, beforeBranch ?? new OwnershipSnapshot());
					LowerStatements(ifStmt.ThenBody, instructions);
					var thenState = _blocks?.ExitBlock();
					instructions.Add(new HirBr(endLabel));

					BlockState? elseState = null;
					if (ifStmt.ElseBody != null) {
						// Restore state before else branch
						if (beforeBranch != null) _resources?.Restore(beforeBranch);

						instructions.Add(new HirLabel(elseLabel));
						_blocks?.EnterBlock(BlockKind.IfElse, beforeBranch ?? new OwnershipSnapshot());
						LowerStatements(ifStmt.ElseBody, instructions);
						elseState = _blocks?.ExitBlock();
						instructions.Add(new HirBr(endLabel));
					}

					instructions.Add(new HirLabel(endLabel));

					// Merge ownership states from branches
					if (_blocks != null && thenState != null) {
						var merge = BlockManager.MergeBranches(thenState, elseState);
						_resources?.ApplyMerge(merge);
					}
					break;
				}

			case WhileStmt whileStmt: {
					var condLabel = NewLabel("while_cond");
					var bodyLabel = NewLabel("while_body");
					var endLabel = NewLabel("while_end");

					instructions.Add(new HirBr(condLabel));
					instructions.Add(new HirLabel(condLabel));

					var cond = LowerExpression(whileStmt.Condition, instructions);
					instructions.Add(new HirBrCond(cond, bodyLabel, endLabel));

					// Track loop body for move validation
					var beforeLoop = _resources?.Snapshot();
					instructions.Add(new HirLabel(bodyLabel));
					_blocks?.EnterBlock(BlockKind.WhileLoop, beforeLoop ?? new OwnershipSnapshot());
					LowerStatements(whileStmt.Body, instructions);
					var loopState = _blocks?.ExitBlock();

					// Validate moves in loop body
					if (_blocks != null && loopState != null) {
						BlockManager.ValidateLoopBody(loopState);
					}

					instructions.Add(new HirBr(condLabel));
					instructions.Add(new HirLabel(endLabel));
					break;
				}

			case ForStmt forStmt: {
					// Lower for loop to while loop with counter
					var startLabel = NewLabel("for_start");
					var bodyLabel = NewLabel("for_body");
					var endLabel = NewLabel("for_end");

					// Get range bounds from RangeExpr
					HirValue startVal, endVal;
					bool inclusive;
					if (forStmt.Iterable is RangeExpr rangeExpr) {
						startVal = LowerExpression(rangeExpr.Start, instructions);
						endVal = LowerExpression(rangeExpr.End, instructions);
						inclusive = rangeExpr.Inclusive;
					} else {
						throw new CompileError(ErrorCode.HirUnsupportedExpression, $"For loop iterable must be a range expression (start..end), got {forStmt.Iterable.GetType().Name}");
					}

					// Allocate slot for loop variable and store initial value
					var loopVarSlot = NewValue();
					instructions.Add(new HirAlloca(loopVarSlot, new HirIntType()));
					instructions.Add(new HirStore(loopVarSlot, startVal, new HirIntType()));
					_variableSlots[forStmt.VarName] = loopVarSlot;
					_variableTypes[forStmt.VarName] = new HirIntType();

					// Register loop variable with resource manager
					_resources?.DeclareVariable(forStmt.VarName, new HirIntType(), isMutable: true, loopVarSlot);

					// Store end value in a slot so it's stable across loop iterations
					var endValSlot = NewValue();
					instructions.Add(new HirAlloca(endValSlot, new HirIntType()));
					instructions.Add(new HirStore(endValSlot, endVal, new HirIntType()));

					instructions.Add(new HirBr(startLabel));
					instructions.Add(new HirLabel(startLabel));

					// Load current i value
					var currentI = NewValue();
					instructions.Add(new HirLoad(currentI, loopVarSlot, new HirIntType()));

					// Load end value
					var currentEnd = NewValue();
					instructions.Add(new HirLoad(currentEnd, endValSlot, new HirIntType()));

					// Check condition: i < end (exclusive) or i <= end (inclusive)
					var condResult = NewValue();
					if (inclusive) {
						instructions.Add(new HirCmpLe(condResult, currentI, currentEnd));
					} else {
						instructions.Add(new HirCmpLt(condResult, currentI, currentEnd));
					}
					instructions.Add(new HirBrCond(condResult, bodyLabel, endLabel));

					// Track loop body for move validation
					var beforeLoop = _resources?.Snapshot();
					instructions.Add(new HirLabel(bodyLabel));
					_blocks?.EnterBlock(BlockKind.ForLoop, beforeLoop ?? new OwnershipSnapshot());
					LowerStatements(forStmt.Body, instructions);
					var loopState = _blocks?.ExitBlock();

					// Validate moves in loop body
					if (_blocks != null && loopState != null) {
						BlockManager.ValidateLoopBody(loopState);
					}

					// Increment: i = i + 1
					var currentIForIncr = NewValue();
					instructions.Add(new HirLoad(currentIForIncr, loopVarSlot, new HirIntType()));
					var one = NewValue();
					instructions.Add(new HirConstInt(one, 1));
					var nextI = NewValue();
					instructions.Add(new HirAdd(nextI, currentIForIncr, one));
					instructions.Add(new HirStore(loopVarSlot, nextI, new HirIntType()));

					instructions.Add(new HirBr(startLabel));
					instructions.Add(new HirLabel(endLabel));
					break;
				}

			case MatchStmt matchStmt: {
					var matchId = _nextLabelId++;
					var endLabel = $"match_{matchId}_end";
					var scrutinee = LowerExpression(matchStmt.Scrutinee, instructions);

					// Pre-generate all labels for consistent naming
					var caseLabels = new List<string>();
					var checkLabels = new List<string>();
					for (var i = 0; i < matchStmt.Cases.Count; i++) {
						caseLabels.Add($"match_{matchId}_case_{i}");
						checkLabels.Add($"match_{matchId}_check_{i}");
					}
					var defaultLabel = $"match_{matchId}_default";

					for (var i = 0; i < matchStmt.Cases.Count; i++) {
						var matchCase = matchStmt.Cases[i];
						var caseLabel = caseLabels[i];
						var nextLabel = i < matchStmt.Cases.Count - 1 ? checkLabels[i + 1] : (matchStmt.DefaultBody != null ? defaultLabel : endLabel);

						// Emit check label for this case (except first which starts immediately)
						instructions.Add(new HirLabel(checkLabels[i]));

						// Check pattern (simplified: just check tag for enums)
						var pattern = matchCase.Patterns[0];
						if (pattern is IdentifierExpr { Name: var caseName }) {
							// Assume enum case - compare tag
							var tagPtr = NewValue();
							instructions.Add(new HirGetFieldPtr(tagPtr, scrutinee, "_tag", 0));
							var tag = NewValue();
							instructions.Add(new HirLoad(tag, tagPtr, new HirIntType()));

							// Get expected tag value
							var enumType = InferExprType(matchStmt.Scrutinee);
							var expectedTag = GetEnumTag(enumType, caseName);
							var expectedVal = NewValue();
							instructions.Add(new HirConstInt(expectedVal, expectedTag));

							var cmp = NewValue();
							instructions.Add(new HirCmpEq(cmp, tag, expectedVal));
							instructions.Add(new HirBrCond(cmp, caseLabel, nextLabel));
						}

						instructions.Add(new HirLabel(caseLabel));

						// Handle pattern bindings
						var binding = matchCase.PatternBindings[0];
						if (binding != null) {
							var scrutineeType = InferExprType(matchStmt.Scrutinee);
							var tagSize = scrutineeType is HirEnumType et ? et.TagSize : 8;
							for (var j = 0; j < binding.Bindings.Count; j++) {
								var bindingName = binding.Bindings[j];
								var payloadPtr = NewValue();
								instructions.Add(new HirGetFieldPtr(payloadPtr, scrutinee, $"_payload_{j}", tagSize + j * 8));
								var payloadVal = NewValue();
								instructions.Add(new HirLoad(payloadVal, payloadPtr, new HirIntType()));

								// Allocate slot for binding and store value
								var bindingSlot = NewValue();
								instructions.Add(new HirAlloca(bindingSlot, new HirIntType()));
								instructions.Add(new HirStore(bindingSlot, payloadVal, new HirIntType()));
								_variableSlots[bindingName] = bindingSlot;
								_variableTypes[bindingName] = new HirIntType();
							}
						}

						LowerStatements(matchCase.Body, instructions);
						instructions.Add(new HirBr(endLabel));
					}

					if (matchStmt.DefaultBody != null) {
						instructions.Add(new HirLabel(defaultLabel));
						LowerStatements(matchStmt.DefaultBody, instructions);
						instructions.Add(new HirBr(endLabel));
					}

					instructions.Add(new HirLabel(endLabel));
					break;
				}
		}
	}

	private HirValue LowerExpression(Expr expr, List<HirInstr> instructions) {
		switch (expr) {
			case IntLiteralExpr intLit: {
					var dest = NewValue();
					instructions.Add(new HirConstInt(dest, intLit.Value));
					return dest;
				}

			case FloatLiteralExpr floatLit: {
					var dest = NewValue();
					instructions.Add(new HirConstFloat(dest, floatLit.Value));
					return dest;
				}

			case BoolLiteralExpr boolLit: {
					var dest = NewValue();
					instructions.Add(new HirConstBool(dest, boolLit.Value));
					return dest;
				}

			case IdentifierExpr id: {
					if (_variableSlots.TryGetValue(id.Name, out var slotPtr)) {
						var varType = _variableTypes.GetValueOrDefault(id.Name, new HirIntType());
						// For struct and enum types, return the slot pointer directly (accessed via pointer)
						if (varType is HirStructType or HirEnumType) {
							return slotPtr;
						}
						// For primitive types, load the value from the stack slot
						var dest = NewValue();
						instructions.Add(new HirLoad(dest, slotPtr, varType));
						return dest;
					}

					// Check if it's an implicit field access (inside instance method)
					if (_currentTypeName != null && _structs.TryGetValue(_currentTypeName, out var structDef)) {
						var field = structDef.Fields.Find(f => f.FieldName == id.Name);
						if (field != null && _variableSlots.TryGetValue("self", out var selfSlotPtr)) {
							// Load self first (self is a pointer, so load it)
							var selfValue = NewValue();
							instructions.Add(new HirLoad(selfValue, selfSlotPtr, new HirPtrType()));
							// Generate self.field access
							var fieldPtr = NewValue();
							instructions.Add(new HirGetFieldPtr(fieldPtr, selfValue, id.Name, field.Offset));
							var dest = NewValue();
							instructions.Add(new HirLoad(dest, fieldPtr, field.Type));
							return dest;
						}
					}

					throw new CompileError(ErrorCode.HirUndefinedVariable, $"Unknown variable: {id.Name}");
				}

			case BinaryExpr binary: {
					var left = LowerExpression(binary.Left, instructions);
					var right = LowerExpression(binary.Right, instructions);
					var dest = NewValue();

					// Check if operands are floats - use float arithmetic instructions
					var leftType = InferExprType(binary.Left);
					var isFloat = leftType is HirFloatType;

					HirInstr instr;
					if (isFloat) {
						instr = binary.Op switch {
							BinaryOp.Add => new HirFAdd(dest, left, right),
							BinaryOp.Sub => new HirFSub(dest, left, right),
							BinaryOp.Mul => new HirFMul(dest, left, right),
							BinaryOp.Div => new HirFDiv(dest, left, right),
							_ => throw new CompileError(ErrorCode.HirUnsupportedExpression, $"Float binary op not supported: {binary.Op}")
						};
					} else {
						instr = binary.Op switch {
							BinaryOp.Add => new HirAdd(dest, left, right),
							BinaryOp.Sub => new HirSub(dest, left, right),
							BinaryOp.Mul => new HirMul(dest, left, right),
							BinaryOp.Div => new HirDiv(dest, left, right),
							BinaryOp.Mod => new HirMod(dest, left, right),
							BinaryOp.Band => new HirBand(dest, left, right),
							BinaryOp.Bor => new HirBor(dest, left, right),
							BinaryOp.Bxor => new HirBxor(dest, left, right),
							BinaryOp.Shl => new HirShl(dest, left, right),
							BinaryOp.Shr => new HirShr(dest, left, right),
							_ => throw new CompileError(ErrorCode.HirUnsupportedExpression, $"Unknown binary op: {binary.Op}")
						};
					}
					instructions.Add(instr);
					return dest;
				}

			case CompareExpr compare: {
					var left = LowerExpression(compare.Left, instructions);
					var right = LowerExpression(compare.Right, instructions);
					var dest = NewValue();

					var instr = compare.Op switch {
						CompareOp.Eq => (HirInstr)new HirCmpEq(dest, left, right),
						CompareOp.Ne => new HirCmpNe(dest, left, right),
						CompareOp.Lt => new HirCmpLt(dest, left, right),
						CompareOp.Le => new HirCmpLe(dest, left, right),
						CompareOp.Gt => new HirCmpGt(dest, left, right),
						CompareOp.Ge => new HirCmpGe(dest, left, right),
						_ => throw new CompileError(ErrorCode.HirUnsupportedExpression, $"Unknown compare op: {compare.Op}")
					};
					instructions.Add(instr);
					return dest;
				}

			case LogicalExpr logical: {
					// Implement short-circuit evaluation with branches
					var dest = NewValue();
					var resultSlot = NewValue();
					instructions.Add(new HirAlloca(resultSlot, new HirBoolType()));

					var left = LowerExpression(logical.Left, instructions);

					var evalRight = NewLabel("logical_eval_right");
					var done = NewLabel("logical_done");

					if (logical.Op == LogicalOp.And) {
						// Short-circuit AND: if left is false, result is false
						// Store left result first (if false, this is the final result)
						instructions.Add(new HirStore(resultSlot, left, new HirBoolType()));
						instructions.Add(new HirBrCond(left, evalRight, done));
					} else {
						// Short-circuit OR: if left is true, result is true
						instructions.Add(new HirStore(resultSlot, left, new HirBoolType()));
						instructions.Add(new HirBrCond(left, done, evalRight));
					}

					// Evaluate right side
					instructions.Add(new HirLabel(evalRight));
					var right = LowerExpression(logical.Right, instructions);
					instructions.Add(new HirStore(resultSlot, right, new HirBoolType()));
					instructions.Add(new HirBr(done));

					instructions.Add(new HirLabel(done));
					instructions.Add(new HirLoad(dest, resultSlot, new HirBoolType()));
					return dest;
				}

			case UnaryExpr unary: {
					var operand = LowerExpression(unary.Operand, instructions);
					var dest = NewValue();

					var instr = unary.Op switch {
						UnaryOp.Negate => (HirInstr)new HirNeg(dest, operand),
						UnaryOp.Not => new HirNot(dest, operand),
						_ => throw new CompileError(ErrorCode.HirUnsupportedExpression, $"Unknown unary op: {unary.Op}")
					};
					instructions.Add(instr);
					return dest;
				}

			case CallExpr call: {
					var args = new List<HirValue>();
					for (var i = 0; i < call.Args.Count; i++) {
						var arg = call.Args[i];
						args.Add(LowerExpression(arg, instructions));

						// Track ownership transfer for variable arguments
						if (arg is IdentifierExpr argId && _resources?.HasVariable(argId.Name) == true) {
							var loc = arg.Location ?? new SourceLocation(0, 0);
							_resources.PassToFunction(argId.Name, call.FuncName, i, loc);
						}
					}
					foreach (var namedArg in call.NamedArgs) {
						args.Add(LowerExpression(namedArg.Value, instructions));
					}

					var retType = GetFunctionReturnType(call.FuncName);
					HirValue? dest = retType is HirVoidType ? null : NewValue();

					instructions.Add(new HirCall(dest, call.FuncName, args, retType));
					return dest ?? NewValue();
				}

			case MethodCallExpr methodCall: {
					var baseType = InferExprType(methodCall.Base);

					// For struct/enum types, pass pointer (address) as self, not the value
					HirValue baseVal;
					if (baseType is HirStructType or HirEnumType) {
						baseVal = LowerExpressionAsAddress(methodCall.Base, instructions);
					} else {
						baseVal = LowerExpression(methodCall.Base, instructions);
					}
					var args = new List<HirValue> { baseVal };

					foreach (var arg in methodCall.Args) {
						var argType = InferExprType(arg);
						// Pass struct/enum arguments by pointer
						if (argType is HirStructType or HirEnumType) {
							args.Add(LowerExpressionAsAddress(arg, instructions));
						} else {
							args.Add(LowerExpression(arg, instructions));
						}
					}
					foreach (var namedArg in methodCall.NamedArgs) {
						var argType = InferExprType(namedArg.Value);
						// Pass struct/enum arguments by pointer
						if (argType is HirStructType or HirEnumType) {
							args.Add(LowerExpressionAsAddress(namedArg.Value, instructions));
						} else {
							args.Add(LowerExpression(namedArg.Value, instructions));
						}
					}

					var funcName = $"{baseType.Name}.{methodCall.MethodName}";
					var retType = GetMethodReturnType(baseType.Name, methodCall.MethodName);

					HirValue? dest = retType is HirVoidType ? null : NewValue();
					instructions.Add(new HirCall(dest, funcName, args, retType));
					return dest ?? NewValue();
				}

			case FieldAccessExpr fieldAccess: {
					// Check if this is an enum case access: EnumName.case (no args)
					if (fieldAccess.Base is IdentifierExpr baseId && _enums.TryGetValue(baseId.Name, out var enumDef)) {
						var variant = enumDef.Variants.Find(v => v.Name == fieldAccess.FieldName)
							?? throw new CompileError(ErrorCode.HirUndefinedVariable, $"Unknown enum case: {fieldAccess.FieldName}");

						var enumType = new HirEnumType(baseId.Name, enumDef.TagSize, enumDef.MaxPayloadSize);
						var ptr = NewValue();
						instructions.Add(new HirAlloca(ptr, enumType));

						// Store tag
						var tagVal = NewValue();
						instructions.Add(new HirConstInt(tagVal, variant.Tag));
						var tagPtr = NewValue();
						instructions.Add(new HirGetFieldPtr(tagPtr, ptr, "_tag", 0));
						instructions.Add(new HirStore(tagPtr, tagVal, new HirIntType()));

						return ptr;
					}

					var baseVal = LowerExpression(fieldAccess.Base, instructions);
					var offset = GetFieldOffset(fieldAccess.Base, fieldAccess.FieldName);
					var fieldType = GetFieldType(fieldAccess.Base, fieldAccess.FieldName);

					var fieldPtr = NewValue();
					instructions.Add(new HirGetFieldPtr(fieldPtr, baseVal, fieldAccess.FieldName, offset));

					var dest = NewValue();
					instructions.Add(new HirLoad(dest, fieldPtr, fieldType));
					return dest;
				}

			case StructInitExpr structInit: {
					var typeName = structInit.TypeName ?? throw new CompileError(ErrorCode.HirUnsupportedExpression, "Anonymous struct init not supported");
					if (!_structs.TryGetValue(typeName, out var structDef)) {
						throw new CompileError(ErrorCode.HirUndefinedType, $"Unknown struct type: {typeName}");
					}

					// Allocate space for struct
					var structType = new HirStructType(typeName, structDef.Fields);
					var ptr = NewValue();
					instructions.Add(new HirAlloca(ptr, structType));

					// Begin struct literal for deferred move tracking
					_resources?.BeginStructLiteral();

					// Initialize fields
					foreach (var fieldInit in structInit.Fields) {
						var field = structDef.Fields.Find(f => f.FieldName == fieldInit.Name)
							?? throw new CompileError(ErrorCode.HirInvalidFieldAccess, $"Unknown field: {fieldInit.Name}");

						var value = LowerExpression(fieldInit.Value, instructions);
						var fieldPtr = NewValue();
						instructions.Add(new HirGetFieldPtr(fieldPtr, ptr, fieldInit.Name, field.Offset));
						instructions.Add(new HirStore(fieldPtr, value, field.Type));
					}

					// End struct literal - apply deferred moves
					_resources?.EndStructLiteral();

					return ptr;
				}

			case StaticCallExpr staticCall: {
					// Check if it's an enum case
					if (_enums.TryGetValue(staticCall.TypeName, out var enumDef)) {
						var variant = enumDef.Variants.Find(v => v.Name == staticCall.MemberName)
							?? throw new CompileError(ErrorCode.HirUndefinedVariable, $"Unknown enum case: {staticCall.MemberName}");

						var enumType = new HirEnumType(staticCall.TypeName, enumDef.TagSize, enumDef.MaxPayloadSize);
						var ptr = NewValue();
						instructions.Add(new HirAlloca(ptr, enumType));

						// Store tag
						var tagVal = NewValue();
						instructions.Add(new HirConstInt(tagVal, variant.Tag));
						var tagPtr = NewValue();
						instructions.Add(new HirGetFieldPtr(tagPtr, ptr, "_tag", 0));
						instructions.Add(new HirStore(tagPtr, tagVal, new HirIntType()));

						// Store payload
						for (var i = 0; i < staticCall.Args.Count; i++) {
							var argVal = LowerExpression(staticCall.Args[i], instructions);
							var payloadPtr = NewValue();
							instructions.Add(new HirGetFieldPtr(payloadPtr, ptr, $"_payload_{i}", enumDef.TagSize + i * 8));
							instructions.Add(new HirStore(payloadPtr, argVal, new HirIntType()));
						}

						return ptr;
					}

					// Check if TypeName is actually a variable (e.g., p1.add(...) where p1 is a Point)
					// In that case, treat this as a method call
					if (_variableTypes.TryGetValue(staticCall.TypeName, out var varType)) {
						// This is a method call on a variable
						var baseIdent = new IdentifierExpr(staticCall.TypeName);

						// For struct/enum types, pass pointer (address) as self, not the value
						HirValue baseVal;
						if (varType is HirStructType or HirEnumType) {
							baseVal = LowerExpressionAsAddress(baseIdent, instructions);
						} else {
							baseVal = LowerExpression(baseIdent, instructions);
						}
						var args = new List<HirValue> { baseVal };

						foreach (var arg in staticCall.Args) {
							var argType = InferExprType(arg);
							// Pass struct/enum arguments by pointer
							if (argType is HirStructType or HirEnumType) {
								args.Add(LowerExpressionAsAddress(arg, instructions));
							} else {
								args.Add(LowerExpression(arg, instructions));
							}
						}
						foreach (var namedArg in staticCall.NamedArgs) {
							var argType = InferExprType(namedArg.Value);
							// Pass struct/enum arguments by pointer
							if (argType is HirStructType or HirEnumType) {
								args.Add(LowerExpressionAsAddress(namedArg.Value, instructions));
							} else {
								args.Add(LowerExpression(namedArg.Value, instructions));
							}
						}

						var funcName = $"{varType.Name}.{staticCall.MemberName}";
						var retType = GetMethodReturnType(varType.Name, staticCall.MemberName);

						HirValue? dest = retType is HirVoidType ? null : NewValue();
						instructions.Add(new HirCall(dest, funcName, args, retType));
						return dest ?? NewValue();
					}

					// It's a static method call on a type
					// Use dot notation to match function names (e.g., "Point.create")
					var funcName2 = $"{staticCall.TypeName}.{staticCall.MemberName}";
					var result = NewValue();

					// Lower arguments (both positional and named)
					var argVals = new List<HirValue>();
					foreach (var arg in staticCall.Args) {
						argVals.Add(LowerExpression(arg, instructions));
					}
					foreach (var namedArg in staticCall.NamedArgs) {
						argVals.Add(LowerExpression(namedArg.Value, instructions));
					}

					var returnType = GetFunctionReturnType(funcName2);
					instructions.Add(new HirCall(result, funcName2, argVals, returnType));
					return result;
				}

			case EnumCaseExpr enumCase: {
					// Create enum value with tag and payload
					if (!_enums.TryGetValue(enumCase.EnumName, out var enumDef)) {
						throw new CompileError(ErrorCode.HirUndefinedType, $"Unknown enum: {enumCase.EnumName}");
					}

					var variant = enumDef.Variants.Find(v => v.Name == enumCase.CaseName)
						?? throw new CompileError(ErrorCode.HirUndefinedVariable, $"Unknown enum case: {enumCase.CaseName}");

					var enumType = new HirEnumType(enumCase.EnumName, enumDef.TagSize, enumDef.MaxPayloadSize);
					var ptr = NewValue();
					instructions.Add(new HirAlloca(ptr, enumType));

					// Store tag
					var tagVal = NewValue();
					instructions.Add(new HirConstInt(tagVal, variant.Tag));
					var tagPtr = NewValue();
					instructions.Add(new HirGetFieldPtr(tagPtr, ptr, "_tag", 0));
					instructions.Add(new HirStore(tagPtr, tagVal, new HirIntType()));

					// Store payload
					for (var i = 0; i < enumCase.Args.Count; i++) {
						var argVal = LowerExpression(enumCase.Args[i], instructions);
						var payloadPtr = NewValue();
						instructions.Add(new HirGetFieldPtr(payloadPtr, ptr, $"_payload_{i}", enumDef.TagSize + i * 8));
						instructions.Add(new HirStore(payloadPtr, argVal, new HirIntType()));
					}

					return ptr;
				}

			case TryExpr tryExpr: {
					// For now, just lower the inner expression
					// Real implementation would check for errors
					var innerVal = LowerExpression(tryExpr.Expression, instructions);

					if (tryExpr.Otherwise != null) {
						// Generate fallback logic if needed
						// For simplicity, just return inner value for now
					}

					return innerVal;
				}

			default:
				throw new CompileError(ErrorCode.HirUnsupportedExpression, $"Unsupported expression type: {expr.GetType().Name}");
		}
	}

	private HirType InferExprType(Expr expr) {
		return expr switch {
			IntLiteralExpr => new HirIntType(),
			FloatLiteralExpr => new HirFloatType(),
			BoolLiteralExpr => new HirBoolType(),
			CharLiteralExpr => new HirByteType(),
			IdentifierExpr id => _variableTypes.GetValueOrDefault(id.Name, new HirIntType()),
			BinaryExpr b => InferExprType(b.Left), // Type is determined by operands
			CompareExpr => new HirBoolType(),
			LogicalExpr => new HirBoolType(),
			UnaryExpr u => InferExprType(u.Operand),
			CallExpr call => GetFunctionReturnType(call.FuncName),
			MethodCallExpr mc => GetMethodReturnType(InferExprType(mc.Base).Name, mc.MethodName),
			FieldAccessExpr fa when fa.Base is IdentifierExpr baseId && _enums.ContainsKey(baseId.Name) =>
				new HirEnumType(baseId.Name, _enums[baseId.Name].TagSize, _enums[baseId.Name].MaxPayloadSize),
			FieldAccessExpr fa => GetFieldType(fa.Base, fa.FieldName),
			StructInitExpr si => si.TypeName != null && _structs.TryGetValue(si.TypeName, out var sd)
				? new HirStructType(si.TypeName, sd.Fields)
				: new HirPtrType(),
			StaticCallExpr sc => _enums.TryGetValue(sc.TypeName, out var ed)
				? new HirEnumType(sc.TypeName, ed.TagSize, ed.MaxPayloadSize)
				: _variableTypes.TryGetValue(sc.TypeName, out var varType)
					? GetMethodReturnType(varType.Name, sc.MemberName)
					: GetFunctionReturnType($"{sc.TypeName}.{sc.MemberName}"),
			EnumCaseExpr ec => _enums.TryGetValue(ec.EnumName, out var ed2)
				? new HirEnumType(ec.EnumName, ed2.TagSize, ed2.MaxPayloadSize)
				: new HirPtrType(),
			TryExpr te => InferExprType(te.Expression),
			_ => new HirIntType()
		};
	}

	private HirType LowerType(TypeRef? typeRef) {
		return typeRef switch {
			SimpleTypeRef { Name: "int" } => new HirIntType(),
			SimpleTypeRef { Name: "float" } => new HirFloatType(),
			SimpleTypeRef { Name: "bool" } => new HirBoolType(),
			SimpleTypeRef { Name: "byte" } => new HirByteType(),
			SimpleTypeRef { Name: "string" } => new HirPtrType(),
			SimpleTypeRef { Name: var name } when _structs.ContainsKey(name) =>
				new HirStructType(name, _structs[name].Fields),
			SimpleTypeRef { Name: var name } when _enums.ContainsKey(name) =>
				new HirEnumType(name, _enums[name].TagSize, _enums[name].MaxPayloadSize),
			SimpleTypeRef { Name: _ } => new HirPtrType(), // Default for unknown types
			null => new HirVoidType(),
			_ => throw new CompileError(ErrorCode.HirUndefinedType, $"Unsupported type: {typeRef.GetType().Name}")
		};
	}

	private int GetFieldOffset(Expr baseExpr, string fieldName) {
		var baseType = InferExprType(baseExpr);
		if (baseType is HirStructType st) {
			var field = st.Fields.Find(f => f.FieldName == fieldName);
			return field?.Offset ?? 0;
		}
		if (baseType.Name is { } typeName && _structs.TryGetValue(typeName, out var structDef)) {
			var field = structDef.Fields.Find(f => f.FieldName == fieldName);
			return field?.Offset ?? 0;
		}
		return 0;
	}

	private HirType GetFieldType(Expr baseExpr, string fieldName) {
		var baseType = InferExprType(baseExpr);
		if (baseType is HirStructType st) {
			var field = st.Fields.Find(f => f.FieldName == fieldName);
			return field?.Type ?? new HirIntType();
		}
		if (baseType.Name is { } typeName && _structs.TryGetValue(typeName, out var structDef)) {
			var field = structDef.Fields.Find(f => f.FieldName == fieldName);
			return field?.Type ?? new HirIntType();
		}
		return new HirIntType();
	}

	private HirType GetFunctionReturnType(string funcName) {
		// Built-in functions
		if (funcName is "print") return new HirVoidType();
		// Look up in collected function signatures
		if (_functionReturnTypes.TryGetValue(funcName, out var retType)) {
			return retType;
		}
		// Default to int for unknown functions
		return new HirIntType();
	}

	private HirType GetMethodReturnType(string typeName, string methodName) {
		var funcName = $"{typeName}.{methodName}";
		if (_functionReturnTypes.TryGetValue(funcName, out var retType)) {
			return retType;
		}
		// Default to int for unknown methods
		return new HirIntType();
	}

	private int GetEnumTag(HirType type, string caseName) {
		if (type is HirEnumType et && _enums.TryGetValue(et.EnumName, out var enumDef)) {
			var variant = enumDef.Variants.Find(v => v.Name == caseName);
			return variant?.Tag ?? 0;
		}
		return 0;
	}

	/// <summary>
	/// Gets the address of an lvalue expression (for passing structs by reference).
	/// For variables, returns the slot pointer directly.
	/// For other expressions, allocates a temp and returns its address.
	/// </summary>
	private HirValue LowerExpressionAsAddress(Expr expr, List<HirInstr> instructions) {
		switch (expr) {
			case IdentifierExpr id:
				if (_variableSlots.TryGetValue(id.Name, out var slotPtr)) {
					return slotPtr;
				}
				break;
			case StructInitExpr: {
					// StructInitExpr already allocates a temp and returns its address
					var initPtr = LowerExpression(expr, instructions);
					return initPtr;
				}
		}
		// Fallback: evaluate expression and store to temp
		var value = LowerExpression(expr, instructions);
		var exprType = InferExprType(expr);
		var tempPtr = NewValue();
		instructions.Add(new HirAlloca(tempPtr, exprType));
		if (exprType is HirStructType structType) {
			// The value is already a pointer to the struct data, use memcpy
			instructions.Add(new HirMemcpy(tempPtr, value, structType.SizeInBytes));
		} else if (exprType is HirEnumType enumType) {
			// The value is already a pointer to the enum data, use memcpy
			instructions.Add(new HirMemcpy(tempPtr, value, enumType.SizeInBytes));
		} else {
			instructions.Add(new HirStore(tempPtr, value, exprType));
		}
		return tempPtr;
	}

	private HirValue NewValue() => new(_nextValueId++);
	private string NewLabel(string prefix) => $"{prefix}_{_nextLabelId++}";
}
