---
feature: subprocess-unsupported
status: selfhosted
status-reason: E3074 is a self-hosted-only diagnostic - that, and only that, is what `selfhosted` means here. THE COMPILER TAKES IT AND PASSES IT: measured 2026-09-02, `spec-test --filter=subprocess-unsupported --target=wasm32-wasi` reports 1 passed, 0 failed.
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

E3074 is a self-hosted-only diagnostic, so this whole spec is marked
`status: selfhosted`.

The case makes sense only on wasm32-wasi — on native targets the same program
compiles and runs — so it excludes the four native lanes.

<!-- test: subprocess-unsupported-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
