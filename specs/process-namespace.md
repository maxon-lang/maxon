---
feature: process-namespace
status: stable
keywords: [process, processNamespace, pid namespace, boot session, introspection, __Builtins, intrinsics]
category: system
---

# `__Builtins.processNamespace()` — the pid space the running process's ids are drawn from

## Documentation

| Intrinsic | Meaning |
|---|---|
| `__Builtins.processNamespace()` | a `__ManagedMemory` naming the pid space this process belongs to; empty when the host cannot say |

Two processes may probe each other's ids only when they share a pid space. What names one differs per
host:

- **Linux**: the target of the `/proc/self/ns/pid` link, such as `pid:[4026531836]`. Two containers, or two
  WSL distributions, on one kernel have different ones.
- **macOS**: the boot-session UUID (`kern.bootsessionuuid`), such as `C5F6D0B2-6A3E-4F0C-9D1B-2E7A8B4C1D3F`.
  A Mac has one pid space per boot, and the UUID names both the machine and the boot.
- **Windows**: the machine's `MachineGuid` (`HKLM\SOFTWARE\Microsoft\Cryptography`), such as
  `9b7f1c2e-3a4d-4e5f-8a6b-7c8d9e0f1a2b`. A Windows machine has one pid space, and the GUID names the machine.
- **wasm32-wasi** does not provide it, and a call is refused with `E3104` at its span.

The answer is host-specific, so the cases below assert properties of it rather than a value.

## Tests

<!-- test: process-namespace.is-named -->
The call answers a non-empty name.
```maxon
function main() returns ExitCode
	let name = String.init(__Builtins.processNamespace())

	if name.isEmpty() 'unnamed'
		return 1
	end 'unnamed'

	return 3
end 'main'
```
```exitcode
3
```

<!-- test: process-namespace.names-the-host-specific-identity -->
On Linux the answer is the pid-namespace link target, `pid:[<inode>]`; on macOS and Windows it is a UUID: 36
bytes in five groups joined by `-`.
```maxon
function main() returns ExitCode
	let name = String.init(__Builtins.processNamespace())

	#if os(Linux)
		if name.startsWith("pid:[") and name.endsWith("]") 'aPidNamespaceLink'
			return 3
		end 'aPidNamespaceLink'
	#endif

	#if os(Macos) or os(Windows)
		if name.byteLength() == 36 and name.split("-").count() == 5 'aUuid'
			return 3
		end 'aUuid'
	#endif

	return 1
end 'main'
```
```exitcode
3
```

<!-- test: process-namespace.is-stable-across-calls -->
Two calls in one process answer the same name.
```maxon
function main() returns ExitCode
	let first = String.init(__Builtins.processNamespace())
	let second = String.init(__Builtins.processNamespace())

	if first == second 'same'
		return 3
	end 'same'

	return 1
end 'main'
```
```exitcode
3
```

<!-- test: process-namespace.arity-checked -->
`processNamespace` takes no arguments.
```maxon
function main() returns ExitCode
	let name = String.init(__Builtins.processNamespace(1))
	return name.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:36: '__Builtins.processNamespace' takes exactly 0 argument, but 1 were given
```

<!-- test: process-namespace.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A wasm32-wasi component has no pid space to name, so the call is refused at its source span.
```maxon
function main() returns ExitCode
	let name = String.init(__Builtins.processNamespace())
	return name.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:36: this construct lowers to the runtime entry '__proc_namespace', which has no wasm32-wasi implementation
```
