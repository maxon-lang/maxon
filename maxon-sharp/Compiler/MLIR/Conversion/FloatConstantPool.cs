using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaxonSharp.Compiler.Ir.Conversion;

/// <summary>
/// The dedup policy for emitted float constants, written once and shared by every backend.
/// Each backend keeps its own pool and its own rdata sink (the alignment differs); what lives
/// here is the part that must not differ, because getting it wrong is a silently wrong answer
/// rather than a crash.
///
/// ⚠ THE POOL IS KEYED ON THE BIT PATTERN, NEVER ON THE VALUE. A <c>Dictionary&lt;double, …&gt;</c>
/// compares with <c>EqualityComparer&lt;double&gt;.Default</c>, i.e. <c>double.Equals</c>, and for
/// that comparison <c>+0.0</c> and <c>-0.0</c> are EQUAL. Keyed on the value, the two literals
/// therefore collapse into ONE rdata slot and whichever spelling the program mentions first
/// supplies the bits for both — so a program that writes <c>-0.0</c> silently receives <c>+0.0</c>,
/// and which one wins changes when an unrelated line moves. (Found 2026-08-02 by an atan2 tick that
/// needed the sign of a zero and got the wrong one out of the oracle: `floatToBits(-0.0)` read 0.)
///
/// ⚠ THE LABEL IS DERIVED FROM THE BITS FOR THE SAME REASON, ONE LAYER DOWN. It is built from the
/// decimal spelling, and a spelling need not be unique across bit patterns: every NaN prints "NaN"
/// whatever payload it carries. Two pool entries sharing one label would be the same collapse again,
/// wearing the emitter's clothes, so the families that decimal cannot tell apart are named from the
/// key instead. Every other double's spelling is unique: on this project's pinned
/// <c>net10.0</c> the default format is the SHORTEST ROUND-TRIP string, which parses back to exactly
/// the double it came from, so two distinct doubles cannot share one. (The infinities are not a third
/// family — <c>InvariantInfo</c> spells them "Infinity" and "-Infinity", distinct from each other and
/// from every finite spelling.)
///
/// ✅ FOR EVERY REACHABLE PROGRAM THE EMITTED LABELS ARE BYTE-IDENTICAL TO WHAT THEY WERE BEFORE the
/// pool was re-keyed, which is why no golden churned and is not obvious: the NaN family is
/// unreachable (below), and net10.0 already formats negative zero WITH its sign, so
/// <c>__float_0</c> / <c>__float_-0</c> are the strings <c>ToString</c> was producing anyway. The
/// zero arm is kept so that the key ⇒ label ⇒ bits chain rests on the key rather than on a
/// formatter's treatment of the sign of zero, which is not this file's invariant to depend on.
///
/// ⚠ THE ZERO COLLAPSE IS MEASURED; THE NaN COLLAPSE IS NOT. <c>double.Equals</c> also reports NaN
/// equal to NaN — unlike <c>==</c> — so distinct NaN payloads would merge by the identical mechanism.
/// But no Maxon program can currently produce a NaN constant: there is no NaN literal spelling, and
/// the checked divide refuses <c>0.0/0.0</c>. That arm is therefore DEFENSIVE AND UNEXERCISED, and it
/// is here so the key ⇒ bits invariant does not quietly depend on that staying true.
/// </summary>
internal static class FloatConstantPool {
  private const string Float64LabelPrefix = "__float_";
  private const string Float32LabelPrefix = "__float32_";
  private const string NanLabelInfix = "nan_";
  private const string PositiveZeroLabelSuffix = "0";
  private const string NegativeZeroLabelSuffix = "-0";

  /// <summary>
  /// The label for <paramref name="value"/>'s rdata slot, adding the slot through
  /// <paramref name="addRdataEntry"/> the first time this bit pattern is seen.
  /// </summary>
  internal static string GetOrCreateFloat64Label(
      double value,
      Dictionary<long, string> pool,
      Action<string, byte[]> addRdataEntry) {
    var key = BitConverter.DoubleToInt64Bits(value);
    if (pool.TryGetValue(key, out var existing))
      return existing;

    var label = LabelFor(Float64LabelPrefix, value, signBit: key < 0, keyHex: key.ToString("x16", CultureInfo.InvariantCulture),
      spelling: value.ToString(CultureInfo.InvariantCulture));
    pool[key] = label;
    addRdataEntry(label, BitConverter.GetBytes(value));
    return label;
  }

  /// <summary>Single-precision counterpart of <see cref="GetOrCreateFloat64Label"/>.</summary>
  internal static string GetOrCreateFloat32Label(
      float value,
      Dictionary<int, string> pool,
      Action<string, byte[]> addRdataEntry) {
    var key = BitConverter.SingleToInt32Bits(value);
    if (pool.TryGetValue(key, out var existing))
      return existing;

    var label = LabelFor(Float32LabelPrefix, value, signBit: key < 0, keyHex: key.ToString("x8", CultureInfo.InvariantCulture),
      spelling: value.ToString(CultureInfo.InvariantCulture));
    pool[key] = label;
    addRdataEntry(label, BitConverter.GetBytes(value));
    return label;
  }

  /// <summary>
  /// The naming policy, written ONCE for both widths. A <c>float</c> widens to <c>double</c>
  /// losslessly and both classifications survive the widening, so only the two things that are
  /// genuinely width-specific — the key's hex spelling and the value's decimal spelling — are the
  /// caller's to supply. Split per width, a clause added to one copy and not the other would be a
  /// silently divergent naming policy inside the file whose whole purpose is that there is one.
  /// </summary>
  private static string LabelFor(string prefix, double value, bool signBit, string keyHex, string spelling) {
    if (value == 0.0)
      return $"{prefix}{(signBit ? NegativeZeroLabelSuffix : PositiveZeroLabelSuffix)}";

    if (double.IsNaN(value))
      return $"{prefix}{NanLabelInfix}{keyHex}";

    return $"{prefix}{spelling}";
  }
}
