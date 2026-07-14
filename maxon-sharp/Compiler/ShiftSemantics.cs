namespace MaxonSharp.Compiler;

/// ⭐ THE SHIFT RULE — the canonical statement of what `shl`/`shr` MEAN in this compiler, and the
/// only one. Everything that has to know reads it: the parser (which folds a count it can see, and
/// rejects a negative one), the const-declaration evaluator, and the Maxon→Standard lowering (which
/// builds the machine code for a count it CANNOT see). Nothing restates it.
///
/// Maxon follows **Go**, whose rule is deliberately NOT the hardware's:
///
///   • **The count is NOT MASKED.** "There is no upper limit on the shift count. Shifts behave as if
///     the left operand is shifted n times by 1 for a shift count of n." So `x shl 64` is 0 — every
///     bit has been shifted out — and `x shl 100` is 0 as well. The hardware disagrees: x64 masks a
///     64-bit shift's count to its low 6 bits and arm64's LSLV/ASRV do the same, so `x shl 64`
///     computes `x shl 0` (x, UNCHANGED) and `x shl 100` computes `x shl 36`. That masking is the
///     reason <see cref="Eval"/> and the guarded lowering exist: both must SATURATE where the
///     hardware wraps.
///
///   • **A right shift is ARITHMETIC when the left operand is signed**, logical when it is unsigned.
///     Every Maxon `int` is a signed 64-bit integer, so `shr` sign-propagates: `(0-8) shr 60` is -1,
///     not 15. Only a value whose ranged type is unsigned (`int(0 to u64.max)`) zero-fills.
///
///   • **A negative count is an ERROR**, never a shift the other way. A constant one is a compile
///     error (E2054); a runtime one panics.
///
/// The saturation follows from the two bullets together, and is the whole content of this file:
/// a shift that fills with ZEROS saturates to zero, and a shift that fills with the SIGN saturates
/// to the sign. `x sar 63` IS the sign, which is why an out-of-range arithmetic right shift is
/// expressed as a CLAMP of the count rather than a select of the result.
public static class ShiftSemantics {
  /// The smallest legal shift count. Not a range bound — the ONLY bound. There is no largest.
  public const long MinShiftCount = 0;

  /// The width of the value being shifted. A count of `ShiftCountBits` or more shifts every bit
  /// out, which is legal and well-defined; it is NOT the top of a legal range.
  public const int ShiftCountBits = 64;

  /// The largest count the hardware's shift instruction distinguishes, and therefore the largest
  /// count that may reach an unguarded `shl`/`sar`/`shr`. A count above it must be SATURATED by
  /// the compiler (see <see cref="Eval"/>), never handed to the instruction.
  public const long MaxUnguardedShiftCount = ShiftCountBits - 1;

  /// True for the one count that is not a shift at all. `a shl -1` reads to a human as "shift the
  /// other way"; masked, it silently computed `a shl 63` — the MAXIMUM LEFT shift, a wrong answer
  /// with the opposite sign.
  public static bool IsNegativeCount(long count) => count < MinShiftCount;

  /// True iff the shift instruction may be handed this count DIRECTLY — the count is non-negative
  /// and the hardware's mask would be a no-op on it. Every other count needs the guard.
  public static bool IsUnguardedCount(long count) =>
    count >= MinShiftCount && count <= MaxUnguardedShiftCount;

  /// True iff this shift fills the bits it vacates with the SIGN rather than with zeros. THE one
  /// place that question is answered, because it is the question the fold (<see cref="Eval"/>) and
  /// the guarded lowering must never answer differently: it alone decides whether an out-of-range
  /// count saturates the RESULT to 0 or CLAMPS the count to <see cref="MaxUnguardedShiftCount"/>.
  ///
  /// Go's rule: a right shift is arithmetic when its LEFT operand is signed, logical when it is
  /// unsigned. A left shift vacates the low bits and always fills them with zeros, whatever the
  /// operand's signedness.
  ///
  /// ⚠ `leftOperandIsUnsigned` is a property of the value being SHIFTED and NEVER of the count.
  /// The parser used to take a shift's `OptimalType` from `lhs ?? rhs`, so a count declared
  /// `int(0 to 63)` — an unsigned optimal type, and the most natural way there is to declare a
  /// shift distance — made a SIGNED `shr` zero-fill: `(0-8) shr n` answered 15 for that `n` and -1
  /// for a plain `int` one. A shift is not symmetric in its operands, and this is where that bites.
  public static bool SignFills(bool isRightShift, bool leftOperandIsUnsigned) =>
    isRightShift && !leftOperandIsUnsigned;

  /// The message E2054 carries, in BOTH compilers (maxon-shv2 renders the same text from
  /// Queries.maxon; the specs gate stderr byte-for-byte, so the two must not drift). It names what
  /// is still LEGAL as well as what is not — the count that was rejected before this rule narrowed
  /// (`shl 64`, `shl 100`) is now a perfectly good way to say "shift every bit out".
  public static string NegativeCountMessage(long count) =>
    $"Shift count {count} is negative: a shift distance must be {MinShiftCount} or greater "
    + $"(a count of {ShiftCountBits} or more is legal — it shifts every bit out)";

  /// Fold `lhs <op> count` under the rule above. `count` MUST be non-negative — a negative one is
  /// E2054 at compile time and a panic at run time, and is never folded.
  ///
  /// `signFills` is true for an ARITHMETIC right shift (a signed `shr`) — the only shift whose
  /// vacated bits are not zeros, and so the only one that does not saturate to 0.
  public static long Eval(long lhs, long count, bool isRightShift, bool signFills) {
    if (IsNegativeCount(count))
      throw new InvalidOperationException(
        $"ShiftSemantics.Eval: negative count {count} — a negative count is E2054 at compile time "
        + "and a runtime panic otherwise; it must never reach the folder");

    if (!isRightShift)
      return count >= ShiftCountBits ? 0L : lhs << (int)count;

    if (signFills)
      // Saturating a sign-filling shift is a CLAMP of the count, not a select of the result:
      // `lhs >> 63` already IS the sign, repeated, which is exactly what shifting every bit out
      // of a signed value leaves behind.
      return lhs >> (int)Math.Min(count, MaxUnguardedShiftCount);

    return count >= ShiftCountBits ? 0L : (long)((ulong)lhs >> (int)count);
  }
}
