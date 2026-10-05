---
feature: harness-gate-a-case-staged-below-an-ignored-directory-compiles-its-maxtest
---
# The a-case-staged-below-an-ignored-directory-compiles-its-maxtest gate

The gate stages this spec below a directory holding `.maxonignore`, and the case's `.maxtest` file must still be compiled.

## Tests

<!-- test: below-ignored.mismatched-end-label -->

```maxon
// --- file: example.maxtest
test 'adds two numbers'
	print("ran")
end 'adds three numbers'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```

```maxoncstderr
error E2008: <fragment>:5:1: Mismatched end label: expected 'adds two numbers', got 'adds three numbers'
```
