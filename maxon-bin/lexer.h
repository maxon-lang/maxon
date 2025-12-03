#ifndef LEXER_H
#define LEXER_H

/**
 * Maxon Lexer
 *
 * Provides tokenization of Maxon source code using SIMD-optimized algorithms.
 * Includes token types, keyword metadata, and the Lexer class.
 */

#include <functional>
#include <optional>
#include <string>
#include <vector>

// ============================================================================
// Token Types
// ============================================================================

enum class TokenType {
	// Keywords (all keywords now use KEYWORD)
	KEYWORD,

	// Identifiers and literals
	IDENTIFIER,
	NUMBER,
	BYTE_LITERAL,  // Byte literal with 'b' suffix (e.g., 42b)
	FLOAT_LITERAL, // Floating-point literal
	STRING,		   // Double-quoted string literals
	BLOCK_ID,	   // Single-quoted block identifiers
	CHARACTER,	   // Single character literal 'A'

	// Operators
	ASSIGN,		 // = (assignment)
	EQUAL_EQUAL, // == (equality comparison)
	PLUS,		 // +
	MINUS,		 // -
	MULTIPLY,	 // *
	DIVIDE,		 // /
	MODULO,		 // % (modulo/remainder)
	NOT_EQUAL,	 // != (not equal)
	GT,			 // >
	LT,			 // <
	GTE,		 // >=
	LTE,		 // <=
	AMPERSAND,	 // & (bitwise AND)
	PIPE,		 // | (bitwise OR)
	CARET,		 // ^ (bitwise XOR)
	LSHIFT,		 // << (left shift)
	RSHIFT,		 // >> (right shift)

	// Delimiters
	LPAREN,	  // (
	RPAREN,	  // )
	LBRACKET, // [
	RBRACKET, // ]
	LBRACE,	  // {
	RBRACE,	  // }
	COMMA,	  // ,
	COLON,	  // :
	DOT,	  // . (member access / namespace resolution)
	DOT_DOT,  // .. (range operator for slicing)

	// Special
	END_OF_FILE,
	UNKNOWN
};

// ============================================================================
// Keyword Metadata
// ============================================================================

enum class KeywordCategory {
	Type,		   // int, float, ptr, char, string
	ControlFlow,   // if, else, while, end, return, break, continue
	Declaration,   // function, var, let, struct, namespace, extern
	MathIntrinsic, // sqrt, abs, floor, ceil, round, trunc, sin, cos
	Literal,	   // true, false
	Operator	   // as
};

enum class MathIntrinsicKind {
	Intrinsic,		 // Built-in (sqrt, abs, floor, ceil, round)
	RuntimeFunction, // Call runtime library function (sin, cos)
	DirectCast		 // Direct IR operation (trunc)
};

struct MathIntrinsicInfo {
	MathIntrinsicKind kind;
	std::string runtimeFunctionName; // For RuntimeFunction kind
	std::string returnType;			 // "int" or "float"
};

struct KeywordData {
	KeywordCategory category;
	std::string description;
	std::optional<MathIntrinsicInfo> mathInfo; // Only set for MathIntrinsic category
};

// ============================================================================
// Token
// ============================================================================

struct Token {
	TokenType type;
	std::string value;
	int line;
	int column;
	std::optional<KeywordData> keywordData;

	Token(TokenType t, const std::string &v, int l, int c)
		: type(t), value(v), line(l), column(c) {}
};

// ============================================================================
// Lexer Class
// ============================================================================

// Forward declarations for internal types
class CharClassifier;
class KeywordMatcher;
class TokenStream;

/**
 * Lexer - Tokenizes Maxon source code
 *
 * Uses SIMD-optimized algorithms for fast lexing.
 */
class Lexer {
  public:
	/**
	 * Construct lexer with source code
	 */
	explicit Lexer(const std::string &source);

	/**
	 * Construct lexer with source code (C string + length)
	 */
	Lexer(const char *source, size_t length);

	/**
	 * Tokenize the source code
	 * Returns a vector of tokens for the parser
	 */
	std::vector<Token> tokenize();

	/**
	 * Tokenize to optimized token stream
	 * More efficient for the parser
	 */
	TokenStream tokenize_stream();

	// ========================================================================
	// Static keyword query methods (for IDE/LSP features)
	// ========================================================================

	struct KeywordInfo {
		std::string name;
		KeywordCategory category;
		std::string description;
	};

	// Get all keyword strings
	static std::vector<std::string> getKeywords();

	// Get keywords with metadata
	static std::vector<KeywordInfo> getKeywordInfo();

	// Get keywords by category
	static std::vector<std::string> getKeywordsByCategory(KeywordCategory category);

	// Check if a name is a math intrinsic
	static bool isMathIntrinsic(const std::string &name);

	// Get math intrinsic info (returns nullptr if not a math intrinsic)
	static const MathIntrinsicInfo *getMathIntrinsicInfo(const std::string &name);

	// Check if a string is a valid type keyword
	static bool isTypeString(const std::string &name);

	// Check if a token is a keyword of specific category
	static bool isControlFlowToken(const Token &token);
	static bool isDeclarationToken(const Token &token);
	static bool isTypeToken(const Token &token);
	static bool isLiteralToken(const Token &token);
	static bool isMathIntrinsicToken(const Token &token);

  private:
	const char *source_;
	size_t length_;
	size_t position_;
	int line_;
	int column_;

	char currentChar() const;
	char peek(int offset = 1) const;
	void advance();
	void advanceBy(size_t n);
	void skipWhitespace();
	void updateLineColumn(size_t start, size_t end);
	void skipLineComment();
	void skipBlockComment();

	Token readNextToken();
	Token readNumber();
	Token readIdentifier();
	Token readString();
	Token readStringLiteral();
	Token readOperatorOrDelimiter(int startLine, int startColumn);

	// Error reporting helper - throws runtime_error with location info
	[[noreturn]] void reportError(const std::string &message);
	[[noreturn]] void reportError(const std::string &message, int line, int column);
};

// ============================================================================
// Utility Functions
// ============================================================================

/**
 * Convenience function to tokenize a source string
 */
inline std::vector<Token> tokenize(const std::string &source) {
	Lexer lexer(source);
	return lexer.tokenize();
}

/**
 * Get SIMD capability string for diagnostics
 */
const char *get_lexer_capability();

/**
 * Print CPU features (for debugging)
 */
void print_cpu_features();

/**
 * Run lexer benchmark
 */
void run_lexer_benchmark(const std::string &source, const std::string &filename, int iterations);

/**
 * Run full pipeline benchmark (lexer + parser)
 */
void run_pipeline_benchmark(const std::string &source, const std::string &filename, int iterations);

#endif // LEXER_H
