---
feature: runtime-source-tier
status: stable
keywords: [runtime, raw-intrinsics, compiler-internals, source-tier, reserved-identifier]
category: diagnostics
---

# The Runtime Source Tier

## Documentation

A `runtime/` directory sits at the checkout root beside `stdlib/`, and its `.maxon` files are a
PRIVILEGED SOURCE TIER. They load as ordinary sources — parsed, type-checked and lowered like any
other file — but the rules that hold the reserved space shut for user code are lifted inside that
cone, and two rules that hold nowhere else are imposed on it.

The tier exists so that the runtime the compiler emits into every program becomes INPUT the compiler
READS FROM THE TREE rather than compiler code that BUILDS IR. A runtime written as IR-building code
lives inside the compiler, so changing it needs the two self-compiles that `maxon-bin/CLAUDE.md`
spells out: the first build fixes the emitter, the second gives the compiler its own new runtime. A
runtime written as SOURCE the compiler reads is just another input — the compiler that reads it is
already correct — so a runtime change needs ONE self-compile, and the runtime becomes readable,
formattable and diffable as Maxon instead of as string-valued IR.

The privileges are what a runtime needs and nothing else:

- **It may DECLARE a `__`-prefixed name.** The reserved prefix is the runtime's own name space, so the
  E2051 reservation that stops user code from declaring into it is lifted across the whole cone.
  ⚠ One tier entry wears no prefix and cannot take one, because its name is a FRAME a backtrace prints:
  `maxon_force_segfault` (`specs/safety.md`). It is reserved from user declarations by NAME under the same
  E2051, and that reservation is what keeps a second declaration of it from making the name contested
  across directories and renaming the tier's own symbol.
- **It may CALL a `__Raw.*` intrinsic.** `__Raw` is the closed table of raw machine and OS operations
  — the floor a runtime is written on top of, and the one surface below which there is no Maxon.

⭐ **THE BODIES ARE COMPILED, AND THE CASE THAT SAYS SO IS `builtins-clock.md`'s.** Every test below
stops at the parser: each asserts what the tier ADMITS or REFUSES, and every one would still pass if the
compiler threw the body away immediately afterwards. What proves otherwise is a REAL family — the wall
clock, `runtime/Clock.maxon`'s `__clock_now_unix_s` — reached from a user program through
`__Builtins.currentUnixTimeSeconds()` and pinned there under a ```RequiredRuntime block. No source file
outside the tier may CALL a runtime entry, so a call the compiler emits is the only root a program can
deliberately reach one through, and a family that has one is the only honest way to watch the far end.

⛔ **THE REFUSAL IS OVER THE CALL DOOR AND NOT OVER THE NAME, AND THE VALUE DOOR IS OPEN.** A user file
that mentions a runtime entry in VALUE position — `let f = __parallel_boundary` — passes no reserved-name
check, resolves to the tier's declaration and links; called through that value it reaches the entry by way
of a synthesized `__fnref_` thunk. Nothing below tests it, and no test here should be read as saying the
name itself is out of a program's reach. `maxon-bin/CLAUDE.md` carries the measurement and states what
closing it would cost.

The restrictions are what a runtime cannot have:

- **No managed value may be admitted.** The reference-counting pass emits calls into the very runtime
  this tier DEFINES, so a managed local here is a runtime function that calls itself into existence.
  The rule is asked of the TYPE a runtime file spells — a field, a return, a parameter, a cast target —
  and of the NAME a binding gives a value whose type was never written.
- **Nothing wider than `module`.** `runtime/` is loaded into every program, so a declaration visible
  outside the tier contests names with the programs it is linked into, and the compiler compiling
  itself is one of them. `module` is the widest visibility the tier's own file-to-file sharing needs.

## Tests

<!-- test: runtime-file-may-declare-a-reserved-name -->
**P1 — THE DECLARATION PRIVILEGE, WIDENED FROM TWO NAMED FILES TO A CONE.** Before the tier, exactly
one file could declare into the reserved space (`<stdlibDir>/Builtins.maxon`, by IDENTITY — see
`reserved-double-underscore.md`, which pins both halves of that exemption). The tier is the same
permission keyed on a DIRECTORY instead of on a file name, and this is the positive control on it:
the declaration compiles rather than raising E2051.

`main` deliberately does not call `__probe_answer`. What is under test is the DECLARATION door, and a
call would drag the call door (case two) into the same case; two doors pinned by one case is a case
that cannot say which one moved.
```maxon
// --- runtime-file: Probe.maxon
function __probe_answer() returns ExitCode
	return 7
end '__probe_answer'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-call-a-raw-intrinsic -->
**P2 — THE CALL PRIVILEGE, AND IT IS A SEPARATE DOOR FROM P1.** Declaring a `__` name and CALLING one
are decided at different sites (`Parser.requireUnreservedName` and
`Parser.requireCalleeIsNotReservedName`), which is exactly the asymmetry the older stdlib exemption
shipped with: the declaration door opened first and the call door stayed shut, so the one file that
could declare `__int_fromString` could not then call it.

