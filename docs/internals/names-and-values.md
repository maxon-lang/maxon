---
title: Names and Values
description: How the parser builds SSA directly, how values are numbered, how symbols are named, and how compiler data is laid out.
sidebar:
  order: 3
---

## Names, SSA and phis

The parser has no AST and there is no `mem2reg`: it emits Maxon IR already in SSA
(`maxon-bin/Compiler/Parser.maxon`). A `let` or `var` binds its name, in the parser's `Scope`, straight to the
value its initializer produced (`Scope.declareValueBinding`). There are no `varDecl`, `varLoad` or `varStore`
ops, so `let x = 42` emits no op at all, and an IR pass cannot see a binding. Facts about bindings that a
later check needs, such as the borrow checker's, must therefore be recorded by the parser.

A loop header's phis (block arguments) are minted before the loop body is parsed. The parser first scans the
loop's tokens for the bindings it assigns (`assignedBindingsIn`), mints one phi for each, and records each
binding's value from before the loop so the entry edge can pass it.

That scan over-approximates: an inner `var` shadowing an outer name looks like an assignment to it. A surplus
phi is safe and a missing one would be a miscompile, so the parser errs towards surplus, and two Std passes
remove it:

- `PruneDeadBlockArgs` deletes phis nothing reads, as a least fixpoint, since a dead phi keeps itself alive
  through the back edge;
- `ElimTrivialBlockArgs` deletes a phi whose incoming values, ignoring itself, are all one value `v`, and
  rewrites its uses to `v`.

Both matter beyond tidiness: a surplus phi counts as a live value across the loop, and can make the register
allocator report a false E5001 ([Register Allocation](register-allocation.md#the-contract)).

## Value numbering

Value numbering is born in the parser. Each op's result gets a dense, function-local `ValueId` from the
parser's counter, which starts at the function's parameter count: parameters occupy `0..paramCount-1`. The
same ids pass through `LowerMaxonToStd` unchanged, and a value a later tier synthesizes continues from the
highest id minted (`ValueMinter`, `nextValueId`).

Because a `ValueId` is a dense index, every per-value column is an array sized by the highest id, and an id
minted and then abandoned still costs every such column a slot. `blockArgIdBound` sizes the columns that only
phis need.

## Data representation

**No `Set`, no `Map`, and no hashing on a hot path.** A `ValueId`, a block index or an op index is already a
dense index, so a hash table keyed by one spends a hash and a probe on what is an array subscript, and every
probe into a table that doubles with the program is a cache miss. Per-value facts live in dense columns
instead: `StdValueUseSet`, `ConstValueMap` in constant folding, the load witnesses in loop-invariant code
motion, the available expressions in CSE (dense columns and a chain, bucketed by a key over both operands),
and the register allocator's bitsets.

**Unions are flat.** A union with any payload-carrying case is a heap box: an 8-byte tag and one 8-byte slot
per field of its widest case. Every case of such a union is boxed, payload-free ones included, so returning
a payload-free case still allocates. A union whose cases carry no payload is a bare tag. A nested union costs
one more box per level, so the compiler's own IR unions, such as `StdOp`, are one flat union rather than a
union of categories.

## Symbols built by joining names

A symbol built by joining two names joins them with a character no name can hold, so the join is injective.
An identifier is `[A-Za-z_][A-Za-z0-9_]*` (`Lexer.maxon`), and the `__` prefix is reserved: a declaration
that starts with it is refused with E2051.

| Join | Joins | Example |
| --- | --- | --- |
| `__` prefix | a compiler-internal symbol, kept out of the author's namespace | `__mm_alloc`, `__destruct_T` |
| `.` | a type and a method, the spelling the author writes | `Type.method` |
| `.` | a type and an interface, in a witness-table label | `__witness_<Type>.<Iface>` |
| `#` | an overload's base name and its signature parts; a default's helper and its function and position | `lookup#Registry#bool`, `__paramDefault#f#0` |
| `$` | a lifted closure and its enclosing function, nesting | `<enclosing>$closure_<k>` |
| `_` | a generic and its type arguments | `Vector_Int` |

Two joins use `_`, which an identifier can hold, and each is injective for its own reason:

- a primitive's static method, `__<primitive>_<method>`, draws its left side from the closed set of
  primitive names, none of which contains `_`, and must match the spelling `stdlib/Builtins.maxon` declares;
- a generic instance name, such as `Vector_Int`, is moved behind the reserved `__` prefix whenever a
  declaration already claims the bare name, so it can never collide with one.
