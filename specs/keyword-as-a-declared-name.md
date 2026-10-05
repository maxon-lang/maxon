---
feature: keyword-as-a-declared-name
status: experimental
keywords: [parser, keyword, function, parameter, declaration, block-structure, token-scan]
category: parser-edge-cases
---

# A KEYWORD MAY BE A DECLARED NAME

## Documentation

`keyword-parameter-names.md` is the canonical spec for one half of this rule, stated mostly
cross-file; this file carries the mechanism in SINGLE-FILE form.

**The rule is one sentence: a declaration position that expects an identifier accepts a KEYWORD
TOKEN as a NAME.** A function's declared name and a parameter's name are both such positions —
nothing else may stand there — so a keyword written in one is a name and not the construct it
usually opens. `function from`, `function if` and
`function match` all compile, and to a byte-identical output size, because the name never reaches
codegen.

**`stdlib/FilePath.maxon` is the consumer** — `public static function from (path String) returns
FilePath throws FilePathError` — and `from` is the only keyword any `stdlib/` file declares as a
function name (4 sites, all `from`). `FilePath` gates `Process`/`File`/`Directory`.

⚠ **THE RULE IS POSITION-SENSITIVE, AND THAT IS THE WHOLE DESIGN.** A keyword accepted as a name
where a name is *already required* takes nothing away from the keyword: `from` still opens a
`Set from […]` construction, `while`/`for`/`match`/`if`/`else`/`end` still open and close blocks, and
`type` still declares a type. Every case below keeps the keyword's real syntactic role ALIVE in the
same program as the declaration that borrows its spelling, because a rule that only ever ran on a
program with no loops in it would not have been tested at all.

⚠⚠ **THE BLOCK-STRUCTURE KEYWORDS ARE THE DANGEROUS ONES, AND THEY ARE DANGEROUS IN THE TOKEN SCANS
RATHER THAN IN THE GRAMMAR.** `Parser.maxon` re-derives Maxon's block structure from the raw token
array (`opensBlockAt` / `closesBlockAt`, and every scan that counts depth through them). A
`function while(…)` declaration whose name is not recognized as a NAME reads as a block that never
closes, and a `function f(end Integer)` parameter reads as a closer that never opened — so the
file's whole block-extent index shifts, the declaration sweep's depth never returns to zero, and the
drift guards fire as a compiler PANIC on a correct program. `keywordIsAName` is where the exclusion
lives; this is the same fact `keyword-named-case-members.md` pins one layer down, for a keyword
spelled as an enum CASE name.

⚠ **A KEYWORD-NAMED PARAMETER MUST ALSO BE READABLE**, or the declaration is decoration: `return
type` inside `function identity(type Integer)` reads the parameter. Every keyword that has an
expression meaning of its own keeps it — `match`, `try`, `function`, `self`, `Self`, `sizeof`,
`true`, `false` in operand position, and `not`/`async`/`await` as prefix operators — so those may be
DECLARED as names but not read bare. `return
end` is a BARE return for the same reason (`end` terminates a value-less `return`).

## Tests

### The declaration positions

<!-- test: a-keyword-as-a-free-function-name -->
```maxon
typealias Integer = int(i64.min to i64.max)

function from(n Integer) returns Integer
	return n + 1
end 'from'

function main() returns ExitCode
	return from(41)
end 'main'
```
```exitcode
42
```

<!-- test: a-keyword-as-a-static-method-name -->
The shape `stdlib/FilePath.maxon` declares.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export let v as Integer

	static function from(n Integer) returns Self
		return Self{v: n}
	end 'from'
end 'Box'

function main() returns ExitCode
	let b = Box.from(42)
	return b.v
end 'main'
```
```exitcode
42
```

<!-- test: a-keyword-as-an-instance-method-name -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export let base as Integer

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	function to() returns Integer
		return base + 2
	end 'to'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(40)
	return c.to()
end 'main'
```
```exitcode
42
```

<!-- test: a-keyword-as-an-interface-METHOD-name -->
An interface REQUIREMENT is a declared name in the same sense a function's own name is, so it admits a
keyword too — a requirement a conforming type could not spell would be one no type could satisfy.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Scaled
	function to() returns Integer
end 'Scaled'

