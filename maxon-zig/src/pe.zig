const std = @import("std");

// PE file constants
const DOS_SIGNATURE: u16 = 0x5A4D; // MZ
const PE_SIGNATURE: u32 = 0x00004550; // PE\0\0
const IMAGE_FILE_MACHINE_AMD64: u16 = 0x8664;
const IMAGE_FILE_EXECUTABLE_IMAGE: u16 = 0x0002;
const IMAGE_FILE_LARGE_ADDRESS_AWARE: u16 = 0x0020;
const IMAGE_SUBSYSTEM_CONSOLE: u16 = 3;
const IMAGE_DLLCHARACTERISTICS_NX_COMPAT: u16 = 0x0100;
const IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE: u16 = 0x0040;
const IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE: u16 = 0x8000;
const IMAGE_SCN_CNT_CODE: u32 = 0x00000020;
const IMAGE_SCN_MEM_EXECUTE: u32 = 0x20000000;
const IMAGE_SCN_MEM_READ: u32 = 0x40000000;
const IMAGE_SCN_CNT_INITIALIZED_DATA: u32 = 0x00000040;

const SECTION_ALIGNMENT: u32 = 0x1000;
const FILE_ALIGNMENT: u32 = 0x200;
const IMAGE_BASE: u64 = 0x140000000;

