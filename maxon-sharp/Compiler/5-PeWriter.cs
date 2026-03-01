using System.Text;
using MaxonSharp.Compiler.Mlir;

namespace MaxonSharp.Compiler;

public class PeWriter {
  // PE constants
  private const uint FileAlignment = 0x200;      // 512 bytes
  private const uint SectionAlignment = 0x1000;  // 4096 bytes
  private const ulong ImageBase = 0x140000000;   // Default for 64-bit

  public static void Write(string path, byte[] code, byte[]? rdata = null, byte[]? data = null, IReadOnlyList<ImportEntry>? imports = null, byte[]? symdata = null, IReadOnlyList<CoffSymbol>? coffSymbols = null) {
    Logger.Debug(LogCategory.Pe, $"Writing PE file: {path}");
    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(fs);

    rdata ??= [];
    data ??= [];
    imports ??= [];
    symdata ??= [];

    var hasRdata = rdata.Length > 0;
    var hasData = data.Length > 0;
    var hasSymdata = symdata.Length > 0;
    var hasImports = imports.Count > 0;

    // Build import section data if needed
    byte[] idataSection = [];
    uint iatRva = 0;
    uint importDirRva = 0;
    uint importDirSize = 0;

    if (hasImports) {
      (idataSection, iatRva, importDirRva, importDirSize) = BuildImportSection(imports);
    }

    // Calculate sizes
    var dosHeaderSize = 64u;
    var peSignatureOffset = dosHeaderSize;
    var coffHeaderSize = 20u;
    var optionalHeaderSize = 240u;  // PE32+ optional header
    var sectionHeaderSize = 40u;
    var numSections = 1u;
    if (hasRdata) numSections++;
    if (hasData) numSections++;
    if (hasSymdata) numSections++;
    if (hasImports) numSections++;

    var headersSize = dosHeaderSize + 4 + coffHeaderSize + optionalHeaderSize + (sectionHeaderSize * numSections);
    var headersAligned = AlignUp(headersSize, FileAlignment);

    var codeSize = (uint)code.Length;
    var codeSizeAligned = AlignUp(codeSize, FileAlignment);
    var codeSizeVirtual = AlignUp(codeSize, SectionAlignment);

    var rdataSize = (uint)rdata.Length;
    var rdataSizeAligned = hasRdata ? AlignUp(rdataSize, FileAlignment) : 0;
    var rdataSizeVirtual = hasRdata ? AlignUp(rdataSize, SectionAlignment) : 0;

    var dataSize = (uint)data.Length;
    var dataSizeAligned = hasData ? AlignUp(dataSize, FileAlignment) : 0;
    var dataSizeVirtual = hasData ? AlignUp(dataSize, SectionAlignment) : 0;

    var symdataSize = (uint)symdata.Length;
    var symdataSizeAligned = hasSymdata ? AlignUp(symdataSize, FileAlignment) : 0;
    var symdataSizeVirtual = hasSymdata ? AlignUp(symdataSize, SectionAlignment) : 0;

    var idataSize = (uint)idataSection.Length;
    var idataSizeAligned = hasImports ? AlignUp(idataSize, FileAlignment) : 0;
    var idataSizeVirtual = hasImports ? AlignUp(idataSize, SectionAlignment) : 0;

    // Section RVAs: .text -> .rdata -> .data -> .symtab -> .idata
    var textRva = SectionAlignment;  // .text section starts at section alignment
    var rdataRva = textRva + codeSizeVirtual;  // .rdata section follows .text
    var dataRva = rdataRva + rdataSizeVirtual;  // .data section follows .rdata
    var symdataRva = dataRva + dataSizeVirtual;  // .symtab section follows .data
    var idataRva = symdataRva + symdataSizeVirtual;  // .idata section follows .symtab

    // Adjust RVAs in import section data for actual position
    if (hasImports) {
      iatRva += idataRva;
      importDirRva += idataRva;
      FixupImportSection(ref idataSection, idataRva);
    }

    // Calculate image size (last section RVA + its virtual size)
    var imageSize = idataRva + idataSizeVirtual;
    if (!hasImports) imageSize = symdataRva + symdataSizeVirtual;
    if (!hasSymdata && !hasImports) imageSize = dataRva + dataSizeVirtual;
    if (!hasData && !hasSymdata && !hasImports) imageSize = rdataRva + rdataSizeVirtual;
    if (!hasRdata && !hasData && !hasSymdata && !hasImports) imageSize = textRva + codeSizeVirtual;

    Logger.Debug(LogCategory.Pe, $"Code section: {codeSize} bytes at RVA 0x{textRva:X}");
    if (hasRdata) Logger.Debug(LogCategory.Pe, $"Rdata section: {rdataSize} bytes at RVA 0x{rdataRva:X}");
    if (hasData) Logger.Debug(LogCategory.Pe, $"Data section: {dataSize} bytes at RVA 0x{dataRva:X}");
    if (hasSymdata) Logger.Debug(LogCategory.Pe, $"Symdata section: {symdataSize} bytes at RVA 0x{symdataRva:X}");
    if (hasImports) Logger.Debug(LogCategory.Pe, $"Import section: {idataSize} bytes at RVA 0x{idataRva:X}");

    // DOS Header
    writer.Write((ushort)0x5A4D);  // e_magic "MZ"
    writer.Write(new byte[58]);     // Rest of DOS header
    writer.Write((uint)peSignatureOffset);  // e_lfanew (offset to PE header)

    // PE Signature
    writer.Write((uint)0x00004550);  // "PE\0\0"

    // COFF Header
    writer.Write((ushort)0x8664);    // Machine: AMD64
    writer.Write((ushort)numSections);  // NumberOfSections
    writer.Write((uint)0);           // TimeDateStamp
    writer.Write((uint)0);           // PointerToSymbolTable
    writer.Write((uint)0);           // NumberOfSymbols
    writer.Write((ushort)optionalHeaderSize);  // SizeOfOptionalHeader
    writer.Write((ushort)0x22);      // Characteristics: EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE

    // Optional Header (PE32+)
    writer.Write((ushort)0x20B);     // Magic: PE32+
    writer.Write((byte)14);          // MajorLinkerVersion
    writer.Write((byte)0);           // MinorLinkerVersion
    writer.Write(codeSizeAligned);   // SizeOfCode
    writer.Write(dataSizeAligned + symdataSizeAligned + idataSizeAligned);  // SizeOfInitializedData
    writer.Write((uint)0);           // SizeOfUninitializedData
    writer.Write(textRva);           // AddressOfEntryPoint (RVA of code)
    writer.Write(textRva);           // BaseOfCode

    // PE32+ additional fields
    writer.Write(ImageBase);         // ImageBase
    writer.Write(SectionAlignment);  // SectionAlignment
    writer.Write(FileAlignment);     // FileAlignment
    writer.Write((ushort)6);         // MajorOperatingSystemVersion
    writer.Write((ushort)0);         // MinorOperatingSystemVersion
    writer.Write((ushort)0);         // MajorImageVersion
    writer.Write((ushort)0);         // MinorImageVersion
    writer.Write((ushort)6);         // MajorSubsystemVersion
    writer.Write((ushort)0);         // MinorSubsystemVersion
    writer.Write((uint)0);           // Win32VersionValue
    writer.Write(imageSize);         // SizeOfImage
    writer.Write(headersAligned);    // SizeOfHeaders
    writer.Write((uint)0);           // CheckSum
    writer.Write((ushort)3);         // Subsystem: CONSOLE
    writer.Write((ushort)0x8160);    // DllCharacteristics: DYNAMIC_BASE | NX_COMPAT | TERMINAL_SERVER_AWARE | HIGH_ENTROPY_VA
    writer.Write((ulong)0x100000);   // SizeOfStackReserve
    writer.Write((ulong)0x1000);     // SizeOfStackCommit
    writer.Write((ulong)0x100000);   // SizeOfHeapReserve
    writer.Write((ulong)0x1000);     // SizeOfHeapCommit
    writer.Write((uint)0);           // LoaderFlags
    writer.Write((uint)16);          // NumberOfRvaAndSizes

    // Data directories (16 entries)
    // 0: Export Table
    writer.Write((uint)0); writer.Write((uint)0);
    // 1: Import Table
    writer.Write(hasImports ? importDirRva : 0u);
    writer.Write(hasImports ? importDirSize : 0u);
    // 2-11: Other directories (zeros)
    for (int i = 2; i < 12; i++) {
      writer.Write((uint)0); writer.Write((uint)0);
    }
    // 12: IAT (Import Address Table) — size includes per-DLL null terminators
    var numDllGroups = hasImports ? imports.GroupBy(i => i.DllName).Count() : 0;
    writer.Write(hasImports ? iatRva : 0u);
    writer.Write(hasImports ? (uint)((imports.Count + numDllGroups) * 8) : 0u);
    // 13-15: Remaining directories (zeros)
    for (int i = 13; i < 16; i++) {
      writer.Write((uint)0); writer.Write((uint)0);
    }

    // Section Header: .text
    WriteSectionHeader(writer, ".text", codeSize, textRva, codeSizeAligned, headersAligned, 0x60000020);

    // Section Header: .rdata (if present) - READ-ONLY data
    uint currentRawDataPos = headersAligned + codeSizeAligned;
    if (hasRdata) {
      // 0x40000040 = IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ
      WriteSectionHeader(writer, ".rdata", rdataSize, rdataRva, rdataSizeAligned, currentRawDataPos, 0x40000040);
      currentRawDataPos += rdataSizeAligned;
    }

    // Section Header: .data (if present)
    if (hasData) {
      WriteSectionHeader(writer, ".data", dataSize, dataRva, dataSizeAligned, currentRawDataPos, 0xC0000040);
      currentRawDataPos += dataSizeAligned;
    }

    // Section Header: .symtab (if present) — read-write because runtime counters (e.g. scope depth) are updated at execution time
    if (hasSymdata) {
      WriteSectionHeader(writer, ".symtab", symdataSize, symdataRva, symdataSizeAligned, currentRawDataPos, 0xC0000040);
      currentRawDataPos += symdataSizeAligned;
    }

    // Section Header: .idata (if present)
    if (hasImports) {
      WriteSectionHeader(writer, ".idata", idataSize, idataRva, idataSizeAligned, currentRawDataPos, 0xC0000040);
    }

    // Padding to align headers
    var currentPos = (uint)fs.Position;
    var padding = headersAligned - currentPos;
    writer.Write(new byte[padding]);

    // .text section (code)
    writer.Write(code);

    // Padding to align code section
    var codePadding = codeSizeAligned - codeSize;
    if (codePadding > 0) {
      writer.Write(new byte[codePadding]);
    }

    // .rdata section (read-only constants)
    if (hasRdata) {
      writer.Write(rdata);
      var rdataPadding = rdataSizeAligned - rdataSize;
      if (rdataPadding > 0) {
        writer.Write(new byte[rdataPadding]);
      }
    }

    // .data section (globals)
    if (hasData) {
      writer.Write(data);
      var dataPadding = dataSizeAligned - dataSize;
      if (dataPadding > 0) {
        writer.Write(new byte[dataPadding]);
      }
    }

    // .symtab section (symbol table for stack traces)
    if (hasSymdata) {
      writer.Write(symdata);
      var symdataPadding = symdataSizeAligned - symdataSize;
      if (symdataPadding > 0) {
        writer.Write(new byte[symdataPadding]);
      }
    }

    // .idata section (imports)
    if (hasImports) {
      writer.Write(idataSection);
      var idataPadding = idataSizeAligned - idataSize;
      if (idataPadding > 0) {
        writer.Write(new byte[idataPadding]);
      }
    }

    // COFF symbol table — appended after all sections, referenced by COFF header
    if (coffSymbols != null && coffSymbols.Count > 0) {
      WriteCoffSymbolTable(fs, writer, coffSymbols, codeSize,
        hasRdata, rdataSize, hasData, dataSize, hasSymdata, symdataSize, hasImports, idataSize);
    }

    Logger.Debug(LogCategory.Pe, "PE write complete");
  }

