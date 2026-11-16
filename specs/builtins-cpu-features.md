---
feature: builtins-cpu-features
status: stable
keywords: [builtins, __Builtins, cpuHasAes, cpuHasSha, SHA-NI, HWCAP_SHA2, intrinsics, cpu features, AES-NI, PCLMULQDQ, SSSE3, SSE4.1, HWCAP_AES, HWCAP_PMULL, cpuid]
category: system
---

# `__Builtins.cpuHasAes()` and `__Builtins.cpuHasSha()`

## Documentation

`__Builtins.cpuHasAes()` answers whether this CPU can run hardware AES: the AES round instructions and the
carry-less multiply AES-GCM's GHASH is built on. It takes no arguments and answers a `bool`.

| Target | Where the answer comes from |
|---|---|
| x64-windows, x64-linux | `cpuid` leaf 1 at startup: AES-NI (ECX bit 25), PCLMULQDQ (bit 1), SSSE3 (bit 9) and SSE4.1 (bit 19) must all be set, and leaf 0 must report leaf 1 |
| arm64-linux | the auxiliary vector's `AT_HWCAP`: `HWCAP_AES` (bit 3) and `HWCAP_PMULL` (bit 4) must both be set |
| arm64-macos | always `true`: every Apple Silicon CPU has both |
| wasm32-wasi | none: the call is refused with E3104 |

`__Builtins.cpuHasSha()` answers, the same way, whether this CPU can run the hardware SHA-256 compression:

| Target | Where the answer comes from |
|---|---|
| x64-windows, x64-linux | `cpuid` leaf 7: SHA-NI (EBX bit 29), with SSSE3 and SSE4.1 from leaf 1 |
| arm64-linux | `AT_HWCAP`: `HWCAP_SHA2` (bit 6) |
| arm64-macos | always `true` |
| wasm32-wasi | none: the call is refused with E3104 |

The probe is part of the program only when something asks it: a `cpuHasAes()` or `cpuHasSha()` call, or a
hash that chooses its tier at startup (`Hasher`, `KeyedHasher`, `String.hash` and every `hash()` built on them). A program
that never asks carries no `cpuid` code and no word for the answer. The answer is read from a word written once at startup, before `main`, so
every call in a run agrees.

Every `__Builtins` member is library-tier: only a source file under `stdlib/` or `runtime/` may call one, and
a call from anywhere else is E3185. The cases below call the intrinsic themselves, so they compile in the
library tier (`<!-- tier: stdlib -->`). A library file selects its code with `#if arch(x64) or arch(arm64)` and
calls the intrinsic only inside it, so a lane that cannot answer never reaches it.

## Tests

<!-- test: builtins-cpu-features.the-answer-is-a-bool -->
<!-- tier: stdlib -->
The call is a `bool`: it is negated, combined with `and` and compared with `==`, none of which an integer
answer permits.
```maxon
function main() returns ExitCode
	let usable = __Builtins.cpuHasAes()
	let again = not not __Builtins.cpuHasAes()

	if usable == again and not (usable and not again) 'consistent'
		print("a bool\n")
	end 'consistent'

	return 0
end 'main'
```
```stdout
a bool
```
```exitcode
0
```

