---
feature: cross-block-method-receiver
status: stable
keywords: [ssa, dominance, method, receiver, statement, branch, merge, block-argument]
category: control-flow
---

# Method-Call Receivers Across a Block Boundary

## Documentation

A variable read in shv2 resolves to the ValueId its binding already holds:
`Parser.parseVariableReference` hands back `binding.boundValue`, and it hands back
the SAME ValueId no matter which block the read sits in. There is no re-tagging op
at the read site and no per-read named reference, so a cross-block read is not a
distinct construct — it is the same read, evaluated somewhere else.

What makes that sound across a branch is BLOCK ARGUMENTS. A `var` reassigned
inside an `if` arm rebinds at parse time; the merge block takes the variable's
value as a block argument, so the name resolves after the merge to the merge
block's parameter — a value defined on every path reaching the read. Dominance
therefore holds by construction rather than by a check, and there is no way for a
read to name a value that only one arm defines.

A receiver is a read like any other, and it takes that one path whether it stands
in expression position (`arr.count()` inside a `return`) or in statement position
(`arr.reserve(4)` on its own line). One resolution path means the two positions
cannot disagree: there is no separate statement-position read to fall out of step
with the expression one.

A variable whose DECLARATION lives inside an arm is a different question, and it
is refused before any of the above applies — after the merge the name has no
binding at all, so nothing resolves and no dominance question is ever asked.

## Tests

<!-- test: struct-method-statement-after-if-merge -->
A managed `var` reassigned inside an `if` arm, then a method-call STATEMENT on it
after the merge. The receiver resolves to the merge block's argument, so the call
lands on whichever array the taken path produced.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function build(n Integer) returns Integer
	var arr = IntArray.create()
	if n > 1 'grow'
		arr = IntArray.create()
	end 'grow'
	arr.reserve(4)
	return arr.count()
end 'build'

function main() returns ExitCode
	print("{build(5)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: user-method-statement-after-if-merge -->
The same shape with a USER type and a method that neither mutates nor takes its
receiver by reference — it only prints. The printed value must come from the
arm's assignment, proving the read resolves to the variable's current contents
and not to the value it held before the branch.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	var v as Integer

	function show()
		print("v={v}\n")
	end 'show'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

function main() returns ExitCode
	var b = Box.create(1)
	if 2 > 1 'grow'
		b = Box.create(7)
	end 'grow'
	b.show()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=7
```

<!-- test: builtin-method-statement-after-if-merge -->
The builtin-method statement path — a `__ManagedMemory` receiver whose method is
emitted INLINE rather than as a call, so the receiver value is consumed at the
call site instead of being passed.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	if 2 > 1 'grow'
		mm = try __ManagedMemory.create(8, elementSize: 8) otherwise return 2
	end 'grow'
	mm.resize(3)
	return mm.count()
end 'main'
```
```exitcode
3
```

<!-- test: interface-method-statement-after-if-merge -->
The interface / type-parameter statement path. Dispatch goes through the
interface's method signature, so the receiver is re-read as a struct value at the
call site.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shower
	function show()
end 'Shower'

type Box implements Shower
	var v as Integer

	function show()
		print("v={v}\n")
	end 'show'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

function run(s Shower, again Shower)
	var t = s
	if 2 > 1 'grow'
		t = again
	end 'grow'
	t.show()
end 'run'

function main() returns ExitCode
	run(Box.create(3), again: Box.create(9))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=9
```

<!-- test: method-statement-after-continue-in-loop -->
A `continue` earlier in a loop body, with the reassignment and the receiver
statement inside the same body. The loop back-edge and the `continue` edge both
carry the variable as a block argument, so the receiver after the inner merge is
still one value.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function build(n Integer) returns Integer
	var arr = IntArray.create()
	var i = 0
	while i < n 'loop'
		i = i + 1
		if i == 2 'skip'
			continue
		end 'skip'
		if n > 1 'grow'
			arr = IntArray.create()
		end 'grow'
		arr.reserve(4)
	end 'loop'
	return arr.count()
end 'build'

function main() returns ExitCode
	print("{build(5)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: method-expression-after-if-merge-boundary -->
The boundary control: the SAME reassignment, with the receiver read in EXPRESSION
position instead. It must agree with the statement form above — one resolution
path is the claim, and a disagreement between the two positions is what would
refute it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function build(n Integer) returns Integer
	var arr = IntArray.create()
	if n > 1 'grow'
		arr = IntArray.create()
	end 'grow'
	return arr.count()
end 'build'

function main() returns ExitCode
	print("{build(5)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: scalar-var-after-if-merge-boundary -->
The other boundary control: a SCALAR `var` reassigned in the arm and read after
the merge. A scalar has no method-call statement form, so this pins the plain
cross-block read the receiver cases build on.
```maxon
function main() returns ExitCode
	var x = 1
	if 2 > 1 'grow'
		x = 2
	end 'grow'
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: error-receiver-declared-only-in-branch -->
The unsound direction, which must stay REFUSED. A receiver whose declaration
lives inside the arm has no binding after the merge, so the read is refused before
dominance is consulted — no relaxation of the cross-block read path can make this
program compile.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	if 2 > 1 'grow'
		var arr = IntArray.create()
		arr.reserve(4)
	end 'grow'
	arr.reserve(8)
	return 0
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/cross-block-method-receiver/error-receiver-declared-only-in-branch.test:10:2: Undefined variable 'arr'
```
