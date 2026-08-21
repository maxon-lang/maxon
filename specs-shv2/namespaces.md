---
feature: namespaces
status: stable
keywords: [namespace, organization, scope, export]
category: organization
---

# Namespaces

## Documentation

Namespaces are derived from the file's location in the directory structure. Functions can be exported to make them available to other files.

### File-Based Namespaces

The namespace of a file is determined by its path:
- `math.maxon` in root → no namespace (global)
- `utils/helpers.maxon` → namespace `utils`
- `stdlib/fmt/integer.maxon` → namespace `stdlib.fmt`

### Export Keyword

Use `export` to make functions visible outside the file:

```maxon
typealias Score = int(i64.min to i64.max)

export function public_add(a Score, b Score) returns Score
	return a + b
end 'public_add'

function private_helper(x Score) returns Score
	return x * 2
end 'private_helper'
```
Only `public_add` can be called from other files. `private_helper` is file-private.

### Example

File: `math/operations.maxon`

```maxon
typealias Score = int(i64.min to i64.max)

function add(a Score, b Score) returns Score
	return a + b
end 'add'

function multiply(x Score, y Score) returns Score
	return x * y
end 'multiply'

function main() returns ExitCode
	return add(3, b: 4)  // Called from within same file
end 'main'
```
```exitcode
7
```


## Tests

<!-- test: basic-namespace -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(10, b: 20)
end 'main'
```
```exitcode
30
```


<!-- test: multiple-functions -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	return double(5) + triple(4)
end 'main'
```
```exitcode
22
```


<!-- test: nested-calls-in-namespace -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function sum_three(a Integer, b Integer, c Integer) returns Integer
	return add(add(a, b: b), b: c)
end 'sum_three'

function main() returns ExitCode
	return sum_three(1, b: 2, c: 3)
end 'main'
```
```exitcode
6
```


<!-- test: cross-file-bare-name-resolves -->
A bare call from `app/main.maxon` finds an exported function in a sibling directory `utils/helper.maxon` via cross-file resolution.
```maxon
// --- file: utils/helper.maxon
typealias Integer = int(i64.min to i64.max)

export function bareHelper() returns Integer
	return 42
end 'bareHelper'

// --- file: app/main.maxon
function main() returns ExitCode
	return bareHelper()
end 'main'
```
```exitcode
42
```


<!-- test: cross-file-qualified-name-resolves -->
A qualified call `utils.helper()` from `app/main.maxon` resolves to the function declared in `utils/helper.maxon`. The directory name is the module namespace.
```maxon
// --- file: utils/helper.maxon
typealias Integer = int(i64.min to i64.max)

export function qualifiedHelper() returns Integer
	return 42
end 'qualifiedHelper'

// --- file: app/main.maxon
function main() returns ExitCode
	return utils.qualifiedHelper()
end 'main'
```
```exitcode
42
```


<!-- test: same-directory-bare-name-sees-sibling -->
Two files in the same directory `utils/` share a module namespace. A function in `utils/b.maxon` calls a function in `utils/a.maxon` with no qualifier because they belong to the same module. The producer uses `module` visibility so it is visible across files inside the `utils/` subtree but not to callers outside it; the consumer (`export`ed) is the only entry point from `app/main.maxon`.
```maxon
// --- file: utils/a.maxon
typealias Integer = int(i64.min to i64.max)

module function siblingProducer() returns Integer
	return 21
end 'siblingProducer'

// --- file: utils/b.maxon
typealias Integer = int(i64.min to i64.max)

export function siblingConsumer() returns Integer
	return siblingProducer() + siblingProducer()
end 'siblingConsumer'

// --- file: app/main.maxon
function main() returns ExitCode
	return siblingConsumer()
end 'main'
```
```exitcode
42
```


<!-- test: multi-segment-namespace-resolves -->
A function declared in a nested user directory `lib/inner/leaf.maxon` is reachable via its full multi-segment qualified name `lib.inner.deepHelper`. The parser walks the dotted chain greedily and resolves against the registered function name; if the qualified callee matches `funcReturnTypes` it routes through the qualified-call path without first looking for a struct or local variable.
```maxon
// --- file: lib/inner/leaf.maxon
typealias Integer = int(i64.min to i64.max)

export function deepHelper() returns Integer
	return 42
end 'deepHelper'

// --- file: app/main.maxon
function main() returns ExitCode
	return lib.inner.deepHelper()
