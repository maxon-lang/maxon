# Seed shims

Patches that withdraw, from the first build only, a declaration the pinned seed (`scripts/seed-tag`) refuses.

`scripts/build-from-seed.sh` applies every `*.patch` here — in sorted order, over a clean worktree —
when a plain build with the seed fails, and restores the files it touched before the second build runs.
`docs/RELEASING.md`'s "A tree can declare something its own seed refuses, and a shim is how it is still built" says why that is sound and
when a patch belongs here.

⛔ **A patch never withdraws the compiler's own DECLARATION of an entry** — its tables are what teach `C1`
the entry, so withdrawing them would leave nothing able to build the tree. That covers a `stdlib/` or
`runtime/` declaration the seed refuses, and it covers the parser arm, the runtime installer and the name
constants of a new `__Builtins` intrinsic. An error CASE the seed's runtime cannot raise is not an entry,
and the fifth case below withdraws it.

⭐ **A CALL SITE IN COMPILER SOURCE IS THE OTHER CASE, AND IT IS ADMITTED.** The compiler is itself a Maxon
program, so it can call an intrinsic this tree adds and the pinned seed does not know — and the seed then
refuses the tree with E3004 at that call, not at the declaration. Withdrawing the call leaves every table
standing, so `C1` knows the intrinsic and compiles the unshimmed tree into `C2`; the first build is merely
one whose instrument reports nothing. Keep such a patch to the single function that calls, so what the
shimmed build loses is stated by the patch itself. A method call on an enum a closure captured is the same case
in a different spelling: a seed that predates it refuses the call with E2015 at the call site, the patch routes it
through a module function the seed can compile, and `C1` knows the construct.

⭐ **A VISIBILITY TIER IS THE THIRD CASE, AND IT SPANS MANY DECLARATIONS.** A rule about what a
signature may name decides a declaration's tier, so a seed that predates the rule audits those tiers
by the older one and refuses the tree at every declaration the new rule widened — E3092/E3093/E3094, not
E3004, and one finding per declaration rather than one call site. Such a patch withdraws the MODIFIER and
nothing else: the declarations, their bodies and every table stay, which is what leaves `C1` able to
compile the unshimmed tree.

⭐ **A LANGUAGE FORM THE SEED CANNOT PARSE IS THE FOURTH CASE, AND ITS PATCH IS GENERATED.** A clause
such as `typealias X = int(range) implements Parent` is an E2001 to a seed that predates it, and
withdrawing it is not enough: the seed then judges every crossing the clause licensed by its older rule,
so the patch also writes each cast that rule demands, and wraps a division whose divisor such a cast widened to
admit 0 in the `try … otherwise panic` the seed then requires. A generic function (`function f(...) uses T`) is
withdrawn whole, which is sound because the compiler calls none. A type-body field whose type comes from its
initializer (`var x = e as T`, `var x = Type.member(...)`, `var x = "text"`) is written `var x as T = e`, and a
decimal integer literal above `i64.max` is written in hex (a typealias range bound as `i64.max`). A
`Compiler.`-qualified name is written bare, which the seed resolves to the same declaration, and an `export` whose
only reach beyond its file was a crossing the patch turned into a cast is withdrawn (E3092). A `readonly`
qualifier on a container's element type (`Array with readonly T`, `Map with (K, readonly V)`) and a `let` on a
union case's payload (`case(let name T)`) are parse errors to a seed that predates them, and the patch
withdraws the qualifier at every declaration, which leaves the container and union types the seed always held.
A `view` binding is written `let`, which the seed reads as a read-only binding of the same value, and a capability
qualifier — `keep` or `sole` on a parameter of a function-type alias or an interface requirement, `returns sole`,
`returns readonly` — is withdrawn. The script withdraws those two on every run, and the older forms above only
with `--older-seed`, for a seed that predates them.
Only those forms and the casts go; the declarations and every table stay, so `C1` knows the forms and compiles the
unshimmed tree. The casts are found by building with the seed for one native target at a time until it is clean,
and again for any lane the tree changed under since its last clean build, so the code a lane's `#if` selects is
judged too. The one patch for every such form is written by
`generate-seed-language-shim.py` and not by hand — regenerate it whenever the sources it touches move, and
after the documenter has written its comments, since a comment edit moves the context a hunk is matched on.
Its hunks are computed over every other patch here, so it sorts LAST: a hand-written patch is numbered below
it and written over the tree with the forms still in place, and the script refuses a patch that sorts after it.

⭐ **THE LIBRARY LINKED INTO `C1` IS THE FIFTH CASE.** The first build compiles the tree's `stdlib/` into
`C1` as it would into any program, so a library body that names a type, a method or a `__Builtins` intrinsic
the seed does not know is refused there, and so is a library error case the seed's runtime has no flag for.
Such a patch rewrites the library BODY to calls the seed knows, and withdraws the error cases that body cannot
produce together with the arms that name them. The restore runs before the second build, so `C1` compiles
`C2` against the unshimmed library, and only `C1`'s own behaviour is weaker.

`0001-withdraw-the-path-resolution-from-the-first-build.patch` is the standing example. The seed knows no
`__ManagedDirectory.resolve` and no `__ManagedDirectoryError.resolveFailed`, so the patch writes
`FilePath.resolvedPhysically` as the path itself, refusing only an empty one, and withdraws the case.

