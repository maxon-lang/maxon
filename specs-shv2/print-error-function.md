---
feature: print-error-function
status: stable
keywords: [printError, stderr, standard error, stdlib, __Builtins, writeStderr, intrinsics]
category: stdlib
---

# `printError` — writing to standard error

## Documentation

`stdlib/PrintError.maxon` declares exactly one free function, and it is `print`'s twin on the
other stream:

```text
export function printError(value String)
	__Builtins.writeStderr(value.addressableBytes())
end 'printError'
```

Like `print`, it takes one `String` and returns nothing. To write anything other than a `String`,
interpolate: `printError("{value}\n")`.

### Why this spec is WRITTEN rather than ported

There is no canonical `/specs/print-error-function.md`. `printError` appears in `/specs` only
incidentally (`source-location-defaults.md`), so there was no file to port byte-identical and this
one is authored — the same route `W5` took for `console-stdin.md` and `process-executable-path.md`.

### The two streams are independent, and that is the property worth pinning

A `print` and a `printError` in the same program must land on *different* streams. A spec that only
checked that the text appeared somewhere would pass just as happily if `printError` were an alias
for `print`, which is exactly the wrong answer this file exists to refuse. So the cases below assert
`stdout` and `stderr` **separately** in one program.

## Tests

<!-- test: basic -->
```maxon
function main() returns ExitCode
	printError("to stderr\n")
	return 0
end 'main'
```
```exitcode
0
```
```stderr
to stderr
```


<!-- test: streams-are-separate -->
```maxon
function main() returns ExitCode
	print("out\n")
	printError("err\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
out
```
```stderr
err
```


<!-- test: interpolation -->
```maxon
function main() returns ExitCode
	let code = 7
	printError("failed with {code}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stderr
failed with 7
```


<!-- test: multiple-calls -->
```maxon
function main() returns ExitCode
	printError("a\n")
	printError("b\n")
	printError("c\n")
	return 0
end 'main'
```
```exitcode
0
```
```stderr
a
b
c
```
