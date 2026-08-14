---
feature: async-linearity
status: stable
keywords: [async, await, green-threads, promise, linearity, E3100, ownership]
category: concurrency
---

# Async / Await — linear await (E3100, P1.5-B2a)

## Documentation

`await` is **linear**: the green thread a promise names is awaited **at most once** on any single flow
path. The awaited thunk owns its result and hands it over at the await, so a second await of the same
thread would take a second reference to a payload the thunk only ever owned once — the two releases
underflow the refcount and free it twice. A second await that is **reachable** from a first is therefore
a compile error, **E3100** — the double free is made *unrepresentable* rather than survived at runtime.

Linearity is a property of the **green thread**, not of the identifier text or the promise's SSA value.
In shv2 the promise's SSA `ValueId` already *is* the stable thread identity: an alias (`let q = p`) binds
`q` to `p`'s value verbatim, and a cross-block read resolves to that same value — so awaiting through
either name awaits the one thread. Re-arming a binding (`p = async g()`) mints a **new** thread, so
awaiting it again is the first await of *that* thread, not a second await of the old one.

The check is **flow-sensitive reachability**, not lexical position, and it has to be in both directions:

- two awaits of one promise in **mutually exclusive** branches are each the only await on their own
  path, and are allowed — neither can reach the other;
- a **single** `await` sitting in a loop, over a promise spawned **outside** the loop, awaits the same
  thread on every iteration — the await is reachable from itself across the back-edge, and is refused.

These cases are **scalar twins** of `specs/async-await.md`'s linearity tests, which gate on `File.exists`
(no shv2 I/O yet); the linearity rule is structural and fires identically for a scalar promise.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** These cases spawn and await, so they reach the driver's `QueryPerformanceCounter`
and `VirtualFree` entries, which exist only on x64-windows at this rung. The marker is not an opt-in.

⚠⚠ **THE `error.*` CASES CARRY THE MARKER TOO, AND THIS PARAGRAPH USED TO SAY THEY DID NOT.** It read
*"the linearity RULE itself is target-neutral, and its compile-time refusals carry no marker"* — the RULE
is target-neutral, but **a case that exercises it cannot be**, and the difference cost two red lanes.
MEASURED 2026-08-14: a legal `async` spawn needs a callee that YIELDS (`E3073` otherwise), the only yield
primitive is `Runtime.yield()`, and that lowers to `__gt_resched`, which is x64-windows-only. So the
thunk reaches a gated construct no matter how it is written, `E3104` is raised at the thunk and — the
compiler reporting the FIRST error — MASKS the `E3100` the case exists to pin. Six cases here, six in
`async-await.md` (whose thunks gate on `File.exists`/`__mf_exists` instead) and one in
`async-try-await.md` were green on the host and red on x64-linux and wasm32-wasi.
⇒ **There is no way to write a target-neutral async case until a second substrate lands** — which is
exactly what `async-scheduler.md`'s *Targets* section already says to watch for: **un-gate these the
moment one does.** Removing the marker before then re-creates the masking, silently on the host.

## Tests

<!-- test: async-linearity.error.double-await -->
<!-- targets: x64-windows -->
`await` is linear: awaiting one promise twice in straight-line code is refused at the second await.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let p = async makeValue()
	let a = await p
	let b = await p
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:10:10: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-linearity.error.double-await-in-loop -->
<!-- targets: x64-windows -->
The check is flow-sensitive, and this is why it must be. There is exactly ONE `await` here lexically, so
a "have I seen this promise awaited before?" check finds nothing — but it sits in a loop over a promise
spawned OUTSIDE the loop, so it awaits the same green thread every iteration. Reachability catches it:
the await is reachable from itself across the back-edge, without re-passing the `async` that would re-arm it.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let p = async makeValue()
	var i = 0
	var acc = 0
	while i < 3 'loop'
		let s = await p
		acc = acc + s
		i = i + 1
	end 'loop'
	return acc as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:12:11: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-linearity.error.double-await-through-alias -->
<!-- targets: x64-windows -->
Linearity is a property of the GREEN THREAD, not of the identifier text. `let q = p` gives one green
thread a second name; awaiting through both names awaits it twice. In shv2 `q` and `p` are the SAME SSA
value, so the second await is refused with no thread-id sidetable — the value IS the thread's identity.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let p = async makeValue()
	let q = p
	let a = await p
	let b = await q
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:11:10: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-linearity.error.double-await-through-alias-in-branch -->
<!-- targets: x64-windows -->
The same alias, made in a DIFFERENT block from the `async` that spawned the thread. A cross-block read
of the promise resolves to the same SSA value it was spawned as (there is no re-tag), so `p` and `q`
inside the branch name one thread — and awaiting both is the second await it is.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 42
end 'makeValue'

