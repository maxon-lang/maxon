---
title: BNF Syntax Reference
description: The formal BNF grammar for Maxon's lexical structure, declarations, types, statements, and expressions.
sidebar:
  order: 1
---

This document defines the complete grammar of the Maxon programming language in
extended BNF notation.

## Notation Conventions

| Notation       | Meaning                                   |
|----------------|-------------------------------------------|
| `'keyword'`    | Terminal keyword or symbol (literal text)  |
| `TOKEN`        | Terminal token produced by the lexer       |
| `rule`         | Non-terminal (grammar rule)               |
| `a b`          | Sequence (a followed by b)                |
| `a \| b`       | Alternation (a or b)                      |
| `[ a ]`        | Optional (zero or one)                    |
| `{ a }`        | Repetition (zero or more)                 |
| `( a \| b )`   | Grouping                                  |
| `NEWLINE`      | Required line break                       |
| `LABEL`        | Character literal used as block label     |

---

## 1 — Lexical Grammar

### 1.1 Characters

```text
letter        = 'a'..'z' | 'A'..'Z'
digit         = '0'..'9'
hex_digit     = digit | 'a'..'f' | 'A'..'F'
bin_digit     = '0' | '1'
oct_digit     = '0'..'7'
```

### 1.2 Identifiers

```text
IDENTIFIER    = ( letter | '_' ) { letter | digit | '_' }
```

### 1.3 Keywords

```text
KEYWORD       = 'and' | 'as' | 'async' | 'await' | 'bool' | 'break' | 'byte' | 'continue'
              | 'cstring' | 'default' | 'else' | 'end' | 'enum' | 'export' | 'extends'
              | 'extension' | 'fallthrough' | 'false' | 'float'
              | 'for' | 'from' | 'function' | 'gives' | 'if' | 'ignore'
              | 'implements' | 'in' | 'int' | 'interface' | 'is' | 'let'
              | 'match' | 'not' | 'of' | 'or' | 'otherwise' | 'panic'
              | 'return' | 'returns' | 'self' | 'Self' | 'shl' | 'shr'
              | 'sizeof' | 'static' | 'then' | 'throw' | 'throws' | 'to'
              | 'true' | 'try' | 'type' | 'typealias' | 'union' | 'upto'
              | 'uses' | 'var' | 'where' | 'while' | 'with' | 'xor'
```

### 1.4 Literals

```text
INTEGER       = decimal_int | hex_int | bin_int | oct_int
decimal_int   = digit { digit | '_' }
hex_int       = '0x' hex_digit { hex_digit | '_' }
bin_int       = '0b' bin_digit { bin_digit | '_' }
oct_int       = '0o' oct_digit { oct_digit | '_' }

FLOAT         = digit { digit } '.' digit { digit } [ ('e' | 'E') ['+' | '-'] digit { digit } ]

STRING        = '"' { string_char | escape_seq } '"'
STRING_INTERP = '"' { string_char | escape_seq | '{' expression [ ':' format_spec ] '}' } '"'
string_char   = <any character except '"', '\', '{', '}', or newline>
escape_seq    = '\n' | '\t' | '\r' | '\0' | '\\' | '\"' | '\{' | '\}' | hex_escape | unicode_escape
hex_escape    = '\x' hex_digit hex_digit
unicode_escape = '\u' hex_digit hex_digit hex_digit hex_digit
format_spec   = int_format | float_format
int_format    = ['0'] [width] [int_type]
int_type      = 'd' | 'x' | 'X' | 'b' | 'o'
float_format  = ['0'] [width] '.' precision
width         = digit { digit }
precision     = digit { digit }

BYTE_STRING   = 'b"' { string_char | escape_seq } '"'

CHARACTER     = "'" ( grapheme_cluster | char_escape ) "'"
grapheme_cluster = <any extended grapheme cluster>
char_escape   = '\n' | '\t' | '\r' | '\0' | '\\' | '\'' | hex_escape | unicode_escape

BOOL          = 'true' | 'false'
```

### 1.5 Labels

Block labels are character literals used as identifiers for block structures.

