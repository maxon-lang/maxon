# WHERE THIS HOST'S MAXON BINARIES ARE — written ONCE, for every script that drives one.
#
# ⛔ THE FACT THIS FILE HOLDS IS "AN EXECUTABLE IS CALLED `foo.exe` ON WINDOWS AND `foo` EVERYWHERE
#    ELSE", written ONCE so no script derives it again.
#
#    Its live consumers are `cross-target-gate.sh`, `output-lock-gate.sh`, `self-host-ab.sh` and
#    `stale-binary-gate.sh`. Each of them used to derive the fact in its own spelling — ONE FACT, FOUR
#    DECLARATIONS — which is the shape this file exists to collapse.
#
#    ⚠ HISTORY, and the reason the collapse was worth doing: measured 2026-08-03 on arm64-macOS, the two
#    scripts that drove the (since retired) rung process had NO such derivation. `rung-start.sh` tested
#    `[ -x "$WORKTREE/bin/maxon.exe" ]`, found nothing on a worktree holding a perfectly good
#    `bin/maxon`, printed
#
#        ⚠ no bootstrap in the worktree — skipping the shv2 build
#
#    …and declared the rung claimed and isolated — with the next step being "gate it", against a worktree
#    with NO COMPILER IN IT. `rung-finish.sh` was worse: seven hard-coded `.exe` paths, so on that host
#    not one gate in the battery could run. Both scripts were retired with the slice board on 2026-09-01;
#    the lesson outlived them, which is why it is recorded here rather than deleted with them.
#
# ⇒ Source this, use `$MAXON_EXE_EXT`, or the two path helpers, and never write `.exe` in a script
#   again. Paths are returned, not echoed with a prefix, so a caller can `[ -x ]` them.

case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT) MAXON_EXE_EXT=".exe"; MAXON_HOST_IS_WINDOWS=1 ;;
	*)                               MAXON_EXE_EXT="";     MAXON_HOST_IS_WINDOWS=0 ;;
esac

# The C# bootstrap in <tree>. `bin/` is GITIGNORED, so a fresh worktree has none until it is copied in
# — which is a different failure from "this host spells it differently", and a caller that tests the
# path this returns can tell them apart by looking at the tree.
maxon_bootstrap_path() { printf '%s/bin/maxon%s' "${1:-.}" "$MAXON_EXE_EXT"; }

# The shv2 compiler in <tree>. Also gitignored, and built BY the bootstrap.
maxon_shv2_path() { printf '%s/maxon-shv2/.maxon/maxon-shv2%s' "${1:-.}" "$MAXON_EXE_EXT"; }
