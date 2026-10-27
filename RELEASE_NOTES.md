# Release notes

What changed in each release of the Maxon compiler and standard library, newest first.

Maxon breaks compatibility whenever a change makes the language better ([why](https://maxon.dev/docs/getting-started/introduction/)).
The first build of a project with a new release lists the breaking changes since the release that last
built it, and the MCP `breaking_changes` tool answers the same.

A change that can break an existing project, script or workflow is listed under `### Breaking changes`,
one `- ` bullet each: the change, then an indented `Fix:` line saying how to put the project right.
`## Unreleased`, when present, holds the breaking changes of the release being prepared and takes the
release's version as its heading when the release is cut.

## Unreleased

### Breaking changes

- `Vector` is renamed `FixedArray`, and its iterator typealias `Vector.VectorIter` is
  `FixedArray.FixedArrayIter`; `Vector with 3 Int` is an unknown type (E2010).
  Fix: rename `Vector` to `FixedArray` and `VectorIter` to `FixedArrayIter`.
- A `FixedArray` `get` or `set` whose index is proven inside the size — a constant, a value whose ranged
  type lies inside, or a counted-loop counter between compile-time bounds — cannot throw, so a `try` on it
  is E3055.
  Fix: drop the `try … otherwise` and write the call bare.
- A constant `FixedArray` index at or past the size is E3005 at compile time.
  Fix: correct the index, or pass a deliberate out-of-range index through a value computed at run time.
- A divide or `mod` whose divisor is proven non-zero by a counted loop's bounds or a range computed from
  them (`100 / i` in `for i in 1 upto 10`) cannot throw, so a `try` on it is E3055.
  Fix: drop the `try … otherwise` and write the divide bare.
- `URL.host()` answers an IP literal without its brackets (`::1` for `http://[::1]/`), where it answered
  `[::1]`; the new `URL.authorityHost()` answers the bracketed spelling. An `HttpRequest`'s URL read in a
  handler follows the same rule.
  Fix: call `authorityHost()` where the brackets are wanted, such as building an authority or a `Host`
  header by hand; code that stripped the brackets itself can drop that step.
- `DerError` has two more cases, `invalidBoolean` and `invalidString`, so a `match` over it that lists
  every case is no longer exhaustive.
  Fix: add arms for `invalidBoolean` and `invalidString`.
- An interface requirement returning one typealias of a generic instance is not met by a method returning
  another typealias of that instance (E3016), as a parameter already was not.
  Fix: declare the method's return with the requirement's own typealias.
- A declared type named like a generic instance (`type Array_Byte`) and the instance itself (`ByteArray`)
  no longer meet each other's interface requirements (E3016).
  Fix: declare the method with the type the requirement names, or rename the declared type.
- An argument passed through an interface value or a `where` constraint is checked against the
  requirement's declared type as a direct call's is: a value of another generic instance, of another
  typealias of the same instance, or a non-`Character`/`CString` value for a `Character` or `CString`
  parameter is E3005.
  Fix: pass a value of the requirement's type, re-branding an instance with `as` (`xs as IntArray`).
- The result of a call through an interface keeps the requirement's typealias, so passing it where another
  typealias of the same instance is expected is E3005.
  Fix: re-brand the result with `as`, or declare the destination with the requirement's typealias.
- A call's result wears the typealias its callee returns as the callee's declaring file resolves it, a
  directory-qualified `returns alpha.Numbers` included, so passing it where another declaration of that
  alias name is expected is E3005.
  Fix: convert the result with `as`, or declare the destination with the typealias the callee returns.
- A `match` payload binding wears the typealias its payload was declared with, as the union's declaring
  file resolves it, so passing it where another typealias of the same instance is expected is E3005.
  Fix: convert the binding with `as`, or declare the parameter with the payload's typealias.
- A tuple holding a value read out of a `let` field, a `let` payload or a read-only element is read-only
  wherever it goes, a tuple a call returns included. Binding it, or one of its read-only elements, to a `var`, assigning
  it to existing `var` names, storing it into a `var` field or an array, or passing it to a function that
  writes it is E3078 (E3159 for the call); each of these compiled before.
  Fix: bind it with `let`, or take a `.clone()` of the elements.

- Passing a `let` to a function whose result holds a closure capturing that parameter — in a container,
  a record or bare — is E3078, also when the result is bound by a `let` and the closure only reads.
  Fix: pass a `clone()`.
- An element of another enum stored into an array by `push`, `set`, `insert` or `fill`, or written in an
  array literal, is refused (E3005).
  Fix: store a value of the element's own enum.
- An array-literal element of another brand is refused (E3005), as `push` refuses one: a `B` value in a
  `[...]` written at an `Array with A` slot.
  Fix: re-brand the element with `as` (`[b as A]`).
- An array-literal element of another type is E3005, where it was E2015.
  Fix: expect E3005 wherever a script or test matches the code.
- A member of your own type named `pop`, `remove`, `removeFirst` or `removeLast` that hands back a promise is
  judged by its body, and one that returns a borrow (`self.items.last()`) is E3141; so is a generic static
  that hands back a promise one of its arguments still holds.
  Fix: return what a library `pop`/`remove` takes out of the field, or have the declaration the call binds
  take that argument.
- A static called on a bare generic name whose result is written over a type parameter no argument fixes
  (`Holder.make()` declared `returns T`) is E3162.
  Fix: call it through a typealias that names the instance (`typealias IntHolder = Holder with Integer`).
- `Type.method` written without its call — a static or instance method, a static the compiler provides
  (`int.fromString`), or a requirement through a type parameter — is E2078, where it was E2010, E2004 or,
  at module scope, E2015.
  Fix: pass a closure that calls it, `function(n) gives Ops.twice(n)`.
- A static a primitive (`int.nope()`, `byte.nope()`), a compiler-owned type (`String`, `__ManagedFile`) or a
  builtin container alias does not have is E2073, reported at the member, where it was E2015, or E3004 for
  `byte`.
  Fix: call a static the type provides; expect E2073 wherever a script or test matches the code.
- A member read through a ranged, function or tuple alias, an interface, a type parameter or a directory
  that does not declare it is E2073, where it was E2010 (`Expected 'min or max'`).
  Fix: read the member through the type that declares it; only a sized type has `.min` and `.max`.
- A `static` requirement called through a type parameter (`T.make()`) is E3116, where it was E3004.
  Fix: call the static on a concrete type that implements the interface.
- Assigning to a method (`Ops.twice = f`) is E2013, where it was E2004.
  Fix: store the function value in a `var` or a `static var`.
- A primitive's static called in a module-level initializer (`let n = int.fromString("5")`) is E2015, where
  it was E2004.
  Fix: compute the value in a function, or write it as a literal.
- A function whose shape differs from the declared function type, stored into a field (`h.op = f`,
  `self.op = f`, a struct literal), a tuple element (`t.0 = f`) or an array element (`push`, `set`, a typed
  array literal), is E3005.
  Fix: store a function whose parameters and result match the declared function type, or change the
  declared type.
- A `?`, `;`, `|`, `&`, `$`, `%`, `~`, `^`, backtick, control character or non-ASCII character outside a
  string literal or a comment is E1010 (`Unexpected character`); the lexer skipped it before.
  Fix: delete the character, or move it into a string literal or a comment.
- A `spawn` of an overloaded factory and an `async` call of an overloaded function bind the overload their
  arguments fit, as any call does: two overloads that fit equally are E3007, and the bound overload alone
  decides which arguments move (a promise included) and which type arguments a generic factory fixes.
  Fix: give the arguments types, or labels, that fit exactly one overload.
- Beside a generic function of the same name, a non-generic overload binds a literal, a negated literal, a
  loop counter or a value computed from them that lies inside its parameter's alias, and the generic binds
  one known to leave it.
  Fix: cast the argument to the type the generic should take (`describe(5 as Other)`).
- A literal cast to the alias of the non-generic overload it already binds (`describe(5 as Score)` beside a
  generic `describe`) is E3010.
  Fix: drop the cast.

- A fresh tuple or record that holds a record a `var` of the function, or a module `var`, may still write
  is E3078 when it is stored into a `let` field, a `let` payload or a `readonly` element, or passed to a
  parameter that stores it there (`Frozen.create((Box.create(1), id(p)))` with `p` a `var` written later);
  it compiled before, with the `let` field's contents writable through `p`. A `let` bound to a fresh tuple
  or record holding a module `var`'s record that is written later is E3188 (`let t = (Box.create(1), gBox)`).
  Fix: hold a `.clone()` of the record, or a record of its own.
- A function value fits a function type only by variance: a read-only parameter may be wider than the
  type's, a parameter the program writes by reference must be the same type, and a result may be narrower.
  A function whose read-only parameter is narrower, whose written parameter differs, or whose result is
  wider than the function type it is stored or passed as — a ranged alias, or a bare `int` in the type's
  declaration, against a function over a narrower alias — is E3005, or E3009 as an `as` cast.
  Fix: declare the function with the function type's parameter and result types.
- A closure literal bound by `let` or `var`, or written where no function type is declared, has an unranged
  result, so it no longer fits a function type declaring a ranged result (E3005); a closure written at a
  parameter, returned value, struct field, field write, variable write or union payload of a function type
  takes that type's parameter and result types, as it did at a parameter.
  Fix: write the closure where the function type is declared, or end its body in a cast to the result alias
  (`function(n Integer) gives (n + 1) as Byte`).
- A call through a function value is checked against the function type the value is held at, not against
  the function behind it: after `let f = one as Bin`, `f(3)` against a two-parameter `Bin` is E3036 beside
  the cast's E3009, where only the E3009 was reported.
  Fix: call the value with the arguments of the type it is held at.
- `HttpError` and `HttpServerError` are unions, each with a new case `tlsFailed(error TlsError)`, so a
  `match` over either that lists every case is no longer exhaustive, and `==` or `!=` on one is E3066.
  Fix: add an arm for `tlsFailed`, and replace a comparison such as `e == HttpError.timedOut` with a `match`
  on `e` that names the case.
- `HttpClient` fetches an `https` URL over TLS, on port 443 unless the URL names one, where it threw
  `HttpError.unsupportedScheme`; the error is now thrown only for a scheme that is neither `http` nor
  `https`.
  Fix: a program that relied on the refusal checks the scheme itself before the call; one that now reaches
  a server over TLS handles `HttpError.tlsFailed`, and passes `tls:` to `HttpClient.send` to trust its own
  roots.
- Two `extension` bodies in a program that declare the same nested `typealias` for one type are E3061 at
  the later declaration, in one file or two, over one instance or two, and for every conformer of an
  interface extension; the compiler kept whichever declaration it reached last and gave the other
  extension's own signatures that instance without a word.
  Fix: rename one of the two members, or declare it in one extension and name it from the other.
- A `type` or `interface` body that declares the same nested `typealias` twice is E3061 at the second
  declaration, for the generic-instance, ranged, function and tuple forms; the compiler kept the last
  declaration and gave the first one's own signatures that meaning without a word.
  Fix: rename one of the two members, or delete the declaration that is not meant.
- An `extension` of a type that declares a nested `typealias` the type's own body declares is E3061 at the
  extension's declaration, in any file and for a standard-library type's members too, as an extension
  method of the type's own method's name is E3176. An interface extension's nested `typealias` yields to a
  conforming type's own declaration of the same name, and the extension's methods compiled for that
  type read the type's; they read the extension's alias while callers saw the type's.
  Fix: rename the extension's member, or delete it and use the type's own.
- A nested `typealias` that a program's `extension` declares under the name of one a library `extension` of
  the same type declares (`ElementArray`, `SortComparator` or `NodePower` on `Array`, `ElementTransform` on
  `Iterable`) is the program's own member: the program's extension bodies and the callers of its methods read
  the program's declaration, where an extension body that wrote the name read the library's and the library's
  bodies could read the program's. A second file's extension naming a member the first file leaves without a
  visibility modifier is E3008.
  Fix: write the extension bodies for the member the program declares, or rename it to keep the library's.
- `TlsListener.accept()` returns once the TCP connection is accepted, and the stream runs its handshake on its
  first `send`, `recv` or `shutdownWrite`, or on the new `handshake()`, as Go's `Conn.Handshake` does. A failed
  handshake is thrown from that call, not from `accept()`, and the options' handshake deadline bounds the
  handshake from that call. A read or write deadline set on the stream before then applies once the handshake
  completes.
  Fix: handle handshake errors where the stream is first used, or call `handshake()` right after `accept()`.
- `TlsStream.protocol()`, `cipherSuite()`, `applicationProtocol()` and `peerCertificates()` throw `TlsError`,
  `handshakePending` until the handshake has completed, and `TlsError` has that case, so a `match` over it that
  lists every case is no longer exhaustive. `HttpTransport.security()` throws `TlsError` the same way.
  Fix: call the four with `try`, after `handshake()` on a stream from `accept()`; add an arm for
  `handshakePending`.
- `HttpServer.serve` moves each connection, a `TcpClient` or a `TlsStream`, to a green thread of its own, so a
  client that stalls and a handler that computes delay only their own connection, and it returns once every
  connection it took has finished. The handler is one value every connection reads at once, so it is
  read-only: a handler whose methods write its fields is E3138 where `serve` sends it to a connection's
  thread, in `stdlib/HttpServer.maxon`, and a handler that holds an OS handle, a function value or a promise,
  or that the calling function also shares with a coroutine, stops the program with exit code 96. What the
  handler wrote in place is no longer shared between requests or visible to the caller.
  Fix: keep what connections share, or what the caller reads after `serve`, in a service the handler holds a
  handle to (`CountingHandler.create(visits.clone())`), declare the handler's fields `let`, and build the
  handler where `serve` is called.
- A cast whose operand is itself a cast, `x as A as B` or `(x as A) as B`, is E2079.
  Fix: cast directly to the final type (`x as B`), or, when both steps do work (a range check, then a
  reinterpretation as `bits(n)`), bind the first: `let a = x as A`, then `a as B`.
- A file-scope `let` or `static` initializer whose operator combines two different typealiases with no
  common ancestor (`let t = a + b` over a `Small` and a `Tally`, a comparison, `and`, `or`, `xor` or `mod`)
  is E3005, as it is in a function body.
  Fix: cast one side with `as` (`a + (b as Small)`).
- A file-scope constant derived from aliased operands carries their typealias, as the same expression does
  in a function body: with `let a = 5 as Small`, `let s = a + a` is a `Small`, so passing `s` where a
  `Tally` is required is E3005 and casting it to `Small` again is E3010.
  Fix: cast with `as` where the other typealias is required (`f(s as Tally)`), and drop a cast to the
  typealias the constant already carries.
- A file-scope `let` initialized by a cast literal (`let Base = 2 as Wide`) whose readers include another
  file-scope initializer that takes the plain constant (`let S = Base as Small`) is E3010, as a read in a
  function body is.
  Fix: write the bare literal (`let Base = 2`).
- A bare call to an instance method of the enclosing type from a `static` method, a file-scope initializer
  or a `static var` initializer is E3193, with or without arguments: a first argument there is never the
  receiver (`bump(m)` with `m` a `Maker` no longer fills `self`).
  Fix: call it on a value (`m.bump()`) or pass the receiver explicitly (`Maker.bump(m)`).

## 0.5.0 — 2026-10-08

### Breaking changes

- A typealias's identity is its declaration rather than its spelling: two declarations of one name are two
  types even over the same range, and a value crossing between them is refused.
  Fix: declare the alias once and share it, or cast the value across (`x as Name`).
- A bare type name that reaches more than one visible declaration is E3063 in every file, including a
  file that declares one of them.
  Fix: qualify the name as the message lists (`dir.Name`, `export.Name`, `stdlib.Name`), or rename one
  declaration.
- A typealias declared inside a type is private to its file unless it carries its own visibility modifier,
  and naming a file-private one from another file, bare or qualified, is E3008.
  Fix: give the alias a visibility modifier (`module`, `export` or `public`).
- E3167 checks every type a signature names, an interface's requirements and the types a named alias
  reaches included, so a signature that was accepted can be refused.
  Fix: raise the type to the function's visibility, or narrow the function to the type's.
- Discarding the result of a pure function (E3064) is refused in more programs: a function that writes only
  into values it created, one that calls a closure it can name, a search through a container's
  `withIterator()`, and a `Map` or `Set` lookup.
  Fix: use the result, or remove the call.
- A program that declares `BuiltinStringLiteral`, `BuiltinCharLiteral` or `BuiltinArrayLiteral` is
  refused (E2015).
  Fix: rename the declaration.
- Only a `.maxproj` marks a project for the language server; a file with no `.maxproj` above it is checked
  alone against the standard library, and nothing else of its directory is loaded for it.
  Fix: add a `<name>.maxproj` file at the project root (`maxon init` writes one).
- `Utf8ByteCount`, `CodepointCount` and `Utf16UnitCount` are removed.
  Fix: use `Count`.
- `==` on two values of a generic type calls the type's `equals`, and is E3005 when the type declares none.
  Fix: declare `equals` on the type.
- Writing through a module `let`'s record handed back by a function, and copying a `let`'s field into a
  `var`, are refused (E3159, E3078).
  Fix: bind the value with `var`, or write through a `clone()`.
- A copy that reaches a promise (a `List.clone`, or a generic type copying an array of promises) is E3141,
  and a borrowed argument (a parameter, a field, an element) passed as a kept argument to a `spawn` factory
  is E3138.
  Fix: `await` the promise and copy the value it holds; pass a `spawn` factory a fresh value or a `clone()`.
- A generic type nested in itself through its own instances is E3184.
  Fix: declare the field over the type's own parameters (`Nest with T`), or remove the recursion.

### Added

- `maxon build` skips the compile when nothing it read has changed since it last built that output,
  printing `Up to date -> <path>`; a build that does compile logs why. `--rebuild`, and the MCP `build`
  tool's `rebuild`, compile regardless. Two builds of one output take turns.
- The standard library's front-end work is cached on disk and shared between compiles, so a small
  program builds in about half the time. `maxon cache` reports the library cache and `maxon cache clear`
  removes it.
- `maxon test` takes a single `.maxtest` file and runs it alone, as does the MCP `test` tool.
- `Count`, the standard library's type for a number of things: Array, List and Vector `count()`,
  `Json.arrayLength`, `Character.byteLength()` and the string views' counts return it, and
  `Scheduler.processorCount()` compares with any of them with no cast.
- `JsonDoc.hasChild`, which says whether an object holds a key.
- `clone()` on a generic instance — a `Map`, a `Set` or a generic type of your own.
- The language server names itself and its version in its `initialize` reply.

### Changed

- A typealias's identity is its declaration rather than its spelling. Two declarations of one name are
  two types, even over the same range, and a value crossing between them needs a cast.
- A bare type name that reaches more than one visible declaration is E3063 in every file, including a
  file that declares one of them.
- A typealias declared inside a type carries its own visibility modifier, and unmarked it is private to
  its file; one inside an interface takes the interface's. Naming a file-private one from another file,
  bare or qualified, is E3008.
- E3167 checks every type a signature names, including an interface's requirements and the types a
  named alias reaches.
- Discarding a pure function's result (E3064) is caught in more programs: a function that writes only
  into values it created, one that calls a closure it can name, a search through a container's
  `withIterator()`, and a `Map` or `Set` lookup. A discarded call is judged by the overload it reached.
- Diagnostics name types and overloads as source writes them: `Array with int`, `stdlib.Name`,
  `dir.Name`, `pick(String)`.
- A program may not declare `BuiltinStringLiteral`, `BuiltinCharLiteral` or `BuiltinArrayLiteral`
  (E2015).
- Only a `.maxproj` marks a project for the language server. A file with no `.maxproj` above it, up to
  its workspace folder, is checked alone against the standard library, and nothing is loaded for it.
  Opening a `.maxproj` loads its project.
- `maxon test`'s default per-file deadline is two minutes.
- The compiler builds itself about a quarter faster, with about 30% less peak memory, and a
  function with many early exits no longer grows quadratically in code size.

### Fixed

- `Array.sort` could return unsorted output on arrays with many equal elements, and the stable sorts
  could reorder equal elements.
- Array `equals`, `hash` and `==` compared raw bytes, which is wrong for a `String` or any other managed
  element, and could disagree with each other and with `Map` and `Set` keys. They compare element by
  element through the element's own `equals` and `hash`.
- An array of floats compared, searched and hashed the wrong values on x64 and arm64, and trapped on
  `wasm32-wasi`.
- `==` on two values of a generic type compared their addresses. It calls the type's `equals`, and is
  E3005 when the type declares none.
- An array of a declared `enum Byte` with more than 256 cases truncated its values.
- On Linux, a spawned child inherited every file the program had open, so a child reading a pipe the
  program was writing could wait forever for its end.
- A program declaring its own `Comparable`, `Equatable`, `Hashable` or `Error` interface lost the
  library's `sort` and `contains` on built-in types, and its services' reply errors stopped conforming
  to `Error`; the program's own interface could not be implemented.
- A ternary choosing between a parameter typed by a function-type alias and a declared function
  crashed the compiler.
- Two conformers binding an associated type differently crashed the compiler, and an associated type
  bound by a type's second `implements` clause, or by an `extension`'s, was not found.
- A generic type nested in itself through its own instances made the compiler allocate without bound;
  it is E3184.
- Writing through a module `let`'s record handed back by a function, or copying a `let`'s field into a
  `var`, compiled and faulted at run time; they are E3159, E3019 and E3078.
- A copy that reached a promise — a `List.clone`, or a generic type copying an array of promises —
  compiled, then crashed or left a green thread un-awaited. Every copy that reaches a promise is E3141.
- A variable passed as a kept argument to a `spawn` factory aborted the program; it is moved, and a
  borrowed argument — a parameter, a field, an element — is E3138.
- A static factory's result, a static call through an instance alias, and an array literal passed at a
  type-parameter slot were refused or mistyped.
- A service reply that is a freshly interpolated `String` was refused with E3137.
- The language server kept declarations a file no longer had, reporting a false cycle after a union was
  replaced by a type of the same name.
- A `--emit-ir-runtime=` name the program lacks crashed the compiler instead of being refused.

### Removed

- `Utf8ByteCount`, `CodepointCount` and `Utf16UnitCount`, replaced by `Count`.

## 0.4.0 — 2026-10-04

### Breaking changes

- A project is marked by `<name>.maxproj`, which replaces `build.maxon`; tasks live in `<name>.maxtasks`,
  and test files are `<name>.maxtest`, no longer `*.test.maxon`. `build.maxon` and `*.test.maxon` are no
  longer read.
  Fix: rename `build.maxon` to `<name>.maxproj` (its targets are the exported functions of no parameters
  returning `ExitCode`, run with `maxon build <target>`), move task functions into `<name>.maxtasks`, and
  rename each `*.test.maxon` to `*.maxtest`.
- `maxon run <file>` is `maxon execute <file>`; `maxon run <task>` now runs a task from the `.maxtasks`
  file.
  Fix: replace `maxon run <file>` with `maxon execute <file>` in scripts and CI. `maxon <file>.maxon` and
  `#!/usr/bin/env maxon` are unchanged.
- Every command-line option is long-form, and the `-o`, `-t`, `--compiler`, `--version`, `-V`, `--help`
  and `-h` spellings are removed. An option given to a command that does not take it is refused, and so is
  a bad `--log=` value.
  Fix: use `--output=` for `-o` and `--filter=` for `-t`, `maxon version` for `--version`, and
  `maxon help [<command>]` for `--help`, and drop `--compiler=` from `spec-test` command lines (it
  compiles every case in its own process). `maxon help <command>` lists the options a command takes.
- `maxon test --filter=` may be repeated, and a pattern that matches no test ends the run.
  Fix: remove or correct the pattern that matches nothing.
- `Runtime.yield()` and `Runtime.processorCount()` are `Scheduler.yield()` and `Scheduler.processorCount()`.
  Fix: rename the calls.
- The trace-key recorder that was `Log` is `TraceCapture`, and `Log` is now a structured logger.
  Fix: call `TraceCapture.trace`, `startCapture`, `stopCapture` and `fired` where the program called the
  same functions on `Log`.
- Arguments to an `async` call or a service message, and the managed locals a closure captures, are moved;
  reading one afterwards is a compile error (E3102). The `let`-argument freeze and E3160 are gone.
  Fix: send or capture a `clone()` when the program still needs the value, or stop reading it after the
  send.
- A bare type name with more than one visible declaration is an error (E3063) rather than a silent pick.
  Fix: qualify it with its directory, `stdlib.` or `export.`, or rename one declaration.
- A hidden type is hidden at every place a type can be named, along with its fields and methods (E3008).
  Fix: export the declaration, or reach it through an exported function or interface.
- A function's parameter, return and `throws` types must be at least as visible as the function (E3167).
  Fix: raise the type to the function's visibility, or narrow the function to the type's.
- A function that declares `throws` must be able to throw (E3168).
  Fix: remove the `throws` clause, or throw from the body.
- Casting a literal to the type its destination already declares is an error (E3010).
  Fix: remove the cast.
- An array index must be the array's `ElementIndex` (a `String` index is refused), `count()` of an array is
  an `ElementIndex`, and several public stdlib aliases are merged into `ElementIndex`, `EntryCount`,
  `BytePos`, `Byte` and `NetworkPort`.
  Fix: index with an `ElementIndex` (cast with `as ElementIndex`), read a string through its views
  (`bytes()`, `codepoints()`, `utf16()`), and use the merged alias names.
- An or-pattern binding is refused on any case that does not bind it (E3129).
  Fix: give the case that disagrees its own arm, or rename the colliding binding.
- A typealias declared inside a type or extension is a nominal type, and a private ranged alias belongs to
  its file.
  Fix: cast values between the alias and another type (`x as Name`), and give an alias that other files use
  a visibility modifier.
- A path holding a NUL names no file, and a NUL in a subprocess's program, argument, working directory,
  environment or redirect path refuses the spawn with `spawnFailed`; a host holding a NUL, or an empty host,
  is `resolveFailed`.
  Fix: do not pass NUL or empty strings; handle `spawnFailed` and `resolveFailed`.
- E3099, E3105 and E3160 are removed.
  Fix: delete any reference to these codes from scripts, tests and documentation that match on them.

### Added

- A debugger, on x64-windows, x64-linux, arm64-linux and arm64-macos. `maxon debug` runs a program under
  it, interactively or from a batch of commands: breakpoints by line, function or address, with
  conditions; stepping; pausing; locals, expressions and backtraces; every green thread, which can be
  listed, inspected and held; and the program's trace events beside its stops. It attaches from outside
  the program, so nothing is built into a program to make it debuggable beyond its `.mxdbg` sidecar.
- `maxon dap-server`, a Debug Adapter Protocol server over stdio, which the VS Code extension debugs
  programs and individual tests through. `maxon test --list --build` builds a project's tests and names
  each test, so a single test can be debugged.
- Thirteen `debug_*` MCP tools that drive the same debugger, and `maxon mcp-server --http`, an HTTP
  transport on loopback with sessions.
- Generic functions: `function f(x T) uses T where T is Equatable`, with `T` inferred from the arguments.
  Integers, floats, `String` and `bool` are `Equatable` and `Comparable`.
- Subtype aliases: `typealias X = int(range) implements Parent` widens to its ancestors with no cast,
  while siblings are refused and narrowing needs one.
- Program-wide defaults: a top-level `default Key = expression` that any thread reads with
  `Key.current()` and replaces with `Key.register(x)`, for a service reached through an interface or for
  an immutable value. `I.handle` is a handle to any running service whose type implements interface `I`.
- `SharedValue`, a value every thread reads lock-free and a writer replaces whole.
- `Log`, a structured logger modelled on Go's `log/slog`: levels, typed attributes, groups, and text and
  JSON handlers. A program logs with its first call.
- `HttpServer`, an HTTP/1.1 server on the green-thread scheduler. `HttpClient` decodes chunked responses,
  answers a closed set of status codes, and takes a whole-exchange timeout and an optional body cap;
  `TcpClient.connect` takes a deadline.
- `Random`, drawing from the operating system's cryptographic source on every target. `File.createText`,
  which refuses a file that already exists; file errors tell a busy file, a denied access and an existing
  file apart. `WallClock.nowUnixNanos`, RFC 3339 formatting and `CivilDate`.
- An enum or union that declares `implements` widens into that interface wherever a `type` would.
- A record whose fields are all `let` and together fit 64 bits — narrow unsigned ranges, `bits(n)` and
  `bool` — is stored as one machine word, with no allocation.
- Closures may be returned, stored in fields and containers, and passed to `async`; captured interface
  values and nested captures work.
- A union payload may be a float.
- A record holding a value at an interface type can be cloned.
- `maxon init`, which scaffolds a project.
- `__Builtins.scavengeMemory()`, which returns freed memory to the operating system, and builtins that
  attribute the live heap and allocation churn by type.

### Changed

- Projects, task files and tests have their own extensions. A `<name>.maxproj` file marks a project and
  describes its builds, replacing `build.maxon`; `maxon build <target>` runs one of its targets. A
  `<name>.maxtasks` file holds tasks, which `maxon run <task>` runs. Test files are `<name>.maxtest`.
- `maxon run <file>` is now `maxon execute <file>`. `maxon <file>.maxon` and `#!/usr/bin/env maxon` are
  unchanged.
- Every command-line option is long-form: `-o` is `--output=` and `-t` is `--filter=`. An option given to
  a command that does not take it is refused, and so is a bad `--log=` value.
- `maxon test --filter=` may be repeated, and each pattern must match a test.
- `Runtime.yield()` and `Runtime.processorCount()` are `Scheduler.yield()` and
  `Scheduler.processorCount()`.
- The trace-key recorder that was `Log` is `TraceCapture`.
- Arguments to an `async` call or a service message, and the managed locals a closure captures, are
  moved; reading one afterwards is a compile error. The `let`-argument freeze and E3160 are gone.
- A bare type name with more than one visible declaration is an error (E3063) rather than a silent pick;
  qualify it with its directory, `stdlib.` or `export.`. A file's own declaration wins.
- A hidden type is hidden at every place a type can be named, along with its fields and methods.
- A function's parameter, return and `throws` types must be at least as visible as the function (E3167).
- A function that declares `throws` must be able to throw (E3168).
- Inside a `test` body, a throwing call or `await` needs no `try`.
- Casting a literal to the type its destination already declares is an error (E3010).
- An array index must be the array's `ElementIndex`; a `String` index is refused. Array `count()` is an
  `ElementIndex`. Several public stdlib aliases were merged into `ElementIndex`, `EntryCount`, `BytePos`,
  `Byte` and `NetworkPort`.
- An or-pattern binding is refused on any case that does not bind it (E3129).
- A typealias declared inside a type or extension is a nominal type, and a private ranged alias belongs
  to its file.
- A full-range unsigned value such as `u64.max` passes range checks; an unsigned subtraction is refused
  only when it borrows.
- The language server checks a document against its whole project, so it reports what a build would.
  Hover and go-to-definition reach other files and the standard library, a window may hold several
  projects, and the server reports when it is loading one.
- A self-compile's peak memory is roughly halved.
- On Windows, program arguments, environment variables, file and directory names, the working directory,
  the executable's path and shared-memory names are UTF-8 for any Unicode text, and a child's arguments,
  environment and working directory reach it as the characters they spell. A non-ASCII host name resolves
  through its Unicode form.
- A duplicate declaration across files is reported on the same file on every host: the one whose path
  sorts later byte by byte, and a free function rather than the method it collides with.
- A path holding a NUL names no file, a NUL in a subprocess's program, argument, working directory,
  environment or redirect path refuses the spawn with `spawnFailed`, and a host holding a NUL, or an empty
  host, is `resolveFailed`.
- A program started with its standard input, output or error closed has them opened on the null device.

### Fixed

- Bytes written to a subprocess's standard input were cut at the first NUL.
- `Subprocess.run` hung when the child wrote more than a pipe holds before reading its input, and its
  timeout did not fire.
- On Windows, concurrently started children inherited each other's pipe ends, so a child's output did
  not end until an unrelated child exited.
- A subprocess whose pipe could not be created was started without it on macOS, and on Windows the
  failure reported an unrelated error.
- A failed subprocess start could close another file's descriptor, and a released subprocess handle
  could release a process started after it.
- A failed call's error reason could be replaced by another's after the green thread moved to another
  operating-system thread.

- A variable passed to a parameter the callee reassigns — a module-level `var`, a field, or a field
  reached through a chain — was not updated.
- Programs with a `bool` global and green threads crashed at startup on arm64.
- A slot in use could be handed out twice under load on arm64, corrupting memory.
- A record of narrow immutable fields spawned as a service crashed at shutdown.
- A value moved in a match arm that ends `and fallthrough` leaked when the match entered the next arm
  directly.
- A promise read out of a temporary container, or consumed twice from a field, crashed instead of being
  kept alive or refused.
- A generic service could hand its own state to a caller, and a fresh container over `T` returned from
  one was wrongly refused.
- A generic method storing a type-parameter value taken by reference was refused, or miscompiled.
- Managed globals were freed before a green-thread program's last handlers ran.
- A dropped coroutine that went on to wait on a socket hung the program.
- On arm64-linux, signal handlers ran with an undefined mask and signal stacks could overflow; writes and
  waits interrupted by a signal failed; a child killed by a signal was not reported as signalled.
- A timed-out subprocess left its children running.
- On Windows, a subprocess started by name could resolve to a file Windows cannot run, such as the
  extensionless script beside `npm.cmd`.
- A range check on an alias declared inside a type or extension was skipped.
- Every runtime abort now prints its reason, and a `wasm32-wasi` panic prints its trace.
- An undefined function reached through `try`, `await`, a capture or `for … in` reports E3004 at the
  call instead of an unrelated error.

### Removed

- `build.maxon`, `*.test.maxon` as test files, and the `-o`, `-t`, `--compiler`, `--version`, `-V`,
  `--help` and `-h` spellings.
- E3099, E3105 and E3160.

## 0.3.1 — 2026-09-18

### Fixed

- `maxon run` and a path-less `maxon build` refused to run anything on a host whose home directory
  cannot be written, rather than using the next cache directory in the list. A container started with
  a numeric user that has no account entry is the common way to meet this: the home directory is the
  filesystem root, nothing can be created under it, and every program was refused. A run now keeps to
  the same order as before and takes the first directory it can actually create.
- The refusal, when no directory at all can be created, names every variable the host consults
  instead of quoting whichever one it stopped on.
- The Docker images could not run a program as any user without an account entry, which is both
  variants' documented use. They name a writable temporary directory now.

### Changed

- `maxon cache` marks as in use the directory a run will actually fill, and where no directory can be
  created it says so instead of marking nothing. Reporting the cache now creates that directory if it
  is absent, which is what the next run would have done.

## 0.3.0 — 2026-09-17

### Breaking changes

- A `let` argument to a service message is lent rather than moved, and a write reaching a lent graph
  through another path is refused (E3160).
  Fix: write through a `clone()`, or send a `var`, a temporary or a literal, which still move. (0.4.0
  replaces lending with moves for every argument.)
- A callee that writes a field of its parameter is refused at the call when the caller's binding is a
  `let`.
  Fix: declare the caller's binding `var`, or pass a `clone()`.
- A handler over a combined error that carries a boxed member must match the error exactly once (E3161).
  Fix: match the error once and bind in its arms whatever the rest of the handler needs.
- `HttpClient` refuses any scheme but `http` before it connects, with `HttpError.unsupportedScheme`; an
  `https` URL was sent as plaintext to port 80 before.
  Fix: use an `http` URL (the client has no TLS), and handle `unsupportedScheme`.
- A missing executable is `SubprocessError.executableNotFound` on every operating system, and a working
  directory that does not exist now fails the spawn where Linux ignored it.
  Fix: handle `executableNotFound` where the program handled `spawnFailed` for a missing program, and pass
  an existing working directory or none.
- A build manifest's `debug_info` is honoured, a manifest build takes the tree lock and applies the same
  build-option refusals a command-line build does, and a malformed manifest field (a non-string `version`
  or source, a define with no `=`, a non-array `defines`) is refused.
  Fix: correct the field the error names (`sources` and `defines` are lists of strings, a define is
  `name=value`, `version` is a dotted string), and set `debug_info` to what the build should have; see
  Describing a build in the CLI reference.
- `optimize` in a build manifest is removed.
  Fix: delete it.

### Added

- `maxon cache`, which reports what the run cache holds on this host and which root the next run will
  fill, and `maxon cache clear`, which empties it. The cache moved out of the host's scratch area,
  where a temporary-file reaper could sweep it mid-session, into `<home>/.maxon/cache` beside the
  compiler an install puts there.
- `Directory.delete`, which removes an empty directory and tells `notFound`, `notEmpty` and
  `deleteFailed` apart.
- `Runtime.processorCount()`, the number of processors the scheduler runs green threads on.
- `Json.quote`, the JSON string escape three places in the standard library and the tooling each had
  a private copy of.
- Two worked examples — `maxgrep`, a parallel grep, and `msort`, a parallel merge sort — each beside
  a tour that reads it.
- A complete reference on maxon.dev: the language, the standard library, the command line and every
  diagnostic, generated from the sources the compiler itself is checked against.

### Changed

- The compiler lexes and parses over a worker pool rather than one thread. On its own source that
  stage spent 7.3 seconds on one processor of sixteen before the change.
- A `let` argument to a service message is LENT rather than moved. The sender keeps its reference and
  goes on reading the value after the send, where the only way to keep reading one was to clone it;
  `var` arguments, temporaries and literals still move. A lent graph is read-only while the service
  can see it, and a write reaching it through another path is refused (E3160).
- A value held at an interface type crosses a message — as a spawn argument, a message parameter or a
  reply.
- A record a callee builds crosses a service boundary as freshly owned, whether it is returned from a
  local it filled or produced by a qualified, static or `try`-guarded call. The proof followed one hop
  and recognised one spelling before.
- A struct-literal field initializer and a field assignment widen a concrete value into an
  interface-typed field, as a call argument already did.
- A closure written at an argument whose parameter is declared with a function type takes its
  parameter types from that declaration, so a comparator passed to `sort` needs no annotations. That
  offer was made at one position only.
- `match` accepts or-arms over a combined error — an awaited reply's own errors merged with
  `ServiceError.stopped`, or a block try's handler over several error types. A handler that binds a
  combined error carrying a boxed member must match it exactly once (E3161), which the release model
  had left unsound in both directions.
- A callee that writes a FIELD of its parameter earns E3019 at the call. `bump(a)` with `bump` doing
  `b.n = 99` compiled and wrote through a binding the caller had declared immutable; a method writing
  its own receiver stays legal, and so does a callee that only calls such a method.
- A `Vector` held inside an element type no longer makes the containing `Array` uncloneable.
- `HttpClient` refuses any scheme but `http` before it connects, rather than sending an `https` URL as
  plaintext to port 80.
- A missing executable is `executableNotFound` on every operating system, and a failed spawn names
  which step failed — stream setup, working directory or launch. A working directory that does not
  exist was silently ignored on Linux.
- A `build.maxon` manifest's `debug_info` is honoured; it was written and read by nothing. A manifest
  build now takes the tree lock and applies the same build-option refusals a command-line build does,
  and malformed manifest fields — a non-string version or source, a define with no `=`, a non-array
  `defines` — are refused rather than silently dropped.
- `maxon build` with no path reports one line about its build runner instead of that runner's own
  compile chatter.

### Fixed

- A `Map` or `Set` under insert-and-remove churn degraded until every lookup probed the whole table,
  because a removed slot counted toward nothing. A thousand rounds over one map and one set took 8.07
  seconds; four thousand now take 87 milliseconds.
- `FilePath.parent()` of a path directly under a root answered an empty or drive-relative path
  instead of the root, so a walk upwards never stopped at the top — it began asking the filesystem
  about the working directory and getting plausible answers back. The parent of a root now throws
  `noParent`.
- A `spawn` or a send could stop the program reporting a second owner for a value that had one: the
  temporaries its arguments built were released after the ownership walk rather than before, and a
  reply's walk ran before the handler had released what it held.
- Reading a promise through an array cursor crashed the compiler when the reply carried a companion
  error type, and a program awaiting promises read that way stopped before its results were in.
- `SharedSegment.readWord`, `writeWord` and `copyOut` read and wrote past the end of the segment
  instead of throwing `outOfBounds`.
- On Windows, `maxon upgrade` and the one-line installer could fail when the calling shell's module
  path hid the host's own PowerShell modules.

### Removed

- `optimize` in a `build.maxon` manifest, which named optimization levels that do not exist.

## 0.2.2 — 2026-09-16

### Added

- `MAXON_PREEMPT=off`, which stops the scheduler taking a processor back from the green thread
  holding it, for a program whose result depends on a thread running uninterrupted. Unset or `on`
  preempts as before; any other value stops the program before `main` runs.

### Changed

- `maxon build`, and `maxon` with no arguments, open with the version line and a warning that Maxon
  is an early preview whose language, standard library and tools may change incompatibly between
  releases.
- A build reports how long the compile took, and no longer prints a line naming the executable
  format it wrote.
- The one-line installers make `maxon` work in the terminal that ran them rather than only in the
  next one. On macOS and Linux, an install whose `PATH` already holds a writable `~/.local/bin` or
  `~/bin` links the compiler there and edits no shell profile.

### Fixed

- A `StreamingSubprocess` handle kept after its child was released could drive the next child the
  runtime started in the same slot — waiting on it, reading its output, writing to its input. A
  copied `StreamingSubprocess` value reached this on its own, because each copy tracked release
  separately. A released handle is refused now.
- The sixty-fifth `runDetached` in a program's life failed, because a detached child never gave its
  runtime slot back.
- Releasing a `StreamingSubprocess` while another green thread collected it left the collector
  reading a slot holding no child, and could corrupt the poll records of unrelated sockets and
  pipes. Two green threads collecting one handle stopped with the error for two readers on one
  socket. Each of the three is a named stop of its own now.
- A socket closed and reopened while an operation was parked on it could take the previous socket's
  timeout, or wake a green thread that no longer owned it.
- Connecting to, or binding, an address written as a dotted quad parked the green thread for a name
  resolution that never ran.
- On Windows, a socket send or receive cancelled by its deadline after the kernel had already moved
  part of the buffer reported a timeout and no count, so a retried send went out twice and received
  bytes were consumed unseen. It reports what it moved, and the deadline is reported by the next
  call.
- On Windows, opening, connecting to and closing a socket counted as blocking kernel calls, so the
  scheduler could take a processor back during them, and Winsock was started up again on every
  connect.
- `Environment.inheritUpdating` and `Environment.custom` take an `EnvMap`, which the standard
  library did not export, so no caller could build the argument. It is public.
- A compiler rebuilding itself moved the running binary aside before compiling, so a compile error
  left the tree with no compiler to build the fix — and the move failed outright when another
  process, such as a language server, still ran the previous binary. The move happens only once the
  compile has succeeded, and a previous binary still in use is retired aside rather than deleted.
- On Windows, `maxon upgrade` ran the install script in a Windows PowerShell that inherited the
  caller's `PSModulePath`. Started from pwsh, that names another edition's modules ahead of this
  one's, and the script stopped with `The term 'Get-FileHash' is not recognized`. The child is
  handed this PowerShell's own module directory.

## 0.2.1 — 2026-09-15

### Fixed

- The compiler occasionally crashed partway through a build with `Range check failed: value outside
  typealias 'AllocCount'`.
- A green thread that started reading a socket while another was finishing a read of the same socket
  could wait forever, unreported by the deadlock detector. It now stops with the documented error for
  two readers on one socket, every time.
- On Windows, a socket receive or send that had already completed when its deadline passed or the
  socket closed reported a timeout: the bytes received were lost, and a retried send went out twice.

## 0.2.0 — 2026-09-14

### Breaking changes

- Mutating a value that a live `let` can still observe is refused, however the value was reached (through a
  call's result, a closure, an interface method or a union payload as well as a direct field); a write
  through a method or a field that could reach such a record is E3159.
  Fix: bind the value with `var`, or write to a `clone()`.

### Added

- One-line installers for macOS, Linux and Windows, which install the latest release into
  `~/.maxon`.
- `maxon upgrade`, which updates the install the running compiler belongs to.
- Container images at `ghcr.io/maxon-lang/maxon`, in `debian` and `distroless` variants for amd64
  and arm64.
- The Homebrew formula serves x64 and arm64 Linux as well as macOS.
- `maxon mcp-server`, a Model Context Protocol server exposing build, run, test, format, check, IR
  dumps and error-code lookup to AI tools.

### Changed

- A green thread that runs for 10 ms without waiting is preempted, including inside a loop that
  calls nothing, so one busy thread no longer holds up the others.
- Socket reads, accepting a connection, subprocess pipes and waiting for a child process park only
  the green thread that waits; none of them occupies an OS thread any more.
- `main` runs as a green thread whose stack grows as needed, and idle green threads give back stack
  they no longer use.
- Mutating a value that a live `let` can still observe is refused, however the value was reached —
  through a call's result, a closure, an interface method or a union payload as well as a direct
  field. A write through a method or a field that could reach such a record is a new error, E3159.
- Generated code is substantially faster: every function called from one place is inlined, and the
  optimizer gains value-range analysis, bounds-check elimination against loop limits, loop
  unswitching, jump threading and hot/cold code layout. `fannkuch-redux` runs in about 1.2× the time
  of the C reference, down from 5.4×.
- A stack trace names inlined functions as their own frames, and prints up to 100 frames, saying
  when it stops short.
- The compiler runs its per-function optimization passes and instruction selection in parallel.
- On Windows, a compiled program's file description is its own name; the "Built with Maxon" credit
  is in its Comments field.

### Fixed

- `maxon build` intermittently hung on macOS when a build manifest ran subprocesses.
- Deep recursion in `main` overflowed its stack at 1 MB on Windows.
- A function returning a tuple of two records could hand back a freed record.
- Passing a struct that holds a file or socket to an `async` call crashed the compiler.
- A fault on arm64 macOS with a corrupted stack hung instead of printing a trace, and a fault on a
  Windows green thread printed its report twice.
- `maxon run` failed in deeply nested directories on Windows.

## 0.1.1 — 2026-09-10

### Breaking changes

- The `--version` and `-V` flags are withdrawn in favour of `maxon version`, and the `--help` and `-h`
  flags in favour of `maxon help`.
  Fix: run `maxon version` and `maxon help [<command>]`.
- `Build.buildOne` in a build manifest is renamed `Build.build`.
  Fix: call `Build.build` with the same arguments.
- `maxon fmt` separates groups of declarations with one blank line at every nesting depth, so a file
  formatted by 0.1.0 is rewritten.
  Fix: run `maxon fmt` over the project and keep the result.
- On Linux and macOS, starting a program that cannot be found raises `SubprocessError.executableNotFound`
  instead of `spawnFailed`.
  Fix: handle `executableNotFound` where the program handled `spawnFailed` for a missing program.

### Added

- `maxon run`, which compiles a program and runs it in one step. A `.maxon` file may begin with a
  `#!` line and be run directly as a script.
- `maxon help`, and `maxon help <command>` for a single command. Running `maxon` with no arguments
  now lists the commands.
- `--define`, which sets a top-level `String` constant at build time.
- Build manifests can declare several named targets, and state the version of what they build.
- Compiled executables carry version metadata: a version resource on Windows, a source version on
  macOS, and a producer string on Linux.

### Changed

- `maxon version` replaces the `--version` and `-V` flags, and reports the commit and date the
  compiler was built from.
- `maxon help` replaces the `--help` and `-h` flags.
- `Build.buildOne` in a build manifest is renamed `Build.build`.
- `maxon fmt` separates groups of declarations with one blank line at every nesting depth, not only
  at the top level.
- On Linux and macOS, starting a program that cannot be found raises
  `SubprocessError.executableNotFound` instead of `spawnFailed`.

### Fixed

- Non-ASCII text displays correctly in a Windows console.
- A deadlocked program on Linux reports the deadlock instead of occasionally hanging.
- A default parameter value is filled in a global variable's initializer, not only inside a function.
- On Linux, a subprocess started by program name finds the program on `PATH`, and a file of that
  name in the working directory is never run in its place.

## 0.1.0 — 2026-09-08

The first release. One compiler, written in Maxon, that builds itself.

### Added

- Binaries for `x64-windows`, `x64-linux`, `arm64-macos` and `arm64-linux`, each built and tested
  on its own architecture.
- A Homebrew formula for macOS.
- A VS Code extension, on the Marketplace and Open VSX, with a language server providing
  diagnostics, hover, go-to-definition, completion, rename, symbols and formatting.
- `maxon build`, `fmt`, `test`, `spec-test` and `lsp-server`, and a build manifest written as a
  Maxon program rather than a configuration file.
