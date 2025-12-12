#ifndef AST_H
#define AST_H

#include <map>
#include <memory>
#include <string>
#include <vector>

// Forward declarations
class Visitor;
class FunctionAST;

// Source range for precise location tracking (used by LSP)
struct SourceRange {
	int startLine;
	int startCol;
	int endLine;
	int endCol;

	// Default constructor - all zeros (indicates unset)
	SourceRange() : startLine(0), startCol(0), endLine(0), endCol(0) {}

	// Constructor with all four values
	SourceRange(int sLine, int sCol, int eLine, int eCol)
		: startLine(sLine), startCol(sCol), endLine(eLine), endCol(eCol) {}

	// Returns true if position is within range (inclusive)
	bool contains(int line, int col) const {
		// Handle unset range
		if (startLine == 0 && startCol == 0 && endLine == 0 && endCol == 0) {
			return false;
		}

		// Check if before start
		if (line < startLine || (line == startLine && col < startCol)) {
			return false;
		}

		// Check if after end
		if (line > endLine || (line == endLine && col > endCol)) {
			return false;
		}

		return true;
	}

	// Returns true if ranges overlap
	bool overlaps(const SourceRange &other) const {
		// Handle unset ranges
		if ((startLine == 0 && startCol == 0 && endLine == 0 && endCol == 0) ||
			(other.startLine == 0 && other.startCol == 0 && other.endLine == 0 && other.endCol == 0)) {
			return false;
		}

		// Check if this range ends before other starts
		if (endLine < other.startLine || (endLine == other.startLine && endCol < other.startCol)) {
			return false;
		}

		// Check if this range starts after other ends
		if (startLine > other.endLine || (startLine == other.endLine && startCol > other.endCol)) {
			return false;
		}

		return true;
	}
};

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
	int endLine;   // End position line (0 = unset)
	int endColumn; // End position column (0 = unset)

	ExprAST(int l = 0, int c = 0) : line(l), column(c), endLine(0), endColumn(0) {}
	virtual ~ExprAST() = default;

	// Clone this expression (for default parameter value injection)
	virtual ExprAST *clone() const { return nullptr; }

	// Helper to get the full source range of this expression
	SourceRange getSourceRange() const {
		return SourceRange(line, column, endLine, endColumn);
	}

	// Helper to set end position
	void setEndPosition(int eLine, int eCol) {
		endLine = eLine;
		endColumn = eCol;
	}
};

// Number literal
class NumberExprAST : public ExprAST {
  public:
	int value;

	NumberExprAST(int val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
	ExprAST *clone() const override { return new NumberExprAST(value, line, column); }
};

// Byte literal (8-bit unsigned, 0-255)
class ByteExprAST : public ExprAST {
  public:
	uint8_t value;

	ByteExprAST(uint8_t val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
	ExprAST *clone() const override { return new ByteExprAST(value, line, column); }
};

// Float literal
class FloatExprAST : public ExprAST {
  public:
	double value;
	std::string literalString; // Original string representation from source

	FloatExprAST(double val, int l = 0, int c = 0, const std::string &literal = "")
		: ExprAST(l, c), value(val), literalString(literal) {}
	ExprAST *clone() const override { return new FloatExprAST(value, line, column, literalString); }
};

// Variable reference
class VariableExprAST : public ExprAST {
  public:
	std::string name;
	bool isFunctionReference = false;		  // True when name refers to a function (function as value)
	mutable std::string resolvedFunctionName; // Fully qualified function name if isFunctionReference

	VariableExprAST(const std::string &n, int l = 0, int c = 0) : ExprAST(l, c), name(n) {}
	ExprAST *clone() const override { return new VariableExprAST(name, line, column); }
};

// Boolean literal
class BooleanExprAST : public ExprAST {
  public:
	bool value;

	BooleanExprAST(bool val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
	ExprAST *clone() const override { return new BooleanExprAST(value, line, column); }
};

// Character literal (grapheme cluster - may contain multiple bytes/codepoints)
class CharacterExprAST : public ExprAST {
  public:
	std::string value; // UTF-8 encoded grapheme cluster

	CharacterExprAST(const std::string &val, int l = 0, int c = 0) : ExprAST(l, c), value(val) {}
	ExprAST *clone() const override { return new CharacterExprAST(value, line, column); }
};

// String literal
class StringLiteralExprAST : public ExprAST {
  public:
	std::string value;
	bool asByteSlice = false; // When true, generate as []byte slice instead of string struct

