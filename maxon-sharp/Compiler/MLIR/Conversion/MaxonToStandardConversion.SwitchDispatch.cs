using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

/// <summary>
/// ⭐ THE ONE PLACE a `match`'s dispatch strategy is chosen.
///
/// The lowering hands down a <see cref="MaxonSwitchOp"/> — a sorted, disjoint list of
/// `(lo, hi) -> arm` intervals plus a default — and says nothing about how to test them. This
/// file decides between a linear compare chain, a jump table, and a binary search, and it is
/// the only file that may. There used to be no such place: the lowering emitted a chain and a
/// later pass tried to recognize a switch back out of it, which meant the shape of a match was
/// written down twice and the recognizer's guards (zero-based ordinals only, `eq` predicates
/// only) were the seams between the two copies.
///
/// ⚠ EVERY THRESHOLD BELOW IS A CONTRACT, not a tuning knob picked by taste — the shv2 compiler
/// will copy these rules, so each one is named and carries the reason it has the value it has.
/// </summary>
public static partial class MaxonToStandardConversion {
  /// Below this many disjoint intervals, testing them one at a time is cheaper than any
  /// dispatch structure: three intervals is at most three compare-and-branch pairs, while a
  /// table costs a bounds check plus a load and an indirect jump (which no branch predictor
  /// likes), and a binary search costs a compare per level before it even reaches a leaf.
  ///
  /// 4 is also the threshold the recognizer this replaced used, so every match that already
  /// formed a jump table still forms one.
  private const int MinIntervalsForDispatch = 4;

  /// A jump table spends one 4-byte .rdata slot per value in `[min, max]`, covered or not, so
  /// the span — not the number of arms — is what has to be capped. 4096 slots is a 16 KiB
  /// table: past that, a binary search's handful of compares beats paying a cache miss on a
  /// table that mostly holds the default label.
  private const int MaxTableSpanSlots = 4096;

  /// Minimum share of the span that must actually be covered for a table to be worth its slots,
  /// as an exact fraction (2/5 = 40%) so the test is integer arithmetic and cannot drift with
  /// floating-point rounding. Below it the table is mostly default-label padding, and a binary
  /// search — whose cost follows the interval COUNT, not the span — wins.
  private const int MinTableDensityNumerator = 2;
  private const int MinTableDensityDenominator = 5;

  /// Slots a table may spend per interval it removes a compare for.
  ///
  /// Density alone cannot judge a table, because the two sides of the trade scale with
  /// different quantities: a table COSTS one slot per value in the span, but only BUYS the
  /// compares it replaces — and that is the interval COUNT, not the covered-value count. A
  /// plan of a few WIDE arms (`1 to 2000`, `3000 to 4000`, `5`, `7`) is 75% dense and well
  /// inside the span cap, yet trades 16 KiB of mostly-identical slots for the four compares a
  /// binary search would have done in two levels.
  ///
  /// 32 slots (a 128-byte cache-line pair) per compare removed is the budget. It keeps the
  /// cases where a wide arm genuinely pays — `specs/match-statements` `grade`, 5 range arms
  /// covering 0…100, is 101 slots against a budget of 160 — and rejects the degenerate one
  /// above, which would need 1000.
  private const int MaxTableSlotsPerInterval = 32;

  /// <summary>
  /// Lowers one <see cref="MaxonSwitchOp"/>. The scrutinee is loaded ONCE, into
  /// <paramref name="block"/>, and every comparison the strategy emits reads that one value.
  ///
  /// The dispatch blocks are spliced in directly after <paramref name="block"/> rather than
  /// appended, because x64 lowers `cf.cond_br` as "jump to ELSE, fall through to THEN": a
  /// cond_br's then-target must be the physically next block. Emitting in pre-order and
  /// inserting the result as one run is what keeps that true for every branch built here.
  /// </summary>
  private static void LowerSwitch(MaxonSwitchOp switchOp, IrFunction<StandardOp> newFunc,
      IrBlock<StandardOp> block) {
    var scrutinee = new StdLoadI64Op(switchOp.ScrutineeVarName);
    block.AddOp(scrutinee);

    var dispatch = new DispatchEmitter(switchOp.DispatchLabelPrefix, scrutinee.Result,
      switchOp.Intervals, switchOp.DefaultBlock);
    dispatch.Emit(block, 0, switchOp.Intervals.Count);

    newFunc.Body.Blocks.InsertRange(newFunc.Body.Blocks.IndexOf(block) + 1, dispatch.EmittedBlocks);
  }

