namespace MaxonSharp.Parser;

// Type references
public abstract record TypeRef;
public record IntTypeRef : TypeRef;

// Expressions
public abstract record Expr;
public record IntLiteralExpr(long Value) : Expr;
public record IdentifierExpr(string Name) : Expr;

// Statements
public abstract record Stmt;
public record ReturnStmt(Expr? Value) : Stmt;

// Declarations
public record FunctionDecl(string Name, TypeRef? ReturnType, List<Stmt> Body);

// Program (root)
public record ProgramAst(List<FunctionDecl> Functions);
