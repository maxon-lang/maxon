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
	try Expect.equal(add(2, 2), expected: 4)
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

This is what makes a forgotten `try` a **compile** error: an assertion is a throwing call, so
omitting `try` is E3057 at build time rather than an assertion whose failure nothing observes.
It is also what lets a test body use a bare `try` with no `otherwise` — outside a throwing
function that is an error.

### Tests live in `*.test.maxon` files

A `test` declaration is legal only in a file whose name ends in `.test.maxon`. One rule, one
place: which declarations a build carries is answerable from the file list alone.

### Two names

A prose name cannot be a symbol — it reaches name mangling, the executable's symbol table, the
`.mxdbg` sidecar and panic stack traces. So a test compiles to an ordinary function carrying
both: a mangled `Name` of `<namespace>.__test_<sanitized>`, where every character outside
`[A-Za-z0-9_]` becomes `_`, and a `DisplayName` holding the prose verbatim.

Because the mangled name is what reaches the symbol table, two tests in one file whose names
sanitize alike are refused, naming both.

## Tests

⚠ **PORT NOTE.** The `/specs` original carries two `RequiredIR:<target>` blocks on
`survives-dead-function-elimination`, in v1's three-section dump format. Neither survives the
port: shv2's spec parser has no `RequiredIR` arm, so both would be read by nobody while reading
as coverage, and `SpecParser.isUnimplementedFenceOpen` refuses the fence rather than walking past
it. What pins the emitted code here is each case's MINTED FRAGMENT GOLDEN, which records what
THIS compiler emits rather than what v1 did. The `/specs` copy keeps its blocks.

<!-- test: basic -->
```maxon
// --- file: example.test.maxon
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
// --- file: example.test.maxon
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
// --- file: example.test.maxon
test 'adds two numbers'
	print("ran")
end 'adds three numbers'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2008: specs/fragments/test-declaration/error.mismatched-end-label.test:5:1: Mismatched end label: expected 'adds two numbers', got 'adds three numbers'
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

// --- file: example.test.maxon
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

<!-- test: error.implied-throws-forgotten-try -->
A forgotten `try` inside a test body is a compile error, not a test that cannot fail.
```maxon
// --- file: assertions.maxon
export function assertTrue(ok bool) throws TestFailure
	if not ok 'bad'
		throw TestFailure.assertion
	end 'bad'
end 'assertTrue'

// --- file: example.test.maxon
test 'forgets its try'
	assertTrue(true)
end 'forgets its try'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/test-declaration/error.implied-throws-forgotten-try.test:11:2: throwing function requires try: 'assertTrue'
```

<!-- test: error.rejects-parameters -->
```maxon
// --- file: example.test.maxon
test 'takes an argument'(value int)
	print("ran")
end 'takes an argument'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/test-declaration/error.rejects-parameters.test:3:25: Expected newline after block label, got '('
```

<!-- test: error.rejects-returns -->
```maxon
// --- file: example.test.maxon
test 'returns something' returns int
	return 1
end 'returns something'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/test-declaration/error.rejects-returns.test:3:26: Expected newline after block label, got 'returns'
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
error E2058: specs/fragments/test-declaration/error.outside-test-file.test:3:1: a 'test' declaration is only allowed in a file whose name ends in '.test.maxon'; rename 'regular.maxon' to 'regular.test.maxon', or move this declaration into one
```

<!-- test: error.duplicate-sanitized-name -->
Two prose names that sanitize to the same symbol are refused, naming both.
```maxon
// --- file: example.test.maxon
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
error E3107: specs/fragments/test-declaration/error.duplicate-sanitized-name.test:7:6: duplicate test name: 'adds two' and 'adds-two' both compile to '__test_adds_two'
```

<!-- test: error.empty-name -->
```maxon
// --- file: example.test.maxon
test ''
	print("ran")
end ''

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2059: specs/fragments/test-declaration/error.empty-name.test:3:6: a 'test' declaration's name cannot be empty
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

⚠⚠ **THE GOLDEN CANNOT MINT ITSELF HONEST — READ IT WHEN YOU MINT IT.** The pin here is the
fragment golden (see the PORT NOTE above), which renders this program's own functions and must
therefore name `__test_is_kept_alive` under its mangled name. But a golden minted while the
symbol was being DROPPED would record its absence and then compare equal for ever — a pin of the
bug rather than of the rule. Nothing in the runner can catch that, because a mint has nothing to
compare against by definition. The exit code below cannot catch it either: `main` returns 0
whether the test survived or not.

⚠ `ExitCode` is SHADOWED, and it has to be: rendering the emitted code makes every type in
the program's signature part of the assertion, and the stdlib's `ExitCode` is host-width —
`u32` under `int(0 to u32.max)` on Windows, `u8` under `int(0 to 255)` on Linux, macOS and
wasi (`stdlib/Process.maxon`). A golden naming one of them is a HOST fact recorded on every
lane that mints one, so the reference read `-> u32` and failed on arm64-macos against an
identical compiler. A fixed range makes every lane's golden say the same thing. Do not
"simplify" this back to the stdlib alias.
```maxon
// --- file: kept.test.maxon
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
// --- file: suite/deep.test.maxon
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