```text
LABEL         = "'" IDENTIFIER "'"
```

### 1.6 Operators and Punctuation

```text
'+'   '-'   '*'   '/'
'='   '=='  '!='  '<'  '<='  '>'  '>='
'('   ')'   '{'   '}'   '['   ']'
','   ':'   '.'
```

### 1.7 Comments

```text
comment       = line_comment | block_comment
line_comment  = '//' { <any character except newline> } NEWLINE
block_comment = '/*' { <any character> } '*/'
doc_comment   = '///' { <any character except newline> } NEWLINE
```

### 1.8 Conditional Compilation Directives

```text
hash_if       = '#if'
hash_else     = '#else'
hash_endif    = '#endif'

conditional_block
              = '#if' condition NEWLINE
                { <tokens> }
                [ '#else' NEWLINE { <tokens> } ]
                '#endif' NEWLINE

condition     = or_expr
or_expr       = and_expr { 'or' and_expr }
and_expr      = unary_expr { 'and' unary_expr }
unary_expr    = 'not' unary_expr | atom
atom          = 'os' '(' IDENTIFIER ')'
              | 'arch' '(' IDENTIFIER ')'
              | 'testing' '(' BOOL ')'
              | '(' condition ')'
```

Conditional compilation directives are evaluated at parse time. Conditions support boolean operators `not`, `and`, `or` (precedence: `or` < `and` < `not`), plus parentheses for grouping. Supported `os` values: `Windows`, `Linux`, `Macos`, `Wasi`. Supported `arch` values: `x64`, `arm64`, `wasm32`. Supported `testing` values: `true`, `false`. Nested `#if` blocks are supported.

---

## 2 — Program Structure

```text
program       = { top_level_decl }

top_level_decl
              = function_decl
              | extern_decl
              | type_decl
              | enum_decl
              | union_decl
              | interface_decl
              | extension_block
              | typealias_decl
              | top_level_var
              | top_level_let
              | conditional_block
```

### 2.1 Visibility

```text
visibility_prefix = [ 'export' | 'module' ]
```

Any top-level declaration may be preceded by `export` to make it visible
everywhere in the compilation, or by `module` to make it visible to every
file in the declaring directory subtree (same directory + any subdirectory).
The two modifiers are mutually exclusive — combining them is a parse error.
`module` is a contextual keyword: it is recognised only when followed by a
declaration token; in any other position it lexes as an identifier.

---

## 3 — Declarations

### 3.1 Function Declaration

```text
function_decl = visibility_prefix 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] [ throws_clause ] NEWLINE
                body
                'end' LABEL

param_list    = param { ',' param }
param         = IDENTIFIER type_ref [ '=' default_value ]    (* IDENTIFIER '_' discards the parameter *)

default_value = [ '-' ] INTEGER                              (* integer literal *)
              | [ '-' ] FLOAT                                (* float literal *)
              | BOOL                                         (* boolean literal *)
              | STRING                                       (* string literal *)
              | CHARACTER                                    (* character literal, e.g. '/' *)
              | BYTE_STRING                                  (* byte string literal, e.g. b"data" *)
              | IDENTIFIER '.' IDENTIFIER                    (* enum case, e.g. Priority.medium *)
              | array_literal                                (* array literal, e.g. [10, 20, 12] *)
              | struct_literal                               (* struct constructor, e.g. Point{x: 0, y: 0} *)

throws_clause = 'throws' type_ref
```

### 3.2 Extern Function Declaration

```text
extern_decl   = 'extern' 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] NEWLINE
```

### 3.3 Type (Struct) Declaration

