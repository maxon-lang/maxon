---
feature: harness-refusal-procs-marker-beyond-32-bits
---
# A procs marker whose value the scheduler ignores

`SpecParser.parseProcsValue` refuses it. The failure it prevents is silent: the scheduler ignores a
`MAXON_MAX_PROCS` that is not a decimal from 1 to 2147483647 and runs on the machine's count, so a marker
the harness accepted with such a value would run the case on the machine's count while the file claims
another.

`18446744073709551621` is `2^64 + 5`, the value a decimal read that wraps in 64 bits takes as 5.

## Tests

<!-- test: procs-marker-beyond-32-bits -->
<!-- procs: 18446744073709551621 -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
