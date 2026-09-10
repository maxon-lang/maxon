---
feature: shebang
status: stable
keywords: [shebang, script, lexer, hashbang, directive, first-line]
category: language
---

# The shebang line

## Documentation

A Maxon source file whose FIRST TWO BYTES are `#!` has that line ignored by the lexer, so a
single-file program can carry a `#!/usr/bin/env maxon` line and be executed directly by a kernel:

```
#!/usr/bin/env maxon
function main() returns ExitCode
	print("hi\n")
	return 0
end 'main'
```

**Only at byte 0.** `#` opens a compiler directive everywhere else (`#if`, `#else`, `#endif`), and a
`#!` anywhere but the very start of the file is the unknown directive `#` — **E1009** — exactly as it
was before this rule existed. The rule is keyed on the file's first byte rather than on "a line
starting with `#!`" because that is the only position a kernel reads one from, and widening it would
silently swallow a mistyped directive in the middle of a file.

**The line's BYTES are skipped; its NEWLINE is not.** The lexer consumes to, and not past, the
terminator, so the newline is still tokenized and every line below the shebang keeps the number it has
in the file. A diagnostic on line 4 of a file with a shebang says line 4 — which is what makes an
editor's jump-to-error land on the right line in a script.

Every consumer of the lexer inherits this: the compiler, the LSP server, and `maxon fmt` (which
preserves the line verbatim — see `tests/fmt/selftest-cases/ShebangFirstLine.in`).

⛔ **WHAT THESE CASES DO AND DO NOT PROVE.** They prove the LEXER ignores the line, on every target
this suite runs. They CANNOT prove a kernel will exec such a file: this harness compiles a program and
runs the executable, so nothing here ever hands the SOURCE file to `execve`. NOTHING IN THIS TREE
gates that: it needs a real file on disk with the executable bit set and a kernel that reads its first
line, which Windows does not have. What `tests/run/wordless.test.maxon` gates is the DRIVER's half —
that a first argument ending in `.maxon` is `run`, which is the command line such an exec produces.
The two are different facts and neither substitutes for the other — which is also why the control
below matters: no exec test could ever catch the rule widening past byte 0.

⚠ These cases use the `// --- file:` multi-file form for a structural reason: a single-file spec
fragment is written with a generated `// test: <name>` comment as its first line
(`SpecTestRunner.fragmentSource`), so `#!` could never be at byte 0 in one. A multi-file section's
bytes are written VERBATIM (`stageSourceFiles`), which is the only shape in this harness that can put
a shebang where the rule reads it.

## Tests

<!-- test: shebang.first-line-is-ignored -->
The whole of the rule: a file opening with `#!` compiles and runs, and the line contributes nothing
to the program.
```maxon
// --- file: main.maxon
#!/usr/bin/env maxon
function main() returns ExitCode
	print("ran\n")
	return 7
end 'main'
```
```stdout
ran
```
```exitcode
7
```

<!-- test: shebang.line-numbers-are-unshifted -->
⭐ The shebang's NEWLINE survives, so the lines below it keep their numbers. `undefinedCallee()` sits on
line 3 of `main.maxon`, which is line 5 of the merged block a reader sees here and the number the pin
below carries. A lexer that consumed the terminator as well as the line would report one LESS, and
every diagnostic in every script would point a line high.
```maxon
// --- file: main.maxon
#!/usr/bin/env maxon
function main() returns ExitCode
	return undefinedCallee()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:5:9: call to undefined function 'undefinedCallee'
```

<!-- test: shebang.only-on-the-first-line -->
⛔ **A CONTROL, and it is GREEN BOTH BEFORE AND AFTER the rule above.** `#!` on any line but the first
is the unknown directive `#`, unchanged. It is here because the rule is a WIDENING of what the lexer
accepts, and nothing else states where that widening stops: without this case, a rule keyed on "a line
beginning `#!`" rather than on byte 0 would pass every other case in this file.
```maxon
// --- file: main.maxon
function main() returns ExitCode
#!/usr/bin/env maxon
	return 0
end 'main'
```
```maxoncstderr
error E1009: <fragment>:4:1: Unknown compiler directive '#'
```
