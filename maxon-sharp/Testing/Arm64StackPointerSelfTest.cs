using MaxonSharp.Compiler.Ir;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Testing;

/// <summary>
/// The self-test for the ONE encoding rule that decides whether an arm64 frame is allocated at all,
/// wired as `maxon arm64-sp-selftest` and run by every `dotnet build`.
///
/// <para>AArch64 spells "register 31" two ways. In the ADD/SUB IMMEDIATE and EXTENDED-REGISTER forms it
/// is the stack pointer; in the SHIFTED-REGISTER form it is the zero register. So `sub sp, sp, x16`
/// built out of the shifted-register word assembles as `sub xzr, xzr, x16` — it encodes, it
/// disassembles, and it moves nothing. A function whose locals are never subtracted from SP then writes
/// them over its caller's frame.</para>
///
/// <para>⚠ NO SPEC CASE CAN PIN THIS, AND ONE THAT SEEMED TO WOULD BE WORSE THAN NONE. The defect is
/// reachable only from a frame whose locals are too wide for the 12-bit immediate field, and the
/// compiler picks frame sizes; a spec case can only ask for one indirectly, by declaring locals. A case
/// built to land on a particular size stops landing on it the moment anything upstream changes a slot,
/// and then passes forever while testing nothing. Measured 2026-09-02: across an 18 MB self-compile
/// exactly ONE function had a 4096-byte locals area, and adding a single match arm elsewhere in the
/// compiler is what put it there.</para>
///
/// <para>It pins the RULE, not the prologue's layout: the shape of a frame is the emitter's to change,
/// and a guard that froze it would fail on every legitimate improvement.</para>
/// </summary>
public static class Arm64StackPointerSelfTest {
  /// Frame sizes to emit a prologue for. They bracket the 12-bit immediate field's 0..4095 from both
  /// sides, because that boundary is the only place the emitter changes how it names the amount.
  private static readonly int[] FrameSizes =
    [16, 32, 272, 4096, 4104, 4108, 4110, 4111, 4112, 4128, 8208, 65552];

  private const uint SpRegisterEncoding = 31;

  /// x29/x30, saved by the prologue's own pre-indexed STP. The rest of the frame is what it then has to
  /// subtract from SP itself.
  private const int SavedRegisterPairBytes = 16;

  /// Above this the locals are subtracted by a page-probing LOOP, whose amount is not a property of any
  /// single word — so `CheckLocalsAllocated` reads the amount only at or below it.
  private const int MaxSingleStepLocals = 4096;

  public static int Run() {
    var failures = 0;

    foreach (var stackSize in FrameSizes) {
      var emitter = new ARM64CodeEmitter();
      emitter.Emit(new ARM64PrologueOp(stackSize));
      var words = DecodeWords(emitter.GetCode());

      failures += CheckNoZeroRegisterWrite(stackSize, words);
      failures += CheckLocalsAllocated(stackSize, words);
    }

    if (failures > 0) return 1;

    // ASCII only: this reports through MSBuild's `Exec`, whose console encoding mangles an em dash.
    Console.WriteLine(
      $"arm64-sp-selftest: OK - {FrameSizes.Length} frame sizes, every stack-pointer write names SP");
    return 0;
  }

  /// A plain 64-bit shifted-register ADD/SUB writing encoding 31 discards its result into XZR. There is
  /// no reason to emit one, so its presence means an intended SP write has silently vanished.
  private static int CheckNoZeroRegisterWrite(int stackSize, uint[] words) {
    var failures = 0;

    foreach (var word in words) {
      if (!IsShiftedRegisterAddSub(word) || DestinationField(word) != SpRegisterEncoding) continue;
      Console.Error.WriteLine(
        $"arm64-sp-selftest FAIL: the prologue for a {stackSize}-byte frame emits 0x{word:X8}, a "
        + "shifted-register add/sub writing register 31 - which is XZR in that form, not SP. The "
        + "extended-register form (EmitAddSubExtendedReg) is the one that names SP.");
      failures++;
    }

    return failures;
  }

