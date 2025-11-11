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

// Function call
class CallExprAST : public ExprAST {
public:
    std::string callee;
    std::vector<std::unique_ptr<ExprAST>> args;
    
    CallExprAST(const std::string& c, std::vector<std::unique_ptr<ExprAST>> a, int l = 0, int col = 0)
        : ExprAST(l, col), callee(c), args(std::move(a)) {}
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
    std::unique_ptr<ExprAST> initializer;
    
    VarDeclStmtAST(const std::string& n, std::unique_ptr<ExprAST> init, int l = 0, int c = 0)
        : StmtAST(l, c), name(n), initializer(std::move(init)) {}
};

// Let declaration (immutable variable)
class LetDeclStmtAST : public StmtAST {
public:
    std::string name;
    std::unique_ptr<ExprAST> initializer;
    
    LetDeclStmtAST(const std::string& n, std::unique_ptr<ExprAST> init, int l = 0, int c = 0)
        : StmtAST(l, c), name(n), initializer(std::move(init)) {}
};

// Assignment statement
class AssignStmtAST : public StmtAST {
public:
    std::string name;
    std::unique_ptr<ExprAST> value;
    
    AssignStmtAST(const std::string& n, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
        : StmtAST(l, c), name(n), value(std::move(val)) {}
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
                int l = 1, int c = 1)
        : name(n), parameters(std::move(params)), returnType(ret), body(std::move(b)),
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
    
    ProgramAST(std::vector<std::unique_ptr<FunctionAST>> funcs,
               std::vector<std::unique_ptr<NamespaceAST>> ns = {})
        : functions(std::move(funcs)), namespaces(std::move(ns)) {}
};

#endif // AST_H