<!-- test: builtins-cpu-features.the-answer-is-stable-across-calls -->
<!-- tier: stdlib -->
Three calls in one run agree: the answer is read from a word written once before `main`, not probed again.
```maxon
function main() returns ExitCode
	let first = __Builtins.cpuHasAes()
	let second = __Builtins.cpuHasAes()
	let third = __Builtins.cpuHasAes()

	print("{first == second and second == third}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: builtins-cpu-features.a-native-host-has-hardware-aes -->
<!-- tier: stdlib -->
Every x64 machine this suite runs on has AES-NI, PCLMULQDQ, SSSE3 and SSE4.1, and every arm64 one has the AES and PMULL
extensions, so the answer is `true` wherever the intrinsic is answered at all. A probe that stored nothing, or
read the wrong register or bit, answers `false` here.
```maxon
function main() returns ExitCode
	#if arch(x64) or arch(arm64)
		let expected = true
	#else
		let expected = false
	#endif

	print("{__Builtins.cpuHasAes() == expected}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: builtins-cpu-features.a-program-that-asks-twice-from-different-functions-agrees -->
<!-- tier: stdlib -->
The word is shared program state, so a call from a function other than `main` reads the same answer.
```maxon
function ask() returns bool
	return __Builtins.cpuHasAes()
end 'ask'

function main() returns ExitCode
	print("{ask() == __Builtins.cpuHasAes()}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: builtins-cpu-features.error.arity-checked -->
<!-- tier: stdlib -->
`cpuHasAes` takes no arguments. An intrinsic has no signature for the ordinary arity check to read, so it is
refused by the `builtinArity` check its siblings use. Front-end only and target-neutral, so it carries no
marker.
```maxon
function main() returns ExitCode
	return __Builtins.cpuHasAes(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.cpuHasAes' takes exactly 0 arguments, but 1 was given
```

<!-- test: builtins-cpu-features.error.refused-outside-the-library-tiers -->
Outside `stdlib/` and `runtime/` the call is refused at the member it names. It has no public replacement.
```maxon
function main() returns ExitCode
	let usable = __Builtins.cpuHasAes()
	print("{usable}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:26: call to '__Builtins.cpuHasAes' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: builtins-cpu-features.error.rejected-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
WASI has no way to ask the CPU what it can do, so this lane refuses the call at its own span with `E3104`,
naming the runtime entry that has no lowering there. It never answers `false` for a question it cannot ask.
```maxon
function main() returns ExitCode
	let usable = __Builtins.cpuHasAes()
	print("{usable}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:26: this construct lowers to the runtime entry '__cpufeat_aes_usable', which has no wasm32-wasi implementation
```

<!-- test: builtins-cpu-features.error.rejected-on-wasm-when-unreached -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The refusal does not depend on the program reaching the call: `probe` is never called and is still refused,
for the reason `builtins-cpu-parallel.cpu-count-rejected-on-wasm-when-unreached` gives.
```maxon
public function probe() returns bool
	return __Builtins.cpuHasAes()
end 'probe'

function main() returns ExitCode
	return 4
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct lowers to the runtime entry '__cpufeat_aes_usable', which has no wasm32-wasi implementation
```

<!-- test: builtins-cpu-features.sha-answer-is-a-bool-and-stable -->
<!-- tier: stdlib -->
`cpuHasSha` is a `bool` — it is negated, combined with `and` and compared with `==`, none of which an
integer answer permits — and three calls in one run agree, because the answer is read from a word written
once before `main`.
```maxon
function main() returns ExitCode
	let first = __Builtins.cpuHasSha()
	let second = not not __Builtins.cpuHasSha()
	let third = __Builtins.cpuHasSha()

	if first == second and not (first and not second) 'consistent'
		print("a bool\n")
	end 'consistent'

	print("{first == second and second == third}\n")
	return 0
end 'main'
```
```stdout
a bool
true
```
```exitcode
0
```

<!-- test: builtins-cpu-features.error.sha-arity-checked -->
<!-- tier: stdlib -->
`cpuHasSha` takes no arguments, and is refused by the same `builtinArity` check as `cpuHasAes`.
```maxon
function main() returns ExitCode
	return __Builtins.cpuHasSha(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.cpuHasSha' takes exactly 0 arguments, but 1 was given
```

<!-- test: builtins-cpu-features.error.sha-rejected-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
WASI cannot ask the CPU for its SHA instructions either, so this lane refuses the call with `E3104`, naming
the runtime entry that has no lowering there.
```maxon
function main() returns ExitCode
	let usable = __Builtins.cpuHasSha()
	print("{usable}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:26: this construct lowers to the runtime entry '__cpufeat_sha_usable', which has no wasm32-wasi implementation
```