  /// <summary>
  /// Writes the COFF symbol table and string table after all sections, then backpatches
  /// PointerToSymbolTable and NumberOfSymbols in the COFF header.
  /// </summary>
  private static void WriteCoffSymbolTable(FileStream fs, BinaryWriter writer,
    IReadOnlyList<CoffSymbol> coffSymbols, uint codeSize,
    bool hasRdata, uint rdataSize, bool hasData, uint dataSize,
    bool hasSymdata, uint symdataSize, bool hasImports, uint idataSize) {

    var symbolTableOffset = (uint)fs.Position;

    // COFF string table holds names that don't fit in the 8-byte inline field
    var stringTableEntries = new List<byte>();
    // Reserve space for the 4-byte size prefix (patched after all names are added)
    stringTableEntries.AddRange(new byte[4]);

    // Must match the order sections were written so section numbers are correct
    var sectionNames = new List<string> { ".text" };
    if (hasRdata) sectionNames.Add(".rdata");
    if (hasData) sectionNames.Add(".data");
    if (hasSymdata) sectionNames.Add(".symtab");
    if (hasImports) sectionNames.Add(".idata");

    var sectionSizes = new List<uint> { codeSize };
    if (hasRdata) sectionSizes.Add(rdataSize);
    if (hasData) sectionSizes.Add(dataSize);
    if (hasSymdata) sectionSizes.Add(symdataSize);
    if (hasImports) sectionSizes.Add(idataSize);

    var totalSymbolCount = 0u;

    // Write section symbols (one per section, each with one auxiliary record)
    for (int i = 0; i < sectionNames.Count; i++) {
      WriteCoffSymbolEntry(writer, stringTableEntries, sectionNames[i],
        value: 0,
        sectionNumber: (ushort)(i + 1),
        type: 0x0000,
        storageClass: 3,  // IMAGE_SYM_CLASS_STATIC
        numAux: 1);
      totalSymbolCount++;

      // Auxiliary format 5: section definition
      writer.Write(sectionSizes[i]);    // Length
      writer.Write((ushort)0);          // NumberOfRelocations
      writer.Write((ushort)0);          // NumberOfLinenumbers
      writer.Write((uint)0);            // CheckSum
      writer.Write((ushort)0);          // Number (COMDAT)
      writer.Write((byte)0);            // Selection
      writer.Write(new byte[3]);        // Unused
      totalSymbolCount++;
    }

    // Write function symbols (all in .text section = section 1)
    foreach (var sym in coffSymbols) {
      WriteCoffSymbolEntry(writer, stringTableEntries, sym.Name,
        value: (uint)sym.CodeOffset,
        sectionNumber: 1,               // .text section
        type: 0x0020,                    // IMAGE_SYM_DTYPE_FUNCTION
        storageClass: 2,                 // IMAGE_SYM_CLASS_EXTERNAL
        numAux: 0);
      totalSymbolCount++;
    }

    // Write the COFF string table (must immediately follow symbol entries)
    var stringTableBytes = stringTableEntries.ToArray();
    BitConverter.GetBytes((uint)stringTableBytes.Length).CopyTo(stringTableBytes, 0);
    writer.Write(stringTableBytes);

    // Backpatch COFF header: PointerToSymbolTable (offset 76) and NumberOfSymbols (offset 80)
    // COFF header layout: DOS(64) + PE_sig(4) + Machine(2) + NumSections(2) + TimeDateStamp(4) + PointerToSymbolTable(4)
    const long pointerToSymbolTableOffset = 64 + 4 + 8;   // = 76
    const long numberOfSymbolsOffset = 64 + 4 + 12;       // = 80

    var endPos = fs.Position;
    fs.Seek(pointerToSymbolTableOffset, SeekOrigin.Begin);
    writer.Write(symbolTableOffset);
    fs.Seek(numberOfSymbolsOffset, SeekOrigin.Begin);
    writer.Write(totalSymbolCount);
    fs.Seek(endPos, SeekOrigin.Begin);

    Logger.Debug(LogCategory.Pe, $"COFF symbol table: {totalSymbolCount} entries at file offset 0x{symbolTableOffset:X}");
  }

