---
feature: stdlib-whitelist
status: stable
keywords: [stdlib, whitelist, Clock, WallClock, prepend, dead-function-elimination, runtime-floor]
category: system
---

# The stdlib whitelist

## Documentation

shv2's stdlib loader enumerates every `.maxon` file under the checkout's `stdlib/`, top level and
subdirectories alike, exactly as v1 and the C# bootstrap do — and then a TEMPORARY WHITELIST filters
which of them are actually loaded, so stdlib support can grow one module at a time, each module gated
on the language features it needs. The filter is stated in exactly one place,
`Compiler/StdlibLoader.maxon`, and this spec deliberately does not restate WHICH modules it names:
nothing would keep a prose copy of that list agreeing with the list, and the same argument is made
below about the bare-builtin roster, where both prose copies had already drifted.

The whitelist is scaffolding, not a feature: it is a filter INSIDE the real loader rather than a list
the loader walks, so removing it is a deletion rather than a rewrite — of ONE file, which owns every
mention of it including the types and the error cases it needs. Everything downstream of the loader
deals in two durable facts instead — "is this function stdlib source or user source?" and "is it
reachable from `main`?" — so no pass has to be rewritten on the day the filter goes.

A whitelisted module is registered into the query database exactly like a user source, so it flows
through the same tokenize → signature-index → parse → merge spine. Its declarations therefore
populate the signature registry, and a user program can call `Clock.nowMs()` /
`WallClock.nowUnixSeconds()` though the program itself contains no `Clock`.

