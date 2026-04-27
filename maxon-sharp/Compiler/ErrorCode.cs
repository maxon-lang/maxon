namespace MaxonSharp.Compiler;

/// <summary>
/// Structured error codes for the compiler.
/// Format: E followed by 4 digits, grouped by compilation stage.
/// - 1xxx: Lexer errors (Stage 1)
/// - 2xxx: Parser errors (Stage 2)
/// - 3xxx: Semantic analysis errors (Stage 3)
/// - 4xxx: IR pipeline errors (Stage 4)
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
  LexerUnescapedBrace = 1006,

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
  ParserMatchDefaultWithEnum = 2044,
  ParserNonConstantInitializer = 2045,
  ParserMatchDefaultEnumMustThrow = 2046,
  ParserRedundantLoopLabel = 2048,
  ParserMatchBlockStatement = 2049,
  ParserOtherwiseBlockMissingBinding = 2050,
  ParserReservedIdentifier = 2051,

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
  SemanticUnknownType = 3011,
  SemanticUnusedVariable = 3012,
  SemanticMissingReturn = 3013,
  SemanticUnexportedFieldAccess = 3014,
  SemanticPartialInterfaceImpl = 3016,
  SemanticWhereConstraintViolation = 3017,
  SemanticUnknownField = 3018,
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
  SemanticAmbiguousTypeReference = 3060,
  SemanticDuplicateTypeAlias = 3061,
  SemanticUnusedTypeAlias = 3062,
  SemanticImmutableRefToMutatingParam = 3063,
  SemanticDiscardedPureResult = 3064,
  SemanticDiscardedImpureResult = 3065,
  SemanticEnumNotComparable = 3066,
  SemanticSelfAssignment = 3067,
  SemanticRefIdentityOnPrimitive = 3068,
  SemanticEqRequiresEquatable = 3069,
  SemanticBorrowConflict = 3070,
  SemanticUnreachableCode = 3071,
  SemanticBuiltinTypeConstruction = 3072,
  AsyncNonYielding = 3073,
  // 3074 retired (was SemanticDiscardedEnumeratedIndex — check removed with withIterator redesign)
  SemanticMatchQualifiedCaseName = 3075,
  SemanticConstructorRestriction = 3076,
  SemanticVarShouldBeLet = 3077,
  SemanticVarFromImmutable = 3078,
  SemanticEnumCannotHaveAssociatedValues = 3079,
  SemanticUnionCannotHaveRawValues = 3080,
  SemanticMatchDiscardedBindings = 3081,
  SemanticEmptyBlock = 3082,
  SemanticTryBlockNoThrows = 3083,
  SemanticTryBlockBindingNotMatched = 3084,
  SemanticUnionMatchPatternAmbiguous = 3085,
  SemanticFieldNotInitialized = 3086,
  SemanticRedundantContainsGet = 3087,

  // IR pipeline errors (4xxx) - Stage 4
  IrUnsupportedExpression = 4001,
  IrUnsupportedStatement = 4002,
  IrUndefinedType = 4003,
  IrUndefinedVariable = 4004,
  IrUndefinedFunction = 4005,
  IrInvalidFieldAccess = 4006,
  IrInvalidMethodCall = 4007,
  IrUnsupportedInstruction = 4008,
  IrTypeCycle = 4014,

  // Code emitter errors (5xxx) - Stage 5
  CodeEmitterNoMain = 5001,
  CodeEmitterUnsupportedInstruction = 5002,

  // PE writer errors (6xxx) - Stage 6
  PeWriteError = 6001,

  // Internal errors (9xxx)
  InternalError = 9001
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
