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
`specs/character-type.md` lists its escapes as `\n \t \\ \'`, with no brace — and a **byte string
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

Which escapes a quoted body accepts is a per-kind fact: an escape table shared by every quoted body
cannot state it, and would accept this as the character `{`. The compiler refuses it (E2016).

The binding is USED on purpose. Left unused, a compiler that accepted the escape would fail this case
with `E3012: unused variable`, a diagnostic that names nothing about braces and would mask the subject
if the rule regressed. Used, such a compiler compiles and RUNS, so the case's red is the behaviour
under test: a character `{` where there should be a refusal.

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

The same rule holds for a byte string: a `b"…"` blob does not interpolate, so it has no more use for a
brace escape than a character literal does. `b"a\{b"` is refused rather than decoded to the three bytes
`a { b`.

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
