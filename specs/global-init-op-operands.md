---
feature: global-init-op-operands
status: stable
keywords: [global, module-init, union, enum, array, map, byte-string, dead-code, refcount]
category: memory
---
# Global Initializers: Every Op Must Declare What It Reads

## Documentation

`DeadFunctionElimination` prunes the producer chains of globals nothing reads.
It runs over `__module_init` (and `__maxon_global_cleanup`) — and only there —
because those are the only bodies whose ops exist solely to fill a global's slot.
Every real program reaches it: the stdlib declares globals a given program does
not touch, so the dead set is essentially never empty.

To decide what to delete it needs the opposite fact — what is still *read*. That
liveness scan used to hand-roll the operand list, naming five op kinds
(`global_store`, `struct_literal`, `return`, `call`, `assign`) out of the ~80 that
carry operands. **Everything it forgot was invisible.** `maxon.enum_construct` was
one of the forgotten ones, so a payload-carrying union case built inside a global
initializer — `var ops = [Op.add(1)]` — had its payload operand counted by nobody.
The literal `1` looked dead, was deleted, and `__module_init` was left holding an
`enum_construct` whose operand nothing defined (`E9001: The given key '%9' was not
present in the dictionary`). The same hole covered `try_call`, `managed_mem_*`,
`binary_op`, `cast`, `field_access` and the indirect call's *callee*.

This is one fact written down twice: an op already declares the values it reads,
and the scan re-declared them, incompletely. `MaxonOp.Operands` is now the single
home for that fact — `PrintableOperands` renders it, and the liveness scan walks
it — so an op cannot read a value that liveness cannot see, and a newly added op
kind cannot silently reintroduce the hole.

The tests below pin the shapes that were broken, and the combination originally
reported: a global byte-string-keyed `Map` alongside an `Array` over a
payload-carrying union.

## Tests

<!-- test: global-union-array-literal -->
### A global array literal over a payload-carrying union keeps its payload
The union case's payload operand is read only by `enum_construct`. When that read
was invisible, the literal `7` was deleted as dead and the module failed to lower.
```maxon
typealias Val = int(i64.min to i64.max)

union Op
	add(value Val)
	nop
end 'Op'

var globalOps = [Op.add(7), Op.nop]

function main() returns ExitCode
	let first = try globalOps.get(0) otherwise Op.nop
	return match first 'check'
		add(v) gives v
		nop gives 99
	end 'check'
end 'main'
```
```exitcode
7
```

<!-- test: global-bytestring-map-with-union-array -->
### A global byte-string-keyed Map alongside an Array over a payload union
The originally reported combination. Neither ingredient alone was enough to show
the hole; the map supplies a live global whose chain runs through `__module_init`,
and the union supplies the unscanned `enum_construct` read.
```maxon
typealias Kind = int(0 to 100)
typealias Val = int(i64.min to i64.max)

type KeywordInfo
	export var kind as Kind
	export var helpText as String

	export static function create(kind Kind, helpText String) returns KeywordInfo
		return Self{kind: kind, helpText: helpText}
	end 'create'
end 'KeywordInfo'

union Op
	add(value Val)
	nop
end 'Op'

typealias OpArray = Array with Op

var keywordMap = [
	b"if": KeywordInfo.create(1, helpText: "Conditional statement."),
	b"else": KeywordInfo.create(2, helpText: "Alternative branch.")
]

function main() returns ExitCode
	var ops = OpArray.create()
	ops.push(Op.add(1))
	ops.push(Op.nop)

	let info = try keywordMap.get(b"if") otherwise KeywordInfo.create(0, helpText: "")
	if info.kind != 1 'badLookup'
		return 1
	end 'badLookup'

	return 0 if ops.count() == 2 and keywordMap.count() == 2 else 2
end 'main'
```
```exitcode
0
```

<!-- test: global-map-with-union-values -->
### A global byte-string-keyed Map whose VALUES are payload-carrying union cases
Puts the `enum_construct` directly inside the map literal's value buffer, so the
payload read and the map's element-assign chain are pruned by the same pass.
```maxon
typealias Val = int(i64.min to i64.max)

union Op
	add(value Val)
	nop
end 'Op'

var opMap = [
	b"add": Op.add(5),
	b"nop": Op.nop
]

function main() returns ExitCode
	let op = try opMap.get(b"add") otherwise Op.nop
	return match op 'check'
		add(v) gives v
		nop gives 99
	end 'check'
end 'main'
```
```exitcode
5
```

<!-- test: live-global-survives-dead-global-pruning -->
### A dead global's pruning must not damage a live global's producer chain
`EliminateDeadOps` only runs when some global is dead, and it then walks the WHOLE
`__module_init` block — so a live global's chain is exposed to the same scan. The
byte-string keys and the interpolated string here are all pure producers: each is
deleted the moment the scan cannot see who reads it.
```maxon
typealias Kind = int(0 to 100)

type KeywordInfo
	export var kind as Kind
	export var helpText as String

	export static function create(kind Kind, helpText String) returns KeywordInfo
		return Self{kind: kind, helpText: helpText}
	end 'create'
end 'KeywordInfo'

function describe(n Kind) returns String
	return "kind {n}"
end 'describe'

// Live: read by main.
var keywordMap = [
	b"if": KeywordInfo.create(1, helpText: describe(1)),
	b"else": KeywordInfo.create(2, helpText: describe(2))
]

// Dead: nothing reads it. Its presence is what makes the pass run at all.
var unusedNames = StringArray.create()

function main() returns ExitCode
	let info = try keywordMap.get(b"else") otherwise KeywordInfo.create(0, helpText: "")
	if info.helpText != "kind 2" 'badHelp'
		return 1
	end 'badHelp'

	return 0 if keywordMap.count() == 2 and info.kind == 2 else 2
end 'main'
```
```exitcode
0
```
