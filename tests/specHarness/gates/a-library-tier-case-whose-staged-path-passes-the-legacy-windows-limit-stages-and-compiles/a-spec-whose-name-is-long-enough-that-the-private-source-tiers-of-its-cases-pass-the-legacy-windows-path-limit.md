---
feature: harness-gate-a-library-tier-case-whose-staged-path-passes-the-legacy-windows-limit-stages-and-compiles
---
# The long staged path gate

A library-tier case stages a private copy of `stdlib/` below `<spec directory>/.spec-tmp/<spec name>/<case name>.tiers/`, so a long spec name and a long case name put every staged file past the 260 units the legacy Windows path API accepts. The case below is a compile-error case, so no executable is spawned from the long directory: it must stage, compile against its private tier and report its error.

## Tests

<!-- test: a-case-whose-name-is-long-enough-that-its-staged-copy-of-the-standard-library-has-files-past-two-hundred-and-sixty-characters -->
<!-- tier: stdlib -->

```maxon
function main() returns ExitCode
	return missing
end 'main'
```

```maxoncstderr
error E2004: <fragment>:3:9: Undefined variable 'missing'
```