end 'main'
```
```exitcode
42
```


<!-- test: error.cross-file-bare-name-ambiguous -->
When two different directories both export a function with the same bare name, a third file's unqualified call is ambiguous. E3095 instructs the user to qualify the call with the appropriate directory namespace. shv2 emits exactly the message pinned below and this suite runs the case (measured 2026-08-06, BATCH29/A3a). The `/specs` copy of this case is suspended and says so there: the C# bootstrap reports an equivalent E3007 overload-ambiguity at a different point in the pipeline.
```maxon
// --- file: alpha/dup.maxon
typealias Integer = int(i64.min to i64.max)

export function duplicate() returns Integer
	return 1
end 'duplicate'

// --- file: beta/dup.maxon
typealias Integer = int(i64.min to i64.max)

export function duplicate() returns Integer
	return 2
end 'duplicate'

// --- file: app/main.maxon
function main() returns ExitCode
	return duplicate()
end 'main'
```
```maxoncstderr
error E3095: app/specs/fragments/namespaces/error.cross-file-bare-name-ambiguous.test:18:9: Ambiguous bare-name call to 'duplicate': multiple visible definitions found. Qualify with a directory name. Candidates: alpha.duplicate, beta.duplicate
```

<!-- test: bare-sibling-instance-method-call-injects-self -->
A bare call to a SIBLING instance method from inside another method of the same
type binds to that method with the enclosing `self` injected as the implicit
receiver — `bump(amount)` inside `bumpTwice` means `self.bump(amount)`. The call
lowers to a plain `call` op carrying only the user argument; the receiver is
supplied at lowering, not written at the call site. Argument validation must
align the lone user arg past the implicit `__self` slot (param 1), not against
`__self` itself, or it spuriously rejects `amount` as the wrong type for the
receiver. Mirrors the compiler's own `IrModule.addBlock` calling its sibling
`IrModule.createAndRegisterBlock` bare.
```maxon
typealias Count = int(0 to 100)

type Counter
	var total as Count

	static function make() returns Counter
		return Counter{total: 0}
	end 'make'

	function bump(amount Count) returns Count
		self.total = self.total + amount
		return self.total
	end 'bump'

	export function bumpTwice(amount Count) returns Count
		let first = bump(amount)
		return bump(first)
	end 'bumpTwice'
end 'Counter'

function main() returns ExitCode
	var c = Counter.make()
	return c.bumpTwice(5)
end 'main'
```
```exitcode
10
```


<!-- test: bare-call-resolves-free-fn-over-different-arity-method -->
A bare call binds a FREE function — a method (`TypeName.method`) needs an
explicit receiver. When a same-named method has a DIFFERENT non-receiver
signature, the bare call resolves to the free function by argument matching
rather than colliding with the method. Here free `store(arr, index:, value:)`
(3 params, `lib/free.maxon`) coexists with the method `Cache.store(key)` (1
param, `lib/cache.maxon`); a bare `store(xs, index: 0, value: 9)` from a third
file resolves to the free function. Mirrors the compiler's own `set(arr, index:,
value:, sentinel:)` free function coexisting with the `Array.set` method.
```maxon
// --- file: lib/free.maxon
typealias Code = int(0 to 125)

export function store(base Code, offset Code, scale Code) returns Code
	return base + offset + scale
end 'store'

// --- file: lib/cache.maxon
typealias Code = int(0 to 125)

export type Cache
	export var last as Code

	export static function make() returns Cache
		return Cache{last: 0}
	end 'make'

	export function store(key Code)
		self.last = key
	end 'store'
end 'Cache'

// --- file: app/main.maxon
function main() returns ExitCode
	let viaFree = store(2, offset: 3, scale: 4)
	var c = Cache.make()
	c.store(99)
	return viaFree