type Cell implements Scaled
	export let n as Integer

	static function from(n Integer) returns Self
		return Self{n: n}
	end 'from'

	function to() returns Integer
		return n * 3
	end 'to'
end 'Cell'

function main() returns ExitCode
	return Cell.from(14).to()
end 'main'
```
```exitcode
42
```

<!-- test: a-keyword-as-a-parameter-name-read-in-the-body -->
The single-file form of `keyword-parameter-names.md`'s `type-as-parameter-name-crossfile`.
```maxon
typealias Integer = int(i64.min to i64.max)

function identity(type Integer) returns Integer
	return type
end 'identity'

function main() returns ExitCode
	return identity(42)
end 'main'
```
```exitcode
42
```

<!-- test: four-different-keywords-as-parameter-names -->
`type`, `enum`, `union` and `interface` — the four `keyword-parameter-names.md` names — in ONE
parameter list, each read in the body. The second and later parameters are LABELLED with their own
keyword spelling, so the argument-label position accepts a keyword too; a parameter that cannot be
labelled cannot be passed.
```maxon
typealias Integer = int(i64.min to i64.max)

function combine(type Integer, enum Integer, union Integer, interface Integer) returns Integer
	return type + enum + union + interface
end 'combine'

function main() returns ExitCode
	return combine(1, enum: 2, union: 4, interface: 35)
end 'main'
```
```exitcode
42
```

### The block-structure keywords, beside their real role

<!-- test: block-keywords-as-function-names-beside-real-blocks -->
`while`, `for`, `end`, `match` and `if` are declared as function NAMES in a program that also runs a
real `while` loop, a real `for` loop, a real `if`/`else` chain and a real `match` — so a token scan
that read any of those names as block structure would mis-predict a construct's `end` and take the
compiler down in `assertScanAligned`. Three of them are declared as STATIC METHODS and called
`Ops.while(i)`, where the `.` before the keyword is what lets the scans exclude it; a fourth is a
top-level free function (a different scan context — the depth-counting declaration sweep) declared but
not called, because a bare `while(…)`/`end(…)`/`for(…)`/`match(…)` is the refused shape the last case
in this file pins. A free `if` IS callable bare, because `if`'s own position test already excludes it.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Grade
	low
	high
end 'Grade'

type Ops
	static function while(n Integer) returns Integer
		return n * 2
	end 'while'

	static function for(n Integer) returns Integer
		return n + 3
	end 'for'

	static function end(n Integer) returns Integer
		return n - 1
	end 'end'
end 'Ops'

function match(n Integer) returns Integer
	return n + 1
end 'match'

function if(n Integer) returns Integer
	if n > 4 'big'
		return 2
	end 'big'
	return 1
end 'if'

function grade(n Integer) returns Grade
	if n > 1 'high'
		return Grade.high
	end 'high'
	return Grade.low
end 'grade'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 3 'count'
		total = total + Ops.while(i)
		i = i + 1
	end 'count'

	for j in 0 upto 3 'walk'
		total = total + Ops.for(j)
	end 'walk'

	if total > 0 'positive'
		total = total + Ops.end(10)
	end 'positive' else 'negative'
		total = total - 1
	end 'negative'

	let bonus = match grade(if(5)) 'pick'
		low gives 5
		high gives 15
	end 'pick'

	return total + bonus
end 'main'
```
```exitcode
42
```

<!-- test: block-keywords-as-parameter-names-beside-real-blocks -->
The same keywords in the PARAMETER position, which is the shape whose miscount is a spurious closer
rather than a spurious opener, and LABELLED at the call site, which is the shape whose miscount is a
spurious opener. The body of the function that declares them runs a real `while` and a real `if`, and
its caller runs a real `for`. The two parameters that are READ are the non-block keywords `from` and
`to`; the case below pins why a bare read of the other three is refused instead.