	StringLiteralExprAST(const std::string &val, int l = 0, int c = 0)
		: ExprAST(l, c), value(val) {}
	ExprAST *clone() const override { return new StringLiteralExprAST(value, line, column); }
};

// Nil literal (represents absence in optional types)
class NilExprAST : public ExprAST {
  public:
	NilExprAST(int l = 0, int c = 0) : ExprAST(l, c) {}
	ExprAST *clone() const override { return new NilExprAST(line, column); }
};

// Type cast expression (e.g., "value as ptr")
class CastExprAST : public ExprAST {
  public:
	std::unique_ptr<ExprAST> expr;
	std::string targetType; // "int", "float", "character", "bool"

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

// Nil coalescing expression (e.g., "optionalValue or defaultValue")
// Unwraps optional if it has a value, otherwise evaluates to the default
class OrCoalesceExprAST : public ExprAST {
  public:
	std::unique_ptr<ExprAST> optionalExpr; // Must be T or nil
	std::unique_ptr<ExprAST> defaultExpr;  // Must be T (result type is T)

	OrCoalesceExprAST(std::unique_ptr<ExprAST> opt, std::unique_ptr<ExprAST> def, int line = 0, int col = 0)
		: ExprAST(line, col), optionalExpr(std::move(opt)), defaultExpr(std::move(def)) {}
};

// Call argument (for named arguments)
struct CallArgument {
	std::string name; // Empty string = positional argument, otherwise the parameter name used
	std::unique_ptr<ExprAST> value;
	int line;
	int column;

	CallArgument(std::unique_ptr<ExprAST> val, int l = 0, int c = 0, const std::string &n = "")
		: name(n), value(std::move(val)), line(l), column(c) {}

	// Check if this is a named argument
	bool isNamed() const { return !name.empty(); }
};

// Function call
class CallExprAST : public ExprAST {
  public:
	std::string callee;
	std::string resolvedCallee; // Resolved name (e.g., "map<int,int>.count" instead of just "count")
	std::vector<CallArgument> args;
	size_t functionId = SIZE_MAX;		 // Resolved during semantic analysis (SIZE_MAX = unresolved)
	bool isSiblingMethodCall = false;	 // True when calling another method of the same type from within a method
	bool isFunctionVariableCall = false; // True when calling via a function-typed variable (e.g., callback(x))

	// For named arguments: maps args[i] to funcInfo.parameters[argToParamMapping[i]]
	// This allows codegen to reorder arguments when named args are used out of order
	std::vector<size_t> argToParamMapping;

	// For enum case construction with associated values (e.g., Result.success(42))
	mutable std::string resolvedEnumName;	  // Set during semantic analysis if this is an enum case construction
	mutable std::string resolvedEnumCaseName; // The case name being constructed

	// Helper to check if this call is an enum case construction
	bool isEnumCaseConstruction() const { return !resolvedEnumName.empty(); }

	CallExprAST(const std::string &c, std::vector<CallArgument> a, int l = 0, int col = 0)
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
// Only supports value-initialized form: [1,2,3]
// Use SizedArrayExprAST for 'array of N T' syntax
class ArrayLiteralExprAST : public ExprAST {
  public:
	// For [val1, val2, ...] syntax
	std::vector<std::unique_ptr<ExprAST>> values; // Element values

	// Constructor for [val1, val2, ...] syntax
	ArrayLiteralExprAST(std::vector<std::unique_ptr<ExprAST>> vals, int l = 0, int c = 0)
		: ExprAST(l, c), values(std::move(vals)) {}
};

// Sized array creation expression (e.g., "array of 5 int", "array of int")
// Creates a zero-initialized array of the specified size and type
class SizedArrayExprAST : public ExprAST {
  public:
	int size;						   // Array size (-1 if using sizeExpr, 0 for empty array: array of T)
	std::unique_ptr<ExprAST> sizeExpr; // Expression for size (for variable-sized arrays)
	std::string elementType;		   // Element type (e.g., "int", "byte", "MyStruct")

	// Constructor for 'array of N T' syntax with constant size
	SizedArrayExprAST(int sz, const std::string &elemType, int l = 0, int c = 0)
		: ExprAST(l, c), size(sz), sizeExpr(nullptr), elementType(elemType) {}

	// Constructor for 'array of expr T' syntax with variable size
	SizedArrayExprAST(std::unique_ptr<ExprAST> szExpr, const std::string &elemType, int l = 0, int c = 0)
		: ExprAST(l, c), size(-1), sizeExpr(std::move(szExpr)), elementType(elemType) {}

