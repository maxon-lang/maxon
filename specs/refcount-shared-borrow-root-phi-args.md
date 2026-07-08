---
feature: refcount-shared-borrow-root-phi-args
status: experimental
keywords: [refcount, borrow, phi, edge, break, memory, map, over-release]
category: memory-safety
---

# Refcount: Shared Borrow-Root Across Several Phi-Args on One Edge

## Documentation

When a borrowed value flows into a control-flow merge, the incoming edge
increfs the borrow so the merge phi owns its own `+1`, and the borrow's owning
allocation (its **root**) is released on that same edge — *after* the incref, so
the interior stays live until it has an independent reference (the borrow-base
deferral).

A single edge can carry **several** phi-args that are all interior borrows of the
**same** root. The canonical shape is a `break` out of a loop that reads several
fields of a borrowed element into outer variables:

```text
break_edge -> exit(c.fieldA, c.fieldB, c.fieldC)   // three interior borrows of `c`
```

Each block-arg needs its own field incref, but the shared root `c` holds exactly
**one** deferred `+1` (its suppressed last-use release), so it must be released
**once**. Releasing it once *per borrowing block-arg* drives the root's refcount
below the reference the owning container still holds: the element is freed while
the map/array still points at it, and teardown then decrefs a freed object
(`__mm_decref: over-release — refcount was already 0`). The releases must also
follow every field incref, so the escaping fields are retained before the root
frees.

This test borrows a map-owned array via `try m.get(k) otherwise ClauseArray.create()`
(a phi merging the borrowed value with a fresh empty array), iterates it, and on
a conditional `break` reads three managed fields of the borrowed element into
three outer `var`s that then escape as the return value. The map owns the array
which owns the element, so an extra release of the element frees it out from under
the map — the exact over-release the conditional-extensions `WhereClause`
diagnostic teardown hit in the self-hosted compiler. Correct single-release
ordering keeps every object alive; the program returns the summed length of the
three surviving field strings.

## Tests

<!-- test: shared-borrow-root-break-edge -->
Several phi-args that borrow the same root on one `break` edge must release that
root once, after the field increfs — not once per block-arg.
```maxon
typealias Count = int(0 to 1000)

type Clause
	export var paramName as String
	export var traitName as String
	export var typeName as String

	static function create(paramName String, traitName String, typeName String) returns Self
		return Self{paramName: paramName, traitName: traitName, typeName: typeName}
	end 'create'
end 'Clause'

typealias ClauseArray = Array with Clause
typealias ClauseMap = Map with (String, ClauseArray)

function satisfied(traitName String) returns bool
	return traitName == "Equatable"
end 'satisfied'

// Borrow a map-owned array, iterate it, and on the conditional break read three
// fields of the borrowed element into outer vars — three interior borrows of the
// same element on one edge.
function firstFailing(m ClauseMap, key String) returns Count
	var failedParam = ""
	var failedTrait = ""
	var failedType = ""
	let clauses = try m.get(key) otherwise ClauseArray.create()
	for i in 0 upto clauses.count() 'each'
		let c = try clauses.get(i) otherwise panic("firstFailing: get OOB")
		if not satisfied(c.traitName) 'fail'
			failedParam = c.paramName
			failedTrait = c.traitName
			failedType = c.typeName
			break
		end 'fail'
	end 'each'
	return failedParam.count() + failedTrait.count() + failedType.count()
end 'firstFailing'

function main() returns ExitCode
	var m = ClauseMap.create()
	var arr = ClauseArray.create()
	arr.push(Clause.create("Item", traitName: "Comparable", typeName: "NotComparable"))
	try m.insert("MyHolder.isGreater", value: arr) otherwise return 1
	// "Item"(4) + "Comparable"(10) + "NotComparable"(13) = 27
	return firstFailing(m, key: "MyHolder.isGreater")
end 'main'
```
```exitcode
27
```
