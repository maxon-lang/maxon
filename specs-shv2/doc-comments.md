---
feature: doc-comments
status: stable
keywords: doc comment, comment, trivia, layout, lexer, token
category: language
---
# Doc comments

## Documentation

A `///` doc comment is a **token** in shv2's lexer (`TokenKind.docComment`), not whitespace the
lexer throws away. It carries its own prose, its own line and column, and its own source span, so a
formatter can round-trip it and an editor can attach it to the declaration below.

That it is a token is the whole reason this file exists. `//` and `/* … */` produce nothing and can
never change how a program parses; `///` produces something, and every place the grammar counts
tokens, ends a line, or expects a name is therefore a decision somebody has to make. **A doc comment
is LAYOUT everywhere except in a position that demands a name**: the parser steps over it wherever it
steps over a newline, through the one predicate `Lexer.tokenKindIsSkippedLayout`.

```maxon
/// What this function is for.
export function f() returns int
	return 1
end 'f'
```

The cases below pin the consequences. They are not a survey of the feature — each one is a place
where making `///` a token could have changed the meaning of a program that already compiled, and
several of them would have failed **silently**, with no diagnostic, if the layout rule had been
written down twice and the copies disagreed.

## Tests

<!-- test: doc-comments.layout-not-a-statement -->
A doc comment as a body's LAST line is layout, not a statement. If it were read as one it would
become the block's final statement and the `return` below it would stop being the tail.
```maxon
function main() returns ExitCode
	let a = 3
	/// a doc comment as the body's last line
	return a
end 'main'
```
```exitcode
3
```

<!-- test: doc-comments.tail-inference-survives -->
The same rule, in the place where it would have been SILENT. `panic` ends a function, and the
emitter elides the unreachable tail after it. A doc comment written after the `panic` must not
become the last top-level statement — nothing would be diagnosed, the exit edge would simply stop
being elided.

⚠ **`tail` is CALLED with a RUNTIME value, and neither half is incidental.** The first version of
this case never called it, so dead-code elimination dropped the function before it was emitted and
the golden recorded only `@main`. The second passed a literal, so the call inlined and the branch
folded and the panic path vanished again. Both passed while exercising nothing. A case that cannot
reach its own subject reads exactly like one that checked it, and this one took two tries to reach it.

⭐ **The claim is backed by a THREE-WAY CONTROL, not by the exit code.** The emitted IR for `tail` is
BYTE-IDENTICAL whether the line after the `panic` is a `///` doc comment, an ordinary `//` comment,
or nothing at all — measured on all three. That is the actual property: the doc comment costs no
codegen. The exit code alone could not have shown it, because the failure this guards against is
silent — an exit edge the emitter stops eliding, with no diagnostic anywhere. The golden below is
what would move.
```maxon
typealias Count = int(0 to u64.max)

function tail(n Count) returns ExitCode
	if n > 0 'positive'
		return 1
	end 'positive'

	panic("gone")
	/// after a panic, the tail is still elided
end 'tail'

function main() returns ExitCode
	// Runtime-valued, so neither the call nor the branch folds away and the panic path
	// survives into the emitted code where the golden can record it. A spec case runs with
	// no arguments, so this is 1.
	return tail(CommandLine.args().count())
end 'main'
```
```exitcode
1
```

<!-- test: doc-comments.not-an-array-element -->
A doc comment inside `[ … ]` is not an element, so the literal is still EMPTY and still refused. A
counter that treated it as content would report a non-empty literal for a program the real parse
reads as empty.
```maxon
function main() returns ExitCode
	let xs = [
		/// a doc comment is not an element
	]
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/doc-comments/doc-comments.not-an-array-element.test:3:11: Unsupported: an empty array literal `[]` — its element type cannot be inferred; use `Array with T` + `.create()` for an empty typed array
```

<!-- test: doc-comments.not-a-block-body -->
A block holding nothing but a doc comment is still an EMPTY block. `//` behaved this way before
`///` was a token and must go on behaving this way now.
```maxon
function main() returns ExitCode
	if 1 == 1 'ok'
		/// a doc comment is not a statement
	end 'ok'
	return 0
end 'main'
```
```maxoncstderr
error E3082: specs/fragments/doc-comments/doc-comments.not-a-block-body.test:5:2: empty block: 'ok'
```

<!-- test: doc-comments.never-a-name -->
⛔ **The one position where a doc comment is NOT layout, and the only one that can produce a wrong
answer rather than a refusal.** A doc comment carries its prose as its value, so it is not an
identifier, not a literal, and not empty — which is every test `Parser.tokenCanBeAName` applies. With
no arm for it there, `consumeNameToken` accepts it and this program declares a function whose name is
the body of a comment.
```maxon
function /// a doc comment where a name belongs
main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/doc-comments/doc-comments.never-a-name.test:2:10: Expected identifier but got 'a doc comment where a name belongs'
```

<!-- test: doc-comments.between-declarations -->
Doc comments in every declaration position they are actually written in — above a file's first
declaration, above a type, indented above its fields and methods, and above enum cases. This is the
shape `stdlib/` uses 1,728 times, so it is the one whose breakage stops the whole suite.
```maxon
/// A coordinate on the plane.
typealias Coord = int(0 to 1000)

/// A point.
type Point
	/// The x coordinate.
	let x as Coord
	/// The y coordinate.
	let y as Coord

	/// Build one.
	static function at(x Coord, y Coord) returns Point
		return Self{x: x, y: y}
	end 'at'

	/// Manhattan distance from the origin.
	function norm() returns Coord
		return self.x + self.y
	end 'norm'
end 'Point'

/// The kinds of thing.
enum Kind
	/// the first
	alpha
	/// the second
	beta
end 'Kind'

function main() returns ExitCode
	let p = Point.at(3, y: 4)
	return p.norm()
end 'main'
```
```exitcode
7
```