	bool hasVariableSize() const { return sizeExpr != nullptr; }
};

// Dictionary type expression (e.g., "map from string to int", "HashMap from int to float")
// Creates an empty dictionary with the specified key and value types
// Works with any type that conforms to the Dictionary interface
class MapLiteralExprAST : public ExprAST {
  public:
	std::string dictType;  // Dictionary type name (e.g., "map", "HashMap", "OrderedMap")
	std::string keyType;   // Key type (must implement Hashable)
	std::string valueType; // Value type

	MapLiteralExprAST(const std::string &dType, const std::string &kType, const std::string &vType, int l = 0, int c = 0)
		: ExprAST(l, c), dictType(dType), keyType(kType), valueType(vType) {}
};

// Map literal with key-value pairs (e.g., ["apple": 1, "banana": 2])
// Creates a map initialized with the specified key-value entries
class MapLiteralWithEntriesExprAST : public ExprAST {
  public:
	// Key-value entry pair
	struct Entry {
		std::unique_ptr<ExprAST> key;
		std::unique_ptr<ExprAST> value;
	};

	std::vector<Entry> entries;			   // Key-value pairs
	mutable std::string inferredKeyType;   // Key type inferred from entries
	mutable std::string inferredValueType; // Value type inferred from entries

	MapLiteralWithEntriesExprAST(std::vector<Entry> ents, int l = 0, int c = 0)
		: ExprAST(l, c), entries(std::move(ents)) {}
};

// Set from array expression (e.g., "set from [1, 2, 3]")
// Creates a set initialized with elements from an array literal
class SetFromExprAST : public ExprAST {
  public:
	std::string setType;					 // Set type name (e.g., "set")
	std::unique_ptr<ExprAST> arrayExpr;		 // The array expression to initialize from
	mutable std::string inferredElementType; // Element type inferred from array literal

	SetFromExprAST(const std::string &sType, std::unique_ptr<ExprAST> arr, int l = 0, int c = 0)
		: ExprAST(l, c), setType(sType), arrayExpr(std::move(arr)) {}
};

// Member access expression (e.g., "array.length")
class MemberAccessExprAST : public ExprAST {
  public:
	std::unique_ptr<ExprAST> object; // Can be any expression (variable, array subscript, etc.)
	std::string objectName;			 // Keep for backward compatibility (when object is simple variable)
	std::string memberName;

	// Resolved enum info (set by semantic analyzer when this is an enum case expression)
	mutable std::string resolvedEnumName;	  // Non-empty if this is EnumName.caseName
	mutable std::string resolvedEnumCaseName; // The case name (e.g., "north" for Direction.north)

	// Constructor for simple variable.member access
	MemberAccessExprAST(const std::string &obj, const std::string &member, int l = 0, int c = 0)
		: ExprAST(l, c), object(nullptr), objectName(obj), memberName(member) {}

	// Constructor for complex expression.member access (e.g., arr[0].member)
	MemberAccessExprAST(std::unique_ptr<ExprAST> obj, const std::string &member, int l = 0, int c = 0)
		: ExprAST(l, c), object(std::move(obj)), objectName(""), memberName(member) {}

	// Check if this is a resolved enum case expression
	bool isEnumCase() const { return !resolvedEnumName.empty(); }
};

// Statement nodes
class StmtAST : public ASTNode {
  public:
	int line;
	int column;
	int endLine;   // End position line (0 = unset)
	int endColumn; // End position column (0 = unset)

	StmtAST(int l = 0, int c = 0) : line(l), column(c), endLine(0), endColumn(0) {}
	virtual ~StmtAST() = default;

	// Helper to get the full source range of this statement
	SourceRange getSourceRange() const {
		return SourceRange(line, column, endLine, endColumn);
	}

	// Helper to set end position
	void setEndPosition(int eLine, int eCol) {
		endLine = eLine;
		endColumn = eCol;
	}
};

// Variable declaration
class VarDeclStmtAST : public StmtAST {
  public:
	std::string name;
	std::string type; // "int", "ptr", "character", or "" for inferred
	std::unique_ptr<ExprAST> initializer;

