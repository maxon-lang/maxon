---
feature: harness-gate-a-held-entry-in-a-multi-file-case-source-directory-fails-its-staging-by-name
---
# The heldsource gate

A multi-file run case that would pass. Its test plants, in the directory the case's files are staged into,
a file another process holds without delete sharing for the whole run. The harness empties that directory
before it stages the case, cannot remove the held file once its wait is spent, and must fail the case's
staging naming the file, so the case never compiles or runs against what was left there.

## Tests

<!-- test: heldsource.multi -->

```maxon
// --- file: lib/helper.maxon
export function helper() returns ExitCode
	print("the multi-file case compiled and ran\n")
	return 0
end 'helper'

// --- file: main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```

```exitcode
0
```

```stdout
the multi-file case compiled and ran
```
