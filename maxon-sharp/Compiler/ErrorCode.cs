namespace MaxonSharp.Compiler;

/// <summary>
/// Structured error codes for the compiler.
/// Format: E followed by 4 digits, grouped by compilation stage.
/// - 1xxx: Lexer errors (Stage 1)
/// - 2xxx: Parser errors (Stage 2)
/// - 3xxx: Semantic analysis errors (Stage 3)
/// - 4xxx: MLIR pipeline errors (Stage 4)
/// - 5xxx: Code emitter errors (Stage 5)
/// - 6xxx: PE writer errors (Stage 6)
/// </summary>
public enum ErrorCode {
  // General errors (0xxx)
  Unknown = 0,

  // Lexer errors (1xxx) - Stage 1
  LexerUnexpectedCharacter = 1001,
  LexerUnterminatedString = 1002,
  LexerUnterminatedChar = 1003,
  LexerInvalidEscape = 1004,
  LexerInvalidNumber = 1005,

  // Parser errors (2xxx) - Stage 2
  ParserUnexpectedToken = 2001,
  ParserExpectedIdentifier = 2002,
  ParserExpectedType = 2003,
  ParserExpectedExpression = 2004,
  ParserExpectedStatement = 2005,
  ParserExpectedEnd = 2006,
  ParserUnexpectedEof = 2007,
  ParserMismatchedEndLabel = 2008,
  ParserInvalidAssignment = 2009,
  ParserExpectedToken = 2010,
  ParserLiteralOverflow = 2011,

  // Semantic errors (3xxx) - Stage 3
  SemanticNoMain = 3001,
  SemanticMainWrongReturnType = 3002,
  SemanticUndefinedVariable = 3003,
  SemanticUndefinedFunction = 3004,
  SemanticTypeMismatch = 3005,
  SemanticDuplicateDefinition = 3006,
  SemanticSymbolNotExported = 3007,
  SemanticUnsafeCast = 3008,

  // MLIR pipeline errors (4xxx) - Stage 4
  MlirUnsupportedExpression = 4001,
  MlirUnsupportedStatement = 4002,
  MlirUndefinedType = 4003,
  MlirUndefinedVariable = 4004,
  MlirUndefinedFunction = 4005,
  MlirInvalidFieldAccess = 4006,
  MlirInvalidMethodCall = 4007,
  MlirUnsupportedInstruction = 4008,
  ImmutableVariable = 4009,
  // Ownership errors (checked in MLIR borrow checker pass)
  OwnershipUseAfterMove = 4010,
  OwnershipMoveFromImmutable = 4011,
  OwnershipBranchConflict = 4012,
  OwnershipMoveInLoop = 4013,

  // Code emitter errors (5xxx) - Stage 5
  CodeEmitterNoMain = 5001,
  CodeEmitterUnsupportedInstruction = 5002,

  // PE writer errors (6xxx) - Stage 6
  PeWriteError = 6001
}

/// <summary>
/// Extension methods for ErrorCode.
/// </summary>
public static class ErrorCodeExtensions {
  /// <summary>
  /// Formats an error code as "E1001", "E2001", etc.
  /// </summary>
  public static string Format(this ErrorCode code) {
    return $"E{(int)code:D4}";
  }
}
