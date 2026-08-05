---
feature: redundant-contains-get-module-storage
status: stable
keywords: contains, get, lint, global, module-storage, mutability
category: language
---
# The Redundant `contains`/`get` Lint Over Module Storage

## Documentation

`if-try.md` states the rule: `if x.contains(k)` followed by `try x.get(k)` on the same path is two
lookups where one will do, and it is refused (E3087). The lint suppresses itself at anything in the
then-block that could have invalidated the membership answer — a write to a name in either path, or
another call on the same receiver.

That suppression scan can only see code it can connect to the path. **A top-level `var` breaks that
premise**: its storage is reachable from every function in the program, so any call in the
then-block may `upsert` into the very table the `contains` probed under a receiver spelling this
walk cannot connect to it. Reporting one would be a false accusation, so a path rooted in a top-level
`var` is not linted.

**The boundary is MUTABILITY and not "the base is a local".** A top-level `let` refuses mutation at
every door — `m.upsert(…)` on it directly and `fill(m)` into a mutating parameter are both E3019 —
so nothing in a then-block can invalidate a membership answer probed through one, and a key that is
a top-level `let` cannot be rewritten either. Both of those are exactly what the lint is for, and
both were skipped while the test was "is the base a local or a capture".

Both reference compilers skip the whole global family, because their lint runs over the lowered IR
and their `BuildAccessPath` / `buildAccessPath` cannot canonicalize a global load at all. shv2's
runs in the parser, which holds the tokens AND the declaration — so it can tell the two apart, and
this is a case where the token-level design is SHARPER than what the references express structurally
rather than merely equivalent to it.

## Tests

<!-- test: error.let-global-receiver-is-linted -->
A top-level `let` container cannot be mutated by anything, so the second lookup is redundant and is
reported.
```maxon
typealias StrMap = Map with (String, String)

let m = StrMap.create()

function main() returns ExitCode
	let k = "a"
	if m.contains(k) 'has'
		print(try m.get(k) otherwise "?")
	end 'has'
	return 0
end 'main'
```
```maxoncstderr
error E3087: <fragment>:8:7: redundant 'Map.contains' followed by 'Map.get' on 'm': use 'if let v = try m.get(k)' (or 'if var') instead — performs one lookup instead of two
```

<!-- test: error.let-global-key-is-linted -->
The KEY side of the same boundary: the receiver is a parameter and the key is a top-level `let`, so
neither can be rewritten between the two lookups.
```maxon
typealias StrMap = Map with (String, String)

let KEY = "a"

function probe(t StrMap) returns ExitCode
	if t.contains(KEY) 'has'
		print(try t.get(KEY) otherwise "?")
	end 'has'
	return 0
end 'probe'

function main() returns ExitCode
	let m = StrMap.create()
	return probe(m)
end 'main'
```
```maxoncstderr
error E3087: <fragment>:7:7: redundant 'Map.contains' followed by 'Map.get' on 't': use 'if let v = try t.get(KEY)' (or 'if var') instead — performs one lookup instead of two
```

<!-- test: var-global-receiver-is-not-linted -->
The suppression this file exists to keep: the SAME program with `var` compiles, because a call in
the then-block could have written the table through a spelling the token walk cannot connect to the
receiver.
```maxon
typealias StrMap = Map with (String, String)

var m = StrMap.create()

function main() returns ExitCode
	let k = "a"
	if m.contains(k) 'has'
		print(try m.get(k) otherwise "?")
	end 'has'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: var-global-key-is-not-linted -->
And the key side of the suppression: a top-level `var` key can be reassigned by any function, so the
two lookups may legitimately probe different keys.
```maxon
typealias StrMap = Map with (String, String)

var key = "a"

function probe(t StrMap) returns ExitCode
	if t.contains(key) 'has'
		print(try t.get(key) otherwise "?")
	end 'has'
	return 0
end 'probe'

function main() returns ExitCode
	let m = StrMap.create()
	return probe(m)
end 'main'
```
```exitcode
0
```
