namespace MaxonSharp.Compiler;

/// <summary>
/// Computes the Mach-O file layout from section sizes.
/// Shared between ARM64CodeEmitterStage (for ADRP fixup resolution) and MachOWriter (for binary output).
/// </summary>
public record MachOLayout {
  public const uint PageSize = 0x4000; // 16KB
  public const ulong TextSegmentVMAddr = 0x100000000;

  public uint TextSectionOffset { get; init; }
  public uint ConstSectionOffset { get; init; }
  public uint TextSegmentFileSize { get; init; }
  public ulong DataSegmentFileOff { get; init; }
  public ulong DataSegmentVMAddr { get; init; }
  public uint DataSegmentFileSize { get; init; }
  public uint GotSectionOffset { get; init; }
  public ulong GotSectionVMAddr { get; init; }
  public uint LoadCommandsSize { get; init; }
  public uint NumCmds { get; init; }
  public bool HasConst { get; init; }
  public bool HasData { get; init; }
  public bool HasImports { get; init; }

  public static MachOLayout Compute(uint codeSize, uint constSize, uint dataSize, uint gotSize, bool hasImports) {
    var hasConst = constSize > 0;
    var hasData = dataSize > 0 || hasImports;

    var segmentCmdBaseSize = 72u;
    var sectionHeaderSize = 80u;
    var textNumSects = 1u + (hasConst ? 1u : 0u);
    var textCmdSize = segmentCmdBaseSize + sectionHeaderSize * textNumSects;
    var dataNumSects = (dataSize > 0 ? 1u : 0u) + (hasImports ? 1u : 0u);
    var dataCmdSize = hasData ? segmentCmdBaseSize + sectionHeaderSize * dataNumSects : 0u;
    var symtabCmdSize = 24u;
    var dysymtabCmdSize = 80u;
    var mainCmdSize = 24u;
    var buildVersionCmdSize = 24u;
    var codeSignatureCmdSize = 16u;
    var chainedFixupsCmdSize = hasImports ? 16u : 0u;
    var exportTrieCmdSize = hasImports ? 16u : 0u;

    var dylibPathLen = (uint)System.Text.Encoding.ASCII.GetByteCount("/usr/lib/libSystem.B.dylib");
    var dylibCmdSize = AlignUp(24u + dylibPathLen + 1u, 8u);
    var dylinkerPathLen = (uint)System.Text.Encoding.ASCII.GetByteCount("/usr/lib/dyld");
    var dylinkerCmdSize = AlignUp(12u + dylinkerPathLen + 1u, 8u);

    var loadCommandsSize = segmentCmdBaseSize + textCmdSize + dataCmdSize + segmentCmdBaseSize +
      symtabCmdSize + dysymtabCmdSize + mainCmdSize + dylibCmdSize + buildVersionCmdSize +
      dylinkerCmdSize + codeSignatureCmdSize + chainedFixupsCmdSize + exportTrieCmdSize;
    var headerSize = 32u;

    var numCmds = 9u; // __PAGEZERO, __TEXT, LC_MAIN, LC_LOAD_DYLIB, LC_BUILD_VERSION, __LINKEDIT, LC_SYMTAB, LC_DYSYMTAB, LC_LOAD_DYLINKER
    numCmds++; // LC_CODE_SIGNATURE
    if (hasData) numCmds++;
    if (hasImports) numCmds += 2;

    var textSectionOffset = AlignUp(headerSize + loadCommandsSize, 4u);
    var constSectionOffset = hasConst ? AlignUp(textSectionOffset + codeSize, 8u) : 0u;
    var textSegmentFileEnd = hasConst ? constSectionOffset + constSize : textSectionOffset + codeSize;
    var textSegmentFileSize = AlignUp(textSegmentFileEnd, PageSize);

    var dataSegmentFileOff = (ulong)textSegmentFileSize;
    var dataSegmentVMAddr = TextSegmentVMAddr + AlignUp((ulong)textSegmentFileSize, PageSize);
    var gotSectionOffset = AlignUp(dataSize, 8u);
    var gotSectionVMAddr = dataSegmentVMAddr + gotSectionOffset;
    var totalDataSize = hasImports ? gotSectionOffset + gotSize : dataSize;
    var dataSegmentFileSize = hasData ? AlignUp(totalDataSize, PageSize) : 0u;

    return new MachOLayout {
      TextSectionOffset = textSectionOffset,
      ConstSectionOffset = constSectionOffset,
      TextSegmentFileSize = textSegmentFileSize,
      DataSegmentFileOff = dataSegmentFileOff,
      DataSegmentVMAddr = dataSegmentVMAddr,
      DataSegmentFileSize = dataSegmentFileSize,
      GotSectionOffset = gotSectionOffset,
      GotSectionVMAddr = gotSectionVMAddr,
      LoadCommandsSize = loadCommandsSize,
      NumCmds = numCmds,
      HasConst = hasConst,
      HasData = hasData,
      HasImports = hasImports,
    };
  }

  public static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) & ~(alignment - 1);
  public static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);
}
