namespace MaxonSharp;

/// <summary>
/// Structured error codes for the compiler.
/// Format: E followed by 3 digits, grouped by phase.
/// </summary>
public enum ErrorCode {
	// General errors (E000-E009)
	Unknown = 0,

	// Lexer errors (E010-E019)
	LexerUnexpectedCharacter = 10,
	LexerUnterminatedString = 11,
	LexerUnterminatedChar = 12,
	LexerInvalidEscape = 13,
	LexerInvalidNumber = 14,

	// Parser errors (E020-E039)
	ParserUnexpectedToken = 20,
	ParserExpectedIdentifier = 21,
	ParserExpectedType = 22,
	ParserExpectedExpression = 23,
	ParserExpectedStatement = 24,
	ParserExpectedEnd = 25,
	ParserUnexpectedEof = 26,
	ParserMismatchedEndLabel = 27,
	ParserInvalidAssignment = 28,
	ParserExpectedToken = 29,

	// Semantic errors (E040-E059)
	SemanticNoMain = 40,
	SemanticMainWrongReturnType = 41,
	SemanticUndefinedVariable = 42,
	SemanticUndefinedFunction = 43,
	SemanticTypeMismatch = 44,
	SemanticDuplicateDefinition = 45,

	// HIR lowering errors (E060-E079)
	HirUnsupportedExpression = 60,
	HirUnsupportedStatement = 61,
	HirUndefinedType = 62,
	HirUndefinedVariable = 63,
	HirUndefinedFunction = 64,
	HirInvalidFieldAccess = 65,
	HirInvalidMethodCall = 66,

	// LIR lowering errors (E080-E089)
	LirUnsupportedInstruction = 80,

	// Codegen errors (E090-E099)
	CodegenNoMain = 90,
	CodegenUnsupportedInstruction = 91,

	// PE writer errors (E100-E109)
	PeWriteError = 100
}

/// <summary>
/// Extension methods for ErrorCode.
/// </summary>
public static class ErrorCodeExtensions {
	/// <summary>
	/// Formats an error code as "E001", "E020", etc.
	/// </summary>
	public static string Format(this ErrorCode code) {
		return $"E{(int)code:D3}";
	}
}
