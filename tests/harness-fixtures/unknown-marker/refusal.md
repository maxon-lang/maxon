---
feature: harness-refusal-unknown-marker
---
# A comment line inside a case that no marker arm reads

`SpecParser.scanTestFromMarker` refuses it. The failure it prevents is silent and fails OPEN: a comment
line the scanner does not recognize would otherwise be walked over, so a misspelt marker unpins the case
and it runs as though the line were not there — under the default target set, processor count or
preemption switch, reporting a verdict about a program the file never asked to run that way.

`unsupported-target` (singular) is the spelling chosen here deliberately: it is one character from the
marker it means, and a case carrying it would run on the very lane the author ruled out.

<!-- expect-refusal: carries a comment line no marker reads -->

## Tests

<!-- test: unknown-marker -->
<!-- unsupported-target: x64-windows -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
