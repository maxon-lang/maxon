---
feature: settled-results-through-merges
status: experimental
keywords: [let, capability, result, merge, rebind, immutable parameter, consumed argument, mixed getter, E3197, E3207, E3209]
category: semantics
---

# A result that settles after parsing stays writable through a copy, a merge and a frozen argument

## Documentation

A call result is judged read-only or writable once every body has been read, by which of the callee's
parameters the result can hold. An argument handed to a parameter declared `immutable` is not one of them: the
callee cannot hand it back, so passing frozen data there never makes the result read-only.

That judgement follows the result wherever the author carries it: into the copy a callee that keeps its
parameter receives, into the copy a `return` hands back, and into a variable that is rebound across a branch or
a loop beside a parameter or a plain local. A `match` or ternary that gives a parameter beside frozen data
fits an `immutable` result, because the parameter is frozen by the result.

A function whose return paths mix frozen data (its own, such as an `immutable` field, a module `let` or a
`static let`) with writable or fresh data is a mixed getter, and its result is read-only for every caller,
whatever the receiver or the arguments are: writing through it is **E3197**, passing it to a `mutable`
parameter is **E3207**, and moving it into an `isolated` position is **E3200**. A single `return` whose
`if … else`, ternary or `match` joins frozen and writable data counts as mixed too, and a function that
returns a mixed result unchanged is mixed. Declare the getter `returns immutable` when every path is frozen,
or return a `.clone()` of the frozen path when the caller must write.

## Tests

<!-- test: a-result-handed-to-a-keeping-callee-is-writable -->
`declare` clones what it files and returns what it built; `Candidate.create` keeps `binding` in a field, so the
caller must be able to write `binding`.

```maxon
typealias Slot = int(0 to 100)
typealias InfoArray = Array with Info
typealias CandidateArray = Array with Candidate

type Info
	export let name as immutable ByteArray
	export var kind as Slot

	static function create(name immutable ByteArray, kind Slot) returns Info
		return Self{name: name, kind: kind}
	end 'create'
end 'Info'

type Scope
	var infos as InfoArray

	static function create() returns Scope
		return Self{infos: InfoArray.create(0, zeroed: false)}
	end 'create'

	function declare(name immutable ByteArray, kind Slot) returns Info
		let made = Info.create(name, kind: kind)
		self.infos.push(made.clone())
		return made
	end 'declare'
end 'Scope'

type Candidate
	export var binding as Info
	export let line as Slot

	static function create(binding Info, line Slot) returns Candidate
		return Self{binding: binding, line: line}
	end 'create'
end 'Candidate'

type Walker
	export var scope as Scope
	export var candidates as CandidateArray

	static function create() returns Walker
		return Self{scope: Scope.create(), candidates: CandidateArray.create(0, zeroed: false)}
	end 'create'

	function declareParam(name immutable ByteArray, kind Slot, line Slot)
		let binding = self.scope.declare(name, kind: kind)
		self.candidates.push(Candidate.create(binding, line: line))
	end 'declareParam'
end 'Walker'

function main() returns ExitCode
	let walker = Walker.create()
	walker.declareParam(b"a", kind: 1, line: 2)
	print("{walker.candidates.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-result-the-callee-also-stored-is-writable-to-its-keeper -->
The callee files `made` itself and returns it too; the caller still keeps the result in a field.

```maxon
typealias Slot = int(0 to 100)
typealias InfoArray = Array with Info
typealias CandidateArray = Array with Candidate

type Info
	export let name as immutable ByteArray
	export var kind as Slot

	static function create(name immutable ByteArray, kind Slot) returns Info
		return Self{name: name, kind: kind}
	end 'create'
end 'Info'

type Scope
	var infos as InfoArray

	static function create() returns Scope
		return Self{infos: InfoArray.create(0, zeroed: false)}
	end 'create'

	function declare(name immutable ByteArray, kind Slot) returns Info
		let made = Info.create(name, kind: kind)
		self.infos.push(made)
		return made
	end 'declare'