  /// <summary>
  /// Writes one 18-byte COFF symbol table entry. Names longer than 8 bytes are added to the string table.
  /// </summary>
  private static void WriteCoffSymbolEntry(BinaryWriter writer, List<byte> stringTable,
    string name, uint value, ushort sectionNumber, ushort type, byte storageClass, byte numAux) {
    var nameBytes = Encoding.ASCII.GetBytes(name);
    if (nameBytes.Length <= 8) {
      // Short name: inline in the 8-byte field, null-padded
      var buf = new byte[8];
      Array.Copy(nameBytes, buf, nameBytes.Length);
      writer.Write(buf);
    } else {
      // Long name: 4 zero bytes + 4-byte offset into string table
      writer.Write((uint)0);
      writer.Write((uint)stringTable.Count);
      stringTable.AddRange(nameBytes);
      stringTable.Add(0);  // null terminator
    }
    writer.Write(value);
    writer.Write(sectionNumber);
    writer.Write(type);
    writer.Write(storageClass);
    writer.Write(numAux);
  }

  private static void WriteSectionHeader(BinaryWriter writer, string name, uint virtualSize, uint virtualAddress,
    uint sizeOfRawData, uint pointerToRawData, uint characteristics) {
    var sectionName = new byte[8];
    var nameBytes = Encoding.ASCII.GetBytes(name);
    Array.Copy(nameBytes, sectionName, Math.Min(nameBytes.Length, 8));
    writer.Write(sectionName);
    writer.Write(virtualSize);
    writer.Write(virtualAddress);
    writer.Write(sizeOfRawData);
    writer.Write(pointerToRawData);
    writer.Write((uint)0);  // PointerToRelocations
    writer.Write((uint)0);  // PointerToLinenumbers
    writer.Write((ushort)0);  // NumberOfRelocations
    writer.Write((ushort)0);  // NumberOfLinenumbers
    writer.Write(characteristics);
  }