```text
type_decl     = visibility_prefix 'type' IDENTIFIER [ uses_clause ]
                [ conformance_clause ] [ where_clause ] NEWLINE
                { type_member }
                'end' LABEL

type_member   = field_decl
              | method_decl
              | static_field_decl
              | static_method_decl
              | typealias_decl

field_decl    = visibility_prefix ('var' | 'let') IDENTIFIER
                ( 'as' type_ref [ '=' expression ]
                | '=' literal_default )
                NEWLINE

                (* literal_default is the shorthand form — type is inferred from the literal.
                   Accepts: signed/unsigned integer, float, bool, or enum case (TypeName.caseName).
                   For any other default expression, the type_ref form is required. *)

literal_default
              = [ '-' ] INTEGER
              | [ '-' ] FLOAT
              | 'true' | 'false'
              | IDENTIFIER '.' IDENTIFIER  (* enum case *)

method_decl   = visibility_prefix 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] [ throws_clause ] NEWLINE
                body
                'end' LABEL

static_field_decl
              = 'static' ('var' | 'let') IDENTIFIER '=' expression NEWLINE

static_method_decl
              = visibility_prefix 'static' 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] [ throws_clause ] NEWLINE
                body
                'end' LABEL
```

### 3.4 Enum Declaration

Enums define named constants with optional raw values. They auto-implement `Equatable` and `Hashable`, and support `==`/`!=` comparison. Enums do NOT support associated values (use `union` for that).

```text
enum_decl     = visibility_prefix 'enum' IDENTIFIER [ backing_type ]
                [ conformance_clause ] NEWLINE
                { enum_case NEWLINE }
                { method_decl }
                'end' LABEL

backing_type  = 'int' | 'float' | 'String'

enum_case     = IDENTIFIER                                          (* simple case *)
              | IDENTIFIER '=' raw_value                            (* raw-value case *)

raw_value     = [ '-' ] INTEGER
              | [ '-' ] FLOAT
              | STRING
              | CHARACTER
              | struct_raw_literal                                   (* struct-backed: TypeName{field: value, ...} *)
              | IDENTIFIER                                            (* function-backed: top-level function name *)

struct_raw_literal
              = IDENTIFIER '{' raw_field_init { ',' raw_field_init } '}'

raw_field_init
              = IDENTIFIER ':' ( [ '-' ] INTEGER | [ '-' ] FLOAT | 'true' | 'false'
              | IDENTIFIER '.' IDENTIFIER                              (* enum member reference *)
              | IDENTIFIER                                             (* constant reference *)
              | struct_raw_literal )
```

### 3.5 Union Declaration

Unions define named cases with optional associated values. They do NOT implement `Equatable` or `Hashable`, do not support `==`/`!=` comparison. Use `match` to inspect union values. Unions support `.name`, `.ordinal`, and the static `.allCaseNames` property (an `Array with String` of case names). They do not support `.allCases` directly, but expose a synthesized `.unionCases` companion enum — `U.unionCases` is an int-backed enum with one bare case per variant, providing `.allCases`, `.fromRawValue`, etc. for symmetric (de)serialization. Unions can also tag each variant with a compile-time struct backing (read via `.rawValue`, identical shape to struct-backed enums).

```text
union_decl    = visibility_prefix 'union' IDENTIFIER
                [ conformance_clause ] NEWLINE
                { union_case NEWLINE }
                { method_decl }
                'end' LABEL

union_case    = IDENTIFIER                                                 (* simple case *)
              | IDENTIFIER '(' assoc_fields ')'                            (* associated-value case *)
              | IDENTIFIER '=' struct_raw_literal                           (* struct-backed simple variant *)
              | IDENTIFIER '(' assoc_fields ')' '=' struct_raw_literal      (* struct-backed associated-value variant *)

assoc_fields  = assoc_field { ',' assoc_field }
assoc_field   = IDENTIFIER type_ref
```

When struct-backed, every variant must carry an `= struct_raw_literal` of the same backing struct type (see `struct_raw_literal` in §3.4). The backing struct is accessible via `.rawValue` on a union value; associated-value payloads are still accessed via `match`.

### 3.6 Interface Declaration

```text
interface_decl
              = visibility_prefix 'interface' IDENTIFIER [ extends_clause ]
                [ uses_clause ] NEWLINE
                { interface_method NEWLINE }
                'end' LABEL

extends_clause
              = 'extends' IDENTIFIER { ',' IDENTIFIER }

interface_method
              = visibility_prefix [ 'static' ] 'function' IDENTIFIER
                '(' [ param_list ] ')' [ 'returns' type_ref ] [ throws_clause ]
```

