# Seed shims

Patches that withdraw, from the first build only, a declaration the previous published release refuses.

`scripts/build-from-seed.sh` applies every `*.patch` here — in sorted order, over a clean worktree —
when a plain build with the seed fails, and restores the files it touched before the second build runs.
`docs/RELEASING.md`'s "Maxon compiles Maxon, so a release needs a release" says why that is sound and
when a patch belongs here.

⛔ **A patch withdraws a `stdlib/` or `runtime/` declaration and never compiler source.** The compiler's
own tables are what teach `C1` the entry; withdrawing them would leave nothing able to build the tree.

⛔ **Delete a patch once a release has shipped the compiler that accepts what it withdraws.** It is dead
from that moment — the plain build succeeds and nothing here is read — and a patch that no longer
applies turns the next genuine refusal into a confusing failure.
