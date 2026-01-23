namespace MaxonSharp.Parser;

// ============================================================================
// Source Location
// ============================================================================

public record SourceLocation(int Line, int Column);

// ============================================================================
// Type References
// ============================================================================

public abstract record TypeRef {
	public SourceLocation? Location { get; init; }
}

public record SimpleTypeRef(string Name) : TypeRef;
public record GenericTypeRef(string BaseName, List<string> TypeArgs) : TypeRef;
public record FunctionTypeRef(List<TypeRef> ParamTypes, TypeRef? ReturnType) : TypeRef;
public record ErrorUnionTypeRef(TypeRef SuccessType, string ErrorType) : TypeRef;

// ============================================================================
// Expressions
// ============================================================================

public abstract record Expr {
	public SourceLocation? Location { get; init; }
}

// Literals
public record IntLiteralExpr(long Value) : Expr;
public record FloatLiteralExpr(double Value) : Expr;
public record BoolLiteralExpr(bool Value) : Expr;
public record StringLiteralExpr(string Value) : Expr;
public record CharLiteralExpr(string Value) : Expr;
public record IdentifierExpr(string Name) : Expr;
public record SelfExpr : Expr;

// Interpolated string: "Hello {name}!"
public record InterpolatedPart(bool IsExpression, string? LiteralValue, Expr? Expression, string? FormatSpec);
public record InterpolatedStringExpr(List<InterpolatedPart> Parts) : Expr;

// Operators
public enum BinaryOp { Add, Sub, Mul, Div, Mod, Band, Bor, Bxor, Shl, Shr }
public enum CompareOp { Eq, Ne, Lt, Le, Gt, Ge }
public enum LogicalOp { And, Or }
public enum UnaryOp { Negate, Not }

public record BinaryExpr(Expr Left, BinaryOp Op, Expr Right) : Expr;
public record CompareExpr(Expr Left, CompareOp Op, Expr Right) : Expr;
public record LogicalExpr(Expr Left, LogicalOp Op, Expr Right) : Expr;
public record UnaryExpr(UnaryOp Op, Expr Operand) : Expr;

// Calls and member access
public record NamedArg(string Name, Expr Value);
public record CallExpr(string FuncName, List<Expr> Args, List<NamedArg> NamedArgs) : Expr;
public record MethodCallExpr(Expr Base, string MethodName, List<Expr> Args, List<NamedArg> NamedArgs) : Expr;
public record FieldAccessExpr(Expr Base, string FieldName) : Expr;
public record IndexExpr(Expr Base, Expr Index) : Expr;

// Struct and array literals
public record FieldInit(string Name, Expr Value);
public record StructInitExpr(string? TypeName, List<string> TypeArgs, List<FieldInit> Fields) : Expr;
public record ArrayLiteralExpr(List<Expr> Elements) : Expr;

// Map literal
public record MapEntry(Expr Key, Expr Value);
public record MapLiteralExpr(List<MapEntry> Entries) : Expr;

// Static method call: Type.method(args) - resolved to enum case or static method in HIR
public record StaticCallExpr(string TypeName, string MemberName, List<Expr> Args, List<NamedArg> NamedArgs) : Expr;

// Enum case construction: Result.success(42) - generated from StaticCallExpr in HIR
public record EnumCaseExpr(string EnumName, string CaseName, List<Expr> Args) : Expr;

// Cast expression: value as int
public record CastExpr(Expr Expression, string TargetType) : Expr;

// Error handling: try expr otherwise default
public enum OtherwiseMode { DefaultExpr, Ignore, Block, BlockWithErr }
public record OtherwiseClause(OtherwiseMode Mode, Expr? DefaultExpr, string? ErrorBinding, List<Stmt>? Body);
public record TryExpr(Expr Expression, OtherwiseClause? Otherwise) : Expr;

// Match expression
public record PatternBinding(string CaseName, List<string> Bindings);
public record MatchExprCase(List<Expr> Patterns, List<PatternBinding?> PatternBindings, Expr Result);
public record MatchExpr(Expr Scrutinee, List<MatchExprCase> Cases, Expr? DefaultExpr, string Label) : Expr;

// Range expression for iteration: start..end, start..<end, start..=end
public record RangeExpr(Expr Start, Expr End, bool Inclusive) : Expr;

// Range pattern for match: 1..=5, 1..<5, 1.., ..=5
public record RangePatternExpr(Expr? Lower, Expr? Upper, bool Inclusive) : Expr;

// Closure: (x int) gives x * 2
public record ClosureParam(string Name, string? TypeName);
public record ClosureExpr(List<ClosureParam> Params, Expr Body) : Expr;

// InitFromArray: TypeName from [1, 2, 3]
public record InitFromArrayExpr(string TypeName, List<string> TypeArgs, Expr Elements) : Expr;

// ============================================================================
// Statements
// ============================================================================

public abstract record Stmt {
	public SourceLocation? Location { get; init; }
}