  /// Builds the dispatch for one plan. Blocks are collected in the order they must be laid out
  /// and handed back for the caller to splice in; nothing here touches the function's block list
  /// directly, so the layout invariant is honoured in exactly one statement.
  private sealed class DispatchEmitter(
      string labelPrefix, StdI64 scrutinee,
      List<MaxonSwitchInterval> intervals, string defaultBlock) {
    public List<IrBlock<StandardOp>> EmittedBlocks { get; } = [];
    private int _nextLabelId;

    private string NextLabel() => $"{labelPrefix}.dispatch{_nextLabelId++}";

    private IrBlock<StandardOp> AddBlock(string label) {
      var block = new IrBlock<StandardOp>(label);
      EmittedBlocks.Add(block);
      return block;
    }

    /// Emits the dispatch for `intervals[from..to)` into <paramref name="block"/>.
    public void Emit(IrBlock<StandardOp> block, int from, int to) {
      int count = to - from;
      if (count < MinIntervalsForDispatch) {
        EmitLinearChain(block, from, to);
        return;
      }
      if (TryTableBias(from, to, out long bias, out int span)) {
        EmitTable(block, from, to, bias, span);
        return;
      }

      // Split on the median interval's lower bound. The intervals are sorted and disjoint, so
      // everything below the pivot lies in the left half and everything at or above it in the
      // right. Both halves are non-empty because count is at least MinIntervalsForDispatch,
      // which is what makes the recursion terminate.
      int mid = from + count / 2;
      var pivot = new StdConstI64Op(intervals[mid].Lo);
      block.AddOp(pivot);
      var below = new StdCmpI64Op("lt", scrutinee, pivot.Result);
      block.AddOp(below);

      // Both labels are minted before either block exists, so the left subtree can emit all of
      // its own blocks between this branch and the right half — which is what puts the
      // then-target physically next.
      string lowerLabel = NextLabel();
      string upperLabel = NextLabel();
      block.AddOp(new StdCondBrOp(below.Result, lowerLabel, upperLabel));

      Emit(AddBlock(lowerLabel), from, mid);
      Emit(AddBlock(upperLabel), mid, to);
    }

    /// A jump table indexes from zero, so the plan is biased by its own minimum: `sub` first,
    /// then index. That bias is the whole reason a dense case set no longer has to start at 0.
    private void EmitTable(IrBlock<StandardOp> block, int from, int to, long bias, int span) {
      var index = scrutinee;
      if (bias != 0) {
        var biasConst = new StdConstI64Op(bias);
        block.AddOp(biasConst);
        var biased = new StdSubI64Op(scrutinee, biasConst.Result);
        block.AddOp(biased);
        index = biased.Result;
      }

      // Every slot in [min, max] needs a target; the ones no interval covers are the default.
      // A range arm and an `or`-list simply fill several slots each — that is the entire fix for
      // a single range arm having previously forced the whole match onto a compare chain.
      var caseTargets = new string[span];
      Array.Fill(caseTargets, defaultBlock);
      for (int i = from; i < to; i++) {
        var interval = intervals[i];
        // Walk SLOT indices, not values: the last slot is below span, whereas a value-driven
        // loop over an interval ending at i64.max would never terminate.
        long lastSlot = unchecked(interval.Hi - bias);
        for (long slot = unchecked(interval.Lo - bias); slot <= lastSlot; slot++) {
          caseTargets[slot] = interval.TargetBlock;
        }
      }

      block.AddOp(new StdSwitchOp(index, caseTargets, defaultBlock));
    }

    /// Tests the intervals one at a time. Each test's "no match" continuation is the physically
    /// next block and its "match" target is the arm — the opposite polarity from the comparison
    /// chain this replaced, which is what lets the arm bodies live anywhere in the function
    /// while the x64 fall-through rule still holds for every branch built here.
    private void EmitLinearChain(IrBlock<StandardOp> block, int from, int to) {
      var current = block;
      for (int i = from; i < to; i++) {
        var interval = intervals[i];
        var missed = EmitIntervalMissTest(current, interval);
        string nextLabel = NextLabel();
        current.AddOp(new StdCondBrOp(missed, nextLabel, interval.TargetBlock));
        current = AddBlock(nextLabel);
      }
      current.AddOp(new StdBrOp(defaultBlock));
    }

    /// Emits the condition "the scrutinee is NOT in this interval". A single value is a plain
    /// `ne`; a range is one biased unsigned compare — `(v - lo) u> (hi - lo)` — which is correct
    /// for every interval including the open-ended ones, because both the subtraction and the
    /// comparison are modular: `min to i64.max` becomes `(v - i64.min) u> ~0`, which is never
    /// true, and that is exactly the arm that matches everything.
    private StdBool EmitIntervalMissTest(IrBlock<StandardOp> block, MaxonSwitchInterval interval) {
      if (interval.Lo == interval.Hi) {
        var value = new StdConstI64Op(interval.Lo);
        block.AddOp(value);
        var notEqual = new StdCmpI64Op("ne", scrutinee, value.Result);
        block.AddOp(notEqual);
        return notEqual.Result;
      }

      var index = scrutinee;
      if (interval.Lo != 0) {
        var loConst = new StdConstI64Op(interval.Lo);
        block.AddOp(loConst);
        var biased = new StdSubI64Op(scrutinee, loConst.Result);
        block.AddOp(biased);
        index = biased.Result;
      }

      var width = new StdConstI64Op(unchecked(interval.Hi - interval.Lo));
      block.AddOp(width);
      var above = new StdCmpU64Op("ugt", index, width.Result);
      block.AddOp(above);
      return above.Result;
    }

    /// True when `intervals[from..to)` is dense enough to be worth a jump table, reporting the
    /// bias (its minimum) and the number of slots the table needs.
    private bool TryTableBias(int from, int to, out long bias, out int span) {
      bias = intervals[from].Lo;
      span = 0;

      // Unsigned so that a span crossing zero — or reaching i64.min / i64.max from an
      // open-ended arm — is measured rather than overflowed. Rejecting on the WIDTH rather
      // than on the bounds keeps a narrow arm at the very edge of the range tabulatable.
      ulong width = unchecked((ulong)intervals[to - 1].Hi - (ulong)bias);
      if (width >= MaxTableSpanSlots) return false;

      span = (int)width + 1;

      // The table must earn its slots against the compares it removes, not merely against the
      // values it covers — see MaxTableSlotsPerInterval.
      if ((long)span > (long)(to - from) * MaxTableSlotsPerInterval) return false;

      long coveredSlots = 0;
      for (int i = from; i < to; i++) {
        coveredSlots += intervals[i].Hi - intervals[i].Lo + 1;
      }

      return coveredSlots * MinTableDensityDenominator >= (long)span * MinTableDensityNumerator;
    }
  }
}
