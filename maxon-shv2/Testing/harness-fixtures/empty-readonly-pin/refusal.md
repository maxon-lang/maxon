---
feature: harness-refusal-empty-readonly-pin
---
# A ```RequiredRdata block that claims no bytes

The read-only twin of `empty-globals-pin`: one refusal, taken through the OTHER fence, so the
`LinkedSectionKind` argument is covered on both of its values.

⚠ The program carries a STRING LITERAL on purpose, so the linked image HAS a `.rdata` section.
Without one, removing the refusal leaves this case failing anyway — "no `.rdata` section" — and
a fixture that goes red for a reason other than the refusal proves nothing about the refusal.

<!-- expect-refusal: carries a ```RequiredRdata block that pins NOTHING -->

## Tests

<!-- test: empty-readonly-pin -->

```maxon
function main() returns ExitCode
	let payload = "a read-only literal, so the image has an .rdata section"
	if payload.isEmpty() 'noPayload'
		return 1
	end 'noPayload'
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRdata
```
