using System.Buffers.Binary;

namespace MaxonSharp.Debug;

/// <summary>
/// Recomputes a binary's build-id — the FNV-1a content hash of its `.text` — from the emitted
/// executable, so the driver can validate a `.mxdbg` sidecar against the exact code it describes.
///
/// Nothing is embedded in the binary (that would break byte-identity — see docs/DEBUGGER_DESIGN.md
/// "Identical executables"); the sidecar stores the hash the compiler computed over
/// <c>codeResult.Code</c>, and the driver reproduces it here by reading precisely those bytes back
/// out of the code section: PE stores the exact code length in the section header's <c>VirtualSize</c>
/// (<c>SizeOfRawData</c> is zero-padded to file alignment), Mach-O in the <c>__text</c> section's
/// <c>size</c>. Reading <c>size</c> bytes at the section's file offset yields the same span
/// <see cref="MxdbgFormat.ComputeBuildId"/> hashed at build time.
///
/// A binary the host cannot parse is REFUSED rather than waved through: a sidecar that cannot be
/// checked against its binary is exactly the stale-instrument case this project keeps closing, so the
/// driver reports the reason and does not proceed with an unverified sidecar.
/// </summary>
public static class BinaryBuildId {
  // PE constants.
  private const int PeLfanewOffset = 0x3C;
  private static readonly byte[] PeSignature = "PE\0\0"u8.ToArray();
  private const int PeCoffNumSectionsOffset = 6;     // from the PE signature
  private const int PeCoffOptHeaderSizeOffset = 20;  // from the PE signature
  private const int PeCoffHeaderSize = 24;           // signature(4) + COFF header(20)
  private const int PeSectionHeaderSize = 40;
  private const int PeSectionVirtualSizeOffset = 8;
  private const int PeSectionRawPtrOffset = 20;
  private const string PeTextSectionName = ".text";

  // Mach-O 64-bit constants.
  private const uint MachoMagic64 = 0xFEEDFACF;
  private const int MachoHeader64Size = 32;
  private const uint MachoLcSegment64 = 0x19;
  private const int MachoSegNCmdsOffset = 16;
  private const int MachoSegCmdHeaderSize = 72;   // through nsects; sections follow
  private const int MachoSegNameOffset = 8;
  private const int MachoSegNSectsOffset = 64;
  private const int MachoSection64Size = 80;
  private const int MachoSectSizeOffset = 40;      // addr(8) precedes size(8), both after 2x name(16)
  private const int MachoSectOffsetOffset = 48;
  private const string MachoTextSegName = "__TEXT";
  private const string MachoTextSectName = "__text";

  /// <summary>
  /// Compute the build-id of <paramref name="binaryPath"/> from its code section. Returns false with a
  /// human-readable <paramref name="error"/> when the file cannot be read or its format is not a PE or
  /// Mach-O the host understands — never a guessed hash.
  /// </summary>
  public static bool TryCompute(string binaryPath, out ulong buildId, out string error) {
    buildId = 0;

    byte[] bytes;
    try {
      bytes = File.ReadAllBytes(binaryPath);
    } catch (Exception ex) {
      error = $"cannot read '{binaryPath}': {ex.Message}";
      return false;
    }

    if (TryExtractTextSection(bytes, out var text, out error)) {
      buildId = MxdbgFormat.ComputeBuildId(text);
      return true;
    }
    return false;
  }

  /// <summary>The code-section bytes the compiler hashed, dispatched on the file's magic.</summary>
  private static bool TryExtractTextSection(byte[] b, out ReadOnlySpan<byte> text, out string error) {
    text = default;

    if (b.Length >= 2 && b[0] == (byte)'M' && b[1] == (byte)'Z')
      return TryExtractPeText(b, out text, out error);

    if (b.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(b) == MachoMagic64)
      return TryExtractMachoText(b, out text, out error);

    error = "unrecognized binary format (not a PE or 64-bit Mach-O)";
    return false;
  }

  private static bool TryExtractPeText(byte[] b, out ReadOnlySpan<byte> text, out string error) {
    text = default;
    error = "";

    if (!TryU32(b, PeLfanewOffset, out uint peOff)) { error = "truncated PE (no e_lfanew)"; return false; }
    if (peOff + PeCoffHeaderSize > b.Length || !b.AsSpan((int)peOff, PeSignature.Length).SequenceEqual(PeSignature)) {
      error = "not a PE (bad signature)";
      return false;
    }

    ushort numSections = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan((int)peOff + PeCoffNumSectionsOffset));
    ushort optHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan((int)peOff + PeCoffOptHeaderSizeOffset));
    long sectionsOff = peOff + PeCoffHeaderSize + optHeaderSize;

    for (int i = 0; i < numSections; i++) {
      long rec = sectionsOff + (long)i * PeSectionHeaderSize;
      if (rec + PeSectionHeaderSize > b.Length) { error = "truncated PE section table"; return false; }
      if (SectionName(b, (int)rec, 8) != PeTextSectionName) continue;

      uint size = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)rec + PeSectionVirtualSizeOffset));
      uint rawPtr = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)rec + PeSectionRawPtrOffset));
      if ((long)rawPtr + size > b.Length) { error = "PE .text extends past end of file"; return false; }
      text = b.AsSpan((int)rawPtr, (int)size);
      return true;
    }

    error = "PE has no .text section";
    return false;
  }

  private static bool TryExtractMachoText(byte[] b, out ReadOnlySpan<byte> text, out string error) {
    text = default;
    error = "";

    if (b.Length < MachoHeader64Size) { error = "truncated Mach-O header"; return false; }
    uint ncmds = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(MachoSegNCmdsOffset));
    long cmd = MachoHeader64Size;

    for (uint i = 0; i < ncmds; i++) {
      if (cmd + 8 > b.Length) { error = "truncated Mach-O load commands"; return false; }
      uint lcCmd = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)cmd));
      uint lcSize = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)cmd + 4));
      if (lcSize == 0 || cmd + lcSize > b.Length) { error = "malformed Mach-O load command"; return false; }

      if (lcCmd == MachoLcSegment64 && SectionName(b, (int)cmd + MachoSegNameOffset, 16) == MachoTextSegName) {
        uint nsects = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)cmd + MachoSegNSectsOffset));
        for (uint s = 0; s < nsects; s++) {
          long sec = cmd + MachoSegCmdHeaderSize + (long)s * MachoSection64Size;
          if (sec + MachoSection64Size > b.Length) { error = "truncated Mach-O section table"; return false; }
          if (SectionName(b, (int)sec, 16) != MachoTextSectName) continue;

          ulong size = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan((int)sec + MachoSectSizeOffset));
          uint offset = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)sec + MachoSectOffsetOffset));
          if (offset + size > (ulong)b.Length) { error = "Mach-O __text extends past end of file"; return false; }
          text = b.AsSpan((int)offset, (int)size);
          return true;
        }
      }
      cmd += lcSize;
    }

    error = "Mach-O has no __TEXT,__text section";
    return false;
  }

  /// A fixed-width, NUL-padded section/segment name field read as ASCII up to its first NUL.
  private static string SectionName(byte[] b, int offset, int width) {
    int end = offset;
    int limit = Math.Min(offset + width, b.Length);
    while (end < limit && b[end] != 0) end++;
    return System.Text.Encoding.ASCII.GetString(b, offset, end - offset);
  }

  private static bool TryU32(byte[] b, int offset, out uint value) {
    if (offset + 4 > b.Length) { value = 0; return false; }
    value = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset));
    return true;
  }
}
