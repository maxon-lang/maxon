#ifndef AST_H
#define AST_H

#include <map>
#include <memory>
#include <string>
#include <vector>

// Forward declarations
class Visitor;
class FunctionAST;

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

// Byte literal (8-bit unsigned, 0-255)
class ByteExprAST : public ExprAST {
  public:
	uint8_t value;

	ByteExprAST(uint8_t val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
};

// Float literal
class FloatExprAST : public ExprAST {
  public:
	double value;
	std::string literalString; // Original string representation from source

	FloatExprAST(double val, int l = 0, int c = 0, const std::string &literal = "")
		: ExprAST(l, c), value(val), literalString(literal) {}
};

// Variable reference
class VariableExprAST : public ExprAST {
  public:
	std::string name;

	VariableExprAST(const std::string &n, int l = 0, int c = 0) : ExprAST(l, c), name(n) {}
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
	bool asByteSlice = false; // When true, generate as []byte slice instead of string struct

	StringLiteralExprAST(const std::string &val, int l = 0, int c = 0)
		: ExprAST(l, c), value(val) {}
};

// Type cast expression (e.g., "value as ptr")
class CastExprAST : public ExprAST {
  public:
	std::unique_ptr<ExprAST> expr;
	std::string targetType; // "int", "float", "char", "bool"

	CastExprAST(std::unique_ptr<ExprAST> e, const std::string &type, int l = 0, int c = 0)
		: ExprAST(l, c), expr(std::move(e)), targetType(type) {}
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
	char op; // '+' or '-'
	std::unique_ptr<ExprAST> operand;

	UnaryExprAST(char o, std::unique_ptr<ExprAST> expr, int line = 0, int col = 0)
		: ExprAST(line, col), op(o), operand(std::move(expr)) {}
};

// Function call
class CallExprAST : public ExprAST {
  public:
	std::string callee;
	std::vector<std::unique_ptr<ExprAST>> args;
	size_t functionId = SIZE_MAX; // Resolved during semantic analysis (SIZE_MAX = unresolved)

	CallExprAST(const std::string &c, std::vector<std::unique_ptr<ExprAST>> a, int l = 0, int col = 0)
		: ExprAST(l, col), callee(c), args(std::move(a)) {}
};

// Array index expression (e.g., "array[5]" or "struct.field[5]")
class ArrayIndexExprAST : public ExprAST {
  public:
	std::string arrayName;				// For simple array[i] access
	std::unique_ptr<ExprAST> arrayExpr; // For complex array access (e.g., struct.field[i])
	std::unique_ptr<ExprAST> index;

	// Constructor for simple array[i] access
	ArrayIndexExprAST(const std::string &name, std::unique_ptr<ExprAST> idx, int l = 0, int c = 0)
		: ExprAST(l, c), arrayName(name), arrayExpr(nullptr), index(std::move(idx)) {}

	// Constructor for complex expr[i] access (e.g., struct.field[i])
	ArrayIndexExprAST(std::unique_ptr<ExprAST> arrExpr, std::unique_ptr<ExprAST> idx, int l = 0, int c = 0)
		: ExprAST(l, c), arrayName(""), arrayExpr(std::move(arrExpr)), index(std::move(idx)) {}

	// Helper to check if this is a complex array access
	bool hasArrayExpr() const { return arrayExpr != nullptr; }
};

// Slice expression (e.g., "str[0..5]", "str[2..]", "str[..5]")
class SliceExprAST : public ExprAST {
  public:
	std::string objectName;
	std::unique_ptr<ExprAST> start; // nullptr means from beginning (0)
	std::unique_ptr<ExprAST> end;	// nullptr means to end

	SliceExprAST(const std::string &name, std::unique_ptr<ExprAST> s, std::unique_ptr<ExprAST> e, int l = 0, int c = 0)
		: ExprAST(l, c), objectName(name), start(std::move(s)), end(std::move(e)) {}
};

// Array literal expression
// Two forms: [5]int (zero-initialized array) or [1,2,3] (value-initialized array)
class ArrayLiteralExprAST : public ExprAST {
  public:
	// For [size]type syntax
	int size;				 // Array size (0 if value-init)
	std::string elementType; // Element type (empty if value-init)

	// For [val1, val2, ...] syntax
	std::vector<std::unique_ptr<ExprAST>> values; // Element values (empty if size-init)

