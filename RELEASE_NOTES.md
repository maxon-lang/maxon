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

- A unique parameter (a `unique` type, a type holding a unique field, a `Reply`) is moved by the function that
  keeps it, as a local is: after `parked.answer(1)`, `shelf.push(token)`, `let first = token` or a field or payload
  store of a parameter, any later read of that parameter on a path that follows is E3102, and so is the same move
  inside a loop that goes round again. A second `answer` on a `Reply` parameter compiled until now.
  Fix: move the parameter once on each path; answer a `Reply` parameter in one arm of each branch, or answer it
  once and return.

- `let b = a` moves a unique value from a `let` source as it does from a `var`: after `let second = first` with
  `let first = Ticket.create(1)` (a `unique` type, a type holding a unique field, a `Reply`), reading `first` is
  E3102. Until now a `let` source stayed readable beside its `let` alias.
  Fix: read the value through the new name, or bind the source with `.clone()` where the type has one; a `unique`
  value has one name.

- `HttpServerError` has the case `deferralUnavailable`, which `HttpExchange.respondLater` throws for an exchange
  `HttpServer.accept` returned, and `HttpHandlerFault` has the case `threwAfterDeferring`, so a `match` over either
  that names every case no longer covers it.
  Fix: add a `deferralUnavailable` arm to each `match` over `HttpServerError` and a `threwAfterDeferring` arm to
  each `match` over `HttpHandlerFault`.

- A service type's state may not reach a handle to its own type: directly (`var me as Echo.handle`), through other
  service types' handles (`Ping` holds a `Pong.handle` and `Pong` holds a `Ping.handle`), or through an interface
  handle whose conformers include it (`Hub implements Listener` holding a `Listener.handle`). It is E4014, with the
  cycle in the message: a service's handle is counted by its holder, so a cycle of handles is never freed and the
  leak gate exits 101. A handle that a message carries is not state and is not a cycle.
  Fix: pass the handle with each message (`start(oracle Oracle.handle, me Asker.handle)`) instead of keeping it in a
  field, or give the two services distinct types.

- A service type's state may not hold a function value while a closure in the program captures a handle that reaches
  that service. A function type does not say what its closure captured, so a field, union payload or container
  element of function type reaches the handle of every service some closure captures, and a cycle through it is
  E4014 (`Echo` with `var notify as Notifier` while any closure captures an `Echo.handle`). The rule does not trace
  which closure is stored where, so it also refuses a program whose function field and capturing closure never meet.
  Fix: pass the handle as an argument to the call that needs it instead of capturing it, or keep the function value
  out of the service's state.

- A service type's state may not hold an interface value while a generic builds a conformer of that interface at a
  type parameter. `Wrap with T` that returns a `Gen with T` as a `Sink` makes, for `Wrap with Echo.handle`, a
  `Gen with Echo.handle` that no source text spells, so a state field `var sink as Sink` reaches the handle of every
  service, and a cycle through it is E4014. The rule does not trace which instantiation is stored, so it also refuses
  a program whose interface field and parametric conformer never meet.
  Fix: pass the handle as an argument to the call that needs it, keep the interface value out of the service's state,
  or build the conformer at a concrete type instead of a type parameter.

- A message or a `spawn` factory that may wait on another party while it holds its own service is E3221, and E3139,
  the await-cycle check, is removed. Waiting is an `await` of a message reply or of a promise whose coroutine the
  compiler cannot see, `awaitAny`, a send with a positive `sendTimeout:`, an `acquire`, the spawn of a throwing
  factory, `Scheduler.quiesce()`, `CownTorture.violations`, and a call to a function that does one of these, which
  includes `HttpServer.serve` and `TraceCapture.stopCapture`. Until now a message could await a reply from another
  service so long as no cycle of services awaited each other. The diagnostic names the chain from the message to
  the wait.
  Fix: send the request with the service's handle and handle the reply in another message (pass the asker's handle
  with the request, or answer through `reply.later()` and `Reply.answer`); send with `sendTimeout: 0`, which fails
  `mailboxFull` instead of waiting; or make the wait from `main`, a test body or a coroutine, which hold no service.

- A call on an `isolated` value that hands back a view of what it opens, such as `j.items.createIterator()` on an
  `isolated` `j`, cannot be bound, returned or stored: the view is mutable data of the region the call opens, which
  is E3201 (E3202 inside a `modify` or `acquire` block), and a `try … otherwise` handler on the `let` makes no
  difference. Until now the iterator was taken for a fresh value.
  Fix: bind the value, not the view: read what is needed in the same statement
  (`let first = try j.items.get(0) otherwise return 0`), or open the value with `modify j 'walk'` and iterate inside
  the block.

- A `unique` element handed out by a container call is lent for the call: `show(items.get(0))` reads it, and
  `let ticket = items.get(0)` is E3214, as is passing it to a parameter the callee keeps. Until now such a binding
  aliased the element.
  Fix: read through the call, or move the element out with `take` (`Slot`), `removeFirst`, `pop` or `remove`.

- `Subprocess` takes its working directory as a `WorkingDirectory`, not a `FilePath`: a child that starts in the
  parent's directory is `WorkingDirectory.inherit` and one that starts elsewhere is `WorkingDirectory.at(path)`, in
  `Subprocess.run`, `Configuration.create`, `Configuration.workingDirectory`, `StreamingSubprocess.spawnWithCwd`,
  `spawnWithEnvironment` and `spawnTraceable`. An empty path no longer means the parent's directory:
  `at(FilePath.empty())` fails with `SubprocessError.spawnFailed`.
  Fix: replace `workingDirectory: dir` with `workingDirectory: WorkingDirectory.at(dir)`, and an empty path
  (`FilePath.empty()`, `FilePath.from("")`) with `WorkingDirectory.inherit`, or omit the argument where it defaults.

- `SamplerError` has the case `signalTrampolineUnavailable`, which `SampledProcess.cycles` and `capture` throw
  on macOS, so a `match` over `SamplerError` that names every case no longer covers it.
  Fix: add a `signalTrampolineUnavailable` arm to each such `match`.

- A type that declares two fields of one name is E3006 (`Duplicate field 'count' in type 'Counter'`), reported
  at the second; the type keeps the first.
  Fix: rename or remove the repeated field.

- A block may not reuse the label of a block that encloses it, and the name of the enclosing function, method,
  `type`, `enum`, `union`, `interface` or `extension` counts as that block's label: `if x 'flipped'` inside
  `function flipped`, a `while … 'scan'` inside another `'scan'`, or a block labelled `'Shade'` in a method of
  `union Shade` is E2081. `end '<label>'` closes the innermost block it names, so a repeated label made the
  closer ambiguous. Two blocks that do not enclose one another may still share a label.
  Fix: rename the inner block's label.