`__Raw.osTickCountMs` is the intrinsic under the privilege here and it takes no argument, so nothing
about the case turns on argument lowering.
```maxon
// --- runtime-file: Probe.maxon
function probeTicks() returns MachineWord
	return __Raw.osTickCountMs()
end 'probeTicks'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-keep-its-own-frame -->
**⭐⭐ A RUNTIME BODY WHOSE CONTRACT IS THE CALL ITSELF, AND THE ROW THAT SAYS SO.** Every other
`__Raw` row is an operation; `ownFrame` is the one that is an INSTRUCTION TO THE COMPILER, and it earns
that exception by being the only thing a runtime body cannot otherwise state. A tier body flows through
the ordinary pipeline, so `InlineLeaves` splices a small one into each of its call sites and dead-function
elimination then drops it — which is correct for a body that computes an answer and destroys a body whose
whole product is a FRAME: a checkpoint a profile attaches to, a stack-walk entry a backtrace prints.

⚠ It appends no Std op and costs no instruction. What it does is set a fact about the enclosing function
(`IrFunction.keepsItsOwnFrame`), which `InlineLeaves.functionShape` refuses to splice for the reason it
already refuses a green-thread stack guard: the frame is load-bearing.

Under test here is the DECLARATION door alone — that the row exists and a runtime file may spell it. That
the frame then survives is measured where a compiler-emitted call reaches one, in
`builtins-parallel-boundary.md`.
```maxon
// --- runtime-file: Probe.maxon
function __probe_marked()
	__Raw.ownFrame()
end '__probe_marked'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: raw-intrinsic-refused-outside-the-runtime-tier -->
⭐⭐ **THE NEGATIVE CONTROL ON P2, AND IT IS THE HALF THAT MATTERS.** The two cases above prove the
privileges are not EMPTY. Neither proves they are not UNIVERSAL — a compiler that let ANY file call
`__Raw.osTickCountMs` would pass both of them, and would hand every program a direct call to the raw
machine floor with no runtime between.

⚠ **THIS CASE CARRIES NO `// --- runtime-file:` SECTION ON PURPOSE.** It is an ordinary program, so it
is the one case in this file whose red today is its OWN red rather than the harness refusing a marker
it does not yet know: the same spelling answers E3004 now (`__Raw` is simply not a name any file may
write) and must answer E3152 after the tier lands. A case that needed the new marker to be RED could
not tell a missing privilege from a missing harness.

The wording is deliberate and does not say the name is undefined — `__Raw.osTickCountMs` IS defined,
and telling this author "no such function" would send them looking for a typo instead of telling them
the intrinsic exists and their file is not allowed to reach it.
```maxon
function main() returns ExitCode
	return __Raw.osTickCountMs() as ExitCode
end 'main'
```
```maxoncstderr
error E3152: <fragment>:3:15: call to '__Raw.osTickCountMs' from outside the runtime source tier: '__Raw' names a raw runtime intrinsic, callable only from a source file under runtime/
```

<!-- test: unlisted-raw-intrinsic-refused-inside-the-runtime-tier -->
**THE ROSTER IS A CLOSED TABLE, AND THE PRIVILEGE IS TO CALL WHAT IS IN IT — NOT TO WRITE `__Raw`.**
A tier that admitted any `__Raw.<anything>` from a runtime file would turn a misspelled intrinsic into
a link-time failure or, worse, a call to a symbol the emitted runtime happens to have.

⚠ The refusal is the EXISTING machinery, not a second one: `reservedCalleeReasonOf` classifies
`__Raw.nope` as `unknownCompilerIntrinsic` and the ordinary E3004 sentence answers. A second error
code for "an intrinsic of that name does not exist" would be the same rule numbered twice, which is
the disease one level up.
```maxon
// --- runtime-file: Probe.maxon
function probeNope() returns MachineWord
	return __Raw.nope()
end 'probeNope'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:15: call to undefined function '__Raw.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: runtime-file-may-not-name-a-managed-value -->
**R1 — THE RESTRICTION THAT IS NOT A STYLE RULE.** A managed local makes the reference-counting pass
emit `__mm_incref` / `__mm_decref` calls around it — into the very runtime this tier defines. The
result is not a link error but a circularity with no fixed point: the runtime function that manages a
`String` would be lowered with managed-value bookkeeping of its own, calling the function being
lowered.

⚠ The refusal is at the NAME, which is why the case binds a local rather than passing one: it must
fire before any later stage decides whether the reference-counting pass has anything to do, or the
legality of a runtime file would turn on which optimizations happened to elide its retains.
```maxon
// --- runtime-file: Probe.maxon
function probeManaged() returns ExitCode
	let s = "hello"
	return s.count() as ExitCode