	// Constructor for [size]type syntax
	ArrayLiteralExprAST(int sz, const std::string &elemType, int l = 0, int c = 0)
		: ExprAST(l, c), size(sz), elementType(elemType) {}

	// Constructor for [val1, val2, ...] syntax
	ArrayLiteralExprAST(std::vector<std::unique_ptr<ExprAST>> vals, int l = 0, int c = 0)
		: ExprAST(l, c), size(0), values(std::move(vals)) {}
};

// Member access expression (e.g., "array.length")
class MemberAccessExprAST : public ExprAST {
  public:
	std::unique_ptr<ExprAST> object; // Can be any expression (variable, array subscript, etc.)
	std::string objectName;			 // Keep for backward compatibility (when object is simple variable)
	std::string memberName;

	// Constructor for simple variable.member access
	MemberAccessExprAST(const std::string &obj, const std::string &member, int l = 0, int c = 0)
		: ExprAST(l, c), object(nullptr), objectName(obj), memberName(member) {}

	// Constructor for complex expression.member access (e.g., arr[0].member)
	MemberAccessExprAST(std::unique_ptr<ExprAST> obj, const std::string &member, int l = 0, int c = 0)
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
	std::string type; // "int", "ptr", "char", or "" for inferred
	std::unique_ptr<ExprAST> initializer;

	VarDeclStmtAST(const std::string &n, std::unique_ptr<ExprAST> init, const std::string &t = "", int l = 0, int c = 0)
		: StmtAST(l, c), name(n), type(t), initializer(std::move(init)) {}
};

// Let declaration (immutable variable)
class LetDeclStmtAST : public StmtAST {
  public:
	std::string name;
	std::string type; // "int", "ptr", "char", or "" for inferred
	std::unique_ptr<ExprAST> initializer;

	LetDeclStmtAST(const std::string &n, std::unique_ptr<ExprAST> init, const std::string &t = "", int l = 0, int c = 0)
		: StmtAST(l, c), name(n), type(t), initializer(std::move(init)) {}
};

// Assignment statement
class AssignStmtAST : public StmtAST {
  public:
	std::string name;
	std::unique_ptr<ExprAST> value;

	AssignStmtAST(const std::string &n, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
		: StmtAST(l, c), name(n), value(std::move(val)) {}
};

// Array assignment statement (e.g., "array[5] = 42")
class ArrayAssignStmtAST : public StmtAST {
  public:
	std::string arrayName;
	std::unique_ptr<ExprAST> index;
	std::unique_ptr<ExprAST> value;

	ArrayAssignStmtAST(const std::string &name, std::unique_ptr<ExprAST> idx, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
		: StmtAST(l, c), arrayName(name), index(std::move(idx)), value(std::move(val)) {}
};

// Array element member assignment statement (e.g., "arr[0].field = 42")
class ArrayMemberAssignStmtAST : public StmtAST {
  public:
	std::string arrayName;
	std::unique_ptr<ExprAST> index;
	std::string memberName;
	std::unique_ptr<ExprAST> value;

	ArrayMemberAssignStmtAST(const std::string &name, std::unique_ptr<ExprAST> idx, const std::string &member, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
		: StmtAST(l, c), arrayName(name), index(std::move(idx)), memberName(member), value(std::move(val)) {}
};

// Struct member assignment statement (e.g., "point.x = 42")
class MemberAssignStmtAST : public StmtAST {
  public:
	std::string objectName;
	std::string memberName;
	std::unique_ptr<ExprAST> value;

	MemberAssignStmtAST(const std::string &obj, const std::string &member, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
		: StmtAST(l, c), objectName(obj), memberName(member), value(std::move(val)) {}
};

// Struct member array element assignment statement (e.g., "obj.arrayField[i] = value")
class MemberArrayAssignStmtAST : public StmtAST {
  public:
	std::string objectName;
	std::string memberName;
	std::unique_ptr<ExprAST> index;
	std::unique_ptr<ExprAST> value;

	MemberArrayAssignStmtAST(const std::string &obj, const std::string &member, std::unique_ptr<ExprAST> idx, std::unique_ptr<ExprAST> val, int l = 0, int c = 0)
		: StmtAST(l, c), objectName(obj), memberName(member), index(std::move(idx)), value(std::move(val)) {}
};

// If statement
class IfStmtAST : public StmtAST {
  public:
	std::unique_ptr<ExprAST> condition;
	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;
	std::string blockId; // Block identifier for multi-line if (empty for single-line)

