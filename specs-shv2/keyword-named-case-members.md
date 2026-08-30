---
feature: keyword-named-case-members
status: experimental
keywords: [enum, union, keyword-case, block-structure, token-scan]
category: type-system
---

# A KEYWORD-named case READ AS A MEMBER (`Kw.end`, `Kw.while`, `Kw.match`, `Kw.for`)

## Documentation

`enums-simple.md`'s `keyword-as-case-name` pins that an enum case may be **spelled with a keyword**
(`enum TokenType { function return end if … }`) and that a `match` may write `end gives …` as an arm.
This file pins the OTHER half of the same fact, which that spec never exercises: **reading such a case
as a MEMBER** — `Kw.end`, `Kw.while` — in an ordinary expression.

The two halves are one rule stated from two sides. `Parser.maxon`'s block-extent token scans
(`opensBlockAt` / `closesBlockAt`, and every scan that counts depth through them) re-derive Maxon's
block structure from the raw token array, and a keyword that is a NAME here is not block structure.
Both shapes are now ONE predicate (`keywordIsAName`), because they are one fact:

- **A match-arm case name is a NAME** — the LOOKAHEAD (the next token is a match-arm separator
  `gives`/`then`/`to`/`upto`/`or`). That half already existed.
- **A match-arm case name CARRYING A PAYLOAD BINDING LIST is a NAME** — the SAME lookahead, taken PAST
  the list (`end(m) then …`, `while(a, b) gives …`). A one-token lookahead cannot see the separator at
  all, because the list sits between them. That half did NOT exist either, and both directions refused a
  program the oracle compiles and runs to 42:
  - `end(m) then …` ended the arm loop, so the match reported `E2026 … not exhaustive, missing: end,
    omega` — a **false rejection**;
  - `while(m) then …` opened a block nothing closed, and `assertScanAligned` took the compiler down.
- **A member name after a `.` is a NAME too** — the LOOKBEHIND. That half did NOT exist, and its
  absence was a **reachable compiler PANIC**, in both directions:
  - `Kw.end` was read as a **CLOSER**, so the scan predicted a construct's `end` too EARLY;
  - `Kw.while` / `Kw.match` / `Kw.for` were read as **OPENERS**, so it predicted one too LATE.

  Either way `assertScanAligned` fired — `parseIfStatement: the token scan predicted the closing 'end'
  at token 60 but the parser closed the last body at token 69`. Every block statement that runs a token
  scan was affected: `if`, `while`, `for`, and a `match`'s scrutinee and arm bodies.

⚠ **`Kw.if` and `Kw.else` never panicked, and pinning them is the point.** Neither is an unconditional
block marker — an `if` opens a block only at STATEMENT START (`ifBeginsStatement`), an `else` only after
a then-branch's `end` (`elseFollowsBlockEnd`) — and a member name satisfies neither. So they were already
excluded, **by a position test that has nothing to do with being a name.** They are pinned here because a
case that passes for an unrelated reason is exactly the case a later rung deletes as redundant, and
because the lookbehind now covers all seven uniformly.

⚠⚠ **`Kw.otherwise` WAS THE EXCEPTION, AND THIS FILE USED TO CLAIM OTHERWISE — the claim was FALSE and a
compiler PANIC lived behind it.** The reasoning was the same as for `if`/`else`: an `otherwise` opens a
block only in its two handler shapes (`otherwiseOpensBlock`), and a member name was said to satisfy
neither. But one of those shapes is `otherwise 'label'`, and a member read that ENDS A BLOCK HEADER is
followed by that header's own block label — so `while k == Kw.otherwise 'loop'` satisfies it exactly, in
the same way `match Kw.end 'm'` satisfies the labelled-closer shape below. Found by D8's independent
review, on the same day D8 made `.otherwise` reachable a second way (a keyword-named METHOD:
`while Ops.otherwise(i) 'loop'`, whose `( <ident> ) <label>` tail is the caught-error binding form).
`handler-case-member-spelling-a-labelled-otherwise-in-a-header` pins it. **The lesson is the file's own:
"excluded by an unrelated position test" is not an exclusion, and the three keywords whose arms did their
own thinking were the three worth doubting.**

⚠ **Two shapes are worse than the rest and each gets its own case, because in both a keyword member
spells a real piece of block syntax character for character:**

- `match Kw.end 'm'` — the scrutinee plus the match's own block label spell `end 'm'`, a labelled
  closing `end`;
