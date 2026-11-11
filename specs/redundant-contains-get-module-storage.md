---
feature: redundant-contains-get-module-storage
status: stable
keywords: contains, get, lint, global, module-storage, mutability
category: language
---
# The Redundant `contains`/`get` Lint Over Module Storage

## Documentation

`if-try.md` states the rule: `if x.contains(k)` followed by `try x.get(k)` on the same path is two
lookups where one will do, and it is refused (E3087). The lint suppresses itself at anything in the
then-block that could have invalidated the membership answer — a write to a name in either path, or
another call on the same receiver.

**A top-level `let` key is linted.** A top-level `let` is read-only data, so a key that is a
top-level `let` cannot be rewritten between the two lookups, and the lint reports the pair exactly as
it does for a key that is a local. A test of "is the key a local or a capture" would skip it.

A lint over the lowered IR cannot canonicalize a global load at all. The compiler's runs in the
parser, which holds the tokens AND the declaration — so it can tell a top-level `let` from a name a
call could rewrite, and this is a case where the token-level design is SHARPER than an IR-level one
rather than merely equivalent to it.

## Tests

<!-- test: error.let-global-key-is-linted -->
The receiver is a parameter and the key is a top-level `let`, so neither can be rewritten between the two
lookups.
```maxon
typealias StrMap = Map with (String, String)

let KEY = "a"

function probe(t StrMap) returns ExitCode
	if t.contains(KEY) 'has'
		print(try t.get(KEY) otherwise "?")
	end 'has'
	return 0
end 'probe'

function main() returns ExitCode
	let m = StrMap.create()
	return probe(m)
end 'main'
```
```maxoncstderr
error E3087: <fragment>:7:7: redundant 'Map.contains' followed by 'Map.get' on 't': use 'if let v = try t.get(KEY)' (or 'if var') instead — performs one lookup instead of two
```
