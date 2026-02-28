# Maxon Language — BNF Syntax Reference

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

```
letter        = 'a'..'z' | 'A'..'Z'
digit         = '0'..'9'
hex_digit     = digit | 'a'..'f' | 'A'..'F'
bin_digit     = '0' | '1'
oct_digit     = '0'..'7'
```

### 1.2 Identifiers

```
IDENTIFIER    = ( letter | '_' ) { letter | digit | '_' }
```

### 1.3 Keywords

```
KEYWORD       = 'and' | 'as' | 'bool' | 'break' | 'byte' | 'continue'
              | 'default' | 'else' | 'end' | 'enum' | 'export' | 'extends'
              | 'extension' | 'fallthrough' | 'false' | 'float'
              | 'for' | 'from' | 'function' | 'gives' | 'if' | 'ignore'
              | 'implements' | 'in' | 'int' | 'interface' | 'is' | 'let'
              | 'match' | 'not' | 'of' | 'or' | 'otherwise'
              | 'return' | 'returns' | 'self' | 'Self' | 'shl' | 'shr'
              | 'static' | 'then' | 'throw' | 'throws' | 'to'
              | 'true' | 'try' | 'type' | 'typealias' | 'union' | 'upto'
              | 'uses' | 'var' | 'where' | 'while' | 'with' | 'xor'
```

### 1.4 Literals

```
INTEGER       = decimal_int | hex_int | bin_int | oct_int
decimal_int   = digit { digit | '_' }
hex_int       = '0x' hex_digit { hex_digit | '_' }
bin_int       = '0b' bin_digit { bin_digit | '_' }
oct_int       = '0o' oct_digit { oct_digit | '_' }

FLOAT         = digit { digit } '.' digit { digit } [ ('e' | 'E') ['+' | '-'] digit { digit } ]

STRING        = '"' { string_char | escape_seq } '"'
STRING_INTERP = '"' { string_char | escape_seq | '{' expression [ ':' format_spec ] '}' } '"'
string_char   = <any character except '"', '\', '{', '}', or newline>
escape_seq    = '\n' | '\t' | '\r' | '\0' | '\\' | '\"' | '\{' | '\}' | hex_escape
hex_escape    = '\x' hex_digit hex_digit
format_spec   = int_format | float_format
int_format    = ['0'] [width] [int_type]
int_type      = 'd' | 'x' | 'X' | 'b' | 'o'
float_format  = ['0'] [width] '.' precision
width         = digit { digit }
precision     = digit { digit }

BYTE_STRING   = 'b"' { string_char | escape_seq } '"'

CHARACTER     = "'" ( grapheme_cluster | char_escape ) "'"
grapheme_cluster = <any extended grapheme cluster>
char_escape   = '\n' | '\t' | '\r' | '\0' | '\\' | '\'' | hex_escape

BOOL          = 'true' | 'false'
```

### 1.5 Labels

Block labels are character literals used as identifiers for block structures.

```
LABEL         = "'" IDENTIFIER "'"
```

### 1.6 Operators and Punctuation

```
'+'   '-'   '*'   '/'
'='   '=='  '!='  '<'  '<='  '>'  '>='
'('   ')'   '{'   '}'   '['   ']'
','   ':'   '.'
```

### 1.7 Comments

```
comment       = '//' { <any character except newline> } NEWLINE
doc_comment   = '///' { <any character except newline> } NEWLINE
```

---

## 2 — Program Structure

```
program       = { top_level_decl }

top_level_decl
              = function_decl
              | extern_decl
              | type_decl
              | union_decl
              | enum_decl
              | interface_decl
              | extension_block
              | typealias_decl
              | top_level_var
              | top_level_let
```

### 2.1 Visibility

```
export_prefix = [ 'export' ]
```

Any top-level declaration may be preceded by `export` to make it visible
to other files.

---

## 3 — Declarations

### 3.1 Function Declaration

```
function_decl = export_prefix 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] [ throws_clause ] NEWLINE
                body
                'end' LABEL

param_list    = param { ',' param }
param         = IDENTIFIER type_ref [ '=' expression ]

throws_clause = 'throws' type_ref
```

### 3.2 Extern Function Declaration

```
extern_decl   = 'extern' 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] NEWLINE
```

### 3.3 Type (Struct) Declaration

```
type_decl     = export_prefix 'type' IDENTIFIER [ uses_clause ]
                [ conformance_clause ] [ where_clause ] NEWLINE
                { type_member }
                'end' LABEL

type_member   = field_decl
              | method_decl
              | static_field_decl
              | static_method_decl
              | typealias_decl

field_decl    = export_prefix ('var' | 'let') IDENTIFIER ( type_ref | '=' expression ) NEWLINE

method_decl   = export_prefix 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] [ throws_clause ] NEWLINE
                body
                'end' LABEL

static_field_decl
              = 'static' ('var' | 'let') IDENTIFIER '=' expression NEWLINE

static_method_decl
              = export_prefix 'static' 'function' IDENTIFIER '(' [ param_list ] ')'
                [ 'returns' type_ref ] [ throws_clause ] NEWLINE
                body
                'end' LABEL
```

