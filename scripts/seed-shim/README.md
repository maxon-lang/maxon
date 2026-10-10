# Seed shims

Patches that withdraw, from the first build only, a declaration the previous published release refuses.

`scripts/build-from-seed.sh` applies every `*.patch` here — in sorted order, over a clean worktree —
when a plain build with the seed fails, and restores the files it touched before the second build runs.
`docs/RELEASING.md`'s "Maxon compiles Maxon, so a release needs a release" says why that is sound and
when a patch belongs here.

⛔ **A patch never withdraws the compiler's own DECLARATION of an entry** — its tables are what teach `C1`
the entry, so withdrawing them would leave nothing able to build the tree. That covers a `stdlib/` or
`runtime/` declaration the seed refuses, and it covers the parser arm, the runtime installer and the name
constants of a new `__Builtins` intrinsic. An error CASE the seed's runtime cannot raise is not an entry,
and the fifth case below withdraws it.

⭐ **A CALL SITE IN COMPILER SOURCE IS THE OTHER CASE, AND IT IS ADMITTED.** The compiler is itself a Maxon
program, so it can call an intrinsic this tree adds and no published release knows — and the seed then
refuses the tree with E3004 at that call, not at the declaration. Withdrawing the call leaves every table
standing, so `C1` knows the intrinsic and compiles the unshimmed tree into `C2`; the first build is merely
one whose instrument reports nothing. Keep such a patch to the single function that calls, so what the
shimmed build loses is stated by the patch itself.

⭐ **A VISIBILITY TIER IS THE THIRD CASE, AND IT SPANS MANY DECLARATIONS.** A rule about what a
signature may name decides a declaration's tier, so a release that predates the rule audits those tiers
by the older one and refuses the tree at every declaration the new rule widened — E3092/E3093/E3094, not
E3004, and one finding per declaration rather than one call site. Such a patch withdraws the MODIFIER and
nothing else: the declarations, their bodies and every table stay, which is what leaves `C1` able to
compile the unshimmed tree.

⭐ **A LANGUAGE FORM THE SEED CANNOT PARSE IS THE FOURTH CASE, AND ITS PATCH IS GENERATED.** A clause
such as `typealias X = int(range) implements Parent` is an E2001 to a release that predates it, and
withdrawing it is not enough: the seed then judges every crossing the clause licensed by its older rule,
so the patch also writes each cast that rule demands, and wraps a division whose divisor such a cast widened to
admit 0 in the `try … otherwise panic` the seed then requires. A generic function (`function f(...) uses T`) is
withdrawn whole, which is sound because the compiler calls none. A type-body field whose type comes from its
initializer (`var x = e as T`, `var x = Type.member(...)`, `var x = "text"`) is written `var x as T = e`, and a
decimal integer literal above `i64.max` is written in hex (a typealias range bound as `i64.max`). Only those
forms and the casts go; the declarations and every table stay, so `C1` knows the forms and compiles the unshimmed tree. The casts are
found by building with the seed until it is clean, so the one patch for every such form is written by
`generate-seed-language-shim.py` and not by hand — regenerate it whenever the sources it touches move.
The seed also compiles a comparison against a literal from 2^63 up as a signed one, so a `C1` it builds derives
wrong division constants and panics in `deriveSignedMagic`; the hand-written `0015-c1-declines-every-division-strength-reduction.patch`
makes `classifyDivision` decline every divisor, so `C1` emits plain divides and builds the unshimmed tree into a correct `C2`.

⭐ **THE LIBRARY LINKED INTO `C1` IS THE FIFTH CASE.** The first build compiles the tree's `stdlib/` into
`C1` as it would into any program, so a library body that calls a `__ManagedFile` method or a `__Builtins`
intrinsic the seed does not know is refused there, and so is a `__ManagedFileError` case the seed's runtime
has no flag for. Such a patch rewrites the library BODY to calls the seed knows, and withdraws the error cases
that body cannot produce together with the arms that name them. Only `C1`'s own behaviour is weaker:
its `File.createText` truncates an existing file, its `File.readText` reads through a copy, and its
`Random.draw` answers 0, so the tree lock it takes rests on a claim file any command may overwrite and waits
that are always the shortest. The restore runs before the second build, so `C1` compiles `C2` against the
unshimmed library.

⛔ **Delete a patch once a release has shipped the compiler that accepts what it withdraws.** It is dead
from that moment — the plain build succeeds and nothing here is read — and a patch that no longer
applies turns the next genuine refusal into a confusing failure.
