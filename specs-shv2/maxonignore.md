---
feature: maxonignore
status: stable
keywords: [maxonignore, source-walk, project, directory, build, duplicate-function]
category: organization
---

# `.maxonignore` — a directory that is not part of the program

## Documentation

`maxon build <directory>` compiles every `.maxon` file beneath that directory. A directory holding a
`.maxonignore` file is the exception: it is **not part of the project**, and neither is anything
beneath it, at any depth. The subtree is pruned out of the walk before a single file in it is read.

The marker is a **flag, not a pattern file** — its contents are never read. Some of the markers in
this repository carry a paragraph of prose explaining what they are for; that prose is for the human
reading the tree, and a line in one that looks like a rule (`!keep-me.maxon`) selects nothing.

### What it is for

Some directories hold `.maxon` files that are deliberately not part of the project around them:
grammar fixtures whose bytes are pinned by a snapshot test, data piped at a tool by its test suite,
or a whole second program parked inside a compiler's own tree. Each of those tends to declare its
own `main`, so without a way to say *"not mine"* the enclosing project would not build at all.

### What it does NOT do

**It does not stop a file being compiled when you NAME it.** `maxon build marked/prog.maxon`
compiles that file. The marker excludes a directory from a **walk**; naming a path is the explicit
act it cannot override, and it is how a program parked inside somebody else's tree gets built at all.

**It is not scoped to what is below the directory you build.** A marker sitting on the build root
itself — or above it — excludes everything, and the build reports that it found no sources rather
than quietly compiling a subset.

## Tests

<!-- test: prunes-a-marked-directory -->
The headline case. `fixtures/` declares a second `main`, which in one program with the first is
`E3006`. The marker is what makes them two programs.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	return 42
end 'main'

// --- file: fixtures/.maxonignore

// --- file: fixtures/second-program.maxon
function main() returns ExitCode
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: prunes-every-depth-below-the-marker -->
The marker excludes the whole subtree, not just the directory holding it — a rule that has to be
stated by a case, because a walk that tested each directory for its OWN marker and nothing else
would pass the case above and fail this one.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	return 42
end 'main'

// --- file: fixtures/.maxonignore

// --- file: fixtures/nested/deeper/second-program.maxon
function main() returns ExitCode
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: marker-contents-are-never-read -->
The marker is a flag. Its text here is a plausible-looking negation of exactly the file it excludes,
and it selects nothing: reading the contents at all would make this program stop compiling.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	return 42
end 'main'

// --- file: fixtures/.maxonignore
# every line of this file is inert, including the two below
!second-program.maxon
fixtures/nested/

// --- file: fixtures/second-program.maxon
function main() returns ExitCode
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: prunes-only-the-marked-subtree -->
⚠ THE OVER-PRUNE GUARD, and the case that makes the three above mean something. A walk that simply
stopped at the first marker — or one that excluded every subdirectory — would pass all of them and
fail this: `helpers/` carries no marker, so its `contributed()` is in the program, and the exit code
is proof that it resolved. Under-compiling is the failure mode a passing suite cannot otherwise see.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	return contributed()
end 'main'

// --- file: fixtures/.maxonignore

// --- file: fixtures/second-program.maxon
function main() returns ExitCode
	return 1
end 'main'

// --- file: helpers/value.maxon
export function contributed() returns ExitCode
	return 42
end 'contributed'
```
```exitcode
42
```

<!-- test: error.an-unmarked-directory-is-compiled -->
⚠ THE NEGATIVE CONTROL, and the reason the four cases above are not vacuous. It is
`prunes-a-marked-directory` with the marker deleted and nothing else changed: the second `main` joins
the program and the build is refused. Every green above is attributable to the marker alone.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	return 42
end 'main'

// --- file: fixtures/second-program.maxon
function main() returns ExitCode
	return 1
end 'main'
```
```maxoncstderr
error E3006: fixtures/<fragment>:8:10: Duplicate function 'main'
```