	VarDeclStmtAST(const std::string &n, std::unique_ptr<ExprAST> init, const std::string &t = "", int l = 0, int c = 0)
		: StmtAST(l, c), name(n), type(t), initializer(std::move(init)) {}
};

// Let declaration (immutable variable)
class LetDeclStmtAST : public StmtAST {
  public:
	std::string name;
	std::string type; // "int", "ptr", "character", or "" for inferred
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
// New syntax: if condition 'thenBlockId' ... end 'thenBlockId' else 'elseBlockId' ... end 'elseBlockId'
class IfStmtAST : public StmtAST {
  public:
	std::unique_ptr<ExprAST> condition;
	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;
	std::string blockId;	 // Block identifier for the if/then branch
	std::string elseBlockId; // Block identifier for the else branch (empty if no else)

	IfStmtAST(std::unique_ptr<ExprAST> cond,
			  std::vector<std::unique_ptr<StmtAST>> thenB,
			  std::vector<std::unique_ptr<StmtAST>> elseB,
			  int l = 0, int c = 0,
			  const std::string &bid = "",
			  const std::string &elseBid = "")
		: StmtAST(l, c), condition(std::move(cond)),
		  thenBody(std::move(thenB)),
		  elseBody(std::move(elseB)),
		  blockId(bid), elseBlockId(elseBid) {}
};

// If-let statement (optional unwrapping)
// New syntax: if let name = optionalExpr 'thenBlockId' ... end 'thenBlockId' else 'elseBlockId' ... end 'elseBlockId'
class IfLetStmtAST : public StmtAST {
  public:
	std::string bindingName;
	std::unique_ptr<ExprAST> optionalExpr;
	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;
	std::string blockId;	 // Block identifier for the if-let/then branch
	std::string elseBlockId; // Block identifier for the else branch (empty if no else)

	IfLetStmtAST(const std::string &name,
				 std::unique_ptr<ExprAST> expr,
				 std::vector<std::unique_ptr<StmtAST>> thenB,
				 std::vector<std::unique_ptr<StmtAST>> elseB,
				 int l = 0, int c = 0,
				 const std::string &bid = "",
				 const std::string &elseBid = "")
		: StmtAST(l, c), bindingName(name), optionalExpr(std::move(expr)),
		  thenBody(std::move(thenB)), elseBody(std::move(elseB)),
		  blockId(bid), elseBlockId(elseBid) {}
};

// Else-unwrap statement (optional unwrapping with default value)
// Syntax: var name = optionalExpr else 'label' ... end 'label'
// The else block MUST assign a value to 'name' before exiting
class ElseUnwrapStmtAST : public StmtAST {
  public:
	std::string name;
	std::string declaredType; // Optional: explicit type annotation
	std::unique_ptr<ExprAST> optionalExpr;
	std::vector<std::unique_ptr<StmtAST>> elseBody;
	std::string blockId;

	ElseUnwrapStmtAST(const std::string &n,
					  const std::string &type,
					  std::unique_ptr<ExprAST> expr,
					  std::vector<std::unique_ptr<StmtAST>> elseB,
					  int l = 0, int c = 0,
					  const std::string &bid = "")
		: StmtAST(l, c), name(n), declaredType(type),
		  optionalExpr(std::move(expr)), elseBody(std::move(elseB)),
		  blockId(bid) {}
};

// Guard-let statement (optional unwrapping with early exit)
// Syntax: let name = optionalExpr or 'label' ... end 'label'
// The guard body MUST exit scope (return, break, continue)
// If the optional has a value, it is unwrapped and bound to 'name'
class GuardLetStmtAST : public StmtAST {
  public:
	std::string name;
	std::unique_ptr<ExprAST> optionalExpr;
	std::vector<std::unique_ptr<StmtAST>> guardBody;
	std::string blockId;

	GuardLetStmtAST(const std::string &n,
					std::unique_ptr<ExprAST> expr,
					std::vector<std::unique_ptr<StmtAST>> guardB,
					int l = 0, int c = 0,
					const std::string &bid = "")
		: StmtAST(l, c), name(n),
		  optionalExpr(std::move(expr)), guardBody(std::move(guardB)),
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

// Error statement - placeholder for parse errors in partial AST
// Used by error recovery to mark locations where parsing failed
class ErrorStmtAST : public StmtAST {
  public:
	std::string message; // Error description