- Every union payload is a `let`, and `let` before a payload (`case(let name T)`) is a syntax error. A `match`
  binding of a payload is never reassigned, so `full(value) then value = 42` is E2013 under a `var` scrutinee
  too, and a `var` that is only matched is E3077. A union whose payload types are all immutable is itself
  immutable, so `isolated` and `merge` on it are refused (E3200), and a unique payload handed to a call that
  keeps it moves out of the union, so reading the union afterwards is E3102.
  Fix: delete `let` before each payload; give a union a new payload by assigning it a new case
  (`b = Box.full(42)`); write `let` for a `var` that is only matched; drop `isolated`, `merge` and `freeze` on
  a union of immutable payloads; read the union before handing its unique payload to a call that keeps it.

- A file-private top-level `function`, `let`, `type`, `enum`, `union` or `interface` that nothing live in its file
  names is E3219, the way an unused local is E3012 and an unused `typealias` is E3062. A declaration is live when
  code that is not itself a file-private declaration names it (an `export`, `module` or `public` declaration,
  `main`, a test, a method), or a live file-private declaration does; declarations that only name each other, or
  a function that only calls itself, are all reported. A name spelled in a branch of `#if` the target excludes
  counts as a use.
  Fix: delete the declaration (the compiler lists each one), or use it.
- `+`, `-` and `*` over a full-width unsigned alias (`int(0 to u64.max)`, which `Count`, `ElementIndex` and
  `BytePos` are) are range-checked where they are computed: a carry out of bit 63, a borrow
  (`a.count() - b.count()` with `b` larger) or a product past `u64.max` panics with `Range check failed:
  value outside typealias 'T'`, in a `let`, a loop bound or an argument alike. They used to wrap to a valid
  bit pattern that no later check could tell from a real count. A check is omitted where the compiler proves
  the result fits, and a result it can compute that leaves the range (both operands constants) is E3005
  (`The result of 2 - 3 is outside the range of 'Count' …`).
  Fix: guard the computation (`if a.count() >= b.count()`), or write `for i in 0 to n - 1` as `for i in 0 upto n`; arithmetic that
  is meant to wrap modulo 2^64 (hashes, generators, address distances) is written over a `bits(64)` alias.
- Unary `-` over a value of a full-width unsigned alias (`-n` with `n` a `Count`) is E3218: the negation of any
  value above 0 is outside `int(0 to u64.max)`, and it used to wrap to a different positive number.
  Fix: convert to a signed alias first (`-(n as Delta)` for an `int(-1000 to 1000)` `Delta`), or, where the
  wrap is meant, do the arithmetic over a `bits(64)` alias.
- A cast to an alias that the arithmetic result already has, such as `(at - 1) as ElementIndex` over an
  `ElementIndex`, is E3010: the result of arithmetic on a full-width unsigned alias is already that alias.
  Fix: delete the `as`.
- `SubprocessError` has two more cases, `treeNotEnded(elapsedMs, stdout, stderr, osError)` and
  `detachRefused(osError)`: a deadline whose kill could not fully end the child's process tree, which threw
  `ioFailed`, and a `runDetached()` the enclosing Windows job refuses breakaway to, which threw `spawnFailed`,
  throw these. A `match` over `SubprocessError` that lists every case is no longer exhaustive (E2026).
  Fix: add arms for `treeNotEnded` and `detachRefused`; code that told these failures apart by the reason text
  of `ioFailed` or `spawnFailed` matches the cases.
- `let` means the name is never reassigned, so a `var` that is never reassigned is E3077 whatever else is done
  with it. A field store, a `push`, a call whose callee writes the parameter and moving the name into another
  binding no longer keep a `var`; what keeps it is an assignment, passing it to a `mutable` parameter and calling
  a `mutable self` method on a value of a value type (`text.append("!")` over a `String`). A `var` bound from a
  record, from state something else writes (`var node = holder.box`) or from a read-only value is reported alike,
  and so is a `var` bound to a literal array or map that is written through.
  Fix: write `let` (the compiler lists each site); write `let items = [..].clone()` where a `var` held a literal
  container that is written through, since a plain `let` of a literal is constant data (E3207).
- Reassigning a `let` is E2013 with the message `'x' is a let and cannot be reassigned` (`'f' is a let field and
  cannot be reassigned` for a field), and passing a `let` to a `mutable` parameter is E2013 (``'n' is a let and
  cannot be passed to the `mutable` parameter 'x'; bind it to a `var` ``), where it was E3159.
  Fix: expect E2013 wherever a script or test matches the code or the message; bind the value to a `var` to
  pass it to a `mutable` parameter.
- Constant data is `immutable`: a `let` bound to a literal, a `b"…"` byte string, a constant record or array
  literal or another constant `let` (module, `static`, field or local), and every inline `b"…"` literal. Storing
  one into a plain `ByteArray` field, array element or union payload is E3196, returning one from an unmarked
  result is E3209, and writing through it, passing it to a parameter the callee writes, or writing a `var` bound
  to a literal is E3207; each compiled before. The same holds through a `String` field of a `static` or module
  `let` (`append` on it is E3207), a closure that returns such data (writing its result is E3207, and it converts
  only to a function type that declares `returns immutable`, E3210), and a field default that names it
  (`var names = sharedNames`, E3196).
  Fix: declare the holder `immutable ByteArray` (a field, an `Array with immutable ByteArray` element, a union
  payload `named(let name immutable ByteArray)`) and the result `returns immutable ByteArray`; to get a mutable
  array build it with `ByteArray.create` and `push`, or `"text".toByteArray()`, or take `.clone()`; build a field
  default fresh, or declare the field `immutable`.
- A `Map` key and a `Set` element are `immutable` slots: `Map.insert`, `Map.upsert` and `Set.insert` take an
  `immutable` key or element, so a record or array key built at run time is E3207 until it is frozen. Numbers,
  `String` and other value types need nothing. A generic body that keeps its type parameter in such a slot (a
  `Set` of `T`, a `Map` key `T`) takes frozen arguments at every instantiation, so a collection of writable
  records passed to it is E3207.
  Fix: write `freeze` before the fresh key or element (`counts.upsert(freeze ByteArray.create(3), value: 7)`);
  `freeze` or `.clone()` the argument of a generic body that keeps its elements frozen.
- A record whose fields are all `let` is immutable only when every field's type is a value type or an immutable
  aggregate, so a record with a `let` array field is no longer immutable data: handing it to an `immutable`
  parameter, or sending it as `immutable`, is E3207.
  Fix: write `freeze` before the value.
- The `CharacterSet` presets are `public static let` constants, so `CharacterSet.whitespaces()` is refused.
  Fix: drop the parentheses (`CharacterSet.whitespaces`, `CharacterSet.decimalDigits`, `CharacterSet.letters`, …);
  `CharacterSet.from(chars)` is unchanged.