### 3.7 Extension Block

```text
extension_block
              = 'extension' IDENTIFIER [ conformance_clause ] [ where_clause ] NEWLINE
                { method_decl }
                'end' LABEL
```

### 3.8 Type Alias Declaration

```text
typealias_decl
              = visibility_prefix 'typealias' IDENTIFIER '=' typealias_rhs NEWLINE

typealias_rhs = ranged_type
              | generic_type
              | tuple_type
              | function_type

ranged_type   = primitive_type '(' range_bound ('to' | 'upto') range_bound ')'

primitive_type
              = 'int' | 'float' | 'byte'

range_bound   = [ '-' ] INTEGER
              | [ '-' ] FLOAT
              | sized_type_ref '.' ('min' | 'max')

sized_type_ref
              = 'u8' | 'u16' | 'u32' | 'u64'
              | 'i8' | 'i16' | 'i32' | 'i64'
              | 'f32' | 'f64'
```

**Range validation constraints:**

- Lower bound must be less than upper bound (or less than or equal for `to`)
- When both bounds use type qualifiers, they must reference the same type (e.g., `i64.min to i64.max`, not `i8.min to i32.max`)
- A signed type qualifier (`iN.max`) cannot be paired with a literal on the other side — use the full range (`iN.min to iN.max`) or an unsigned type (`0 to uN.max`)
- Integer ranges cannot span both negative values and values above `i64.max`
- `byte` ranges must have bounds within 0 to u8.max

```text
generic_type  = IDENTIFIER 'with' type_args

tuple_type    = '(' type_ref ',' type_ref { ',' type_ref } ')'
```

### 3.9 Top-Level Variables

```text
top_level_var = visibility_prefix 'var' IDENTIFIER '=' expression NEWLINE
top_level_let = visibility_prefix 'let' IDENTIFIER '=' expression NEWLINE
```

---

## 4 — Type System Clauses

```text
uses_clause   = 'uses' IDENTIFIER { ',' IDENTIFIER }

conformance_clause
              = 'implements' conformance_entry { ',' conformance_entry }

conformance_entry
              = IDENTIFIER [ 'with' type_args ]

type_args     = type_ref
              | '(' type_ref { ',' type_ref } ')'

where_clause  = 'where' constraint { ',' constraint }

constraint    = IDENTIFIER 'is' IDENTIFIER { 'and' IDENTIFIER }

type_ref      = 'bool'
              | 'cstring'
              | 'Self'
              | IDENTIFIER '.' IDENTIFIER
              | IDENTIFIER
              | function_type
              | tuple_type

function_type = 'function' '(' [ type_ref { ',' type_ref } ] ')' [ 'returns' type_ref ]
              (* `returns` is optional; without it, the function type
                 returns void. The literal `function(...)` form is legal
                 only as the topmost type of a `typealias` RHS — at every
                 other use site (parameter types, return types, struct
                 fields, generic arguments) reference the alias by name. *)
```

---

## 5 — Statements

```text
body          = statement NEWLINE { statement NEWLINE }   (* at least one statement required; empty blocks are E3082 *)

statement     = return_stmt
              | annotated_decl
              | var_decl
              | let_decl
              | discard_stmt
              | if_stmt
              | while_stmt
              | for_stmt
              | match_stmt
              | break_stmt
              | continue_stmt
              | throw_stmt
              | panic_stmt
              | try_stmt
              | assignment_stmt
              | tuple_assign_stmt
              | expression_stmt

expression_stmt
              = expression
```

### 5.1 Variable Declarations

```text
annotated_decl
              = '@heap' ( var_decl | let_decl )

var_decl      = 'var' IDENTIFIER '=' expression
              | 'var' '(' IDENTIFIER { ',' IDENTIFIER } ')' '=' expression

let_decl      = 'let' IDENTIFIER '=' expression
              | 'let' '(' IDENTIFIER { ',' IDENTIFIER } ')' '=' expression

discard_stmt  = '_' '=' expression
```

Constraints:
- The expression in `discard_stmt` must be a function call (error E3067)
- `var _ = ...` and `let _ = ...` are not allowed; use `_ = ...` instead

