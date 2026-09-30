---
feature: harness-gate-rewrite-drifted-goldens
---
# The rewrite-drifted-goldens gate

`rewrite-drifted-goldens.maxtest`, beside this directory, runs `spec-test` over a copy of it twice: once
to let the passing case MINT its golden, and once with `--rewrite-drifted-goldens` after that golden is
corrupted and a golden is planted for each of the two failing cases. It requires the passing case's golden
to be written back to what was minted, both planted goldens to be left byte for byte, this file to be left
byte for byte, and no drift to be reported.

The first case PASSES and pins emitted code. The second FAILS on its exit code. The third FAILS on a stale
trace-capture block, which `--update-required` would re-splice into this file and this flag must not.

## Tests

<!-- test: rewrite-gate.pinned -->

```maxon
function main() returns ExitCode
	return 3
end 'main'
```

```exitcode
3
```

<!-- test: rewrite-gate.wrong-exit-code -->

```maxon
function main() returns ExitCode
	return 4
end 'main'
```

```exitcode
9
```

<!-- test: rewrite-gate.stale-capture -->

```maxon
function main() returns ExitCode
	let farewell = "bye {2 + 2}"
	return 0 if farewell.count() > 0 else 1
end 'main'
```

```exitcode
0
```

```mm-trace
stale line only a re-splice would replace
```