- `… or Kw.end == Kw.end else 2` — a TERNARY whose condition ends in the member puts `end` immediately
  before the `else`, which is how a block `else` is told from a ternary one (`elseFollowsBlockEnd`). So
  the ternary read as opening a block, and the enclosing loop's scan ran past its `end`.

Nothing but the preceding `.` tells any of these apart, which is why the lookbehind belongs to the one
shared predicate and every scan that counts depth asks it rather than testing `TokenKind.end` itself.

⚠⚠ **THE PAYLOAD SHAPE'S SEPARATOR SET IS NARROWER THAN THE BARE SHAPE'S — `then`/`gives` ONLY — AND
WIDENING IT BREAKS THE STDLIB.** A **parenthesized condition** is legal Maxon, so a real `if`/`while`
header can be followed by `(`…`)` too, and what comes after that group is whatever the surrounding
expression needs: `if (driveByte >= 65 and driveByte <= 90) or (driveByte >= 97 and driveByte <= 122)
'isDrive'` (`stdlib/FilePath.maxon`) puts **`or`** immediately past it. `then` and `gives` are the only
two separators a payload-carrying arm can take AND the only two no real header can be followed by;
`to`/`upto` belong to scalar RANGE patterns, which carry no payload. `or` is the one accepted gap —
`end(m) or omega then …` still reads as a closer, and shv2 refuses a payload binding on an `or`-pattern
outright, so the program stays refused either way; the rung that lifts THAT restriction is the one that
must widen this set. `a-parenthesized-condition-with-or-past-the-group` below is the case that turns a
premature widening red.

⚠ **AND THE PAYLOAD SHAPE MAY ONLY EVER ANSWER *YES*.** `Kw.end(20, b: 22)` — CONSTRUCTING a
keyword-named case that carries a payload — has the `.` before the keyword AND the `(` after it, so a
payload test that returned its verdict outright would answer for the lookbehind and hide it. Measured
during this file's own review: it took `assertScanAligned` down on
`if tagOf(Kw.end(20, b: 22)) == 42 'ok'`. `constructing-a-payload-carrying-keyword-case` pins it.

⚠ **One case here was authored AFTER the fix and never ran red, deliberately:**
`closer-case-member-spelling-a-labelled-end-in-a-header` puts `Kw.end == Kw.end 'label'` in a `while`
AND an `if` header, so the header itself ends in the labelled-`end` shape. Its red is carried by the two
cases above it (a `while` condition and an `if` condition, both observed panicking), and it is here for
the reason `enum-union-method-receiver.md` states for its own: next rung, only a committed case still
runs.

⚠ **THE SIBLING-RECEIVER WALK COUNTS THE SAME DEPTH, AND ITS MISCOUNT IS A FALSE REJECTION.**
`ensureSiblingReceivers` (which resolves a bare `inner()` inside a method to `self.inner()`) walks a
type body through the same two predicates, and a `Kw.end` / `Kw.while` in a METHOD BODY moved its
delimiter. Both directions refuse a legal program, and each has a case below:

- `Kw.end` **ended the walk early** — the type's remaining methods were never registered, so a bare
  call to one reported `E3004 call to undefined function 'bonus'`;
- `Kw.while` **ran the walk past the type's own `end`** — a FREE function declared after the type was
  adopted as an instance sibling, so a bare call to it reported `E3004 … 'Holder.helper'`.

This is the same family the D1 review fixed for an enum's CASE LIST, reached from the other side: that
fix taught the walk that a member list is not block structure, and this one teaches it that a member
READ is not either.

⚠ **The DECLARATION SWEEP counts the same depth, and its miscount is silent rather than loud.**
`foldDeclaredSignaturesInto`'s walk gates its `let`/`var` arm on depth 0, so a `Kw.end` inside a
function body dropped the sweep's depth to 0 and every following LOCAL binding was recorded as a
TOP-LEVEL one. Measured before the fix: the answers happened to stay right (the real parse is
authoritative for what a binding IS, and an unreferenced global is eliminated), so this half had no
observable symptom at all — which is precisely why it goes through the same shared predicate as the
loud half rather than being left to be found later.

## Tests

<!-- test: closer-case-member-in-if-condition -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	if tagOf(Kw.end) == 2 'ok'
		return 20
	end 'ok'
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
20
```

<!-- test: closer-case-member-in-while-condition -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	var n = 0
	var acc = 0
	while n < tagOf(Kw.end) 'spin'
		acc = acc + 10
		n = n + 1
	end 'spin'
	return acc as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
20
```

<!-- test: closer-case-member-in-for-iterable -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	var acc = 0
	for i in 0 upto tagOf(Kw.end) 'each'
		acc = acc + 10 + i
	end 'each'
	return acc as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