pub fn writePE(path: []const u8, code: []const u8) !void {
    const file = try std.fs.cwd().createFile(path, .{});
    defer file.close();

    // Calculate sizes and offsets
    const dos_header_size: u32 = 64;
    const pe_header_offset: u32 = dos_header_size;
    const coff_header_size: u32 = 20;
    const optional_header_size: u32 = 240; // PE32+ optional header
    const section_header_size: u32 = 40;
    const num_sections: u16 = 2; // .text and .idata
    const headers_size: u32 = pe_header_offset + 4 + coff_header_size + optional_header_size + (section_header_size * num_sections);
    const headers_aligned: u32 = alignUp(headers_size, FILE_ALIGNMENT);

    // .text section
    const text_file_offset: u32 = headers_aligned;
    const text_rva: u32 = SECTION_ALIGNMENT;
    const text_raw_size: u32 = alignUp(@as(u32, @intCast(code.len)), FILE_ALIGNMENT);
    const text_virtual_size: u32 = @as(u32, @intCast(code.len));

    // .idata section (import data)
    const idata_file_offset: u32 = text_file_offset + text_raw_size;
    const idata_rva: u32 = text_rva + alignUp(text_raw_size, SECTION_ALIGNMENT);

    // Import directory structure (matching existing pe_writer.cpp layout)
    // Layout: IDT (40 bytes) -> IAT (16 bytes) -> INT (16 bytes) -> strings
    const idt_size: u32 = 40; // 2 entries * 20 bytes (1 real + 1 null terminator)
    const iat_offset: u32 = idt_size; // IAT right after IDT
    const int_offset: u32 = iat_offset + 16; // INT after IAT
    const strings_offset: u32 = int_offset + 16; // Strings after INT

    // String offsets within strings area
    const dll_name_rel: u32 = 0; // "kernel32.dll\0" = 13 bytes
    const hint_name_rel: u32 = 14; // align to 2, then 2-byte hint + "ExitProcess\0" = 14 bytes

    const dll_name_offset: u32 = strings_offset + dll_name_rel;
    const hint_name_offset: u32 = strings_offset + hint_name_rel;

    const idata_virtual_size: u32 = strings_offset + 14 + 14; // ~100 bytes
    const idata_raw_size: u32 = alignUp(idata_virtual_size, FILE_ALIGNMENT);

    // Total image size
    const image_size: u32 = alignUp(idata_rva + idata_raw_size, SECTION_ALIGNMENT);

    // Build buffer
    var buf: [4096]u8 = undefined;
    var pos: usize = 0;

    // --- DOS Header (64 bytes) ---
    writeU16(&buf, &pos, DOS_SIGNATURE); // e_magic
    writeU16(&buf, &pos, 0x90); // e_cblp
    writeU16(&buf, &pos, 0x03); // e_cp
    writeU16(&buf, &pos, 0x00); // e_crlc
    writeU16(&buf, &pos, 0x04); // e_cparhdr
    writeU16(&buf, &pos, 0x00); // e_minalloc
    writeU16(&buf, &pos, 0xFFFF); // e_maxalloc
    writeU16(&buf, &pos, 0x00); // e_ss
    writeU16(&buf, &pos, 0xB8); // e_sp
    writeU16(&buf, &pos, 0x00); // e_csum
    writeU16(&buf, &pos, 0x00); // e_ip
    writeU16(&buf, &pos, 0x00); // e_cs
    writeU16(&buf, &pos, 0x40); // e_lfarlc
    writeU16(&buf, &pos, 0x00); // e_ovno
    writeZeros(&buf, &pos, 8); // e_res[4]
    writeU16(&buf, &pos, 0x00); // e_oemid
    writeU16(&buf, &pos, 0x00); // e_oeminfo
    writeZeros(&buf, &pos, 20); // e_res2[10]
    writeU32(&buf, &pos, pe_header_offset); // e_lfanew

    // --- PE Signature ---
    writeU32(&buf, &pos, PE_SIGNATURE);

    // --- COFF File Header ---
    writeU16(&buf, &pos, IMAGE_FILE_MACHINE_AMD64); // Machine
    writeU16(&buf, &pos, num_sections); // NumberOfSections
    writeU32(&buf, &pos, 0); // TimeDateStamp
    writeU32(&buf, &pos, 0); // PointerToSymbolTable
    writeU32(&buf, &pos, 0); // NumberOfSymbols
    writeU16(&buf, &pos, optional_header_size); // SizeOfOptionalHeader
    writeU16(&buf, &pos, IMAGE_FILE_EXECUTABLE_IMAGE | IMAGE_FILE_LARGE_ADDRESS_AWARE); // Characteristics

    // --- Optional Header (PE32+) ---
    writeU16(&buf, &pos, 0x20B); // Magic (PE32+)
    buf[pos] = 14;
    pos += 1; // MajorLinkerVersion
    buf[pos] = 0;
    pos += 1; // MinorLinkerVersion
    writeU32(&buf, &pos, text_raw_size); // SizeOfCode
    writeU32(&buf, &pos, idata_raw_size); // SizeOfInitializedData
    writeU32(&buf, &pos, 0); // SizeOfUninitializedData
    writeU32(&buf, &pos, text_rva); // AddressOfEntryPoint
    writeU32(&buf, &pos, text_rva); // BaseOfCode

    // PE32+ specific fields
    writeU64(&buf, &pos, IMAGE_BASE); // ImageBase
    writeU32(&buf, &pos, SECTION_ALIGNMENT); // SectionAlignment
    writeU32(&buf, &pos, FILE_ALIGNMENT); // FileAlignment
    writeU16(&buf, &pos, 6); // MajorOperatingSystemVersion
    writeU16(&buf, &pos, 0); // MinorOperatingSystemVersion
    writeU16(&buf, &pos, 0); // MajorImageVersion
    writeU16(&buf, &pos, 0); // MinorImageVersion
    writeU16(&buf, &pos, 6); // MajorSubsystemVersion
    writeU16(&buf, &pos, 0); // MinorSubsystemVersion
    writeU32(&buf, &pos, 0); // Win32VersionValue
    writeU32(&buf, &pos, image_size); // SizeOfImage
    writeU32(&buf, &pos, headers_aligned); // SizeOfHeaders
    writeU32(&buf, &pos, 0); // CheckSum
    writeU16(&buf, &pos, IMAGE_SUBSYSTEM_CONSOLE); // Subsystem
    writeU16(&buf, &pos, IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE | IMAGE_DLLCHARACTERISTICS_NX_COMPAT | IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE); // DllCharacteristics
    writeU64(&buf, &pos, 0x100000); // SizeOfStackReserve
    writeU64(&buf, &pos, 0x1000); // SizeOfStackCommit
    writeU64(&buf, &pos, 0x100000); // SizeOfHeapReserve
    writeU64(&buf, &pos, 0x1000); // SizeOfHeapCommit
    writeU32(&buf, &pos, 0); // LoaderFlags
    writeU32(&buf, &pos, 16); // NumberOfRvaAndSizes

    // Data directories (16 entries, 8 bytes each = 128 bytes)
    // 0: Export Table
    writeU64(&buf, &pos, 0);
    // 1: Import Table
    writeU32(&buf, &pos, idata_rva); // Import Table RVA
    writeU32(&buf, &pos, 40); // Import Table Size (2 * 20 bytes)
    // 2: Resource Table
    writeU64(&buf, &pos, 0);
    // 3: Exception Table
    writeU64(&buf, &pos, 0);
    // 4: Certificate Table
    writeU64(&buf, &pos, 0);
    // 5: Base Relocation Table
    writeU64(&buf, &pos, 0);
    // 6: Debug
    writeU64(&buf, &pos, 0);
    // 7: Architecture
    writeU64(&buf, &pos, 0);
    // 8: Global Ptr
    writeU64(&buf, &pos, 0);
    // 9: TLS Table
    writeU64(&buf, &pos, 0);
    // 10: Load Config Table
    writeU64(&buf, &pos, 0);
    // 11: Bound Import
    writeU64(&buf, &pos, 0);
    // 12: IAT
    writeU32(&buf, &pos, idata_rva + iat_offset); // IAT RVA
    writeU32(&buf, &pos, 16); // IAT Size (2 entries * 8 bytes)
    // 13: Delay Import Descriptor
    writeU64(&buf, &pos, 0);
    // 14: CLR Runtime Header
    writeU64(&buf, &pos, 0);
    // 15: Reserved
    writeU64(&buf, &pos, 0);

    // --- Section Headers ---
    // .text section
    writeBytes(&buf, &pos, ".text\x00\x00\x00"); // Name (8 bytes)
    writeU32(&buf, &pos, text_virtual_size); // VirtualSize
    writeU32(&buf, &pos, text_rva); // VirtualAddress
    writeU32(&buf, &pos, text_raw_size); // SizeOfRawData
    writeU32(&buf, &pos, text_file_offset); // PointerToRawData
    writeU32(&buf, &pos, 0); // PointerToRelocations
    writeU32(&buf, &pos, 0); // PointerToLinenumbers
    writeU16(&buf, &pos, 0); // NumberOfRelocations
    writeU16(&buf, &pos, 0); // NumberOfLinenumbers
    writeU32(&buf, &pos, IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ); // Characteristics

    // .idata section
    writeBytes(&buf, &pos, ".idata\x00\x00"); // Name (8 bytes)
    writeU32(&buf, &pos, idata_virtual_size); // VirtualSize
    writeU32(&buf, &pos, idata_rva); // VirtualAddress
    writeU32(&buf, &pos, idata_raw_size); // SizeOfRawData
    writeU32(&buf, &pos, idata_file_offset); // PointerToRawData
    writeU32(&buf, &pos, 0); // PointerToRelocations
    writeU32(&buf, &pos, 0); // PointerToLinenumbers
    writeU16(&buf, &pos, 0); // NumberOfRelocations
    writeU16(&buf, &pos, 0); // NumberOfLinenumbers
    writeU32(&buf, &pos, IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ); // Characteristics

    // Pad to file alignment
    const padding_to_text = text_file_offset - pos;
    writeZeros(&buf, &pos, padding_to_text);

    // --- .text section ---
    // Write code with patched IAT reference
    // Code layout: sub rsp,40 (4) + mov ecx,imm (5) + call [rip+off] (6) = 15 bytes
    // The call instruction displacement is at offset 11 (after sub rsp + mov ecx + FF 15)
    // RIP after call = text_rva + 15
    const code_size: u32 = @intCast(code.len);
    const rip_after_call: i64 = @as(i64, text_rva) + code_size;
    const iat_address: i64 = @as(i64, idata_rva) + iat_offset;
    const call_offset: i32 = @intCast(iat_address - rip_after_call);

    // Write the code with patched offset
    // Code up to displacement: sub rsp,40 (4) + mov ecx,imm (5) + FF 15 (2) = 11 bytes
    writeBytes(&buf, &pos, code[0..11]);
    writeI32(&buf, &pos, call_offset); // patched offset

    // Pad .text section
    writeZeros(&buf, &pos, text_raw_size - code_size);

    // --- .idata section ---
    // Import Directory Table (IDT) - 2 entries: 1 for kernel32.dll, 1 null terminator
    // Entry for kernel32.dll
    writeU32(&buf, &pos, idata_rva + int_offset); // OriginalFirstThunk (INT RVA)
    writeU32(&buf, &pos, 0); // TimeDateStamp
    writeU32(&buf, &pos, 0); // ForwarderChain
    writeU32(&buf, &pos, idata_rva + dll_name_offset); // Name RVA
    writeU32(&buf, &pos, idata_rva + iat_offset); // FirstThunk (IAT RVA)

    // Null terminator entry
    writeZeros(&buf, &pos, 20);

    // Import Address Table (IAT) - loaded by Windows with actual addresses
    writeU64(&buf, &pos, idata_rva + hint_name_offset); // Hint/Name RVA
    writeU64(&buf, &pos, 0); // Null terminator

    // Import Name Table (INT) - same as IAT, used for binding
    writeU64(&buf, &pos, idata_rva + hint_name_offset); // Hint/Name RVA
    writeU64(&buf, &pos, 0); // Null terminator

    // DLL Name
    writeBytes(&buf, &pos, "kernel32.dll\x00");

    // Padding to align hint/name to 2 bytes
    buf[pos] = 0;
    pos += 1;

    // Hint/Name Table
    writeU16(&buf, &pos, 0); // Hint
    writeBytes(&buf, &pos, "ExitProcess\x00"); // Name

    // Pad .idata section
    const current_idata_written = pos - (text_file_offset + text_raw_size);
    const idata_padding = idata_raw_size - @as(u32, @intCast(current_idata_written));
    writeZeros(&buf, &pos, idata_padding);

    // Write everything to file
    try file.writeAll(buf[0..pos]);
}

