namespace MaxonSharp.Compiler;

public class Parser(List<Token> tokens, int sourceFileIndex) {
	private readonly List<Token> _tokens = tokens;
	private readonly int _sourceFileIndex = sourceFileIndex;
	private int _pos;

	private static readonly Dictionary<string, BuiltinOp> BuiltinFunctions = new() {
		["trunc"] = BuiltinOp.Trunc,
		["sqrt"] = BuiltinOp.Sqrt,
		["floor"] = BuiltinOp.Floor,
		["ceil"] = BuiltinOp.Ceil,
		["round"] = BuiltinOp.Round,
		["abs"] = BuiltinOp.Abs,
		["min"] = BuiltinOp.Min,
		["max"] = BuiltinOp.Max,
	};

	public ProgramAst Parse() {
		Logger.Debug(LogCategory.Parser, "Starting parser");
		var types = new List<TypeDecl>();
		var enums = new List<EnumDecl>();
		var interfaces = new List<InterfaceDecl>();
		var extensions = new List<ExtensionDecl>();
		var functions = new List<FunctionDecl>();
		var globalConstants = new List<GlobalConstant>();
		var globalVariables = new List<GlobalVariable>();
		var typeAliases = new List<TypeAliasDecl>();

		SkipNewlines();
		while (!IsAtEnd() && Current().Type != TokenType.Eof) {
			var isExport = false;
			if (Check(TokenType.Export)) {
				Advance();
				isExport = true;
			}
			ParseTopLevelDecl(isExport, types, enums, interfaces, extensions, functions, globalConstants, globalVariables, typeAliases);
			SkipNewlines();
		}

		Logger.Debug(LogCategory.Parser, $"Parser complete: {functions.Count} functions, {types.Count} types");
		return new ProgramAst(types, enums, interfaces, extensions, functions, globalConstants, globalVariables, typeAliases);
	}

	private void ParseTopLevelDecl(
		bool isExport,
		List<TypeDecl> types,
		List<EnumDecl> enums,
		List<InterfaceDecl> interfaces,
		List<ExtensionDecl> extensions,
		List<FunctionDecl> functions,
		List<GlobalConstant> globalConstants,
		List<GlobalVariable> globalVariables,
		List<TypeAliasDecl> typeAliases) {
		if (Check(TokenType.Type)) {
			types.Add(ParseTypeDecl(isExport));
		} else if (Check(TokenType.TypeAlias)) {
			typeAliases.Add(ParseTypeAliasDecl(isExport));
		} else if (Check(TokenType.Interface)) {
			interfaces.Add(ParseInterfaceDecl(isExport));
		} else if (Check(TokenType.Extension)) {
			extensions.Add(ParseExtensionDecl(isExport));
		} else if (Check(TokenType.Enum)) {
			enums.Add(ParseEnumDecl(isExport));
		} else if (Check(TokenType.Let)) {
			globalConstants.Add(ParseGlobalConstant(isExport));
		} else if (Check(TokenType.Var)) {
			globalVariables.Add(ParseGlobalVariable(isExport));
		} else if (Check(TokenType.Function)) {
			functions.Add(ParseFunction(isExport));
		} else {
			var msg = isExport ? $"Unexpected token after export: {Current().Type}" : $"Unexpected token {Current().Type}";
			throw new CompileError(ErrorCode.ParserUnexpectedToken, msg, Current().Line, Current().Column);
		}
	}

	// ============================================================================
	// Top-level declarations
	// ============================================================================