- An `Array` or `ByteArray` is created with a capacity: `Items.create()` with no argument is E3195.
  Fix: write `create(0)` for an empty array, `create(n)` for `n` zero elements in one allocation, or
  `create(n, zeroed: false)` for an empty array with room for `n`; a collection whose length is fixed after the
  fill is a `FixedArray`.
- Creating a `FixedArray` is a pure call, as creating an `Array` is, so discarding the result of
  `Vec3.create()`, or of a static that returns `Self{}`, with `_ =` is E3064.
  Fix: use the value, or delete the call.
- `InputSource.delayed(data)` is removed, with the fixed one-second stdin feed it stood for.
  Fix: to feed a child after it prints a marker, run it as a `StreamingSubprocess`, read its stdout with
  `tryReadStdoutLine()` until the marker line, then `writeStdinLine(...)` and `closeStdin()`; where the
  child only needs to block on stdin, use `InputSource.hold`.
- A `with` list names at most 64 type arguments; a longer one is E2015.
  Fix: group the arguments into a smaller number of types, such as a record type passed as one argument.
- `Vector` is renamed `FixedArray`, and its iterator typealias `Vector.VectorIter` is
  `FixedArray.FixedArrayIter`; `Vector with 3 Int` is an unknown type (E2010).
  Fix: rename `Vector` to `FixedArray` and `VectorIter` to `FixedArrayIter`.
- A `FixedArray` `get` or `set` whose index is proven inside the size — a constant, a value whose ranged
  type lies inside, or a counted-loop counter between compile-time bounds — cannot throw, so a `try` on it
  is E3055.
  Fix: drop the `try … otherwise` and write the call bare.
- A `module`, `export` or `public` top-level `let`, or `static let`
  member, whose type is less visible than the binding is E3167 (`exported constant 'api.BIG' names
  file-private enum 'Status' in its type`). The type is the one the initializer gives it, inferred or
  not, and includes the typealias the initializer was spelled through (`50 as Small`,
  `Tallies.create()`).
  Fix: widen the type to the binding's tier (`export enum Status`, `export typealias Small`, or
  `public` for a `public` binding), or narrow the binding (`let BIG` without `export`).
- Every other surface a declaration exposes is held to E3167 too: an instance field marked `module`,
  `export` or `public` (at the narrower of its own and its type's tier), a union case's payloads, a
  typealias's target, an `implements` clause and its `with` arguments, a type's or generic function's
  `where` clause, an interface's `extends` clause, and a struct-backed enum's backing record
  (`exported field 'api.Holder.mode' names file-private enum 'Mode' in its type`).
  Fix: widen the named type to the surface's tier (`export enum Mode`), or narrow the surface (drop the
  `export` from `export var mode as Mode`).
- A declaration whose name contains `unionCases`, such as a method `Holder.unionCasesFor` or an extension
  method `int.unionCasesFor`, is held to E3167 and to the unused-export check E3092 like any other.
  Fix: widen the type its signature names to the declaration's tier, or narrow the declaration; drop the
  `export` from one nothing outside its file uses.
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
  Fix: store the function value in a `var`.
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
- A function value fits a function type only by variance: a read-only parameter may be wider than the
  type's, a `mutable` parameter must be the same type, and a result may be narrower.
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
  first `send`, `readSome` or `shutdownWrite`, or on the new `handshake()`, as Go's `Conn.Handshake` does. A failed
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
  read-only: a handler whose methods write its fields is E3210 where it conforms to `HttpHandler`, and a
  handler that holds an OS handle, a function value or a promise cannot be frozen, so `serve` refuses it at
  compile time. What the handler wrote in place is no longer shared between requests or visible to the caller.
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
- A bare call to an instance method of the enclosing type from a `static` method or a file-scope
  initializer is E3193, with or without arguments: a first argument there is never the
  receiver (`bump(m)` with `m` a `Maker` no longer fills `self`).
  Fix: call it on a value (`m.bump()`) or pass the receiver explicitly (`Maker.bump(m)`).
- In a type that declares a `static` and an instance method of one name, a bare call from an instance method
  or its closures calls the instance method, whichever is declared first: with `function getValue()`
  declared before `static function getValue()`, a bare `getValue()` in an instance method is
  `self.getValue()`.
  Fix: write `Box.getValue()`, qualified by the type's name, where the static method is meant.
- A `union` value is refused as an operand of an arithmetic, bitwise or shift operator, of unary `-` or
  `not`, and as a condition, as a struct is: `n + Shape.empty` is E2004
  (`Cannot operate on int and Shape`).
  Fix: read the union with `match`, or its tag with `.ordinal` or `.rawValue`; declare an `enum` where the
  values are meant to be numbers.
- `TlsClient.connect`'s `deadlineMs` parameter is renamed `dialDeadlineMs` and bounds only the TCP
  connection, where it used to bound the connection and the handshake each; the handshake is bounded by
  the new `TlsClientOptions.setHandshakeDeadline(milliseconds)`, 30 seconds by default as
  `TlsServerOptions` already is, where a client with no `deadlineMs` waited without a limit. A handshake
  that outlasts it throws `handshakeTimedOut`. `HttpClient` caps the handshake deadline at what is left of
  the exchange's timeout.
  Fix: rename `deadlineMs:` to `dialDeadlineMs:` at each `TlsClient.connect` call, and call
  `options.setHandshakeDeadline(...)` where the old `deadlineMs` also had to bound the handshake (`0` waits
  without a limit).
- A message parameter, a message reply or a `spawn` factory argument whose type can hold a promise is
  E3217: a record with a promise in a field, a union with a case carrying one, or an array or list of
  promises, empty or not; so is a bare promise handed to a factory. A coroutine belongs to the strand that
  started it, so a promise that crossed was dropped, awaited or cancelled by a strand that does not own it,
  and these programs compiled and aborted with `fatal error: promise dropped by a machine that does not
  hold its strand`. A service's own state may still hold the promises its handlers start.
  Fix: `await` the promise and send, return or pass the result it yields.
- `spawn Type.factory(args)` runs the factory on the new service's own green thread instead of on the
  spawner's, and its arguments move there the way a message's do. A coroutine the factory starts therefore
  belongs to the service's strand. Programs that compiled before are refused. A binding passed to a factory is moved whether or not the factory keeps it, so
  reading it after the `spawn` is E3102, and an argument is held to a message argument's terms (`isolated T` or
  `immutable T`, E3217 without one), so one read out of a field or a container is not `isolated` (E3200) even
  where the factory does not keep it. A factory, or a function it calls, is held to the rules a message
  handler is: waiting on another party is E3221. A factory argument held at an opaque type parameter (a `T` value
  inside a generic function) is E2015.
  Fix: pass `binding.clone()` to keep reading the binding, and `.clone()` a borrowed argument; keep the
  value in a field of the service and reach it through a message; handle a reply in another message, as the E3221 entry
  describes; spawn from a function of a concrete type.
