#ifndef AST_H
#define AST_H

#include <string>
#include <vector>
#include <memory>

// Forward declarations
class Visitor;

// Base AST Node
class ASTNode {
public:
    virtual ~ASTNode() = default;
};

// Expression nodes
class ExprAST : public ASTNode {
public:
    int line;
    int column;
    
    ExprAST(int l = 0, int c = 0) : line(l), column(c) {}
    virtual ~ExprAST() = default;
};

// Number literal
class NumberExprAST : public ExprAST {
public:
    int value;
    
    NumberExprAST(int val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
};

// Float literal
class FloatExprAST : public ExprAST {
public:
    double value;
    
    FloatExprAST(double val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
};

// Variable reference
class VariableExprAST : public ExprAST {
public:
    std::string name;
    
    VariableExprAST(const std::string& n, int l = 0, int c = 0) : ExprAST(l, c), name(n) {}
};

// Boolean literal
class BooleanExprAST : public ExprAST {
public:
    bool value;
    
    BooleanExprAST(bool val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
};

// Character literal
class CharacterExprAST : public ExprAST {
public:
    char value;
    
    CharacterExprAST(char val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
};

// String literal
class StringLiteralExprAST : public ExprAST {
public:
    std::string value;
    
    StringLiteralExprAST(const std::string& val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
};

// Type cast expression (e.g., "value as ptr")
class CastExprAST : public ExprAST {
public:
    std::unique_ptr<ExprAST> expr;
    std::string targetType;  // "int", "ptr", "char"
    
    CastExprAST(std::unique_ptr<ExprAST> e, const std::string& type, int l = 0, int c = 0)
        : ExprAST(l, c), expr(std::move(e)), targetType(type) {}
};

// Address-of expression (e.g., "&variable")
class AddressOfExprAST : public ExprAST {
public:
    std::string varName;
    
    AddressOfExprAST(const std::string& name, int l = 0, int c = 0)
        : ExprAST(l, c), varName(name) {}
};

// Dereference expression (e.g., "*ptr")
class DerefExprAST : public ExprAST {
public:
    std::unique_ptr<ExprAST> expr;
    
    DerefExprAST(std::unique_ptr<ExprAST> e, int l = 0, int c = 0)
        : ExprAST(l, c), expr(std::move(e)) {}
};

// Binary operation
class BinaryExprAST : public ExprAST {
public:
    char op;
    std::unique_ptr<ExprAST> left;
    std::unique_ptr<ExprAST> right;
    
    BinaryExprAST(char o, std::unique_ptr<ExprAST> l, std::unique_ptr<ExprAST> r, int line = 0, int col = 0)
        : ExprAST(line, col), op(o), left(std::move(l)), right(std::move(r)) {}
};

// Unary operation (e.g., -x, +x)
class UnaryExprAST : public ExprAST {
public:
    char op;  // '+' or '-'
    std::unique_ptr<ExprAST> operand;
    
    UnaryExprAST(char o, std::unique_ptr<ExprAST> expr, int line = 0, int col = 0)
        : ExprAST(line, col), op(o), operand(std::move(expr)) {}
};

// Function call
class CallExprAST : public ExprAST {
public:
    std::string callee;
    std::vector<std::unique_ptr<ExprAST>> args;
    
    CallExprAST(const std::string& c, std::vector<std::unique_ptr<ExprAST>> a, int l = 0, int col = 0)
        : ExprAST(l, col), callee(c), args(std::move(a)) {}
};

// Array index expression (e.g., "array[5]")
class ArrayIndexExprAST : public ExprAST {
public:
    std::string arrayName;
    std::unique_ptr<ExprAST> index;
    
    ArrayIndexExprAST(const std::string& name, std::unique_ptr<ExprAST> idx, int l = 0, int c = 0)
        : ExprAST(l, c), arrayName(name), index(std::move(idx)) {}
};

// Array literal expression
// Two forms: [5]int (zero-initialized array) or [1,2,3] (value-initialized array)
class ArrayLiteralExprAST : public ExprAST {
public:
    // For [size]type syntax
    int size;                                      // Array size (0 if value-init)
    std::string elementType;                       // Element type (empty if value-init)
    
    // For [val1, val2, ...] syntax
    std::vector<std::unique_ptr<ExprAST>> values;  // Element values (empty if size-init)
    
    // Constructor for [size]type syntax
    ArrayLiteralExprAST(int sz, const std::string& elemType, int l = 0, int c = 0)
        : ExprAST(l, c), size(sz), elementType(elemType) {}
    