end 'Scope'

type Candidate
	export var binding as Info
	export let line as Slot

	static function create(binding Info, line Slot) returns Candidate
		return Self{binding: binding, line: line}
	end 'create'
end 'Candidate'

type Walker
	export var scope as Scope
	export var candidates as CandidateArray

	static function create() returns Walker
		return Self{scope: Scope.create(), candidates: CandidateArray.create(0, zeroed: false)}
	end 'create'

	function declareParam(name immutable ByteArray, kind Slot, line Slot)
		let binding = self.scope.declare(name, kind: kind)
		self.candidates.push(Candidate.create(binding, line: line))
	end 'declareParam'
end 'Walker'

function main() returns ExitCode
	let walker = Walker.create()
	walker.declareParam(b"a", kind: 1, line: 2)
	print("{walker.candidates.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-binding-rebound-beside-a-parameter-is-writable-through-a-callee -->
`current` is the parameter `head` on the way in and a fresh block on each pass; `append` writes it either way.

```maxon
typealias Count = int(0 to 1000)
typealias BlockArray = Array with Block

type Block
	export let id as Count
	export let label as immutable ByteArray
	export var hits as Count

	static function create(id Count, label immutable ByteArray) returns Block
		return Self{id: id, label: label, hits: 0}
	end 'create'

	function touch()
		self.hits = self.hits + 1
	end 'touch'
end 'Block'

type Module
	var blocks as BlockArray

	static function create() returns Module
		return Self{blocks: BlockArray.create(0, zeroed: false)}
	end 'create'

	function add(label immutable ByteArray) returns Block
		let made = Block.create(self.blocks.count() as Count, label: label)
		self.blocks.push(made)
		return made
	end 'add'

	function append(block Block)
		block.touch()
	end 'append'
end 'Module'

type Pass
	export var module as Module

	static function create() returns Pass
		return Self{module: Module.create()}
	end 'create'

	function cascade(head Block, rounds Count)
		var current = head

		for i in 0 upto rounds 'eachRound'
			self.module.append(current)

			if i == 1 'repeat'
				self.module.append(current)
			end 'repeat' else 'advance'
				let next = self.module.add(b"next")
				current = next
			end 'advance'
		end 'eachRound'
	end 'cascade'
end 'Pass'

function main() returns ExitCode
	let pass = Pass.create()
	let head = pass.module.add(b"head")
	pass.cascade(head, rounds: 3)
	print("{head.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-field-is-written-through-a-binding-rebound-beside-a-parameter -->
The same rebinding, written through directly rather than handed to a callee.

```maxon
typealias Count = int(0 to 1000)
typealias BlockArray = Array with Block

type Block
	export let id as Count
	export let label as immutable ByteArray
	export var hits as Count

	static function create(id Count, label immutable ByteArray) returns Block
		return Self{id: id, label: label, hits: 0}
	end 'create'
end 'Block'

type Module
	var blocks as BlockArray

	static function create() returns Module
		return Self{blocks: BlockArray.create(0, zeroed: false)}
	end 'create'

	function add(label immutable ByteArray) returns Block
		let made = Block.create(self.blocks.count() as Count, label: label)
		self.blocks.push(made)
		return made
	end 'add'
end 'Module'

type Pass
	export var module as Module

	static function create() returns Pass
		return Self{module: Module.create()}
	end 'create'

	function mark(head Block, rounds Count)
		var current = head

		for i in 0 upto rounds 'eachRound'
			current.hits = 5

			if i == 1 'repeat'
				current.hits = 6
			end 'repeat' else 'advance'
				let next = self.module.add(b"next")
				current = next
			end 'advance'
		end 'eachRound'
	end 'mark'
end 'Pass'

function main() returns ExitCode
	let pass = Pass.create()
	let head = pass.module.add(b"head")
	pass.mark(head, rounds: 3)
	print("{head.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-ternary-beside-an-immutable-field-read-does-not-freeze-its-holder -->
`joined.alias` is read out of an `immutable` slot, so choosing it beside another frozen value asks nothing of
`joined` itself, and a `var` holding a fresh `Join` can be handed to `step` on every pass.

```maxon
type Join
	export let alias as immutable ByteArray

	static function create(alias immutable ByteArray) returns Self
		return Self{alias: alias}
	end 'create'
end 'Join'

function longer(left immutable ByteArray, right immutable ByteArray) returns immutable ByteArray
	return left if left.count() > right.count() else right
end 'longer'

function step(joined Join, baseline immutable ByteArray, give immutable ByteArray) returns Join
	return Join.create(longer(baseline if joined.alias.isEmpty() else joined.alias, right: give))
end 'step'

function main() returns ExitCode
	var joined = Join.create(b"")

	for _ in 0 upto 3 'eachPass'
		joined = step(joined, baseline: b"ab", give: b"abc")
	end 'eachPass'

	print("{joined.alias.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: a-ternary-of-a-parameter-and-a-constant-fits-an-immutable-result -->
```maxon
function pick(name ByteArray) returns immutable ByteArray
	return name if name.count() > 3 else b"default"
end 'pick'

function main() returns ExitCode
	let picked = pick(b"ab")
	print("{picked.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-match-giving-a-parameter-and-a-constant-fits-an-immutable-result -->
```maxon
function pick(name ByteArray) returns immutable ByteArray
	return match name.count() > 3 'sel'
		true gives name
		default gives b"default"
	end 'sel'
end 'pick'

function main() returns ExitCode
	let picked = pick(b"ab")
	print("{picked.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.an-immutable-result-choosing-a-parameter-or-a-constant-refuses-mutable-data -->
`pick` may hand back `name` itself, and its result is `immutable`, so the caller's `live` has to be frozen: otherwise the result would change when `live` does.

```maxon
function pick(name ByteArray, long bool) returns immutable ByteArray
	return name if long else b"default"
end 'pick'

function main() returns ExitCode
	let live = ByteArray.create(4)
	let picked = pick(live, long: true)
	live.push(1)
	print("{picked.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:8:20: 'pick' needs 'name' frozen (line 3: `return name if long else b"default"`); 'live' is mutable. Freeze it first
```

<!-- test: error.an-immutable-result-rebinding-a-parameter-in-a-loop-refuses-mutable-data -->
The same holds when the choice is a variable rebound in a loop.

```maxon
function pick(name ByteArray, rounds Count) returns immutable ByteArray
	var chosen = name

	for _ in 0 upto rounds 'rebind'
		chosen = b"default"
	end 'rebind'

	return chosen
end 'pick'

function main() returns ExitCode
	let live = ByteArray.create(4)
	let picked = pick(live, rounds: 2)
	live.push(1)
	print("{picked.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:14:20: 'pick' needs 'name' frozen (line 9: `return chosen`); 'live' is mutable. Freeze it first
```

<!-- test: an-immutable-result-choosing-a-parameter-accepts-frozen-data-through-a-branch-and-a-loop -->
```maxon
function pickBranch(name ByteArray, long bool) returns immutable ByteArray
	return name if long else b"default"
end 'pickBranch'

function pickLoop(name ByteArray, rounds Count) returns immutable ByteArray
	var chosen = name

	for _ in 0 upto rounds 'rebind'
		chosen = b"default"
	end 'rebind'

	return chosen
end 'pickLoop'

function main() returns ExitCode
	let live = freeze ByteArray.create(4)
	print("{pickBranch(live, long: true).count()} {pickLoop(live, rounds: 0).count()} {pickLoop(live, rounds: 2).count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 4 7
```

<!-- test: a-getter-beside-its-own-frozen-data-is-read-only-for-its-caller -->
`pick` hands back the frozen `shared` box on one path and the writable `own` box on the other, so its result is read-only to every caller: reading it is accepted, writing through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'
end 'Holder'

function total(holder Holder) returns Tally
	let picked = holder.pick(true)

	return picked.n
end 'total'

function main() returns ExitCode
	let holder = Holder.create()
	print("{total(holder)}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-getter-beside-its-own-frozen-data-is-not-writable-for-its-caller -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'
end 'Holder'

function bump(holder Holder)
	var picked = holder.pick(true)
	picked.n = 9
end 'bump'

function main() returns ExitCode
	let holder = Holder.create()
	bump(holder)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:31:2: cannot write 'n' through 'picked', which is read-only
```

<!-- test: a-getter-returning-only-its-own-frozen-data-must-declare-the-result-immutable -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7)}
	end 'create'

	function pick() returns Box
		return self.shared
	end 'pick'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	print("{holder.pick().n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:20:10: a return does not fit the declared result: 'self.shared' is immutable, the result is not `immutable`; declare the result `immutable`
```

<!-- test: a-getter-beside-its-own-frozen-data-is-not-writable-through-a-local-var -->
The caller's receiver is a local `var`, not a parameter, and the mixed getter's result is still read-only.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	var picked = holder.pick(true)
	picked.n = 9
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:32:2: cannot write 'n' through 'picked', which is read-only
```

<!-- test: a-getter-beside-its-own-frozen-data-is-readable-through-a-local-var -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	let picked = holder.pick(true)
	print("{picked.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-getter-of-only-writable-data-stays-writable-through-a-local-var -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	var own as Box

	static function create() returns Self
		return Self{own: Box.create(1)}
	end 'create'

	function mine() returns Box
		return self.own
	end 'mine'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	let picked = holder.mine()
	picked.n = 9
	print("{holder.mine().n}\n")
	return 0
end 'main'
```
```stdout
9
```
```exitcode
0
```

<!-- test: a-mixed-getter-result-is-refused-for-a-mutable-parameter-through-a-local-var -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'
end 'Holder'

function reset(box mutable Box)
	box.n = 0
end 'reset'

function main() returns ExitCode
	var holder = Holder.create()
	reset(holder.pick(true))
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:35:8: 'reset' takes `mutable` 'box' (line 30: `box.n = 0`); 'holder.pick(true)' is read-only
```

<!-- test: a-ternary-of-frozen-and-writable-data-is-not-writable-for-its-caller -->
A single `return` whose ternary gives frozen data on one arm and writable data on the other.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		return self.shared if flag else self.own
	end 'pick'
end 'Holder'

function bump(holder Holder)
	var picked = holder.pick(true)
	picked.n = 9
end 'bump'

function main() returns ExitCode
	let holder = Holder.create()
	bump(holder)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:27:2: cannot write 'n' through 'picked', which is read-only
```

<!-- test: a-match-of-frozen-and-writable-data-is-not-writable-for-its-caller -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		return match flag 'sel'
			true gives self.shared
			default gives self.own
		end 'sel'
	end 'pick'
end 'Holder'

function bump(holder Holder)
	var picked = holder.pick(true)
	picked.n = 9
end 'bump'

function main() returns ExitCode
	let holder = Holder.create()
	bump(holder)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:30:2: cannot write 'n' through 'picked', which is read-only
```

<!-- test: a-ternary-of-frozen-and-writable-data-is-not-writable-through-a-local-var -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	let shared as immutable Box
	var own as Box

	static function create() returns Self
		return Self{shared: freeze Box.create(7), own: Box.create(1)}
	end 'create'

	function pick(flag bool) returns Box
		return self.shared if flag else self.own
	end 'pick'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	var picked = holder.pick(true)
	picked.n = 9
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:28:2: cannot write 'n' through 'picked', which is read-only
```

<!-- test: a-result-mixing-a-module-let-array-with-a-fresh-one-is-not-writable-for-its-caller -->
`get` hands back image data on one path and a fresh array on the other, so its result is read-only for every caller: the call's arguments are all values, and that does not make the result fresh.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

function get(fresh bool) returns Digits
	if fresh 'fresh'
		return Digits.create(0)
	end 'fresh'

	return Primes
end 'get'

function main() returns ExitCode
	let d = get(false)
	d.push(1)
	print("{d.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:17:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-ternary-of-a-fresh-array-and-a-module-let-array-is-not-writable-for-its-caller -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

function get(fresh bool) returns Digits
	return Digits.create(0) if fresh else Primes
end 'get'

function main() returns ExitCode
	let d = get(false)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:13:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-match-arm-giving-a-module-let-array-beside-a-fresh-one-is-not-writable-for-its-caller -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

enum Which
	fixed
	live
end 'Which'

function get(which Which) returns Digits
	return match which 'which'
		fixed gives Primes
		live gives Digits.create(0)
	end 'which'
end 'get'

function main() returns ExitCode
	let d = get(Which.fixed)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:21:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-map-miss-falling-back-to-a-module-let-array-is-not-writable-for-its-caller -->
`pick` builds its own map, so every argument it takes is a value; the `otherwise` fallback is the module `let` array, and the result mixes it with the stored one.

```maxon
typealias Count = int(0 to 1000)
typealias Items = Array with Count
typealias ItemIndex = Map with (Count, Items)

let Empty = Items from [1, 2]

function pick(k Count) returns Items
	let index = ItemIndex.create()
	let stored = Items.create(0)
	stored.push(7)
	index.upsert(1, value: stored)
	return try index.get(k) otherwise Empty
end 'pick'

function main() returns ExitCode
	let mine = pick(9)
	mine.push(3)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:18:7: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-static-let-array-beside-a-fresh-one-is-not-writable-for-its-caller -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

type Table
	static let Primes = Digits from [2, 3, 5, 7]

	static function get(fresh bool) returns Digits
		if fresh 'fresh'
			return Digits.create(0)
		end 'fresh'

		return Table.Primes
	end 'get'
end 'Table'

function main() returns ExitCode
	let d = Table.get(false)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:19:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-generic-function-handing-back-a-module-let-array-beside-a-fresh-one-is-not-writable -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

function choose(_ T, fresh bool) uses T returns Digits
	if fresh 'fresh'
		return Digits.create(0)
	end 'fresh'

	return Primes
end 'choose'

function main() returns ExitCode
	let d = choose(1, fresh: false)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:17:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-function-forwarding-a-mixed-result-is-not-writable-for-its-caller -->
`outer` returns `get`'s result unchanged, so it is as read-only as `get`'s.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

function get(fresh bool) returns Digits
	if fresh 'fresh'
		return Digits.create(0)
	end 'fresh'

	return Primes
end 'get'

function outer(fresh bool) returns Digits
	return get(fresh)
end 'outer'

function main() returns ExitCode
	let d = outer(false)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:21:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-method-forwarding-a-mixed-getter-is-not-writable-for-its-caller -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

type Holder
	let shared as immutable Digits
	var own as Digits

	static function create() returns Self
		return Self{shared: freeze Digits from [2, 3], own: Digits.create(0)}
	end 'create'

	function pick(flag bool) returns Digits
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'

	function outer(flag bool) returns Digits
		return self.pick(flag)
	end 'outer'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	let d = holder.outer(false)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:29:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-mixed-result-passed-straight-to-a-writing-function-is-refused -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

function get(fresh bool) returns Digits
	return Digits.create(0) if fresh else Primes
end 'get'

function writeIt(d mutable Digits)
	d.push(1)
end 'writeIt'

function main() returns ExitCode
	writeIt(get(false))
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:16:10: 'writeIt' takes `mutable` 'd' (line 12: `d.push(1)`); 'get(false)' is read-only
```

<!-- test: a-mixed-result-bound-to-a-var-is-not-writable-for-its-caller -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

function get(fresh bool) returns Digits
	return Digits.create(0) if fresh else Primes
end 'get'

function main() returns ExitCode
	var d = get(false)
	d.push(1)
	d = get(true)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:13:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-mixed-result-is-not-isolated -->
A fresh clean call may become `isolated`; the result of `get` may be image data, so it may not.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

type Box
	export var part as isolated Digits

	static function holding(part isolated Digits) returns Self
		return Self{part: part}
	end 'holding'
end 'Box'

function get(fresh bool) returns Digits
	if fresh 'fresh'
		return Digits.create(0)
	end 'fresh'

	return Primes
end 'get'

function main() returns ExitCode
	let b = Box.holding(Digits.create(0))
	b.part = get(false)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:25:11: 'get(false)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; the call hands back read-only data, so pass a copy
```

<!-- test: a-mixed-result-is-read-and-a-copy-of-it-is-written -->
The declaration is accepted: a mixed result is read-only for its callers, not refused where it is declared. Reading it, and writing a copy of it, are accepted.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

function get(fresh bool) returns Digits
	if fresh 'fresh'
		return Digits.create(0)
	end 'fresh'

	return Primes
end 'get'

function main() returns ExitCode
	let d = get(false)
	let mine = get(false).clone()
	mine.push(1)
	print("{d.count()} {mine.count()} {Primes.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 5 4
```

<!-- test: a-clean-call-building-its-own-array-stays-writable-and-isolated -->
`build` takes only values and hands back an array it built, so the caller writes it and may move it into an `isolated` field.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

type Box
	export var part as isolated Digits

	static function holding(part isolated Digits) returns Self
		return Self{part: part}
	end 'holding'
end 'Box'

function build(extra Digit) returns Digits
	let made = Digits.create(0)
	made.push(extra)
	return made
end 'build'

function main() returns ExitCode
	let d = build(4)
	d.push(5)
	let b = Box.holding(Digits.create(0))
	b.part = build(6)
	modify b.part 'edit'
		b.part.push(7)
	end 'edit'
	print("{d.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-static-let-record-beside-a-fresh-one-is-not-writable-for-its-caller -->
```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static let Shared = Self{n: 7}

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	static function get(fresh bool) returns Self
		if fresh 'fresh'
			return Self{n: 1}
		end 'fresh'

		return Box.Shared
	end 'get'
end 'Box'

function main() returns ExitCode
	let b = Box.get(false)
	b.n = 9
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:24:2: cannot write 'n' through 'b', which is read-only
```

<!-- test: a-method-of-a-fresh-receiver-handing-back-a-module-let-array-is-not-writable-for-its-caller -->
The receiver is a fresh value, so nothing about the call makes its result read-only except what `get` hands back.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

let Primes = Digits from [2, 3, 5, 7]

type Source
	var tag as Digit

	static function create() returns Self
		return Self{tag: 1}
	end 'create'

	function get(fresh bool) returns Digits
		if fresh 'fresh'
			return Digits.create(0)
		end 'fresh'

		return Primes
	end 'get'
end 'Source'

function main() returns ExitCode
	let d = Source.create().get(false)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:25:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-closure-built-by-a-clean-call-stays-callable-through-a-var-rebound-to-a-copy -->
`relay` returns the closure `make` builds, so the closure is fresh on every path and a caller that calls it, rebinds a `var` to it or calls the `var` is calling writable data.

```maxon
typealias Tally = int(0 to 1000)
typealias Bump = function() returns Tally mutates captures

type Total
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump() returns Tally
		self.n = self.n + 1
		return self.n
	end 'bump'
end 'Total'

function make() returns Bump
	let total = Total.create()
	return function() gives total.bump()
end 'make'

function relay() returns Bump
	return make()
end 'relay'

function main() returns ExitCode
	let b = relay()
	var h = relay()
	h = b
	let first = b()
	let second = h()
	print("{first} {second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: a-result-mixing-a-module-let-closure-with-a-fresh-one-is-not-callable-for-its-caller -->
`pick` hands back a module `let` closure on one path and a closure `make` builds on the other, so its result is read-only for every caller and a `mutates captures` function cannot be called through it.

```maxon
typealias Tally = int(0 to 1000)
typealias Bump = function() returns Tally mutates captures

type Total
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump() returns Tally
		self.n = self.n + 1
		return self.n
	end 'bump'
end 'Total'

function make() returns Bump
	let total = Total.create()
	return function() gives total.bump()
end 'make'

let Shared = function() gives 7 as Tally

function pick(fresh bool) returns Bump
	if fresh 'fresh'
		return make()
	end 'fresh'

	return Shared
end 'pick'

function main() returns ExitCode
	let f = pick(false)
	let n = f()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3211: <fragment>:35:11: 'Bump' mutates captures and cannot be called through read-only data
```

<!-- test: a-conformer-returning-a-receiver-field-through-a-frozen-holder-is-not-writable -->
`Shelf.part` hands back a field of its receiver without building a record, and `through` passes that result along from a frozen holder, so the caller holds frozen data.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

interface Parted
	function part() returns Digits
end 'Parted'

type Shelf implements Parted
	var items as Digits

	static function create() returns Self
		return Self{items: Digits.create(0)}
	end 'create'

	function part() returns Digits
		return self.items
	end 'part'
end 'Shelf'

function through(h Parted) returns Digits
	return h.part()
end 'through'

function main() returns ExitCode
	let h = freeze Shelf.create()
	let d = through(h)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:28:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-conformer-reached-through-an-interface-field-of-a-frozen-holder-is-not-writable -->
The call dispatches through a witness table, so the caller cannot see which conformer answers, and the holder is frozen all the same.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

interface Parted
	function part() returns Digits
end 'Parted'

type Shelf implements Parted
	var items as Digits

	static function create() returns Self
		return Self{items: Digits.create(0)}
	end 'create'

	function part() returns Digits
		return self.items
	end 'part'
end 'Shelf'

type Cabinet
	var parted as Parted

	static function create() returns Self
		return Self{parted: Shelf.create()}
	end 'create'
end 'Cabinet'

function through(c Cabinet) returns Digits
	return c.parted.part()
end 'through'

function main() returns ExitCode
	let c = freeze Cabinet.create()
	let d = through(c)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:36:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-conformer-declared-mutable-self-reached-through-a-frozen-holder-is-not-writable -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

interface Parted
	function part(mutable self) returns Digits
end 'Parted'

type Shelf implements Parted
	var items as Digits

	static function create() returns Self
		return Self{items: Digits.create(0)}
	end 'create'

	function part() returns Digits
		return self.items
	end 'part'
end 'Shelf'

type Cabinet
	var parted as Parted

	static function create() returns Self
		return Self{parted: Shelf.create()}
	end 'create'
end 'Cabinet'

function main() returns ExitCode
	let c = freeze Cabinet.create()
	let d = c.parted.part()
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:32:4: cannot write through a read-only value, as it joins immutable and mutable data
```

<!-- test: a-method-relaying-a-mixed-getter-with-fixed-arguments-is-not-writable-for-its-caller -->
`relay` names no flag of its own, so nothing about its call tells the caller that `pick` may hand back the frozen `shared` array.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

type Holder
	let shared as immutable Digits
	var own as Digits

	static function create() returns Self
		return Self{shared: freeze Digits from [2, 3], own: Digits.create(0)}
	end 'create'

	function pick(flag bool) returns Digits
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'

	function relay() returns Digits
		return self.pick(true)
	end 'relay'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	let d = holder.relay()
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:29:4: cannot write 'a container' through 'a call result', which is read-only
```

<!-- test: a-mixed-getter-called-on-a-fresh-receiver-is-not-writable-for-its-caller -->
`Holder.create()` is a clean call, so nothing it was handed is read-only, and the result of `pick` is read-only because `pick` itself may return the frozen `shared` array.

```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

type Holder
	let shared as immutable Digits
	var own as Digits

	static function create() returns Self
		return Self{shared: freeze Digits from [2, 3], own: Digits.create(0)}
	end 'create'

	function pick(flag bool) returns Digits
		if flag 'sharedOne'
			return self.shared
		end 'sharedOne'

		return self.own
	end 'pick'
end 'Holder'

function main() returns ExitCode
	let d = Holder.create().pick(true)
	d.push(1)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:24:4: cannot write 'a container' through 'a call result', which is read-only
```