  /// <summary>
  /// Builds the import section (.idata) for the given imports.
  /// Returns the section data and the relative offsets of IAT and import directory within the section.
  /// All RVAs are relative to the start of the .idata section (will be adjusted later).
  /// </summary>
  private static (byte[] data, uint iatOffset, uint importDirOffset, uint importDirSize) BuildImportSection(
    IReadOnlyList<ImportEntry> imports) {
    // Group imports by DLL
    var dllGroups = imports.GroupBy(i => i.DllName).ToList();

    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // Layout:
    // 1. IAT (Import Address Table) - array of 64-bit pointers (one per import + null terminator per DLL)
    // 2. ILT (Import Lookup Table) - same structure as IAT
    // 3. Import Directory Table - one entry per DLL + null terminator
    // 4. Hint/Name Table - hint (2 bytes) + function name (null-terminated) for each import
    // 5. DLL names (null-terminated strings)

    // Calculate sizes and offsets (relative to section start)
    var totalImports = imports.Count;
    var totalDlls = dllGroups.Count;

    // IAT: 8 bytes per import + 8 bytes null terminator per DLL
    var iatSize = (totalImports + totalDlls) * 8;
    var iatOffset = 0u;

    // ILT: same as IAT
    var iltOffset = (uint)iatSize;
    var iltSize = iatSize;

    // Import Directory: 20 bytes per DLL + 20 bytes null terminator
    var importDirOffset = iltOffset + (uint)iltSize;
    var importDirSize = (uint)((totalDlls + 1) * 20);

    // Hint/Name Table: calculate size
    var hintNameOffset = importDirOffset + importDirSize;

    // Pre-calculate hint/name offsets for each function (unique by function name)
    var hintNameOffsets = new Dictionary<string, uint>();
    var currentHintOffset = hintNameOffset;
    foreach (var import in imports) {
      if (!hintNameOffsets.ContainsKey(import.FunctionName)) {
        hintNameOffsets[import.FunctionName] = currentHintOffset;
        currentHintOffset += 2;  // Hint
        currentHintOffset += (uint)(import.FunctionName.Length + 1);  // Name + null
        if (currentHintOffset % 2 != 0) currentHintOffset++;  // Padding
      }
    }
    var hintNameEndOffset = currentHintOffset;

    // DLL names: calculate offsets
    var dllNameOffsets = new Dictionary<string, uint>();
    var currentDllOffset = hintNameEndOffset;
    foreach (var group in dllGroups) {
      dllNameOffsets[group.Key] = currentDllOffset;
      currentDllOffset += (uint)(group.Key.Length + 1);
    }

    // Track positions that need RVA fixup
    var qwordFixups = new List<long>();  // 64-bit RVAs in IAT/ILT
    var dwordFixups = new List<long>();  // 32-bit RVAs in Import Directory

    // 1. Write IAT (entries will be filled by loader, but we need to point to hint/name)
    foreach (var group in dllGroups) {
      foreach (var import in group) {
        qwordFixups.Add(ms.Position);
        bw.Write((ulong)hintNameOffsets[import.FunctionName]);
      }
      bw.Write((ulong)0);  // Null terminator for this DLL's entries
    }

    // 2. Write ILT (same as IAT before loading)
    foreach (var group in dllGroups) {
      foreach (var import in group) {
        qwordFixups.Add(ms.Position);
        bw.Write((ulong)hintNameOffsets[import.FunctionName]);
      }
      bw.Write((ulong)0);  // Null terminator
    }

    // 3. Write Import Directory Table
    var currentIatRva = iatOffset;
    var currentIltRva = iltOffset;
    foreach (var group in dllGroups) {
      dwordFixups.Add(ms.Position);
      bw.Write(currentIltRva);  // OriginalFirstThunk (ILT RVA)
      bw.Write((uint)0);  // TimeDateStamp
      bw.Write((uint)0);  // ForwarderChain
      dwordFixups.Add(ms.Position);
      bw.Write(dllNameOffsets[group.Key]);  // Name RVA
      dwordFixups.Add(ms.Position);
      bw.Write(currentIatRva);  // FirstThunk (IAT RVA)

      var entryCount = group.Count() + 1;  // +1 for null terminator
      currentIatRva += (uint)(entryCount * 8);
      currentIltRva += (uint)(entryCount * 8);
    }
    // Null terminator entry
    bw.Write((uint)0);
    bw.Write((uint)0);
    bw.Write((uint)0);
    bw.Write((uint)0);
    bw.Write((uint)0);

    // 4. Write Hint/Name Table
    var writtenFunctions = new HashSet<string>();
    foreach (var import in imports) {
      if (!writtenFunctions.Contains(import.FunctionName)) {
        bw.Write((ushort)0);  // Hint (we don't know it, use 0)
        bw.Write(Encoding.ASCII.GetBytes(import.FunctionName));
        bw.Write((byte)0);  // Null terminator
        if (ms.Position % 2 != 0) bw.Write((byte)0);  // Padding
        writtenFunctions.Add(import.FunctionName);
      }
    }

    // 5. Write DLL names
    foreach (var group in dllGroups) {
      bw.Write(Encoding.ASCII.GetBytes(group.Key));
      bw.Write((byte)0);  // Null terminator
    }

    var data = ms.ToArray();

    // Apply RVA fixups - these are relative to section start, actual fixup happens later
    // Store the fixup info in the data for later processing
    // Actually, we'll store the fixup lists and apply in FixupImportSection

    return (data, iatOffset, importDirOffset, importDirSize);
  }

