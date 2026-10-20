---
feature: harness-gate-async-trace-ids
---
# The async-trace ids gate

An `<!-- AsyncTrace -->` case pins which green thread each trace line names, not the id value, because a
spawn draws its id from the processor's batch and the value depends on where the spawning thread ran.
`async-trace-ids.maxtest`, beside this directory, runs `spec-test` over a copy of this file and requires
`async-trace-ids.renumbered` to PASS and `async-trace-ids.wrong-identity` to FAIL with a stderr mismatch.

The first case pins the ids `5` for a run whose real id is `1`. The second pins a different thread on the
`io_yield` line than on the `spawn` line, which no renumbering reconciles, so it is the control that shows
the renumbering has not made the comparison vacuous.

## Tests

<!-- test: async-trace-ids.renumbered -->
<!-- AsyncTrace -->

```maxon
typealias Integer = int(i64.min to i64.max)

function ioTask() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 42
end 'ioTask'

function main() returns ExitCode
	let p = async ioTask()
	let r = await p
	return r
end 'main'
```

```exitcode
42
```

```stderr
spawn #5
io_yield #5 [file_exists]
io_resume #5 [file_exists]
await #5 [yield]
```

<!-- test: async-trace-ids.wrong-identity -->
<!-- AsyncTrace -->

```maxon
typealias Integer = int(i64.min to i64.max)

function ioTask() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 42
end 'ioTask'

function main() returns ExitCode
	let p = async ioTask()
	let r = await p
	return r
end 'main'
```

```exitcode
42
```

```stderr
spawn #5
io_yield #6 [file_exists]
io_resume #5 [file_exists]
await #5 [yield]
```
