---
feature: harness-refusal-empty-globals-pin
---
# A ```RequiredData block that claims no bytes

`SpecParser.pushSectionPin` refuses an empty block: the compare is a PREFIX, and the empty
prefix matches every image ever linked.

## Tests

<!-- test: empty-globals-pin -->

```maxon
let counter = 42 as Count

function main() returns ExitCode
	print("{counter + 1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```RequiredData
```
