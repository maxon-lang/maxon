---
feature: subprocess-unsupported
status: selfhosted
keywords: [subprocess, wasm, wasi, diagnostics, target]
category: diagnostics
---

# Subprocess Unsupported on wasm32-wasi

## Documentation

The `Subprocess` API spawns child processes, which requires a host
process-spawn primitive. WASI (wasm32-wasi) provides no such primitive, so
any user program that calls into the `Subprocess` family fails to compile for
that target with **E3074 `semanticSubprocessUnsupportedTarget`**.

The check fires only at user call sites into the public subprocess API
(`Subprocess.run`, `Configuration.run`, `StreamingSubprocess.spawn`, etc.) and
only when the compile target cannot host subprocess. On every native target
the program compiles and runs normally; on wasm32-wasi the call is rejected at
compile time rather than failing at run time with `spawnFailed`.

Guard a subprocess call with `#if not os(Wasi)` (compile it only on non-WASI
targets) to provide a wasm-safe fallback.

## Tests

E3074 is a self-hosted-only diagnostic: the C# bootstrap retired error code
3074 and has no subprocess-reachability check, so this whole spec is marked
`status: selfhosted` to skip it in the C# runner (which would otherwise run the
program on its host x64-windows target, where it compiles fine, and report a
missing-error failure).

Within the self-hosted runner the test only makes sense on wasm32-wasi — on
native targets the same program compiles and runs — so it also carries a
`<!-- targets: wasm32-wasi -->` directive restricting it to that target.

<!-- test: subprocess-unsupported-on-wasm -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 1
	if not result.succeeded() 'check'
		return 2
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3074: Subprocess is not supported on wasm32-wasi (no process-spawn primitive); guard the call with #if not os(Wasi).
```
