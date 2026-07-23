# First-class state machines in Maxon — design plan

## Motivation

State machines are underused in practice, and when they *are* used they're
often hand-rolled badly. Two goals drive this proposal:

1. **Encourage use** — give the pattern a name so it's discoverable and is the
   obvious tool to reach for.
2. **Prevent bad hand-rolled implementations** — make the good structure the
   default path (a "pit of success").

## What Maxon already gives you

The correctness *core* is already in the language, provided people reach for it:

- `union` — a closed state type with per-variant payload, so illegal *states*
  are already unrepresentable.
- exhaustive `match` — a missing case is `E2026`; a bare `default` is banned
  (must be `default throws` / `default panic`), so silent fallthrough can't
  happen.

So a hand-written FSM in Maxon is just a `union` (tag = state) plus a `match`
(transition table). The gaps this proposal closes are the two things a plain
type *cannot* express:

- **The transition graph is emergent, not declared.** Whatever transitions the
  code happens to write are legal by default; nothing states the intended edge
  set, so nothing can check it.
- **The pattern isn't discoverable.** People who'd never go looking for
  `union` + `match` reach for loose `int`/`bool` flags instead.

## Non-goal: this is not the async lowering

In many languages `async` *is* a compiler-synthesized state machine, so "add an
FSM" feels like reusing that transform. **Maxon's async does not lower to a
state machine** — it's stackful green threads on a GMP scheduler
(`__gt_spawn` / `__gt_await`, growable 2KB stacks). There is no suspend/resume
state-machine pass to reuse. This construct is a *static-checking* feature over
`union` + `match`, not a new runtime.

## Surface syntax

```maxon
machine Door
	state closed
	state open
	state locked(key_id int)

	initial closed

	event open_it
	event close_it
	event lock(key_id int)
	event unlock(key_id int)

	on_unhandled reject          # required: reject | ignore

	from closed    on open_it              to open
	from open      on close_it             to closed
	from closed    on lock(k)              to locked(k)
	from locked(k) on unlock(k2)           to closed    when k == k2
	from open      on close_it             to closed    do
		log("door closing")
	end
end 'Door'
```

Four declaration kinds:

- `state <name>` / `state <name>(field Type, ...)` — a state, optionally with
  per-state payload. Rides on `union`.
- `event <name>` / `event <name>(field Type, ...)` — an input, optionally with
  payload.
- `initial <state>` — the starting state.
- `from <state-pattern> on <event-pattern> to <state>` — a transition rule,
  with optional `when <guard>` and optional `do … end` action block.

Bindings (`k`, `k2`) come from the source-state and event patterns, exactly
like `match` arm bindings.

## The key line: `on_unhandled`

The most common way a hand-rolled FSM goes bad is **silently swallowing an
event that arrives in a state with no rule for it**. Making the policy a
**required declaration** forces the author to answer the question they'd
otherwise skip:

- `reject` — an undeclared `(state, event)` pair makes `step` **fallible**; it
  throws. A dropped event becomes a diagnosable error, not a silent no-op.
- `ignore` — undeclared pairs leave the state unchanged, but only because the
  author said so on purpose.

There is no third, silent option.

## Desugaring

The whole construct lowers to existing machinery — two `union`s and one step
function. No new IR op, runtime, codegen, or backend work.

```maxon
union DoorState
	closed
	open
	locked(key_id int)
end 'DoorState'

union DoorEvent
	open_it
	close_it
	lock(key_id int)
	unlock(key_id int)
end 'DoorEvent'

# on_unhandled reject  =>  step throws on an undeclared pair
function door_step(s DoorState, e DoorEvent) returns DoorState throws
	return match s 'by_state'
		closed then match e 'ev'
			open_it     gives open
			lock(k)     gives locked(k)
			default     throws FsmError.no_transition("Door", "closed")
		end 'ev'

		open then match e 'ev'
			close_it    gives (block 'act'
				log("door closing")
				gives closed
			end 'act')
			default     throws FsmError.no_transition("Door", "open")
		end 'ev'

		locked(k) then match e 'ev'
			# guard `when k == k2` desugars to a plain `if` in the arm —
			# needs NO new match-guard feature in the language
			unlock(k2)  gives (if k == k2 then closed
			                   else throws FsmError.guard_failed("Door"))
			default     throws FsmError.no_transition("Door", "locked")
		end 'ev'
	end 'by_state'
end 'door_step'
```

