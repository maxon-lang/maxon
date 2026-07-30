---
feature: cross-file-union-keyword-case-range
status: experimental
keywords: [enum, match, range, keyword-case, cross-file]
category: type-system
---

# Cross-File Enum Keyword Case Access and Range Match

## Documentation

An enum case may be spelled with a keyword (`float`, `int`, `bool`, `string`).
Accessing such a case as `TypeName.float` and matching it with a `caseA to caseB`
range arm must work even when the declaring enum lives in a file that has not
been parsed yet at the reference site. The parser defers the qualified read to
TypeResolution and carries the raw endpoint case names on the range arm so the
owning enum and case ordinals are recovered once every file is parsed — rather
than dropping a keyword-spelled member from the qualified name (which would
surface as "Undefined variable 'TypeName'") or collapsing a range arm to its
start case (which would surface as a spurious non-exhaustive match and a -1
ordinal in the runtime range check).

## Tests

### Keyword case access and contiguous range match across files

<!-- test: cross-file-keyword-case-and-range -->
```maxon
// --- file: kind.maxon
export enum Kind
	boolean
	integer
	float
	named
	function
	unresolved
	other
end 'Kind'

// --- file: main.maxon
function isScalar(k Kind) returns bool
	return match k 'check'
		boolean to float gives true
		named to other gives false
	end 'check'
end 'isScalar'

function pickFloat() returns Kind
	return Kind.float
end 'pickFloat'

function pickBoolean() returns Kind
	return Kind.boolean
end 'pickBoolean'

function pickNamed() returns Kind
	return Kind.named
end 'pickNamed'

function main() returns ExitCode
	var score = 0
	if isScalar(pickFloat()) 'f'
		score = score + 1
	end 'f'
	if isScalar(pickBoolean()) 'b'
		score = score + 2
	end 'b'
	if isScalar(pickNamed()) 'n'
		score = score + 4
	end 'n'
	return score as ExitCode
end 'main'
```
```exitcode
3
```


<!-- disabled-test: cross-file-nested-match-result-scrutinee -->
<!-- needs a nested payload-bearing union payload destructor cascade (E2015) -->
The result of a `match` whose arms construct values of a CROSS-FILE union is
itself matched. A cross-file union case construction (`MyType.named(7)`,
`MyType.unresolved`) is emitted as a constructor call, not an inline
`enumConstruct`, so its result type only resolves once the case constructor's
registered return type is consulted — which can lag the producer-type walk of
the match-merge block. The merge result's type must converge to the union so the
SECOND `match` sees an enum-typed scrutinee rather than the parser's int seed
(otherwise E3005 "match scrutinee must be an enum-typed value"). Mirrors the
compiler's own `returnTypeInterfaceName` / `matchLayoutLoad`:
`let raw = match ref { ... gives Enum.case }; match raw { ... }`.
```maxon
// --- file: types.maxon
export typealias Dim = int(0 to 100)

export union MyType
	unresolved
	named(id Dim)
	other(x Dim)
end 'MyType'

export union Wrapper
	void
	value(inner MyType)
end 'Wrapper'

// --- file: main.maxon
function classify(ref Wrapper) returns Dim
	let raw = match ref 'unwrap'
		void gives MyType.unresolved
		value(inner) gives inner
	end 'unwrap'
	return match raw 'r'
		named(id) gives id
		other(x) gives x
		unresolved gives 0
	end 'r'
end 'classify'

function main() returns ExitCode
	return classify(Wrapper.value(MyType.named(7)))
end 'main'
```
```exitcode
7
```