public record ReturnStmt(Expr? Value) : Stmt;
public record LetDeclStmt(string Name, TypeRef? TypeAnnotation, Expr Value) : Stmt;
public record VarDeclStmt(string Name, TypeRef? TypeAnnotation, Expr Value) : Stmt;
public record AssignStmt(string Target, Expr Value) : Stmt;
public record FieldAssignStmt(Expr Base, string FieldName, Expr Value) : Stmt;
public record IndexAssignStmt(Expr Base, Expr Index, Expr Value) : Stmt;
public record ExprStmt(Expr Expression) : Stmt;  // For call/method_call as statement
public record ThrowStmt(Expr ErrorExpr) : Stmt;
public record BreakStmt(string? Label) : Stmt;
public record ContinueStmt(string? Label) : Stmt;

// Control flow with child blocks
public record BlockInfo(int StartLine, int StartColumn, int EndLine, string? Identifier);

public record IfStmt(Expr Condition, List<Stmt> ThenBody, BlockInfo ThenBlock, List<Stmt>? ElseBody, BlockInfo? ElseBlock) : Stmt;
public record WhileStmt(Expr Condition, List<Stmt> Body, BlockInfo Block) : Stmt;
public record ForStmt(string VarName, Expr Iterable, List<Stmt> Body, BlockInfo Block) : Stmt;

// Match statement
public record MatchCase(List<Expr> Patterns, List<PatternBinding?> PatternBindings, List<Stmt> Body, bool HasFallthrough);
public record MatchStmt(Expr Scrutinee, List<MatchCase> Cases, List<Stmt>? DefaultBody, BlockInfo Block) : Stmt;

// ============================================================================
// Declarations
// ============================================================================

public record ParamDecl(string Name, TypeRef Type, Expr? DefaultValue = null) {
	public SourceLocation? Location { get; init; }
}

public record FieldDecl(string Name, TypeRef Type, bool IsMutable, bool IsExport = false, bool IsStatic = false, Expr? DefaultValue = null);

public record MethodDecl(
	string Name,
	bool IsStatic,
	bool IsExport,
	List<ParamDecl> Params,
	TypeRef? ReturnType,
	string? ThrowsType,
	List<Stmt> Body,
	BlockInfo Block,
	string? DocComment = null
);

public record FunctionDecl(
	string Name,
	bool IsExport,
	List<ParamDecl> Params,
	TypeRef? ReturnType,
	string? ThrowsType,
	List<Stmt> Body,
	BlockInfo Block,
	string? DocComment = null
);

public record TypeDecl(
	string Name,
	bool IsExport,
	List<string> GenericParams,
	List<InterfaceConformance> Conformances,
	List<TypeAliasDecl> AssociatedTypes,
	List<FieldDecl> Fields,
	List<MethodDecl> Methods,
	BlockInfo Block
);

public record EnumMember(string Name, Expr? Value, List<ParamDecl> AssociatedValues) {
	public SourceLocation? Location { get; init; }
}

public record EnumDecl(
	string Name,
	bool IsExport,
	List<InterfaceConformance> Conformances,
	List<EnumMember> Members,
	List<MethodDecl> Methods,
	BlockInfo Block
);

public record InterfaceConformance(string InterfaceName, List<string> TypeArgs);

public record InterfaceMethod(string Name, bool IsStatic, List<ParamDecl> Params, TypeRef? ReturnType, string? ThrowsType);

public record InterfaceDecl(
	string Name,
	bool IsExport,
	List<string> GenericParams,
	List<string> Extends,
	List<TypeAliasDecl> AssociatedTypes,
	List<InterfaceMethod> Methods,
	BlockInfo Block
);

public record ExtensionMethod(
	string Name,
	List<ParamDecl> Params,
	TypeRef? ReturnType,
	string? ThrowsType,
	List<Stmt> Body,
	BlockInfo Block
);

public record ExtensionDecl(
	string InterfaceName,
	bool IsExport,
	List<TypeAliasDecl> AssociatedTypes,
	List<ExtensionMethod> Methods,
	BlockInfo Block
);

public record TypeAliasDecl(string Name, string BaseType, List<string> TypeArgs, bool IsExport = false) {
	public SourceLocation? Location { get; init; }
}

public record GlobalConstant(string Name, bool IsExport, Expr Value) {
	public SourceLocation? Location { get; init; }
}

public record GlobalVariable(string Name, bool IsExport, Expr Value) {
	public SourceLocation? Location { get; init; }
}

// ============================================================================
// Program (root)
// ============================================================================

public record ProgramAst(
	List<TypeDecl> Types,
	List<EnumDecl> Enums,
	List<InterfaceDecl> Interfaces,
	List<ExtensionDecl> Extensions,
	List<FunctionDecl> Functions,
	List<GlobalConstant> GlobalConstants,
	List<GlobalVariable> GlobalVariables,
	List<TypeAliasDecl> TypeAliases
);