function cond() returns bool
	return true
end 'cond'

function main() returns ExitCode
	let p = async makeValue()
	if cond() 'branch'
		let q = p
		let a = await p
		let b = await q
		return (a + b) as ExitCode
	end 'branch'
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:16:11: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-linearity.error.double-await-alias-outlives-rebind -->
<!-- targets: x64-windows -->
Re-arming `p` does NOT end the first thread's life while `q` still names it. The reachability walk stops
a path only when the promise's DEFINITION is re-passed (a re-arm); reassigning `p` mints a NEW value, so
`q` still names the first thread when it is awaited — and that await is the second one, refused.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 21
end 'makeValue'

function main() returns ExitCode
	var p = async makeValue()
	let q = p
	let a = await p
	p = async makeValue()
	let b = await q
	let c = await p
	return (a + b + c) as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:12:10: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-linearity.error.double-await-after-ternary-arm -->
<!-- targets: x64-windows -->
An `await` in one ternary arm does NOT make the promise spent on the path where that arm was not taken —
but the await AFTER the ternary is reachable from the arm that WAS taken, so on that path the thread is
awaited twice. Exclusivity buys the arms nothing here: reachability decides, and the arm reaches the tail.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 21
end 'makeValue'

function no() returns bool
	return false
end 'no'

function main() returns ExitCode
	let p = async makeValue()
	let v = (await p) if no() else 0
	let w = await p
	return (v + w) as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:14:10: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-linearity.await-in-exclusive-branches -->
<!-- targets: x64-windows -->
Two awaits of one promise in MUTUALLY EXCLUSIVE branches are each the only await on their own path, and
are allowed. A lexical "already awaited" check would reject this valid program; reachability does not,
because neither await can reach the other.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 21
end 'makeValue'

function no() returns bool
	return false
end 'no'

function main() returns ExitCode
	let p = async makeValue()
	if no() 'branch'
		let a = await p
		return a as ExitCode
	end 'branch'
	let b = await p
	return (b + 1) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: async-linearity.rearm-after-await -->
<!-- targets: x64-windows -->
Reassigning a promise binding RE-ARMS it: `p` now names a new green thread, so awaiting it again is the
first await of that thread, not a second await of the old one. The linear check refuses a second await
of one thread, not a second `await p` in the text.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 21
end 'makeValue'

function main() returns ExitCode
	var p = async makeValue()
	let a = await p
	p = async makeValue()
	let b = await p
	return (a + b) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-linearity.await-aliased-loop-element -->
<!-- targets: x64-windows -->
A loop that re-arms the promise each iteration, WITH an alias inside the loop. Every iteration spawns a
fresh thread and awaits it exactly once through `q`, so the single `await q` is one await per promise,
not N awaits of one. This is the case an over-eager check breaks: the alias unifies `p` and `q`, and the
re-arm — re-passing the promise's definition on the back-edge — is what keeps the single await legal.
```maxon
function makeValue(i int) returns int
	Runtime.yield()
	return i
end 'makeValue'

function main() returns ExitCode
	var i = 0
	var acc = 0
	while i < 3 'await'
		let p = async makeValue(i)
		let q = p
		let s = await q
		acc = acc + s
		i = i + 1
	end 'await'
	return acc as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: async-linearity.await-in-ternary-arms -->
<!-- targets: x64-windows -->
The two arms of a ternary are MUTUALLY EXCLUSIVE — only the selected arm is evaluated — so an `await` in
each is the only await on its own path, exactly as in an `if`/`else`. The reachability check reads the
arms as the separate branches they lower to, not as one straight-line block.
```maxon
function makeValue() returns int
	Runtime.yield()
	return 21
end 'makeValue'

function no() returns bool
	return false
end 'no'

function main() returns ExitCode
	let p = async makeValue()
	let v = (await p) if no() else (await p)
	return (v + 1) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: async-linearity.await-aliased-in-ternary-arms -->
<!-- targets: x64-windows -->
The same, through an ALIAS: `p` and `q` are one green thread under two names, and the two arms await it
through different names. Linearity keys on the THREAD, so it sees one thread awaited in each of two
exclusive arms — one await per path, and legal. This is the intersection of the two facts that must both
hold: the alias must UNIFY (or `await p; await q` in sequence would double-free), and the arms must stay
EXCLUSIVE (or unifying them would reject this).
```maxon
function makeValue() returns int
	Runtime.yield()
	return 21
end 'makeValue'

function no() returns bool
	return false
end 'no'

function main() returns ExitCode
	let p = async makeValue()
	let q = p
	let v = (await p) if no() else (await q)
	return (v + 1) as ExitCode
end 'main'
```
```exitcode
22
```