    // Constructor for [val1, val2, ...] syntax
    ArrayLiteralExprAST(std::vector<std::unique_ptr<ExprAST>> vals, int l = 0, int c = 0)
        : ExprAST(l, c), size(0), values(std::move(vals)) {}
};

// Member access expression (e.g., "array.length")
class MemberAccessExprAST : public ExprAST {
public:
    std::unique_ptr<ExprAST> object;  // Can be any expression (variable, array subscript, etc.)
    std::string objectName;           // Keep for backward compatibility (when object is simple variable)
    std::string memberName;
    
    // Constructor for simple variable.member access
    MemberAccessExprAST(const std::string& obj, const std::string& member, int l = 0, int c = 0)
        : ExprAST(l, c), object(nullptr), objectName(obj), memberName(member) {}
    
    // Constructor for complex expression.member access (e.g., arr[0].member)
    MemberAccessExprAST(std::unique_ptr<ExprAST> obj, const std::string& member, int l = 0, int c = 0)
        : ExprAST(l, c), object(std::move(obj)), objectName(""), memberName(member) {}
};

// Statement nodes
class StmtAST : public ASTNode {
public:
    int line;
    int column;
    
    StmtAST(int l = 0, int c = 0) : line(l), column(c) {}
    virtual ~StmtAST() = default;
};

// Variable declaration
class VarDeclStmtAST : public StmtAST {
public:
    std::string name;
    std::string type;  // "int", "ptr", "char", or "" for inferred
    std::unique_ptr<ExprAST> initializer;
    
    VarDeclStmtAST(const std::string& n, std::unique_ptr<ExprAST> init, const std::string& t = "", int l = 0, int c = 0)
        : StmtAST(l, c), name(n), type(t), initializer(std::move(init)) {}
};

// Let declaration (immutable variable)
class LetDeclStmtAST : public StmtAST {
public:
    std::string name;
    std::string type;  // "int", "ptr", "char", or "" for inferred
    std::unique_ptr<ExprAST> initializer;
    
    LetDeclStmtAST(const std::string& n, std::unique_ptr<ExprAST> init, const std::string& t = "", int l = 0, int c = 0)
        : StmtAST(l, c), name(n), type(t), initializer(std::move(init)) {}
};

// Assignment statement
class AssignStmtAST : public StmtAST {
public:
    std::string name;
    std::unique_ptr<ExprAST> value;
    
    AssignStmtAST(const std::string& n, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
        : StmtAST(l, c), name(n), value(std::move(val)) {}
};

// Array assignment statement (e.g., "array[5] = 42")
class ArrayAssignStmtAST : public StmtAST {
public:
    std::string arrayName;
    std::unique_ptr<ExprAST> index;
    std::unique_ptr<ExprAST> value;
    
    ArrayAssignStmtAST(const std::string& name, std::unique_ptr<ExprAST> idx, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
        : StmtAST(l, c), arrayName(name), index(std::move(idx)), value(std::move(val)) {}
};

// Array element member assignment statement (e.g., "arr[0].field = 42")
class ArrayMemberAssignStmtAST : public StmtAST {
public:
    std::string arrayName;
    std::unique_ptr<ExprAST> index;
    std::string memberName;
    std::unique_ptr<ExprAST> value;
    
    ArrayMemberAssignStmtAST(const std::string& name, std::unique_ptr<ExprAST> idx, const std::string& member, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
        : StmtAST(l, c), arrayName(name), index(std::move(idx)), memberName(member), value(std::move(val)) {}
};

// Pointer dereference assignment statement (e.g., "*ptr = 42")
class DerefAssignStmtAST : public StmtAST {
public:
    std::unique_ptr<ExprAST> pointer;
    std::unique_ptr<ExprAST> value;
    
    DerefAssignStmtAST(std::unique_ptr<ExprAST> ptr, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
        : StmtAST(l, c), pointer(std::move(ptr)), value(std::move(val)) {}
};

// If statement
class IfStmtAST : public StmtAST {
public:
    std::unique_ptr<ExprAST> condition;
    std::vector<std::unique_ptr<StmtAST>> thenBody;
    std::vector<std::unique_ptr<StmtAST>> elseBody;
    
    IfStmtAST(std::unique_ptr<ExprAST> cond,
              std::vector<std::unique_ptr<StmtAST>> thenB,
              std::vector<std::unique_ptr<StmtAST>> elseB,
              int l = 0, int c = 0)
        : StmtAST(l, c), condition(std::move(cond)), 
          thenBody(std::move(thenB)),
          elseBody(std::move(elseB)) {}
};

// While statement
class WhileStmtAST : public StmtAST {
public:
    std::unique_ptr<ExprAST> condition;
    std::vector<std::unique_ptr<StmtAST>> body;
    
