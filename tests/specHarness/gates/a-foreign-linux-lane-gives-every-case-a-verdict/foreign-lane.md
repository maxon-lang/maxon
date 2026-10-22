---
feature: harness-gate-a-foreign-linux-lane-gives-every-case-a-verdict
---
# The foreign-linux-lane gate

The gate runs this spec with `--target=x64-linux`. Wherever a runner executes that binary, each case must PASS and no
worker may die; a host with no runner for it reports each case NOTRUN, exits 1 and says the run proved nothing. The
second case parks a green thread on a running child, which a guest without `pidfd_open` awaits through SIGCHLD
delivered on a signalfd.

## Tests

<!-- test: foreign-lane.plain -->

```maxon
function main() returns ExitCode
	return 7
end 'main'
```

```exitcode
7
```

<!-- test: foreign-lane.parks-on-a-child -->

```maxon
function child() returns Integer
	_ = try Subprocess.run(Executable.name("sleep"), arguments: StringArray from ["0.5"]) otherwise panic("the child could not be spawned")
	return 3
end 'child'

function main() returns ExitCode
	let parked = async child()
	let code = await parked
	return code as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```

```exitcode
3
```
