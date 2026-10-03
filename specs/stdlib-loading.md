---
feature: stdlib-loading
status: stable
keywords: [stdlib, loader, Clock, WallClock, dead-function-elimination, runtime-floor]
category: system
---

# Loading the stdlib

## Documentation

The stdlib loader enumerates every `.maxon` file under the checkout's `stdlib/`, top level and
subdirectories alike — and loads all of them. NO file is held back: the loader carries no exclusion,
by name or by filter. This spec deliberately does not restate what `stdlib/` CONTAINS:
nothing would keep a prose copy of that inventory agreeing with the directory, and the same argument
is made below about the bare-builtin roster, where both prose copies had already drifted.

Everything downstream of the loader deals in two facts — "is this function stdlib source or user
source?" and "is it reachable from `main`?" ⇒ What follows is written about a STDLIB MODULE.

A stdlib module is registered into the query database exactly like a user source, so it flows
through the same tokenize → signature-index → parse → merge spine. Its declarations therefore
populate the signature registry, and a user program can call `Clock.nowMs()` /
`WallClock.nowUnixSeconds()` though the program itself contains no `Clock`.

That sameness runs one level deeper: which files under a directory are Maxon sources is decided by
ONE enumerator (`Compiler.collectMaxonSources`), which walks the user's own root and `stdlib/` alike,
so the `.maxon` extension it admits and every ignore rule either walk grows are stated once.
The loader also skips a file the project has ALREADY registered — the one case being a user root that
lies under `stdlib/` — because a source registered under two spellings of its path is parsed twice and
every function in it then collides with itself. (The harness stages every fragment in a temp directory,
outside the checkout's `stdlib/`, so no case below pins this.)

`stdlib/` is located by walking UP from the COMPILER's own executable directory, not from the
current working directory: the compiler may be run from any directory — the spec runner compiles in a
throwaway temp dir — so the working directory need have no `stdlib/` above it, while the compiler binary
always lives inside the checkout. A missing `stdlib/` is a loud, hard error — never a silent skip.

### An unused stdlib module changes NOTHING

Every stdlib module reaches EVERY compile, so a program that never reads a clock must compile to the
exact bytes it would without `stdlib/Clock.maxon` — same x64 goldens, same wasm/arm64 output.
Two mechanisms compose to guarantee that:

1. Dead-function elimination drops every stdlib function no reachable code calls, before the
   back end — so an unused Clock never reaches instruction selection, register allocation or
   encoding, and never lands in a golden, on any target.

2. The runtime-floor decision (`scanRuntimeUsage`: does this program carry the heap / GT scheduler /
   wall clock?) runs at the Maxon tier, BEFORE that elimination. Clock.nowMs's body calls
   `__gt_now_ns` and WallClock.nowUnixSeconds's calls `__clock_now_unix_s`, so a naive load would
   install the scheduler and a `.data` slot for a program that reads no clock — slots the later
   elimination cannot prune. That scan therefore SKIPS the stdlib functions no path from `main`
   reaches (`LibraryFacts.unreachable`): code the program does not contain feeds the floor decision
   nothing. User functions are never skipped — an unreached user body still speaks for the program —
   so nothing about a program that touches no stdlib changes.

⭐ **THIS PROPERTY IS WHAT KEEPS THE WHOLE OF `stdlib/` FREE** — a program that reaches none of it pays
for none of it, and every module still to be written is admitted on exactly those terms rather than on a
maintainer's judgement.

The whole `specs` corpus — every program that never touches Clock — is the standing proof of this.
`no-clock-is-byte-neutral` below is the same guard stated directly.

#### A stdlib module's own LITERALS must not renumber the program's `.rdata`

Elimination is not the only pass that runs over a stdlib module's bodies, and it is not the earliest.
Everything a body registers in the read-only data section is registered BEFORE elimination, by
lowering: a string literal mints a byte blob and a 48-byte record, a byte-string literal mints a blob,
and a generic receiver mints a layout descriptor. Elimination prunes FUNCTIONS, never `.rdata`, so
every one of those payloads outlives the function that asked for it.

That matters because the synthetic `.rdata` labels are minted from ONE counter shared by every prefix.
A single surviving blob therefore renumbers `__str_blob_` AND `__jumptable_` labels program-wide — in a
program that mentions no string at all.

⚠ **`__fconst_` IS NOT ON THAT LIST.** A float island is named by its VALUE
(`__fconst_-5.5`, `GlobalDataTable.registerFloatConstant`) and registered through the LABELLED door, which does not touch the
shared counter — so a float label cannot move for a reason outside its own value. That keeps one prefix
out of the blast radius; it does not shrink the detection, because the two remaining prefixes still renumber
together and the case below names both.

So a pre-elimination pass may not let an unreachable stdlib body register anything either, and
`lowerMaxonToStd` skips such a body on exactly the reachability fact the runtime-floor scan skips on.
The two derivations are independent — one walks the Maxon module from `main`, the other walks the Std
module from a larger root set that includes every function an `.rdata` slot names — so the elimination
pass CHECKS that it drops every function whose body lowering skipped, rather than assuming it. A
disagreement would otherwise link cleanly and call an empty function.

`a-stdlib-modules-literals-are-byte-neutral` below is that guard stated as a golden: it compiles a
program holding a float constant and a dense-`match` jump table, so its fragment names labels from two
of the three prefixes the shared counter mints, and any stdlib module that registers `.rdata` for code
the program cannot reach moves them. (A payload registered under a STRUCTURAL label — a witness table,
a layout descriptor — does not advance that counter, so it shifts `.rdata` OFFSETS without moving any
label a fragment prints. The lowering skip covers it; no fragment golden can see it.)

### The collision rule

A stdlib module must declare no name a user program declares and no builtin type/name. These are
rules about ADDING OR CHANGING a stdlib module.

- FUNCTION-name collisions — a user `Clock.nowMs` against the stdlib one, or two stdlib
  modules against each other — are caught loudly by the whole-program duplicate-function check,
  `E3006`, because a stdlib file merges through the identical path a user file does. A user program
  that declares its own `type Clock` with a `nowMs()` is rejected with

  ```text
  error E3006: <path>/stdlib/Clock.maxon:18:25: Duplicate function 'Clock.nowMs'
  ```

  naming the stdlib definition it collided with. (That path is the real `stdlib/Clock.maxon`,
  not the test fragment, so this diagnostic is documented here rather than pinned as a golden — the
  runner only rewrites the fragment's own path to `<fragment>`.)

- TYPE-name-vs-builtin collisions are the maintainer's responsibility, with **exactly two
  exceptions**. `String` and `Character` are REFUSED as user type names (`E2015`, via
  `isCompilerOwnedTypeName`), because those two are the only builtins that mint
  CONFORMANCE IMPL SYMBOLS (`String.hash`, `Character.hash`, …) which a user declaration of the same
  name would not collide with but silently **REPLACE**: `undefinedImplNames` would then decline to install
  the builtin. Unrefused, both would be silent and undiagnosed: a `Box with String` whose
  `.itemHash()` of `""` answers the user's body instead of djb2's `5381`, and a `Set with String`
  that stores `"alice"` **twice** because the user's `equals` answers `false`.
  ⚠ **For every OTHER builtin it is the maintainer's responsibility** — the compiler has no general
  builtin-type-redeclaration diagnostic, and enforcing one is a language matter rather than a loader
  one. Clock declares `Clock`/`WallClock` and time typealiases, none of them builtin, so it
  cannot hit this. Do not add a stdlib module that redeclares a builtin.

- FUNCTION-name-vs-BARE-BUILTIN collisions are SILENT, so the builtin is RETIRED FIRST. The parser
  recognizes a set of BARE names — `print` and `trunc` among them — before any registry is consulted,
  so while one of them claims a name, a CALL to that name never reaches a declaration of it: loading
  such a module would compile code no ordinary call site can reach while charging the per-compile
  load cost for zero delivered capability, and the name would have two routes — `let f = name` is not
  a call site, so it takes the address of the declaration the call sites cannot see.

  The same shadowing hides a user declaration: while a bare builtin claims a name, a user file
  declaring its own function of that name compiles with the declaration silently unlinked, no
  diagnostic, and its calls still reach the builtin — a wrong answer. Retiring the builtin and letting
  the module load repairs both, and it is the pattern for every builtin still standing in for a stdlib
  module. **The rule: do not add a stdlib module whose function name a bare builtin
  claims. Retire the builtin first — and check the roster at its SOURCE, `parseCallNamed`'s bare-name
  `if` chain, never against a list written in prose.** This spec does not restate the roster,
  deliberately: nothing makes a prose copy agree with the chain, and a maintainer who checks a module
  against a list rather than the chain can be told a claimed name is free.

  ⚠ **WHAT THE MODULE THEN OWNS, IT DOES *NOT* OWN AGAINST THE USER.** Under namespaces a user
  `function sleep` compiles clean, and the USER's body is what its own call sites reach. The cases
  that establish it are *A user's own declaration outranks a stdlib module's free function* below.

### A diagnostic raised inside stdlib source is attributed to the crossing call

Stdlib source is compiled as part of the program, so a rejection raised in one of its bodies would be
positioned at ITS path — an absolute `…/stdlib/Foo.maxon:L:C` naming a file the user never opened, at
a line they cannot change, for a choice they made at a call site somewhere else.

That is not a quirk of one module: it fires wherever a stdlib body bottoms out in something
target-gated, which is exactly what a stdlib leaf is FOR — Clock and every module behind it ends
in a `__Builtins.*` intrinsic — and it gets more common, not less, as stdlib grows.

The refusal that reaches this is the TARGET gate, `E3104`. It is reported at the FIRST call
crossing from user code INTO stdlib, and it names the stdlib function the user actually wrote:

```text
error E3104: <fragment>:3:16: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

The requirement is TRANSITIVE through the stdlib call graph: `Clock.elapsedMs` names no runtime entry
itself — it calls `Clock.nowMs`, which does — so a program calling `elapsedMs` is refused at ITS call,
naming `Clock.elapsedMs` and still naming the entry that has no lowering. A user's own helper is user
code, so `main → myHelper → Clock.nowMs` is blamed inside `myHelper`. And a stdlib function no path
from `main` reaches is refused nowhere at all, which is what keeps an unused module byte-neutral.

The gate is therefore reachability-BLIND for user code and reachability-AWARE for stdlib source: an
`__Builtins.sleep(1)` in a function `main` never calls is still refused for wasm
(`builtins-sleep.rejected-on-wasm-when-unreached`), while a `Clock.nowMs()` in one is not.

A retired bare-name builtin puts a program in the second case rather than the first: `sleep(1)` is a
call into stdlib rather than a `__gt_sleep` in user code, so an unreached one compiles for wasm
(`async-sleep.unreached-compiles-on-wasm`). Every builtin retired the same way moves the same way, and it
is the correct direction — the entry genuinely is stdlib's, and a program that cannot reach it does
not contain it.

## Tests

<!-- test: stdlib-loading.clock-from-stdlib -->
A program that calls `Clock.nowMs()` and `WallClock.nowUnixSeconds()` but declares NEITHER type
compiles and runs — the declarations came from stdlib, not the program. A monotonic reading
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

<!-- test: stdlib-loading.elapsed-through-a-sibling -->
`Clock.elapsedMs(since:)` is a stdlib function that itself calls another stdlib function
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
		return 7
	end 'nonNegative'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-loading.nanos-from-stdlib -->
`Clock.nowNanos()` reaches the same monotonic counter as `nowMs` but through the nanosecond entry —
another stdlib function pulled in only because the program names it.
```maxon
function main() returns ExitCode
	let a = Clock.nowNanos()
	let b = Clock.nowNanos()
	if b >= a 'nonDecreasing'
		return 4
	end 'nonDecreasing'
	return 1
end 'main'
```
```exitcode
4
```

<!-- test: stdlib-loading.no-clock-is-byte-neutral -->
A program that mentions no stdlib module must compile to exactly what it did before any of them
existed: the loader hands Clock AND Sleep to this compile too, and every one of their functions is
pruned back out with no runtime floor installed. This case carries NO target restriction, so its
byte-neutrality is checked on wasm as well — loading stdlib must not drag the x64-only clock or timer
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

<!-- test: stdlib-loading.a-stdlib-modules-literals-are-byte-neutral -->
The sibling of `no-clock-is-byte-neutral`, for the half that case cannot see. That program's fragment
names no `.rdata` label at all, so it stays green while every synthetic label in the corpus renumbers.
This one holds a dense-`match` jump table and a String blob, so its fragment NAMES labels off the one
shared counter — `__jumptable_1` and `__str_blob_0`, the last being the range-check panic message that took
id 0. A stdlib module that registers ANY `.rdata` for code no path from `main` reaches moves both.

⚠ The float constant is still in the program and is still worth having — it is what makes the jump table
share a compile with an island of another kind — but its `__fconst_12.5` label is not a counter
reading: float islands are named by their value and take the labelled door.

⛔⛔ **BUT IT DETECTS THAT THROUGH ITS GOLDEN, SO IT CANNOT FAIL — IT IS A READING, NOT A GATE.** A
fragment mismatch prints a `note:`, counts as no failure and leaves the exit code at 0. With
`registerProgramLiteralBlobs`' unreachable-stdlib gate neutralised — an orphan blob at `.rdata` byte 0
of every program in the suite — this case still PASSES. Keep it: the drift it prints names the moved labels, which no other case does. But the
enforcement lives in `a-stdlib-modules-literals-cannot-reach-the-rdata-image` below, which pins the linked
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

<!-- test: stdlib-loading.a-stdlib-modules-literals-cannot-reach-the-rdata-image -->
⛔⛔ **THE BYTE-NEUTRALITY CLAIM'S ONLY GATE. ITS TWO SIBLINGS ABOVE CANNOT FAIL.**
`a-stdlib-modules-literals-are-byte-neutral` detects a displaced `.rdata` payload through its golden
FRAGMENT — and a fragment mismatch is REFERENCE, NOT A GATE: it prints a `note:`, counts as no failure
and leaves the exit code at 0. Neutralising `LowerMaxonToStd.registerProgramLiteralBlobs`'
unreachable-stdlib gate puts an orphan blob from `stdlib/Json.maxon` at `.rdata` byte 0 of every program
in the suite, and that case still PASSES. It is a real reading and a useful one, but nothing in the
battery turns it red.

⭐ **A ```RequiredRdata BLOCK IS THAT ENFORCEMENT, AND THE FIT IS EXACT.** The block is compared as a run
FROM BYTE 0, read back out of the LINKED IMAGE rather than out of the compiler's opinion of it — so it
answers precisely "did anything get in front of this program's read-only data?". This program's whole
`.rdata` is its two float constants, 16 bytes, every one of them pinned; a stdlib module that registers ANY
`.rdata` for code no path from `main` reaches lands ahead of them, because `registerProgramLiteralBlobs`
runs before the target tier mints a float. With the gate neutralised it reports
`.rdata mismatch at byte 6: expected 0x29, got 0x00` — eight zero bytes of orphan where `12.5` belongs.

⚠ **IT MUST HOLD NO STRING LITERAL OF ITS OWN, and that is not a stylistic choice.** The user's `main` is
walked before stdlib's functions, so a program literal in `main` keeps byte 0 whatever an orphan does and
the pin goes green on the broken compiler. The payload a displacement is visible against has to
be one the COMPILER composes.

⛔⛔ **AND THE LOOP IS LOAD-BEARING: WITHOUT IT THIS PROGRAM HAS NO `.rdata` AT ALL.** Straight-line
`let scale = 12.5` / `let floor = 1.5` / `if scale > floor` is folded by `foldConstants`, which folds
FLOATS — the comparison becomes a constant, `foldConstantBranches` takes the arm, and both float
`const`s are retired unread. The program still returns 8, but materialises neither float, the linked
image has no `.rdata` section, and this gate cannot read the thing it gates: `could not read the .rdata
section … has no .rdata section`.

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

<!-- test: stdlib-loading.target-refusal-blames-the-crossing-call -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A stdlib body that bottoms out in an x64-only runtime entry is refused at the USER's call, and
names the stdlib function they wrote — never at `stdlib/Clock.maxon`, a file they never opened
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
error E3104: <fragment>:3:16: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-loading.target-refusal-blames-the-crossing-call-second-entry -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The attribution is a property of the CROSSING and not of one runtime entry: a second stdlib function
reaching a second entry is refused at the same user span, naming what the author wrote and the entry that
has no lowering.

⚠⚠ **THE SUBJECT IS THE ATTRIBUTION, AND NO NATIVE LANE HAS A CROSSING TO SHOW IT ON**, which is a
fact about the table rather than about this case: `TargetFacilities.targetProvidesFacility` answers
`true` for every `HostFacility` on arm64-macOS and on arm64-linux, so
`targetProvidesEveryFacility` short-circuits `checkCalls` there and NO `E3104` can be raised on either.
The one native gap left is x64-linux's `processPriority`, whose only door is a `__Builtins` intrinsic
rather than a stdlib function — so it cannot show a stdlib CROSSING at all.

⇒ Both cases run on wasm32-wasi, and what the pair shows is the half that has a subject: two
different stdlib functions reaching two different entries produce the same attribution. ⚠ **The half it
cannot show is "not of one backend"** — the shape a completed lane leaves behind; a native lane that
grows a new facility is where this case belongs.
```maxon
function main() returns ExitCode
	let c = try TcpClient.connect("127.0.0.1", port: 9) otherwise return 4
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:24: 'TcpClient.connect' lowers to the runtime entry '__ms_tcp_connect', which has no wasm32-wasi implementation
```

<!-- test: stdlib-loading.target-refusal-is-transitive -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
`Clock.elapsedMs` reaches no runtime entry itself — it calls `Clock.nowMs`, which does. The
requirement propagates through the stdlib call graph, so the caller is blamed at the function
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
error E3104: <fragment>:3:16: 'Clock.elapsedMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-loading.target-refusal-blames-the-users-own-helper -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A user's own function is not stdlib, so the crossing is the call INSIDE it — code the user can
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
error E3104: <fragment>:3:15: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-loading.unreached-clock-still-compiles-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The crossing gate is reachability-AWARE, exactly as the runtime-floor skip is: `reader` is never
called, so no path from `main` crosses into stdlib and the program compiles for wasm
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

### A user's own declaration outranks a stdlib module's free function

A stdlib module's free functions are stdlib's; a user program's are the user's. Where the two spell
the same name the USER's declaration is what its own call sites reach, which is what namespaces make
true here.

⚠ **THE TWO CASES BELOW RUN THAT CLAIM**: `sleep` is a stdlib module's free function, a user `function
sleep` compiles clean — not an `E3006` duplicate — and the user's body is what runs.

<!-- test: stdlib-loading.a-user-free-function-outranks-the-stdlib-modules -->
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

⚠⚠ **THE CASE ABOVE IS A POSITIVE CONTROL AND ONE ON ITS OWN IS WORTH NOTHING.** Its user `sleep`
returns nothing and so does `stdlib/Sleep.maxon`'s, so it cannot tell "the call reached the user's
declaration" from "the call read whichever declaration folded last". The case below is the NEGATIVE
control: a user `sleep` that RETURNS a value against the stdlib module's VOID one. Selection picking
the user's while the RETURN TYPE comes from a bare key the stdlib's fold overwrote refuses a legal
program with `E2004: Function 'sleep' does not return a value` — contradicting a signature two lines
above it. The rule is the PAIR; see `namespaces.md`'s `root-declaration-owns-the-bare-key` cases for
the same property with no stdlib in it at all.

<!-- test: stdlib-loading.a-value-returning-user-free-function-outranks-a-void-stdlib-one -->
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

The two UAX #29 classifiers are declared by `stdlib/helpers/string/grapheme.maxon` rather than
recognized as BARE-NAME BUILTINS in `parseCallNamed`, which is what makes the declaration the call
site reaches the one the program contains: a bare builtin is matched before any registry is consulted,
so a user's own `graphemeBreakProperty` would compile and be silently unreachable.

<!-- test: stdlib-loading.a-user-grapheme-classifier-is-the-one-that-runs -->
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
job in three files — every one of them walks a String's bytes through `String.byteAt`, the
throwing primitive, and `grapheme.maxon` calls the other two. `Unicode.maxon` and
`Build.maxon` need nothing new at all.

A module's real content is its CALL SITES and not its own parse: `maxon build <module>` answering
`E3001: No 'main' function found` says the module is loadable, never that a program can use it.

<!-- test: stdlib-loading.utf8-helpers-from-stdlib -->
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

<!-- test: stdlib-loading.hash-string-from-stdlib -->
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

<!-- test: stdlib-loading.unicode-is-whitespace-from-stdlib -->
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

<!-- test: stdlib-loading.build-config-from-stdlib -->
`Build.build(source, output:)` emits the JSON a `.maxproj` target hands the compiler. It is the one new entry that
is neither a byte walk nor a classifier — a `type` with fields, a `static`, and an `Array with
String` — so what it pins is that a stdlib module of ordinary shape reaches user code intact.

⚠ It comes out on stdout HERE because `MAXON_BUILD_DESCRIPTION` is unset: a program running outside
`maxon build` has no description file to write to, and printing is what lets one be inspected.
```maxon
function main() returns ExitCode
	Build.build("src", output: ".maxon/demo")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
{
  "output": ".maxon/demo",
  "sources": ["src"],
  "debug_info": true,
  "version": "",
  "defines": []
}
```

<!-- test: stdlib-loading.build-config-escapes-strings -->
⛔ **EVERY STRING THE CONFIG PRINTS IS JSON-ESCAPED.** A `"` in an output path would otherwise close the
JSON string early, and a Windows path's `\` would start an escape the compiler's JSON reader rejects or
misreads — so the output comes out as `.maxon/a\"b` and the source as `src\\app`, and the document stays
the same shape `build-config-from-stdlib` pins.
```maxon
function main() returns ExitCode
	Build.build("src\\app", output: ".maxon/a\"b")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
{
  "output": ".maxon/a\"b",
  "sources": ["src\\app"],
  "debug_info": true,
  "version": "",
  "defines": []
}
```

<!-- test: stdlib-loading.build-config-asks-for-a-rebuild-with-its-output -->
```maxon
function main() returns ExitCode
	Build.build(".", rebuildWithOutput: true)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
{
  "output": "",
  "sources": ["."],
  "debug_info": true,
  "version": "",
  "defines": [],
  "rebuild_with_output": true
}
```

<!-- test: stdlib-loading.ascii-classifiers-from-stdlib -->
`stdlib/Ascii.maxon`'s six classifiers. Its bodies are `match` arms over **`Character` RANGE
patterns** (`'0' to '9'`, `'a' to 'z' or 'A' to 'Z'`), so what this pins is not only that the module
reaches user code, but that `Character` range patterns hold when the `match` is compiled from a STDLIB
source rather than from a spec.

⚠ The last three conditions are the ones worth having, and they are NEGATIVE: `isDigit('x')` and
`isUpper('k')` pin that the range arms have a lower bound as well as an upper one, and
`isAlpha('é')` pins the module's own `c.byteLength() != 1` guard — a two-byte scrutinee must fall out
before the `'a' to 'z'` comparison ever runs. A case with positives alone would pass against a
classifier that answered `true` for everything.
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

⭐ **TWO IMPLEMENTATIONS OF ONE TABLE EXIST, AND THIS IS THE ONLY PLACE THEY ANSWER THE SAME
QUESTION.** `countGraphemes(s)` is `stdlib/helpers/string/grapheme.maxon`'s own UAX #29 walk, written
in Maxon over `String.byteAt`; `s.count()` is the segmenter the compiler SYNTHESIZES
(`GraphemeRuntime`), which reads no stdlib at all. Nothing else in the tree makes them disagree
observably — `grapheme-clusters.md`'s 21 cases exercise one side or the other, never both on one
input.

⚠ A failure here is a REAL disagreement between the corpus and the compiler's table, never an
expectation to adjust.

<!-- test: stdlib-loading.the-corpus-segmenter-agrees-with-the-synthesized-one -->
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
THE CASE ABOVE DRIVES ONE OF THEM.** The compiler's String record carries `isAscii@40`, the
WEAKER fact, so serving it would count `"\r\n"` as two clusters; `false` declines the shortcut and hands
every input to the walk, which is the definition. That is only sound if it is sound at EVERY reading
site, and `countGraphemes` is one of three — `byteIndexToGraphemeIndex` and `graphemeOffsetToBytePos`
each carry their own shortcut, with their own boundary arithmetic (`byteIdx >= len`, `count > 0`,
`startBytePos < len`) that the walk has to reproduce.

The case below is the other two, on both sides of the divide: an ASCII string and a CR+LF one. A
failure here means the walk and the shortcut have come apart, which is what the constant `false`
exists to make impossible.

<!-- test: stdlib-loading.declining-the-single-byte-shortcut-agrees-with-taking-it -->
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

### `print` and `printError` — a bare-name retirement

`stdlib/Print.maxon` and `stdlib/PrintError.maxon` reach user code because `print` is not a
compiler-recognized BARE NAME: one matched in `parseCallNamed` before any registry is consulted would
make the module unusable by this file's own rule, since a call to the name could never reach a
declaration of it. That harm is not a refusal: with the bare name live, `print("hi\n")` compiles clean
and never reaches `Print.maxon`, so loading the pair would deliver nothing.

The first case below is the differing-declarations control this file demands: a user's own `print` that
does something the stdlib module's cannot be mistaken for — writing to the OTHER stream. A bare builtin
would hide this declaration from the call site and send the text to stdout.

<!-- test: stdlib-loading.a-user-print-outranks-the-stdlib-module -->
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

The ARITY refusal comes from the ordinary check, which reads `stdlib/Print.maxon`'s signature and says
so in the voice every other call gets.

<!-- test: stdlib-loading.error.print-arity-comes-from-the-stdlib-declaration -->
```maxon
function main() returns ExitCode
	print()
	return 0
end 'main'
```
```maxoncstderr
error E3036: specs/fragments/stdlib-loading/stdlib-loading.error.print-arity-comes-from-the-stdlib-declaration.test:3:2: 'print' expects 1 argument(s) but 0 were provided
```

The VOID-RESULT refusal (`void-call-result.md` covers `noop`/`push`/`insert`/`append`/`reserve` and
never `print`).

<!-- test: stdlib-loading.error.print-void-result-comes-from-the-stdlib-declaration -->
```maxon
function main() returns ExitCode
	let x = print("a")
	return x
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/stdlib-loading/stdlib-loading.error.print-void-result-comes-from-the-stdlib-declaration.test:3:10: Function 'print' does not return a value
```

And the two streams are INDEPENDENT, asserted separately in one program — a spec that only checked
that the text appeared somewhere would pass just as happily if `printError` were an alias for
`print`. (`print-error-function.md` is that feature's own file; this case is here because the two
modules are ONE facility and this is its end-to-end proof.)

<!-- test: stdlib-loading.both-print-modules-from-stdlib -->
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
extension method it costs the whole answer: when a `<Conformer>.<method>` is
declared by extensions in more than one file, **nobody keeps the bare spelling** — `Array.contains`
is declared by `stdlib/Array.maxon`'s `where Element is Equatable` extension AND published onto
`Array` by `stdlib/Interfaces.maxon`'s `extension Iterable`, so its members register as
`Array.contains#type parameter` and `Array.contains#struct` and NOTHING is named `Array.contains`.
A scan by bare name misses that real edge, files every stdlib name `unreachable`, and `lowerMaxonToStd`
lowers no body — and `DeadFunctionElimination` then reaches the resolved member from its own root
set and PANICS (`requireUnreachableLibraryStayedDead`, which is the guard doing its job: without it
the program would link and call an EMPTY function).

⭐ **The widening is written ONCE**: `markReachable` and this scan both ask `nameReachesStdlib`, which
widens a callee through `project.overloadSets`, so the one fact has one reader.

<!-- test: stdlib-loading.a-contested-extension-method-is-the-only-edge-into-stdlib -->
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

<!-- test: stdlib-loading.the-other-member-of-the-contested-set-is-an-edge-too -->
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

⭐ **THE TWO CASES ABOVE TEST THEIR RULE ONLY BECAUSE `contains` IS STRUCK FROM
`Parser.arraySurfaceMemberNames`.** With the roster serving `contains`, these two programs would never
cross into stdlib at all and would PASS without touching the rule they exist for. Without the widening,
both panic in `DeadFunctionElimination`, naming `Array.contains#struct` and `Array.contains#type parameter`.

⚠ **`Array.contains` IS THE ONLY CONTESTED `<Conformer>.<method>` THE CORPUS HAS**
(`stdlib/Interfaces.maxon`'s `extension Iterable` and `stdlib/Array.maxon`'s `where Element is Equatable`
extension are the two files; every other method either of them declares is unique to one), and a user file
cannot manufacture a second: a user `extension Array` declaring
`filter(element Element)` beside `Iterable`'s `filter(keep ElementPredicate)` does not resolve by argument
type — `nums.filter(10)` reports `E3005 'if' requires a bool condition, got 'struct'`, i.e. it binds the
stdlib member. That is a separate finding about overload resolution across a contested set, not a vehicle
for these two.

## `stdlib/Range.maxon` — and the two things that make it real

`RangeIterator implements Iterator with RangeBound, BidirectionalIterator`, where
`BidirectionalIterator extends Iterator`, has its inherited `current()` checked under the `RangeBound`
binding, so the module agrees with its own `returns RangeBound` and probes `E3001` and nothing else.

⚠ **A GREEN `E3001` IS EVIDENCE ONLY FOR THE DECLARATIONS THE COMPILER ACTUALLY ANALYZED**, so the
module is checked the two ways this file's siblings demand rather than on the probe alone:

- **The injection control FIRES.** `let bogus = NoSuchType.definitelyUndefined(1)` placed in
  `RangeIterator.current()`'s body answers `E3001` **+ `E3004`** — the control the six `helpers/sort/*`
  files fail, so this module's readiness is not the vacuous kind.
- **The module is BYTE-NEUTRAL**: `function main() returns ExitCode / return 7` compiles to the same
  code bytes with the module loaded as without it.

⭐ **AND THE MODULE IS REACHED THROUGH THE PROTOCOL, NOT MERELY LOADED.** `Range implements Iterable with
(RangeBound, RangeIterator)`, so the case below drives `createIterator()` -> `current()` -> `advance()`
across the witness edge — the inherited requirement's binding doing work in a real program rather than
in a reduction.

<!-- test: stdlib-loading.range-iterates-through-its-iterable-conformance -->
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

⭐ **WHAT LOADING `Range.maxon` BUYS BEYOND THE NAME: `to` IN EXPRESSION POSITION.** `Range.maxon`'s own
header describes it as producing *"first-class iterable values produced by `start to end` … used in
expression position"*, and the module is what makes that spelling mean something — the parser constructs the
declaration this file loads. In a `for … in` header the keyword stays the direct while-loop desugaring it
always was, which is why this case binds the range instead. Pinned here because it is the surface the
module's own documentation advertises, and a load that did not deliver it would be a load of a name alone.

<!-- test: stdlib-loading.range-is-constructible-from-to-in-expression-position -->
```maxon
function main() returns ExitCode
	let r = 3 to 7
	var sum = 0
	for x in r 'walk'
		sum = sum + x
	end 'walk'
	return sum as ExitCode
end 'main'
```
```exitcode
25
```

## `stdlib/Json.maxon` — and the control that proves it is not inert

At 1,080 lines it is the largest pure-corpus module `stdlib/` carries and a whole SUBSYSTEM rather than
a handful of leaf functions: a recursive-descent parser (`JsonParser`), an arena of nodes (`JsonDoc` over
`JsonNodeArray`), and 22 free emitter functions. It probes `E3001` and nothing else.

⚠ **A GREEN `E3001` IS EVIDENCE ONLY FOR THE DECLARATIONS THE COMPILER ACTUALLY ANALYZED**, and for a
module whose name the compiler might SYNTHESIZE it is not evidence at all — a synthesized twin out-votes
the declaration and leaves the module inert. Both are checked rather than assumed:

- **The injection control FIRES.** `let bogus = NoSuchType.definitelyUndefined(1)` placed in
  `findKeyInNode`'s body answers `E3001` **+ `E3004`**, the control the six `helpers/sort/*` files fail.
- **There is no synthesized twin to out-vote the declaration.** `Json` is on neither
  `TypeResolution.isCompilerOwnedTypeName` nor `builtinTypeNameTag`, it is not one of the seven
  `*BuiltinBaseName` roots (`Set`, `Map`, `List`, `Vector` and the three `__Managed*`), and the whole
  compiler mentions the name only in prose. So the stdlib declaration is the only one there is.

⛔ **`Json.maxon` IS BYTE-NEUTRAL ONLY BECAUSE OF `registerProgramLiteralBlobs`' UNREACHABLE-STDLIB
GATE.** A STRING field default mints a nullary helper, and `LowerMaxonToStd.registerProgramLiteralBlobs`
walks it — a pre-elimination door onto `GlobalDataTable.nextStringId` of its own. The two `.rdata`
cases above are written against that gate.

⚠ **A DISPLACED LABEL SHOWS AS DRIFT, NOT AS A FAILING CASE.** `a-stdlib-modules-literals-are-byte-neutral`
shifts by a label and still PASSES — a golden is REFERENCE, not a gate. The case that turns this
invariant red is `a-stdlib-modules-literals-cannot-reach-the-rdata-image`.

⭐ **AND THE TWO CASES BELOW ARE THE DIFFERING-DECLARATIONS CONTROL IN SPEC FORM.** Neither can pass
against an inert entry and neither can pass by merely NAMING the type: each drives the module's own
logic end to end and checks a value only that logic can produce.

<!-- test: stdlib-loading.json-parse-and-read-from-stdlib -->
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
		return (count + (n as JsonInt) + second) as ExitCode
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

<!-- test: stdlib-loading.json-stringify-round-trips-through-stdlib -->
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

<!-- test: stdlib-loading.json-negative-zero-round-trips -->
`-0` is a legal JSON number (`[ minus ] int`) and a distinct double from `0`, so it must survive a
round trip in BOTH directions. The parse half is `numberFromBytes`'s `result = -result`, which
must not compile as `0.0 - result` (that answers `+0.0`); the serialize half is `writeNumber`'s
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
