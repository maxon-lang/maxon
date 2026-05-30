---
feature: ternary-expression
status: experimental
keywords: [ternary, conditional, if, else, inline]
category: expressions
---

# Ternary Expression

## Documentation

Ternary expressions provide a concise way to choose between two values based on a condition. The syntax places the true value first, followed by the condition, then the false value:

```text
<true_value> if <condition> else <false_value>
```

The condition must be a `bool` expression, and both arms must produce the same type.

**Basic usage:**

```maxon
function main() returns ExitCode
	let x = 10 if true else 20
	return x
end 'main'
```
```exitcode
10
```

**With comparisons:**

```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	let max = a if a > b else b
	return max
end 'main'
```
```exitcode
5
```

**With strings:**

```maxon
function main() returns ExitCode
	let flag = true
	let status = "Active" if flag else "Offline"
	print(status)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Active
```

**In string interpolation:**

```text
let msg = "Status: {"Active" if is_online else "Offline"}"
```

**Chaining:**

Ternary expressions can be chained. The else branch is parsed as a full expression, so chained ternaries associate to the right:

```text
let x = 1 if a else 2 if b else 3
// equivalent to: 1 if a else (2 if b else 3)
```

### Precedence

The ternary operator binds **looser** than all binary operators:

```text
a + b if cond else c * d
// equivalent to: (a + b) if cond else (c * d)
```

### Rules

- The condition must be a `bool` expression
- Both arms must produce the same type
- Binds looser than all binary operators
- Chainable via right-association of the else branch

## Tests

<!-- test: ternary-expression.basic-true -->
```maxon
function main() returns ExitCode
	let x = 10 if true else 20
	return x
end 'main'
```
```exitcode
10
```

<!-- test: ternary-expression.basic-false -->
```maxon
function main() returns ExitCode
	let x = 10 if false else 20
	return x
end 'main'
```
```exitcode
20
```

<!-- test: ternary-expression.with-variable-condition -->
```maxon
function main() returns ExitCode
	let flag = true
	let x = 1 if flag else 0
	return x
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-comparison -->
```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	let x = 1 if a > b else 0
	return x
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-strings -->
```maxon
function main() returns ExitCode
	let flag = true
	let s = "yes" if flag else "no"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
yes
```

<!-- test: ternary-expression.string-interp-expression -->
```maxon
function main() returns ExitCode
	let x = 5
	let msg = "value: {x if x > 0 else 0}"
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
value: 5
```

<!-- test: ternary-expression.string-interp-nested-strings -->
```maxon
function main() returns ExitCode
	let flag = true
	let msg = "status: {"on" if flag else "off"}"
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
status: on
```

<!-- test: ternary-expression.complex-expressions -->
```maxon
function main() returns ExitCode
	let a = 3
	let b = 2
	let cond = true
	let x = a + b if cond else a * b
	return x
end 'main'
```
```exitcode
5
```

<!-- test: ternary-expression.chained -->
```maxon
function main() returns ExitCode
	let a = false
	let b = true
	let x = 1 if a else 2 if b else 3
	return x
end 'main'
```
```exitcode
2
```

<!-- test: ternary-expression.in-return -->
```maxon
function main() returns ExitCode
	let done = true
	return 1 if done else 0
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-floats -->
```maxon
function main() returns ExitCode
	let x = 1.5 if true else 2.5
	return trunc(x)
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-bools -->
```maxon
function main() returns ExitCode
	let x = true if true else false
	let result = 1 if x else 0
	return result
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.false-path-strings -->
```maxon
function main() returns ExitCode
	let flag = false
	let s = "yes" if flag else "no"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
no
```

<!-- test: ternary-expression.in-extension-method-before-sibling -->

Regression: a postfix ternary on a `let` binding inside an extension method
must not confuse the parser's extension-block scanner. The scanner walks
tokens at depth 1 to find function declarations; an inline `if ... else`
must not be counted as a block opener, otherwise sibling methods declared
later in the same extension block fail to register and become "Undefined
method" at every call site.

```maxon
typealias Small = int(0 to 1000)