### 5.2 Assignment

```text
assignment_stmt
              = target '=' expression

target        = IDENTIFIER
              | IDENTIFIER '.' IDENTIFIER { '.' IDENTIFIER }
              | 'self' '.' IDENTIFIER { '.' IDENTIFIER }
              | IDENTIFIER '.' IDENTIFIER '=' expression     (* via .set() *)
```

### 5.2.1 Tuple Assignment

Assigns multiple return values to existing mutable variables in one statement:

```text
tuple_assign_stmt
              = '(' tuple_assign_target { ',' tuple_assign_target } ')' '=' expression

tuple_assign_target
              = IDENTIFIER    (* must refer to an existing var-declared variable *)
              | '_'           (* discard this element *)
```

Constraints:
- Each `IDENTIFIER` must refer to a `var`-declared (mutable) variable in scope
- `let` variables are not valid targets (error E2013)
- The number of targets must equal the tuple's element count (error E3005)

### 5.3 Return

```text
return_stmt   = 'return' [ expression ]
```

### 5.4 If Statement

```text
if_stmt       = 'if' condition LABEL NEWLINE
                body
                'end' LABEL [ else_clause ]
              | if_try_stmt

else_clause   = 'else' if_stmt
              | 'else' LABEL NEWLINE body 'end' LABEL
              | 'else' '(' IDENTIFIER ')' LABEL NEWLINE body 'end' LABEL

condition     = expression

if_try_stmt   = 'if' 'try' expression LABEL NEWLINE
                body
                'end' LABEL [ else_clause ]
              | 'if' ('let' | 'var') IDENTIFIER '=' 'try' expression LABEL NEWLINE
                body
                'end' LABEL [ else_clause ]
```

### 5.5 While Loop

```text
while_stmt    = 'while' expression LABEL NEWLINE
                body
                'end' LABEL
```

### 5.6 For Loop

```text
for_stmt      = 'for' loop_var 'in' iterable_expr LABEL NEWLINE
                body
                'end' LABEL

loop_var      = IDENTIFIER                                      (* '_' discards the value *)
              | '(' IDENTIFIER { ',' IDENTIFIER } ')'           (* '_' discards individual elements *)

iterable_expr = expression ('to' | 'upto') expression          (* range form *)
              | expression '.' 'withIterator' '(' ')'          (* iterator + element tuple form *)
              | expression                                      (* iterator form *)
```

### 5.7 Match Statement

```text
match_stmt    = 'match' expression LABEL NEWLINE
                { match_arm NEWLINE }
                'end' LABEL

match_arm     = match_patterns 'then' match_action
              | 'default' 'then' match_action
              | 'default' 'throws' expression                     (* enum: throws error for unmatched cases *)
              | 'default' 'panic' '(' STRING ')'                  (* terminates with error message *)

match_action  = statement [ 'and' 'fallthrough' ]
              | 'break' [ LABEL ]

match_patterns
              = match_pattern { 'or' match_pattern }

match_pattern = literal_pattern
              | case_pattern
              | range_pattern
              | case_range_pattern

literal_pattern
              = [ '-' ] INTEGER
              | [ '-' ] FLOAT
              | STRING
              | CHARACTER
              | BOOL

case_pattern  = IDENTIFIER [ '(' binding_list ')' ]              (* bare case name; bindings for union associated values *)

binding_list  = IDENTIFIER { ',' IDENTIFIER }                    (* '_' discards individual bindings; all-discard is error E3081 *)

range_pattern = expression 'to' expression              (* inclusive both bounds *)
              | expression 'upto' expression            (* exclusive upper bound *)
              | expression 'to' 'max'                   (* open upper bound *)
              | 'min' 'to' expression                   (* open lower, inclusive upper *)
              | 'min' 'upto' expression                 (* open lower, exclusive upper *)

case_range_pattern
              = IDENTIFIER 'to' IDENTIFIER              (* inclusive case range — bare case names *)
              | IDENTIFIER 'upto' IDENTIFIER            (* exclusive upper case range — bare case names *)
```

