---
feature: rethrow-stashed-self-field
status: stable
keywords: [throw, rethrow, self, field, refcount, memory, error-handling, codegen]
category: memory-management
---

# Re-Throwing A Stashed Self Field

## Documentation

When an instance method enters and reads `self.field` (where the field is a
heap-allocated value such as an associated-value union), the compiler caches the
loaded heap pointer in a local at function entry. If the method then makes a
call that mutates `self.field` — overwriting the previous heap pointer and
freeing it via decref — that local must be reloaded before any later use,
otherwise the local dangles and any subsequent read (especially `throw <field>`,
which transfers ownership of that pointer to the caller) operates on freed
memory.

The reload-after-call mechanism that already covers struct-typed self fields is
extended to cover associated-value enum-typed (union) self fields, since both
are stored as heap pointers and have the same dangling-alias hazard.

## Tests

<!-- test: rethrow-stashed-self-union-field -->
### Re-throwing a union-typed self field that was rewritten by an inner call

A method stashes a union value into a self field. An inner method overwrites
that field while throwing a different error type. The outer method catches the
inner error and re-throws the (rewritten) field. The caller must observe the
rewritten variant — and there must be no use-after-free of the original value.

```maxon
typealias N = int(0 to i64.max)

union Inner implements Error
	exhausted
end 'Inner'

union Outer implements Error
	first(line N, column N)
	second(line N, column N)
end 'Outer'

type Holder
	var pending Outer

	static function create() returns Holder
		return Self{pending: Outer.first(line: 0, column: 0)}
	end 'create'

	function inner() throws Inner
		pending = Outer.second(line: 1, column: 1)
		throw Inner.exhausted
	end 'inner'

	function outer() throws Outer
		try inner() otherwise 'innerErr'
			throw pending
		end 'innerErr'
	end 'outer'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	try h.outer() otherwise (e) 'fail'
		match e 'kind'
			first then return 1
			second then return 0
		end 'kind'
	end 'fail'
	return 2
end 'main'
```
```exitcode
0
```
