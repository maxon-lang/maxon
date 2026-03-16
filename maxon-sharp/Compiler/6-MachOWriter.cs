using System.Security.Cryptography;
using System.Text;
using MaxonSharp.Compiler.Mlir;

namespace MaxonSharp.Compiler;

public class MachOWriter {
  private const uint MH_MAGIC_64 = 0xFEEDFACF;
  private const uint CPU_TYPE_ARM64 = 0x0100000C;
  private const uint CPU_SUBTYPE_ARM64_ALL = 0;
  private const uint MH_EXECUTE = 2;

  private const uint MH_NOUNDEFS = 0x1;
  private const uint MH_DYLDLINK = 0x4;
  private const uint MH_TWOLEVEL = 0x80;
  private const uint MH_PIE = 0x200000;

  private const uint LC_SEGMENT_64 = 0x19;
  private const uint LC_SYMTAB = 0x02;
  private const uint LC_DYSYMTAB = 0x0B;
  private const uint LC_CODE_SIGNATURE = 0x1D;
  private const uint LC_MAIN = 0x80000028;
  private const uint LC_LOAD_DYLIB = 0x0C;
  private const uint LC_LOAD_DYLINKER = 0x0E;
  private const uint LC_BUILD_VERSION = 0x32;
  private const uint LC_DYLD_CHAINED_FIXUPS = 0x80000034;
  private const uint LC_DYLD_EXPORTS_TRIE = 0x80000033;

  private const ulong PageZeroSize = 0x100000000;

  private const int VM_PROT_READ = 0x01;
  private const int VM_PROT_WRITE = 0x02;
  private const int VM_PROT_EXECUTE = 0x04;

  private const uint S_REGULAR = 0x00000000;
  private const uint S_ATTR_PURE_INSTRUCTIONS = 0x80000000;
  private const uint S_ATTR_SOME_INSTRUCTIONS = 0x00000400;

  // Chained fixup pointer format for GOT entries (DYLD_CHAINED_PTR_64 = 6)
  // Bind entry: bit 63 = 1 (bind), bits 0-23 = ordinal, bits 24-31 = addend
  private const int DYLD_CHAINED_PTR_64 = 6;
  // DYLD_CHAINED_IMPORT format 1: 4 bytes per import
  private const int DYLD_CHAINED_IMPORT = 1;

  // Code signature constants
  private const uint CSMAGIC_EMBEDDED_SIGNATURE = 0xFADE0CC0;
  private const uint CSMAGIC_CODEDIRECTORY = 0xFADE0C02;
  private const uint CSSLOT_CODEDIRECTORY = 0;
  private const uint CS_ADHOC = 0x2;
  private const uint CS_LINKER_SIGNED = 0x20000;
  private const int CS_HASHTYPE_SHA256 = 2;
  private const int CS_SHA256_LEN = 32;
  private const int CS_PAGE_SIZE_LOG2 = 12; // 4096-byte pages for hashing

