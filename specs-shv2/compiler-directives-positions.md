---
feature: compiler-directives-positions
status: stable
keywords: directive, if, else, endif, conditional, preprocessor, position
category: language
---
# Compiler directive positions

## Documentation

`specs/compiler-directives.md` declares that `#if` / `#else` / `#endif` are valid at top level,
inside a `type`, `enum`, `union`, `interface` or `extension` body, and inside a function body — but
its five cases are **all statement-position**. Every other position had zero coverage. This file
covers them, plus the condition grammar's precedence and the two failure modes shv2 refuses.

**Every expected value here was ORACLE-VERIFIED against the C# bootstrap before it was written down**,
except where a case below says otherwise and gives the reason. They are differential cases, not
invented ones.

### Three places shv2 deliberately differs from the bootstrap

1. **`enum` and `union` bodies.** The bootstrap refuses a directive there
   (`E2010: Expected identifier but got '#if'`, measured for both keywords). shv2 accepts it, because
   `specs/compiler-directives.md:31-34` lists both as legal positions and `maxon-selfhosted` wires
   both (`Parser.maxon:5768`, `:5975`). In shv2 the two share one parse loop, so it is a single arm.
   No spec anywhere asserts the refusal.
2. **Parentheses in a condition.** The bootstrap refuses them outright — even `#if (testing(false))`
   is `E2010: Expected identifier but got '('` — although `specs/compiler-directives.md:27` documents
   "`and`, `or`, `not`, plus parentheses for grouping" and `maxon-selfhosted` implements them
   (`Parser.maxon:17889-17893`). shv2 follows the documentation and v1.
3. **Structural imbalance is REFUSED (E2063).** An unterminated `#if` and a stray `#endif` both
   compile clean on the bootstrap (measured: exit 40 and exit 41), and v1 accepts them just as
   quietly. Silently swallowing the rest of a file is a wrong-answer generator, so shv2 reports.

## Tests

<!-- test: directives.top-level-branches -->
Top level, between declarations — the position `stdlib/FilePath.maxon:3` needs. Both arms declare
`pick`; only the live one exists, so this is not a duplicate definition.
```maxon
#if testing(false)
	function pick() returns ExitCode
		return 21
	end 'pick'
#else
	function pick() returns ExitCode
		return 99
	end 'pick'
#endif

function main() returns ExitCode
	return pick()
end 'main'
```
```exitcode
21
```

<!-- test: directives.top-level-nested -->
Nesting at TOP LEVEL. The corpus nests only inside a function body, where the enclosing construct
already brackets the region; here the inner `#if` has nothing but the outer one to balance against.
```maxon
#if testing(false)
	#if testing(true)
		function pick() returns ExitCode
			return 98
		end 'pick'
	#else
		function pick() returns ExitCode
			return 22
		end 'pick'
	#endif
#else
	function pick() returns ExitCode
		return 97
	end 'pick'
#endif

function main() returns ExitCode
	return pick()
end 'main'
```
```exitcode
22
```

<!-- test: directives.type-body-static-function -->
A `type` body, around a `static function` with no `#else` — the exact shape of
`stdlib/FilePath.maxon:49-68`.
```maxon
type Holder
	#if testing(false)
		static function value() returns ExitCode
			return 23
		end 'value'
	#endif
end 'Holder'

function main() returns ExitCode
	return Holder.value()
end 'main'
```
```exitcode
23
```

<!-- test: directives.type-body-field-pair -->
⭐ **A `#if`-gated FIELD PAIR — the case that decided where conditional compilation lives in shv2.**

