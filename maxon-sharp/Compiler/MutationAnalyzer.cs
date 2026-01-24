namespace MaxonSharp.Compiler;

/// <summary>
/// Analyzes which function parameters are mutated.
/// A parameter is considered mutated if:
/// 1. It is directly assigned to
/// 2. It is assigned to a mutable (var) field in a struct literal
/// 3. It is assigned to a mutable local variable
/// 4. It is passed to another function that mutates its corresponding parameter
/// </summary>
public class MutationAnalyzer {
	// Maps "functionName:paramIndex" → true if mutated
	private readonly Dictionary<string, bool> _mutatedParams = [];
	// Maps "functionName:paramName" → paramIndex for lookup
	private readonly Dictionary<string, int> _paramIndices = [];
	// Maps variable name → parameter name if the variable holds a parameter value
	private readonly Dictionary<string, string> _varToParam = [];
	// Tracks which params are mutable (var) bindings
	private readonly HashSet<string> _mutableBindings = [];

	/// <summary>
	/// Analyze entire program, populate _mutatedParams.
	/// </summary>
	public void Analyze(ProgramAst program) {
		Logger.Debug(LogCategory.Semantic, "Starting mutation analysis");

		// First pass: collect function signatures
		foreach (var func in program.Functions) {
			for (var i = 0; i < func.Params.Count; i++) {
				var key = $"{func.Name}:{func.Params[i].Name}";
				_paramIndices[key] = i;
			}
		}

		foreach (var type in program.Types) {
			foreach (var method in type.Methods) {
				var funcName = $"{type.Name}.{method.Name}";
				var paramOffset = method.IsStatic ? 0 : 1; // 'self' param for instance methods
				for (var i = 0; i < method.Params.Count; i++) {
					var key = $"{funcName}:{method.Params[i].Name}";
					_paramIndices[key] = i + paramOffset;
				}
			}
		}

		// Second pass: analyze function bodies for mutations
		foreach (var func in program.Functions) {
			AnalyzeFunction(func.Name, func.Params, func.Body);
		}

		foreach (var type in program.Types) {
			foreach (var method in type.Methods) {
				var funcName = $"{type.Name}.{method.Name}";
				AnalyzeFunction(funcName, method.Params, method.Body);
			}
		}

		Logger.Debug(LogCategory.Semantic, $"Mutation analysis complete: {_mutatedParams.Count(kv => kv.Value)} mutated params");
	}

	/// <summary>
	/// Query: does this function mutate its param at the given index?
	/// </summary>
	public bool IsMutated(string functionName, int paramIndex) {
		var key = $"{functionName}:{paramIndex}";
		return _mutatedParams.TryGetValue(key, out var mutated) && mutated;
	}

	/// <summary>
	/// Query: does this function mutate its param with the given name?
	/// </summary>
	public bool IsMutatedByName(string functionName, string paramName) {
		var key = $"{functionName}:{paramName}";
		if (_paramIndices.TryGetValue(key, out var index)) {
			return IsMutated(functionName, index);
		}
		return false;
	}

	private void AnalyzeFunction(string funcName, List<ParamDecl> parameters, List<Stmt> body) {
		// Clear per-function state
		_varToParam.Clear();
		_mutableBindings.Clear();

		// Initialize parameter tracking
		for (var i = 0; i < parameters.Count; i++) {
			var param = parameters[i];
			_varToParam[param.Name] = param.Name;
		}

		// Analyze body for mutations
		foreach (var stmt in body) {
			AnalyzeStatement(funcName, stmt);
		}
	}

	private void AnalyzeStatement(string funcName, Stmt stmt) {
		switch (stmt) {
			case LetDeclStmt letDecl:
				// let binding - immutable, doesn't propagate mutation
				// But track if the value comes from a param
				if (letDecl.Value is IdentifierExpr id && _varToParam.TryGetValue(id.Name, out var paramName)) {
					// Immutable binding - doesn't count as mutation
					// But we should still track that this var holds the param value
					// (for detecting if it's passed to a mutating function)
					_varToParam[letDecl.Name] = paramName;
				}
				AnalyzeExpr(funcName, letDecl.Value);
				break;

			case VarDeclStmt varDecl:
				// var binding - mutable, assigning param to mutable var counts as mutation
				if (varDecl.Value is IdentifierExpr varId && _varToParam.TryGetValue(varId.Name, out var varParamName)) {
					// Mutable binding to a parameter - this counts as mutation
					MarkMutated(funcName, varParamName);
					_varToParam[varDecl.Name] = varParamName;
					_mutableBindings.Add(varDecl.Name);
				}
				AnalyzeExpr(funcName, varDecl.Value);
				break;

			case AssignStmt assign:
				// Direct assignment to a parameter marks it as mutated
				if (_varToParam.TryGetValue(assign.Target, out var assignParamName)) {
					MarkMutated(funcName, assignParamName);
				}
				AnalyzeExpr(funcName, assign.Value);
				break;

			case FieldAssignStmt fieldAssign:
				// Field assignment on a param marks it as mutated
				if (fieldAssign.Base is IdentifierExpr baseId && _varToParam.TryGetValue(baseId.Name, out var fieldParamName)) {
					MarkMutated(funcName, fieldParamName);
				}
				AnalyzeExpr(funcName, fieldAssign.Base);
				AnalyzeExpr(funcName, fieldAssign.Value);
				break;

			case IndexAssignStmt indexAssign:
				// Index assignment on a param marks it as mutated
				if (indexAssign.Base is IdentifierExpr indexBaseId && _varToParam.TryGetValue(indexBaseId.Name, out var indexParamName)) {
					MarkMutated(funcName, indexParamName);
				}
				AnalyzeExpr(funcName, indexAssign.Base);
				AnalyzeExpr(funcName, indexAssign.Index);
				AnalyzeExpr(funcName, indexAssign.Value);
				break;

			case ExprStmt exprStmt:
				AnalyzeExpr(funcName, exprStmt.Expression);
				break;

			case ReturnStmt ret:
				if (ret.Value != null) {
					AnalyzeExpr(funcName, ret.Value);
				}
				break;

			case IfStmt ifStmt:
				AnalyzeExpr(funcName, ifStmt.Condition);
				foreach (var s in ifStmt.ThenBody) {
					AnalyzeStatement(funcName, s);
				}
				if (ifStmt.ElseBody != null) {
					foreach (var s in ifStmt.ElseBody) {
						AnalyzeStatement(funcName, s);
					}
				}
				break;

			case WhileStmt whileStmt:
				AnalyzeExpr(funcName, whileStmt.Condition);
				foreach (var s in whileStmt.Body) {
					AnalyzeStatement(funcName, s);
				}
				break;

			case ForStmt forStmt:
				AnalyzeExpr(funcName, forStmt.Iterable);
				foreach (var s in forStmt.Body) {
					AnalyzeStatement(funcName, s);
				}
				break;

			case MatchStmt matchStmt:
				AnalyzeExpr(funcName, matchStmt.Scrutinee);
				foreach (var matchCase in matchStmt.Cases) {
					foreach (var pattern in matchCase.Patterns) {
						AnalyzeExpr(funcName, pattern);
					}
					foreach (var s in matchCase.Body) {
						AnalyzeStatement(funcName, s);
					}
				}
				if (matchStmt.DefaultBody != null) {
					foreach (var s in matchStmt.DefaultBody) {
						AnalyzeStatement(funcName, s);
					}
				}
				break;
		}
	}

