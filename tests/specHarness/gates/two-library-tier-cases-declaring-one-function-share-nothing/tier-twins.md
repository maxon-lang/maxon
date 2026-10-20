---
feature: harness-gate-two-library-tier-cases-declaring-one-function-share-nothing
---
# The two-library-tier-cases gate

Library-tier cases stage their files into one shared directory of the spec's private `stdlib/`. Two of them below declare the same top-level function and the same typealias, and a third declares neither and calls the function: the first two compile only if the directory is emptied between cases, and the third compiles only if the earlier cases' files are gone from the library walk too.

## Tests

<!-- test: tier-twins.first -->
<!-- tier: stdlib -->

```maxon
typealias Integer = int(i64.min to i64.max)

function twin() returns Integer
	return 3
end 'twin'

function main() returns ExitCode
	return twin() as ExitCode
end 'main'
```

```exitcode
3
```

<!-- test: tier-twins.second -->
<!-- tier: stdlib -->

```maxon
typealias Integer = int(i64.min to i64.max)

function twin() returns Integer
	return 5
end 'twin'

function main() returns ExitCode
	return twin() as ExitCode
end 'main'
```

```exitcode
5
```

<!-- test: tier-twins.neither-is-still-there -->
<!-- tier: stdlib -->

```maxon
function main() returns ExitCode
	return twin() as ExitCode
end 'main'
```

```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function 'twin'
```
