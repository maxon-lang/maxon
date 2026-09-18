# Seed shims

Patches that withdraw, from the first build only, a declaration the previous published release refuses.

`scripts/build-from-seed.sh` applies every `*.patch` here — in sorted order, over a clean worktree —
when a plain build with the seed fails, and restores the files it touched before the second build runs.
`docs/RELEASING.md`'s "Maxon compiles Maxon, so a release needs a release" says why that is sound and
when a patch belongs here.

⛔ **A patch never withdraws the compiler's own DECLARATION of an entry** — its tables are what teach `C1`
the entry, so withdrawing them would leave nothing able to build the tree. That covers a `stdlib/` or
`runtime/` declaration the seed refuses, and it covers the parser arm, the runtime installer and the name
constants of a new `__Builtins` intrinsic.

⭐ **A CALL SITE IN COMPILER SOURCE IS THE OTHER CASE, AND IT IS ADMITTED.** The compiler is itself a Maxon
program, so it can call an intrinsic this tree adds and no published release knows — and the seed then
refuses the tree with E3004 at that call, not at the declaration. Withdrawing the call leaves every table
standing, so `C1` knows the intrinsic and compiles the unshimmed tree into `C2`; the first build is merely
one whose instrument reports nothing. Keep such a patch to the single function that calls, so what the
shimmed build loses is stated by the patch itself.

⛔ **Delete a patch once a release has shipped the compiler that accepts what it withdraws.** It is dead
from that moment — the plain build succeeds and nothing here is read — and a patch that no longer
applies turns the next genuine refusal into a confusing failure.