	ErrorStmtAST(const std::string &msg, int l, int c, int endL, int endC)
		: StmtAST(l, c), message(msg) {
		setEndPosition(endL, endC);
	}
};

// Function parameter (defined early for use by InterfaceMethodSignature)
// named arguments: name type [= default]
// - name: Parameter name (used both internally and at call site for named arguments)
// - defaultValue: Optional default value expression (nullptr if required parameter)
// Parameters with defaults can ONLY be provided via named arguments at call site
struct FunctionParameter {
	std::string name;
	std::string type;
	std::shared_ptr<ExprAST> defaultValue; // nullptr if no default (required parameter)
	int line;
	int column;

	FunctionParameter(const std::string &n, const std::string &t, int l = 0, int c = 0,
					  std::shared_ptr<ExprAST> defVal = nullptr)
		: name(n), type(t), defaultValue(std::move(defVal)), line(l), column(c) {}

	// Check if this parameter has a default value
	bool hasDefault() const { return defaultValue != nullptr; }
};

// Interface method signature (for interface definitions)
struct InterfaceMethodSignature {
	std::string name;
	std::vector<FunctionParameter> parameters; // First param is 'self' with type 'Self'
	std::string returnType;
	bool hasDefaultImplementation = false;			   // True if this method has a default impl
	std::vector<std::unique_ptr<StmtAST>> defaultBody; // Body of default impl (empty if none)
	int line;
	int column;

	InterfaceMethodSignature(const std::string &n, std::vector<FunctionParameter> params,
							 const std::string &ret, int l = 0, int c = 0,
							 bool hasDefault = false,
							 std::vector<std::unique_ptr<StmtAST>> defBody = {})
		: name(n), parameters(std::move(params)), returnType(ret),
		  hasDefaultImplementation(hasDefault), defaultBody(std::move(defBody)),
		  line(l), column(c) {}
};

// Interface definition
class InterfaceDefAST : public ASTNode {
  public:
	std::string name;
	std::string namespaceName;	  // Namespace this interface belongs to
	std::string extendsInterface; // Base interface this extends (empty if none)
	std::vector<InterfaceMethodSignature> methods;
	std::vector<std::string> associatedTypes; // Associated type declarations (e.g., "Element")
	bool isExported;
	int line;
	int column;
	int endLine;   // End position line (0 = unset)
	int endColumn; // End position column (0 = unset)

	InterfaceDefAST(const std::string &n, std::vector<InterfaceMethodSignature> m,
					int l = 0, int c = 0, const std::string &ns = "", bool exp = false,
					std::vector<std::string> assocTypes = {}, const std::string &extends = "")
		: name(n), namespaceName(ns), extendsInterface(extends), methods(std::move(m)),
		  associatedTypes(std::move(assocTypes)), isExported(exp), line(l), column(c),
		  endLine(0), endColumn(0) {}

	// Helper to get the full source range of this interface
	SourceRange getSourceRange() const {
		return SourceRange(line, column, endLine, endColumn);
	}

	// Helper to set end position
	void setEndPosition(int eLine, int eCol) {
		endLine = eLine;
		endColumn = eCol;
	}
};

// Struct field definition
struct StructField {
	std::string name;
	std::string type;					   // Can be "" if type is inferred from defaultValue
	bool isImmutable;					   // true for 'let', false for 'var'
	std::unique_ptr<ExprAST> defaultValue; // Optional default value expression
	int line;
	int column;

	StructField(const std::string &n, const std::string &t, bool immutable,
				std::unique_ptr<ExprAST> defVal = nullptr, int l = 0, int c = 0)
		: name(n), type(t), isImmutable(immutable), defaultValue(std::move(defVal)), line(l), column(c) {}
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
	std::vector<std::unique_ptr<FunctionAST>> methods;					   // Methods declared inside the struct
	std::vector<std::string> associatedTypeParams;						   // Associated type parameter names from 'uses' clause
	std::vector<std::string> conformsTo;								   // Interface names this struct conforms to (via 'is')
	std::map<std::string, std::string> typeAssignments;					   // Associated type assignments (e.g., "Element" -> "character")
	std::map<std::string, std::vector<std::string>> interfaceTypeBindings; // Per-interface 'with' types (resolved to typeAssignments in semantic analyzer)
	bool isExported;													   // true if this struct is exported (visible outside this file)
	int line;
	int column;
	int endLine;   // End position line (0 = unset)
	int endColumn; // End position column (0 = unset)

