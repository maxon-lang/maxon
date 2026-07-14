using MaxonSharp.Compiler.Ir.Dialects;

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
///     A bare `int` is signed, so `shr` sign-propagates on one: `(0-8) shr 60` is -1, not 15. A
///     value whose ranged type is unsigned — a range with a low bound of 0, `int(0 to u64.max)`
///     being Maxon's `uint64` — zero-fills instead: `u64.max shr 60` is 15. <see cref="KindOf"/>
///     is the one place that decision is made.
///
///   • **A SHIFT IS 64 BITS WIDE.** Its operands and its result are i64, whatever ranged type the
///     left operand carries. A ranged type decides a shift's FILL (see <see cref="KindOf"/>) and
///     NEVER its WIDTH — those are two different questions, and answering the second from the first
///     is a wrong answer twice over:
///
///       – it truncates the VALUE. `x shl 29` on an `int(-2^31 to 2^31-1)` needs 61 bits to hold
///         its answer; narrowed to a 32-bit op, `(0-8) shl 29` was **0** while the same shift by a
///         count the compiler could not see answered **-4294967296**. Same value, same count, two
///         answers — the folder and the emitted code disagreeing, which is what this file exists to
///         make impossible.
///       – it masks the COUNT to FIVE bits. A 32-bit shift instruction takes its count mod 32
///         (`sar r32, cl`), so `x shr 33` — a count this file calls perfectly ordinary, and which
///         therefore reaches the instruction unguarded — would have computed `x shr 1`.
///
///     <see cref="Eval"/> computes in `long`, and there is exactly one width for the emitted code to
///     agree with it in.
///
///   • **A negative count is an ERROR**, never a shift the other way. A constant one is a compile
///     error (E2054); a runtime one panics.
///
/// The saturation follows from the bullets together, and is the whole content of this file:
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

  /// The three shifts the hardware has, and the whole of what a shift's FILL amounts to. A single
  /// three-valued fact, replacing the `(isRightShift, signFills)` pair that used to carry it — that
  /// pair has FOUR states, and the fourth ("a left shift that fills with the sign") names nothing,
  /// so every reader had to know not to build it and every function taking it had to trust that no
  /// one did. This is the C# twin of maxon-shv2's three shift opcodes (`shl` / `shr` /
  /// `shrLogical`), and it exists for the same reason: the fill is decided ONCE, from the left
  /// operand's type, and then CARRIED — never re-derived at each site that acts on it.
  public enum ShiftKind {
    /// `shl` — vacates the LOW bits and fills them with zeros, whatever the operand's signedness.
    Left,

    /// `shr` on a SIGNED left operand — x64 SAR / arm64 ASR. Fills with the sign, so an
    /// out-of-range count saturates to the sign (a CLAMP of the count; `x sar 63` already IS it).
    ArithmeticRight,

    /// `shr` on an UNSIGNED left operand — x64 SHR / arm64 LSR. Fills with zeros, so an
    /// out-of-range count saturates the RESULT to 0.
    LogicalRight,
  }

  /// The shift `op` performs on a left operand of the given signedness — THE one place Go's rule
  /// becomes a concrete choice, and the question the fold (<see cref="Eval"/>) and the codegen
  /// (MaxonToStandardConversion.EmitShift) must never answer differently. Every reader takes the
  /// answer FROM here rather than re-deriving it from `(op, IsUnsigned)`.
  ///
  /// Go's rule: a right shift is arithmetic when its LEFT operand is signed, logical when it is
  /// unsigned. A left shift always zero-fills, whatever the operand's signedness.
  ///
  /// ⚠ `leftOperandIsUnsigned` is a property of the value being SHIFTED and NEVER of the count.
  /// The parser used to take a shift's `OptimalType` from `lhs ?? rhs`, so a count declared
  /// `int(0 to 63)` — an unsigned optimal type, and the most natural way there is to declare a
  /// shift distance — made a SIGNED `shr` zero-fill: `(0-8) shr n` answered 15 for that `n` and -1
  /// for a plain `int` one. A shift is not symmetric in its operands, and this is where that bites.
  public static ShiftKind KindOf(MaxonBinOperator op, bool leftOperandIsUnsigned) => op switch {
    MaxonBinOperator.Shl => ShiftKind.Left,
    MaxonBinOperator.Shr => leftOperandIsUnsigned ? ShiftKind.LogicalRight : ShiftKind.ArithmeticRight,
    _ => throw new InvalidOperationException(
      $"ShiftSemantics.KindOf: {op} is not a shift — only `shl`/`shr` have a fill"),
  };

  /// True iff a shift of this kind fills the bits it vacates with ZEROS — a left shift, or a
  /// logical right one. The zero-fillers saturate an out-of-range count to 0; the sign-filler
  /// (<see cref="ShiftKind.ArithmeticRight"/>) is the one exception, and clamps the count instead.
  public static bool ZeroFills(ShiftKind kind) => kind is not ShiftKind.ArithmeticRight;

  /// The message E2054 carries, in BOTH compilers (maxon-shv2 renders the same text from
  /// Queries.maxon; the specs gate stderr byte-for-byte, so the two must not drift). It names what
  /// is still LEGAL as well as what is not — the count that was rejected before this rule narrowed
  /// (`shl 64`, `shl 100`) is now a perfectly good way to say "shift every bit out".
  public static string NegativeCountMessage(long count) =>
    $"Shift count {count} is negative: a shift distance must be {MinShiftCount} or greater "
    + $"(a count of {ShiftCountBits} or more is legal — it shifts every bit out)";

  /// Fold `lhs <kind> count`. `count` MUST be non-negative — a negative one is E2054 at compile
  /// time and a panic at run time, and is never folded. `kind` is <see cref="KindOf"/>'s answer,
  /// which is the same one the codegen reads, so this fold cannot disagree with the emitted shift.
  public static long Eval(long lhs, long count, ShiftKind kind) {
    if (IsNegativeCount(count))
      throw new InvalidOperationException(
        $"ShiftSemantics.Eval: negative count {count} — a negative count is E2054 at compile time "
        + "and a runtime panic otherwise; it must never reach the folder");

    return kind switch {
      ShiftKind.Left => count >= ShiftCountBits ? 0L : lhs << (int)count,
      ShiftKind.LogicalRight => count >= ShiftCountBits ? 0L : (long)((ulong)lhs >> (int)count),
      // Saturating a sign-filling shift is a CLAMP of the count, not a select of the result:
      // `lhs >> 63` already IS the sign, repeated, which is exactly what shifting every bit out
      // of a signed value leaves behind.
      ShiftKind.ArithmeticRight => lhs >> (int)Math.Min(count, MaxUnguardedShiftCount),
      _ => throw new InvalidOperationException($"ShiftSemantics.Eval: unhandled ShiftKind {kind}"),
    };
  }
}
