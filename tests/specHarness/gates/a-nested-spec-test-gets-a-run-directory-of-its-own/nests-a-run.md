---
feature: harness-gate-a-nested-run-owns-its-working-directory
---
# The nests-a-run gate

A run case that runs `spec-test` itself, over a spec whose one case reports whether it runs in the directory the outer case runs in.

## Tests

<!-- test: nests-a-run.the-inner-run-has-a-directory-of-its-own -->

```maxon
function main() returns ExitCode
	let compiler = try Process.environmentVariable("NESTED_COMPILER") otherwise return 1
	let innerDir = try Process.environmentVariable("NESTED_SPEC_DIR") otherwise return 2
	let compilerPath = try FilePath.from(compiler) otherwise return 3

	let overrides = EnvMap.create()
	overrides.upsert("OUTER_RUN_DIR", value: Directory.currentPath().filename())

	let config = Configuration.create(Executable.path(compilerPath))
	config.arguments = StringArray from ["spec-test", innerDir]
	config.environment = Environment.inheritUpdating(overrides)

	let ran = try config.run() otherwise return 4

	if ran.succeeded() 'separate'
		print("separate\n")
		return 0
	end 'separate'

	print("shared\n{ran.stdout}\n{ran.stderr}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
separate
```