21
```

<!-- test: closer-case-member-as-match-scrutinee -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function main() returns ExitCode
	match Kw.end 'm'
		alpha then return 11
		end then return 22
		omega then return 33
	end 'm'
end 'main'
```
```exitcode
22
```

<!-- test: closer-case-member-in-match-arm-body -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function pick(n Integer) returns Integer
	match n 'p'
		0 then return tagOf(Kw.end) * 21
		default then return 9
	end 'p'
end 'pick'

function main() returns ExitCode
	return pick(0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: closer-case-member-in-else-if-chain -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	if tagOf(Kw.end) == 1 'first'
		return 1
	end 'first' else if tagOf(Kw.end) == 2 'second'
		return 42
	end 'second' else 'rest'
		return 3
	end 'rest'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: closer-case-member-spelling-a-labelled-end-in-a-header -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function main() returns ExitCode
	var acc = 0
	var i = 0
	while Kw.end == Kw.end 'spin'
		if Kw.end == Kw.end 'inner'
			acc = acc + 21
		end 'inner'
		i = i + 1
		if i == 2 'done'
			break
		end 'done'
	end 'spin'
	return acc as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: handler-case-member-spelling-a-labelled-otherwise-in-a-header -->
⚠⚠ **THE THIRD SHAPE, AND IT WAS A LIVE PANIC UNTIL D8's REVIEW** — the exact twin of the `end 'm'` case
above, for the OTHER keyword that has a labelled block form. A member read `Kw.otherwise` at the end of a
block header puts `otherwise` immediately before the construct's own block label, which is
`otherwiseOpensBlock`'s `otherwise 'label'` form character for character — so the header opened a second
block nothing closed and `assertScanAligned` took the compiler down. It is why the claim above that a
member name "satisfies none of those" position tests was WRONG for `otherwise`: it satisfies the label
form whenever the member is the header's last token. The cure is that `opensBlockAt` now asks the
keyword-as-a-name exclusion ONCE, ahead of every arm, instead of per arm — three arms had it and the
`otherwise` arm did not.
```maxon
enum Kw
	otherwise
	alpha
end 'Kw'

function main() returns ExitCode
	let k = Kw.alpha
	var i = 0
	while k == Kw.otherwise 'loop'
		i = i + 1
	end 'loop'

	if k == Kw.otherwise 'chk'
		i = 9
	end 'chk'

	return (42 + i) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: closer-case-member-before-a-ternary-else -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function main() returns ExitCode
	var acc = 0
	var i = 0
	while i < 2 'spin'
		let x = 1 if i == 0 or Kw.end == Kw.end else 2
		acc = acc + x
		i = i + 1
	end 'spin'
	return acc as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: opener-case-member-in-if-condition -->
```maxon
enum Kw
	alpha
	while
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		while then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	if tagOf(Kw.while) == 2 'ok'
		return 25
	end 'ok'
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
25
```

<!-- test: opener-case-member-as-match-scrutinee -->
```maxon
enum Kw
	alpha
	match
	omega
end 'Kw'

function main() returns ExitCode
	match Kw.match 'm'
		alpha then return 11
		match then return 22
		omega then return 33
	end 'm'
end 'main'
```
```exitcode
22
```

<!-- test: opener-case-member-in-for-iterable -->
```maxon
enum Kw
	alpha
	for
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		for then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	var acc = 0
	for i in 0 upto tagOf(Kw.for) 'each'
		acc = acc + 10 + i
	end 'each'
	return acc as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
21
```

<!-- test: every-block-keyword-as-a-case-member -->
```maxon
enum Kw
	if
	else
	end
	while
	match
	for
	otherwise
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		if then return 1
		else then return 2
		end then return 4
		while then return 8
		match then return 16
		for then return 32
		otherwise then return 64
	end 'm'
end 'tagOf'

function main() returns ExitCode
	var sum = 0
	if tagOf(Kw.if) == 1 'guard'
		sum = tagOf(Kw.if) + tagOf(Kw.else) + tagOf(Kw.end) + tagOf(Kw.while) + tagOf(Kw.match) + tagOf(Kw.for) + tagOf(Kw.otherwise)
	end 'guard'
	return sum as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
127
```

<!-- test: closer-case-member-nested-two-deep -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < tagOf(Kw.end) 'outer'
		if tagOf(Kw.end) == 2 'inner'
			total = total + 21
		end 'inner'
		i = i + 1
	end 'outer'
	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: closer-case-member-in-a-method-body-keeps-later-siblings -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

type Holder
	export var base as Integer

	export static function create() returns Holder
		return Self{base: tagOf(Kw.end)}
	end 'create'

	export function total() returns Integer
		return self.base + bonus()
	end 'total'

	export function bonus() returns Integer
		return 40
	end 'bonus'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.total() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: opener-case-member-in-a-method-body-adopts-no-free-function -->
```maxon
enum Kw
	alpha
	while
	omega
end 'Kw'

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		while then return 2
		omega then return 3
	end 'm'
end 'tagOf'

type Holder
	export var base as Integer

	export static function create() returns Holder
		return Self{base: tagOf(Kw.while)}
	end 'create'

	export function total() returns Integer
		return self.base + helper()
	end 'total'
end 'Holder'

function helper() returns Integer
	return 40
end 'helper'

function main() returns ExitCode
	let h = Holder.create()
	return h.total() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: closer-case-member-with-a-payload-binding-list -->
```maxon
typealias Num = int(0 to 1000)

union Kw
	alpha(n Num)
	end(m Num)
	omega
end 'Kw'

function tagOf(k Kw) returns Num
	match k 'm'
		alpha(n) then return n
		end(m) then return m + 40
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	let v = tagOf(Kw.end(2))
	if v == 42 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: opener-case-member-with-a-payload-binding-list -->
```maxon
typealias Num = int(0 to 1000)

union Kw
	alpha(n Num)
	while(m Num)
	omega
end 'Kw'

function tagOf(k Kw) returns Num
	match k 'm'
		alpha(n) then return n
		while(m) then return m + 40
		omega then return 3
	end 'm'
end 'tagOf'

function main() returns ExitCode
	let v = tagOf(Kw.while(2))
	if v == 42 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: payload-case-member-with-two-slots-in-a-gives-arm -->
```maxon
typealias Num = int(0 to 1000)

union Kw
	alpha(n Num)
	end(a Num, b Num)
end 'Kw'

function tagOf(k Kw) returns Num
	return match k 'm'
		alpha(n) gives n
		end(a, b) gives a + b
	end 'm'
end 'tagOf'

function main() returns ExitCode
	if tagOf(Kw.end(20, b: 22)) == 42 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: every-block-keyword-as-a-payload-carrying-case -->
```maxon
typealias Num = int(0 to 1000)

union Kw
	if(a Num)
	else(b Num)
	end(c Num)
	while(d Num)
	match(e Num)
	for(f Num)
	otherwise(g Num)
end 'Kw'

function tagOf(k Kw) returns Num
	match k 'm'
		if(a) then return a
		else(b) then return b
		end(c) then return c
		while(d) then return d
		match(e) then return e
		for(f) then return f
		otherwise(g) then return g
	end 'm'
end 'tagOf'

function main() returns ExitCode
	var sum = 0
	var i = 0
	while i < 2 'spin'
		sum = sum + tagOf(Kw.if(1)) + tagOf(Kw.else(2)) + tagOf(Kw.end(4)) + tagOf(Kw.while(8))
		i = i + 1
	end 'spin'
	if sum == 30 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: constructing-a-payload-carrying-keyword-case -->
```maxon
typealias Num = int(0 to 1000)

union Kw
	alpha(n Num)
	end(a Num, b Num)
end 'Kw'

function tagOf(k Kw) returns Num
	match k 'm'
		alpha(n) then return n
		end(a, b) then return a + b
	end 'm'
end 'tagOf'

function main() returns ExitCode
	var acc = 0
	var i = 0
	while i < 2 'spin'
		if tagOf(Kw.end(20, b: 1)) == 21 'ok'
			acc = acc + 21
		end 'ok'
		i = i + 1
	end 'spin'
	return acc as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-parenthesized-condition-with-or-past-the-group -->
```maxon
function main() returns ExitCode
	var i = 0
	var acc = 0
	while ((i < 2) or (i < 0)) or false 'spin'
		if ((i == 0) or (i == 1)) and true 'inner'
			acc = acc + 21
		end 'inner'
		i = i + 1
	end 'spin'
	if acc == 42 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: closer-case-member-and-the-declaration-sweep -->
```maxon
enum Kw
	alpha
	end
	omega
end 'Kw'

var counter = 10

function tagOf(k Kw) returns Integer
	match k 'm'
		alpha then return 1
		end then return 2
		omega then return 3
	end 'm'
end 'tagOf'

function shadowing() returns Integer
	let a = tagOf(Kw.end)
	var counter = 100
	counter = counter + 1
	return a + counter
end 'shadowing'

function main() returns ExitCode
	return (shadowing() + counter) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
113
```
