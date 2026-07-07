---
feature: match-enum-or-pattern
status: experimental
keywords: [match, enum, or, pattern, alternative, dispatch]
category: control-flow
---

# Match Enum Or-Patterns

## Documentation

A match arm may list several enum cases separated by `or`; the arm fires when
the scrutinee equals **any** of the listed alternatives:

```text
match cls 'm'
	freshRc0 or selfReturnBorrow or incomingOwner gives true
	notManaged or callReturnRc1 or borrowed gives false
end 'm'
```

Every alternative in the `or`-list must be tested, not just the first. This
holds for both the expression form (`gives`) and the statement form (`then`),
and regardless of whether the case ordinals are contiguous.

## Tests

<!-- test: interleaved-classification -->
Every alternative of an enum or-arm must route to the arm body, even when the
covered ordinals interleave with the other arm's ordinals. This mirrors the
compiler's own `returnedValueClassIsAcquired` (the arm covers ordinals 1, 3, 4).
```maxon
enum OwnershipClass
	notManaged
	freshRc0
	callReturnRc1
	selfReturnBorrow
	incomingOwner
	borrowed
end 'OwnershipClass'

function classIsAcquired(c OwnershipClass) returns bool
	return match c 'm'
		freshRc0 or selfReturnBorrow or incomingOwner gives true
		notManaged or callReturnRc1 or borrowed gives false
	end 'm'
end 'classIsAcquired'

function main() returns ExitCode
	for c in OwnershipClass.allCases 'e'
		print("{c.name}={classIsAcquired(c)}\n")
	end 'e'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
notManaged=false
freshRc0=true
callReturnRc1=false
selfReturnBorrow=true
incomingOwner=true
borrowed=false
```

<!-- test: expr-second-term -->
The second alternative of a three-term or-arm (expression form) fires the arm.
```maxon
enum E
	a
	b
	c
	d
end 'E'

function pick(v E) returns ExitCode
	return match v 'm'
		a or b or c gives 7
		d gives 9
	end 'm'
end 'pick'

function main() returns ExitCode
	return pick(E.b)
end 'main'
```
```exitcode
7
```

<!-- test: expr-third-term -->
The third alternative of a three-term or-arm (expression form) fires the arm.
```maxon
enum E
	a
	b
	c
	d
end 'E'

function pick(v E) returns ExitCode
	return match v 'm'
		a or b or c gives 7
		d gives 9
	end 'm'
end 'pick'

function main() returns ExitCode
	return pick(E.c)
end 'main'
```
```exitcode
7
```

<!-- test: stmt-form-all-terms -->
Statement-form or-arms must test every alternative in both arms.
```maxon
enum E
	a
	b
	c
	d
	e
end 'E'

typealias Tag = int(0 to 9)

function classify(v E) returns Tag
	var r = 0 as Tag
	match v 'm'
		a or c or e then r = 1
		b or d then r = 2
	end 'm'
	return r
end 'classify'

function main() returns ExitCode
	for x in E.allCases 'e'
		print("{x.name}={classify(x)}\n")
	end 'e'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=1
b=2
c=1
d=2
e=1
```

<!-- test: two-term-both-fire -->
Both alternatives of a two-term or-arm fire the arm (contiguous ordinals).
```maxon
enum E
	c0
	c1
	c2
	c3
end 'E'

function isMid(v E) returns bool
	return match v 'm'
		c1 or c2 gives true
		c0 or c3 gives false
	end 'm'
end 'isMid'

function main() returns ExitCode
	for x in E.allCases 'e'
		print("{x.name}={isMid(x)}\n")
	end 'e'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
c0=false
c1=true
c2=true
c3=false
```
