---
feature: harness-gate-a-named-called-once-program-function-renders-out-of-line
---
# The a-named-called-once-program-function-renders-out-of-line gate

Both cases hold the same program, whose `helper` is called once. The gate plants a wrong host-lane `TargetIr` pin
on the last case and a placeholder pin for each other lane beneath both markers, and `--update-required` mints the
host lane's pin of each. The first case names `helper` in a ```RequiredFunctions block, so its pin must show it as a
function of its own, ahead of `main`, and `main` must still call it; the second names nothing, so `helper` is spliced
into `main` and its pin must show `main` alone, with no call.

## Tests

<!-- test: named-called-once.named -->

```maxon
function helper(n Integer) returns Integer
	return n + 3
end 'helper'

function main() returns ExitCode
	return helper(4)
end 'main'
typealias Integer = int(i64.min to i64.max)
```

```exitcode
7
```

```RequiredFunctions
helper
```

<!-- test: named-called-once.unnamed -->

```maxon
function helper(n Integer) returns Integer
	return n + 3
end 'helper'

function main() returns ExitCode
	return helper(4)
end 'main'
typealias Integer = int(i64.min to i64.max)
```

```exitcode
7
```
