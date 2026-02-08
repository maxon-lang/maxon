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
  ParserCircularDependency = 2012,
  ParserImmutableVariable = 2013,
  ParserMatchFallthroughWithReturn = 2025,
  ParserMatchNotExhaustive = 2026,
  ParserMatchDuplicatePattern = 2027,
  ParserMatchTypeMismatch = 2028,
  ParserMatchDefaultNotLast = 2029,
  ParserMatchMissingBlockId = 2042,
  ParserMatchMismatchedBlockId = 2043,

  // Semantic errors (3xxx) - Stage 3
  SemanticNoMain = 3001,
  SemanticMainWrongReturnType = 3002,
  SemanticUndefinedVariable = 3003,
  SemanticUndefinedFunction = 3004,
  SemanticTypeMismatch = 3005,
  SemanticDuplicateDefinition = 3006,
  SemanticAmbiguousFunctionCall = 3007,
  SemanticSymbolNotExported = 3008,
  SemanticUnsafeCast = 3009,
  SemanticUnexpectedToken = 3010,
  SemanticUnknownType = 3011,
  SemanticUnusedVariable = 3012,
  SemanticMissingReturn = 3013,
  SemanticUnexportedFieldAccess = 3014,
  SemanticRedundantTypeAnnotation = 3015,
  SemanticPartialInterfaceImpl = 3016,
  SemanticEnumDuplicateCase = 3030,
  SemanticEnumDuplicateRawValue = 3031,
  SemanticEnumRawValueTypeMismatch = 3032,
  SemanticEnumUnknownCase = 3034,
  SemanticEnumWrongBindingCount = 3035,
  SemanticWrongArgCount = 3036,
  SemanticMainCannotThrow = 3054,
  SemanticTryRequiresThrowingFunction = 3055,
  SemanticThrowingFunctionRequiresTry = 3057,
  SemanticOtherwiseRequiresTry = 3058,
  SemanticErrorTypeMismatch = 3059,

  // MLIR pipeline errors (4xxx) - Stage 4
  MlirUnsupportedExpression = 4001,
  MlirUnsupportedStatement = 4002,
  MlirUndefinedType = 4003,
  MlirUndefinedVariable = 4004,
  MlirUndefinedFunction = 4005,
  MlirInvalidFieldAccess = 4006,
  MlirInvalidMethodCall = 4007,
  MlirUnsupportedInstruction = 4008,
  // Ownership errors (checked in MLIR borrow checker pass)
  MlirOwnershipUseAfterMove = 4010,
  MlirOwnershipMoveFromImmutable = 4011,
  MlirOwnershipBranchConflict = 4012,
  MlirOwnershipMoveInLoop = 4013,

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
