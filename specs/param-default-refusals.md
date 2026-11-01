---
feature: param-default-refusals
status: selfhosted
keywords: [default, default-values, parameters, closure, interface]
category: core
---

# Where a Parameter Default Has No Name to Be Published Under

## Documentation

A parameter default is compiled as a synthesized nullary function, and a call site that omits the
argument emits a call to it. Everything therefore hangs off ONE thing: a name the call site can look the
declaration up by. Three declaration forms cannot supply one, and each is refused rather than mis-served.

- A CLOSURE literal is called indirectly, through a function value; the call site has no callee name.
  It is refused where the `=` is written.
- An INTERFACE requirement is in no signature registry; a witness dispatch slots against the interface's
  formals. It is refused where the `=` is written.
- A SECOND declaration of a name. The name's defaults are published from its first declaration, so the
  second has no helper of its own to be read back. It is refused as the duplicate definition (E3006), at its
  name, ahead of the whole-program duplicate check that is reported once every file has parsed.

These cases exist because a refusal nothing runs is a claim, not a door.

## Tests

<!-- test: error-default-on-closure-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let f = function(n Integer = 3) returns Integer
		gives n
	end 'f'
	return f(2)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:29: Unsupported: a default value on closure parameter 'n' — a closure is called INDIRECTLY, through a function value, so no call site has a callee name to look the default up by. Give the closure's caller the value explicitly
```

<!-- test: error-default-on-interface-requirement -->
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(times Integer = 2) returns Integer
end 'Greeter'

type Loud implements Greeter
	function greet(times Integer) returns Integer
		return times
	end 'greet'
end 'Loud'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:31: Unsupported: a default value on interface requirement parameter 'times' — a witness dispatch slots its arguments against the INTERFACE's formals, which are in no signature registry, so no dispatch site can read a default from the requirement. Declare the default on each conforming type's method
```

<!-- test: error-default-on-a-name-declared-twice -->
Both declarations default a parameter and the two shapes DIFFER — one parameter against two. The second is
refused as the duplicate it is. Left to the whole-program duplicate check, which waits for every file to parse,
the second declaration's own helper would be read back from the first's shape, find nothing at its
parameter's position, and the compiler would PANIC with no source position.
```maxon
typealias Integer = int(i64.min to i64.max)

function f(a Integer = 1) returns Integer
	return a
end 'f'

function f(x Integer, y Integer = 2) returns Integer
	return x + y
end 'f'

function main() returns ExitCode
	return f()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:10: duplicate definition of 'f' — already declared as `function f`
```

<!-- test: two-files-agreeing-on-their-defaults-report-ONE-duplicate -->
Two files declaring one `f` with the SAME default shape are one duplicate, and they must earn exactly ONE
E3006. A default is compiled as a synthesized function named after the declaration that owns it, so the
duplicate `f` drags a duplicate `__paramDefault#f#0` behind it; reported, that second E3006 would explain a
symbol absent from the source.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export function f(a Integer = 1) returns Integer
	return a
end 'f'

// --- file: b.maxon
export function f(a Integer = 1) returns Integer
	return a + 1
end 'f'

// --- file: main.maxon
function main() returns ExitCode
	return f() as ExitCode
end 'main'
```
```maxoncstderr
error E3006: <fragment>:10:17: duplicate definition of 'f' — already declared as `function f`
```

<!-- test: error-default-on-a-duplicate-in-a-directory-a-second-directory-contests -->
The same refusal in a directory whose bare name a second directory also declares. `beta/` declares a `pick`
of its own, so `alpha/`'s declarations are registered under `alpha.pick`, and the first declaration's place
in the registry has to travel with them: the second `alpha` declaration is still the duplicate.
```maxon
// --- file: alpha/a.maxon
export typealias Num = int(-1000 to 1000)

export function pick(a Num) returns Num
	return a
end 'pick'

export function pick(a Num, b Num = 5) returns Num
	return a * 100 + b + 5
end 'pick'

// --- file: beta/b.maxon
export typealias Small = int(-1000 to 1000)

export function pick(a Small) returns Small
	return a + 50
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return alpha.pick(2) as ExitCode
end 'main'
```
```maxoncstderr
error E3006: alpha/<fragment>:9:17: duplicate definition of 'pick' — already declared as `function pick`
```

<!-- test: error-param-default-trailing-tokens -->
The capture walks to the `,` or `)` that ends the default, and the expression the drain parses out of
that region has to reach it. Anything left over is text the author wrote and the compiler was about to
ignore — dropped, `b Integer = 7 zzz` would silently default to 7.
```maxon
typealias Integer = int(i64.min to i64.max)

function f(a Integer, b Integer = 7 zzz) returns Integer
	return a + b
end 'f'

function main() returns ExitCode
	return f(1)
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:37: Expected 'end of default value' but got 'zzz'
```

<!-- test: error-param-default-exponent-without-point -->
`1e100` is the integer `1` followed by the identifier `e100` — a float literal must contain a decimal
point. Dropped, it made `b Real = 1e100` default to `1`: a hundred orders of magnitude, silently.
Write `1.0e100`.
```maxon
typealias Real = float(f64.min to f64.max)

function f(a Real, b Real = 1e100) returns Real
	return a + b
end 'f'

function main() returns ExitCode
	print("{f(1.0)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:30: Expected 'end of default value' but got 'e100'
```