Match arms for enum and union types use **bare case names** (e.g., `red`, `pending`), not
qualified `Type.case` syntax. Using a qualified name such as `Color.red` in a match arm is
a compile error (E3075).

Although `match_action` reduces to `statement`, the parser rejects the block-opening
statements `if`, `while`, `for`, nested `match`, and the multi-line block forms of
`try` (`try 'label' ... end 'label'` and `try call() otherwise 'label' ... end 'label'`)
in match arms with E2049. Every single-statement `try` form (bare propagation,
`otherwise panic`, `otherwise ignore`, `otherwise return/break/continue/throw`, and
`otherwise <expr>`) is allowed.

### 5.8 Break and Continue

```text
break_stmt    = 'break' [ LABEL ]   (* LABEL must NOT name the innermost
                                       enclosing loop; that's E2048 *)

continue_stmt = 'continue' [ LABEL ] (* same E2048 rule as `break` *)
```

### 5.9 Throw

```text
throw_stmt    = 'throw' expression
```

### 5.11 Panic

```text
panic_stmt    = 'panic' '(' ( STRING | STRING_INTERP ) ')'
```

### 5.12 Try Statement

```text
try_stmt      = 'try' expression 'otherwise' otherwise_clause
              | 'try' expression                                (* propagation — only in throwing functions *)
              | try_block

otherwise_clause
              = 'ignore'
              | 'panic' '(' ( STRING | STRING_INTERP ) ')'      (* panic on error *)
              | ( return_stmt | break_stmt | continue_stmt | throw_stmt )  (* single-statement form *)
              | expression                                      (* default value *)
              | [ '(' IDENTIFIER ')' ] LABEL NEWLINE
                body
                'end' LABEL

(* try block wraps multiple throwing calls under a shared handler. Inside the body,
   bare calls to throwing functions do not require the `try` keyword. The block-handler
   form must contain a match on the error binding; the terminal forms either panic or
   re-throw, and may bind `(e)` for use in the panic message or throw expression. *)
try_block     = 'try' LABEL NEWLINE
                body
                'end' LABEL NEWLINE
                'otherwise' try_block_otherwise

try_block_otherwise
              = '(' IDENTIFIER ')' LABEL NEWLINE
                body                                            (* must contain `match` on binding *)
                'end' LABEL
              | [ '(' IDENTIFIER ')' ] 'panic' '(' ( STRING | STRING_INTERP ) ')'   (* terminal panic *)
              | [ '(' IDENTIFIER ')' ] 'throws' expression                          (* re-throw fixed error *)
```

---

## 6 — Expressions

### 6.1 Precedence (lowest to highest)

| Level | Operators / Forms                     | Associativity |
|------:|---------------------------------------|---------------|
| 0     | `if`...`else` (conditional)           | Right         |
| 1     | `or`                                  | Left          |
| 2     | `xor`                                 | Left          |
| 3     | `and`                                 | Left          |
| 4     | `==`  `!=`  `<`  `>`  `<=`  `>=`  `is`  `is not` | Left          |
| 5     | `shl`  `shr`                          | Left          |
| 6     | `+`  `-`                              | Left          |
| 7     | `*`  `/`  `mod`                       | Left          |
| 8     | `as` (type cast)                      | Left (postfix)|
| 9     | `-` (unary negation), `not`           | Right (prefix)|
| 10    | `.` (member access), `()` (call)      | Left (postfix)|

### 6.2 Expression Grammar

```text
expression    = conditional_expr

conditional_expr
              = or_expr 'if' expression 'else' conditional_expr   (* ternary, right-associative *)
              | or_expr

or_expr       = xor_expr { 'or' xor_expr }

xor_expr      = and_expr { 'xor' and_expr }

and_expr      = comparison { 'and' comparison }

comparison    = shift_expr { ( cmp_op shift_expr ) | ( 'is' ['not'] shift_expr ) }
cmp_op        = '==' | '!=' | '<' | '>' | '<=' | '>='

shift_expr    = additive { ('shl' | 'shr') additive }

additive      = multiplicative { ('+' | '-') multiplicative }

multiplicative
              = cast_expr { ('*' | '/' | 'mod') cast_expr }

cast_expr     = unary_expr { 'as' type_ref }

unary_expr    = '-' postfix_expr
              | 'not' unary_expr
              | postfix_expr

postfix_expr  = primary { postfix_op }

postfix_op    = '.' IDENTIFIER [ '(' [ arg_list ] ')' ]   (* method call or field access *)
              | '.' INTEGER                                 (* tuple positional access: .0, .1, ... *)
              | '(' [ arg_list ] ')'                        (* function call *)
```

