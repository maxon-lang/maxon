---
feature: harness-refusal-block-unclosed-to-the-end
---
# A block the file never closes

`SpecParser.readFence` refuses it. Every line after the opening would be read as the block's body, so
anything below it would run nowhere with nothing reported.

## Tests

<!-- test: first-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