    WhileStmtAST(std::unique_ptr<ExprAST> cond,
                 std::vector<std::unique_ptr<StmtAST>> b,
                 int l = 0, int c = 0)
        : StmtAST(l, c), condition(std::move(cond)), body(std::move(b)) {}
};

// Return statement
class ReturnStmtAST : public StmtAST {
public:
    std::unique_ptr<ExprAST> value;
    
    ReturnStmtAST(std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
        : StmtAST(l, c), value(std::move(val)) {}
};

// Break statement
class BreakStmtAST : public StmtAST {
public:
    BreakStmtAST(int l = 0, int c = 0) : StmtAST(l, c) {}
};

// Continue statement
class ContinueStmtAST : public StmtAST {
public:
    ContinueStmtAST(int l = 0, int c = 0) : StmtAST(l, c) {}
};

// Expression statement (e.g., function call)
class ExprStmtAST : public StmtAST {
public:
    std::unique_ptr<ExprAST> expression;
    
    ExprStmtAST(std::unique_ptr<ExprAST> expr, int l = 0, int c = 0)
        : StmtAST(l, c), expression(std::move(expr)) {}
};

// Struct field definition
struct StructField {
    std::string name;
    std::string type;
    int line;
    int column;
    
    StructField(const std::string& n, const std::string& t, int l = 0, int c = 0)
        : name(n), type(t), line(l), column(c) {}
};

// Struct initialization field (name: value pair)
struct StructInitField {
    std::string name;
    std::unique_ptr<ExprAST> value;
    int line;
    int column;
    
    StructInitField(const std::string& n, std::unique_ptr<ExprAST> v, int l = 0, int c = 0)
        : name(n), value(std::move(v)), line(l), column(c) {}
};

// Struct definition
class StructDefAST : public ASTNode {
public:
    std::string name;
    std::vector<StructField> fields;
    int line;
    int column;
    
    StructDefAST(const std::string& n, std::vector<StructField> f, int l = 0, int c = 0)
        : name(n), fields(std::move(f)), line(l), column(c) {}
};

// Struct initialization expression (struct literal)
class StructInitExprAST : public ExprAST {
public:
    std::string structName;
    std::vector<StructInitField> fields;
    
    StructInitExprAST(const std::string& name, std::vector<StructInitField> f, int l = 0, int c = 0)
        : ExprAST(l, c), structName(name), fields(std::move(f)) {}
};

// Function parameter
struct FunctionParameter {
    std::string name;
    std::string type;
    int line;
    int column;
    
    FunctionParameter(const std::string& n, const std::string& t, int l = 0, int c = 0)
        : name(n), type(t), line(l), column(c) {}
};

// Function declaration
class FunctionAST : public ASTNode {
public:
    std::string name;
    std::string namespaceName;  // Namespace this function belongs to (may be empty for global)
    std::vector<FunctionParameter> parameters;
    std::string returnType;
    std::vector<std::unique_ptr<StmtAST>> body;
    bool isExtern;  // true if this is an extern function declaration
    int line;
    int column;
    
    FunctionAST(const std::string& n, 
                std::vector<FunctionParameter> params,
                const std::string& ret,
                std::vector<std::unique_ptr<StmtAST>> b,
                bool ext = false,
                int l = 1, int c = 1,
                const std::string& ns = "")
        : name(n), namespaceName(ns), parameters(std::move(params)), returnType(ret), body(std::move(b)),
          isExtern(ext), line(l), column(c) {}
};

// Namespace declaration
class NamespaceAST : public ASTNode {
public:
    std::string name;
    std::vector<std::unique_ptr<FunctionAST>> functions;
    int line;
    int column;
    
    NamespaceAST(const std::string& n, 
                 std::vector<std::unique_ptr<FunctionAST>> funcs,
                 int l = 1, int c = 1)
        : name(n), functions(std::move(funcs)), line(l), column(c) {}
};

// Program (collection of functions and namespaces)
class ProgramAST : public ASTNode {
public:
    std::vector<std::unique_ptr<FunctionAST>> functions;
    std::vector<std::unique_ptr<NamespaceAST>> namespaces;
    std::vector<std::unique_ptr<StructDefAST>> structs;
    
    ProgramAST() = default;
    
    ProgramAST(std::vector<std::unique_ptr<FunctionAST>> funcs,
               std::vector<std::unique_ptr<NamespaceAST>> ns = {},
               std::vector<std::unique_ptr<StructDefAST>> st = {})
        : functions(std::move(funcs)), namespaces(std::move(ns)), structs(std::move(st)) {}
};

#endif // AST_H
