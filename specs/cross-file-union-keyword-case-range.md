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