end 'main'
```
```exitcode
9
```


⭐⭐ **A ROOT DECLARATION OWNS THE BARE KEY, AND THE FOUR CASES BELOW ARE WHAT THAT MEANS.** N1c
qualifies a contested free function's registration name with its module directory — but the root
has no qualifier, so a root declaration's registration name IS the bare name and a bare call
reaches it. The whole-program declaration sweep files every declaration under its bare name first
and re-files a contested one afterwards, so while a contest lasts the bare key is written by every
contestant in fold order and the LAST one wins.

⛔ **SELECTION AND TYPING THEN READ ONE FACT FROM TWO PLACES AND GOT TWO ANSWERS.** The merge
registry routed the call to the ROOT's declaration — its parameter types are what a range refusal
named — while the sweep tables handed the call site whatever the last contestant had written there.
MEASURED, all three against the root's own declaration two lines above the call: a value-returning
root function was refused `E2004: Function 'pick' does not return a value`; a `try … otherwise (e)`
bound `e` to the SUBDIRECTORY's error enum and refused the root's own case as
`E3034: unknown enum case`; and a root declaration with NO parameter default silently borrowed the
subdirectory's, so `pick()` ran the root's body on the subdirectory's argument and printed a number
the program does not contain. A wrong ANSWER, not a diagnostic.

The premise that had made this look safe was written down — *"which of the stale bare entries
survives does not matter: the only call that reads one is a bare call to a contested name, and that
call is E3095"* — and it holds only when every contestant is in a SUBDIRECTORY. A root declaration
contributes no qualified spelling, so the candidate set can never reach the two E3095 requires, no
ambiguity is reported, and the call proceeds to read the stale entry.

<!-- test: root-declaration-owns-the-bare-key -->
A root `pick` that RETURNS a value, contested by a subdirectory's VOID `pick` declared after it.
The bare call is the root's, so it has a value to bind.
```maxon
// --- file: main.maxon
typealias Ms = int(0 to 1000)

function pick(milliseconds Ms) returns Ms
	return milliseconds + 7
end 'pick'

function main() returns ExitCode
	let r = pick(1)
	print("r={r}\n")
	return 0
end 'main'

// --- file: sub/helper.maxon
typealias Slot = int(0 to 100)

public function pick(slot Slot)
	print("sub {slot}\n")
end 'pick'
```
```exitcode
0
```
```stdout
r=8
```


<!-- test: root-declaration-owns-the-bare-key-whatever-the-fold-order -->
The same program with the SUBDIRECTORY declared first. This is the ordering control: it passed
while the case above failed, which is precisely what identified the bare key's last-writer-wins as
the fault rather than the contest itself.
```maxon
// --- file: sub/helper.maxon
typealias Slot = int(0 to 100)

public function pick(slot Slot)
	print("sub {slot}\n")
end 'pick'

// --- file: main.maxon
typealias Ms = int(0 to 1000)

function pick(milliseconds Ms) returns Ms
	return milliseconds + 7
end 'pick'

function main() returns ExitCode
	let r = pick(1)
	print("r={r}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=8
```


<!-- test: root-declaration-owns-the-bare-key-throws-clause -->
`returnTypes` is not the only table keyed by the bare name. The `throws` clause is what types the
`(e)` binding of a `try … otherwise`, so a root declaration whose clause was overwritten by a
subdirectory's bound `e` to the wrong enum and refused the root's own case.
```maxon
// --- file: main.maxon
enum RootError
	rootBad
end 'RootError'

typealias Ms = int(0 to 1000)

function pick(milliseconds Ms) returns Ms throws RootError
	if milliseconds > 500 'tooBig'
		throw RootError.rootBad
	end 'tooBig'
	return milliseconds + 7
end 'pick'

function main() returns ExitCode
	let r = try pick(1) otherwise (e) 'failed'
		match e 'which'
			rootBad then break 'which'
		end 'which'
		return 9
	end 'failed'
	print("r={r}\n")
	return 0
end 'main'

// --- file: sub/helper.maxon
enum SubError
	subBad
end 'SubError'

typealias Slot = int(0 to 100)

public function pick(slot Slot) returns Slot throws SubError
	if slot > 50 'tooBig'
		throw SubError.subBad
	end 'tooBig'
	return slot
end 'pick'
```
```exitcode
0
```
```stdout
r=8
```


<!-- test: error.root-declaration-owns-the-bare-key-parameter-defaults -->
Parameter defaults are recorded ONLY by a declaration that has one, so a root declaration with no
default left the bare key holding the SUBDIRECTORY's. `pick()` then compiled, ran the ROOT's body
on the subdirectory's default and printed `r=97` — a number the program does not contain. The root
declares one required parameter and omitting it is E3036.
```maxon
// --- file: main.maxon
typealias Ms = int(0 to 1000)

function pick(milliseconds Ms) returns Ms
	return milliseconds + 7
end 'pick'

function main() returns ExitCode
	let r = pick()
	print("r={r}\n")
	return 0
end 'main'

// --- file: sub/helper.maxon
typealias Slot = int(0 to 100)

export function pick(slot Slot = 90) returns Slot
	return slot
end 'pick'
```
```maxoncstderr
error E3036: specs/fragments/namespaces/error.root-declaration-owns-the-bare-key-parameter-defaults.test:10:10: 'pick' expects 1 argument(s) but 0 were provided
```