	StructDefAST(const std::string &n, std::vector<StructField> f, int l = 0, int c = 0,
				 const std::string &ns = "", bool exp = false,
				 std::vector<std::string> interfaces = {},
				 std::vector<std::unique_ptr<FunctionAST>> m = {},
				 std::map<std::string, std::string> typeAssigns = {},
				 std::map<std::string, std::vector<std::string>> ifaceBindings = {},
				 std::vector<std::string> assocTypeParams = {})
		: name(n), namespaceName(ns), fields(std::move(f)), methods(std::move(m)),
		  associatedTypeParams(std::move(assocTypeParams)),
		  conformsTo(std::move(interfaces)), typeAssignments(std::move(typeAssigns)),
		  interfaceTypeBindings(std::move(ifaceBindings)),
		  isExported(exp), line(l), column(c), endLine(0), endColumn(0) {}

	// Helper to get the full source range of this struct
	SourceRange getSourceRange() const {
		return SourceRange(line, column, endLine, endColumn);
	}

	// Helper to set end position
	void setEndPosition(int eLine, int eCol) {
		endLine = eLine;
		endColumn = eCol;
	}
};

// Struct initialization expression (struct literal)
class StructInitExprAST : public ExprAST {
  public:
	std::string structName;
	std::vector<StructInitField> fields;

	StructInitExprAST(const std::string &name, std::vector<StructInitField> f, int l = 0, int c = 0)
		: ExprAST(l, c), structName(name), fields(std::move(f)) {}
};

// Enum associated value field (for associated value enums)
struct EnumAssocValue {
	std::string name;
	std::string type;
	int line;
	int column;

	EnumAssocValue(const std::string &n, const std::string &t, int l = 0, int c = 0)
		: name(n), type(t), line(l), column(c) {}
};

// Enum case definition
struct EnumCaseAST {
	std::string name;
	std::vector<EnumAssocValue> associatedValues; // Empty for simple cases
	std::unique_ptr<ExprAST> rawValue;			  // For raw value enums (optional)
	int line;
	int column;

	EnumCaseAST(const std::string &n, int l = 0, int c = 0,
				std::vector<EnumAssocValue> assoc = {},
				std::unique_ptr<ExprAST> raw = nullptr)
		: name(n), associatedValues(std::move(assoc)), rawValue(std::move(raw)), line(l), column(c) {}
};

// Enum definition
class EnumDefAST : public ASTNode {
  public:
	std::string name;
	std::string namespaceName;						   // Namespace this enum belongs to
	std::string rawValueType;						   // "int" or "string", empty if none
	std::vector<EnumCaseAST> cases;					   // Enum cases
	std::vector<std::unique_ptr<FunctionAST>> methods; // Methods declared inside the enum
	bool isExported;
	int line;
	int column;
	int endLine;   // End position line (0 = unset)
	int endColumn; // End position column (0 = unset)

	EnumDefAST(const std::string &n, std::vector<EnumCaseAST> c, int l = 0, int col = 0,
			   const std::string &ns = "", bool exp = false,
			   const std::string &rawType = "",
			   std::vector<std::unique_ptr<FunctionAST>> m = {})
		: name(n), namespaceName(ns), rawValueType(rawType), cases(std::move(c)),
		  methods(std::move(m)), isExported(exp), line(l), column(col), endLine(0), endColumn(0) {}

	// Check if this enum has associated values (any case has them)
	bool hasAssociatedValues() const {
		for (const auto &c : cases) {
			if (!c.associatedValues.empty())
				return true;
		}
		return false;
	}

	// Check if this is a raw value enum
	bool hasRawValueType() const { return !rawValueType.empty(); }

	// Helper to get the full source range of this enum
	SourceRange getSourceRange() const {
		return SourceRange(line, column, endLine, endColumn);
	}

	// Helper to set end position
	void setEndPosition(int eLine, int eCol) {
		endLine = eLine;
		endColumn = eCol;
	}
};

// Enum case reference expression (e.g., Direction.north or Result.success(42))
class EnumCaseExprAST : public ExprAST {
  public:
	std::string enumName;
	std::string caseName;
	std::vector<std::unique_ptr<ExprAST>> arguments; // For associated values

	EnumCaseExprAST(const std::string &eName, const std::string &cName, int l = 0, int c = 0,
					std::vector<std::unique_ptr<ExprAST>> args = {})
		: ExprAST(l, c), enumName(eName), caseName(cName), arguments(std::move(args)) {}
};

// Match case for match statement/expression
// Each case contains one or more patterns (via 'or'), and either:
// - A single statement (for match statements) with optional fallthrough
// - A single expression (for match expressions) that returns a value
// For enum case patterns with bindings: case success(value) then ...
struct MatchCaseAST {
	std::vector<std::unique_ptr<ExprAST>> patterns; // One or more patterns (via 'or')
	std::unique_ptr<StmtAST> statement;				// Single statement (for match statement, null for expression)
	std::unique_ptr<ExprAST> resultExpr;			// Result expression (for match expression, null for statement)
	bool isDefault;									// true for 'default' case
	bool hasFallthrough;							// true if case ends with 'and fallthrough'
	int line;
	int column;

