# Hand-built ladders — for the costs `scale-test` structurally cannot see

`scale-test` is the standing instrument and the thing whose trend `docs/optimization-log.md`
records. **These generators are not a replacement for it.** They exist because a scale corpus is
a list of shapes someone thought to generate, and every optimizer pass so far has had to build a
throwaway ladder to answer a question the corpus could not express — then thrown it away, so the
next pass built it again and the number in the log could never be re-run by anyone.

That last part is not hypothetical. The Wave 2 rung reported a headline of
`404,409 → 2,218,086` base bytes and recorded **no ladder**; when the optimizer rebuilt one, the
base column came out **~60× larger**. The rung had measured a different program, and nobody could
tell, because there was nothing to re-run. **A headline nobody can re-run is not a result.**

## The generators

Each writes one self-contained `.maxon` program to `<outfile>`. All four call an opaque
`scaleOpaque(a int)` the optimizer cannot see through, so the call is a real clobber point.

| script | shape | the question it answers |
|---|---|---|
| `gen.sh <N> <float\|int> <out>` | N units, **one** value live across one call | the Wave 2 headline: is a cross-call float force-spilled? The `int` form is the **control** — it must stay bit-identical across a change that only touches the float path. |
| `gen12.sh <N> <floatsPer> <out>` | N units, `floatsPer` floats live across one call | overflow **past** the callee-saved XMM half. With `floatsPer > 10` the spill is forced, so the splitter really runs and `growValueSpace` mints ids on every split. |
| `gen12i.sh <N> <intsPer> <out>` | the same, with ints | its control, and the one that shows the single-basic-block splitting quadratic is **file-agnostic**. |
| `genloop.sh <loops> <floatsPer> <out>` | the exact shape `ScaleCorpus.floatSpillSource` generates | ties a hand-ladder reading back to the corpus's own shape — the check that a pathological ladder is not being mistaken for the realistic case. |

## Reading one

```
<compiler> build out.maxon -o out --metrics=m.txt
awk -F'\t' '$1=="regalloc" && $2=="splitting"' m.txt      # nanos, allocs, frees, bytes
```

Compare a **base** binary against **head** at equal-length checkout paths — path length is a real
constant in the byte column (two runs of identical content once differed by exactly 546 bytes
because one worktree path was 14 characters longer).

## What a reading means

**The ladder DOUBLES, so the ratio between consecutive rungs IS the growth**: ×2.00 linear,
×4.00 quadratic, read straight off. Do not fit an exponent — the doubling already gives you the
answer, and a fit is interpretation dressed as measurement.

⚠ **Allocations and bytes are bit-for-bit reproducible; time is not.** Time is worth reading
interactively (a quadratic in *time* with linear allocations is exactly the shape `scale-test` is
blind to, and two have been found that way), but never trend it — a loaded box in July against an
idle one in August compares nothing.

⚠ **These ladders are pathological on purpose, and that cuts both ways.** A curve that bends here
is a real cost the compiler can be made to pay, but it is not automatically a cost anyone pays:
the single-basic-block splitting quadratic filed against P1.7 bends at ×3.99 on `gen12i.sh` while
`genloop.sh` — the corpus's own shape — reads ×2.02 out to 256 loops. **Report both**, and say
which one a real program looks like.