- A generic body the program instantiates with a promise, or a type holding one, is held to a promise's
  rules: a `T` parameter it stores in a record twice, or inside a loop, is E3102, and a closure in it that
  stores or returns a captured `T` is E3141.
  Fix: store the parameter once, outside any loop, and hand the closure the value as a parameter.
- The `ByteWord` alias is removed; `readOrderedWord` returns, and `appendOrderedWord` takes, an
  `OrderedWord`, the same `bits(64)`.
  Fix: write `OrderedWord` wherever `ByteWord` appeared.
- A sum, difference or product of values whose ranges are known carries the range those ranges give. A
  `FixedArray` `get` or `set` indexed by it that lands inside the size cannot throw, and neither can a `/`
  or `mod` by one that excludes 0, so a `try` on either is E3055.
  Fix: drop the `try … otherwise` and write the access or the division bare.
- Arguments and right-hand sides run before the target or receiver path is resolved, and an assignment
  counts the new value, stores it, then releases the old one. In `x.inner.name = x.dropInner()` and in
  `x.inner.add(x.dropInner())`, where the call rebinds `x.inner`, the write or the call lands in the
  `x.inner` that exists after the call runs.
  Fix: a program that depended on the path being resolved first puts the call on its own line before the
  statement, which makes the order explicit.
- A parameter is a `let` binding, so reassigning one — by assignment, by storing a fresh record in it, or by
  a method that writes its value in place such as `s.append("!")` over a `String` parameter — is E2013.
  Nothing is inferred any more: a parameter is by reference only where it is marked `mutable` before its type.
  Fix: write `mutable` on a value-type parameter (a number, `bool`, an enum or `String`) the function must
  reassign, as in `function bump(n mutable Count)`, or copy the parameter into a local `var` and reassign the
  local. A record-type parameter is never rebound; copy it into a local `var`.
- A `mutable` parameter takes a `var`, a field or an element that is writable: a literal or an expression
  handed to it is E2013, where it was given a temporary, and so is a `let`, a module-level `let`, a name a
  closure captured, and a field of a `let` that is another name for a record held elsewhere. A function with a
  `mutable` parameter fits only a function type that spells `mutable` at that position (E3005), and a
  message or factory parameter cannot be `mutable` (E2010).
  Fix: pass a `var` (a captured name or a module `let` goes through a local `var` or a `.clone()`); write the marker in the function type, as in `typealias Step = function(mutable Count)`;
  return the new value from a message or a factory instead of writing a parameter.
- Maxon has no overloading. A function name is declared once per directory, a method name once per type and
  an interface requirement name once per interface, so a second function of one name, a `static` and an
  instance method of one name, a method of a type and one its `extension` adds, and a field and a method of
  one type sharing a name are each E3006. E3007 and E3176 no longer exist.
  Fix: give the second declaration its own name, or fold an optional argument into one declaration with a
  default parameter.
- Standard-library operations that were overloads have one name per form. `Array.contains(sequence)` is
  `containsSequence`, `Array.sort(cmp)` is `sortBy`, `Array.sortUnstable(cmp)` is `sortUnstableBy`,
  `FilePath.join(FilePath)` is `joinPath`, `Hasher.combine(bytes)` is `combineBytes`,
  `Hasher.combined(state, bytes:)` is `combinedBytes`, `String.contains(Character)` is `containsCharacter`,
  `String.slice(start, length:)` is `sliceLength`, `TlsStream.send(ByteArray)` is `sendBytes` and
  `Iterable.contains(predicate)` is `containsWhere`. `Subprocess.run`, `TcpClient.connect`, `String.trim`,
  `String.trimStart` and `String.trimEnd` are one declaration each with default parameters.
  Fix: write the new name at each call; the arguments are unchanged.
- The standard library's accessors over stored values are fields, read without parentheses, and calling one
  is E4006 (`Type 'PemBlock' has no method named 'label'`): `DerHeader.tagClass`, `tagNumber` and `length`,
  `DerBitString.unusedBits`, `PemBlock.label` and `bytes`, `URL.scheme` and `path`, `X509Attribute.oid` and
  `value`, `X509Name.attributes`, `X509PublicKey.algorithm`, `X509BasicConstraints.isCertificateAuthority`,
  the `Certificate` fields `version`, `serialNumber`, `signatureAlgorithm`, `issuer`, `notBefore`,
  `notAfter`, `tbsCertificate` and `signature`, and the TLS handshake message and key-schedule types
  under `stdlib/helpers/tls`.
  Fix: drop the `()`, as in `block.label` and `certificate.issuer`.
- A top-level name is declared once per directory, whatever its kind and visibility: a `type`, `enum`,
  `union`, `interface`, `typealias`, top-level `let`, and `function` share one namespace, private or
  exported and generic or not. A second declaration in a directory is E3006 (`duplicate definition of
  'Point' — already declared as `type Point``), including two files of one directory that each declare a
  file-private `helper` and a `type Point` beside a `function Point`. Directories are separate scopes, and
  locals and parameters may shadow a top-level name.
  Fix: rename one declaration, or declare the name once at `module` scope and use it from both files.
