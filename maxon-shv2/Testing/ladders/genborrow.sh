#!/usr/bin/env bash
# E3070 BORROW LIVENESS — the four per-function shapes `ScaleCorpus` cannot drive past K = 1.
#
# ⚠ **THE CORPUS IS NOT BLIND HERE — IT IS NARROW, WHICH IS WORSE, BECAUSE IT READS AS COVERAGE.**
# It DOES mint borrows: `ScaleCorpus`'s `v_marray.maxon` holds exactly ONE function
# (`scaleManagedArrayOps`) and exactly one `let mgN = try msN.get(0) otherwise ""` per managed-Array
# group — 4 borrows over 8 subjects at rung 0, doubling with the knob. So `BorrowCheck` runs, builds
# its buckets, and reports a healthy-looking non-zero at every rung. But:
#
#   • EVERY SUBJECT CARRIES EXACTLY ONE BORROW, so the resolver's per-site walk has degree 1 and its
#     sites × borrows term is structurally unreachable — the `dead` mode below is the only way to see it;
#   • EVERY ACCESSOR IS BOUND to a name, so the pending claim list is emptied at every statement and
#     never accumulates — the `pending` mode below is the only way to see it;
#   • NO `for`-in AT ALL (the manifest says so), so the lexical half of the rule is untouched.
#
# The one shape it does reach is the DISTINCT-SUBJECT scan, at 8 → 256 subjects in that one body.
# ⇒ A non-zero from this corpus is not evidence that a per-function borrow structure scales. This
# ladder is the instrument for that, and it is the twin of `genfor.sh` one rung on.
#
# THE QUESTIONS, one per mode, and they are four DIFFERENT algorithmic shapes:
#
#   • pending  — UNCLAIMED PENDING BORROWS. `s = try arr.get(0) otherwise ""` (an ASSIGNMENT, not a
#                binding) mints a `PendingBorrow` that NO name ever claims.
#                ✅ FOUND A QUADRATIC AND CLOSED IT: nothing dropped the entry until the function's
#                `end`, and `retargetPendingBorrow` / `attachPendingBorrows` both re-walked the list
#                every time — +0.44M +0.48M +2.02M +7.74M +24.2M ticks over the parent at
#                K = 64…1024, ×4.2 ×3.8 ×3.1. `parseStatements` now cuts back to a per-statement
#                WATERMARK; after, +0.36M +0.82M +1.59M +3.96M +4.77M, linear.
#   • subjects — DISTINCT STORAGES. `Parser.borrowSubjectIdOf` is a LINEAR SCAN of
#                `BorrowFacts.subjects`, entered once per mint and once per recorded write, so K
#                distinct borrowed-and-written arrays in ONE body is O(K²).
#                ◑ MEASURED AND LEFT: ×3.99 ×2.23 ×2.66 ×3.08. Fitting aK + bK² over the top two
#                rungs gives a = 15,697 and b = 36.2 ticks, so the quadratic term does not equal
#                the linear one below K ≈ 434 distinct subjects in ONE body. Both closures cost
#                every program something (8 bytes on every `VarInfo`, or a per-function Map).
#   • dead     — BORROWS × SITES. `BorrowCheck.reportFirstLiveBorrow` walks every borrow of the
#                subject a site writes. The borrows are all DEAD (never read again, so NLL expires
#                them at their own activation) so no diagnostic fires and every walk runs to the
#                end — the resolver's worst case, K borrows × K sites.
#                ✅ FOUND A QUADRATIC AND CLOSED IT: +0.40M +1.53M +5.85M +23.7M +94.3M ticks,
#                ×3.8 ×3.8 ×4.1 ×4.0 — the cleanest quadratic of the four. A per-subject cursor
#                (deadness is monotone in the site token) made it O(borrows + sites); after,
#                +55k +82k +162k +249k +103k, no growth.
#   • noborrow — THE CONTROL, and it is the reading that matters most: the same array-method calls
#                through the same doors with NOT ONE borrow in the program (a TRIVIAL element type,
#                so `emitArrayElementAccessor` mints nothing). It is what the feature costs a
#                program that never uses it — the shape of the defect P1.7a 2b-i found, and the
#                shape `genfor.sh`'s own `noloop` mode exists for. A per-access tax hides inside a
#                borrowing ladder and stands out here.
#                READS: allocations IDENTICAL TO THE DIGIT at every K — the shared-empty
#                `BorrowFacts` is never copy-on-written and nothing is recorded — while parse CPU
#                is +5.5% +5.2% +7.0% +5.7%. Gating the per-call `borrowSubjectNameAt` derivation
#                (the one candidate with a sound gate) was measured and REJECTED: -19k +120k -228k
#                -618k, sign-flipping, i.e. inside the band. The rest is spread over several
#                constant-factor additions no per-function CPU attribution exists to separate.
#
# EVERYTHING GOES IN ONE FUNCTION, deliberately: every term this rung added is PER-FUNCTION, so a
# ladder that widened the function COUNT would divide K by the very thing it doubles and read x2 for
# a quadratic. K doubles inside one body; the ladder doubles the program at the same time, so
# rung-over-rung x2.00 is linear and x4.00 is quadratic exactly as `scale-test` reads.
#
# Usage: genborrow.sh <K> <pending|subjects|dead|noborrow> <out>
#   e.g. genborrow.sh 256 pending a.maxon   and   genborrow.sh 512 pending b.maxon
#        genborrow.sh 512 noborrow c.maxon  is the same access count with no borrow at all.
#
# Compile-only: `main` never calls into the generated body with a populated array, and this ladder
# is about COMPILE cost — `scale-test` never runs its corpus either.
set -euo pipefail
K="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  pending|subjects|dead|noborrow) ;;
  *) echo "genborrow.sh: mode must be pending, subjects, dead or noborrow (got '$MODE')" >&2; exit 2 ;;