  /// <summary>
  /// Fixes up RVAs in the import section data once the actual .idata RVA is known.
  /// </summary>
  private static void FixupImportSection(ref byte[] data, uint idataRva) {
    // All RVAs in the import section are currently relative to section start.
    // We need to add idataRva to convert them to actual image RVAs.

    // The structure is:
    // - IAT: 8-byte entries with RVAs to Hint/Name (non-zero entries need fixup)
    // - ILT: 8-byte entries with RVAs to Hint/Name (non-zero entries need fixup)
    // - Import Directory: 20-byte entries where fields 0, 3, 4 are RVAs needing fixup

    // Simple approach: scan for 8-byte entries until we hit a pattern that looks like Import Directory
    // An Import Directory entry has 5 dwords, and the values are small (< section size)

    using var ms = new MemoryStream(data);
    using var reader = new BinaryReader(ms);
    using var writer = new BinaryWriter(ms);

    // Find where the 8-byte entries end by looking for the Import Directory pattern
    // Import Directory starts where we see small values in 32-bit units that make sense as RVAs
    // The IAT/ILT entries point to Hint/Name which are after Import Directory

    // Scan for non-zero 8-byte entries
    ms.Position = 0;
    while (ms.Position + 8 <= data.Length) {
      var pos = ms.Position;
      var val = reader.ReadUInt64();

      // If this looks like two 32-bit values that are both small and non-zero,
      // we've probably hit the Import Directory
      if (val != 0) {
        var low = (uint)(val & 0xFFFFFFFF);
        var high = (uint)(val >> 32);
        // Import Directory's first entry has: ILT RVA (small), TimeDateStamp (0)
        // So high would be 0 for the first entry
        if (high == 0 && low < data.Length) {
          // This could be start of Import Directory
          // Check if next 12 bytes look like (0, 0, NameRVA)
          if (ms.Position + 12 <= data.Length) {
            var next8 = reader.ReadUInt64();
            var nextLow = (uint)(next8 & 0xFFFFFFFF);
            var nextHigh = (uint)(next8 >> 32);
            // ForwarderChain is usually 0, NameRVA should be small
            if (nextLow == 0 && nextHigh < (uint)data.Length && nextHigh > low) {
              // This is likely Import Directory start
              ms.Position = pos;
              break;
            }
            ms.Position = pos + 8;  // Continue scanning
          }
        }
      }
    }

    // Record where Import Directory starts
    var importDirStart = ms.Position;

    // Go back and fix up all 8-byte entries (IAT + ILT)
    ms.Position = 0;
    while (ms.Position < importDirStart) {
      var pos = ms.Position;
      var val = reader.ReadUInt64();
      if (val != 0) {
        ms.Position = pos;
        writer.Write(val + idataRva);
      }
    }

    // Now fix up Import Directory entries
    // Each entry is 20 bytes: ILT_RVA(4), Timestamp(4), Forwarder(4), Name_RVA(4), IAT_RVA(4)
    // We need to fix: ILT_RVA (offset 0), Name_RVA (offset 12), IAT_RVA (offset 16)
    ms.Position = importDirStart;
    while (ms.Position + 20 <= data.Length) {
      var entryStart = ms.Position;

      var iltRva = reader.ReadUInt32();
      var timestamp = reader.ReadUInt32();
      var forwarder = reader.ReadUInt32();
      var nameRva = reader.ReadUInt32();
      var iatRva = reader.ReadUInt32();

      // Null terminator entry
      if (iltRva == 0 && timestamp == 0 && forwarder == 0 && nameRva == 0 && iatRva == 0) {
        break;
      }

      // Fix up RVAs
      ms.Position = entryStart;
      writer.Write(iltRva + idataRva);  // ILT RVA
      writer.Write(timestamp);  // Timestamp (no fixup)
      writer.Write(forwarder);  // Forwarder (no fixup)
      writer.Write(nameRva + idataRva);  // Name RVA
      writer.Write(iatRva + idataRva);  // IAT RVA
    }

    data = ms.ToArray();
  }

  private static uint AlignUp(uint value, uint alignment) {
    return (value + alignment - 1) & ~(alignment - 1);
  }
}