### 3.4 Union Declaration

```
union_decl    = export_prefix 'union' IDENTIFIER [ backing_type ]
                [ conformance_clause ] NEWLINE
                { union_case NEWLINE }
                { method_decl }
                'end' LABEL

backing_type  = 'int' | 'float' | 'String'

union_case    = IDENTIFIER                                          (* simple case *)
              | IDENTIFIER '=' raw_value                            (* raw-value case *)
              | IDENTIFIER '(' assoc_fields ')'                     (* associated-value case *)

raw_value     = [ '-' ] INTEGER
              | [ '-' ] FLOAT
              | STRING
              | CHARACTER

```

### 3.5 Enum Declaration

```
enum_decl     = export_prefix 'enum' IDENTIFIER NEWLINE
                { enum_case NEWLINE }
                'end' LABEL

enum_case     = IDENTIFIER                  (* auto-increment from 0; integer-backed only *)
              | IDENTIFIER '=' raw_value    (* explicit value *)

raw_value     = [ '-' ] INTEGER
              | [ '-' ] FLOAT
              | STRING
              | CHARACTER

assoc_fields  = assoc_field { ',' assoc_field }
assoc_field   = IDENTIFIER type_ref
```

### 3.6 Interface Declaration

```
interface_decl
              = export_prefix 'interface' IDENTIFIER [ extends_clause ]
                [ uses_clause ] NEWLINE
                { interface_method NEWLINE }
                'end' LABEL

extends_clause
              = 'extends' IDENTIFIER { ',' IDENTIFIER }

interface_method
              = export_prefix [ 'static' ] 'function' IDENTIFIER
                '(' [ param_list ] ')' [ 'returns' type_ref ] [ throws_clause ]
```

### 3.7 Extension Block

```
extension_block
              = 'extension' IDENTIFIER [ conformance_clause ] [ where_clause ] NEWLINE
                { method_decl }
                'end' LABEL
```

### 3.8 Type Alias Declaration

```
typealias_decl
              = export_prefix 'typealias' IDENTIFIER '=' typealias_rhs NEWLINE

typealias_rhs = ranged_type
              | generic_type
              | tuple_type

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

```
generic_type  = IDENTIFIER 'with' type_args

tuple_type    = '(' type_ref ',' type_ref { ',' type_ref } ')'
```

### 3.9 Top-Level Variables

```
top_level_var = export_prefix 'var' IDENTIFIER '=' expression NEWLINE
top_level_let = export_prefix 'let' IDENTIFIER '=' expression NEWLINE
```

---

## 4 — Type System Clauses

```
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
              | 'Self'
              | IDENTIFIER
              | function_type
              | tuple_type

function_type = '(' [ type_ref { ',' type_ref } ] ')' 'returns' type_ref
```

---

## 5 — Statements

```
body          = { statement NEWLINE }

statement     = return_stmt
              | var_decl
              | let_decl
              | if_stmt
              | while_stmt
              | for_stmt
              | match_stmt
              | break_stmt
              | continue_stmt
              | throw_stmt
              | try_stmt
              | assignment_stmt
              | expression_stmt

expression_stmt
              = expression
```

### 5.1 Variable Declarations

```
var_decl      = 'var' IDENTIFIER '=' expression
              | 'var' '(' IDENTIFIER { ',' IDENTIFIER } ')' '=' expression

let_decl      = 'let' IDENTIFIER '=' expression
              | 'let' '(' IDENTIFIER { ',' IDENTIFIER } ')' '=' expression
```

### 5.2 Assignment

```
assignment_stmt
              = target '=' expression

target        = IDENTIFIER
              | IDENTIFIER '.' IDENTIFIER { '.' IDENTIFIER }
              | 'self' '.' IDENTIFIER { '.' IDENTIFIER }
              | IDENTIFIER '.' IDENTIFIER '=' expression     (* via .set() *)
```

### 5.3 Return

```
return_stmt   = 'return' [ expression ]
```

### 5.4 If Statement

```
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
              | 'if' 'let' IDENTIFIER '=' 'try' expression LABEL NEWLINE
                body
                'end' LABEL [ else_clause ]
```

### 5.5 While Loop

```
while_stmt    = 'while' expression LABEL NEWLINE
                body
                'end' LABEL
```

### 5.6 For Loop

```
for_stmt      = 'for' loop_var 'in' iterable_expr LABEL NEWLINE
                body
                'end' LABEL

loop_var      = IDENTIFIER
              | '(' IDENTIFIER { ',' IDENTIFIER } ')'

iterable_expr = expression ('to' | 'upto') expression          (* range form *)
              | expression '.' 'enumerated' '(' ')'            (* enumerated form *)
              | expression                                      (* iterator form *)