  /// The locals below x29/x30 must actually be subtracted from SP, by exactly their own size.
  private static int CheckLocalsAllocated(int stackSize, uint[] words) {
    var localsSize = stackSize - SavedRegisterPairBytes;
    if (localsSize <= 0 || localsSize > MaxSingleStepLocals) return 0;

    var subtracted = SpSubtrahends(words);
    if (subtracted.Count == 1 && subtracted[0] == localsSize) return 0;

    Console.Error.WriteLine(
      $"arm64-sp-selftest FAIL: a {stackSize}-byte frame owes SP one subtraction of {localsSize}, "
      + $"but its prologue subtracts [{string.Join(", ", subtracted)}].");
    return 1;
  }

  /// Every amount the emitted words subtract from SP, in order. Reads the immediate form's imm12 field
  /// directly, and the extended-register form's amount out of the register the preceding MOVZ/MOVK
  /// sequence materialized it into.
  private static List<long> SpSubtrahends(uint[] words) {
    var registers = new Dictionary<uint, long>();
    var subtrahends = new List<long>();

    foreach (var word in words) {
      if (IsWideMove(word)) {
        var destination = DestinationField(word);
        var halfword = (long)((word >> 5) & 0xFFFF) << (int)(16 * ((word >> 21) & 0x3));
        registers[destination] =
          IsMoveKeep(word) ? registers.GetValueOrDefault(destination) | halfword : halfword;
        continue;
      }

      if (!SubtractsFromStackPointer(word)) continue;

      if (IsImmediateSub(word)) {
        subtrahends.Add((word >> 10) & 0xFFF);
      } else if (registers.TryGetValue((word >> 16) & 0x1F, out var amount)) {
        subtrahends.Add(amount);
      } else {
        throw new InvalidOperationException(
          $"arm64-sp-selftest: 0x{word:X8} subtracts a register this prologue never materialized");
      }
    }

    return subtrahends;
  }

  /// A SUB writing SP and reading SP, in either of the two forms that read register 31 as SP.
  private static bool SubtractsFromStackPointer(uint word) {
    if (DestinationField(word) != SpRegisterEncoding) return false;
    if (SourceField(word) != SpRegisterEncoding) return false;

    return IsImmediateSub(word) || IsExtendedRegisterSub(word);
  }

  /// SUB (immediate), 64-bit, `sh` = 0 — so bits 21:10 are the amount with no implied shift.
  private static bool IsImmediateSub(uint word) => (word & 0xFFC00000) == 0xD1000000;

  /// SUB (extended register), 64-bit, `option` = UXTX, `imm3` = 0.
  private static bool IsExtendedRegisterSub(uint word) => (word & 0xFFE0FC00) == 0xCB206000;

  /// ADD/SUB (shifted register), 64-bit — the form whose Rd and Rn read 31 as XZR.
  private static bool IsShiftedRegisterAddSub(uint word) =>
    (word & 0xFF200000) is 0x8B000000 or 0xCB000000;

  private static bool IsWideMove(uint word) => (word & 0xFF800000) is 0xD2800000 or 0xF2800000;

  private static bool IsMoveKeep(uint word) => (word & 0xFF800000) == 0xF2800000;

  private static uint DestinationField(uint word) => word & 0x1F;

  private static uint SourceField(uint word) => (word >> 5) & 0x1F;

  private static uint[] DecodeWords(byte[] code) {
    if (code.Length % 4 != 0) {
      throw new InvalidOperationException(
        $"arm64-sp-selftest: emitted {code.Length} bytes, which is not a whole number of instructions");
    }

    var words = new uint[code.Length / 4];
    for (var i = 0; i < words.Length; i++) words[i] = BitConverter.ToUInt32(code, i * 4);
    return words;
  }
}
