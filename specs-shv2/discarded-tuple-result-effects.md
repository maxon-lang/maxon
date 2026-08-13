---
feature: discarded-tuple-result-effects
status: stable
keywords: [tuple, assignment, discard, purity, effects, diagnostics]
category: diagnostics
---

# Which callees `(_, _) = …` refuses

## Documentation

`specs/tuple-assign.md` pins one half of this: `(_, _) = makePair(10, b: 32)` uses none of the call's
result, so if `makePair` has no other reason to run the call is dead code and takes **E3064**. That case
says nothing about the other half — **which callees are NOT refused** — and the other half is where the
rule can do damage, because E3064 *rejects a program*. A classification that is too generous does not
miss a diagnostic; it refuses code that should compile.

shv2 answers it with a whole-program summary (`SemanticCheck.buildEffectFreeSummary`) whose contract is
one-directional: **it may only ever say "not proven effect-free" about something that is; it may never
say "effect-free" about something that is not.** A function that assigns into an aggregate that already
existed — its receiver's field, a field through a parameter, a mutable `match` payload — has an effect,
whatever the field's type and whichever of the spellings the author used.

### ⚠ The reference compiler cannot arbitrate this, because it contradicts itself

MEASURED against the C# bootstrap, three programs that differ only in how one field write is spelled:

| the write | bootstrap | shv2 |
|---|---|---|
| bare `n = n + 1` inside a method | compiles | compiles |
| `self.n = self.n + 1` inside a method | **E3064** | compiles |
| `c.n = 7` through a `c Counter` parameter | **E3064** | compiles |

One write, two spellings, two verdicts from the reference. So "does shv2 match the oracle here" has no
single answer, and a check that measures only the last two rows reads as total agreement — which is how
this was nearly accepted as correct. **shv2 accepts all three deliberately**: it accepts everything the
oracle accepts on this axis, plus the two shapes the oracle wrongly refuses. The bootstrap's hole is not
copied.

## Tests

⚠ **EVERY CASE BELOW WAS `error E3064` BEFORE `IrFunction.assignsIntoAnExistingAggregate` EXISTED.** The
summary seeded off E3070's parameter masks, which their own doors narrow to ARRAY-instance fields, so an
int field write set nothing and every one of these was refused. They are the red-gate control for that
fix; `refuses-a-callee-with-no-effect` is the control in the other direction, and must stay red.

<!-- test: bare-self-field-store-is-an-effect -->
The canonical spelling — a bare field name inside a method. `bump` mutates the receiver, so the call is
worth making whatever happens to its result.
```maxon
typealias Count = int(i64.min to i64.max)

type Counter
	export var n as Count

	function bump() returns (Count, Count)
		n = n + 1
		return (n, n)
	end 'bump'

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	(_, _) = c.bump()
	(_, _) = c.bump()
	return c.n
end 'main'
```
```exitcode
2
```

<!-- test: explicit-self-field-store-is-an-effect -->
The same write with an explicit `self.`. One rule about one storage; it must not answer differently for
the way the source spelled it — and this is the row the bootstrap gets wrong.
```maxon
typealias Count = int(i64.min to i64.max)

type Counter
	export var n as Count

	function bump() returns (Count, Count)
		self.n = self.n + 1
		return (self.n, self.n)
	end 'bump'

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	(_, _) = c.bump()
	(_, _) = c.bump()
	(_, _) = c.bump()
	return c.n
end 'main'
```
```exitcode
3
```

<!-- test: field-store-through-a-parameter-is-an-effect -->
A free function writing a field of the struct it was handed. The caller sees the write, so the call has a
reason to run that its return type does not express.
```maxon
typealias Count = int(i64.min to i64.max)

type Counter
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Counter'

function poke(c Counter) returns (Count, Count)
	c.n = 7
	return (1, 2)
end 'poke'

function main() returns ExitCode
	var c = Counter.create()
	(_, _) = poke(c)
	return c.n
end 'main'
```
```exitcode
7
```

<!-- test: transitive-field-store-is-an-effect -->
The effect reaches the caller through the call graph, two hops up, exactly as a `print` does. Without the
closure this would compile for the wrong reason, so the depth is the point.
```maxon
typealias Count = int(i64.min to i64.max)

type Counter
	export var n as Count

	function bump() returns (Count, Count)
		n = n + 1
		return (n, n)
	end 'bump'

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Counter'

function oneHop(c Counter) returns (Count, Count)
	return c.bump()
end 'oneHop'

function twoHops(c Counter) returns (Count, Count)
	return oneHop(c)
end 'twoHops'

function main() returns ExitCode
	var c = Counter.create()
	(_, _) = twoHops(c)
	return c.n
end 'main'
```
```exitcode
1
```

<!-- test: refuses-a-callee-with-no-effect -->
⭐ **THE CONTROL, AND IT MUST STAY RED.** The four cases above prove the rule stopped refusing things it
should not; this one proves it still refuses what it should. A callee that only builds and returns a
value has no reason to run when nothing takes the result.
```maxon
typealias Count = int(i64.min to i64.max)

function pair(a Count, b Count) returns (Count, Count)
	return (a, b)
end 'pair'

function main() returns ExitCode
	(_, _) = pair(1, b: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3064: <fragment>:9:2: result of pure function 'pair' must be used
```