```

### 5.7 Match Statement

```
match_stmt    = 'match' expression LABEL NEWLINE
                { match_arm NEWLINE }
                'end' LABEL

match_arm     = match_patterns 'then' match_action
              | 'default' 'then' match_action
              | 'default' 'throws' expression                   (* union-only: throws error for unmatched cases *)

match_action  = statement [ 'and' 'fallthrough' ]
              | 'break' [ LABEL ]

match_patterns
              = match_pattern { 'or' match_pattern }

match_pattern = literal_pattern
              | union_pattern
              | range_pattern

literal_pattern
              = [ '-' ] INTEGER
              | [ '-' ] FLOAT
              | STRING
              | CHARACTER
              | BOOL

union_pattern = IDENTIFIER [ '(' binding_list ')' ]

binding_list  = IDENTIFIER { ',' IDENTIFIER }

range_pattern = expression '..=' expression             (* inclusive both bounds *)
              | expression '..<' expression             (* exclusive upper bound *)
              | expression '..'                         (* open upper bound *)
              | '..=' expression                        (* open lower, inclusive upper *)
              | '..<' expression                        (* open lower, exclusive upper *)
              | '..'                                    (* wildcard *)
```

### 5.8 Break and Continue

```
break_stmt    = 'break' [ LABEL ]

continue_stmt = 'continue' [ LABEL ]
```

### 5.9 Throw

```
throw_stmt    = 'throw' expression
```

### 5.10 Try Statement

```
try_stmt      = 'try' expression 'otherwise' otherwise_clause
              | 'try' expression                                (* propagation — only in throwing functions *)

otherwise_clause
              = 'ignore'
              | expression                                      (* default value *)
              | [ '(' IDENTIFIER ')' ] LABEL NEWLINE
                body
                'end' LABEL
```

---

## 6 — Expressions

### 6.1 Precedence (lowest to highest)

| Level | Operators / Forms                     | Associativity |
|------:|---------------------------------------|---------------|
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

```
expression    = or_expr

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

unary_expr    = '-' unary_expr
              | 'not' unary_expr
              | postfix_expr

postfix_expr  = primary { postfix_op }

postfix_op    = '.' IDENTIFIER [ '(' [ arg_list ] ')' ]   (* method call or field access *)
              | '.' INTEGER                                 (* tuple positional access: .0, .1, ... *)
              | '(' [ arg_list ] ')'                        (* function call *)
```

### 6.3 Primary Expressions

```
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
              | ranged_construction
              | union_access
              | static_access
              | closure
              | match_expr
              | try_expr
              | from_expr
              | IDENTIFIER

array_literal = '[' [ expression { ',' expression } ] ']'

map_literal   = '[' expression ':' expression { ',' expression ':' expression } ']'

tuple_literal = '(' expression ',' expression { ',' expression } ')'

paren_expr    = '(' expression ')'

struct_literal
              = IDENTIFIER '{' field_init { ',' field_init } '}'

field_init    = IDENTIFIER ':' expression

ranged_construction
              = IDENTIFIER '{' expression '}'               (* e.g., Age{25} *)

union_access  = IDENTIFIER '.' IDENTIFIER [ '(' [ arg_list ] ')' ]

static_access = IDENTIFIER '.' IDENTIFIER [ '(' [ arg_list ] ')' ]

from_expr     = IDENTIFIER 'from' '[' [ expression { ',' expression } ] ']'

closure       = '(' [ closure_params ] ')' 'gives' expression
closure_params
              = closure_param { ',' closure_param }
closure_param = IDENTIFIER [ type_ref ]
```

### 6.4 Match Expression

```
match_expr    = 'match' expression LABEL NEWLINE
                { match_expr_arm NEWLINE }
                'end' LABEL

match_expr_arm
              = match_patterns 'gives' expression
              | 'default' 'gives' expression
              | 'default' 'throws' expression                   (* union-only: throws error for unmatched cases *)
```

### 6.5 Try Expression

```
try_expr      = 'try' expression 'otherwise' otherwise_clause
              | 'try' expression
```

### 6.6 Function and Method Calls

```
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

```
if <cond> 'label'  ...  end 'label'
while <cond> 'label'  ...  end 'label'
for <var> in <iter> 'label'  ...  end 'label'
match <expr> 'label'  ...  end 'label'
try <expr> otherwise 'label'  ...  end 'label'
else 'label'  ...  end 'label'
```

Type, union, enum, interface, and extension bodies also end with a matching label:

```
type Point  ...  end 'Point'
union Color  ...  end 'Color'
enum Status  ...  end 'Status'
interface Hashable  ...  end 'Hashable'
extension Iterable  ...  end 'Iterable'
function main()  ...  end 'main'
```

---

## 8 — Reserved for Future Use

The following tokens are recognized but not yet fully specified:

- `'of'` — reserved keyword
- `'extends'` — used in interface inheritance

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
| `Eof`              | end of input                                     |