### 6.3 Primary Expressions

```text
primary       = INTEGER
              | FLOAT
              | STRING
              | STRING_INTERP
              | BYTE_STRING
              | CHARACTER
              | 'true'
              | 'false'
              | 'self'
              | array_literal
              | map_literal
              | tuple_literal
              | paren_expr
              | struct_literal
              | enum_access
              | static_access
              | closure
              | match_expr
              | try_expr
              | from_expr
              | async_expr
              | await_expr
              | type_bound_expr
              | sizeof_expr
              | IDENTIFIER

array_literal = '[' [ expression { ',' expression } ] ']'

map_literal   = '[' expression ':' expression { ',' expression ':' expression } ']'

tuple_literal = '(' expression ',' expression { ',' expression } ')'

paren_expr    = '(' expression ')'

struct_literal
              = IDENTIFIER '{' [ field_init { ',' field_init } ] '}'

field_init    = IDENTIFIER ':' expression

                (* Semantic rule (E3086): every field of the constructed type must be
                   initialized. A field counts as initialized if (a) its declaration
                   supplies a default via `= expr`, (b) it appears as a field_init in
                   the literal, or (c) the literal is the direct return expression of
                   a `static` function whose return type is the enclosing type, and
                   `self.field = expr` assigns it on every control-flow path reaching
                   the literal. A literal-provided value always wins over a default. *)

                (* Ranged-primitive construction is performed via the postfix `as`
                   cast (see `cast_expr`) — `8080 as Port`, `n as PoolA.Idx`.
                   The dedicated `TypeName{value}` syntax was removed in favor
                   of unifying with `as`. *)

enum_access   = IDENTIFIER '.' IDENTIFIER [ '(' [ arg_list ] ')' ]
              | IDENTIFIER '.' 'allCases'                            (* Array of all cases — enums only *)
              | IDENTIFIER '.' 'allCaseNames'                        (* Array with String of case names — enums and unions *)
              | IDENTIFIER '.' 'unionCases' '.' IDENTIFIER           (* synthesized discriminant-enum case — unions only *)
              | IDENTIFIER '.' 'unionCases' '.' 'allCases'           (* Array of all discriminant cases — unions only *)
              | IDENTIFIER '.' 'unionCases' '.' 'fromRawValue' '(' expression ')'  (* lift int discriminant to companion enum *)

static_access = IDENTIFIER '.' IDENTIFIER [ '(' [ arg_list ] ')' ]

type_bound_expr
              = sized_type_name '.' ( 'min' | 'max' )         (* e.g., u64.max, i32.min *)

sizeof_expr   = 'sizeof' '(' type_ref ')'                     (* compile-time size in bytes *)

from_expr     = IDENTIFIER 'from' '[' [ expression { ',' expression } ] ']'

closure       = 'function' '(' [ closure_params ] ')' 'gives' expression
closure_params
              = closure_param { ',' closure_param }
closure_param = IDENTIFIER [ type_ref ]                          (* '_' discards the parameter *)
```

### 6.4 Match Expression

```text
match_expr    = 'match' expression LABEL NEWLINE
                { match_expr_arm NEWLINE }
                'end' LABEL

match_expr_arm
              = match_patterns 'gives' expression
              | match_patterns 'panic' '(' ( STRING | STRING_INTERP ) ')'   (* per-arm panic *)
              | match_patterns 'throws' expression                          (* per-arm throw *)
              | 'default' 'gives' expression
              | 'default' 'throws' expression                     (* enum: throws error for unmatched cases *)
              | 'default' 'panic' '(' STRING ')'                  (* terminates with error message *)
```