	private void AnalyzeExpr(string funcName, Expr expr) {
		switch (expr) {
			case CallExpr call:
				// Check if any param is passed to a mutating function
				for (var i = 0; i < call.Args.Count; i++) {
					if (call.Args[i] is IdentifierExpr argId && _varToParam.TryGetValue(argId.Name, out var paramName)) {
						// If callee mutates this param position, mark our param as mutated
						if (IsMutated(call.FuncName, i)) {
							MarkMutated(funcName, paramName);
						}
					}
					AnalyzeExpr(funcName, call.Args[i]);
				}
				foreach (var namedArg in call.NamedArgs) {
					AnalyzeExpr(funcName, namedArg.Value);
				}
				break;

			case MethodCallExpr methodCall:
				AnalyzeExpr(funcName, methodCall.Base);
				foreach (var arg in methodCall.Args) {
					AnalyzeExpr(funcName, arg);
				}
				foreach (var namedArg in methodCall.NamedArgs) {
					AnalyzeExpr(funcName, namedArg.Value);
				}
				break;

			case StaticCallExpr staticCall:
				foreach (var arg in staticCall.Args) {
					AnalyzeExpr(funcName, arg);
				}
				foreach (var namedArg in staticCall.NamedArgs) {
					AnalyzeExpr(funcName, namedArg.Value);
				}
				break;

			case StructInitExpr structInit:
				// Check if any field is mutable and receives a param value
				// For now, we can't easily know which fields are mutable without type info
				// This would need to be enhanced with type context
				foreach (var field in structInit.Fields) {
					AnalyzeExpr(funcName, field.Value);
				}
				break;

			case BinaryExpr binary:
				AnalyzeExpr(funcName, binary.Left);
				AnalyzeExpr(funcName, binary.Right);
				break;

			case CompareExpr compare:
				AnalyzeExpr(funcName, compare.Left);
				AnalyzeExpr(funcName, compare.Right);
				break;

			case LogicalExpr logical:
				AnalyzeExpr(funcName, logical.Left);
				AnalyzeExpr(funcName, logical.Right);
				break;

			case UnaryExpr unary:
				AnalyzeExpr(funcName, unary.Operand);
				break;

			case FieldAccessExpr fieldAccess:
				AnalyzeExpr(funcName, fieldAccess.Base);
				break;

			case IndexExpr indexExpr:
				AnalyzeExpr(funcName, indexExpr.Base);
				AnalyzeExpr(funcName, indexExpr.Index);
				break;

			case TryExpr tryExpr:
				AnalyzeExpr(funcName, tryExpr.Expression);
				if (tryExpr.Otherwise?.DefaultExpr != null) {
					AnalyzeExpr(funcName, tryExpr.Otherwise.DefaultExpr);
				}
				break;

			case ArrayLiteralExpr arrayLit:
				foreach (var elem in arrayLit.Elements) {
					AnalyzeExpr(funcName, elem);
				}
				break;

			case RangeExpr range:
				AnalyzeExpr(funcName, range.Start);
				AnalyzeExpr(funcName, range.End);
				break;

			// Literals and identifiers don't need recursion
			case IntLiteralExpr:
			case FloatLiteralExpr:
			case BoolLiteralExpr:
			case StringLiteralExpr:
			case CharLiteralExpr:
			case IdentifierExpr:
			case SelfExpr:
				break;
		}
	}

	private void MarkMutated(string funcName, string paramName) {
		var key = $"{funcName}:{paramName}";
		if (_paramIndices.TryGetValue(key, out var index)) {
			var mutKey = $"{funcName}:{index}";
			_mutatedParams[mutKey] = true;
			Logger.Debug(LogCategory.Semantic, $"Marked param {paramName} (index {index}) of {funcName} as mutated");
		}
	}
}
