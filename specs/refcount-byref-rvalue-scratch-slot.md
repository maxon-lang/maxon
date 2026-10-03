---
feature: refcount-byref-rvalue-scratch-slot
status: selfhosted
keywords: [refcount, byref, pass-by-reference, rvalue, scratch, slot, managed, memory]
category: memory-safety
---

# Refcount: By-Reference R-Value Scratch Slot Disposal

## Documentation

A parameter that a callee REASSIGNS (`p = ...`) is passed by reference: the caller
hands the callee a CELL holding the argument, so the write lands where the caller can
see it. When the argument is an R-VALUE — a literal, an expression, or a fresh call
result like `Node.create(1)` — it has no caller-side binding and therefore no cell, so
the caller materializes a fresh anonymous SCRATCH cell, spills the r-value into it, and
passes that cell (`Parser.materializeByRefScratchCell`).

For a SCALAR by-reference parameter the cell holds a bare word — no reference to
maintain. For a MANAGED by-reference struct the r-value's `+1` is MOVED into the cell,
and after the call the cell's FINAL occupant is caller-owned:

- if the callee reassigned the parameter, the cell holds the reassigned value, and the
  callee released the original r-value once when it replaced it; or
- if the callee did not reassign it, the cell still holds the original r-value.

Either way the caller owns exactly one reference and must release it once. The scratch
cell is an owned binding on the enclosing block's binding stack, so the block's scope
drop releases the cell's occupant and then the cell, exactly once, whichever value it
holds. The spill writes the cell before anything can read it, so there is no prior
occupant to release.

The drop happens at the enclosing BLOCK's exit rather than at the statement's: the callee
may have stored the argument somewhere the caller still reads through this statement.
A managed r-value whose final occupant were never released would trip the process-exit
leak gate (exit 101) once per call.

The scratch cell behaves exactly like the cell a by-reference local is passed in, and
is sound on every target, `wasm32-wasi` included.

## Tests

<!-- test: rvalue-reassigned-byref-managed-param -->
A fresh managed r-value passed directly to a parameter-reassigning function goes
through a by-reference scratch cell. The reassignment releases the original r-value
once and the reassigned value is dropped once at scope exit — no leak, no double-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var value as Integer

	static function create(v Integer) returns Self
		return Self{value: v}
	end 'create'
end 'Node'

function reassign(n Node) returns Integer
	let before = n.value
	n = Node.create(99)
	return before + n.value
end 'reassign'

function main() returns ExitCode
	print("r={reassign(Node.create(1))}")
	return 0
end 'main'
```
```stdout
r=100
```
```exitcode
0
```

<!-- test: rvalue-byref-managed-param-not-reassigned -->
When the by-reference parameter is reassigned only on a path that does not fire at
runtime, the scratch cell still holds the ORIGINAL r-value, which must be dropped
exactly once at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var value as Integer

	static function create(v Integer) returns Self
		return Self{value: v}
	end 'create'
end 'Node'

function maybeReassign(n Node, replace bool) returns Integer
	if replace 'yes'
		n = Node.create(100)
	end 'yes'
	return n.value
end 'maybeReassign'

function main() returns ExitCode
	print("r={maybeReassign(Node.create(7), replace: false)}")
	return 0
end 'main'
```
```stdout
r=7
```
```exitcode
0
```