### 6.5 Try Expression

```text
try_expr      = 'try' expression 'otherwise' otherwise_clause
              | 'try' expression
```

### 6.6 Async/Await Expressions

```text
async_expr    = 'async' IDENTIFIER '(' [ arg_list ] ')'    (* spawn green thread, returns promise *)
              | 'async' TYPE '.' IDENTIFIER '(' [ arg_list ] ')'  (* spawn struct method call *)

await_expr    = 'await' expression                          (* wait for promise, returns result *)

try_await     = 'try' 'await' expression                    (* await throwing promise, propagate error *)
              | 'try' 'await' expression 'otherwise' otherwise_clause  (* see 5.12 for all forms *)

cancel_expr   = expression '.' 'cancel' '(' ')'            (* cancel a green thread *)
```

**Restrictions:**
- `async` can only be applied to direct function calls (not closures or indirect calls)
- `async` target function must yield (contain I/O operations or `await` points)
- Throwing async functions require `try await` (not plain `await`)

### 6.7 Function and Method Calls

```text
call_expr     = IDENTIFIER '(' [ arg_list ] ')'
              | postfix_expr '.' IDENTIFIER '(' [ arg_list ] ')'

arg_list      = arg { ',' arg }

arg           = expression                                  (* first argument: positional *)
              | IDENTIFIER ':' expression                   (* subsequent arguments: named *)
```

**Calling convention:** the first argument is positional; all subsequent
arguments must use `name: value` syntax and may appear in any order.
Arguments with default values may be omitted.

---

## 7 — Summary of Block Structure

Every compound statement in Maxon requires a single-quoted label after
the opening keyword and a matching label after `end`.

```text
if <cond> 'label'  ...  end 'label'
while <cond> 'label'  ...  end 'label'
for <var> in <iter> 'label'  ...  end 'label'
match <expr> 'label'  ...  end 'label'
try <expr> otherwise 'label'  ...  end 'label'
else 'label'  ...  end 'label'
```

Type, enum, union, interface, and extension bodies also end with a matching label:

```text
type Point  ...  end 'Point'
enum Color  ...  end 'Color'
union Result  ...  end 'Result'
interface Hashable  ...  end 'Hashable'
extension Iterable  ...  end 'Iterable'
function main()  ...  end 'main'
```

---

## Appendix A — Complete Token Table

| Token Type         | Lexeme(s)                                       |
|--------------------|-------------------------------------------------|
| `Identifier`       | `[a-zA-Z_][a-zA-Z0-9_]*`                        |
| `IntegerLiteral`   | `42`, `0xFF`, `0b1010`, `0o777`, `1_000`         |
| `FloatLiteral`     | `3.14`, `1.0e10`                                 |
| `StringLiteral`    | `"hello"`                                        |
| `StringInterp`     | `"hello {name}"`                                 |
| `CharacterLiteral` | `'A'`, `'\n'`                                    |
| `Plus`             | `+`                                              |
| `Minus`            | `-`                                              |
| `Star`             | `*`                                              |
| `Slash`            | `/`                                              |
| `Equals`           | `=`                                              |
| `EqualsEquals`     | `==`                                             |
| `NotEquals`        | `!=`                                              |
| `LessThan`         | `<`                                              |
| `LessEquals`       | `<=`                                             |
| `GreaterThan`      | `>`                                              |
| `GreaterEquals`    | `>=`                                             |
| `LeftParen`        | `(`                                              |
| `RightParen`       | `)`                                              |
| `LeftBrace`        | `{`                                              |
| `RightBrace`       | `}`                                              |
| `LeftBracket`      | `[`                                              |
| `RightBracket`     | `]`                                              |
| `Comma`            | `,`                                              |
| `Colon`            | `:`                                              |
| `Dot`              | `.`                                              |
| `Newline`          | `\n`                                             |
| `DocComment`       | `/// ...`                                        |
| `HashIf`           | `#if`                                            |
| `HashElse`         | `#else`                                          |
| `HashEndif`        | `#endif`                                         |
| `Eof`              | end of input                                     |
