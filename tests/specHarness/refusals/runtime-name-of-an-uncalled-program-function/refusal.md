---
feature: harness-refusal-runtime-name-of-an-uncalled-program-function
---
# A ```RequiredRuntime name of a program function nothing calls

`TargetPrinter.requireEveryNameRendered` refuses it: `spare` is the PROGRAM's own function, but nothing
calls it, so dead-function elimination removes it and the block would pin nothing. The refusal says
the fix: call it, or drop the name.

## Tests

<!-- test: runtime-name-of-an-uncalled-program-function -->

```maxon
public function spare() returns ExitCode
	return 3
end 'spare'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
spare
```
