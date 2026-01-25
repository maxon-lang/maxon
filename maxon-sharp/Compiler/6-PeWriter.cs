namespace MaxonSharp.Compiler;

public class PeWriter {
	// PE constants
	private const uint FileAlignment = 0x200;      // 512 bytes
	private const uint SectionAlignment = 0x1000;  // 4096 bytes
	private const ulong ImageBase = 0x140000000;   // Default for 64-bit

	public static void Write(string path, byte[] code, byte[]? data = null) {
		Logger.Debug(LogCategory.Pe, $"Writing PE file: {path}");
		using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
		using var writer = new BinaryWriter(fs);

		data ??= [];
		var hasData = data.Length > 0;

		// Calculate sizes
		var dosHeaderSize = 64u;
		var peSignatureOffset = dosHeaderSize;
		var coffHeaderSize = 20u;
		var optionalHeaderSize = 240u;  // PE32+ optional header
		var sectionHeaderSize = 40u;
		var numSections = hasData ? 2u : 1u;

		var headersSize = dosHeaderSize + 4 + coffHeaderSize + optionalHeaderSize + (sectionHeaderSize * numSections);
		var headersAligned = AlignUp(headersSize, FileAlignment);

		var codeSize = (uint)code.Length;
		var codeSizeAligned = AlignUp(codeSize, FileAlignment);
		var codeSizeVirtual = AlignUp(codeSize, SectionAlignment);

		var dataSize = (uint)data.Length;
		var dataSizeAligned = AlignUp(dataSize, FileAlignment);
		var dataSizeVirtual = AlignUp(dataSize, SectionAlignment);

		var textRva = SectionAlignment;  // .text section starts at section alignment
		var dataRva = textRva + codeSizeVirtual;  // .data section follows .text
		var imageSize = hasData ? dataRva + dataSizeVirtual : textRva + codeSizeVirtual;

		Logger.Debug(LogCategory.Pe, $"Code section: {codeSize} bytes");
		if (hasData) Logger.Debug(LogCategory.Pe, $"Data section: {dataSize} bytes");

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
		writer.Write(hasData ? dataSizeAligned : 0u);  // SizeOfInitializedData
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

		// Data directories (16 entries, all zeros for minimal exe)
		for (int i = 0; i < 16; i++) {
			writer.Write((uint)0);  // VirtualAddress
			writer.Write((uint)0);  // Size
		}

		// Section Header: .text
		var sectionName = new byte[8];
		var textBytes = ".text"u8.ToArray();
		Array.Copy(textBytes, sectionName, textBytes.Length);
		writer.Write(sectionName);           // Name
		writer.Write(codeSize);              // VirtualSize
		writer.Write(textRva);               // VirtualAddress
		writer.Write(codeSizeAligned);       // SizeOfRawData
		writer.Write(headersAligned);        // PointerToRawData
		writer.Write((uint)0);               // PointerToRelocations
		writer.Write((uint)0);               // PointerToLinenumbers
		writer.Write((ushort)0);             // NumberOfRelocations
		writer.Write((ushort)0);             // NumberOfLinenumbers
		writer.Write((uint)0x60000020);      // Characteristics: CNT_CODE | MEM_EXECUTE | MEM_READ

		// Section Header: .data (if present)
		if (hasData) {
			var dataName = new byte[8];
			var dataBytes = ".data"u8.ToArray();
			Array.Copy(dataBytes, dataName, dataBytes.Length);
			writer.Write(dataName);              // Name
			writer.Write(dataSize);              // VirtualSize
			writer.Write(dataRva);               // VirtualAddress
			writer.Write(dataSizeAligned);       // SizeOfRawData
			writer.Write(headersAligned + codeSizeAligned);  // PointerToRawData
			writer.Write((uint)0);               // PointerToRelocations
			writer.Write((uint)0);               // PointerToLinenumbers
			writer.Write((ushort)0);             // NumberOfRelocations
			writer.Write((ushort)0);             // NumberOfLinenumbers
			writer.Write((uint)0xC0000040);      // Characteristics: CNT_INITIALIZED_DATA | MEM_READ | MEM_WRITE
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

		// .data section (globals)
		if (hasData) {
			writer.Write(data);
			var dataPadding = dataSizeAligned - dataSize;
			if (dataPadding > 0) {
				writer.Write(new byte[dataPadding]);
			}
		}

		Logger.Debug(LogCategory.Pe, "PE write complete");
	}

	private static uint AlignUp(uint value, uint alignment) {
		return (value + alignment - 1) & ~(alignment - 1);
	}
}
