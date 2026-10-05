---
feature: test-declaration
status: experimental
keywords: [test, testing, unit test, TestFailure, contextual keyword]
category: infrastructure
---

# Test Declaration

## Documentation

### Declaring a test

A `test` declaration is a top-level declaration, parallel to `function`. It names itself with a
quoted prose name rather than an identifier:

```text
test 'adds two numbers'
	Expect.equal(add(2, 2), expected: 4)
end 'adds two numbers'
```

The name is a character literal and may contain anything except a `'` — spaces, digits and
punctuation are all fine. The `end` label must repeat it exactly, as with every other block.

A `test` takes **no parameters** and **no `returns`**. There is nothing to get wrong, which is the
point: a malformed test is a parse error rather than a test that silently passes.

### `test` is a contextual keyword

`test` is recognised as a declaration opener only where a declaration may start *and* the next
token is the test's quoted name. Everywhere else it is an ordinary identifier, so `for test in
tests`, `let test = ...` and `test.name` keep working. Both halves of that rule are needed:
`match test 'check'` is also an identifier followed by a character literal, and only the
declaration position tells the two apart.

### Implied `throws TestFailure`

Every `test` implicitly declares `throws TestFailure` (`stdlib/Testing.maxon`). Nobody writes the
clause, and it cannot be written.

This is what lets an assertion's failure leave the test: an assertion throws `TestFailure`, and a
test body needs no `try` on it — every throwing call, interface call and `await` in a test body
gets an implied handler that propagates a `TestFailure` and reports any other error before failing
the test (`specs/test-uncaught-throw.md`). It is also what lets a test body use a bare `try` with
no `otherwise` — outside a throwing function that is an error.

### Tests live in `*.maxtest` files

A `test` declaration is legal only in a file whose name ends in `.maxtest`. One rule, one
place: which declarations a build carries is answerable from the file list alone.

### Two names

A prose name cannot be a symbol — it reaches name mangling, the executable's symbol table, the
`.mxdbg` sidecar and panic stack traces. So a test compiles to an ordinary function carrying
both: a mangled `Name` of `<namespace>.__test_<sanitized>`, where every character outside
`[A-Za-z0-9_]` becomes `_`, and a `DisplayName` holding the prose verbatim.

Because the mangled name is what reaches the symbol table, two tests in one file whose names
sanitize alike are refused, naming both.

## Tests

⚠ **NOTHING HERE PINS THE EMITTED CODE; `--emit-ir` RENDERS IT.** The spec parser has no
`RequiredIR` arm, so such a block would be read by nobody while reading as coverage, and
`SpecParser.isUnimplementedFenceOpen` refuses the fence rather than walking past it.

<!-- test: basic -->
```maxon
// --- file: example.maxtest
test 'adds two numbers'
	let sum = 2 + 2
	print("sum is {sum}")
end 'adds two numbers'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: multi-word-name-round-trip -->
A prose name may contain spaces, digits and punctuation; `end` must repeat it verbatim.
```maxon
// --- file: example.maxtest
test 'rejects a negative index (regression, issue #42)'
	print("ran")
end 'rejects a negative index (regression, issue #42)'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.mismatched-end-label -->
```maxon
// --- file: example.maxtest
test 'adds two numbers'
	print("ran")
end 'adds three numbers'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2008: specs/test-declaration/error.mismatched-end-label.maxon:5:1: Mismatched end label: expected 'adds two numbers', got 'adds three numbers'
```

<!-- test: implied-throws-accepts-bare-try -->
A bare `try` with no `otherwise` compiles inside a test body. Outside a function declaring
`throws` that is E2001, so this is positive evidence of the implied `throws TestFailure`.
```maxon
// --- file: assertions.maxon
export function assertTrue(ok bool) throws TestFailure
	if not ok 'bad'
		throw TestFailure.assertion
	end 'bad'
end 'assertTrue'

// --- file: example.maxtest
test 'a bare try needs no otherwise'
	try assertTrue(true)