A struct's fields are recorded by the whole-program declaration SWEEP
(`Parser.recordScannedType`), which is a raw token walk and not the parser's. Had directives been
taught to the parse loops alone, the sweep would have recorded BOTH `first` and `unwanted`, laying
`second` at the wrong offset — with no diagnostic anywhere. `32` is only reachable if exactly one
arm became a field.
```maxon
type Box
	#if testing(false)
		export let first as ExitCode
	#else
		export let unwanted as ExitCode
	#endif
	export let second as ExitCode

	export static function create(second ExitCode) returns Box
		return Box{first: 2, second: second}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(32)
	return b.second
end 'main'
```
```exitcode
32
```

<!-- test: directives.same-name-in-both-branches -->
⭐ **The `stdlib/FilePath.maxon:3-11` shape: one NAME declared in both arms, then used.**

The sweep records top-level bindings FIRST-WINS and the parse reports `E3006` when the winner's
position is not the one it dispatched. With the sweep reading both arms, the live declaration on any
target whose branch is not first would be reported as a duplicate of itself — a correct program
rejected. Reaching `31` at all is the assertion.
```maxon
#if testing(false)
	let marker = 31
#else
	let marker = 91
#endif

function main() returns ExitCode
	return marker
end 'main'
```
```exitcode
31
```

<!-- test: directives.interface-body-requirement -->
An `interface` body, around a method requirement. The conforming type supplies it either way, so the
observable is that the interface declaration parses and conformance still checks.
```maxon
interface Shape
	function area() returns ExitCode
	#if testing(false)
		function perimeter() returns ExitCode
	#endif
end 'Shape'

type Square implements Shape
	export let side as ExitCode

	export static function create(side ExitCode) returns Square
		return Square{side: side}
	end 'create'

	export function area() returns ExitCode
		return side
	end 'area'

	export function perimeter() returns ExitCode
		return 29
	end 'perimeter'
end 'Square'

function main() returns ExitCode
	let s = Square.create(4)
	return s.perimeter()
end 'main'
```
```exitcode
29
```

<!-- test: directives.enum-body-cases -->
An `enum` body. ⚠ **NOT oracle-verified — the bootstrap REFUSES this position**
(`E2010: Expected identifier but got '#if'`, measured). shv2 follows
`specs/compiler-directives.md:31-34` and `maxon-selfhosted/Compiler/Parser.maxon:5768`. See
divergence 1 above.

The `match` is what makes it an assertion rather than a smoke test: shv2 requires an exhaustive
match, so if the dead arm's `rejected` had also been recorded this would not compile.
```maxon
enum Mode
	#if testing(false)
		chosen
	#else
		rejected
	#endif
	steady
end 'Mode'

function main() returns ExitCode
	return match Mode.chosen 'm'
		chosen gives 34
		steady gives 90
	end 'm'
end 'main'
```
```exitcode
34
```

<!-- test: directives.union-body-cases -->
A `union` body — its own case because it is its own keyword, even though shv2 parses both through
one loop. ⚠ **NOT oracle-verified for `directives.enum-body-cases`'s reason**: the bootstrap refuses
`#if` here too, measured separately (`E2010: Expected identifier but got '#if'`).
```maxon
union Reading
	#if testing(false)
		settled
	#else
		discarded
	#endif
	pending
end 'Reading'

function main() returns ExitCode
	return match Reading.settled 'r'
		settled gives 35
		pending gives 90
	end 'r'
end 'main'
```
```exitcode
35
```

<!-- test: directives.dead-branch-unparsed-at-top-level -->
A dead branch at TOP LEVEL holding a call to a function that does not exist. The corpus proves
token-level skipping inside a function body; this proves it where declarations live, which is a
different walk.
```maxon
#if testing(true)
	function ghost() returns ExitCode
		return no_such_function_anywhere()
	end 'ghost'
#endif

function main() returns ExitCode
	return 30
end 'main'
```
```exitcode
30
```

<!-- test: directives.dead-branch-unparsed-in-type-body -->
The same, inside a `type` body — the third walk, and the one the declaration sweep descends into
(`recordScannedType`).
```maxon
type Holder
	#if testing(true)
		static function ghost() returns ExitCode
			return no_such_function_anywhere()
		end 'ghost'
	#endif

	export static function value() returns ExitCode
		return 33
	end 'value'
end 'Holder'

function main() returns ExitCode
	return Holder.value()
end 'main'
```
```exitcode
33
```