⭐ **IT PINS A SEMANTIC ERROR, AND THAT IS WHAT MAKES IT STILL A PARSER CASE.** `while`, `for` and `match`
are unread by construction — the whole point is that they occupy the parameter position — and an unread
parameter is `E3012` (see `unused-parameters`), which no spelling of these three can avoid: `_` would
delete the very thing under test. But E3012 is raised in the SEMANTIC stage, so reaching it is proof the
token stream was counted correctly and the declaration PARSED — a miscount would produce a spurious closer
and a parse error instead, which is the regression this case exists to catch. The capability is
tested one stage before any exit code could test it: `E3012 … unused variable: 'while'`,
at the declared name's own position.
```maxon
typealias Integer = int(i64.min to i64.max)

function weigh(while Integer, for Integer, match Integer, from Integer, to Integer) returns Integer
	var acc = 0
	var k = 0
	while k < 3 'spin'
		acc = acc + from
		k = k + 1
	end 'spin'

	if acc > 2 'over'
		acc = acc + to
	end 'over'

	return acc
end 'weigh'

function main() returns ExitCode
	var total = 0
	for i in 1 upto 4 'walk'
		total = total + weigh(i, for: 3, match: 4, from: 2, to: 8)
	end 'walk'
	return total
end 'main'
```
```maxoncstderr
error E3012: specs/keyword-as-a-declared-name/block-keywords-as-parameter-names-beside-real-blocks.maxon:4:16: unused variable: 'while'
```

<!-- test: error.a-bare-read-of-a-block-keyword-parameter -->
⛔ **THE ONE SHAPE OF THIS RULE THE COMPILER REFUSES, and it is refused rather than mis-compiled.** A bare
read of a binding named `while`/`match`/`for`/`end` is a NAME to the expression parser and BLOCK
STRUCTURE to the token scans, whose verdict for those four does not depend on position. The scans see
raw tokens and have no scope, so they cannot know the name is bound; accepted, the program panics in
`assertScanAligned`. `if` and `else` are NOT refused — their position tests already answer no for an
operand — which is why the case above reads `from` and `to` freely.

⚠ **`otherwise` IS refused, but only in ONE position, and NOT because it was listed** — the refusal set is
DERIVED by asking `opensBlockAt`/`closesBlockAt` at the cursor, so it answers per program rather than per
keyword. A bare `otherwise` read as a whole block condition puts the header's label straight after it
(`while otherwise 'loop'`), which is `otherwiseOpensBlock`'s labelled-handler form — so that one program
is refused with the same E2015, while `acc + otherwise` is not. The derivation is what makes the set
right without anyone maintaining it; no fixed list of exempt keywords summarises it.
```maxon
typealias Integer = int(i64.min to i64.max)

function weigh(while Integer) returns Integer
	return 1 + while
end 'weigh'

function main() returns ExitCode
	return weigh(41)
end 'main'
```
```maxoncstderr
error E2015: specs/keyword-as-a-declared-name/error.a-bare-read-of-a-block-keyword-parameter.maxon:5:13: Unsupported: reading 'while' as a value here — it is a legal DECLARED name, but the token scans that re-derive Maxon's block structure read a bare `while` in this position as block structure and have no scope to tell them otherwise. Pass it under a different name, or read a differently-named binding
```

### Two hazards outside the grammar

