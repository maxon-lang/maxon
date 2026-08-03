---
feature: param-default-refusals
status: selfhosted
keywords: [default, default-values, parameters, closure, interface, overload, duplicate]
category: core
---

# Where a Parameter Default Has No Name to Be Published Under

## Documentation

A parameter default is compiled as a synthesized nullary function, and a call site that omits the
argument emits a call to it. Everything therefore hangs off ONE thing: a name the call site can look the
declaration up by. Three declaration forms cannot supply one, and a fourth supplies one that is not the
declaration's alone — each is refused where the `=` is written, rather than mis-served.

- A CLOSURE literal is called indirectly, through a function value; the call site has no callee name.
- An INTERFACE requirement is in no signature registry; a witness dispatch slots against the interface's
  formals.
- An OVERLOADED name is resolved a whole pass after the argument is filled, so a short call would be
  filled from whichever declaration the by-name sweep recorded last.
- A name DECLARED TWICE, each declaration defaulting something, has the same problem by accident rather
  than by intent — and is refused at the `=` because the duplicate-definition diagnostic (E3006) is only
  reported once every file has parsed, which is after the losing declaration's own helper is built.

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
	export function greet(times Integer) returns Integer
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

<!-- test: error-default-on-overloaded-name -->
```maxon
typealias Integer = int(i64.min to i64.max)

function show(n Integer = 1) returns Integer
	return n
end 'show'

function show(s String) returns Integer
	return 2
end 'show'

function main() returns ExitCode
	return show()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:10: Unsupported: overloading 'show' — one of its declarations gives a parameter a DEFAULT VALUE, and the whole-program declaration sweep publishes defaults under the name the source wrote, so a call that omits an argument cannot be told which overload's default to supply. The argument is filled when the call is parsed and the overload is resolved a whole pass later, so nothing downstream can repair it. Give the overloads distinct names, or drop the default
```

<!-- test: error-default-on-a-name-declared-twice -->
Both declarations default a parameter, so neither is the overload set's sole owner of the name. Without
this refusal the losing declaration's drain reads the winner's shape out of the by-name registry, finds
no helper at its own parameter's position, and the compiler PANICS with no source position at all — while
the identical program with the defaults removed answers a clean E3006.
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
error E2015: <fragment>:4:22: Unsupported: a default value on parameter 'a' of 'f' — that name is declared more than once in this program, each declaration with its own parameter defaults, and the whole-program declaration sweep publishes defaults under the name the source wrote, so a call that omits an argument cannot be told which declaration's default to supply. Remove the duplicate declaration, or drop the default
```

<!-- test: error-param-default-trailing-tokens -->
The capture walks to the `,` or `)` that ends the default, and the expression the drain parses out of
that region has to reach it. Anything left over is text the author wrote and the compiler was about to
ignore — `b Integer = 7 zzz` silently defaulted to 7. The bootstrap dropped it too, and now neither does.
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
error E2010: <fragment>:4:37: Expected 'end of default value' but got 'identifier'
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
error E2010: <fragment>:4:30: Expected 'end of default value' but got 'identifier'
```