<!-- test: directives.unknown-os-argument-is-false -->
`os(Plan9)` — an unknown ARGUMENT to a KNOWN predicate is silently FALSE, not an error. That is what
lets portable source name a platform this compiler does not target. Contrast
`error.unknown-predicate` below, where the FUNCTION is unknown.
```maxon
function main() returns ExitCode
	#if os(Plan9)
		return 96
	#else
		return 25
	#endif
end 'main'
```
```exitcode
25
```

<!-- test: directives.precedence-and-binds-tighter -->
`A or B and C` parses as `A or (B and C)`, not `(A or B) and C`. With A true and C false the two
readings give different answers, so the exit code pins the precedence rather than merely exercising
it: `and`-tighter gives `true or (false and false)` = true = 27, `or`-tighter would give
`(true or false) and false` = false = 95.
```maxon
function main() returns ExitCode
	#if testing(false) or testing(true) and testing(true)
		return 27
	#else
		return 95
	#endif
end 'main'
```
```exitcode
27
```

<!-- test: directives.parens-override-precedence -->
The same three operands, parenthesized the other way, giving the opposite answer — which is what
shows the parentheses are read rather than ignored. ⚠ **NOT oracle-verified: the bootstrap refuses
parentheses entirely**, though the documentation promises them and v1 implements them. See
divergence 2 above.
```maxon
function main() returns ExitCode
	#if (testing(false) or testing(true)) and testing(true)
		return 94
	#else
		return 28
	#endif
end 'main'
```
```exitcode
28
```

<!-- test: error.unknown-predicate -->
An unknown PREDICATE NAME is a hard error. The message is the bootstrap's, word for word — it
reports the same text under its own `E3005`; shv2 gives it a parser-band code of its own.
```maxon
function main() returns ExitCode
	#if wibble(true)
		return 1
	#endif
	return 0
end 'main'
```
```maxoncstderr
error E2064: specs/fragments/compiler-directives-positions/error.unknown-predicate.test:3:6: Unknown conditional compilation function 'wibble'. Expected 'os', 'arch', 'testing', 'rcSanitize', or 'leakReport'.
```

<!-- test: error.unterminated-if -->
⚠ **A DELIBERATE DIVERGENCE FROM BOTH REFERENCES.** The bootstrap compiles this and exits 40;
v1 returns quietly at EOF. An unterminated `#if` silently swallows every declaration after it, and
the symptom surfaces as an unrelated "undefined" elsewhere — so shv2 reports it at the `#if` that has
no partner.
```maxon
function main() returns ExitCode
#if testing(false)
	return 40
end 'main'
```
```maxoncstderr
error E2063: specs/fragments/compiler-directives-positions/error.unterminated-if.test:3:1: Unterminated '#if' -- the region is still open at end of file. Every '#if' needs a matching '#endif'
```

<!-- test: error.stray-endif -->
The other half of the same divergence: an `#endif` closing nothing. The bootstrap compiles this and
exits 41.
```maxon
function main() returns ExitCode
	return 41
#endif
end 'main'
```
```maxoncstderr
error E2063: specs/fragments/compiler-directives-positions/error.stray-endif.test:4:1: '#endif' has no matching '#if'
```

<!-- test: error.orphan-else -->
And an `#else` with no `#if`. Its own case because it takes a different arm of the filter from
`#endif` — one flips a region, the other closes one, and only a test of each proves both guard.
```maxon
function main() returns ExitCode
	return 42
#else
	return 43
#endif
end 'main'
```
```maxoncstderr
error E2063: specs/fragments/compiler-directives-positions/error.orphan-else.test:4:1: '#else' has no matching '#if'
```