That sameness runs one level deeper: which files under a directory are Maxon sources is decided by
ONE enumerator (`Compiler.collectMaxonSources`), which walks the user's own root and `stdlib/` alike,
so the extension, the excluded build manifest and every ignore rule either walk grows are stated once.
The loader also skips a file the project has ALREADY registered — the one case being a user root that
lies under `stdlib/` — because a source registered under two spellings of its path is parsed twice and
every function in it then collides with itself. (Not pinned by a test below: the harness stages every
fragment in a temp directory and cannot place one inside the checkout's `stdlib/`.)

`stdlib/` is located by walking UP from the COMPILER's own executable directory, not from the
current working directory: the spec runner and `run_program` compile in a throwaway temp dir, so the
working directory has no `stdlib/` above it, while the compiler binary always lives inside the
checkout. A missing `stdlib/`, or a whitelisted path that does not exist under it, is a loud, hard
error — never a silent skip.

### An unused whitelisted module changes NOTHING

The whitelist prepends Clock to EVERY compile, so a program that never reads a clock must still
compile to the exact bytes it did before Clock was whitelisted — same x64 goldens, same wasm/arm64
output. Two mechanisms compose to guarantee that:

1. Dead-function elimination drops every whitelisted function no reachable code calls, before the
   back end — so an unused Clock never reaches instruction selection, register allocation or
   encoding, and never lands in a golden, on any target.

2. The runtime-floor decision (`scanRuntimeUsage`: does this program carry the heap / GT scheduler /
   wall clock?) runs at the Maxon tier, BEFORE that elimination. Clock.nowMs's body calls
   `__gt_now_ns` and WallClock.nowUnixSeconds's calls `__clock_now_unix_s`, so a naive load would
   install the scheduler and a `.data` slot for a program that reads no clock — slots the later
   elimination cannot prune. That scan therefore SKIPS the stdlib functions no path from `main`
   reaches (`StdlibFacts.unreachable`): code the program does not contain feeds the floor decision
   nothing. User functions are never skipped — an unreached user body still speaks for the program —
   so nothing about a program that touches no stdlib changes.

The whole existing `specs-shv2` corpus — every program that never touches Clock — is the standing
proof of this: not one committed fragment moves when Clock is added to the whitelist.
`no-clock-is-byte-neutral` below is the same guard stated directly.

#### A listed module's own LITERALS must not renumber the program's `.rdata`

Elimination is not the only pass that runs over a listed module's bodies, and it is not the earliest.
Everything a body registers in the read-only data section is registered BEFORE elimination, by
lowering: a string literal mints a byte blob and a 48-byte record, a byte-string literal mints a blob,
and a generic receiver mints a layout descriptor. Elimination prunes FUNCTIONS, never `.rdata`, so
every one of those payloads outlives the function that asked for it.

That matters because the synthetic `.rdata` labels are minted from ONE counter shared by every prefix.
A single surviving blob therefore renumbers `__str_blob_` AND `__jumptable_` labels program-wide — in a
program that mentions no string at all. Measured when the first listed module containing literals was
added: **317 committed fragments moved**, for declarations no user program reaches.

⚠ **`__fconst_` USED TO BE ON THAT LIST AND NO LONGER IS.** A float island is named by its VALUE
(`__fconst_-5.5`, `GlobalDataTable.registerFloatConstant`) and registered through the LABELLED door, which does not touch the
shared counter — so a float label cannot move for a reason outside its own value. That removes one prefix
from the blast radius; it does not shrink the detection, because the two remaining prefixes still renumber
together and the case below names both.

So a pre-elimination pass may not let an unreachable stdlib body register anything either, and
`lowerMaxonToStd` skips such a body on exactly the reachability fact the runtime-floor scan skips on.
The two derivations are independent — one walks the Maxon module from `main`, the other walks the Std
module from a larger root set that includes every function an `.rdata` slot names — so the elimination
pass CHECKS that it drops every function whose body lowering skipped, rather than assuming it. A
disagreement would otherwise link cleanly and call an empty function.

`a-listed-module's-literals-are-byte-neutral` below is that guard stated as a golden: it compiles a
program holding a float constant and a dense-`match` jump table, so its fragment names labels from two
of the three prefixes the shared counter mints, and any listed module that registers `.rdata` for code
the program cannot reach moves them. (A payload registered under a STRUCTURAL label — a witness table,
a layout descriptor — does not advance that counter, so it shifts `.rdata` OFFSETS without moving any
label a fragment prints. The lowering skip covers it; no fragment golden can see it.)

### The collision rule

A whitelisted module must declare no name a user program declares and no builtin type/name.

- FUNCTION-name collisions — a user `Clock.nowMs` against the whitelisted one, or two whitelisted
  modules — are caught loudly by the whole-program duplicate-function check, `E3006`, because a
  whitelisted file merges through the identical path a user file does. A user program that declares
  its own `type Clock` with a `nowMs()` is rejected with

  ```text
  error E3006: <path>/stdlib/Clock.maxon:18:25: Duplicate function 'Clock.nowMs'
  ```

  naming the whitelisted definition it collided with. (That path is the real `stdlib/Clock.maxon`,
  not the test fragment, so this diagnostic is documented here rather than pinned as a golden — the
  runner only rewrites the fragment's own path to `<fragment>`.)

- TYPE-name-vs-builtin collisions are the maintainer's responsibility, with **exactly two
  exceptions**. `String` and `Character` are now REFUSED as user type names (`E2015`, via
  `isCompilerOwnedTypeName`) — BATCH2 slice 2, because those two are the only builtins that mint
  CONFORMANCE IMPL SYMBOLS (`String.hash`, `Character.hash`, …) which a user declaration of the same
  name does not collide with but silently **REPLACES**: `undefinedImplNames` then declines to install
  the builtin. Measured before the fix, both silent and undiagnosed: a `Box with String` whose
  `.itemHash()` of `""` answered the user's body instead of djb2's `5381`, and a `Set with String`
  that stored `"alice"` **twice** because the user's `equals` answered `false`.
  ⚠ **For every OTHER builtin the original statement still holds** — shv2 has no general
  builtin-type-redeclaration diagnostic, and enforcing one is a language matter rather than a
  whitelist one. Clock declares `Clock`/`WallClock` and time typealiases, none of them builtin, so it
  cannot hit this. Do not whitelist a module that redeclares a builtin.

- FUNCTION-name-vs-BARE-BUILTIN collisions are SILENT, so the builtin is RETIRED FIRST — which is
  what made `stdlib/Sleep.maxon` the second entry. The parser recognizes a set of BARE names —
  `print` and `trunc` among them — before any registry is consulted, so while
  one of them claims a name, a CALL to that name never reaches a declaration of it: whitelisting
  such a module would load code no ordinary call site can reach while charging the per-compile load
  cost for zero delivered capability, and the name would have two routes — `let f = sleep` is not a
  call site, so it took the address of the declaration the call sites could not see.

  That shadowing was not the whitelist's doing and predated it: a user file declaring its own
  `function sleep` compiled with the declaration silently unlinked, no diagnostic, and `sleep(1)`
  still reached the builtin — a wrong answer. Deleting the bare-name `sleep` builtin and listing
  the module repaired both, and it is the pattern for every builtin still standing in for a stdlib
  module. **The rule: do not whitelist a module whose function name a bare builtin claims. Retire
  the builtin first — and check the roster at its source, `parseCallNamed`'s bare-name `if` chain,
  never against a list written in prose.** Neither this spec nor `StdlibLoader.maxon` restates the
  roster, deliberately: nothing makes a prose copy agree with the chain, and both copies had already
  drifted, naming four of the fifteen names standing after `sleep` left and omitting `spawnReadLine`
  and all seven `subp*`. A maintainer who checked a module against the list rather than the chain
  would have been told a claimed name was free.

  What a listed module then owns, it owns whole: a user program that declares its own `function
  sleep` is now the ordinary duplicate, `E3006`, naming `stdlib/Sleep.maxon` — loud where it was
  silent. Shadowing a stdlib declaration with a user one needs namespaces, which shv2 does not have
  (the reference compiler resolves the user's `sleep` and calls it), and that is the same general
  gap the `Clock` entry above already has.

### A diagnostic raised inside stdlib source is attributed to the crossing call

Stdlib source is compiled as part of the program, so a rejection raised in one of its bodies would be
positioned at ITS path — an absolute `…/stdlib/Foo.maxon:L:C` naming a file the user never opened, at
a line they cannot change, for a choice they made at a call site somewhere else.

That is not a quirk of one module: it fires wherever a stdlib body bottoms out in something
target-gated, which is exactly what a stdlib leaf is FOR — every module in the queue behind Clock ends
in a `__Builtins.*` intrinsic — and it gets more common, not less, as stdlib grows.

The refusal that reaches this today is the TARGET gate, `E3104`. It is reported at the FIRST call
crossing from user code INTO stdlib, and it names the stdlib function the user actually wrote:

```text
error E3104: <fragment>:3:16: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

The requirement is TRANSITIVE through the stdlib call graph: `Clock.elapsedMs` names no runtime entry
itself — it calls `Clock.nowMs`, which does — so a program calling `elapsedMs` is refused at ITS call,
naming `Clock.elapsedMs` and still naming the entry that has no lowering. A user's own helper is user
code, so `main → myHelper → Clock.nowMs` is blamed inside `myHelper`. And a stdlib function no path
from `main` reaches is refused nowhere at all, which is what keeps an unused module byte-neutral.

The gate is therefore reachability-BLIND for user code and reachability-AWARE for stdlib source: an
`__Builtins.sleep(1)` in a function `main` never calls is still refused for wasm
(`builtins-sleep.rejected-on-wasm-when-unreached`), while a `Clock.nowMs()` in one is not.

Retiring the bare-name `sleep` builtin moved a program from the first case to the second, and that was
decided rather than discovered: `sleep(1)` used to BE a `__gt_sleep` in user code, and is now a call into
stdlib, so an unreached one compiles for wasm where it was refused
(`async-sleep.unreached-compiles-on-wasm`). Every builtin retired the same way moves the same way, and it
is the correct direction — the entry genuinely is stdlib's now, and a program that cannot reach it does
not contain it.

## Tests

<!-- test: stdlib-whitelist.clock-from-whitelist -->
<!-- targets: x64-windows -->
A program that calls `Clock.nowMs()` and `WallClock.nowUnixSeconds()` but declares NEITHER type
compiles and runs — the declarations came from the whitelist, not the program. A monotonic reading
and a calendar reading are both positive on any real host.
```maxon
function main() returns ExitCode
	let ms = Clock.nowMs()
	let secs = WallClock.nowUnixSeconds()
	var score = 0
	if ms > 0 'monotonicPositive'
		score = score + 1
	end 'monotonicPositive'
	if secs > 0 'calendarPositive'
		score = score + 1
	end 'calendarPositive'
	return score as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: stdlib-whitelist.elapsed-through-a-sibling -->
<!-- targets: x64-windows -->
`Clock.elapsedMs(since:)` is a whitelisted function that itself calls another whitelisted function
(`Clock.nowMs`), so reaching it must keep BOTH alive through the call graph — the reachability the
runtime-floor skip is computed against. Elapsed time since a reading taken moments earlier is
non-negative.
```maxon
function main() returns ExitCode
	let start = Clock.nowMs()
	var spins = 0
	while spins < 50000 'burn'
		spins = spins + 1
	end 'burn'
	let elapsed = Clock.elapsedMs(start)
	if elapsed >= 0 'nonNegative'
		return 7 as ExitCode
	end 'nonNegative'
	return 1 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-whitelist.nanos-from-whitelist -->
<!-- targets: x64-windows -->
`Clock.nowNanos()` reaches the same monotonic counter as `nowMs` but through the nanosecond entry —
another whitelisted function pulled in only because the program names it.
```maxon
function main() returns ExitCode
	let a = Clock.nowNanos()
	let b = Clock.nowNanos()
	if b >= a 'nonDecreasing'
		return 4 as ExitCode
	end 'nonDecreasing'
	return 1 as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: stdlib-whitelist.no-clock-is-byte-neutral -->
A program that mentions no listed module must compile to exactly what it did before any of them was
whitelisted: the whitelist adds Clock AND Sleep to this compile too, and every one of their functions is
pruned back out with no runtime floor installed. This case carries NO target restriction, so its
byte-neutrality is checked on wasm as well — the whitelist must not drag the x64-only clock or timer
substrate into a non-x64 target for a program that reads no clock and sleeps nowhere.
```maxon
function main() returns ExitCode
	let answer = 42
	return answer as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: stdlib-whitelist.a-listed-modules-literals-are-byte-neutral -->
<!-- targets: x64-windows -->
The sibling of `no-clock-is-byte-neutral`, for the half that case cannot see. That program's fragment
names no `.rdata` label at all, so it stays green while every synthetic label in the corpus renumbers.
This one holds a dense-`match` jump table and a String blob, so its fragment NAMES labels off the one
shared counter — `__jumptable_1` and `__str_blob_0`, the last being the range-check panic message that took
id 0. A listed module that registers ANY `.rdata` for code no path from `main` reaches moves both.

⚠ The float constant is still in the program and is still worth having — it is what makes the jump table
share a compile with an island of another kind — but its `__fconst_12.5` label is NO LONGER a counter
reading: float islands are named by their value and take the labelled door. This paragraph said
`__fconst_1` for as long as they did.

⛔⛔ **BUT IT DETECTS THAT THROUGH ITS GOLDEN, SO IT CANNOT FAIL — IT IS A READING, NOT A GATE (W69
review).** A fragment mismatch prints a `note:`, counts as no failure and leaves the exit code at 0 (user
ruling 2026-08-02). MEASURED: with `registerProgramLiteralBlobs`' unreachable-stdlib gate neutralised and
the compiler rebuilt — an orphan blob at `.rdata` byte 0 of every program in the suite — this case
reported **PASS**. Keep it: the drift it prints names the moved labels, which no other case does. But the
enforcement lives in `a-listed-modules-literals-cannot-reach-the-rdata-image` below, which pins the linked
image and goes red.
```maxon
typealias Weight = float(0.0 to 100.0)

enum Marker
	alpha
	beta
	gamma
	delta
	epsilon
	zeta
	eta
	theta
end 'Marker'

function main() returns ExitCode
	let scale = 12.5 as Weight
	let pick = Marker.gamma
	let slot = match pick 'which'
		alpha gives 0
		beta gives 1
		gamma gives 2
		delta gives 3
		epsilon gives 4
		zeta gives 5
		eta gives 6
		theta gives 7
	end 'which'
	return trunc(scale) + slot
end 'main'
```
```exitcode
14
```

<!-- test: stdlib-whitelist.a-listed-modules-literals-cannot-reach-the-rdata-image -->
⛔⛔ **THE BYTE-NEUTRALITY CLAIM'S ONLY GATE. ITS TWO SIBLINGS ABOVE CANNOT FAIL, AND ONE OF THEM WAS
CREDITED WITH CATCHING THIS RUNG'S DEFECT (W69 review).** `a-listed-modules-literals-are-byte-neutral`
detects a displaced `.rdata` payload through its golden FRAGMENT — and a fragment mismatch is REFERENCE, NOT
A GATE (user ruling 2026-08-02): it prints a `note:`, counts as no failure and leaves the exit code at 0.
MEASURED by neutralising `LowerMaxonToStd.registerProgramLiteralBlobs`' unreachable-stdlib gate and
rebuilding, which puts an orphan blob from `stdlib/Json.maxon` at `.rdata` byte 0 of every program in the
suite: that case reported **PASS**. It is a real reading and a useful one, but nothing in the battery turns
it red, so the invariant this whole file rests on had no enforcement at all.

⭐ **A ```RequiredRdata BLOCK IS THAT ENFORCEMENT, AND THE FIT IS EXACT.** The block is compared as a run
FROM BYTE 0, read back out of the LINKED IMAGE rather than out of the compiler's opinion of it — so it
answers precisely "did anything get in front of this program's read-only data?". This program's whole
`.rdata` is its two float constants, 16 bytes, every one of them pinned; a listed module that registers ANY
`.rdata` for code no path from `main` reaches lands ahead of them, because `registerProgramLiteralBlobs`
runs before the target tier mints a float. Re-measured with the gate neutralised:
`.rdata mismatch at byte 6: expected 0x29, got 0x00` — eight zero bytes of orphan where `12.5` belongs.

⚠ **IT MUST HOLD NO STRING LITERAL OF ITS OWN, and that is not a stylistic choice.** The user's `main` is
walked before stdlib's functions, so a program literal in `main` keeps byte 0 whatever an orphan does and
the pin goes green on the broken compiler — MEASURED, on a first draft of this case that pinned its own
`"MAXONPIN"` blob and passed with the gate neutralised. The payload a displacement is visible against has to
be one the COMPILER composes.

⛔⛔ **AND THE LOOP IS LOAD-BEARING: WITHOUT IT THIS PROGRAM HAS NO `.rdata` AT ALL.** The case used to read
`let scale = 12.5` / `let floor = 1.5` / `if scale > floor`, and on 2026-08-31 `foldConstants` learned to
fold FLOATS — so the comparison folded to a constant, `foldConstantBranches` took the arm, and both float
`const`s were retired unread. The program still returned 8; it simply stopped materialising either float,
the linked image lost its `.rdata` section outright, and this gate could no longer read the thing it
gates. **MEASURED, and it is the failure that found this**: `could not read the .rdata section … has no
.rdata section`.

⇒ The loop makes `scale` a HEADER PHI, which this pass reads as unknown by construction — *"it is not a
constant propagator; a value that is constant on every path into a phi is not constant to this pass"*. So
`scale + floor` and `scale > floor` both keep their instructions, `12.5` is materialised as the phi's
entering value and `1.5` as an operand (floats have no immediate form on any target, so
`foldConstOperands` cannot absorb it either), and the two islands are registered in source order. **Do not
simplify it back.** A folded version of this program passes its exit code and gates nothing — which is
precisely the failure mode the paragraph above this one is about, arriving by a second route.
```maxon
function main() returns ExitCode
	var scale = 12.5
	let floor = 1.5
	var spins = 0
	while spins < 1 'spin'
		scale = scale + floor
		spins = spins + 1
	end 'spin'
	if scale > floor 'gt'
		return 8
	end 'gt'
	return 1
end 'main'
```
```exitcode
8
```
```RequiredRdata
f64 12.5
f64 1.5
```

<!-- test: stdlib-whitelist.target-refusal-blames-the-crossing-call -->
<!-- targets: wasm32-wasi -->
A whitelisted body that bottoms out in an x64-only runtime entry is refused at the USER's call, and
names the whitelisted function they wrote — never at `stdlib/Clock.maxon`, a file they never opened
and cannot change.
```maxon
function main() returns ExitCode
	let t = Clock.nowMs()
	if t > 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:16: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-whitelist.target-refusal-blames-the-crossing-call-arm64 -->
<!-- targets: arm64-macos -->
The attribution is a property of the whitelist mechanism, not of one backend: the same program
compiled for arm64 is refused at the same user span, naming the same whitelisted function and the
same missing runtime entry.
```maxon
function main() returns ExitCode
	let t = Clock.nowMs()
	if t > 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:16: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no arm64-macos implementation
```

<!-- test: stdlib-whitelist.target-refusal-is-transitive -->
<!-- targets: wasm32-wasi -->
`Clock.elapsedMs` reaches no runtime entry itself — it calls `Clock.nowMs`, which does. The
requirement propagates through the whitelisted call graph, so the caller is blamed at the function
THEY named, while the entry named is still the one that has no lowering.
```maxon
function main() returns ExitCode
	let e = Clock.elapsedMs(0)
	if e >= 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:16: this construct is x64-windows only at this rung: 'Clock.elapsedMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-whitelist.target-refusal-blames-the-users-own-helper -->
<!-- targets: wasm32-wasi -->
A user's own function is not whitelisted, so the crossing is the call INSIDE it — code the user can
actually change — rather than `main`'s call to the helper or anything in `stdlib/`.
```maxon
function reader() returns Integer
	return Clock.nowMs()
end 'reader'

function main() returns ExitCode
	let t = reader()
	if t > 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3104: <fragment>:3:15: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-whitelist.unreached-clock-still-compiles-on-wasm -->
<!-- targets: wasm32-wasi -->
The crossing gate is reachability-AWARE, exactly as the runtime-floor skip is: `reader` is never
called, so no path from `main` crosses into the whitelist and the program compiles for wasm
unchanged. This is the byte-neutrality guarantee stated as a run — attributing the refusal to the
caller must not turn an untaken crossing into a refusal.
```maxon
function reader() returns Integer
	return Clock.nowMs()
end 'reader'

function main() returns ExitCode
	return 4
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
4
```

### A user's own declaration outranks a listed module's free function

A listed module's free functions are stdlib's; a user program's are the user's. Where the two spell
the same name the USER's declaration is what its own call sites reach, which is what the reference
compiler does (measured: a user `function sleep` compiles there and runs) and what N1's namespaces
made true here.

⚠ **THE TWO CASES BELOW ARE THE FIRST TO RUN THAT CLAIM**, and running it is what showed the prose
this section replaced was FALSE. It read *"a user program that declares its own `function sleep` is
now the ordinary duplicate, `E3006`, naming `stdlib/Sleep.maxon` — loud where it was silent"*, and
nothing ever compiled such a program: `sleep` is a listed module's free function, a user `function
sleep` compiles clean, and the user's body is what runs. A refusal nothing runs is a claim.

<!-- test: stdlib-whitelist.a-user-free-function-outranks-the-listed-modules -->
```maxon
typealias Ms = int(0 to 1000000)

function sleep(milliseconds Ms)
	print("mine {milliseconds}\n")
end 'sleep'

function main() returns ExitCode
	sleep(41)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
mine 41
```

⚠⚠ **THE CASE ABOVE IS A POSITIVE CONTROL AND ONE ON ITS OWN IS WORTH NOTHING — IT AGREES WITH
THE STALE ANSWER.** Its user `sleep` returns nothing and so does `stdlib/Sleep.maxon`'s, so it
cannot tell "the call reached the user's declaration" from "the call read whichever declaration
folded last". The case below is the NEGATIVE control, and it is the one that failed: a user `sleep`
that RETURNS a value against the listed module's VOID one. Selection already picked the user's (its
`Ms` is what a range refusal named), while the RETURN TYPE came from a bare key the stdlib's fold
had overwritten, so a legal program was refused `E2004: Function 'sleep' does not return a value` —
contradicting a signature two lines above it. The rule is the PAIR; see `namespaces.md`'s
`root-declaration-owns-the-bare-key` cases for the same defect with no stdlib in it at all.

<!-- test: stdlib-whitelist.a-value-returning-user-free-function-outranks-a-void-listed-one -->
```maxon
typealias Ms = int(0 to 1000)

function sleep(milliseconds Ms) returns Ms
	return milliseconds + 7
end 'sleep'

function main() returns ExitCode
	let r = sleep(1)
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

The two UAX #29 classifiers are the case that made this a DEFECT rather than a documentation gap.
They were BARE-NAME BUILTINS in `parseCallNamed`, recognized before any registry is consulted, so a
user's own `graphemeBreakProperty` compiled and was silently unreachable — measured, shv2 printed
`p=0 e=false` where the reference printed `p=164 e=true`, from the very same source. Retiring the two
builtins and listing `stdlib/helpers/string/grapheme.maxon` is what makes the declaration the call
site reaches the one the program contains.

<!-- test: stdlib-whitelist.a-user-grapheme-classifier-is-the-one-that-runs -->
```maxon
typealias Cp = int(0 to 1114111)

function graphemeBreakProperty(cp Cp) returns Cp
	return cp + 99
end 'graphemeBreakProperty'

function isExtendedPictographic(cp Cp) returns bool
	return cp == 65
end 'isExtendedPictographic'

function main() returns ExitCode
	let p = graphemeBreakProperty(65)
	let e = isExtendedPictographic(65)
	print("p={p} e={e}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
p=164 e=true
```

### What the five byte-walking modules deliver

`helpers/string/utf8.maxon`, `helpers/string/hash.maxon` and `helpers/string/grapheme.maxon` are one
entry in three lines — every one of them walks a String's bytes through `String.byteAt`, the
throwing primitive this rung built, and `grapheme.maxon` calls the other two. `Unicode.maxon` and
`Build.maxon` need nothing new at all.

An entry's real content is its CALL SITES and not its own parse: `maxon-shv2 build <module>` answering
`E3001: No 'main' function found` says the module is loadable, never that a program can use it.

<!-- test: stdlib-whitelist.utf8-helpers-from-the-whitelist -->
```maxon
function main() returns ExitCode
	let s = "héllo"
	print("{utf8ByteLengthAt(s, pos: 0)}\n")
	print("{utf8ByteLengthAt(s, pos: 1)}\n")
	print("{utf8DecodeAt(s, pos: 0)}\n")
	print("{utf8DecodeAt(s, pos: 1)}\n")
	if utf8IsLead(104) 'lead'
		print("lead\n")
	end 'lead'
	if utf8IsContinuation(169) 'cont'
		print("cont\n")
	end 'cont'
	print("{utf8EncodeLength(233)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
2
104
233
lead
cont
2
```

<!-- test: stdlib-whitelist.hash-string-from-the-whitelist -->
`hashString` is djb2 over the whole string — the hottest byte-walk in the stdlib, and the one
`Map with (String, V)` will call on every insert. `"a"` is `5381 * 33 + 97`.
```maxon
function main() returns ExitCode
	print("{hashString("a")}\n")
	print("{hashString("")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
177670
5381
```

<!-- test: stdlib-whitelist.unicode-is-whitespace-from-the-whitelist -->
```maxon
function main() returns ExitCode
	if Unicode.isWhitespace(32) 'space'
		print("space\n")
	end 'space'
	if Unicode.isWhitespace(12288) 'ideographic'
		print("ideographic\n")
	end 'ideographic'
	if Unicode.isWhitespace(65) 'letter'
		print("letter\n")
	end 'letter'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
space
ideographic
```

<!-- test: stdlib-whitelist.build-config-from-the-whitelist -->
`Build.build(name)` emits the JSON a `build.maxon` hands the compiler. It is the one new entry that
is neither a byte walk nor a classifier — a `type` with fields, a `static`, and an `Array with
String` — so what it pins is that a listed module of ordinary shape reaches user code intact.

⚠ The JSON comes out on ONE line, and that is BOTH compilers: `print` writes no trailing newline and
`emitBuildConfig` supplies none, so the module's per-line `print` calls run together. MEASURED against the
reference on the identical program — byte-for-byte the same single line — which is what makes this an
agreement rather than a transcription of shv2's answer.
```maxon
function main() returns ExitCode
	Build.build("demo")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
{  "name": "demo",  "output": ".maxon/demo",  "sources": [  ],  "optimize": false,  "debug_info": true}
```

<!-- test: stdlib-whitelist.ascii-classifiers-from-the-whitelist -->
`stdlib/Ascii.maxon`'s six classifiers. It is the first listed module whose bodies are `match` arms
over **`Character` RANGE patterns** (`'0' to '9'`, `'a' to 'z' or 'A' to 'Z'`), which is the construct
BATCH23 built — before it, this module was `E2028` at `:10:4` because the pattern typed `int` against
a `Character` scrutinee. So what this pins is not only that the entry reaches user code, but that the
character rung holds when the `match` is compiled from a STDLIB source rather than from the spec that
built it.

⚠ The last three conditions are the ones worth having, and they are NEGATIVE: `isDigit('x')` and
`isUpper('k')` pin that the range arms have a lower bound as well as an upper one, and
`isAlpha('é')` pins the module's own `c.byteLength() != 1` guard — a two-byte scrutinee must fall out
before the `'a' to 'z'` comparison ever runs. A case with positives alone would pass against a
classifier that answered `true` for everything.

MEASURED against the reference on the identical program — same six lines, same order, same exit — so
this is an agreement between the two compilers rather than a transcription of shv2's own answer.
```maxon
function main() returns ExitCode
	if Ascii.isDigit('7') 'digit'
		print("digit\n")
	end 'digit'
	if Ascii.isAlpha('q') 'alpha'
		print("alpha\n")
	end 'alpha'
	if Ascii.isAlphanumeric('Z') 'alnum'
		print("alnum\n")
	end 'alnum'
	if Ascii.isWhitespace('\t') 'tab'
		print("tab\n")
	end 'tab'
	if Ascii.isUpper('K') 'upper'
		print("upper\n")
	end 'upper'
	if Ascii.isLower('k') 'lower'
		print("lower\n")
	end 'lower'
	if Ascii.isDigit('x') 'notDigit'
		print("UNREACHED-notDigit\n")
	end 'notDigit'
	if Ascii.isUpper('k') 'notUpper'
		print("UNREACHED-notUpper\n")
	end 'notUpper'
	if Ascii.isAlpha('é') 'notAscii'
		print("UNREACHED-notAscii\n")
	end 'notAscii'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
digit
alpha
alnum
tab
upper
lower
```

### The corpus segmenter and the synthesized one, side by side

⭐ **TWO IMPLEMENTATIONS OF ONE TABLE NOW EXIST, AND THIS IS THE ONLY PLACE THEY ANSWER THE SAME
QUESTION.** `countGraphemes(s)` is `stdlib/helpers/string/grapheme.maxon`'s own UAX #29 walk, written
in Maxon over `String.byteAt`; `s.count()` is the segmenter the compiler SYNTHESIZES
(`GraphemeRuntime`), which reads no stdlib at all. Nothing else in the tree makes them disagree
observably — `grapheme-clusters.md`'s 21 cases exercise one side or the other, never both on one
input.

⚠ A failure here is a REAL disagreement between the corpus and the compiler's table, never an
expectation to adjust.

<!-- test: stdlib-whitelist.the-corpus-segmenter-agrees-with-the-synthesized-one -->
```maxon
function report(s String)
	print("{countGraphemes(s)} {s.count()}\n")
end 'report'

function main() returns ExitCode
	report("abc")
	report("")
	report("héllo")
	report("\r\n")
	report("👨‍💻")
	report("Hi🎉中")
	report("🇺🇸")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
0 0
5 5
1 1
1 1
4 4
1 1
```

⚠⚠ **`hasSingleByteGraphemes()` IS ANSWERED A CONSTANT `false`, AND THREE CORPUS FUNCTIONS READ IT —
THE CASE ABOVE DRIVES ONE OF THEM (slice-8 review).** shv2's String record carries `isAscii@40`, the
WEAKER fact, so serving it would count `"\r\n"` as two clusters; `false` declines the shortcut and hands
every input to the walk, which is the definition. That is only sound if it is sound at EVERY reading
site, and `countGraphemes` is one of three — `byteIndexToGraphemeIndex` and `graphemeOffsetToBytePos`
each carry their own shortcut, with their own boundary arithmetic (`byteIdx >= len`, `count > 0`,
`startBytePos < len`) that the walk has to reproduce.

The case below is the other two, on both sides of the divide: an ASCII string, where the REFERENCE takes
its fast path and shv2 walks, and a CR+LF one, where neither does. MEASURED against the reference on the
identical program — byte-for-byte the same three lines — so this is an agreement between two compilers
rather than a transcription of shv2's answer. A failure here means the walk and the shortcut have come
apart, which is what the constant `false` exists to make impossible.

<!-- test: stdlib-whitelist.declining-the-single-byte-shortcut-agrees-with-taking-it -->
```maxon
function main() returns ExitCode
	let a = "abcdef"
	print("{byteIndexToGraphemeIndex(a, byteIdx: 3)} {graphemeOffsetToBytePos(a, startBytePos: 2, count: 2)} {findGraphemeStart(a, beforePos: 4)}\n")
	let c = "a\r\nb"
	print("{byteIndexToGraphemeIndex(c, byteIdx: 3)} {graphemeOffsetToBytePos(c, startBytePos: 0, count: 2)}\n")
	print("{byteIndexToGraphemeIndex(a, byteIdx: 99)} {graphemeOffsetToBytePos(a, startBytePos: 1, count: 99)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4 3
2 3
6 6
```

### `print` and `printError` — the third bare-name retirement (W35)

`stdlib/Print.maxon` and `stdlib/PrintError.maxon` are the third pair listed by retiring a builtin
first, after `sleep` and the two UAX #29 classifiers. Until W35, `print` was a compiler-recognized
BARE NAME matched in `parseCallNamed` before any registry is consulted — which made shv2 the only one
of the three compilers to treat it that way (the bootstrap resolves every `print(...)` through
ordinary overload resolution; v1 deleted its own `parsePrintStatement` and `TokenKind.print` to do the
same), and which made the module unlistable by this file's own rule: a call to the name could never
reach a declaration of it.

⚠ **The harm was NOT a refusal, which is what the rung that filed this predicted.** MEASURED with both
entries added and the builtin still live: `print("hi\n")` compiled clean and `Print.maxon` was never
reached, while its twin one file over raised `E3004` for `__Builtins.writeStderr` — proving the module
WOULD have been analyzed. So the entry would have been a no-op counting +1 on the cone.

The four cases below are what the retirement bought, each measured on this tree.

The first is the differing-declarations control this file demands: a user's own `print` that does
something the listed module's cannot be mistaken for — writing to the OTHER stream. While the builtin
stood, the call site could not see this declaration at all and the text went to stdout.

<!-- test: stdlib-whitelist.a-user-print-outranks-the-listed-module -->
```maxon
function print(value String)
	printError("mine: {value}")
end 'print'

function main() returns ExitCode
	print("x\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
```
```stderr
mine: x
```

The ARITY refusal, which no spec anywhere pinned before this rung — and it is the one message the
retirement CHANGED. The builtin raised its own `'print' takes exactly 1 argument, but 0 were given`
from a transcribed arity constant; the ordinary check reads `stdlib/Print.maxon`'s signature and says
so in the voice every other call gets. Same code, same position, same verdict, different producer:
this is the `sleep` precedent's "one golden holding the builtin's bespoke argument rejection,
replaced by the ordinary one" in its diagnostic form.

<!-- test: stdlib-whitelist.error.print-arity-comes-from-the-listed-declaration -->
```maxon
function main() returns ExitCode
	print()
	return 0
end 'main'
```
```maxoncstderr
error E3036: specs/fragments/stdlib-whitelist/stdlib-whitelist.error.print-arity-comes-from-the-listed-declaration.test:3:2: 'print' expects 1 argument(s) but 0 were provided
```

The VOID-RESULT refusal, also pinned by nothing before this rung (`void-call-result.md` covers
`noop`/`push`/`insert`/`append`/`reserve` and never `print`). This one is UNCHANGED by the
retirement — the builtin raised the identical sentence — which is worth a case precisely because it
is the half that did not move: a reader comparing it against the arity case above can see which of
the two the change touched.

<!-- test: stdlib-whitelist.error.print-void-result-comes-from-the-listed-declaration -->
```maxon
function main() returns ExitCode
	let x = print("a")
	return x
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/stdlib-whitelist/stdlib-whitelist.error.print-void-result-comes-from-the-listed-declaration.test:3:10: Function 'print' does not return a value
```

And the two streams are INDEPENDENT, asserted separately in one program — a spec that only checked
that the text appeared somewhere would pass just as happily if `printError` were an alias for
`print`. (`print-error-function.md` is that feature's own file; this case is here because the two
modules are ONE whitelist entry and this is the entry's end-to-end proof.)

<!-- test: stdlib-whitelist.both-print-modules-from-the-whitelist -->
```maxon
function main() returns ExitCode
	print("out {1 + 1}\n")
	printError("err {2 + 2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
out 2
```
```stderr
err 4
```

### A CONTESTED extension method's call is an edge into stdlib, and the short-circuit has to see it

`userCodeReachesStdlib` is the exact short-circuit this whole derivation opens with: *"no op in user
code names a stdlib function ⇒ no stdlib function is reachable ⇒ file them all `unreachable`"*. It
tests the op's callee NAME against the stdlib function-name set — and a call op carries the OVERLOAD
SET's BASE name until `SemanticCheck.resolveOverloadedCalls` rebinds it to a member.

For most overload sets that costs nothing, because the first declaration KEEPS the bare spelling and
the base therefore IS a function name (`String.contains` is exactly this). For a **contested**
extension method it costs the whole answer: D7's rule is that when a `<Conformer>.<method>` is
declared by extensions in more than one file, **nobody keeps the bare spelling** — `Array.contains`
is declared by `stdlib/Array.maxon`'s `where Element is Equatable` extension AND published onto
`Array` by `stdlib/Interfaces.maxon`'s `extension Iterable`, so its members register as
`Array.contains#type parameter` and `Array.contains#struct` and NOTHING is named `Array.contains`.
The scan therefore missed a real edge, every stdlib name was filed `unreachable`, `lowerMaxonToStd`
lowered no body — and `DeadFunctionElimination` then reached the resolved member from its own root
set and PANICKED (`requireUnreachableStdlibStayedDead`, which is the guard doing its job: without it
the program would have linked and called an EMPTY function).

⭐ The cure is the one this file's own header argues for everywhere else: **the widening is written
ONCE**. `markReachable` already widened a callee through `project.overloadSets`; this scan did not,
which is one fact with two readers and the narrower one deciding. Both now ask
`nameReachesStdlib`. MEASURED red before it: both cases below panicked in
`DeadFunctionElimination`, and adding a single non-overloaded stdlib call (`"a".isAscii()`) to
either one made it compile — which is what identified the short-circuit rather than the walk.

<!-- test: stdlib-whitelist.a-contested-extension-method-is-the-only-edge-into-stdlib -->
```maxon
function main() returns ExitCode
	let nums = [10, 20, 30, 40]
	if nums.contains([20, 30]) 'found'
		return 7
	end 'found'
	return 1
end 'main'
```
```exitcode
7
```

The ELEMENT overload of the same contested set, which registers under a different suffix and is
reached by the same widening. Both are here because the two members are separate functions and a
walk that widened to only the first would keep this one red.

<!-- test: stdlib-whitelist.the-other-member-of-the-contested-set-is-an-edge-too -->
```maxon
function main() returns ExitCode
	let nums = [10, 20, 30, 40]
	if nums.contains(30) 'found'
		return 7
	end 'found'
	return 1
end 'main'
```
```exitcode
7
```

⭐ **THE TWO CASES ABOVE SHIPPED DISABLED FOR TWO RUNGS, AND `ARR3b` IS THE CONDITION THEY NAMED.** They
were written RED and measured RED — both panicked in `DeadFunctionElimination`, naming
`Array.contains#struct` and `Array.contains#type parameter` — against a tree where `contains` had been
struck from `Parser.arraySurfaceMemberNames`, and they went GREEN on the widening. `contains` did not
retire at the time (its corpus body faulted), so with the roster serving it these two programs never
crossed into stdlib at all and would have PASSED without touching the rule they exist for. `contains` is
struck now, so they cross, and they are live again.

⚠ **`Array.contains` IS STILL THE ONLY CONTESTED `<Conformer>.<method>` THE CORPUS HAS**
(`stdlib/Interfaces.maxon`'s `extension Iterable` and `stdlib/Array.maxon`'s `where Element is Equatable`
extension are the two files; every other method either of them declares is unique to one), and a user file
still cannot manufacture a second: RE-MEASURED at `ARR3b`, a user `extension Array` declaring
`filter(element Element)` beside `Iterable`'s `filter(keep ElementPredicate)` does not resolve by argument
type — `nums.filter(10)` reports `E3005 'if' requires a bool condition, got 'struct'`, i.e. it binds the
stdlib member. That is a separate finding about overload resolution across a contested set, not a vehicle
for these two.

## `stdlib/Range.maxon` — the entry W62 added, and the two things that make it real

**Listed 2026-08-12 (BATCH36).** Its one blocker was `W60`: `RangeIterator implements Iterator with
RangeBound, BidirectionalIterator`, where `BidirectionalIterator extends Iterator`, had the inherited
`current()` checked with NO binding, so the module read `expected current() returns Element` against its
own `returns RangeBound`. With that cured it probes `E3001` and nothing else.

⚠ **A GREEN `E3001` IS EVIDENCE ONLY FOR THE DECLARATIONS THE COMPILER ACTUALLY ANALYZED**, so the entry
was checked the two ways this file's siblings demand rather than on the probe alone:

- **The injection control FIRES.** `let bogus = NoSuchType.definitelyUndefined(1)` placed in
  `RangeIterator.current()`'s body answers `E3001` **+ `E3004`** — the control the six `helpers/sort/*`
  files fail, so this module's readiness is not the vacuous kind.
- **The entry is BYTE-NEUTRAL, measured both ways on one tree** rather than against a constant remembered
  from another rung: `function main() returns ExitCode / return 7` compiles to **1,592 bytes of code with
  the entry and 1,592 without**, from two full compiler builds differing only in the whitelist line.
  *(The executable-level `cmp` that `S2k` also ran was NOT repeated here; this is the codeBytes match.)*

⭐ **AND THE MODULE IS REACHED THROUGH THE PROTOCOL, NOT MERELY LOADED.** `Range implements Iterable with
(RangeBound, RangeIterator)`, so the case below drives `createIterator()` -> `current()` -> `advance()`
across the witness edge — which is `W60`'s cure doing work in a real program rather than in a reduction.
**Capability oracle-agreed: the bootstrap answers 25 for the identical source.**

<!-- test: stdlib-whitelist.range-iterates-through-its-iterable-conformance -->
```maxon
function main() returns ExitCode
	let r = Range.create(3, finish: 7)
	var total = 0
	for v in r 'loop'
		total = total + v
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
25
```

⚠ **WHAT THE ENTRY DOES NOT BUY, MEASURED: `to` IN EXPRESSION POSITION.** `Range.maxon`'s own header
describes it as producing *"first-class iterable values produced by `start to end` … used in expression
position"*, and that syntax is **still `E2001`** on this tree — `let r = 3 to 7` does not parse. The
desugaring of a `for … in` header remains the direct while-loop it always was. So the module is listed and
reachable **by name**, and the surface its own documentation advertises is a separate door that no row yet
owns. Pinned here rather than left in a rung report, because the next reader of this entry will otherwise
assume the entry delivered it.

<!-- test: stdlib-whitelist.error.range-is-not-yet-constructible-from-to-in-expression-position -->
```maxon
function main() returns ExitCode
	let r = 3 to 7
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:3:12: unexpected token: 'to'
```

## `stdlib/Json.maxon` — the entry W69 added, and the control that proves it is not inert

**Listed 2026-08-12 (W69).** At 1,080 lines it is the largest pure-corpus module the list carries and
the first that is a whole SUBSYSTEM rather than a handful of leaf functions: a recursive-descent parser
(`JsonParser`), an arena of nodes (`JsonDoc` over `JsonNodeArray`), and 22 free emitter functions. Its
last diagnostic blocker was `W66`'s field access through a struct-typed field (`Json.maxon:281`) and,
past it, an `E2015` at `:400:21` that upstream's *a durable store CO-OWNS* cure cleared; it now probes
`E3001` and nothing else.

⚠ **A GREEN `E3001` IS EVIDENCE ONLY FOR THE DECLARATIONS THE COMPILER ACTUALLY ANALYZED**, and for a
module whose name the compiler might SYNTHESIZE it is not evidence at all — that is the trap that made
`Set`, `Vector` and `unicodeCategory` inert listings. Both were checked rather than assumed:

- **The injection control FIRES.** `let bogus = NoSuchType.definitelyUndefined(1)` placed in
  `findKeyInNode`'s body answers `E3001` **+ `E3004`**, the control the six `helpers/sort/*` files fail.
- **There is no synthesized twin to out-vote the declaration.** `Json` is on neither
  `TypeResolution.isCompilerOwnedTypeName` nor `builtinTypeNameTag`, it is not one of the seven
  `*BuiltinBaseName` roots (`Set`, `Map`, `List`, `Vector` and the three `__Managed*`), and the whole
  compiler mentions the name only in prose. So the listed declaration is the only one there is.

⛔ **AND THE ENTRY WAS NOT BYTE-NEUTRAL WHEN FIRST ADDED.** The fault was not this module's: a STRING
field default mints a nullary helper, and `LowerMaxonToStd.registerProgramLiteralBlobs` walked it with no
unreachable-stdlib gate — a THIRD pre-elimination door onto `GlobalDataTable.nextStringId` where
`InsertRangeChecks`'s header claims the class is shut at two. The measurement and the cure are recorded
once, at the entry in `StdlibLoader.maxon`; what belongs here is only that it was found and not papered
over by a re-mint.

⚠ **WHAT FOUND IT WAS THE DRIFT COUNT, NOT A FAILING CASE, AND THIS SECTION SAID OTHERWISE FOR ONE
COMMIT.** `a-listed-modules-literals-are-byte-neutral` shifted by a label and reported **PASS** — measured
directly at the W69 review, on a rebuilt compiler with the gate neutralised. A golden is REFERENCE, not a
gate. The case that turns this invariant red is
`a-listed-modules-literals-cannot-reach-the-rdata-image`, added by that review.

⭐ **AND THE TWO CASES BELOW ARE THE DIFFERING-DECLARATIONS CONTROL IN SPEC FORM.** Neither can pass
against an inert entry and neither can pass by merely NAMING the type: each drives the module's own
logic end to end and checks a value only that logic can produce. Both are oracle-agreed — the bootstrap,
which loads all of `stdlib/`, answers identically on the identical source.

<!-- test: stdlib-whitelist.json-parse-and-read-from-the-whitelist -->
`Json.parse` over a document holding a number, a string, a bool and an array, then five accessors
reading back out. What it exercises is the parse machinery: `JsonParser`'s whitespace skipping, its object and
array recursion, its string and number scanners, `findKeyInNode`'s linear key walk, and `JsonDoc`'s
arena indirection — `second` is read through `doc.get(id).numberValue`, so the node id the array
returned has to name the right arena slot. The last read is NEGATIVE and is the one worth having:
`getInt` for a key the document does not carry must reach the `otherwise` arm, so a `findKeyInNode`
that answered with any id at all would fail here rather than pass quietly.
```maxon
function main() returns ExitCode
	let doc = try Json.parse("\{\"count\": 7, \"name\": \"maxon\", \"ok\": true, \"tags\": [10, 20, 30]\}") otherwise 'parseErr'
		panic("Json.parse rejected a valid document")
	end 'parseErr'
	let count = try doc.getInt(doc.root, key: "count") otherwise 'countErr'
		panic("count missing")
	end 'countErr'
	let name = try doc.getString(doc.root, key: "name") otherwise 'nameErr'
		panic("name missing")
	end 'nameErr'
	let ok = try doc.getBool(doc.root, key: "ok") otherwise 'okErr'
		panic("ok missing")
	end 'okErr'
	let tags = try doc.getChild(doc.root, key: "tags") otherwise 'tagsErr'
		panic("tags missing")
	end 'tagsErr'
	let n = try doc.arrayLength(tags) otherwise 'lenErr'
		panic("tags is not an array")
	end 'lenErr'
	let secondId = try doc.arrayAt(tags, index: 1) otherwise 'atErr'
		panic("tags[1] missing")
	end 'atErr'
	let second = trunc(doc.get(secondId).numberValue)
	print("{name} count={count} tags={n} second={second} ok={ok}\n")
	let absent = try doc.getInt(doc.root, key: "missing") otherwise 'absent'
		print("absent key refused\n")
		return (count + n + second) as ExitCode
	end 'absent'
	print("UNREACHED {absent}\n")
	return 0
end 'main'
```
```exitcode
30
```
```stdout
maxon count=7 tags=3 second=20 ok=true
absent key refused
```

<!-- test: stdlib-whitelist.json-stringify-round-trips-through-the-whitelist -->
The other half of the module, which the parse case cannot reach: the 22 free emitter functions, driven
by building an arena BY HAND through `JsonNode`'s exported constructors and serializing it. The emitted
text is checked literally, so it pins `writeJsonString`'s escaping of an embedded quote, `writeNumber`'s
integral shortcut (`2.5` keeps its fraction, `1.0` prints as `1`), `writeBool` and `nullBytes` — and
then the same text is fed back through `Json.parse`, so a serializer that emitted something almost-JSON
would fail on the round trip rather than only on the string compare.
```maxon
function main() returns ExitCode
	var doc = JsonDoc.create()
	var keys = StringArray.create()
	var children = JsonNodeIdArray.create()
	keys.push("name")
	children.push(doc.add(JsonNode.stringNode("a\"b")))
	keys.push("size")
	children.push(doc.add(JsonNode.numberNode(2.5)))
	keys.push("done")
	children.push(doc.add(JsonNode.boolNode(false)))
	var items = JsonNodeIdArray.create()
	items.push(doc.add(JsonNode.numberNode(1.0)))
	items.push(doc.add(JsonNode.nullNode()))
	keys.push("items")
	children.push(doc.add(JsonNode.arrayNode(items)))
	doc.root = doc.add(JsonNode.objectNode(keys, children: children))
	let text = Json.stringify(doc)
	print("{text}\n")
	let round = try Json.parse(text) otherwise 'reparse'
		panic("stringify emitted something parse rejects")
	end 'reparse'
	let size = try round.getInt(round.root, key: "size") otherwise 'sizeErr'
		panic("size missing after round trip")
	end 'sizeErr'
	let name = try round.getString(round.root, key: "name") otherwise 'nameErr'
		panic("name missing after round trip")
	end 'nameErr'
	print("{name} {size}\n")
	return (size + 40) as ExitCode
end 'main'
```
```exitcode
42
```
```stdout
{"name":"a\"b","size":2.5,"done":false,"items":[1,null]}
a"b 2
```

<!-- test: stdlib-whitelist.json-negative-zero-round-trips -->
`-0` is a legal JSON number (`[ minus ] int`) and a distinct double from `0`, so it must survive a
round trip in BOTH directions. The parse half is `numberFromBytes`'s `result = -result`, which until
2026-08-30 compiled as `0.0 - result` and answered `+0.0`; the serialize half is `writeNumber`'s
integral shortcut, which cannot see the sign through `==` and has to ask `Math.hasNegativeSignBit`.
`0` and `-0.5` ride along as the controls on either side of the shortcut.
```maxon
function main() returns ExitCode
	let negativeZero = try Json.parse("-0") otherwise 'parseNegativeZero'
		panic("Json.parse rejected -0")
	end 'parseNegativeZero'
	if not Math.hasNegativeSignBit(negativeZero.get(negativeZero.root).numberValue) 'signLost'
		return 1
	end 'signLost'
	print("{Json.stringify(negativeZero)}\n")
	let positiveZero = try Json.parse("0") otherwise 'parsePositiveZero'
		panic("Json.parse rejected 0")
	end 'parsePositiveZero'
	print("{Json.stringify(positiveZero)}\n")
	let negativeHalf = try Json.parse("-0.5") otherwise 'parseNegativeHalf'
		panic("Json.parse rejected -0.5")
	end 'parseNegativeHalf'
	print("{Json.stringify(negativeHalf)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-0
0
-0.5
```
