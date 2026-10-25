---
feature: harness-gate-a-tier-case-in-a-stdlib-subdirectory-sees-that-directorys-modules
---
# The library-tier-subdirectory gate

A `module` declaration is visible in its declaring directory's subtree, so a case staged at the `stdlib/` root cannot see one declared under `stdlib/helpers/sort/`. `<!-- tier: stdlib/helpers/sort -->` stages the case in that directory instead. The first case declares a `module` function in `helpers/sort/` through an overlay and calls it from the subdirectory tier; the second is the control, the same program at the `stdlib/` root, and is refused; the third names a directory the library does not have and is refused by name.

**Its expected stdout is DELIBERATELY WRONG on the third case, and it must stay wrong**: the gate reads the refusal out of that case's FAIL line.

## Tests

<!-- test: tier-subdirectory.sees-the-directorys-module -->
<!-- tier: stdlib/helpers/sort -->

```maxon
// --- stdlib-overlay: helpers/sort/smallSort.maxon
module function tierProbe() returns ExitCode
	return 7
end 'tierProbe'
// --- file: main.maxon
function main() returns ExitCode
	return tierProbe()
end 'main'
```

```exitcode
7
```

<!-- test: tier-subdirectory.control -->
<!-- tier: stdlib -->

```maxon
// --- stdlib-overlay: helpers/sort/smallSort.maxon
module function tierProbe() returns ExitCode
	return 7
end 'tierProbe'
// --- file: main.maxon
function main() returns ExitCode
	return tierProbe()
end 'main'
```

```maxoncstderr
error E3088: <fragment>:8:9: function 'tierProbe' is module-scoped and not visible from this directory
```

<!-- test: tier-subdirectory.missing-directory -->
<!-- tier: stdlib/helpers/nowhere -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