end 'probeManaged'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:4:6: 's' has managed type 'String': a runtime source file may not name a managed value, because the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-may-not-declare-public -->
**R2 — THERE IS NO API SURFACE TO EXPORT.** The compiler reaches a runtime entry BY NAME, from its own
emitted code; nothing in user source ever names one, and nothing could — the names carry the reserved
prefix that no ordinary file may write. So `public` on a runtime declaration states a contract with a
caller that cannot exist, and the refusal says which of the two halves is wrong.

⚠ The refusal is a CEILING on visibility rather than a banned word, so its sibling below asks the same
rule of `export`. `module` is where the ceiling sits: the tier's own files still share with each other.
```maxon
// --- runtime-file: Probe.maxon
public function probePublic() returns ExitCode
	return 7
end 'probePublic'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3154: <fragment>:3:1: 'public' is not allowed in a runtime source file: runtime/ is loaded into every program, so a declaration visible outside the tier contests names with the programs it is linked into — the compiler compiling itself among them; 'module' is the widest visibility the tier's own file-to-file sharing needs
```

<!-- test: runtime-file-may-not-export -->
**R2's SECOND HALF, AND THE ONE THAT WAS MEASURED.** `export` mints GLOBAL visibility, so an `export`
declaration in a file the compiler loads into EVERY program it builds is a name offered to every one of
them — including to the compiler compiling ITSELF, where a `runtime/` alias and the compiler's own of
that name make each other ambiguous (E3063) and the self-compile stops.

⚠ `module` and `file` stay legal and must: `runtime/Word.maxon` declares its word roster `module`, and
the tier resolves those names across its own files. The rule is a CEILING, which is why one code and one
sentence answer for both halves.
```maxon
// --- runtime-file: Probe.maxon
export function probeExported() returns ExitCode
	return 7
end 'probeExported'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3154: <fragment>:3:1: 'export' is not allowed in a runtime source file: runtime/ is loaded into every program, so a declaration visible outside the tier contests names with the programs it is linked into — the compiler compiling itself among them; 'module' is the widest visibility the tier's own file-to-file sharing needs
```

<!-- test: runtime-file-may-not-declare-a-managed-field -->
**R1 ASKED OF A TYPE NOBODY NAMES.** A `String` FIELD binds no local and names no parameter, so the
value-level refusal above never sees it — and a runtime `type` holding one is exactly the circularity
that rule exists to forbid, deferred to whatever function first constructs the box.

⚠ The anchor is the field's TYPE and not its name, because the type is what the author has to change.
The refusal is asked at the one door every type a file spells comes through (`Parser.parseTypeReference`),
so a field, a return, a parameter and a cast target are one rule rather than four.
```maxon
// --- runtime-file: Probe.maxon
type ProbeBox
	var label as String
end 'ProbeBox'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:4:15: managed type 'String' is not allowed in a runtime source file: the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-unreserved-declaration-is-still-module-scoped -->
⭐⭐ **THE NEGATIVE CONTROL ON THE ONE RULE THAT LETS A COMPILER-EMITTED CALL REACH THE TIER.** A runtime
entry point answers to no directory's module scope: `stdlib/Clock.maxon`'s `nowUnixSeconds` becomes a call
to `runtime/Clock.maxon`'s `__clock_now_unix_s`, from a different directory, and visibility has nothing to
refuse there because no file could have NAMED it — the callee is RESERVED, which is what makes the name
unwritable (`SemanticCheck.calleeVisibleFrom`).

⛔ **BEING RESERVED IS HALF THAT TEST, AND THIS IS THE HALF THAT MEASURES IT.** Keyed on the tier alone, the
exemption would make every declaration in `runtime/` callable from every file in the program — the tier is
loaded into all of them — so an UNRESERVED runtime declaration would become a global name in everything the
compiler builds, which is the contest E3154's ceiling exists to prevent. It stays module-scoped, and the
module is `runtime/`.

⚠ **RESERVED IS NOT THE SAME AS `__`-PREFIXED, AND ONE TIER ENTRY IS THE DIFFERENCE.**
`maxon_force_segfault` wears no prefix and is admitted by that same arm, because the DECLARATION door
reserves the word in the free-function name space (`specs/safety.md`). The rule the case below measures is
therefore *"a runtime declaration the compiler has not reserved stays module-scoped"*, and `probeHelper` is
one of those.
```maxon
// --- runtime-file: Probe.maxon
module function probeHelper() returns ExitCode
	return 7
end 'probeHelper'
// --- file: main.maxon
function main() returns ExitCode
	return probeHelper()
end 'main'
```
```maxoncstderr
error E3088: <fragment>:8:9: function 'probeHelper' is module-scoped and not visible from this directory
```