	private FunctionDecl ParseFunction(bool isExport) {
		var startToken = Expect(TokenType.Function);
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;
		Logger.Debug(LogCategory.Parser, $"Parsing function: {name}");
		Expect(TokenType.LeftParen);
		var parameters = ParseParamList();
		var returnType = ParseOptionalReturnType();
		var throwsType = ParseOptionalThrowsClause();
		SkipNewlines();
		var body = ParseBodyUntilEnd();
		var endLine = ExpectEndLabel(name);

		return new FunctionDecl(
			name, isExport, parameters, returnType, throwsType, body,
			SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, name)
		);
	}

	private TypeDecl ParseTypeDecl(bool isExport) {
		var startToken = Expect(TokenType.Type);
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;
		Logger.Debug(LogCategory.Parser, $"Parsing type: {name}");

		var genericParams = new List<string>();
		if (Check(TokenType.Uses)) {
			Advance();
			genericParams.Add(Expect(TokenType.Identifier).Value);
			while (Check(TokenType.Comma)) {
				Advance();
				genericParams.Add(Expect(TokenType.Identifier).Value);
			}
		}

		var conformances = new List<InterfaceConformance>();
		if (Check(TokenType.Is)) {
			Advance();
			conformances.Add(ParseInterfaceConformance());
			while (Check(TokenType.Comma)) {
				Advance();
				conformances.Add(ParseInterfaceConformance());
			}
		}

		SkipNewlines();

		var associatedTypes = new List<TypeAliasDecl>();
		var fields = new List<FieldDecl>();
		var methods = new List<MethodDecl>();

		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;

			if (Check(TokenType.TypeAlias)) {
				associatedTypes.Add(ParseTypeAliasDecl(false));
			} else if (IsFieldStart()) {
				fields.Add(ParseFieldDecl());
			} else if (IsMethodStart()) {
				methods.Add(ParseMethodDecl());
			} else {
				throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected field or method in type, got {Current().Type}", Current().Line, Current().Column);
			}
			SkipNewlines();
		}

		var endLine = ExpectEndLabel(name);

		return new TypeDecl(
			name, isExport, genericParams, conformances, associatedTypes, fields, methods,
			SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, name)
		) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private EnumDecl ParseEnumDecl(bool isExport) {
		var startToken = Expect(TokenType.Enum);
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;
		Logger.Debug(LogCategory.Parser, $"Parsing enum: {name}");

		var conformances = new List<InterfaceConformance>();
		if (Check(TokenType.Is)) {
			Advance();
			conformances.Add(ParseInterfaceConformance());
			while (Check(TokenType.Comma)) {
				Advance();
				conformances.Add(ParseInterfaceConformance());
			}
		}

		SkipNewlines(); ;

		var members = new List<EnumMember>();
		var methods = new List<MethodDecl>();

		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;

			if (Check(TokenType.Function) || Check(TokenType.Static)) {
				methods.Add(ParseMethodDecl());
			} else if (Check(TokenType.Identifier)) {
				members.Add(ParseEnumMember());
			} else {
				throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected enum member or method, got {Current().Type}", Current().Line, Current().Column);
			}
			SkipNewlines();
		}

		var endLine = ExpectEndLabel(name);

		return new EnumDecl(
			name, isExport, conformances, members, methods,
			SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, name)
		);
	}

	private EnumMember ParseEnumMember() {
		var nameToken = Expect(TokenType.Identifier);
		Expr? value = null;
		var associatedValues = new List<ParamDecl>();

		if (Check(TokenType.Equals)) {
			Advance();
			value = ParseExpression();
		} else if (Check(TokenType.LeftParen)) {
			Advance();
			if (!Check(TokenType.RightParen)) {
				associatedValues.Add(ParseParamDecl());
				while (Check(TokenType.Comma)) {
					Advance();
					associatedValues.Add(ParseParamDecl());
				}
			}
			Expect(TokenType.RightParen);
		}

		return new EnumMember(nameToken.Value, value, associatedValues) {
			Location = SourceLocation.Point(_sourceFileIndex, nameToken.Line, nameToken.Column)
		};
	}

	private InterfaceDecl ParseInterfaceDecl(bool isExport) {
		var startToken = Expect(TokenType.Interface);
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;

		var genericParams = new List<string>();
		if (Check(TokenType.Uses)) {
			Advance();
			genericParams.Add(Expect(TokenType.Identifier).Value);
			while (Check(TokenType.Comma)) {
				Advance();
				genericParams.Add(Expect(TokenType.Identifier).Value);
			}
		}

		var extends = new List<string>();
		if (Check(TokenType.Extends)) {
			Advance();
			extends.Add(Expect(TokenType.Identifier).Value);
			while (Check(TokenType.Comma)) {
				Advance();
				extends.Add(Expect(TokenType.Identifier).Value);
			}
		}

		SkipNewlines();

		var associatedTypes = new List<TypeAliasDecl>();
		var methods = new List<InterfaceMethod>();

		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;

			if (Check(TokenType.TypeAlias)) {
				associatedTypes.Add(ParseTypeAliasDecl(false));
			} else if (Check(TokenType.Static) || Check(TokenType.Function)) {
				methods.Add(ParseInterfaceMethod());
			} else {
				throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected method in interface, got {Current().Type}", Current().Line, Current().Column);
			}
			SkipNewlines();
		}

		var endLine = ExpectEndLabel(name);

		return new InterfaceDecl(
			name, isExport, genericParams, extends, associatedTypes, methods,
			SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, name)
		);
	}

	private InterfaceMethod ParseInterfaceMethod() {
		var isStatic = false;
		if (Check(TokenType.Static)) {
			Advance();
			isStatic = true;
		}

		Expect(TokenType.Function);
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;
		Expect(TokenType.LeftParen);
		var parameters = ParseParamList();
		var returnType = ParseOptionalReturnType();
		var throwsType = ParseOptionalThrowsClause();

		return new InterfaceMethod(name, isStatic, parameters, returnType, throwsType);
	}

	private ExtensionDecl ParseExtensionDecl(bool isExport) {
		var startToken = Expect(TokenType.Extension);
		var interfaceName = Expect(TokenType.Identifier).Value;

		SkipNewlines();

		var associatedTypes = new List<TypeAliasDecl>();
		var methods = new List<ExtensionMethod>();

		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;

			if (Check(TokenType.TypeAlias)) {
				associatedTypes.Add(ParseTypeAliasDecl(false));
			} else if (Check(TokenType.Function)) {
				methods.Add(ParseExtensionMethod());
			} else {
				throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected method in extension, got {Current().Type}", Current().Line, Current().Column);
			}
			SkipNewlines();
		}

		var endLine = ExpectEndLabel(interfaceName);

		return new ExtensionDecl(
			interfaceName, isExport, associatedTypes, methods,
			SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, interfaceName)
		);
	}

	private ExtensionMethod ParseExtensionMethod() {
		var startToken = Expect(TokenType.Function);
		var name = Expect(TokenType.Identifier).Value;
		Expect(TokenType.LeftParen);
		var parameters = ParseParamList();
		var returnType = ParseOptionalReturnType();
		var throwsType = ParseOptionalThrowsClause();
		SkipNewlines();
		var body = ParseBodyUntilEnd();
		var endLine = ExpectEndLabel(name);

		return new ExtensionMethod(
			name, parameters, returnType, throwsType, body,
			SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, name)
		);
	}

	private TypeAliasDecl ParseTypeAliasDecl(bool isExport) {
		var startToken = Expect(TokenType.TypeAlias);
		var name = Expect(TokenType.Identifier).Value;
		Expect(TokenType.Is);
		var baseType = ExpectTypeName();

		var typeArgs = new List<string>();
		if (Check(TokenType.With)) {
			Advance();
			if (Check(TokenType.LeftParen)) {
				Advance();
				typeArgs.Add(ExpectTypeName());
				while (Check(TokenType.Comma)) {
					Advance();
					typeArgs.Add(ExpectTypeName());
				}
				Expect(TokenType.RightParen);
			} else {
				typeArgs.Add(ExpectTypeName());
			}
		}

		return new TypeAliasDecl(name, baseType, typeArgs, isExport) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private GlobalConstant ParseGlobalConstant(bool isExport) {
		var startToken = Expect(TokenType.Let);
		var name = Expect(TokenType.Identifier).Value;
		Expect(TokenType.Equals);
		var value = ParseExpression();

		return new GlobalConstant(name, isExport, value) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private GlobalVariable ParseGlobalVariable(bool isExport) {
		var startToken = Expect(TokenType.Var);
		var name = Expect(TokenType.Identifier).Value;
		Expect(TokenType.Equals);
		var value = ParseExpression();

		return new GlobalVariable(name, isExport, value) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private FieldDecl ParseFieldDecl() {
		var isExport = false;
		if (Check(TokenType.Export)) {
			Advance();
			isExport = true;
		}

		var isStatic = false;
		if (Check(TokenType.Static)) {
			Advance();
			isStatic = true;
		}

		var isMutable = Check(TokenType.Var);
		if (Check(TokenType.Var)) Advance();
		else Expect(TokenType.Let);

		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;

		// Type is optional when followed by `=` (type will be inferred from default value)
		TypeRef? type = null;
		if (!Check(TokenType.Equals)) {
			type = ParseTypeRef();
		}

		Expr? defaultValue = null;
		if (Check(TokenType.Equals)) {
			Advance();
			defaultValue = ParseExpression();
		}

		// If no type and no default value, that's an error
		if (type == null && defaultValue == null) {
			throw new CompileError(ErrorCode.ParserExpectedType, "Field must have either a type annotation or a default value", Current().Line, Current().Column);
		}

		return new FieldDecl(name, type, isMutable, isExport, isStatic, defaultValue) {
			Location = SourceLocation.Point(_sourceFileIndex, nameToken.Line, nameToken.Column)
		};
	}

	private MethodDecl ParseMethodDecl() {
		var isExport = false;
		if (Check(TokenType.Export)) {
			Advance();
			isExport = true;
		}

		var isStatic = false;
		if (Check(TokenType.Static)) {
			Advance();
			isStatic = true;
		}

		var startToken = Expect(TokenType.Function);

		// Parse method name: could be simple "init" or qualified "InterfaceName.methodName"
		var name = Expect(TokenType.Identifier).Value;
		var endLabelName = name; // For end label, use the final method name part
		if (Check(TokenType.Dot)) {
			// Qualified name: Interface.method
			Advance();
			var methodName = Expect(TokenType.Identifier).Value;
			name = $"{name}.{methodName}";
			endLabelName = methodName; // End label uses just the method name
		}

		Expect(TokenType.LeftParen);
		var parameters = ParseParamList();
		var returnType = ParseOptionalReturnType();
		var throwsType = ParseOptionalThrowsClause();
		SkipNewlines();
		var body = ParseBodyUntilEnd();
		var endLine = ExpectEndLabel(endLabelName);

		return new MethodDecl(
			name, isStatic, isExport, parameters, returnType, throwsType, body,
			SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, name)
		);
	}

	private InterfaceConformance ParseInterfaceConformance() {
		var interfaceName = Expect(TokenType.Identifier).Value;
		var typeArgs = new List<string>();

		if (Check(TokenType.With)) {
			Advance();
			if (Check(TokenType.LeftParen)) {
				Advance();
				typeArgs.Add(ExpectTypeName());
				while (Check(TokenType.Comma)) {
					Advance();
					typeArgs.Add(ExpectTypeName());
				}
				Expect(TokenType.RightParen);
			} else {
				typeArgs.Add(ExpectTypeName());
			}
		}

		return new InterfaceConformance(interfaceName, typeArgs);
	}

	// ============================================================================
	// Parameter and type parsing
	// ============================================================================

	private List<ParamDecl> ParseParamList() {
		var parameters = new List<ParamDecl>();

		if (!Check(TokenType.RightParen)) {
			parameters.Add(ParseParamDecl());
			while (Check(TokenType.Comma)) {
				Advance();
				parameters.Add(ParseParamDecl());
			}
		}
		Expect(TokenType.RightParen);

		return parameters;
	}

	private ParamDecl ParseParamDecl() {
		var nameToken = Expect(TokenType.Identifier);
		var type = ParseTypeRef();

		Expr? defaultValue = null;
		if (Check(TokenType.Equals)) {
			Advance();
			defaultValue = ParseExpression();
		}

		return new ParamDecl(nameToken.Value, type, defaultValue) {
			Location = SourceLocation.Point(_sourceFileIndex, nameToken.Line, nameToken.Column)
		};
	}

	private TypeRef? ParseOptionalReturnType() {
		if (Check(TokenType.Returns)) {
			Advance();
			return ParseTypeRef();
		}
		return null;
	}

	private string? ParseOptionalThrowsClause() {
		if (Check(TokenType.Throws)) {
			Advance();
			return Expect(TokenType.Identifier).Value;
		}
		return null;
	}

	private TypeRef ParseTypeRef() {
		var startToken = Current();

		// Check for function type: (params) returns ReturnType
		if (Check(TokenType.LeftParen)) {
			return ParseFunctionTypeRef(startToken);
		}

		var typeName = ExpectTypeName();

		// Check for generic type: TypeName with TypeArg or TypeName with TypeArg1, TypeArg2
		if (Check(TokenType.With)) {
			Advance();
			var typeArgs = new List<string> {
				ExpectTypeName()
			};
			while (Check(TokenType.Comma)) {
				Advance();
				typeArgs.Add(ExpectTypeName());
			}
			return new GenericTypeRef(typeName, typeArgs) {
				Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
			};
		}

		return new SimpleTypeRef(typeName) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private FunctionTypeRef ParseFunctionTypeRef(Token startToken) {
		// Parse (ParamType1, ParamType2, ...) returns ReturnType
		Expect(TokenType.LeftParen);
		var paramTypes = new List<TypeRef>();

		if (!Check(TokenType.RightParen)) {
			paramTypes.Add(ParseTypeRef());
			while (Check(TokenType.Comma)) {
				Advance();
				paramTypes.Add(ParseTypeRef());
			}
		}
		Expect(TokenType.RightParen);

		TypeRef? returnType = null;
		if (Check(TokenType.Returns)) {
			Advance();
			returnType = ParseTypeRef();
		}

		return new FunctionTypeRef(paramTypes, returnType) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private string ExpectTypeName() {
		if (Check(TokenType.Int)) return Advance().Value;
		if (Check(TokenType.Float)) return Advance().Value;
		if (Check(TokenType.Bool)) return Advance().Value;
		if (Check(TokenType.Byte)) return Advance().Value;
		if (Check(TokenType.SelfType)) return Advance().Value;
		if (Check(TokenType.Identifier)) return Advance().Value;

		throw new CompileError(ErrorCode.ParserExpectedType, "Expected type name", Current().Line, Current().Column);
	}

	// ============================================================================
	// Statement parsing
	// ============================================================================

	private List<Stmt> ParseBodyUntilEnd() {
		var statements = new List<Stmt>();

		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;
			statements.Add(ParseStatement());
			SkipNewlines();
		}

		return statements;
	}

	private Stmt ParseStatement() {
		var startToken = Current();

		if (Check(TokenType.Return)) {
			Advance();
			Expr? value = null;
			if (!Check(TokenType.Newline) && !Check(TokenType.End) && !Check(TokenType.Eof)) {
				value = ParseExpression();
			}
			return new ReturnStmt(value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		if (Check(TokenType.Let)) {
			Advance();
			var name = Expect(TokenType.Identifier).Value;
			TypeRef? typeAnnotation = null;
			if (!Check(TokenType.Equals)) {
				typeAnnotation = ParseTypeRef();
			}
			Expect(TokenType.Equals);
			var value = ParseExpression();
			return new LetDeclStmt(name, typeAnnotation, value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		if (Check(TokenType.Var)) {
			Advance();
			var name = Expect(TokenType.Identifier).Value;
			TypeRef? typeAnnotation = null;
			if (!Check(TokenType.Equals)) {
				typeAnnotation = ParseTypeRef();
			}
			Expect(TokenType.Equals);
			var value = ParseExpression();
			return new VarDeclStmt(name, typeAnnotation, value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		if (Check(TokenType.If)) {
			return ParseIfStatement();
		}

		if (Check(TokenType.While)) {
			return ParseWhileStatement();
		}

		if (Check(TokenType.For)) {
			return ParseForStatement();
		}

		if (Check(TokenType.Match)) {
			return ParseMatchStatement();
		}

		if (Check(TokenType.Break)) {
			Advance();
			string? label = null;
			if (Check(TokenType.CharacterLiteral)) {
				label = Advance().Value;
			}
			return new BreakStmt(label) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		if (Check(TokenType.Continue)) {
			Advance();
			string? label = null;
			if (Check(TokenType.CharacterLiteral)) {
				label = Advance().Value;
			}
			return new ContinueStmt(label) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		if (Check(TokenType.Throw)) {
			Advance();
			var errorExpr = ParseExpression();
			return new ThrowStmt(errorExpr) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		// Assignment or expression statement
		var expr = ParseExpression();

		// Check for assignment
		if (Check(TokenType.Equals)) {
			Advance();
			var value = ParseExpression();

			if (expr is IdentifierExpr idExpr) {
				return new AssignStmt(idExpr.Name, value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
			}
			if (expr is FieldAccessExpr fieldExpr) {
				return new FieldAssignStmt(fieldExpr.Base, fieldExpr.FieldName, value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
			}
			if (expr is IndexExpr indexExpr) {
				return new IndexAssignStmt(indexExpr.Base, indexExpr.Index, value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
			}

			throw new CompileError(ErrorCode.ParserInvalidAssignment, "Invalid assignment target", startToken.Line, startToken.Column);
		}

		return new ExprStmt(expr) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
	}

	private IfStmt ParseIfStatement() {
		var startToken = Expect(TokenType.If);
		var condition = ParseExpression();
		var label = ExpectBlockLabel();
		SkipNewlines();

		var thenBody = new List<Stmt>();
		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;
			thenBody.Add(ParseStatement());
			SkipNewlines();
		}

		var thenEndLine = ExpectEndLabel(label);
		var thenBlock = SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, thenEndLine, label);

		List<Stmt>? elseBody = null;
		SourceLocation? elseBlock = null;

		if (Check(TokenType.Else)) {
			var elseStartToken = Advance();

			// Check for else-if chain: "else if <condition> 'label'"
			if (Check(TokenType.If)) {
				// Parse the nested if as the single statement in the else body
				var nestedIf = ParseIfStatement();
				elseBody = [nestedIf];
				// Use the nested if's end line for the else block info
				var endLine = nestedIf.ElseBlock?.EndLine ?? nestedIf.ThenBlock.EndLine;
				elseBlock = SourceLocation.Block(_sourceFileIndex, elseStartToken.Line, elseStartToken.Column, endLine, null);
			} else {
				var elseLabel = ExpectBlockLabel();
				SkipNewlines();

				elseBody = [];
				while (!Check(TokenType.End) && !IsAtEnd()) {
					SkipNewlines();
					if (Check(TokenType.End)) break;
					elseBody.Add(ParseStatement());
					SkipNewlines();
				}

				var elseEndLine = ExpectEndLabel(elseLabel);
				elseBlock = SourceLocation.Block(_sourceFileIndex, elseStartToken.Line, elseStartToken.Column, elseEndLine, elseLabel);
			}
		}

		return new IfStmt(condition, thenBody, thenBlock, elseBody, elseBlock) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private WhileStmt ParseWhileStatement() {
		var startToken = Expect(TokenType.While);
		var condition = ParseExpression();
		var label = ExpectBlockLabel();
		SkipNewlines();

		var body = new List<Stmt>();
		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;
			body.Add(ParseStatement());
			SkipNewlines();
		}

		var endLine = ExpectEndLabel(label);

		return new WhileStmt(condition, body, SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, label)) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private ForStmt ParseForStatement() {
		var startToken = Expect(TokenType.For);
		var varName = Expect(TokenType.Identifier).Value;
		Expect(TokenType.In);
		var iterable = ParseExpression();
		var label = ExpectBlockLabel();
		SkipNewlines();

		var body = new List<Stmt>();
		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;
			body.Add(ParseStatement());
			SkipNewlines();
		}

		var endLine = ExpectEndLabel(label);

		return new ForStmt(varName, iterable, body, SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, label)) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private MatchStmt ParseMatchStatement() {
		var startToken = Expect(TokenType.Match);
		var scrutinee = ParseExpression();
		var label = ExpectBlockLabel();
		SkipNewlines();

		var cases = new List<MatchCase>();
		List<Stmt>? defaultBody = null;

		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;

			if (Check(TokenType.Default)) {
				Advance();
				Expect(TokenType.Then);
				defaultBody = [ParseStatement()];
			} else {
				cases.Add(ParseMatchCase());
			}
			SkipNewlines();
		}

		var endLine = ExpectEndLabel(label);

		return new MatchStmt(scrutinee, cases, defaultBody, SourceLocation.Block(_sourceFileIndex, startToken.Line, startToken.Column, endLine, label)) {
			Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column)
		};
	}

	private MatchCase ParseMatchCase() {
		var patterns = new List<Expr>();
		var patternBindings = new List<PatternBinding?>();

		// Parse first pattern
		var (pattern, binding) = ParseMatchPattern();
		patterns.Add(pattern);
		patternBindings.Add(binding);

		// Parse additional patterns with 'or'
		while (Check(TokenType.Or)) {
			Advance();
			var (p, b) = ParseMatchPattern();
			patterns.Add(p);
			patternBindings.Add(b);
		}

		var hasFallthrough = false;
		if (Check(TokenType.And) && Peek(1)?.Type == TokenType.Fallthrough) {
			Advance(); // and
			Advance(); // fallthrough
			hasFallthrough = true;
		}

		Expect(TokenType.Then);
		var body = new List<Stmt> { ParseStatement() };

		return new MatchCase(patterns, patternBindings, body, hasFallthrough);
	}

	private (Expr pattern, PatternBinding? binding) ParseMatchPattern() {
		// Check for enum case pattern: CaseName or CaseName(binding)
		if (Check(TokenType.Identifier)) {
			var nameToken = Advance();
			var caseName = nameToken.Value;

			if (Check(TokenType.LeftParen)) {
				Advance();
				var bindings = new List<string>();
				if (!Check(TokenType.RightParen)) {
					bindings.Add(Expect(TokenType.Identifier).Value);
					while (Check(TokenType.Comma)) {
						Advance();
						bindings.Add(Expect(TokenType.Identifier).Value);
					}
				}
				Expect(TokenType.RightParen);

				return (
					new IdentifierExpr(caseName) { Location = SourceLocation.Point(_sourceFileIndex, nameToken.Line, nameToken.Column) },
					new PatternBinding(caseName, bindings)
				);
			}

			return (
				new IdentifierExpr(caseName) { Location = SourceLocation.Point(_sourceFileIndex, nameToken.Line, nameToken.Column) },
				null
			);
		}

		// Other patterns (literals, etc.)
		return (ParseExpression(), null);
	}

	// ============================================================================
	// Expression parsing (precedence climbing)
	// ============================================================================

	private Expr ParseExpression() {
		return ParseLogicalOr();
	}

	private Expr ParseLogicalOr() {
		var left = ParseLogicalAnd();

		while (Check(TokenType.Or)) {
			var opToken = Current();
			Advance();
			var right = ParseLogicalAnd();
			left = new LogicalExpr(left, LogicalOp.Or, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
		}

		return left;
	}

	private Expr ParseLogicalAnd() {
		var left = ParseRange();

		while (Check(TokenType.And)) {
			// Check if 'and' is followed by 'fallthrough' - if so, stop parsing
			if (Peek(1)?.Type == TokenType.Fallthrough) break;

			var opToken = Current();
			Advance();
			var right = ParseBitwiseOr();
			left = new LogicalExpr(left, LogicalOp.And, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
		}

		return left;
	}

	private Expr ParseRange() {
		var left = ParseBitwiseOr();

		// Check for range operators: .., ..=, ..<
		if (Check(TokenType.DotDot)) {
			var opToken = Current();
			Advance();
			var right = ParseBitwiseOr();
			return new RangeExpr(left, right, false) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) }; // exclusive by default (..)
		}
		if (Check(TokenType.DotDotEquals)) {
			var opToken = Current();
			Advance();
			var right = ParseBitwiseOr();
			return new RangeExpr(left, right, true) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) }; // inclusive (..=)
		}
		if (Check(TokenType.DotDotLess)) {
			var opToken = Current();
			Advance();
			var right = ParseBitwiseOr();
			return new RangeExpr(left, right, false) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) }; // exclusive (..<)
		}

		return left;
	}

	private Expr ParseBitwiseOr() {
		var left = ParseBitwiseXor();

		while (Check(TokenType.Pipe)) {
			var opToken = Current();
			Advance();
			var right = ParseBitwiseXor();
			left = new BinaryExpr(left, BinaryOp.Bor, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
		}

		return left;
	}

	private Expr ParseBitwiseXor() {
		var left = ParseBitwiseAnd();

		while (Check(TokenType.Caret)) {
			var opToken = Current();
			Advance();
			var right = ParseBitwiseAnd();
			left = new BinaryExpr(left, BinaryOp.Bxor, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
		}

		return left;
	}

	private Expr ParseBitwiseAnd() {
		var left = ParseComparison();

		while (Check(TokenType.Ampersand)) {
			var opToken = Current();
			Advance();
			var right = ParseComparison();
			left = new BinaryExpr(left, BinaryOp.Band, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
		}

		return left;
	}

	private Expr ParseComparison() {
		var left = ParseShift();

		if (MatchComparison() is CompareOp op) {
			var opToken = Current();
			Advance();
			var right = ParseShift();
			left = new CompareExpr(left, op, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
		}

		return left;
	}

	private CompareOp? MatchComparison() {
		if (Check(TokenType.EqualsEquals)) return CompareOp.Eq;
		if (Check(TokenType.NotEquals)) return CompareOp.Ne;
		if (Check(TokenType.LessThan)) return CompareOp.Lt;
		if (Check(TokenType.LessEquals)) return CompareOp.Le;
		if (Check(TokenType.GreaterThan)) return CompareOp.Gt;
		if (Check(TokenType.GreaterEquals)) return CompareOp.Ge;
		return null;
	}

	private Expr ParseShift() {
		var left = ParseAdditive();

		while (true) {
			if (Check(TokenType.LeftShift)) {
				var opToken = Current();
				Advance();
				var right = ParseAdditive();
				left = new BinaryExpr(left, BinaryOp.Shl, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
			} else if (Check(TokenType.RightShift)) {
				var opToken = Current();
				Advance();
				var right = ParseAdditive();
				left = new BinaryExpr(left, BinaryOp.Shr, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
			} else {
				break;
			}
		}

		return left;
	}

	private Expr ParseAdditive() {
		var left = ParseMultiplicative();

		while (true) {
			if (Check(TokenType.Plus)) {
				var opToken = Current();
				Advance();
				var right = ParseMultiplicative();
				left = new BinaryExpr(left, BinaryOp.Add, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
			} else if (Check(TokenType.Minus)) {
				var opToken = Current();
				Advance();
				var right = ParseMultiplicative();
				left = new BinaryExpr(left, BinaryOp.Sub, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
			} else {
				break;
			}
		}

		return left;
	}

	private Expr ParseMultiplicative() {
		var left = ParseUnary();

		while (true) {
			if (Check(TokenType.Star)) {
				var opToken = Current();
				Advance();
				var right = ParseUnary();
				left = new BinaryExpr(left, BinaryOp.Mul, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
			} else if (Check(TokenType.Slash)) {
				var opToken = Current();
				Advance();
				var right = ParseUnary();
				left = new BinaryExpr(left, BinaryOp.Div, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
			} else if (Check(TokenType.Mod)) {
				var opToken = Current();
				Advance();
				var right = ParseUnary();
				left = new BinaryExpr(left, BinaryOp.Mod, right) { Location = SourceLocation.Point(_sourceFileIndex, opToken.Line, opToken.Column) };
			} else {
				break;
			}
		}

		return left;
	}

	private Expr ParseUnary() {
		if (Check(TokenType.Minus)) {
			var startToken = Advance();

			// Fold negative numeric literals directly
			if (Check(TokenType.IntegerLiteral)) {
				var value = ParseIntegerLiteral(Advance().Value);
				return new IntLiteralExpr(-value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
			}
			if (Check(TokenType.FloatLiteral)) {
				var value = double.Parse(Advance().Value.Replace("_", ""));
				return new FloatLiteralExpr(-value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
			}

			var operand = ParseUnary();
			return new UnaryExpr(UnaryOp.Negate, operand) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		if (Check(TokenType.Not)) {
			var startToken = Advance();
			var operand = ParseUnary();
			return new UnaryExpr(UnaryOp.Not, operand) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		if (Check(TokenType.Try)) {
			return ParseTryExpression();
		}

		return ParsePostfix(ParsePrimary());
	}

	private TryExpr ParseTryExpression() {
		var startToken = Expect(TokenType.Try);
		var operand = ParseUnary();

		OtherwiseClause? otherwise = null;
		if (Check(TokenType.Otherwise)) {
			Advance();

			if (Check(TokenType.Ignore)) {
				Advance();
				otherwise = new OtherwiseClause(OtherwiseMode.Ignore, null, null, null);
			} else if (Check(TokenType.LeftParen)) {
				// Block with error binding
				Advance();
				var errName = Expect(TokenType.Identifier).Value;
				Expect(TokenType.RightParen);
				var label = ExpectBlockLabel();
				SkipNewlines();
				var body = ParseBodyUntilEnd();
				ExpectEndLabel(label);
				otherwise = new OtherwiseClause(OtherwiseMode.BlockWithErr, null, errName, body);
			} else if (Check(TokenType.CharacterLiteral)) {
				// Block without error binding
				var label = ExpectBlockLabel();
				SkipNewlines();
				var body = ParseBodyUntilEnd();
				ExpectEndLabel(label);
				otherwise = new OtherwiseClause(OtherwiseMode.Block, null, null, body);
			} else {
				// Default expression
				var defaultExpr = ParseExpression();
				otherwise = new OtherwiseClause(OtherwiseMode.DefaultExpr, defaultExpr, null, null);
			}
		}

		return new TryExpr(operand, otherwise) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
	}

	private Expr ParsePrimary() {
		var startToken = Current();

		// Match expression
		if (Check(TokenType.Match)) {
			return ParseMatchExpression();
		}

		// Integer literal
		if (Check(TokenType.IntegerLiteral)) {
			var value = ParseIntegerLiteral(Advance().Value);
			return new IntLiteralExpr(value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		// Float literal
		if (Check(TokenType.FloatLiteral)) {
			var value = double.Parse(Advance().Value.Replace("_", ""));
			return new FloatLiteralExpr(value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		// Boolean literals
		if (Check(TokenType.True)) {
			Advance();
			return new BoolLiteralExpr(true) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}
		if (Check(TokenType.False)) {
			Advance();
			return new BoolLiteralExpr(false) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		// String literal
		if (Check(TokenType.StringLiteral)) {
			var value = Advance().Value;
			return new StringLiteralExpr(value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		// Interpolated string
		if (Check(TokenType.StringInterp)) {
			return ParseInterpolatedString(Advance().Value, startToken);
		}

		// Character literal
		if (Check(TokenType.CharacterLiteral)) {
			var value = Advance().Value;
			return new CharLiteralExpr(value) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		// Self
		if (Check(TokenType.Self)) {
			Advance();
			return new SelfExpr() { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
		}

		// Array literal
		if (Check(TokenType.LeftBracket)) {
			return ParseArrayLiteral();
		}

		// Struct literal with inferred type: { field: value }
		if (Check(TokenType.LeftBrace)) {
			return ParseAnonymousStructInit();
		}

		// Parenthesized expression or closure
		if (Check(TokenType.LeftParen)) {
			Advance();
			var expr = ParseExpression();
			Expect(TokenType.RightParen);
			return expr;
		}

		// Self type (for Self{...} in methods)
		if (Check(TokenType.SelfType)) {
			var token = Advance();
			if (Check(TokenType.LeftBrace)) {
				return ParseStructInit(token.Value);
			}
			return new IdentifierExpr(token.Value) { Location = SourceLocation.Point(_sourceFileIndex, token.Line, token.Column) };
		}

		// Identifier, function call, or type init
		if (Check(TokenType.Identifier)) {
			var token = Advance();
			var name = token.Value;

			// Check for struct init: TypeName{...}
			if (Check(TokenType.LeftBrace)) {
				return ParseStructInit(name);
			}

			// Check for static call or field access: TypeName.member
			// Note: At parse time we can't distinguish Type.method() from var.method()
			// so we create StaticCallExpr and resolve it in MLIR lowering
			if (Check(TokenType.Dot)) {
				Advance();
				var memberToken = Expect(TokenType.Identifier);
				var memberName = memberToken.Value;

				// Check for call with args: name.member(args)
				if (Check(TokenType.LeftParen)) {
					Advance();
					var args = new List<Expr>();
					var namedArgs = new List<NamedArg>();
					ParseCallArgs(args, namedArgs);
					// Could be static method, enum case, or instance method - resolved in MLIR lowering
					// Use memberToken location so errors point to the method name, not the type name
					return new StaticCallExpr(name, memberName, args, namedArgs) { Location = SourceLocation.Point(_sourceFileIndex, memberToken.Line, memberToken.Column) };
				}

				// Field access continues in parsePostfix
				return ParsePostfix(new FieldAccessExpr(
					new IdentifierExpr(name) { Location = SourceLocation.Point(_sourceFileIndex, token.Line, token.Column) },
					memberName
				) { Location = SourceLocation.Point(_sourceFileIndex, token.Line, token.Column) });
			}

			// Check for function call: name(...)
			if (Check(TokenType.LeftParen)) {
				Advance();
				var args = new List<Expr>();
				var namedArgs = new List<NamedArg>();
				ParseCallArgs(args, namedArgs);
				var builtin = BuiltinFunctions.GetValueOrDefault(name, BuiltinOp.None);
				return new CallExpr(name, args, namedArgs, builtin) { Location = SourceLocation.Point(_sourceFileIndex, token.Line, token.Column) };
			}

			return new IdentifierExpr(name) { Location = SourceLocation.Point(_sourceFileIndex, token.Line, token.Column) };
		}

		throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected expression, got {startToken.Type}", startToken.Line, startToken.Column);
	}

	private Expr ParsePostfix(Expr expr) {
		while (true) {
			if (Check(TokenType.Dot)) {
				var dotToken = Current();
				Advance();
				var memberName = Expect(TokenType.Identifier).Value;

				// Check for method call
				if (Check(TokenType.LeftParen)) {
					Advance();
					var args = new List<Expr>();
					var namedArgs = new List<NamedArg>();
					ParseCallArgs(args, namedArgs);
					expr = new MethodCallExpr(expr, memberName, args, namedArgs) { Location = SourceLocation.Point(_sourceFileIndex, dotToken.Line, dotToken.Column) };
				} else {
					expr = new FieldAccessExpr(expr, memberName) { Location = SourceLocation.Point(_sourceFileIndex, dotToken.Line, dotToken.Column) };
				}
			} else if (Check(TokenType.LeftBracket)) {
				var bracketToken = Current();
				Advance();
				var index = ParseExpression();
				Expect(TokenType.RightBracket);
				expr = new IndexExpr(expr, index) { Location = SourceLocation.Point(_sourceFileIndex, bracketToken.Line, bracketToken.Column) };
			} else if (Check(TokenType.As)) {
				var asToken = Current();
				Advance();
				var targetType = ExpectTypeName();
				expr = new CastExpr(expr, targetType) { Location = SourceLocation.Point(_sourceFileIndex, asToken.Line, asToken.Column) };
			} else {
				break;
			}
		}

		return expr;
	}

	private void ParseCallArgs(List<Expr> args, List<NamedArg> namedArgs) {
		if (!Check(TokenType.RightParen)) {
			ParseOneArg(args, namedArgs);
			while (Check(TokenType.Comma)) {
				Advance();
				ParseOneArg(args, namedArgs);
			}
		}
		Expect(TokenType.RightParen);
	}

	private void ParseOneArg(List<Expr> args, List<NamedArg> namedArgs) {
		// Check for named argument: name: value
		if (Check(TokenType.Identifier) && Peek(1)?.Type == TokenType.Colon) {
			var name = Advance().Value;
			Advance(); // consume ':'
			var value = ParseExpression();
			namedArgs.Add(new NamedArg(name, value));
		} else {
			if (namedArgs.Count > 0) {
				throw new CompileError(ErrorCode.ParserUnexpectedToken, "Positional argument after named argument", Current().Line, Current().Column);
			}
			args.Add(ParseExpression());
		}
	}

	private ArrayLiteralExpr ParseArrayLiteral() {
		var startToken = Expect(TokenType.LeftBracket);
		var elements = new List<Expr>();

		if (!Check(TokenType.RightBracket)) {
			elements.Add(ParseExpression());
			while (Check(TokenType.Comma)) {
				Advance();
				if (Check(TokenType.RightBracket)) break; // trailing comma
				elements.Add(ParseExpression());
			}
		}
		Expect(TokenType.RightBracket);

		return new ArrayLiteralExpr(elements) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
	}

	private StructInitExpr ParseStructInit(string typeName) {
		var startToken = Expect(TokenType.LeftBrace);
		var fields = new List<FieldInit>();

		if (!Check(TokenType.RightBrace)) {
			fields.Add(ParseFieldInit());
			while (Check(TokenType.Comma)) {
				Advance();
				if (Check(TokenType.RightBrace)) break; // trailing comma
				fields.Add(ParseFieldInit());
			}
		}
		Expect(TokenType.RightBrace);

		return new StructInitExpr(typeName, [], fields) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
	}

	private StructInitExpr ParseAnonymousStructInit() {
		var startToken = Expect(TokenType.LeftBrace);
		var fields = new List<FieldInit>();

		if (!Check(TokenType.RightBrace)) {
			fields.Add(ParseFieldInit());
			while (Check(TokenType.Comma)) {
				Advance();
				if (Check(TokenType.RightBrace)) break;
				fields.Add(ParseFieldInit());
			}
		}
		Expect(TokenType.RightBrace);

		return new StructInitExpr(null, [], fields) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
	}

	private FieldInit ParseFieldInit() {
		var name = Expect(TokenType.Identifier).Value;
		Expect(TokenType.Colon);
		var value = ParseExpression();
		return new FieldInit(name, value);
	}

	private MatchExpr ParseMatchExpression() {
		var startToken = Expect(TokenType.Match);
		var scrutinee = ParseExpression();
		var label = ExpectBlockLabel();
		SkipNewlines();

		var cases = new List<MatchExprCase>();
		Expr? defaultExpr = null;

		while (!Check(TokenType.End) && !IsAtEnd()) {
			SkipNewlines();
			if (Check(TokenType.End)) break;

			if (Check(TokenType.Default)) {
				Advance();
				Expect(TokenType.Then);
				defaultExpr = ParseExpression();
			} else {
				cases.Add(ParseMatchExprCase());
			}
			SkipNewlines();
		}

		ExpectEndLabel(label);

		return new MatchExpr(scrutinee, cases, defaultExpr, label) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
	}

	private MatchExprCase ParseMatchExprCase() {
		var patterns = new List<Expr>();
		var patternBindings = new List<PatternBinding?>();

		var (pattern, binding) = ParseMatchPattern();
		patterns.Add(pattern);
		patternBindings.Add(binding);

		while (Check(TokenType.Or)) {
			Advance();
			var (p, b) = ParseMatchPattern();
			patterns.Add(p);
			patternBindings.Add(b);
		}

		Expect(TokenType.Then);
		var result = ParseExpression();

		return new MatchExprCase(patterns, patternBindings, result);
	}

	private InterpolatedStringExpr ParseInterpolatedString(string text, Token startToken) {
		var parts = new List<InterpolatedPart>();
		var i = 0;

		while (i < text.Length) {
			if (text[i] == '{') {
				// Find matching }
				var start = i + 1;
				var depth = 1;
				var j = start;
				while (j < text.Length && depth > 0) {
					if (text[j] == '{') depth++;
					else if (text[j] == '}') depth--;
					j++;
				}

				var exprText = text[start..(j - 1)];

				// Check for format specifier
				string? formatSpec = null;
				var colonIdx = exprText.IndexOf(':');
				if (colonIdx >= 0) {
					formatSpec = exprText[(colonIdx + 1)..];
					exprText = exprText[..colonIdx];
				}

				// Parse the expression
				var exprLexer = new Lexer(exprText);
				var exprTokens = exprLexer.Tokenize();
				var exprParser = new Parser(exprTokens, _sourceFileIndex);
				var expr = exprParser.ParseExpression();

				parts.Add(new InterpolatedPart(true, null, expr, formatSpec));
				i = j;
			} else {
				// Literal text
				var start = i;
				while (i < text.Length && text[i] != '{') {
					if (text[i] == '\\' && i + 1 < text.Length) {
						i += 2; // Skip escape sequence
					} else {
						i++;
					}
				}
				var literal = text[start..i];
				if (literal.Length > 0) {
					parts.Add(new InterpolatedPart(false, literal, null, null));
				}
			}
		}

		return new InterpolatedStringExpr(parts) { Location = SourceLocation.Point(_sourceFileIndex, startToken.Line, startToken.Column) };
	}

	// ============================================================================
	// Helpers
	// ============================================================================

	/// <summary>
	/// Checks if the current position starts a field declaration.
	/// Handles: var, let, export var, export let, static var, static let, export static var, export static let
	/// </summary>
	private bool IsFieldStart() {
		var offset = 0;

		// Skip optional export
		if (Peek(offset)?.Type == TokenType.Export) offset++;

		// Skip optional static
		if (Peek(offset)?.Type == TokenType.Static) offset++;

		// Must be var or let
		var tokenType = Peek(offset)?.Type;
		return tokenType == TokenType.Var || tokenType == TokenType.Let;
	}

	/// <summary>
	/// Checks if the current position starts a method declaration.
	/// Handles: function, static function, export function, export static function
	/// </summary>
	private bool IsMethodStart() {
		var offset = 0;

		// Skip optional export
		if (Peek(offset)?.Type == TokenType.Export) offset++;

		// Skip optional static
		if (Peek(offset)?.Type == TokenType.Static) offset++;

		// Must be function
		return Peek(offset)?.Type == TokenType.Function;
	}

	private static long ParseIntegerLiteral(string text) {
		text = text.Replace("_", "");

		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
			return Convert.ToInt64(text[2..], 16);
		}
		if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
			return Convert.ToInt64(text[2..], 2);
		}
		if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) {
			return Convert.ToInt64(text[2..], 8);
		}

		return long.Parse(text);
	}

	private string ExpectBlockLabel() {
		var token = Expect(TokenType.CharacterLiteral);
		return token.Value;
	}

	private int ExpectEndLabel(string expectedLabel) {
		var endToken = Expect(TokenType.End);
		if (Check(TokenType.CharacterLiteral)) {
			var label = Advance().Value;
			if (label != expectedLabel) {
				throw new CompileError(ErrorCode.ParserMismatchedEndLabel, $"Mismatched end label: expected '{expectedLabel}', got '{label}'", endToken.Line, endToken.Column);
			}
		}
		return endToken.Line;
	}

	private void SkipNewlines() {
		while (Check(TokenType.Newline) || Check(TokenType.DocComment)) {
			Advance();
		}
	}

	private Token Current() => _pos < _tokens.Count ? _tokens[_pos] : new Token(TokenType.Eof, "", 0, 0);

	private Token? Peek(int offset) {
		var pos = _pos + offset;
		return pos < _tokens.Count ? _tokens[pos] : null;
	}

	private bool Check(TokenType type) => !IsAtEnd() && Current().Type == type;

	private Token Advance() {
		if (!IsAtEnd()) _pos++;
		return _tokens[_pos - 1];
	}

	private Token Expect(TokenType type) {
		if (!Check(type)) {
			throw new CompileError(ErrorCode.ParserExpectedToken, $"Expected {type} but got {Current().Type}", Current().Line, Current().Column);
		}
		return Advance();
	}

	private bool IsAtEnd() => _pos >= _tokens.Count;
}
