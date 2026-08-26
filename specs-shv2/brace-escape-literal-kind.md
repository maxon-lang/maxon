---
feature: brace-escape-literal-kind
status: experimental
keywords: [escape, brace, interpolation, literal, character, byte-string]
category: literals
---

# Brace Escapes Belong to the Interpolating Literal

## Documentation

`\{` and `\}` exist for exactly one reason: inside a string literal a bare `{` **opens an
interpolation**, so an author who wants a literal brace needs a way to say so.
`"Use \{expr\} syntax"` is the string `Use {expr} syntax` (see `string-interpolation.md`).

That reason does not reach the other two quoted forms. A **character literal** cannot interpolate —
`/specs/character-type.md` lists its escapes as `\n \t \\ \'`, with no brace — and a **byte string
literal** (`b"…"`) does not interpolate either. In both, `{` is an ordinary character that needs no
rescuing, so `\{` and `\}` name no known escape and are refused.

### Syntax

```text
let s = "Use \{expr\} syntax"   // "Use {expr} syntax" — the escape's one home
let c = '{'                      // a brace character needs NO escape here
let b = b"{"                     // nor here
```

The other self-escapes are unaffected and remain available in every form: `\\`, `\'`, `\"`.

## Tests

<!-- test: string-literal-decodes-both-braces -->
### A string literal decodes `\{` and `\}` to the bare braces

The positive control for the whole rule, and the case the narrowing must not touch: the one literal
form that interpolates is the one form where the brace escapes mean something.

```maxon
function main() returns ExitCode
	let s = "Use \{expr\} syntax"
	print("[{s}] {s.byteLength()}\n")
	print("[\{] [\}]\n")
	return 0
end 'main'
```
```stdout
[Use {expr} syntax] 17
[{] [}]
```

<!-- test: open-brace-escape-refused-in-character-literal -->
### `'\{'` is not an escape in a character literal

MEASURED 2026-08-26: shv2 accepted this and produced the character `{`, because one shared escape
table served every quoted body and had no way to state a per-kind fact (D14). The bootstrap oracle
refuses it — `error E1004: Invalid escape sequence '\{' in character literal`.

The binding is USED on purpose. Left unused, the pre-fix compiler — which accepted the escape — failed
this case with `E3012: unused variable`, a diagnostic that names nothing about braces and would have
masked the subject if the rule ever regressed. Used, the pre-fix compiler compiles and RUNS, so the
case's red is the behaviour under test: a character `{` where there should have been a refusal.

```maxon
function main() returns ExitCode
	let c = '\{'
	print("[{c}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2016: <fragment>:3:10: invalid character literal: unknown escape sequence '\{'
```

<!-- test: close-brace-escape-refused-in-character-literal -->
### `'\}'` is not an escape in a character literal either

The closing brace rides the same table row as the opening one, so it has to be pinned with it —
otherwise a later edit could restore half the rule and stay green.

```maxon
function main() returns ExitCode
	let c = '\}'
	print("[{c}]\n")
	return 0
end 'main'
```
```maxoncstderr
error E2016: <fragment>:3:10: invalid character literal: unknown escape sequence '\}'
```

<!-- test: brace-escape-refused-in-byte-string -->
### `\{` is not an escape in a byte string literal

The second arrival of the same defect, and the one D14's row did not name: a `b"…"` blob does not
interpolate, so it has no more use for a brace escape than a character literal does. Before the fix
`b"a\{b"` decoded to the three bytes `a { b`; the bootstrap refuses it with `error E1004: Invalid
escape sequence '\{' in byte string literal`.

```maxon
function main() returns ExitCode
	let b = b"a\{b"
	print("{b.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:10: Unsupported: the escape sequence '\{' in a byte string literal is not a recognized escape
```

<!-- test: unescaped-brace-is-an-ordinary-character -->
### A bare `{` needs no escape where nothing interpolates

The other half of the rule, and the reason refusing the escape costs an author nothing: the brace the
escape would have produced is already writable directly in both forms.

```maxon
function main() returns ExitCode
	let open = '{'
	let close = '}'
	let blob = b"a{b}c"
	print("[{open}{close}] {blob.count()}\n")
	return 0
end 'main'
```
```stdout
[{}] 5
```