⭐ **A RULE THE TREE HAS DELETED AND THE SEED STILL ENFORCES IS THE SIXTH CASE, AND ITS REWRITES ARE
GENERATED WITH THE FOURTH'S.** With the forms withdrawn the seed judges the tree by its older rules, and refuses
sites the tree is right to write: a `let` that holds a collection's element across a call that mutates the
collection (E3070), a loop it reads as still borrowing what its body mutates (E3070), a loop element assigned
through (E2013), a `var` it sees no assignment to because the tree counts a hand-off to a mutating parameter as
one (E3077), an immutable value bound, assigned, passed or stored where it allows only a mutable one (E3078), and
a `var` it reads as moved by the `let` a `view` of it became (E3102). The patch writes each site the way the
older rule accepts: the `let` is bound to a `.clone()` of what it held; the loop iterates a clone held in a
local, only where the script proves the body leaves the collection unmutated; the loop becomes an index loop over
a `var` alias of the element it reads; the `var` is a `let`; the value gets the `.clone()` the seed asks for; and
the moved `var` is a `let` the seed aliases, where nothing reassigns it. An E3070 at a `let` that was a `view`
stops the script, naming the site: a clone would stop the view seeing the writes the tree lets it observe. These rewrites exist only in `C1`, which the restore removes before `C2` is built, and what
stands behind them is the fixed point rather than an argument: `scripts/fixpoint.sh` from a compiler this
script built must reproduce the bytes a slot-built compiler reproduces. The script finds the sites by building
with the seed for one native target at a time until it is clean, and again for any lane the tree changed under
since its last clean build, so the code a lane's `#if` selects is judged too. A refusal it cannot rewrite stops it,
naming the site.

The seed's parser ends a file's parse at its first type refusal, so one build reports at most one such site per
file, and a file holding hundreds would cost hundreds of builds. Once a file has been refused at a single site in
two rounds running, the script builds a copy of the tree under the work directory's `view/` in which that file's
top-level functions and the instance methods of its `type` blocks are spread over files of about 600 lines each (a
method moves through an `extension <Type>` block; `static` methods, `__`-named functions and the methods of enums,
unions, interfaces and extensions stay where they are; each literal constant a piece reads is copied into it, and
the file's other declarations that name no visibility are written `module`), so each piece reports its own first
site. Only the casts those builds report are used, written into the real file at the line they came from.

The unsplit build decides. Only the refusals a cast answers (E3005, E2028, E3059, E3010) are read from it. One of
those the script cannot rewrite or cannot place on a line of the real file, or a round in which it changes
nothing, drops the copy for the rest of the lane and is printed, and the unsplit builds decide from there: a
refusal that is real reaches them and stops the script naming the site. Every lane's last build is of the unsplit
tree, so the patch is one the seed builds unsplit for every lane.

A cast the seed asked for at a `get`, `set` or `resize` argument is also written at the other calls of the same
method in the same function whose receiver text and argument text are identical, at calls in code outside strings,
character literals and comments, when every identifier those texts name is bound at most once in the function and
that binding's scope covers both calls. The seed must then refuse each of them the same way, so the cast is the
one it would have asked for. When the seed refuses a line holding such a cast, the cast is taken back, and that
speculation is retired for the file.

⭐ **A CONSTRUCT THE SEED MISCOMPILES IS THE SEVENTH CASE.** A seed that compiles a comparison against a literal
from 2^63 up as a signed one builds a `C1` that derives wrong division constants and panics in
`deriveSignedMagic`. The patch for it makes `classifyDivision` decline every divisor, so `C1` emits plain divides
and builds the unshimmed tree into a correct `C2`. Such a patch is hand-written, and like the second case it
withdraws behaviour in one function and leaves every table standing.

⭐ **A RUNTIME-TIER GEOMETRY THE SEED'S PINS REFUSE IS THE EIGHTH CASE.** The seed checks the geometry
`runtime/SlabRuntime.maxon` restates against its own (`checkSlabRuntimeGeometry`), and its allocator emits the
slow-path lock it was built with, so a tree that changes the lock's width or how the lock is initialised is
refused in the first build. The patch for it writes the tier back to the seed's geometry and lock initialisation
for that build only, so the tier `C1` carries matches the allocator the seed emits into it; the restore runs
first, and `C1` builds `C2` from the tree's geometry. It is `git diff HEAD -R` of the tier file, regenerated after
every edit to that file.

⭐ **A SEND THE SEED READS BY ITS OLDER RULE IS THE NINTH CASE.** The tree's sends fail: a full mailbox answers
`mailboxFull` and a stopped one `serviceStopped`, so every send site is a `try`, a reply-bearing message's promise
names only its own error type, and `stdlib/Builtins.maxon` declares no `ServiceError`. A seed that predates
failable sends refuses `try` on a send (E2015) and expects `ServiceError` in the library. The patch for it writes
every send site back to the seed's form and declares `ServiceError` again. It touches no compiler logic: the
parser, lowering and runtime that teach `C1` failable sends stay, so `C1` compiles the unshimmed tree into `C2`,
and only `C1`'s own services and library send by the seed's rule. Like the eighth case's, it is hand-written over
the committed tree.

⛔ **The commit that pins a new seed deletes every patch.** A patch here is what makes CI freeze, prove and pin
that seed (`docs/RELEASING.md`), and `scripts/pin-seed.sh` deletes them all in the pinning commit once the seed
has built the tree with none. A patch is dead from that moment — the plain build succeeds before anything here
is read — and one left to stop applying turns the next genuine refusal into a confusing failure.