Three consequences fall out for free:

- The generated `default throws` is exactly the project's existing match rule,
  so the construct emits idiomatic-by-the-rules code.
- A guard becomes an ordinary `if` — no need to add match guards to the
  language to ship this.
- `ignore` instead of `reject` swaps every `default throws …` for
  `default gives s` and drops `throws` from the signature. One knob, mechanical.

### Optional ergonomic wrapper

A second, optional desugar can generate a `.send` method over a struct holding
the current state, for a more object-like surface:

```maxon
var door = Door.new()         # starts in `initial`
try door.send(lock(42))
try door.send(unlock(42))
match door.state 'now' … end 'now'
```

## Static checks the declared graph unlocks

This is the "more than a type" payoff. Because the edge set is *declared* rather
than emergent, the check pass can reject things a `union` + hand-written `match`
never could:

1. **Typo'd target** — `to opne` isn't a declared state → compile error.
2. **Illegal transition is structural** — the machine can only produce edges
   that were declared; you can't accidentally introduce `closed → open` logic
   somewhere with no rule for it.
3. **Dead states** — a state with no outgoing transition, or one unreachable
   from `initial`, is a warning. A plain enum can't tell you a variant is a dead
   end.
4. **The whole graph is one greppable block** — so it can be rendered to a
   diagram / DOT, because the transitions are data, not scattered control flow.

## Implementation scope

Everything above is **lexer keyword + pre-scan/registry entry + parser rule +
one semantic-check pass + a desugar to `union`/`match`**. No new IR op, no
runtime, no codegen, no x64/arm64 parity work. A tight, low-risk rung.

Compiler stages touched (mirrors how a new declaration threads the tree):

1. **Lexer** — `machine` keyword (and the sub-keywords `state`, `event`,
   `initial`, `on_unhandled`, `from`, `on`, `to`, `when` in machine context).
2. **Pre-scan / registry** — register `DoorState` / `DoorEvent` types up front,
   like any other type.
3. **Parser** — `ParseMachineDecl`.
4. **Semantic check** — the four static checks above; verify every `to`/`from`/
   `on` names a declared state/event; verify `on_unhandled` is present.
5. **Desugar** — emit the two `union`s and the `step` function (and the optional
   `.send` wrapper) as ordinary AST, then let the existing pipeline take over.

## Out of scope for v1 (the long tail)

Left in library/pattern space until real usage pulls it in. This is deliberate:
FSMs have a long feature tail, and blessing each addition as a *keyword* makes
every one of them a language change.

- **entry/exit actions** (`on_enter` / `on_exit` per state) — pure sugar, but
  adds surface; wait for demand.
- **hierarchical / nested states** (statecharts) — where it stops being sugar
  and becomes a real feature.
- **timers, async transitions, event queues** — these drag in the async /
  green-thread runtime; a different and much larger project. Keeping v1
  synchronous (`step` is a pure `State × Event → State`) is what keeps it small.

## Open question: keyword vs. stdlib

Worth deciding before writing parser code. The correctness core already lives in
the language (`union` + enforced `match`); what's missing is a name and the
declared/checked edge set. That can be delivered as a `machine` keyword, or as a
blessed stdlib type plus a `derive`-style helper and a lint that nudges
flag-based code toward it. The stdlib route keeps the long tail in library code
instead of the grammar, at some cost to discoverability. This plan assumes the
keyword route; revisit if the tail looks likely to grow.

## Next step

Write this up as a `specs/machine-*.md` spec — states, events, the
`on_unhandled` policy, the four static checks, and the desugar as the
RequiredIR target — so the design is reviewable before any parser code lands.
That is how features enter this compiler.