end 'a bare try needs no otherwise'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-bare-assertion-compiles -->
A bare assertion in a test body compiles, because the test body is an implied `try`. The assertion's
`TestFailure` is the test's own error, so it propagates plainly, with no report.
```maxon
// --- file: assertions.maxon
export function assertTrue(ok bool) throws TestFailure
	if not ok 'bad'
		throw TestFailure.assertion
	end 'bad'
end 'assertTrue'

// --- file: example.maxtest
test 'forgets its try'
	assertTrue(true)
end 'forgets its try'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.rejects-parameters -->
```maxon
// --- file: example.maxtest
test 'takes an argument'(value int)
	print("ran")
end 'takes an argument'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/test-declaration/error.rejects-parameters.maxon:3:25: Expected newline after block label, got '('
```

<!-- test: error.rejects-returns -->
```maxon
// --- file: example.maxtest
test 'returns something' returns int
	return 1
end 'returns something'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/test-declaration/error.rejects-returns.maxon:3:26: Expected newline after block label, got 'returns'
```

<!-- test: error.outside-test-file -->
```maxon
// --- file: regular.maxon
test 'not in a test file'
	print("ran")
end 'not in a test file'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2058: specs/test-declaration/error.outside-test-file.maxon:3:1: a 'test' declaration is only allowed in a file whose name ends in '.maxtest'; rename 'regular.maxon' to 'regular.maxtest', or move this declaration into one
```

<!-- test: error.duplicate-sanitized-name -->
Two prose names that sanitize to the same symbol are refused, naming both.
```maxon
// --- file: example.maxtest
test 'adds two'
	print("first")
end 'adds two'

test 'adds-two'
	print("second")
end 'adds-two'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3107: specs/test-declaration/error.duplicate-sanitized-name.maxon:7:6: duplicate test name: 'adds two' and 'adds-two' both compile to '__test_adds_two'
```

<!-- test: error.empty-name -->
```maxon
// --- file: example.maxtest
test ''
	print("ran")
end ''

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2059: specs/test-declaration/error.empty-name.maxon:3:6: a 'test' declaration's name cannot be empty
```

<!-- test: test-stays-an-ordinary-identifier -->
The regression guard for the contextual keyword. Every shape here uses `test` as a plain
identifier, including `match test 'check'` — an identifier followed by a character literal,
which is what the declaration looks like everywhere except at declaration position.
```maxon
typealias Count = int(0 to 100)

enum Status
	ready
	busy
end 'Status'

function classify(test Status) returns Count
	match test 'check'
		ready then return 20
		busy then return 1
	end 'check'
end 'classify'

function main() returns ExitCode
	var total = 0 as Count
	let tests = [1, 2, 3]

	for test in tests 'loop'
		total = total + test
	end 'loop'

	let test = Status.ready

	if test == Status.ready 'ready'
		total = total + classify(test)
	end 'ready'

	return total
end 'main'
```
```exitcode
26
```

<!-- test: survives-dead-function-elimination -->
Nothing in the program calls a test, so dead-function elimination would drop it. Tests are
roots instead.

⚠⚠ **THE EXIT CODE CANNOT SEE WHETHER THE TEST SURVIVED.** `main` returns 0 whether it did or
not. The emitted code (see the note above) renders this program's own functions, and is where
`__test_is_kept_alive` shows under its mangled name.

⚠ `ExitCode` is SHADOWED: rendering the emitted code shows every type in the program's
signature, and the stdlib's `ExitCode` is host-width —
`u32` under `int(0 to u32.max)` on Windows, `u8` under `int(0 to 255)` on Linux, macOS and
wasi (`stdlib/Process.maxon`). A rendering naming one of them is a HOST fact, so Windows would read
`-> u32` where arm64-macos reads `-> u8` from an identical compiler. A fixed range makes every lane's
emitted code say the same thing. Do not "simplify" this back to the stdlib alias.
```maxon
// --- file: kept.maxtest
test 'is kept alive'
	throw TestFailure.assertion
end 'is kept alive'

// --- file: main.maxon
typealias ExitCode = int(0 to 125)

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: namespace-qualified-name -->
A test in a subdirectory takes that directory's namespace, exactly as a function does.
```maxon
// --- file: suite/deep.maxtest
test 'lives in a subdirectory'
	print("ran")
end 'lives in a subdirectory'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
