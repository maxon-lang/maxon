## Easiest (Simple Compiler Changes)
arithmetic.md - Basic arithmetic operators (likely already working)
assignment.md - Variable assignment (likely already working)
comparison-operators.md - Comparison operators (likely already working)
expressions.md - Basic expression evaluation (likely already working)
float-type.md - Float type (already working)
function-declaration.md - Function declarations (already working)
if-statements.md - If/else control flow (likely already working)
int-type.md - Basic integer type (already working)
literals.md - Basic literal parsing (likely already working)
return-statement.md - Basic control flow (likely already working)
unary-negation.md - Unary minus operator (simple)
variables.md - let/var declarations (likely already working)

## Simple additions:
bitwise-operators.md - Add bitwise ops to lexer/parser/codegen
byte-type.md - Add u8 type and casting
type-casting.md
unary-operators.md - Additional unary operators
parentheses.md - Expression grouping
redundant-type-annotation.md - Error checking
missing-return-error.md - Error checking
unknown-keyword-error.md - Error checking
unused-parameters.md - Warning/error checking
parameter-labels.md - Parameter syntax enhancement
qualified-names.md - Namespace-qualified names
export-keyword.md - Export visibility
top-level-let.md - Top-level constant declarations
static-variables.md - Static variable support
static-methods.md - Static method support

## Medium Difficulty
method-calls.md - Method call syntax and dispatch
self-keyword.md - Self reference in methods
type-methods.md - Methods on types
method-call-on-parameter.md - Method calls on parameters
contextual-literal-typing.md - Type inference improvements
implicit-type-conversion.md - Type coercion rules
namespaces.md - Namespace system
multi-file.md - Multi-file compilation
stdlib-basic.md - Basic stdlib integration
stdlib-autodiscovery.md - Automatic stdlib discovery
duplicate-block-identifiers.md - Error checking for blocks

## Prerequisites
pair.md
equatable.md
enums-simple.md
match-simple.md
error-handling.md
runtime (HeapAlloc,HeapFree, etc)
__chkstk
closures
arrays.md
vector.md
interface-conformance.md - Interface conformance checking
if-try (without print or string)
set.md - Hash set data structure with full runtime

first-class-functions.md - Function pointers, closures
challenge-struct-field-assign.md - Struct field assignment semantics
strings



## Hard (Significant Compiler Work)
initablefromarrayliteral.md - Protocol for array initialization
array-managed-elements.md - Managed array memory
init-from-literal.md - Initialization protocols
interfaces.md - Interface system with conformance
interface-extensions.md - Extending interfaces
associated-types.md - Associated types in interfaces
collection.md - Collection protocols
primitive-hashable.md - Hashable protocol for primitives
exp.md - Math function (might need runtime support)
type-checking.md - Enhanced type checking

## Very Hard (Major Features)
ownership.md - Ownership and borrow checking
map.md - Hash map data structure with full runtime
stdlib-array.md - Full stdlib Array implementation
stdlib-set.md - Full stdlib Set implementation
enums.md - Full enum system with associated values
match-statements.md - Pattern matching on enums
safe-ffi.md - FFI boundary safety

## Extremely Hard (Language Evolution)
challenge-struct-ownership.md - Complex ownership scenarios
challenge-struct-lifetime.md - Lifetime tracking
challenge-nested-structs.md - Nested struct ownership
challenge-array-of-structs.md - Array ownership with structs
optimizations.md - Optimization passes
export-var-fields.md - Exporting mutable fields
parsable-interface.md - Parse protocol
