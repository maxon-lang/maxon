---
feature: harness-refusal-runtime-block-on-a-rejected-case
---
# A ```RequiredRuntime block on a case that never links

The same refusal as `section-pin-on-a-rejected-case`, reached through the other block kind: a
compile that fails never runs the printer, so neither the render nor its misspelling refusal
happens.

<!-- expect-refusal: expects a COMPILE ERROR (a ```maxoncstderr block) and also carries a -->

## Tests

<!-- test: runtime-block-on-a-rejected-case -->

```maxon
function main() returns ExitCode
	return nosuchthing
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:9: Undefined variable 'nosuchthing'
```
```RequiredRuntime
mrt_start
```