	IfStmtAST(std::unique_ptr<ExprAST> cond,
			  std::vector<std::unique_ptr<StmtAST>> thenB,
			  std::vector<std::unique_ptr<StmtAST>> elseB,
			  int l = 0, int c = 0,
			  const std::string &bid = "")
		: StmtAST(l, c), condition(std::move(cond)),
		  thenBody(std::move(thenB)),
		  elseBody(std::move(elseB)),
		  blockId(bid) {}
};

// While statement
class WhileStmtAST : public StmtAST {
  public:
	std::unique_ptr<ExprAST> condition;
	std::vector<std::unique_ptr<StmtAST>> body;
	std::string blockId; // Block identifier

	WhileStmtAST(std::unique_ptr<ExprAST> cond,
				 std::vector<std::unique_ptr<StmtAST>> b,
				 int l = 0, int c = 0,
				 const std::string &bid = "")
		: StmtAST(l, c), condition(std::move(cond)), body(std::move(b)), blockId(bid) {}
};

// For statement (desugars to iterator-based while loop)
class ForStmtAST : public StmtAST {
  public:
	std::string loopVar;						// Loop variable name (e.g., "i")
	std::unique_ptr<ExprAST> iterable;			// What to iterate over (range, array, etc.)
	std::vector<std::unique_ptr<StmtAST>> body; // Loop body
	std::string blockId;						// Block identifier

	ForStmtAST(const std::string &var,
			   std::unique_ptr<ExprAST> iter,
			   std::vector<std::unique_ptr<StmtAST>> b,
			   int l = 0, int c = 0,
			   const std::string &bid = "")
		: StmtAST(l, c), loopVar(var), iterable(std::move(iter)), body(std::move(b)), blockId(bid) {}
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
	std::string targetLabel; // Empty string means break innermost loop

	BreakStmtAST(int l = 0, int c = 0, const std::string &label = "")
		: StmtAST(l, c), targetLabel(label) {}
};

// Continue statement
class ContinueStmtAST : public StmtAST {
  public:
	std::string targetLabel; // Empty string means continue innermost loop

	ContinueStmtAST(int l = 0, int c = 0, const std::string &label = "")
		: StmtAST(l, c), targetLabel(label) {}
};

// Expression statement (e.g., function call)
class ExprStmtAST : public StmtAST {
  public:
	std::unique_ptr<ExprAST> expression;

	ExprStmtAST(std::unique_ptr<ExprAST> expr, int l = 0, int c = 0)
		: StmtAST(l, c), expression(std::move(expr)) {}
};

// Function parameter (defined early for use by InterfaceMethodSignature)
struct FunctionParameter {
	std::string name;
	std::string type;
	int line;
	int column;

	FunctionParameter(const std::string &n, const std::string &t, int l = 0, int c = 0)
		: name(n), type(t), line(l), column(c) {}
};

// Interface method signature (for interface definitions)
struct InterfaceMethodSignature {
	std::string name;
	std::vector<FunctionParameter> parameters; // First param is 'self' with type 'Self'
	std::string returnType;
	int line;
	int column;

	InterfaceMethodSignature(const std::string &n, std::vector<FunctionParameter> params,
							 const std::string &ret, int l = 0, int c = 0)
		: name(n), parameters(std::move(params)), returnType(ret), line(l), column(c) {}
};

// Interface definition
class InterfaceDefAST : public ASTNode {
  public:
	std::string name;
	std::string namespaceName; // Namespace this interface belongs to
	std::vector<InterfaceMethodSignature> methods;
	std::vector<std::string> associatedTypes; // Associated type declarations (e.g., "Element")
	bool isExported;
	int line;
	int column;

	InterfaceDefAST(const std::string &n, std::vector<InterfaceMethodSignature> m,
					int l = 0, int c = 0, const std::string &ns = "", bool exp = false,
					std::vector<std::string> assocTypes = {})
		: name(n), namespaceName(ns), methods(std::move(m)), associatedTypes(std::move(assocTypes)),
		  isExported(exp), line(l), column(c) {}
};

// Struct field definition
struct StructField {
	std::string name;
	std::string type;
	int line;
	int column;