	// Enum case pattern fields (for pattern matching with value extraction)
	bool isEnumCasePattern = false;	   // true if this is 'case X(bindings)' pattern
	std::string enumCaseName;		   // Case name (e.g., "success")
	std::vector<std::string> bindings; // Variable names to bind associated values

	// Constructor for regular patterns
	MatchCaseAST(std::vector<std::unique_ptr<ExprAST>> pats,
				 std::unique_ptr<StmtAST> stmt,
				 std::unique_ptr<ExprAST> expr,
				 bool isDef, bool fallthrough,
				 int l = 0, int c = 0)
		: patterns(std::move(pats)), statement(std::move(stmt)),
		  resultExpr(std::move(expr)), isDefault(isDef),
		  hasFallthrough(fallthrough), line(l), column(c),
		  isEnumCasePattern(false) {}

	// Constructor for enum case patterns with bindings
	MatchCaseAST(const std::string &caseName,
				 std::vector<std::string> bindingNames,
				 std::unique_ptr<StmtAST> stmt,
				 std::unique_ptr<ExprAST> expr,
				 bool fallthrough,
				 int l = 0, int c = 0)
		: patterns(), statement(std::move(stmt)),
		  resultExpr(std::move(expr)), isDefault(false),
		  hasFallthrough(fallthrough), line(l), column(c),
		  isEnumCasePattern(true), enumCaseName(caseName),
		  bindings(std::move(bindingNames)) {}
};

// Match statement
// Syntax: match expr 'label' ... end 'label'
// Each case: pattern then statement [and fallthrough]
// Or: default then statement
class MatchStmtAST : public StmtAST {
  public:
	std::unique_ptr<ExprAST> scrutinee; // The expression being matched
	std::vector<MatchCaseAST> cases;	// Match cases
	std::string blockId;				// Block identifier
	bool isExhaustive = false;			// Set by semantic analyzer for exhaustive enum matches

	MatchStmtAST(std::unique_ptr<ExprAST> scrut,
				 std::vector<MatchCaseAST> cs,
				 int l = 0, int c = 0,
				 const std::string &bid = "")
		: StmtAST(l, c), scrutinee(std::move(scrut)),
		  cases(std::move(cs)), blockId(bid) {}
};

// Match expression
// Syntax: match expr 'label' ... end 'label'
// Each case: pattern gives expr
// Or: default gives expr
// Used as an expression (returns a value)
class MatchExprAST : public ExprAST {
  public:
	std::unique_ptr<ExprAST> scrutinee; // The expression being matched
	std::vector<MatchCaseAST> cases;	// Match cases
	std::string blockId;				// Block identifier

	MatchExprAST(std::unique_ptr<ExprAST> scrut,
				 std::vector<MatchCaseAST> cs,
				 int l = 0, int c = 0,
				 const std::string &bid = "")
		: ExprAST(l, c), scrutinee(std::move(scrut)),
		  cases(std::move(cs)), blockId(bid) {}
};

// Closure/lambda expression
// Supports both single-expression closures (e.g., x gives x * 2) and
// multi-statement closures with block labels (e.g., x 'label' ... end 'label')
class ClosureExprAST : public ExprAST {
  public:
	std::vector<FunctionParameter> parameters;	// Closure parameters
	std::string returnType;						// Return type (may be empty if inferred)
	std::vector<std::unique_ptr<StmtAST>> body; // Statements in closure body (for multi-statement closures)
	std::unique_ptr<ExprAST> singleExpr;		// For single-expression closures like x gives x * 2
	bool isSingleExpression;					// True if closure is just an expression, false if has statements
	std::string blockId;						// Block identifier for multi-statement closures

