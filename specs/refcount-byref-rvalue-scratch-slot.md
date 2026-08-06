---
feature: refcount-byref-rvalue-scratch-slot
status: selfhosted
status-reason: 1 of its 2 cases does not compile here (E3019: cannot pass an immutable `let` to a by-reference parameter), so this compiler and the spec disagree about what a reassigned parameter accepts (measured 2026-08-06, BATCH29/A3a). shv2 fails both, on E2013 for the same shape.
keywords: [refcount, byref, pass-by-reference, rvalue, scratch, slot, managed, memory]
category: memory-safety
---

# Refcount: By-Reference R-Value Scratch Slot Disposal

## Documentation

A parameter that a callee REASSIGNS (`p = ...`) is passed by reference: the caller
hands the callee the ADDRESS of the argument's storage so the write lands in the
caller's frame. When the argument is a plain local the caller passes that local's
slot address. But when the argument is an R-VALUE — a literal, an expression, or a
fresh call result like `Node.create(1)` — it has no caller-visible backing slot, so
the caller materializes a fresh SCRATCH slot, spills the r-value into it, and passes
that slot's address (`LowerMaxonToStd.byRefArgAddress` / `allocByRefScratchSlot`).

For a SCALAR by-reference parameter the scratch slot holds a bare i64 — no reference
to maintain. For a MANAGED by-reference struct the r-value's `+1` is MOVED into the
scratch slot, and after the call the slot's FINAL occupant is caller-owned:

- if the callee reassigned the parameter, the slot holds the reassigned value, whose
  `+1` the callee's write-back transferred into the slot (and the callee released the
  original r-value once via its decref-old); or
- if the callee did not reassign it, the slot still holds the original r-value.

Either way the caller owns exactly one reference and must release it once at scope
exit. Because the scratch slot's `stack_addr` makes it address-taken, the moved-in
value's ordinary SSA-store release is suppressed (the value lives in the slot, read
by-reference during the call), so the ONLY correct release is a scope-exit drop of
the slot's content.

Two properties make the scratch slot different from a closure-captured slot, and the
refcount inserter (`isByRefRvalueScratchSlot`) handles both:

- It is written EXACTLY ONCE (the caller's spill), with no prior occupant, and — unlike
  a real scope slot — it is NOT zero-initialized at function entry (it is seeded past
  `slotMaxonTypes.count()`, which the entry zero-init loop bounds on). So the
  release-before-store sweep must SKIP it, otherwise it would decref the slot's
  uninitialized stack before the single store.
- Its id is past `slotMaxonTypes.count()`, so the scope-exit drop sweep must widen its
  bound to the address-taken-slot bitmap to cover it; otherwise the moved-in `+1`
  leaks (nothing else releases it).

Without the fix a managed r-value routed through the scratch slot leaked its final
occupant once per call (the process-exit leak gate trips: exit 101) and, on a
non-zero entry stack, could decref uninitialized stack.

Unlike the consumed-interface-param slot, this slot is written THROUGH the pointer by
the callee rather than re-read as a bare interface value, so it behaves exactly like
the already-supported slot-backed pass-by-reference and is sound on every target,
`wasm32-wasi` included.

## Tests

<!-- test: rvalue-reassigned-byref-managed-param -->
A fresh managed r-value passed directly to a parameter-reassigning function goes
through a by-reference scratch slot. The reassignment releases the original r-value
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
runtime, the scratch slot still holds the ORIGINAL r-value, which must be dropped
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
