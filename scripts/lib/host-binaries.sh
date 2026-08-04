# WHERE THIS HOST'S MAXON BINARIES ARE — written ONCE, for every script that drives one.
#
# ⛔ THE FACT THIS FILE HOLDS IS "AN EXECUTABLE IS CALLED `foo.exe` ON WINDOWS AND `foo` EVERYWHERE
#    ELSE", AND IT WAS WRITTEN IN FOUR SCRIPTS AND MISSING FROM THE TWO THAT RUN THE RUNG PROCESS.
#
#    Measured 2026-08-03 on arm64-macOS, and it is why this file exists: `scripts/rung-start.sh`
#    tested `[ -x "$WORKTREE/bin/maxon.exe" ]`, found nothing on a worktree that had a perfectly good
#    `bin/maxon` in it, and printed
#
#        ⚠ no bootstrap in the worktree — skipping the shv2 build
#
#    …then declared the rung CLAIMED AND ISOLATED. The next step in `SKILL.md` is "gate it", against a
#    worktree with NO COMPILER IN IT. `scripts/rung-finish.sh` was worse: seven hard-coded `.exe`
#    paths, so on this host not one gate in the battery could run at all.
#
#    Three other scripts already knew — `cross-target-gate.sh`, `output-lock-gate.sh` and
#    `stale-binary-gate.sh` each derive it, each in their own spelling. That is the board's `G3` one
#    tier down: ONE FACT, FOUR DECLARATIONS, and the two consumers that needed it most had none.
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
