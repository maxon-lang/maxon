---
feature: harness-refusal-empty-globals-pin
---
# A ```RequiredData block that claims no bytes

`SpecParser.pushSectionPin` refuses an empty block: the compare is a PREFIX, and the empty
prefix matches every image ever linked.

<!-- expect-refusal: carries a ```RequiredData block that pins NOTHING -->

## Tests

<!-- test: empty-globals-pin -->

```maxon
var counter = 42

function main() returns ExitCode
	counter = counter + 1
	return 0
end 'main'
```
```exitcode
0
```
```RequiredData
```