	ClosureExprAST(std::vector<FunctionParameter> params,
				   std::string retType,
				   std::vector<std::unique_ptr<StmtAST>> bodyStmts,
				   std::unique_ptr<ExprAST> expr,
				   bool singleExpr,
				   int line, int col,
				   std::string bid = "")
		: ExprAST(line, col), parameters(std::move(params)),
		  returnType(std::move(retType)), body(std::move(bodyStmts)),
		  singleExpr(std::move(expr)), isSingleExpression(singleExpr),
		  blockId(std::move(bid)) {}
};

// If-case statement for enum pattern matching
// Syntax: if case caseName(binding1, binding2) = expr 'label' ... end 'label'
class IfCaseStmtAST : public StmtAST {
  public:
	std::string caseName;			   // The case to match (e.g., "success")
	std::vector<std::string> bindings; // Variable names to bind associated values
	std::unique_ptr<ExprAST> enumExpr; // The enum expression being matched
	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;
	std::string blockId;

	IfCaseStmtAST(const std::string &cname, std::vector<std::string> binds,
				  std::unique_ptr<ExprAST> expr,
				  std::vector<std::unique_ptr<StmtAST>> thenB,
				  std::vector<std::unique_ptr<StmtAST>> elseB,
				  int l = 0, int c = 0, const std::string &bid = "")
		: StmtAST(l, c), caseName(cname), bindings(std::move(binds)),
		  enumExpr(std::move(expr)), thenBody(std::move(thenB)),
		  elseBody(std::move(elseB)), blockId(bid) {}
};

// Function declaration
class FunctionAST : public ASTNode {
  public:
	std::string name;
	std::string namespaceName;		 // Namespace this function belongs to (derived from file path)
	std::string receiverType;		 // For methods: the type this method belongs to (e.g., "Point")
	std::string implementsInterface; // For interface methods: the interface this method implements (e.g., "Hashable")
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
	int endLine;   // End position line (0 = unset)
	int endColumn; // End position column (0 = unset)

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
				const std::string &receiver = "",
				const std::string &implInterface = "")
		: name(n), namespaceName(ns), receiverType(receiver), implementsInterface(implInterface), parameters(std::move(params)), returnType(ret), body(std::move(b)),
		  isExtern(ext), isExported(exp), dllName(dll), isStaticLib(staticLib), libPath(libFilePath), line(l), column(c), endLine(0), endColumn(0) {}

	// Helper to get the full source range of this function
	SourceRange getSourceRange() const {
		return SourceRange(line, column, endLine, endColumn);
	}

	// Helper to set end position
	void setEndPosition(int eLine, int eCol) {
		endLine = eLine;
		endColumn = eCol;
	}
};

// Global let declaration (compile-time constant)
class GlobalLetDeclAST : public ASTNode {
  public:
	std::string name;
	std::string type; // "int", "float", "bool", "string", or "" for inferred
	std::unique_ptr<ExprAST> initializer;
	bool isExported;
	int line;
	int column;

	GlobalLetDeclAST(const std::string &n, std::unique_ptr<ExprAST> init, const std::string &t = "",
					 bool exported = false, int l = 0, int c = 0)
		: name(n), type(t), initializer(std::move(init)), isExported(exported), line(l), column(c) {}
};

// Parse error information for error recovery (stored in ProgramAST)
struct ASTParseError {
	std::string message;
	int line;
	int column;

	ASTParseError(const std::string &msg, int l, int c)
		: message(msg), line(l), column(c) {}
};

// Program (collection of functions, structs, enums, interfaces, and global constants)
class ProgramAST : public ASTNode {
  public:
	std::vector<std::unique_ptr<FunctionAST>> functions;
	std::vector<std::unique_ptr<StructDefAST>> structs;
	std::vector<std::unique_ptr<EnumDefAST>> enums;
	std::vector<std::unique_ptr<InterfaceDefAST>> interfaces;
	std::vector<std::unique_ptr<GlobalLetDeclAST>> globals; // Top-level let constants
	std::vector<ASTParseError> parseErrors;					// Errors encountered during parsing

	ProgramAST() = default;

	ProgramAST(std::vector<std::unique_ptr<FunctionAST>> funcs,
			   std::vector<std::unique_ptr<StructDefAST>> st = {},
			   std::vector<std::unique_ptr<InterfaceDefAST>> protos = {},
			   std::vector<std::unique_ptr<EnumDefAST>> en = {},
			   std::vector<std::unique_ptr<GlobalLetDeclAST>> globs = {})
		: functions(std::move(funcs)), structs(std::move(st)), enums(std::move(en)), interfaces(std::move(protos)), globals(std::move(globs)) {}

	// Check if parsing encountered errors
	bool hasParseErrors() const { return !parseErrors.empty(); }
};

#endif // AST_H
