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
  LexerUnterminatedBlockComment = 1007,

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
  ParserFirstArgCannotBeNamed = 2052,

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
  SemanticUnneededCast = 3010,
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
  // E3063: a bare-name type reference has more than one reachable
  // typealias definition under directory-as-module rules. The user must
  // write the directory-qualified form (`dir.Name`) to disambiguate.
  // Mirrors E3095 (functions) but operates on the typealias registry.
  SemanticAmbiguousTypeAlias = 3063,
  // E3019: cannot pass an immutable 'let' variable to a function that mutates
  // the corresponding parameter. Re-coded from 3063 when E3063 was reassigned
  // to SemanticAmbiguousTypeAlias.
  SemanticImmutableRefToMutatingParam = 3019,
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
  // 3074 reserved for SemanticSubprocessUnsupportedTarget — a self-hosted-only
  // diagnostic (the self-hosted RejectWasmSubprocess pass rejects the Subprocess
  // API on wasm32-wasi). The C# bootstrap has no such check, so it owns no enum
  // member here, but the number is taken: do not reuse it. (Originally
  // SemanticDiscardedEnumeratedIndex, retired with the withIterator redesign.)
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
  // Symbol declared with the `module` keyword (visible to the declaring directory
  // subtree only) but accessed from outside that subtree.
  SemanticSymbolNotInModuleScope = 3088,
  // `==`/`!=` against an enum/union's `.name`, `.ordinal`, or `.rawValue`
  // accessor. Such a comparison is checking which case a value is, which must
  // be done on the value itself: `value == Type.case` for a payload-free case,
  // or `match value` for a union variant (so adding a case forces every site
  // to handle it instead of silently slipping through). Comparing the derived
  // string/int accessor bypasses that and is a string/int compare to boot.
  // 3097: 3089-3096 are taken by the self-hosted compiler's own semantic
  // diagnostics (semanticTypeResolutionLeak..semanticAmbiguousCrossFileCall),
  // so this shared diagnostic takes the next code free on both sides.
  SemanticEnumAccessorComparison = 3097,
  // `try await p` where p came out of storage as a bare `Promise with T`, in one of
  // the two forms that need the thunk's error TYPE. That type has one type parameter
  // — the RESULT — and so no slot to carry the thunk's `throws` type: boxing a promise
  // into it erases the error type, keeping only a runtime bit saying whether the flag
  // is a heap pointer. The two forms that cannot be served without the type are:
  //
  //   - `otherwise (e)` — there is no type to give `e`, and binding it used to hand
  //     back the error flag silently typed as a raw `int`;
  //   - propagation (bare `try await p` inside a `throws` function) — there is no type
  //     to check the enclosing function's `throws` against, so the SPAWNED function's
  //     ordinals get re-thrown through this function's error flag and the caller
  //     decodes one error type as another. When the caller's type has associated
  //     values it then mm_decrefs an ordinal as a pointer and the program faults.
  //
  // Both are refused. The `otherwise` forms that do NOT need the type (a default value,
  // `ignore`, `panic`, an unbound `'label'`) still work: they release the payload off a
  // runtime bit read back from the box. Awaiting the promise where `async` produced it
  // keeps the error type and serves every form.
  SemanticAwaitErrorTypeErased = 3098,

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