interface Bounded
	function lo() returns Small
	function hi() returns Small
end 'Bounded'

extension Bounded
	function pickSmaller() returns Small
		let smaller = self.lo() if self.lo() < self.hi() else self.hi()
		return smaller
	end 'pickSmaller'

	function describe() returns Small
		return self.hi() - self.lo()
	end 'describe'
end 'Bounded'

type Pair implements Bounded
	let l as Small
	let h as Small

	function lo() returns Small
		return l
	end 'lo'

	function hi() returns Small
		return h
	end 'hi'

	static function create(l Small, h Small) returns Self
		return Self{l: l, h: h}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(7, h: 12)
	let s = p.pickSmaller()
	let d = p.describe()
	return s + d
end 'main'
```
```exitcode
12
```

<!-- test: ternary-expression.error.unused-loopvar-before-ternary -->

Regression: a postfix ternary appearing downstream of an inner-loop
`try ... otherwise` (which allocates a `try_N.merge` block) inside an
outer loop whose induction variable is **unused** must still surface the
unused-variable diagnostic (E3012), not crash. The self-hosted parser
previously left the inner `try_N.merge` block without a terminator in this
exact CFG shape and tripped `assertAllBlocksTerminated` before E3012 could
be reported. The block-terminator wiring must remain well-formed regardless
of how many `if ... else` merge blocks are allocated later in the same
function body.

```maxon
typealias Idx = int(i64.min to i64.max)
typealias IdxList = List with Idx

function splice(lst IdxList, anchor Idx, after bool)
	for outer in 0 upto 2 'eachBlock'
		var insertPos = -1 as Idx
		for posIdx in 0 upto lst.count() 'eachOpRef'
			let ref = try lst.get(posIdx) otherwise panic("oob")
			if ref == anchor 'foundAnchor'
				insertPos = posIdx
				break
			end 'foundAnchor'
		end 'eachOpRef'
		if insertPos >= 0 'doInsert'
			let target = insertPos + 1 if after else insertPos
			try lst.insert(target, value: 99) otherwise panic("insert oob")
			return
		end 'doInsert'
	end 'eachBlock'
	panic("not found")
end 'splice'

function main() returns ExitCode
	var a = IdxList.create()
	a.append(10)
	a.append(20)
	a.append(30)
	splice(a, anchor: 20, after: false)
	print("pos1={try a.get(1) otherwise -1} pos2={try a.get(2) otherwise -1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/ternary-expression/ternary-expression.error.unused-loopvar-before-ternary.test:6:6: unused variable: 'outer'
```

<!-- test: ternary-expression.error.type-mismatch -->
```maxon
function main() returns ExitCode
	let x = 10 if true else "hello"
	return 0
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.type-mismatch.test:3:13: ternary expression type mismatch: true branch is 'Integer' but false branch is 'Struct'
```

<!-- test: ternary-expression.error.non-bool-condition -->
```maxon
function main() returns ExitCode
	let x = 10 if 42 else 20
	return 0
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.non-bool-condition.test:3:13: ternary expression requires a bool condition, got 'Integer'
```

<!-- test: ternary-expression.error.struct-type-mismatch -->

Both arms are managed (struct) types, so a coarse Bool/Integer/Float/Struct
kind check sees them as equal. The arm-match check must compare the concrete
type names, otherwise `Cat if c else Dog` slips through and crashes in lowering
when the merged result is read with the wrong field set.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cat
	export var legs as Integer

	static function create() returns Self
		return Self{legs: 4}
	end 'create'
end 'Cat'

type Dog
	export var tails as Integer

	static function create() returns Self
		return Self{tails: 1}
	end 'create'
end 'Dog'

function main() returns ExitCode
	let flag = true
	let x = Cat.create() if flag else Dog.create()
	return x.legs
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.struct-type-mismatch.test:22:23: ternary expression type mismatch: true branch is 'Cat' but false branch is 'Dog'
```