	StructField(const std::string &n, const std::string &t, int l = 0, int c = 0)
		: name(n), type(t), line(l), column(c) {}
};

// Struct initialization field (name: value pair)
struct StructInitField {
	std::string name;
	std::unique_ptr<ExprAST> value;
	int line;
	int column;

	StructInitField(const std::string &n, std::unique_ptr<ExprAST> v, int l = 0, int c = 0)
		: name(n), value(std::move(v)), line(l), column(c) {}
};

// Struct definition
class StructDefAST : public ASTNode {
  public:
	std::string name;
	std::string namespaceName; // Namespace this struct belongs to (derived from file path)
	std::vector<StructField> fields;
	std::vector<std::unique_ptr<FunctionAST>> methods;	// Methods declared inside the struct
	std::vector<std::string> conformsTo;				// Interface names this struct conforms to (via 'is')
	std::map<std::string, std::string> typeAssignments; // Associated type assignments (e.g., "Element" -> "char")
	bool isExported;									// true if this struct is exported (visible outside this file)
	int line;
	int column;

	StructDefAST(const std::string &n, std::vector<StructField> f, int l = 0, int c = 0,
				 const std::string &ns = "", bool exp = false,
				 std::vector<std::string> interfaces = {},
				 std::vector<std::unique_ptr<FunctionAST>> m = {},
				 std::map<std::string, std::string> typeAssigns = {})
		: name(n), namespaceName(ns), fields(std::move(f)), methods(std::move(m)),
		  conformsTo(std::move(interfaces)), typeAssignments(std::move(typeAssigns)),
		  isExported(exp), line(l), column(c) {}
};

// Struct initialization expression (struct literal)
class StructInitExprAST : public ExprAST {
  public:
	std::string structName;
	std::vector<StructInitField> fields;

	StructInitExprAST(const std::string &name, std::vector<StructInitField> f, int l = 0, int c = 0)
		: ExprAST(l, c), structName(name), fields(std::move(f)) {}
};

// Function declaration
class FunctionAST : public ASTNode {
  public:
	std::string name;
	std::string namespaceName; // Namespace this function belongs to (derived from file path)
	std::string receiverType;  // For methods: the type this method belongs to (e.g., "Point")
	std::vector<FunctionParameter> parameters;
	std::string returnType;
	std::vector<std::unique_ptr<StmtAST>> body;
	bool isExtern;		 // true if this is an extern function declaration
	bool isExported;	 // true if this function is exported (visible outside this file)
	std::string dllName; // DLL/lib name for extern functions (without extension)
	bool isStaticLib;	 // true if linking against a static library (.lib), false for DLL
	std::string libPath; // Full path to static library file (if isStaticLib)
	int line;
	int column;

	// Check if this is a method (has a receiver type)
	bool isMethod() const { return !receiverType.empty(); }

	FunctionAST(const std::string &n,
				std::vector<FunctionParameter> params,
				const std::string &ret,
				std::vector<std::unique_ptr<StmtAST>> b,
				bool ext = false,
				int l = 1, int c = 1,
				const std::string &ns = "",
				bool exp = false,
				const std::string &dll = "",
				bool staticLib = false,
				const std::string &libFilePath = "",
				const std::string &receiver = "")
		: name(n), namespaceName(ns), receiverType(receiver), parameters(std::move(params)), returnType(ret), body(std::move(b)),
		  isExtern(ext), isExported(exp), dllName(dll), isStaticLib(staticLib), libPath(libFilePath), line(l), column(c) {}
};

// Program (collection of functions, structs, and interfaces)
class ProgramAST : public ASTNode {
  public:
	std::vector<std::unique_ptr<FunctionAST>> functions;
	std::vector<std::unique_ptr<StructDefAST>> structs;
	std::vector<std::unique_ptr<InterfaceDefAST>> interfaces;

	ProgramAST() = default;

	ProgramAST(std::vector<std::unique_ptr<FunctionAST>> funcs,
			   std::vector<std::unique_ptr<StructDefAST>> st = {},
			   std::vector<std::unique_ptr<InterfaceDefAST>> protos = {})
		: functions(std::move(funcs)), structs(std::move(st)), interfaces(std::move(protos)) {}
};

#endif // AST_H