esac
if [ "$K" -lt 1 ]; then echo "genborrow.sh: K must be >= 1" >&2; exit 2; fi

{
  echo "// borrow ladder: K=$K, mode $MODE"
  echo "typealias Int = int(i64.min to i64.max)"
  echo "typealias StringArray = Array with String"
  echo "typealias IntArray = Array with Int"
  echo ""
  echo "function fn() returns Int"

  case "$MODE" in
    pending)
      # An ASSIGNMENT from an element takes no borrow the parser can complete — `attachPendingBorrows`
      # runs only at a BINDING — so every one of these leaves a `PendingBorrow` behind for the rest of
      # the body. The `try … otherwise <value>` is what drives `retargetPendingBorrow` over it.
      echo -e "\tvar arr = StringArray.create()"
      echo -e "\tvar s = \"\""
      echo -e "\tvar total = 0"
      n=0
      while [ "$n" -lt "$K" ]; do
        echo -e "\ts = try arr.get(0) otherwise \"\""
        echo -e "\ttotal = total + s.byteLength()"
        n=$(( n + 1 ))
      done
      echo -e "\treturn total"
      ;;
    subjects)
      # K DISTINCT storages, each borrowed once and written once: 2K entries into the subject scan,
      # over a list that reaches length K.
      #
      # ⚠ **EACH ONE LIVES IN ITS OWN BLOCK, AND THAT IS A FACT ABOUT REGISTERS, NOT ABOUT THE SCAN**
      # — the same cap `genfor.sh`'s depth knob documents. K arrays declared side by side in one frame
      # are all live to the function's `end` (each owes a drop there), and the allocator REFUSES at
      # K=128 rather than spill (E5001). A block gives each one a scope to die in while leaving the
      # per-FUNCTION subject list, which is never reset, at full length K.
      echo -e "\tvar seed = 1"
      n=0
      while [ "$n" -lt "$K" ]; do
        echo -e "\tif seed > 0 'b${n}'"
        echo -e "\t\tvar a${n} = StringArray.create()"
        echo -e "\t\tlet s${n} = try a${n}.get(0) otherwise \"\""
        echo -e "\t\ta${n}.clear()"
        echo -e "\tend 'b${n}'"
        n=$(( n + 1 ))
      done
      echo -e "\treturn seed"
      ;;
    dead)
      # ONE storage, K borrows and K writes. Every borrow is dead at every write (nothing reads the
      # name again), so no E3070 fires and the resolver's walk runs to the end each time — which is
      # the worst case, and the only one a ladder should measure.
      echo -e "\tvar arr = StringArray.create()"
      n=0
      while [ "$n" -lt "$K" ]; do echo -e "\tlet s${n} = try arr.get(0) otherwise \"\""; n=$(( n + 1 )); done
      n=0
      while [ "$n" -lt "$K" ]; do echo -e "\tarr.clear()"; n=$(( n + 1 )); done
      echo -e "\treturn 0"
      ;;
    noborrow)
      # THE CONTROL. A TRIVIAL element type mints no borrow whatever the method, so `borrows.count()`
      # is 0 for this whole body and every gate this rung added answers false — while the receiver
      # still travels all the doors (`parseArrayMethodCall` derives a borrow subject name at each).
      echo -e "\tvar arr = IntArray.create()"
      echo -e "\tvar total = 0"
      n=0
      while [ "$n" -lt "$K" ]; do
        echo -e "\tarr.push(${n})"
        echo -e "\ttotal = total + arr.count()"
        n=$(( n + 1 ))
      done
      echo -e "\treturn total"
      ;;
  esac

  echo "end 'fn'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\treturn 0 if fn() >= 0 else 1"
  echo "end 'main'"
} > "$OUT"