- A bare name resolves the same way for every kind of declaration: the reading file's own directory's
  declaration, else the project root's, else the one declaration the file may name, else it is ambiguous
  (E3063 for a type name, E3095 for a function or a binding). A bare name that the project root declares and the
  reader's own directory also declares now means the reader's own, a reader's own declaration no longer
  competes with another directory's, and a program's own names shadow the standard library's and the
  runtime tier's. E3063 no longer ends `or rename this file's own
  declaration`.
  Fix: write `export.name` for the project root's declaration and `dir.name` for another directory's.
- A bare read of a name that the reader's directory declares as a function means that function, where it
  read another directory's exported `let` of the name.
  Fix: write `dir.name` to read the `let`; another directory's top-level `let` is read as
  `alpha.LIMIT`.
- A `String` or `Character` interpolated with `q` (`"{name:q}"`) prints between quotes with a literal's
  escapes, where the specifier left it plain: `{`/`}` are `\{`/`\}` in a `String`, control characters are
  `\uXXXX`, and a width pads the quoted text (`{name:8q}`).
  Fix: drop `:q` where the plain text is wanted.
- `Aes.encryptBlock`, `Aes.decryptBlock`, `Aes.xorKeyStream`, `AesGcm.seal` and `AesGcm.open` are pure calls
  on every target, so discarding a result with `_ =` is E3064.
  Fix: use the result. For `AesGcm.open` that means acting on the plain text or on the failure, since the call
  is also the authentication check.
- Storing a value of one named integer type into an array whose element type is another, or a value of one
  float alias into an array of another, is E3005. `items.push(n)`, `items.set(i, value: n)` and
  `WideArray from [n]`, with `n` a `Narrow` and the array an `Array with Wide`, name the cast, as a variable,
  field or argument does; a float alias has no cast hint. A program that declares its own `typealias Byte`
  and stores it into a stdlib `ByteArray`, or the reverse, crosses two types.
  Fix: write `n as Wide`, or use the stdlib `Byte`. A plain `int`, a value of the element's own alias and an
  `as Wide` cast are accepted.
- A payload assignment in a `match` must reach its union. Assigning a match binding replaces the payload held
  by the matched union, so it must be able to write that union: the scrutinee is a local `var`, a field of a
  `var`, a record parameter's field, or a parameter marked `mutable`. Any other scrutinee (an unmarked
  parameter, the receiver `self`, a temporary) is E2013, and the message names the fix.
  Fix: mark the parameter `mutable`, or copy the union into a local `var` and match on that.
- A call that fills array slots with zero is E3106 for an element that is a payload-free enum or union whose
  raw values (tags) include no 0, as it is for a ranged alias that excludes 0: `Array.create(n)` and
  `create(n, zeroed: true)`, `resize` and `FixedArray.create`. Zero named no case, so a `match`
  over such an element was not exhaustive. In a generic body, where the element is a type parameter, the
  instantiations decide: `create(n)` over the parameter and a `FixedArray with N <type parameter>`'s
  `create()` are refused when any instantiation of the type is at a managed type, at such an enum or at a
  ranged alias that excludes 0, and so is `resize` in a body you wrote (a standard library body may still
  `resize` a column it fills before reading, as `Map` and `Set` do).
  Fix: build the array with `create(n, zeroed: false)` and `push` each element, or with `growFilled(n, value:)`
  naming a case, or give the enum a case whose raw value is 0. In a generic body, hold the elements with `push`
  or `growFilled(n, value:)`, or instantiate the type only at elements that admit 0.
- A promise held in a local is must-use. A promise that reaches the end of its scope, is overwritten, is
  discarded with `_ =` (`_ = async work()`), or is left behind by an early `return`, a `throw` or a propagating
  `try` before it is awaited is E3213, and so is a service reply bound to a local and never consumed. A
  service reply discarded with `_ = try h.say("a")` is still accepted.
  Fix: `await` the promise, call `.cancel()` on it, store it or return it on every path; `.cancel()` is how a
  coroutine is abandoned.
- An `async` call given the caller's mutable data (a record, an array, a variable at a `mutable` parameter)
  makes a tied promise. It is held in a local and awaited or cancelled on every exit of the function, and
  returning it, storing it in a field or a container (`walks.push(async walkTree(root))`) or passing it to a
  function is E3212, as is an exit that leaves it neither awaited nor cancelled.
  Fix: `await` it or call `.cancel()` on it before every `return`, `throw` and propagating `try`; pass a
  `.clone()` of the argument to untie the call (`async walkTree(root.clone())`).
- A variable handed to a `mutable` parameter of an `async` callee is lent to the coroutine, which writes the
  caller's own variable, where the coroutine wrote storage of its own and nothing reached the caller. Until
  the promise is awaited or cancelled, reading, writing or passing the variable is E3102, and naming it in the
  statement that starts the coroutine is E3186. Non-local storage at a by-reference
  position of an `async` call is E2015.
  Fix: `await` or `.cancel()` the promise before the variable is used again, and pass a local `var` or a field
  of one.
- A variable passed at a `mutable` parameter must be declared at the parameter's own type, alias included: a
  `Narrow` variable at a `mutable Wide` parameter, or a `Wide` one at a `mutable Narrow` parameter, is E3005 at
  the call, in direct calls, method calls, calls through function values and interfaces, generic functions and
  coroutines. The write used to be range-checked when the call returned.
  Fix: declare the variable or field at the parameter's type, or pass a local of that type.
- E3186 follows evaluation order and the callee's writes. A local read by an operand evaluated before a call
  writes it through a `mutable` parameter is refused anywhere in the statement, judged per root binding: the
  other argument or the receiver (`bump(h.n, h: h.copy())`, `self.item.bump(self.level)`), sibling fields of one
  root, a field of `self` or a module `var` beside its own cell, `f(s, s)` through a function value or an
  interface, a ternary condition wrapped around the call, a closure that captures the local, and a receiver
  call whose by-reference write reaches a root an argument reads. A read that runs after the write, and a
  `mutable` parameter the callee never writes, are accepted. The message names the other operand and the cure
  where it said `overlaps argument`. A static `var` field path or a namespace-qualified `var` passed by
  reference now reaches the callee's write, where it was lost.
  Fix: copy the local into a `let` in an earlier statement and use the copy for the other operand; for a
  by-reference operand, copy into a `var`, pass the copy and store it back after the call.
- A function type and an interface requirement are read-only and neither wait nor yield. A function value,
  closure or conformer that writes a parameter, its receiver or a capture the boundary does not declare, or
  that may wait or yield, is E3210, and a `mutates captures` value called through frozen or read-only data is
  E3211, inside a `modify` block as well when the value came from the data the block holds. A closure that
  builds a record from its captures counts as writing them. The library's own boundaries are declared the same
  way: `Iterator.advance`, `BidirectionalIterator.retreat` and `LogHandler.handle` are `mutable self`,
  `HttpHandler.handle` takes
  `exchange mutable HttpExchange`, and `Array.SortComparator` and the other library function types do not
  declare `mutates captures`.
  Fix: declare what the callee does on the boundary: `mutable` on the parameter (`function(mutable Row)`),
  `mutable self` on the requirement (`function grow(mutable self, by Tally)`), `mutates captures` after the
  result of a function type whose closures write what they capture or build a record from it; declare
  `mutable self` on the interface requirement whose conformer's result holds data from its receiver (E3210).
  Spell `mutable` on an `HttpHandler` conformer's `exchange` parameter. A callee that waits on a reply or
  awaits a coroutine cannot stand behind a function type or requirement: start the wait in the caller.
- `keep`, `sole`, `returns sole` and `returns readonly` are removed, along with E2076 and E3189, and fail as
  any stray word does. A message or factory parameter marked `mutable` is E2010, where it was E2076.
  Fix: delete the qualifier. A function type whose callee writes a parameter says `mutable T`; whether a
  function keeps a parameter is read from its body.
- A type with a unique field is unique, `Promise` is declared `type Promise is unique`, and a unique value
  is never cloned or aliased. `clone()` of a record, array or promise holding a promise is E3214, and so is
  freezing one, or binding, returning, storing or handing to a keeping parameter a unique value read out of a
  field, an element, a `for` loop variable or a tuple destructure.
  Fix: move the value out with `pop` or `remove`, or `swap` it out of an `isolated` field; a holder of promises
  has no `clone()`.
- A `for` loop over an array whose elements move out as the loop runs, such as an array of promises, makes the
  array read-only in the body: pushing to it, clearing it or passing it to a function that writes it is E3197 or
  E3207.
  Fix: collect what the body adds in another array and use it after the loop.
- Every program is held to the capability checks, whether or not its source spells a region word: a write
  through frozen or read-only data (E3197), a store that mixes capabilities (E3196), an argument a
  parameter's inferred use refuses (E3207), a parameter the body needs both mutable and immutable (E3206),
  results whose source arguments disagree (E3208, E3209), a call that may wait or yield inside a `modify`
  block (E3205), and a function value or conformer that writes, keeps or returns a parameter its function
  type or requirement leaves read-only (E3210).
  Fix: follow the message, which names the use that decided the class: pass a mutable argument, `freeze` a
  `.clone()` where a frozen one is wanted, or move the write out of the data that is frozen; declare
  `mutable` on the parameter or `mutable self` on the boundary where a function value or conformer writes,
  and return a `.clone()` of a read-only parameter instead of the parameter.
- `sendable` and `isolated` after `is` in a generic type's `where` clause are region bounds, so an interface
  of either name is not a constraint there, and the same words in a function's or an extension's `where`
  clause are E3215.
  Fix: rename an interface called `sendable` or `isolated`; bound a generic function's parameter by an
  interface.
- A child's output is read through a `StreamReader`: `StreamingSubprocess.readStdoutLine`,
  `readStdoutLineCapped`, `readStdoutBytes`, `readStderrLine`, `readStderrLineCapped`, `tryReadStdoutLine` and
  `tryReadStderrLine` are removed, with `LinePoll`, `SubprocessError.endOfStream` and `endedTheStream()`.
  `takeStdout()` and `takeStderr()` each hand out the stream once, as an `isolated ChildOutputReader`, whose
  `readLine()` answers a `LineOrEnd` (`line(text)`, `ended`) and throws `StreamError`, and whose
  `readBytes(n)` answers a `ByteArray`.
  Fix: take the reader once (`let out = try child.takeStdout()`) and read from it: match `out.readLine()` for
  `line(text)`, and treat `ended` where `endOfStream` was caught; `String.from(try out.readBytes(n))` for the
  bytes. Delete `endOfStream` arms. A loop that polled with `tryReadStdoutLine` reads each stream in a service
  of its own and `awaitAny`s over the replies beside a timer.
- `maxon test` fails a test that passes but leaves a process running when it ends, naming the process, on
  Windows, Linux and macOS; when no single test of a process leaves it alone, every test of that process
  fails, naming them. `spec-test` fails a case the same way.
  Fix: wait for every process a test starts before it ends: `Subprocess.run`, `wait()` on a
  `StreamingSubprocess`, or a poll of `Process.isAlive` until a detached child has ended.
- `Process.isAlive(pid)` answers `false` for a process that has ended and not been reaped, on Linux and macOS
  as on Windows.
  Fix: wait on a child with `wait()` where the program needs to know it was reaped; a loop that polls
  `isAlive` until it answers `false` needs no change.
- `Directory.list` throws `DirectoryListError.accessDenied` for a directory the process may not list and
  `listFailed` for any other failure, where it threw `notFound` for both.
  Fix: handle `accessDenied` and `listFailed` beside `notFound` where a `notFound` arm stood for every
  failure to list.
- A module-level or `static` `var` is removed: `var` at file scope, in a `module` or `export` declaration, or
  after `static` is a syntax error, where it declared one word shared by every function. `let` stays, at file
  scope and after `static`.
  Fix: keep a counter or flag a function updates in a local `var` and pass it to the function as a `mutable`
  parameter; keep state a closure writes in a record the closure captures; share state between a service, a
  coroutine and `main` through a `SharedValue`, or keep it in a field of the service and reach it with a
  message; write a value that is never reassigned as a `let`.
- A service type whose state holds a promise — a field of type `Promise`, an array of promises, a record that
  holds one — is E3216, reported at the `spawn`. Each message may run on a different green thread, and a
  coroutine belongs to the strand that started it.
  Fix: `await` the promise, or `.cancel()` it, before the message or factory returns, and keep its result in
  the field.
- A service's `spawn(capacity: n)` admits exactly `n` queued messages. A service that was parked idle took one
  more directly, so the `n + 1`th send succeeded; it now fails `mailboxFull` (or waits, with a `sendTimeout:`).
  Fix: give the service a capacity one larger where a program relies on the extra slot, or give the send a
  `sendTimeout:`.
- A service message parameter, message reply or factory parameter whose type is a record, a union with a
  payload, an array or other container, or an interface value is written `isolated T` or `immutable T`; with
  neither, the `spawn` is E3217. Only the factory the `spawn` names is judged, and a reply is judged at its
  declaration. Numbers, `bool`, `String`, `Character`, enums, unions whose cases carry nothing, service and
  interface handles, `SharedValue` cells, tuples that own no heap, and types that hold a `unique` value or an
  OS handle need no word.
  Fix: write `isolated T` and send a fresh value or an `isolated` local, which the sender cannot read
  afterwards, or write `immutable T` and send `freeze x.clone()`.
- `LogHandler.handle` takes `record immutable LogRecord`, so a handler conforming to
  `LogHandler` declares `handle(record immutable LogRecord)`.
  `LogRecord.create` takes `attrs isolated LogFieldArray`.
  Fix: spell `immutable` on a `LogHandler` conformer's `record` parameter; pass `attrs` as a fresh
  `LogFieldArray.create(0)` call or an `isolated` local, and `freeze` a `LogRecord` before sending it to a
  handler.
- `HttpServer.serve` takes `handler immutable HttpHandler`.
  Fix: write `server.serve(freeze MyHandler.create(…))`; a function that receives a handler for `serve`
  declares its parameter `immutable`.
- A promise has no zero value, so `create(n)`, `create(n, zeroed: true)` and `resize` on an `Array` of
  promises are E3106, as they are for a `String`, and so is `create(n)` in a generic body that some
  instantiation makes an array of promises.
  Fix: create with `create(n, zeroed: false)` and fill with `push`.
- An `extension` whose name is not a declared `type`, `enum`, `union`, `interface` or primitive is refused:
  an unknown name is E3011, and a typealias (`extension WideBox` over `typealias WideBox = Box with Wide`) is
  E2015. Both compiled to an extension that added nothing.
  Fix: extend the declaration the name stands for, or delete the extension.
- An `extension` of an `enum` or a `union` adds its methods, where it added nothing and its body was never
  checked. The body is now compiled, so a method that repeats one the enum declares is E3006, an error in a
  body is reported, a `static function` is E2015 as in the enum's own body, and a nested `typealias` is E2010,
  since an enum body declares none.
  Fix: correct what the extension now reports, declare a `static function` as a free function and a
  `typealias` at the top level, and delete an extension that was meant to add nothing.
- The standard library functions `utf8IsAscii(b Byte)` and `utf8IsValidCodepoint(cp Codepoint)` are removed;
  a call to either is E3004.
  Fix: write `b < 128` for `utf8IsAscii(b)` and `cp < 0xD800 or cp > 0xDFFF` for `utf8IsValidCodepoint(cp)`;
  to test whether a whole `String` is ASCII, call `isAscii()`.
- A character or string literal whose bytes are not well-formed UTF-8 is E1004: `'\uD800'`, `"a\uDFFFb"`, and
  a surrogate or a malformed byte sequence typed into the source. UTF-8 has no spelling for a surrogate
  (U+D800 to U+DFFF), and such a literal used to build a `Character` or `String` holding one.
  Fix: write `\uFFFD` where a placeholder is meant; bytes that are not text are a `b"…"` literal.
- A function that returns frozen data on one path and writable data on another, including one `return` whose
  `if … else` or `match` gives both, hands back a read-only result to every caller: writing through it, or
  passing it to a `mutable` parameter, is E3197 or E3207 whatever the receiver is, where only a caller whose
  receiver was an unmarked parameter was held to it.
  Fix: declare the function `returns immutable` when every path is frozen, or return a `.clone()` of the frozen
  path.
- A function declared `returns immutable` that returns one of its parameters on some path, including a
  `return` whose `if … else` or `match` also gives frozen data, takes frozen data for that parameter, as it does
  when it returns the parameter alone: passing a mutable value is E3207.
  Fix: `freeze` the argument, or pass a `.clone()` of it.
- The keys of a `Map` literal are held to `Map.upsert`'s `immutable` key like the argument of a written
  `upsert`, so a key that something may still write, such as the fresh result of a call that returns its
  argument, is E3207.
  Fix: `freeze` the key where the literal writes it.
- A `let` bound to a collection literal (a `Map`, a `FixedArray` or an array literal) is constant data, so a
  call that writes it (`upsert`, `set`, `push`) is E3207, and a `let` bound to a `String` is a name that is
  never reassigned, so `let s = "…"` followed by `s.append(…)` is E2013.
  Fix: declare the name `var`, or write to a `.clone()`.
- `freeze` of a record that is an immutable aggregate (every field a `let` of a value type or of such a
  record) is E3200, because the record already counts as a value: `freeze LogHandlerOptions.create()`.
  Fix: delete the `freeze`.
- A `for` over a read-only `List` (frozen, or a module-level `let`), or over a collection whose element type is
  `immutable` (`Array with immutable Box`), hands out read-only elements: writing one is E3197. The element an
  iterator hands out of a frozen collection parameter is frozen too, so handing it to a function that writes its
  parameter is E3207.
  Fix: iterate a `.clone()`, or write through a mutable list; take a mutable collection, or `.clone()` the
  element, where a callee writes it.
- A closure that writes through a captured iterator writes the collection the iterator runs over, so a function
  whose parameter the closure iterates takes a mutable argument: a frozen one is E3207. A union parameter is
  judged like a record parameter, and a parameter a function passes only through a retained alias (an
  interface-typed argument, for one) to a callee that stores or writes it is mutable only, so a read-only
  argument is E3207.
  Fix: pass writable data or a `.clone()`.
- `try … otherwise <param>` joining two parameter sources is E3208 for mixed arguments (one frozen, one
  fresh), and binding an element of an `isolated` container to a `let` or `var` outside a block is E3198.
  Fix: `freeze` or `.clone()` one argument so both share a capability; open the container with `modify`.
- A bare `get(…)` inside an `extension Array` or `Array` body takes the capability of the receiver, so
  writing through its result when the receiver is frozen or read-only is E3197, and so is writing through what
  an `extension` member returns out of an instance whose elements are `immutable` (`return try self.get(0)`
  over `Array with immutable Point`).
  Fix: take a `.clone()` of the element.
- `SubprocessError` has a new case, `statusLost(stdout, stderr)`, thrown by `Subprocess.run` and by
  `StreamingSubprocess.wait` for a child that ended while another process took its exit
  status (on macOS, a program killed while a debugger's stub held it; on Linux, a child reaped by another process). A `match` over `SubprocessError` with no
  `default` that lists every case no longer compiles.
  Fix: add a `statusLost` arm, or `statusLost(stdout, stderr)` where the arm uses
  the output, or give the `match` a `default throws`.
- A conformer that binds an associated type to an interface (`type Impl implements Source with Named`) is
  E2015 at that binding when an `extension` of the interface stores the associated type as a container element
  (`typealias ItemArray = Array with Item`), however many bodies use the alias. It compiled while nothing used
  the alias, and stopped the compiler with a panic once something did. A standard library extension counts
  the same way: `extension Iterable` stores `Element` as a container element (`ElementArray`) and in the
  function types `ElementTransform` and `ElementPredicate`, so `type Names implements Iterable with (Named,
  NamedCursor)`, which binds `Element` to an interface, is E2015 at that binding and again at the library's
  alias.
  Fix: bind the associated type to a concrete type that implements the interface (for an `Iterable`
  conformer, a concrete element type), or take the container out of your own extension.
- `Directory.delete` throws `deleteFailed`, not `notFound`, for a path that holds a file or a link, a dangling
  link included. `notFound` means nothing is at the path.
  Fix: where a handler treats `notFound` as "already gone" for a path that may hold a file or a link, handle
  `deleteFailed` the same way, or remove a file with `File.delete`.
- `RunProcessError`, the error `__Builtins.runProcess` throws, has a second case, `statusLost`: the child ended
  while another process took its exit status, so there is no exit code to report. It used to answer the
  status the runtime last held, 0. A `match` over `RunProcessError` that lists every case, with no `default`, no
  longer compiles (E2026).
  Fix: add a `statusLost` arm beside `spawnFailed`, or give the `match` a `default throws`.
- A bare generic base means what the reading file means by that name. A file where a `typealias Pair` wins the
  bare name `Pair` (its own, a sibling file's in the same directory, or the project root's) and that writes `Pair with …` named the
  generic type another directory exports as `Pair`; it is now E2055 (`Type 'Pair' has no associated types`), because
  an alias is not a generic type and the alias that wins is the declaration the bare name means.
  Fix: write the directory-qualified base for the generic type (`a.Pair with …`), or rename the alias.
- `view` is removed: `view x = e` fails as any stray word does. It was another spelling of `let`.
  Fix: write `let` (`view v = h.box` is `let v = h.box`).
- The `readonly` element qualifier is removed, with E2075 and E2077: `Array with readonly T` and `Map with K,
  readonly V` fail as any stray word does. A collection whose elements nothing may write is `Array with
  immutable T` or `Map with K, immutable V`; a value stored into one must be frozen (E3196), and an element read
  out is read-only (a write through it is E3197).
  Fix: write `immutable` where `readonly` stood, and `freeze` a value built at run time where it is stored into
  the collection.
- The read-only value rules that made `let` deep are gone, and with them E3078, E3159, E3188, E3135, E3137,
  E3138, E3163, E3164 and E3191: binding read-only data to a `var`, to a mutable place or to a keeping
  parameter now compiles, and the write is what is refused. A write through frozen, constant or read-only data is
  E3197 (E3207 where the data is handed to a parameter the callee writes), a store into a plain field, element or
  payload is E3196, a `mutable` by-reference argument that is a literal, an expression, a captured name or a
  module `let` is E2013, a message or factory value that cannot cross is E3217, and a `spawn` reachable from a
  global initializer is E3220. A send no longer checks sole ownership at run time: a value that crosses is
  `isolated` (E3200, E3102) or `immutable` (E3207), so a program whose message arguments were aliased by something
  else is refused at compile time, and exit code 96 now means only that a `freeze` or publish walk reached a value
  held at an interface type whose conformer no walk can be built for.
  Fix: expect the replacement code wherever a script or test matches the old one; write `.clone()` where a
  mutable copy of read-only data is wanted, and send `isolated` values or `freeze x.clone()`.
- The initializer of a module-level `let` or a `static let` calls nothing: a call in it is E3220 (`let shared =
  Cache.create()`, `let name = "ada".toByteArray()`, `static let set = CharacterSet.from(chars)`), and a call in
  the initializer of a number, `bool` or enum binding is E2045 as before. A module `let` is image data, laid down
  before the program starts and read as one `immutable` copy by every function and thread; a call that built a
  value once before `main` no longer runs there.
  Fix: write a literal; or build the value with a function where it is used, and call `.clone()` on a constant
  where a mutable copy is wanted.
- The OS handle types `TcpClient`, `TcpListener`, `TlsStream`, `TlsListener`, `HttpServer`,
  `StreamingSubprocess` and `SharedSegment`, and every type that holds one, are unique: one name owns a handle.
  Binding one with `let`, pushing it into a container or handing it to a `spawn` moves it, so reading the name
  afterwards is E3102, and `clone()` of one is E3214. `SampledProcess.clone`, `SeizedThreads.clone` and
  `MachinePin.clone` are removed, and `StreamingSubprocess.handle` is private. Aliasing a handle used to stop the
  program with exit code 96 at a send.
  Fix: read what you need from a handle before it moves; let one owner hold it and lend it to calls; take it out
  of a container with `pop`, `remove` or `swap`.
- A `unique` value handed to an `async` call is lent to the coroutine until its promise is awaited or cancelled:
  `async serve(server)` followed by a read, `close()` or pass of `server` before the await is E3102, and a
  `HttpServer` is stopped with `.cancel()` on its serving promise, not by a concurrent `close()`. `serve` on a
  server already closed returns `closed`.
  Fix: read the value before the `async`, or after the await or cancel; stop a server with `.cancel()` on its
  promise, then call `close()`.
- A `throw` inside a `modify` block, or in the statement that opens an `isolated` value, is E3202 when its
  payload holds mutable data of the open region, as `return` is. A `throw` caught by a `try` block opened inside
  the `modify` stays legal.
  Fix: copy the value out (for example `.clone()`) before it goes into the payload, or throw only values,
  `immutable` data or fresh `isolated` data.
- `StreamingSubprocess.waitWithTimeout` is removed: `wait` takes the deadline, `wait(deadline:
  WaitDeadline.within(ms))`, and with none waits until the child exits. A `WaitDeadlineMs` is at least one.
  Fix: replace `waitWithTimeout(ms)` with `wait(deadline: WaitDeadline.within(ms))`, and `waitWithTimeout(0)`
  with `wait()`.
- `Subprocess.run` takes `deadline WaitDeadline = WaitDeadline.none` where it took `timeoutMs`, and
  `Configuration.timeoutMs` is `Configuration.deadline`, a `WaitDeadline`.
  Fix: write `deadline: WaitDeadline.within(ms)` for `timeoutMs: ms` and `config.deadline =
  WaitDeadline.within(ms)` for `config.timeoutMs = ms`; drop a `timeoutMs` of `0`, which is the default
  `WaitDeadline.none`.
- `ExitPoll` and `TerminationPoll` have the case `reapedElsewhere`: another process took the child's exit
  status, so this process cannot tell whether it still runs. A `match` over either that lists every case no
  longer covers it.
  Fix: add a `reapedElsewhere` arm, or ask `pollExit().childIsKnownGone()` where the question is only whether
  the child has exited.
- `Console.stdin()` returns a `StdinReader`, a `StreamReader` over standard input: `readLine()` answers a
  `LineOrEnd` and throws `StreamError`, and `readBytes(n)` throws `StreamError`. `Stdin`, `Stdin.create()` and
  `ConsoleError` are removed.
  Fix: match `try stdin.readLine()` for `line(text)` and stop at `ended` where `ConsoleError.endOfFile` was
  caught; write `StdinReader` for `Stdin`.
- `TcpClient.recv(bufferSize)` is `readSome(most)`: it answers an `isolated ByteArray`, an empty one once the
  peer has closed, and throws `StreamError` (`timedOut` past the read deadline, `failed(reason)` for an OS
  error). `RecvCapacity` is `ReadCapacity`, declared in `stdlib/Stream.maxon`, and `NetworkError.recvFailed` is
  removed.
  Fix: write `String.from(try conn.readSome(n))` for `try conn.recv(n)`, treat an empty answer as the peer's
  close where `connectionClosed` was caught, and handle `StreamError`; read lines with
  `StreamReader.over(conn)`. Delete `recvFailed` arms.
- A method of a generic type that widens an instance built over the type's own parameters to an interface (`Gen
  with T` returned as a `Sink` inside `type Wrap uses T`) takes the widened value's witness table from the layout
  descriptor its caller passes. When the instance's methods reach the enclosing type again, so that composing the
  table at a concrete instantiation would build an ever-deeper chain of instances, the widening is E2015.
  Fix: give the type argument a concrete type, or pass the value it wraps as a type parameter of its own.
- `StreamReader.readLine()` takes no cap and reads a line however long it is: it answers a `LineOrEnd`
  (`line(text)`, `ended`). A capped read is `readLineWithin(cap)`, which answers a `LineRead` with
  `pastTheCap`. `DefaultLineCap` is removed.
  Fix: delete the `pastTheCap` arm from every `match` over a `readLine()` result and write `LineOrEnd` where
  `LineRead` names its type; replace `readLine(cap: n)` and `readLine(n)` with `readLineWithin(n)`.
- `as isolated T` is the whole initializer of a `let` or `var`; written inside a call argument it is a syntax
  error (E2010).
  Fix: bind the region to a local first (`let fresh = Payload.create(1) as isolated Payload`) and pass the local.

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