<!-- test: a-keyword-named-parameter-that-a-CONSTRUCTOR-CONSUMES -->
⚠⚠ **A KEYWORD-NAMED PARAMETER IS STILL A PARAMETER TO THE OWNERSHIP MACHINERY, and the token scans
that decide "does this constructor CONSUME parameter k?" read a reference to one by TOKEN KIND.** A scan
that does not know a keyword may be a name records no consume for `Self{value: from}`: the caller keeps
its `+1`, the box owns the same `String`, and both drop it — **SIGSEGV, exit 139** — so renaming the
parameter to anything that is not a keyword is the entire difference between a crash and the right
answer. The monomorphic twin does not crash but emits a spurious `__mm_alloc` + `__str_copy` per
construction, which is a wrong COST driven by a name's spelling.
```maxon
type Box uses T
	export var value as T

	static function create(from T) returns Self
		return Self{value: from}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	let n = 7
	let msg = "hello{n}"
	let b = StrBox.create(msg)
	return (b.value.byteLength() * 7) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-keyword-case-arm-whose-value-is-a-PARENTHESIZED-match -->
⚠⚠ **`function gives (…)` SPELLS `function <name> (` CHARACTER FOR CHARACTER**, because `function` is a
legal case NAME and `gives` is word-shaped. Taken at its word, a match ARM reads as a function declaration
and the parenthesized expression as its parameter list — and everything inside it, the nested `match` and
its own `end` included, would be recorded as a declared name. `closesBlockAt` would then stop seeing that
`end`, the inner arm loop would read it as one more case, and the compiler would refuse a legal program:
**`E3034 unknown enum case: 'end'`**, pointing at the closing `end`.

"No block structure may appear inside a parameter list" — the argument the recording walk rests on — is
therefore a CHECK (`parenGroupCanBeAParameterList`) rather than a claim: a block keyword may
stand in a parameter list only where a NAME may, so one anywhere else proves the group is an expression.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Kw
	function
	alpha
end 'Kw'

enum Pick
	one
	two
end 'Pick'

function classify(k Kw, p Pick) returns Integer
	return match k 'outer'
		function gives (match p 'inner'
			one gives 40
			two gives 2
		end 'inner')
		alpha gives 0
	end 'outer'
end 'classify'

function main() returns ExitCode
	return classify(Kw.function, p: Pick.one) + classify(Kw.function, p: Pick.two) + classify(Kw.alpha, p: Pick.one)
end 'main'
```
```exitcode
42
```

### Two scans that must ask the keyword-as-a-name predicate — each a compiler PANIC on a correct program otherwise

⭐⭐ **BOTH ARE THE SAME HAZARD: A SCAN THAT ASKS A RAW `TokenKind` INSTEAD OF THE ONE PREDICATE THAT
KNOWS A KEYWORD MAY BE A NAME.** Neither is in the grammar, and in both the widened DECLARATION position
is what makes a raw-token scan reachable. The cure in both places is to ask the predicate rather than
test the raw token, not to add a case to the raw test.

<!-- test: a-keyword-named-METHOD-CALL-ending-a-block-HEADER -->
⛔ **`opensBlockAt` asks the keyword-as-a-name exclusion ONCE, ahead of every arm, and the `otherwise`
arm is why.** `otherwiseOpensBlock`'s caught-error handler form is `otherwise ( <ident> ) <label>` — which a
keyword-named METHOD CALL at the end of a block header spells character for character, `.otherwise`
being a legal member name. An arm that skipped the exclusion would read `while Ops.otherwise(i) 'loop'`
as opening a second block nothing closes, and the extent scan would run past the loop's `end`:
`panic: parseWhileStatement: the token scan predicted the closing 'end' at token 76 but the parser closed
the last body at token 68`. The `if` form is the same. An arm decides whether this POSITION opens a
block, and whether the token is block structure AT ALL is not an arm's business.
```maxon
typealias Integer = int(i64.min to i64.max)

type Ops
	static function otherwise(n Integer) returns bool
		return n < 3
	end 'otherwise'
end 'Ops'

function main() returns ExitCode
	var i = 0
	while Ops.otherwise(i) 'loop'
		i = i + 1
	end 'loop'

	if Ops.otherwise(i) 'lbl'
		i = 99
	end 'lbl'

	return (i * 14) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: keyword-named-INTERFACE-REQUIREMENTS-among-others -->
⛔ **THE DECLARATION SWEEP MUST NOT CLOSE AN `interface` BODY ON A RAW `end`, WHICH IS A LEGAL
REQUIREMENT NAME.** `recordScannedInterface` must consume the whole declaration itself — its
bodiless signatures would otherwise trip the sweep's `function` arm and leave `depth` permanently one too
deep — and it finds its terminator through `closesBlockAt`. A terminator found by testing `TokenKind.end`
stops the walk ON a `function end(…)` requirement, mid-signature, and the requirements after it fall to
the outer loop and do exactly the damage the routine exists to prevent: every `depth == 0` gate (`type`,
`enum`, `interface`) stops firing for the rest of the file. `panic: requireConstructible: type Cell is
being parsed right now, but the declaration sweep never recorded it`.

⚠ **IT TAKES THREE REQUIREMENTS TO SEE, AND THAT IS THE INTERESTING PART.** With exactly ONE requirement
after the `end`-named one, the stray `function`'s `+1` cancels the interface's own `end` and the file
compiles — a green answer from a broken scan, off an arithmetic coincidence. A two-case probe would call
this clean.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Bounded
	function end(k Integer) returns Integer
	function while(k Integer) returns Integer
	function match(k Integer) returns Integer
end 'Bounded'

type Span implements Bounded
	export let lo as Integer

	static function from(k Integer) returns Self
		return Self{lo: k}
	end 'from'

	function end(k Integer) returns Integer
		return lo + k
	end 'end'

	function while(k Integer) returns Integer
		return k
	end 'while'

	function match(k Integer) returns Integer
		return k
	end 'match'
end 'Span'

function main() returns ExitCode
	let s = Span.from(40)
	return s.end(2) as ExitCode
end 'main'
```
```exitcode
42
```

### The keyword keeps its own role in the same program

<!-- test: a-function-named-from-beside-a-set-from-construction -->
`from` opens a `Set from […]` construction. Declaring a function named `from` in the same program
must not disturb it, and calling that function must not be read as one.
```maxon
typealias Integer = int(i64.min to i64.max)

function from(n Integer) returns Integer
	return n * 7
end 'from'

function main() returns ExitCode
	let s = Set from [10, 20, 30, 40, 50, 60]
	return from(s.count() as Integer)
end 'main'
```
```exitcode
42
```

<!-- test: a-parameter-named-type-beside-a-real-type-declaration -->
`type` declares a type. A parameter named `type` in the same program must not be read as one — a
token pre-scanner that read `type StdType` inside a parameter list as a top-level `type StdType`
declaration would shadow the real one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Holder'

function unwrap(type Holder) returns Integer
	return type.n
end 'unwrap'

function main() returns ExitCode
	return unwrap(Holder.create(42))
end 'main'
```
```exitcode
42
```

### A keyword-named binding OPENING a block header

<!-- test: a-keyword-named-parameter-AS-THE-FIRST-TOKEN-OF-A-CONDITION -->
⛔⛔ **A BLOCK HEADER'S CONDITION MAY *BEGIN* WITH A KEYWORD-NAMED BINDING, AND THAT PUTS AN ARM
SEPARATOR IMMEDIATELY AFTER THE BLOCK KEYWORD.** A keyword followed by `gives`/`then`/`to`/`upto`/`or`
is not necessarily a match-arm case name, because a control keyword CAN be followed by a match-arm
separator in real control flow: a condition is an expression, an expression may start with a NAME, and
five of Maxon's separators are declarable as names. So `if to >= from 'forward'` is an `if` whose next
token is `to` — read as an arm, it would open no block, its `end` would close one that never opened, the
extent index would shift, and `assertScanAligned` would take the compiler down on a well-formed program.

⚠ **IT IS NOT A CONSTRUCTED CASE — `maxon-bin/Compiler/Targets/Shared/GlobalDataTable.maxon` declares
`function twosComplementDistance(to ByteOffset, from ByteOffset)`.** So the arm lookahead is asked only
where an arm can stand: at the top level of a `match` BODY, which the block-extent walk's own opener stack
knows and no bounded lookaround can see (`keywordIsANameAt`). The case below reads `to` first in an `if`,
in a `while` and as a `match` SCRUTINEE — the three headers whose next token is an expression.
```maxon
typealias Integer = int(i64.min to i64.max)

function twosComplementDistance(to Integer, from Integer) returns Integer
	if to >= from 'forward'
		return to - from
	end 'forward'

	return (from - to) * -1
end 'twosComplementDistance'

function span(to Integer, from Integer) returns Integer
	var acc = 0
	var k = from
	while to > k 'spin'
		acc = acc + 1
		k = k + 1
	end 'spin'

	return acc
end 'span'

function weight(to Integer) returns Integer
	return match to 'pick'
		1 gives 100
		2 gives 4
		default gives 0
	end 'pick'
end 'weight'

function main() returns ExitCode
	return (twosComplementDistance(50, from: 8) + span(4, from: 1) + weight(2)) as ExitCode
end 'main'
```
```exitcode
49
```

<!-- test: a-keyword-named-condition-BESIDE-keyword-named-match-arms -->
⭐⭐ **THE DISCRIMINATING CASE: both readings of the same lookahead, alive in one program.** The scoping
above is only right if the arm lookahead still FIRES where an arm really stands, and the two shapes are
spelled with the same two tokens. `end gives 4` inside the match body is a keyword-named case name whose
next token is a separator; `if to >= from 'forward'` outside it is a block header whose next token is
that same separator kind. Drop the scoping and the second panics; drop the lookahead and the first is a
closing `end` that ends the arm list early (`E2026 … not exhaustive`). Only a program holding both pins
which of the two the walk is answering.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Kw
	function
	end
	if
	other
end 'Kw'

function score(k Kw) returns Integer
	return match k 'classify'
		function gives 1
		end gives 4
		if gives 8
		other gives 16
	end 'classify'
end 'score'

function gap(to Integer, from Integer) returns Integer
	if to >= from 'forward'
		return to - from
	end 'forward'

	return 0
end 'gap'

function main() returns ExitCode
	return (score(Kw.end) + gap(46, from: 8)) as ExitCode
end 'main'
```
```exitcode
42
```

### A KEYWORD-NAMED CALL TARGET AFTER `try`

⛔⛔ **`parseTryCallReceiver`'s general arm must not ask `TokenKind.identifier`, WHICH A KEYWORD IS NOT.** A
function and a parameter may be DECLARED with a keyword, so `try from.get(k)` on a parameter named `from`
(`IR/CsrGraph.maxon`'s `buildCsr`) is an ordinary throwing method call — and an identifier test would refuse it as
`E2015 "try must be applied to a call … (got 'from')"` on well-formed source. This is
`keyword-as-a-declared-name.md`'s rule at one more door.

⭐⭐ **THE GENERAL ARM STANDS LAST RATHER THAN CARRYING AN EXCLUSION LIST, and the difference is the one
this file keeps paying for.** Standing any higher, it would claim `Self.check(v)` and `int.fromString(s)`
out from under the two arms that exist to serve them — the `Self` arm's own header says it cannot ride the
named arm, and a primitive static reaches `parseDottedPrimary` through neither. The alternative is
`tokenCanBeAName(…) and not at(selfType) and not primitiveStaticCallAt(…)`, an enumerated exclusion that
goes stale the next time a keyword-headed target earns an arm. **Ordering IS the rule in this router** — its
own header says the field-chain arm is "checked FIRST" for exactly this reason — so the general case goes
last and every specific one keeps its claim by standing above it.

<!-- test: a-try-target-may-be-a-keyword-named-receiver -->
All three shapes in one program, because the rule is about which arm claims which: a KEYWORD-named
receiver, a PRIMITIVE static, and a `Self.`-qualified static — the two that keep their claim because the
general arm stands below them.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

enum BoxError implements Error
	negative
end 'BoxError'

type Box
	export let n as Integer

	static function viaSelf(n Integer) returns Integer throws BoxError
		if n < 0 'bad'
			throw BoxError.negative
		end 'bad'
		return n + 1
	end 'viaSelf'

	static function checked(n Integer) returns Integer throws BoxError
		if n < 0 'bad'
			throw BoxError.negative
		end 'bad'

		return try Self.viaSelf(n) otherwise panic("self")
	end 'checked'
end 'Box'

function firstOf(from IntArray) returns Integer
	return try from.get(0) otherwise panic("oob")
end 'firstOf'

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	let a = firstOf(xs)
	let b = try int.fromString("5") otherwise panic("parse")
	let c = try Box.checked(1) otherwise panic("checked")
	print("a={a} b={b} c={c}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=7 b=5 c=2
```

<!-- test: error.a-bare-read-of-end-in-a-first-method-is-the-only-diagnostic -->
```maxon
// --- file: span.maxon
module type Span
	var start as SpanPos

	module static function create(start SpanPos) returns Span
		return Span{start: start}
	end 'create'

	module function endsBefore(position SpanPos, end SpanPos) returns bool
		return position < end and start < end
	end 'endsBefore'

	module function shifted(by SpanPos) returns Span
		let moved = Span.create(start + by)
		return moved
	end 'shifted'

	module function width(to SpanPos) returns SpanPos
		return to - start
	end 'width'
end 'Span'

// --- file: types.maxon
module typealias SpanPos = int(0 to 1000)

// --- file: main.maxon
function main() returns ExitCode
	let span = Span.create(1).shifted(2)

	if span.endsBefore(span.width(4), end: 5) 'found'
		return 0
	end 'found'

	return 1
end 'main'
```
```maxoncstderr
error E2015: specs/keyword-as-a-declared-name/error.a-bare-read-of-end-in-a-first-method-is-the-only-diagnostic.maxon:11:21: Unsupported: reading 'end' as a value here — it is a legal DECLARED name, but the token scans that re-derive Maxon's block structure read a bare `end` in this position as block structure and have no scope to tell them otherwise. Pass it under a different name, or read a differently-named binding
```