  public static void Write(string path, byte[] code, byte[]? rdata = null, byte[]? data = null,
    byte[]? ucddata = null, byte[]? symdata = null,
    byte[]? got = null, IReadOnlyList<string>? importNames = null) {
    Logger.Debug(LogCategory.Pe, $"Writing Mach-O file: {path}");

    rdata ??= [];
    data ??= [];
    ucddata ??= [];
    symdata ??= [];
    got ??= [];
    importNames ??= [];

    var hasImports = importNames.Count > 0 && got.Length > 0;
    var importCount = importNames.Count;

    // Merge ucddata and symdata into const section
    byte[] constData;
    if (ucddata.Length > 0 || symdata.Length > 0) {
      constData = new byte[rdata.Length + ucddata.Length + symdata.Length];
      Array.Copy(rdata, 0, constData, 0, rdata.Length);
      Array.Copy(ucddata, 0, constData, rdata.Length, ucddata.Length);
      Array.Copy(symdata, 0, constData, rdata.Length + ucddata.Length, symdata.Length);
    } else {
      constData = rdata;
    }

    // Binary identifier for code signature (filename without extension)
    var identifier = Path.GetFileNameWithoutExtension(path);
    var identifierBytes = Encoding.ASCII.GetBytes(identifier);

    // Compute shared layout
    var layout = MachOLayout.Compute(
      codeSize: (uint)code.Length,
      constSize: (uint)constData.Length,
      dataSize: (uint)data.Length,
      gotSize: (uint)got.Length,
      hasImports: hasImports);

    var hasConst = layout.HasConst;
    var hasData = layout.HasData;
    var textSectionOffset = layout.TextSectionOffset;
    var textSectionSize = (uint)code.Length;
    var constSectionOffset = layout.ConstSectionOffset;
    var constSectionSize = (uint)constData.Length;
    var textSegmentFileSize = layout.TextSegmentFileSize;
    var dataSegmentFileOff = layout.DataSegmentFileOff;
    var dataSegmentVMAddr = layout.DataSegmentVMAddr;
    var dataSectionSize = (uint)data.Length;
    var gotSectionOffset = layout.GotSectionOffset;
    var gotSectionSize = (uint)got.Length;
    var dataSegmentFileSize = layout.DataSegmentFileSize;
    var loadCommandsSize = layout.LoadCommandsSize;
    var numCmds = layout.NumCmds;

    var dylibPathStr = "/usr/lib/libSystem.B.dylib";
    var dylibPathBytes = Encoding.ASCII.GetBytes(dylibPathStr);
    var dylibCmdSize = AlignUp(24u + (uint)dylibPathBytes.Length + 1u, 8u);
    var dylinkerPathStr = "/usr/lib/dyld";
    var dylinkerPathBytes = Encoding.ASCII.GetBytes(dylinkerPathStr);
    var dylinkerCmdSize = AlignUp(12u + (uint)dylinkerPathBytes.Length + 1u, 8u);

    var textNumSects = 1u + (hasConst ? 1u : 0u);
    var dataNumSects = (data.Length > 0 ? 1u : 0u) + (hasImports ? 1u : 0u);
    var chainedFixupsCmdSize = hasImports ? 16u : 0u;
    var exportTrieCmdSize = hasImports ? 16u : 0u;
    var symtabCmdSize = 24u;
    var dysymtabCmdSize = 80u;
    var mainCmdSize = 24u;
    var buildVersionCmdSize = 24u;
    var codeSignatureCmdSize = 16u;

    // --- Build chained fixups data for __LINKEDIT ---
    byte[] chainedFixupsData = [];
    byte[] exportsTrieData = [];

    if (hasImports) {
      // Patch GOT entries with chained fixup bind pointers
      var patchedGot = new byte[got.Length];
      for (int i = 0; i < importCount; i++) {
        ulong ordinal = (uint)i;
        ulong next = (i < importCount - 1) ? 2u : 0u;
        ulong bindEntry = (1UL << 63) | (next << 51) | ordinal;
        var bytes = BitConverter.GetBytes(bindEntry);
        Array.Copy(bytes, 0, patchedGot, i * 8, 8);
      }
      got = patchedGot;

      // Build chained fixups blob
      var cf = new MemoryStream();
      var cw = new BinaryWriter(cf);

      var startsOffset = 0x20u;
      var segCount = 4u;
      var startsSize = 4u + segCount * 4u;
      var dataSegStartsSize = 4u + 2u + 2u + 8u + 4u + 2u + 2u;
      var dataSegStartsOffset = startsSize;

      var importsOffset = AlignUp(startsOffset + startsSize + dataSegStartsSize, 4u);
      var symbolsOffset = importsOffset + (uint)(importCount * 4);

      var symbolPool = new MemoryStream();
      symbolPool.WriteByte(0);
      var symbolOffsets = new List<uint>();
      foreach (var name in importNames) {
        symbolOffsets.Add((uint)symbolPool.Position);
        var nameBytes = Encoding.ASCII.GetBytes("_" + name);
        symbolPool.Write(nameBytes);
        symbolPool.WriteByte(0);
      }
      var symbolsData = symbolPool.ToArray();

      cw.Write(0u);
      cw.Write(startsOffset);
      cw.Write(importsOffset);
      cw.Write(symbolsOffset);
      cw.Write((uint)importCount);
      cw.Write((uint)DYLD_CHAINED_IMPORT);
      cw.Write(0u);

      while (cf.Position < startsOffset) cw.Write((byte)0);

      cw.Write(segCount);
      cw.Write(0u);
      cw.Write(0u);
      cw.Write(dataSegStartsOffset);
      cw.Write(0u);

      cw.Write(dataSegStartsSize);
      cw.Write((ushort)MachOLayout.PageSize);
      cw.Write((ushort)DYLD_CHAINED_PTR_64);
      cw.Write((ulong)dataSegmentFileOff);
      cw.Write(0u);
      cw.Write((ushort)1);
      cw.Write((ushort)gotSectionOffset);

      while (cf.Position < importsOffset) cw.Write((byte)0);

      for (int i = 0; i < importCount; i++) {
        uint importEntry = 1u;
        importEntry |= 0u << 8;
        importEntry |= (symbolOffsets[i] << 9);
        cw.Write(importEntry);
      }

      cw.Write(symbolsData);

      while (cf.Position % 8 != 0) cw.Write((byte)0);

      chainedFixupsData = cf.ToArray();
      exportsTrieData = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    }

    // --- __LINKEDIT content ---
    var linkeditFileOff = hasData ? dataSegmentFileOff + dataSegmentFileSize : textSegmentFileSize;
    var linkeditVMAddr = hasData
      ? dataSegmentVMAddr + AlignUp((ulong)dataSegmentFileSize, MachOLayout.PageSize)
      : MachOLayout.TextSegmentVMAddr + AlignUp((ulong)textSegmentFileSize, MachOLayout.PageSize);

    var chainedFixupsOff = (uint)linkeditFileOff;
    var chainedFixupsSize = (uint)chainedFixupsData.Length;
    var exportsTrieOff = AlignUp(chainedFixupsOff + chainedFixupsSize, 8u);
    var exportsTrieSize = (uint)exportsTrieData.Length;
    var strtabOff = AlignUp(exportsTrieOff + exportsTrieSize, 4u);
    var strtabSize = 4u;

    // Code signature follows the string table, aligned to 16 bytes
    var codeSignatureOff = AlignUp(strtabOff + strtabSize, 16u);
    var codeLimit = codeSignatureOff; // everything before the signature is signed
    var hashPageSize = 1u << CS_PAGE_SIZE_LOG2; // 4096
    var nCodeSlots = (codeLimit + hashPageSize - 1) / hashPageSize;

    // CodeDirectory layout:
    //   header (88 bytes for v20400) + identifier (len+1) + page hashes (nCodeSlots * 32)
    var cdHeaderSize = 88u;
    var identLen = (uint)identifierBytes.Length + 1u; // null-terminated
    var hashOffset = cdHeaderSize + identLen;
    var cdSize = hashOffset + nCodeSlots * (uint)CS_SHA256_LEN;

    // SuperBlob: header (12) + 1 BlobIndex (8) + CodeDirectory
    var superBlobHeaderSize = 12u + 8u; // magic(4) + length(4) + count(4) + index(type:4 + offset:4)
    var codeSignatureSize = superBlobHeaderSize + cdSize;

    // __LINKEDIT must encompass the code signature
    var linkeditContentEnd = codeSignatureOff + codeSignatureSize;
    var linkeditFileSize = AlignUp(linkeditContentEnd - (uint)linkeditFileOff, MachOLayout.PageSize);
    var linkeditVMSize = linkeditFileSize;

    var entryOff = (ulong)textSectionOffset;

    var flags = MH_DYLDLINK | MH_TWOLEVEL | MH_PIE;
    if (!hasImports) flags |= MH_NOUNDEFS;

    // === Write to memory buffer first (needed for page hash computation) ===
    var ms = new MemoryStream((int)codeSignatureOff + (int)codeSignatureSize + 4096);
    var writer = new BinaryWriter(ms);

    // === Write Mach-O Header ===
    writer.Write(MH_MAGIC_64);
    writer.Write(CPU_TYPE_ARM64);
    writer.Write(CPU_SUBTYPE_ARM64_ALL);
    writer.Write(MH_EXECUTE);
    writer.Write(numCmds);
    writer.Write(loadCommandsSize);
    writer.Write(flags);
    writer.Write(0u);

    // === __PAGEZERO ===
    WriteSegmentCommand(writer, "__PAGEZERO", 0, PageZeroSize, 0, 0, 0, 0, 0);

    // === __TEXT ===
    WriteSegmentCommand(writer, "__TEXT", MachOLayout.TextSegmentVMAddr, textSegmentFileSize,
      0, textSegmentFileSize, VM_PROT_READ | VM_PROT_EXECUTE, VM_PROT_READ | VM_PROT_EXECUTE, textNumSects);
    WriteSectionHeader(writer, "__text", "__TEXT", MachOLayout.TextSegmentVMAddr + textSectionOffset,
      textSectionSize, textSectionOffset, 2, S_ATTR_PURE_INSTRUCTIONS | S_ATTR_SOME_INSTRUCTIONS | S_REGULAR);
    if (hasConst)
      WriteSectionHeader(writer, "__const", "__TEXT", MachOLayout.TextSegmentVMAddr + constSectionOffset,
        constSectionSize, constSectionOffset, 3, S_REGULAR);

    // === __DATA ===
    if (hasData) {
      WriteSegmentCommand(writer, "__DATA", dataSegmentVMAddr, dataSegmentFileSize,
        dataSegmentFileOff, dataSegmentFileSize, VM_PROT_READ | VM_PROT_WRITE, VM_PROT_READ | VM_PROT_WRITE, dataNumSects);
      if (data.Length > 0)
        WriteSectionHeader(writer, "__data", "__DATA", dataSegmentVMAddr, dataSectionSize,
          (uint)dataSegmentFileOff, 3, S_REGULAR);
      if (hasImports)
        WriteSectionHeader(writer, "__got", "__DATA", dataSegmentVMAddr + gotSectionOffset,
          gotSectionSize, (uint)(dataSegmentFileOff + gotSectionOffset), 3, S_REGULAR);
    }

    // === __LINKEDIT ===
    WriteSegmentCommand(writer, "__LINKEDIT", linkeditVMAddr, linkeditVMSize,
      linkeditFileOff, linkeditFileSize, VM_PROT_READ, VM_PROT_READ, 0);

    // === LC_SYMTAB ===
    writer.Write(LC_SYMTAB);
    writer.Write(symtabCmdSize);
    writer.Write(strtabOff);
    writer.Write(0u);
    writer.Write(strtabOff);
    writer.Write(strtabSize);

    // === LC_DYSYMTAB ===
    writer.Write(LC_DYSYMTAB);
    writer.Write(dysymtabCmdSize);
    for (int i = 0; i < 18; i++) writer.Write(0u);

    // === LC_LOAD_DYLINKER ===
    writer.Write(LC_LOAD_DYLINKER);
    writer.Write(dylinkerCmdSize);
    writer.Write(12u);
    writer.Write(dylinkerPathBytes);
    writer.Write((byte)0);
    var dylinkerPadding = dylinkerCmdSize - (12u + (uint)dylinkerPathBytes.Length + 1u);
    if (dylinkerPadding > 0) writer.Write(new byte[dylinkerPadding]);

    // === LC_MAIN ===
    writer.Write(LC_MAIN);
    writer.Write(mainCmdSize);
    writer.Write(entryOff);
    writer.Write(0UL);

    // === LC_LOAD_DYLIB ===
    writer.Write(LC_LOAD_DYLIB);
    writer.Write(dylibCmdSize);
    writer.Write(24u);
    writer.Write(0u); writer.Write(0u); writer.Write(0u);
    writer.Write(dylibPathBytes);
    writer.Write((byte)0);
    var dylibPadding = dylibCmdSize - (24u + (uint)dylibPathBytes.Length + 1u);
    if (dylibPadding > 0) writer.Write(new byte[dylibPadding]);

    // === LC_BUILD_VERSION ===
    writer.Write(LC_BUILD_VERSION);
    writer.Write(buildVersionCmdSize);
    writer.Write(1u);
    writer.Write(0x000D0000u);
    writer.Write(0u); writer.Write(0u);

    // === LC_DYLD_CHAINED_FIXUPS (if imports) ===
    if (hasImports) {
      writer.Write(LC_DYLD_CHAINED_FIXUPS);
      writer.Write(chainedFixupsCmdSize);
      writer.Write(chainedFixupsOff);
      writer.Write(chainedFixupsSize);

      writer.Write(LC_DYLD_EXPORTS_TRIE);
      writer.Write(exportTrieCmdSize);
      writer.Write(exportsTrieOff);
      writer.Write(exportsTrieSize);
    }

    // === LC_CODE_SIGNATURE ===
    writer.Write(LC_CODE_SIGNATURE);
    writer.Write(codeSignatureCmdSize);
    writer.Write(codeSignatureOff);
    writer.Write(codeSignatureSize);

    // === Pad to __text ===
    var currentPos = (uint)ms.Position;
    if (currentPos < textSectionOffset) writer.Write(new byte[textSectionOffset - currentPos]);

    // === __text ===
    writer.Write(code);

    // === __const ===
    if (hasConst) {
      currentPos = (uint)ms.Position;
      if (currentPos < constSectionOffset) writer.Write(new byte[constSectionOffset - currentPos]);
      writer.Write(constData);
    }

    // === Pad __TEXT to page ===
    currentPos = (uint)ms.Position;
    if (currentPos < textSegmentFileSize) writer.Write(new byte[textSegmentFileSize - currentPos]);

    // === __DATA ===
    if (hasData) {
      if (data.Length > 0) writer.Write(data);
      if (hasImports) {
        currentPos = (uint)ms.Position;
        var gotFileOffset = (uint)dataSegmentFileOff + gotSectionOffset;
        if (currentPos < gotFileOffset) writer.Write(new byte[gotFileOffset - currentPos]);
        writer.Write(got);
      }
      currentPos = (uint)ms.Position;
      var dataSegmentEnd = dataSegmentFileOff + dataSegmentFileSize;
      if (currentPos < dataSegmentEnd) writer.Write(new byte[dataSegmentEnd - currentPos]);
    }

    // === __LINKEDIT ===
    if (chainedFixupsData.Length > 0) {
      writer.Write(chainedFixupsData);
      currentPos = (uint)ms.Position;
      if (currentPos < exportsTrieOff) writer.Write(new byte[exportsTrieOff - currentPos]);
    }
    if (exportsTrieData.Length > 0) {
      writer.Write(exportsTrieData);
      currentPos = (uint)ms.Position;
      if (currentPos < strtabOff) writer.Write(new byte[strtabOff - currentPos]);
    }
    writer.Write(new byte[strtabSize]);

    // Pad to code signature offset
    currentPos = (uint)ms.Position;
    if (currentPos < codeSignatureOff) writer.Write(new byte[codeSignatureOff - currentPos]);

    // === Build ad-hoc code signature ===
    var fileData = ms.GetBuffer();

    // Compute SHA-256 hash of each 4096-byte page
    var pageHashes = new byte[nCodeSlots * CS_SHA256_LEN];
    for (int slot = 0; slot < (int)nCodeSlots; slot++) {
      int pageStart = slot * (int)hashPageSize;
      int pageEnd = Math.Min(pageStart + (int)hashPageSize, (int)codeLimit);
      int pageLen = pageEnd - pageStart;
      var hash = SHA256.HashData(fileData.AsSpan(pageStart, pageLen));
      Array.Copy(hash, 0, pageHashes, slot * CS_SHA256_LEN, CS_SHA256_LEN);
    }

    // Write SuperBlob
    var sigStream = new MemoryStream((int)codeSignatureSize);
    var sw = new BinaryWriter(sigStream);

    // SuperBlob header (big-endian)
    WriteBE32(sw, CSMAGIC_EMBEDDED_SIGNATURE);
    WriteBE32(sw, codeSignatureSize);
    WriteBE32(sw, 1); // count = 1 blob

    // BlobIndex[0]
    WriteBE32(sw, CSSLOT_CODEDIRECTORY);
    WriteBE32(sw, superBlobHeaderSize); // offset to CodeDirectory within SuperBlob

    // CodeDirectory (big-endian)
    WriteBE32(sw, CSMAGIC_CODEDIRECTORY);
    WriteBE32(sw, cdSize);        // length
    WriteBE32(sw, 0x20400);       // version (supports execSeg fields)
    WriteBE32(sw, CS_ADHOC | CS_LINKER_SIGNED); // flags
    WriteBE32(sw, hashOffset);    // hashOffset (from start of CodeDirectory)
    WriteBE32(sw, cdHeaderSize);  // identOffset (identifier right after header)
    WriteBE32(sw, 0);             // nSpecialSlots
    WriteBE32(sw, nCodeSlots);    // nCodeSlots
    WriteBE32(sw, codeLimit);     // codeLimit
    sw.Write((byte)CS_SHA256_LEN);// hashSize
    sw.Write((byte)CS_HASHTYPE_SHA256); // hashType
    sw.Write((byte)0);            // platform
    sw.Write((byte)CS_PAGE_SIZE_LOG2); // pageSize (log2)
    WriteBE32(sw, 0);             // spare2
    WriteBE32(sw, 0);             // scatterOffset
    WriteBE32(sw, 0);             // teamOffset (v20200)
    WriteBE32(sw, 0);             // spare3 (v20300)
    WriteBE64(sw, 0);             // codeLimit64 (v20300)
    WriteBE64(sw, 0);             // execSegBase (v20400)
    WriteBE64(sw, textSegmentFileSize); // execSegLimit (v20400)
    WriteBE64(sw, 0x1);           // execSegFlags (CS_EXECSEG_MAIN_BINARY)

    // Identifier (null-terminated)
    sw.Write(identifierBytes);
    sw.Write((byte)0);

    // Page hashes
    sw.Write(pageHashes);

    var sigData = sigStream.ToArray();
    writer.Write(sigData);

    // Pad to end of __LINKEDIT
    currentPos = (uint)ms.Position;
    var linkeditEnd = linkeditFileOff + linkeditFileSize;
    if (currentPos < (uint)linkeditEnd) writer.Write(new byte[(uint)linkeditEnd - currentPos]);

    // === Write buffer to disk ===
    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
    var finalData = ms.ToArray();
    fs.Write(finalData, 0, finalData.Length);

    // Set executable permission on macOS/Linux
    if (!OperatingSystem.IsWindows()) {
      File.SetUnixFileMode(path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    Logger.Debug(LogCategory.Pe, "Mach-O write complete");
  }

  private static void WriteBE32(BinaryWriter w, uint value) {
    w.Write((byte)(value >> 24));
    w.Write((byte)(value >> 16));
    w.Write((byte)(value >> 8));
    w.Write((byte)value);
  }

  private static void WriteBE64(BinaryWriter w, ulong value) {
    WriteBE32(w, (uint)(value >> 32));
    WriteBE32(w, (uint)value);
  }

  private static void WriteSegmentCommand(BinaryWriter writer, string segname,
    ulong vmaddr, ulong vmsize, ulong fileoff, ulong filesize,
    int maxprot, int initprot, uint nsects) {
    writer.Write(LC_SEGMENT_64);
    writer.Write(72u + 80u * nsects);
    var nameBytes = new byte[16];
    var rawName = Encoding.ASCII.GetBytes(segname);
    Array.Copy(rawName, nameBytes, Math.Min(rawName.Length, 16));
    writer.Write(nameBytes);
    writer.Write(vmaddr); writer.Write(vmsize);
    writer.Write(fileoff); writer.Write(filesize);
    writer.Write(maxprot); writer.Write(initprot);
    writer.Write(nsects); writer.Write(0u);
  }

  private static void WriteSectionHeader(BinaryWriter writer, string sectname, string segname,
    ulong addr, ulong size, uint offset, uint align, uint flags) {
    var sectBytes = new byte[16];
    Array.Copy(Encoding.ASCII.GetBytes(sectname), sectBytes, Math.Min(Encoding.ASCII.GetByteCount(sectname), 16));
    writer.Write(sectBytes);
    var segBytes = new byte[16];
    Array.Copy(Encoding.ASCII.GetBytes(segname), segBytes, Math.Min(Encoding.ASCII.GetByteCount(segname), 16));
    writer.Write(segBytes);
    writer.Write(addr); writer.Write(size);
    writer.Write(offset); writer.Write(align);
    writer.Write(0u); writer.Write(0u); // reloff, nreloc
    writer.Write(flags);
    writer.Write(0u); writer.Write(0u); writer.Write(0u); // reserved1-3
  }

  private static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) & ~(alignment - 1);
  private static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);
}
