---
feature: refcount-param-field-borrow-forward
status: selfhosted
status-reason: its one case does not compile here (E2003: unknown type `i64`), a type name this compiler does not have (measured 2026-08-06, BATCH29/A3a). shv2 cannot take it either: E2015 for an `Array` member `cursor` outside P1.7's surface.
keywords: [refcount, borrow, param, self, field, iterator, forward, memory, leak]
category: memory-safety
---

# Refcount: Forwarding a Param/Self-Field-Owned Borrow Through a Try-Wrapper

## Documentation

A method that returns an interior view of one of its receiver's fields —
`return cursor.current()`, where `cursor` is a field of the borrowed `self` — hands
back a *borrow*: the field's container owns the occupant, and the container OUTLIVES
the call (the caller passed `self` in and holds it). Such a method is itself
borrow-returning, so its callers borrow the result and never release it.

The gap this test guards is the **forward across a call boundary**. When a wrapper
returns the *result* of a param-field-owned borrow function —

```text
function consumeExpecting() returns Token throws E
	return try consume()   // consume: `return cursor.current()`, a self-field borrow
end
```

— `consumeExpecting` is itself borrow-returning (its callers borrow), yet the
returned-borrow retain that a **torn-down-local** element forward needs
(`CommandLine.optionValue`'s `return parts.get(1)`, whose local `parts` dies at
return) fires here too. That retain is a stranded `+1`: the caller borrows the
param-owned container and never releases it, so every forwarded element leaks at
`rc>0`. On the self-hosted compiler's `Parser` this leaked ~4.1k tokens (plus their
backing) on every hello self-compile — the tokens' owning array outlives the parse,
so the exit-time array teardown reached only `rc1` and every token survived.

The fix classifies such wrappers (`funcReturnsParamBorrow`, a strict subset of the
borrow set whose every return roots in a PARAMETER's interior — a param-rooted load,
or a forward of a borrow call whose receiver roots in a param) and suppresses the
forward's retain, so the whole chain stays a clean borrow. A torn-down-local element
forward keeps its retain (its source really does die at return, and its receiver
roots in a local allocation, not a param), so this is scoped to genuinely-outliving
param/self-field sources.

This test builds a lexer-like type over an `Array` field iterated by an
`ArrayIterator` field, forwards the iterator's `current()` borrow through a
non-inlined `consumeExpecting` try-wrapper called in a loop, and borrows each
result. Under the leak gate the forward must not leak: the program exits with the sum
of the borrowed tokens' fields mod 256 (`903 mod 256 = 135`) — a stranded retain would
leak a token per call and the leak gate would exit 101 instead.

## Tests

<!-- test: forward-param-field-borrow -->
Forwarding a self-field-owned iterator borrow through a try-wrapper must not retain.
```maxon
enum IterErr implements Error
	stop
end 'IterErr'

type Tok
	export var line as Integer

	static function make(l Integer) returns Tok
		return Self{line: l}
	end 'make'
end 'Tok'

typealias TokBuf = Array with Tok
typealias TokCur = ArrayIterator with Tok

type Lex
	var toks as TokBuf
	var cur as TokCur
	var counter as Integer

	static function create() returns Self throws IterErr
		var toks = TokBuf.create()
		for i in 0 upto 200 'fill'
			toks.push(Tok.make(i))
		end 'fill'
		let cur = try toks.cursor() otherwise throw IterErr.stop
		return Self{toks: toks, cur: cur, counter: 0}
	end 'create'

	// Returns cur.current() (a borrow of the self-field-owned token array). The
	// counter arithmetic keeps this over the inline budget so it stays a standalone
	// borrow-returning function, mirroring the large real Parser.consume.
	function consume() returns Tok throws IterErr
		counter = counter + 1
		counter = counter + 2
		counter = counter + 3
		counter = counter + 4
		counter = counter + 5
		counter = counter + 6
		counter = counter + 7
		counter = counter + 8
		counter = counter + 9
		counter = counter + 10
		let t = cur.current()
		try cur.advance() otherwise throw IterErr.stop
		return t
	end 'consume'

	// Mirrors Parser.consumeExpecting: `return try consume()`. Bloated so it stays
	// standalone and the returned-borrow retain would land in its own body.
	function consumeExpecting(tag Integer) returns Tok throws IterErr
		counter = counter + tag
		counter = counter + tag
		counter = counter + tag
		counter = counter + tag
		counter = counter + tag
		counter = counter + tag
		counter = counter + tag
		counter = counter + tag
		return try consume()
	end 'consumeExpecting'
end 'Lex'

function run() returns Integer throws IterErr
	var lex = try Lex.create()
	var sum = 0
	for _ in 0 upto 40 'loop'
		let a = try lex.consumeExpecting(1)
		sum = sum + a.line
	end 'loop'
	let b = try lex.consumeExpecting(2)
	let c = try lex.consumeExpecting(3)
	let d = try lex.consumeExpecting(4)
	sum = sum + b.line + c.line + d.line
	return sum
end 'run'

function main() returns ExitCode
	// The borrowed token fields sum to 903; the exit code checks the value
	// AND, via the leak gate, that no forwarded token leaked (a leak would
	// exit 101 instead of 903 mod 256 = 135).
	let s = try run() otherwise 0
	return s mod 256
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
135
```
