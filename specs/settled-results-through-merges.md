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

A getter whose return paths mix frozen data (its own, such as an `immutable` field) with writable data is a
mixed getter, and its result is read-only for every caller, whatever the receiver is: writing through it is
**E3197**, and passing it to a `mutable` parameter is **E3207**. A
single `return` whose `if … else`, ternary or `match` joins frozen and writable data counts as mixed too.
Declare the getter `returns immutable` when every path is frozen, or return a `.clone()` of the frozen path
when the caller must write.

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
		var binding = self.scope.declare(name, kind: kind)
		self.candidates.push(Candidate.create(binding, line: line))
	end 'declareParam'
end 'Walker'

function main() returns ExitCode
	var walker = Walker.create()
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
	var walker = Walker.create()
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
		var made = Block.create(self.blocks.count() as Count, label: label)
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
				var next = self.module.add(b"next")
				current = next
			end 'advance'
		end 'eachRound'
	end 'cascade'
end 'Pass'

function main() returns ExitCode
	var pass = Pass.create()
	var head = pass.module.add(b"head")
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
		var made = Block.create(self.blocks.count() as Count, label: label)
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
				var next = self.module.add(b"next")
				current = next
			end 'advance'
		end 'eachRound'
	end 'mark'
end 'Pass'

function main() returns ExitCode
	var pass = Pass.create()
	var head = pass.module.add(b"head")
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
	var holder = Holder.create()
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
	var holder = Holder.create()
	var picked = holder.mine()
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
