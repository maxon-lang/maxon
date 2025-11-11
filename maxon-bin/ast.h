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
    virtual ~ExprAST() = default;
};

// Number literal
class NumberExprAST : public ExprAST {
public:
    int value;
    
    NumberExprAST(int val) : value(val) {}
};

// Variable reference
class VariableExprAST : public ExprAST {
public:
    std::string name;
    
    VariableExprAST(const std::string& n) : name(n) {}
};

// Boolean literal
class BooleanExprAST : public ExprAST {
public:
    bool value;
    
    BooleanExprAST(bool val) : value(val) {}
};

// Binary operation
class BinaryExprAST : public ExprAST {
public:
    char op;
    std::unique_ptr<ExprAST> left;
    std::unique_ptr<ExprAST> right;
    
    BinaryExprAST(char o, std::unique_ptr<ExprAST> l, std::unique_ptr<ExprAST> r)
        : op(o), left(std::move(l)), right(std::move(r)) {}
};

// Function call
class CallExprAST : public ExprAST {
public:
    std::string callee;
    std::vector<std::unique_ptr<ExprAST>> args;
    
    CallExprAST(const std::string& c, std::vector<std::unique_ptr<ExprAST>> a)
        : callee(c), args(std::move(a)) {}
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
    int line;
    int column;
    
    FunctionAST(const std::string& n, 
                std::vector<FunctionParameter> params,
                const std::string& ret,
                std::vector<std::unique_ptr<StmtAST>> b,
                int l = 1, int c = 1)
        : name(n), parameters(std::move(params)), returnType(ret), body(std::move(b)),
          line(l), column(c) {}
};

// Program (collection of functions)
class ProgramAST : public ASTNode {
public:
    std::vector<std::unique_ptr<FunctionAST>> functions;
    
    ProgramAST(std::vector<std::unique_ptr<FunctionAST>> funcs)
        : functions(std::move(funcs)) {}
};

#endif // AST_H
