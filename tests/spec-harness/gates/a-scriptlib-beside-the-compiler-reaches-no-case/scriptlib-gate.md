---
feature: harness-gate-a-scriptlib-beside-the-compiler-reaches-no-case
---
# The scriptlib gate

`a-scriptlib-beside-the-compiler-reaches-no-case.maxtest`, beside this directory, runs `spec-test` over a
copy of it with a compiler staged beside a `scriptlib/` whose one package does not parse. The case below is
one file, and it passes only if that package is never loaded.

## Tests

<!-- test: scriptlib-gate.returns-zero -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
