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
  // E2054: a `shl`/`shr` whose count is a LITERAL outside 0..63 (`a shl -1`, `a shl 64`).
  // An `int` is 64 bits, so no other distance names a distinct shift, and the hardware
  // silently MASKS the count into that range — which turned `a shl -1`, read by a human as
  // "shift the other way", into the maximum LEFT shift. A shift by a runtime value is
  // unaffected and still legal. (2053 is taken by maxon-shv2's arg-missing-label check,
  // which the bootstrap reports through ParserUnexpectedToken.)
  ParserShiftCountOutOfRange = 2054,

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
  // A promise is stored in a Promise type that does not name the error its thunk throws.
  //
  // `Promise` is parameterised by BOTH what its thunk returns and what its thunk throws:
  // `Promise with T` is a NON-throwing promise, `Promise with (T, E)` one that throws E.
  // Storing a promise in the wrong one is refused here — including, in particular, storing
  // a THROWING promise in a `Promise with T`, which is what used to erase the error type.
  //
  // That erasure was the root of a family of bugs, all of which this refusal (plus the
  // two-parameter form that makes it avoidable) turns into impossibilities:
  //
  //   - `otherwise (e)` had no type to give `e`, and silently handed back the raw i64
  //     promise handle typed `int`;
  //   - an associated-value error's payload had no static type to mm_decref, so it LEAKED
  //     (only a runtime `errorIsHeapPtr` bit in the box approximated the answer);
  //   - propagation (a bare `try await p` inside a `throws` function) had no error type to
  //     check the enclosing function's `throws` against, so a thunk throwing A could be
  //     awaited inside a function throwing B and A's ordinals reinterpreted as B's tags.
  //
  // The fix a diagnostic can name is the point: it says which two-parameter type to write.
  SemanticPromiseErrorTypeMismatch = 3098,
  // A closure that CAPTURES, ESCAPING the frame its captures point into.
  //
  // THE RULE: a closure that captures may not escape its defining frame.
  //
  // A closure captures BY REFERENCE: LowerClosureCreate allocates an environment and
  // fills it with the ADDRESSES of the enclosing frame's stack slots, so that reads
  // through a capture see later mutations of the captured variable. The environment is
  // therefore only meaningful while that frame is alive. Let the closure outlive the
  // frame and every captured read dereferences a dead frame — the classic upward-funarg
  // problem. It compiles clean and dies at runtime, so it is refused where the mistake
  // is still legible rather than left to fault inside emitted code the author never wrote.
  //
  // The routes refused are every store the parser can see WITHOUT interprocedural
  // analysis:
  //   - RETURNING one out of the frame that built it (`makeAdder` — the common idiom, and
  //     the reason this exists). Returning a closure an OUTER frame built is fine and is
  //     allowed: that environment points into a frame that is still alive.
  //   - storing one in a struct FIELD, a GLOBAL/static, a CONTAINER (array/map literal)
  //     element, a union's associated-value PAYLOAD, or through a PAYLOAD BINDING (which
  //     looks like a plain local but is an alias INTO the enum's heap box, so assigning
  //     through it writes back). Each is one 8-byte slot holding the code pointer alone,
  //     and each is heap memory outliving every frame, so the store drops the environment;
  //     the call then passes env=0 and the first captured read dereferences null.
  //
  // DELIBERATELY NOT REFUSED — the interprocedural route: a capturing closure passed as a
  // CALL ARGUMENT to a callee that then stores it (`Handler.create(function(n) gives n +
  // bump)`), and symmetrically a capturing closure arriving as a call's RETURN value. At
  // that store the value is a *parameter*, and whether it carries an environment is a fact
  // about the CALLER — deciding it needs a per-parameter escape summary propagated over
  // the call graph, i.e. escape analysis proper. That is scoped OUT of the bootstrap and
  // stays a runtime nil-deref. Passing a capturing closure DOWN to a callee that only
  // CALLS it is perfectly safe and must keep working.
  //
  // A NON-capturing closure is unaffected and must keep working — it lowers to a plain
  // MaxonFunctionRefOp, has no environment to lose, and passes every check above BY
  // CONSTRUCTION rather than by an exception carved for it. It is what a table of handlers
  // or passes keyed by a struct field is built from.
  //
  // What would MAKE the refused routes work is escape analysis plus by-value (or boxed)
  // capture, so a closure's environment outlives the frame that built it. That is a real
  // language mechanism and it is DELIBERATELY DEFERRED here: adopting it would change the
  // by-reference capture semantics the closure specs currently pin. shv2 schedules it at
  // P1.5, where it co-lands with `async` — a green-thread capture IS an escape.
  SemanticCapturingClosureEscapes = 3099,

  // A promise is awaited a SECOND time. `await` is LINEAR: a promise is awaited exactly once.
  //
  // 3100 rather than 3099: E3099 is SemanticCapturingClosureEscapes, which landed on main while
  // this was in review. Both of us picked "the next code free" and both of us were right, because
  // the registry is written down TWICE — here and in maxon-selfhosted/Compiler/ErrorCode.maxon —
  // and neither copy sees the other's pending codes. Same disease as the promise's two bits, one
  // level up. Keep the two files in step until there is only one of them.
  //
  // The thunk owns its result and HANDS IT OVER at the await — that is the ownership model the
  // language already has everywhere else. A second await would take a second +1 on a payload the
  // thunk only ever owned once, and the two releases underflow the refcount and free it twice
  // ("mm_decref: refcount underflow (already zero)"). It is not an error-handling bug and does
  // not need a throwing thunk: a plain `async` returning a String double-frees identically.
  //
  // So the double-free is made UNREPRESENTABLE rather than fixed. The check is flow-sensitive —
  // two awaits of the same promise in mutually exclusive branches are each the only await on
  // their own path, and are allowed; what is refused is a second await REACHABLE from a first.
  SemanticPromiseAlreadyAwaited = 3100,

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
