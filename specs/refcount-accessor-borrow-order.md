---
feature: refcount-accessor-borrow-order
status: experimental
keywords: [refcount, borrow, inline, accessor, ordering, memory, union]
category: memory-safety
---

# Refcount: Accessor Borrow-Base Release Ordering

## Documentation

An inlined multi-arm accessor that returns a managed *field* of its argument
produces, in each field-returning arm, the shape:

```text
field = load(base + offset)   // interior borrow of `base`
decref base                   // `base` is dead after this arm
incref field                  // the returned field takes its own +1
```

The two refcount operations land at the **same** program point (the field's
def-acquire and the base's last-use release). They MUST execute
retain-before-release: `incref field` first, then `decref base`. If the base is
released first, its destructor decrements the field (the base owns it), driving
the field's refcount to zero and freeing it — and the following `incref` then
touches freed memory. When the base's refcount is exactly one (a freshly built,
single-owner value, e.g. a just-materialized parser `result`), the premature
`decref` frees the field, so the arm's `incref` resurrects a freed slot and a
later drop double-frees it (`__mm_decref: over-release — refcount was already 0`).

This test builds a boxed-union `Expr` carrying a managed `Ty` field and an
inlined `exprType` accessor whose `direct`/`named` arms return that field while
its `unresolved` arm returns a fresh allocation (so the accessor's continuation
phi merges a borrow with a fresh value — disagreeing roots — and the base's
death lands in the field-returning arm). A path that `return`s the value makes
the field def-acquire, reproducing the exact `load field; decref base; incref
field` collision. Correct retain-before-release ordering keeps every object
alive through its last use; the program must complete and report the final
type's length.

## Tests

<!-- test: inlined-accessor-field-return-order -->
An inlined accessor's field-returning arm must incref the borrowed field before
releasing the base, or the base's destructor frees the field mid-arm.
```maxon
typealias TokenId = int(0 to u64.max)

union Ty
	concrete(name ByteArray)
	stringy
end 'Ty'

union Expr
	direct(ty Ty)
	named(ty Ty, id TokenId)
	unresolved
end 'Expr'

// Multi-arm accessor: the direct/named arms return the `ty` field (an interior
// borrow of `e`); the `unresolved` arm returns a fresh `Ty.stringy` (a distinct
// borrow root), so the inlined continuation phi drops e's borrow and the base's
// death lands in a field-returning arm.
function exprType(e Expr) returns Ty
	return match e 'k'
		direct(ty) gives ty
		named(ty, _) gives ty
		unresolved gives Ty.stringy
	end 'k'
end 'exprType'

function makeDirect(ty Ty) returns Expr
	return Expr.direct(ty)
end 'makeDirect'

function tyLen(ty Ty) returns TokenId
	return match ty 'l'
		concrete(name) gives name.count()
		stringy gives 0
	end 'l'
end 'tyLen'

// A fresh single-+1 `Expr`, like a freshly materialized parser `result`.
function makeFresh(seed TokenId) returns Expr
	if seed == 0 'z'
		return Expr.unresolved
	end 'z'
	return Expr.direct(Ty.concrete("hello".toByteArray()))
end 'makeFresh'

function walk(rounds TokenId) returns Expr
	var last = Expr.unresolved
	var total = 0

	for i in 1 to rounds 'loop'
		let e = makeFresh(i)

		if total == 888 'ret'
			// Returns the fresh `e`, marking it transferred so a field loaded
			// out of it (in `exprType`) def-acquires — the collision trigger.
			return e
		end 'ret'

		// exprType(e) is `e`'s last use; the fresh `e` (rc 1) then dies right
		// where its `ty` field was just loaded.
		let ty = exprType(e)
		total = total + tyLen(ty)
		last = makeDirect(ty)
	end 'loop'

	return last
end 'walk'

function main() returns ExitCode
	let r = walk(5)
	let ty = exprType(r)
	return tyLen(ty)
end 'main'
```
```exitcode
5
```
