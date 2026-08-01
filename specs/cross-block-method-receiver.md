---
feature: cross-block-method-receiver
status: stable
keywords: [ssa, dominance, method, receiver, statement, branch, merge]
category: control-flow
---

# Method-Call Receivers Across a Block Boundary

## Documentation

The bootstrap parser has no phi nodes and no block arguments. A value crosses a
block boundary by being RE-READ through its variable NAME: `VarInfo.Value` is the
raw SSA value the variable last produced and `VarInfo.DefinedInBlock` is the block
that produced it, so every read must ask whether it is still in that block. A read
in a DIFFERENT block mints a fresh named reference op (`MaxonVarRefOp`,
`MaxonStructVarRefOp`, `MaxonEnumVarRefOp`, `MaxonFunctionVarRefOp`) at the read
site, which lowering resolves against the variable's storage slot.

A receiver is a read like any other. Reassigning a variable inside an `if` arm
leaves `DefinedInBlock` pointing at the ARM, and the arm does not dominate the
merge block — so a receiver read after the merge that hands back the raw
`VarInfo.Value` names a value that is not defined on every path reaching it. The
SSA verifier catches that as E9001, which is correct: the verifier is a downstream
detector, and the defect is the read that skipped the re-materialization.

Every EXPRESSION-position receiver already went through the one resolver that
performs this check. The three STATEMENT-position method-call paths did not — they
read `VarInfo.Value` directly — so a bare `receiver.method(...)` statement after a
branch merge was rejected outright:

```text
error E9001: in 'build', op 'maxon.call @stdlib.Array.reserve' in block
'grow_0.merge' reads %4, which is defined in block 'grow_0' — and 'grow_0'
does not dominate 'grow_0.merge'.
```

The distinguishing fact is STATEMENT POSITION, not mutation and not by-reference
passing: a non-mutating user method called for its effect fails identically, while
the same receiver in expression position compiles. The loop and the `continue`
that first surfaced this are incidental — the branch merge alone is enough.

All three statement paths now resolve their receiver through the same routine as
every other read, so the rule is written once and cannot drift apart again.

A variable that genuinely is not in scope on the path that reads it is still
refused — by name resolution (E3003), before dominance is ever consulted.

## Tests

<!-- test: struct-method-statement-after-if-merge -->
A managed `var` reassigned inside an `if` arm, then a method-call STATEMENT on it
after the merge. Pre-fix: E9001 naming `stdlib.Array.reserve` and the arm block.
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
receiver by reference — it only prints. It failed identically before the fix,
which is what localises the defect to statement position rather than to mutation.
The printed value must come from the arm's assignment, proving the re-read
resolves to the variable's current contents and not to a stale slot.
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
emitted INLINE rather than as a call. It reached the same raw read, so it failed
the same way (`maxon.managed_mem_clear` naming the arm block).
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	if 2 > 1 'grow'
		mm = try __ManagedMemory.create(8, elementSize: 8) otherwise return 2
	end 'grow'
	mm.clear()
	return mm.length()
end 'main'
```
```exitcode
0
```

<!-- test: interface-method-statement-after-if-merge -->
The interface / type-parameter statement path, the third copy of the same raw
read. Dispatch goes through the interface's method signature, so the receiver is
re-read as a struct value at the call site.
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
The shape the defect was originally reported under: a `continue` earlier in a loop
body, with the reassignment and the receiver statement inside the same body. The
`continue` is incidental — the inner branch merge is the whole trigger — but the
reported shape is pinned so the report stays covered.
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
position instead. This path always went through the shared resolver and always
compiled — it must stay green, because it is what localises the defect to the
statement paths.
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
the merge. Scalars have no method-call statement form, so they never reached the
raw read — this pins that the shared resolver still hands a scalar its cross-block
`var_ref`.
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
The unsound direction, which must stay REFUSED. A receiver whose declaration lives
inside the arm is not in scope after the merge at all; name resolution rejects it
before dominance is consulted, so relaxing the dominance-safe read path cannot
make this program compile.
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
error E3003: specs/fragments/cross-block-method-receiver/error-receiver-declared-only-in-branch.test:10:2: Undefined variable 'arr'
```