fn writeU16(buf: []u8, pos: *usize, val: u16) void {
    const bytes = @as([2]u8, @bitCast(val));
    buf[pos.*] = bytes[0];
    buf[pos.* + 1] = bytes[1];
    pos.* += 2;
}

fn writeU32(buf: []u8, pos: *usize, val: u32) void {
    const bytes = @as([4]u8, @bitCast(val));
    @memcpy(buf[pos.* .. pos.* + 4], &bytes);
    pos.* += 4;
}

fn writeI32(buf: []u8, pos: *usize, val: i32) void {
    const bytes = @as([4]u8, @bitCast(val));
    @memcpy(buf[pos.* .. pos.* + 4], &bytes);
    pos.* += 4;
}

fn writeU64(buf: []u8, pos: *usize, val: u64) void {
    const bytes = @as([8]u8, @bitCast(val));
    @memcpy(buf[pos.* .. pos.* + 8], &bytes);
    pos.* += 8;
}

fn writeZeros(buf: []u8, pos: *usize, count: usize) void {
    @memset(buf[pos.* .. pos.* + count], 0);
    pos.* += count;
}

fn writeBytes(buf: []u8, pos: *usize, bytes: []const u8) void {
    @memcpy(buf[pos.* .. pos.* + bytes.len], bytes);
    pos.* += bytes.len;
}

fn alignUp(value: u32, alignment: u32) u32 {
    return (value + alignment - 1) & ~(alignment - 1);
}
