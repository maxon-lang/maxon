const std = @import("std");
const ir_codegen = @import("ir_codegen.zig");

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

/// Import function info
const ImportFunc = struct {
    name: []const u8,
    code_offsets: std.ArrayListUnmanaged(usize), // All code locations needing this import
    iat_rva: u32 = 0, // Filled in during layout
};

/// DLL import info
const DllImport = struct {
    name: []const u8,
    functions: std.ArrayListUnmanaged(ImportFunc),
};

pub fn writePE(path: []const u8, code: []const u8, external_patches: []const ir_codegen.ExternalCallPatch) !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    defer _ = gpa.deinit();
    const allocator = gpa.allocator();

    // Build import table from external patches
    var dlls: std.ArrayListUnmanaged(DllImport) = .empty;
    defer {
        for (dlls.items) |*dll| {
            for (dll.functions.items) |*func| {
                func.code_offsets.deinit(allocator);
            }
            dll.functions.deinit(allocator);
        }
        dlls.deinit(allocator);
    }

    // Always include ExitProcess for main's exit
    try addImport(&dlls, allocator, "kernel32.dll", "ExitProcess", null);

    // Add all external patches
    for (external_patches) |patch| {
        try addImport(&dlls, allocator, patch.dll, patch.func_name, patch.offset);
    }

    // Also scan code for FF 15 patterns without patches (legacy ExitProcess call)
    var i: usize = 0;
    while (i + 5 < code.len) : (i += 1) {
        if (code[i] == 0xFF and code[i + 1] == 0x15) {
            // Check if already tracked
            const offset = i + 2; // Position of the displacement
            var found = false;
            for (dlls.items) |dll| {
                for (dll.functions.items) |func| {
                    for (func.code_offsets.items) |o| {
                        if (o == offset) {
                            found = true;
                            break;
                        }
                    }
                }
            }
            if (!found) {
                // This is the legacy ExitProcess call from main
                try addImport(&dlls, allocator, "kernel32.dll", "ExitProcess", offset);
            }
        }
    }

    // Calculate .idata section layout
    // Layout: [IDT entries][null IDT] [IAT per DLL...] [INT per DLL...] [strings...]
    const num_dlls: u32 = @intCast(dlls.items.len);
    const idt_size: u32 = (num_dlls + 1) * 20; // 20 bytes per entry + null terminator

    // Calculate IAT/INT size and positions
    var total_funcs: u32 = 0;
    for (dlls.items) |dll| {
        total_funcs += @intCast(dll.functions.items.len);
    }

    // Each function needs 8 bytes in IAT and 8 bytes in INT, plus null terminators
    const iat_size: u32 = (total_funcs + num_dlls) * 8; // +1 null per DLL
    const int_size: u32 = iat_size;

    const iat_offset: u32 = idt_size;
    const int_offset: u32 = iat_offset + iat_size;
    const strings_offset: u32 = int_offset + int_size;

    // Calculate string table size
    var strings_size: u32 = 0;
    for (dlls.items) |dll| {
        strings_size += @intCast(dll.name.len + 1); // DLL name + null
        for (dll.functions.items) |func| {
            // Align to 2 bytes, then hint (2 bytes) + name + null
            if (strings_size % 2 != 0) strings_size += 1;
            strings_size += 2 + @as(u32, @intCast(func.name.len)) + 1;
        }
    }

    const idata_virtual_size: u32 = strings_offset + strings_size;
    const idata_raw_size: u32 = alignUp(idata_virtual_size, FILE_ALIGNMENT);

    // Calculate section offsets
    const dos_header_size: u32 = 64;
    const pe_header_offset: u32 = dos_header_size;
    const coff_header_size: u32 = 20;
    const optional_header_size: u32 = 240;
    const section_header_size: u32 = 40;
    const num_sections: u16 = 2;
    const headers_size: u32 = pe_header_offset + 4 + coff_header_size + optional_header_size + (section_header_size * num_sections);
    const headers_aligned: u32 = alignUp(headers_size, FILE_ALIGNMENT);

    const text_file_offset: u32 = headers_aligned;
    const text_rva: u32 = SECTION_ALIGNMENT;
    const text_raw_size: u32 = alignUp(@as(u32, @intCast(code.len)), FILE_ALIGNMENT);
    const text_virtual_size: u32 = @intCast(code.len);

    const idata_file_offset: u32 = text_file_offset + text_raw_size;
    const idata_rva: u32 = text_rva + alignUp(text_raw_size, SECTION_ALIGNMENT);

    const image_size: u32 = alignUp(idata_rva + idata_raw_size, SECTION_ALIGNMENT);

    // Assign IAT RVAs to functions and collect string offsets
    var current_iat_offset: u32 = iat_offset;
    var current_string_offset: u32 = strings_offset;
    var dll_name_offsets = try allocator.alloc(u32, dlls.items.len);
    defer allocator.free(dll_name_offsets);
    var func_hint_offsets = try allocator.alloc(u32, total_funcs);
    defer allocator.free(func_hint_offsets);

    var func_idx: usize = 0;
    for (dlls.items, 0..) |*dll, dll_idx| {
        dll_name_offsets[dll_idx] = current_string_offset;
        current_string_offset += @intCast(dll.name.len + 1);

        for (dll.functions.items) |*func| {
            func.iat_rva = idata_rva + current_iat_offset;
            current_iat_offset += 8;

            // Align to 2 for hint/name
            if (current_string_offset % 2 != 0) current_string_offset += 1;
            func_hint_offsets[func_idx] = current_string_offset;
            current_string_offset += 2 + @as(u32, @intCast(func.name.len)) + 1;
            func_idx += 1;
        }
        current_iat_offset += 8; // Null terminator
    }

    // Build buffer (use dynamic allocation for large imports)
    const max_size: usize = 16384;
    var buf: [max_size]u8 = undefined;
    var pos: usize = 0;

    // --- DOS Header ---
    writeU16(&buf, &pos, DOS_SIGNATURE);
    writeU16(&buf, &pos, 0x90);
    writeU16(&buf, &pos, 0x03);
    writeU16(&buf, &pos, 0x00);
    writeU16(&buf, &pos, 0x04);
    writeU16(&buf, &pos, 0x00);
    writeU16(&buf, &pos, 0xFFFF);
    writeU16(&buf, &pos, 0x00);
    writeU16(&buf, &pos, 0xB8);
    writeU16(&buf, &pos, 0x00);
    writeU16(&buf, &pos, 0x00);
    writeU16(&buf, &pos, 0x00);
    writeU16(&buf, &pos, 0x40);
    writeU16(&buf, &pos, 0x00);
    writeZeros(&buf, &pos, 8);
    writeU16(&buf, &pos, 0x00);
    writeU16(&buf, &pos, 0x00);
    writeZeros(&buf, &pos, 20);
    writeU32(&buf, &pos, pe_header_offset);

    // --- PE Signature ---
    writeU32(&buf, &pos, PE_SIGNATURE);

    // --- COFF Header ---
    writeU16(&buf, &pos, IMAGE_FILE_MACHINE_AMD64);
    writeU16(&buf, &pos, num_sections);
    writeU32(&buf, &pos, 0);
    writeU32(&buf, &pos, 0);
    writeU32(&buf, &pos, 0);
    writeU16(&buf, &pos, optional_header_size);
    writeU16(&buf, &pos, IMAGE_FILE_EXECUTABLE_IMAGE | IMAGE_FILE_LARGE_ADDRESS_AWARE);

    // --- Optional Header ---
    writeU16(&buf, &pos, 0x20B);
    buf[pos] = 14;
    pos += 1;
    buf[pos] = 0;
    pos += 1;
    writeU32(&buf, &pos, text_raw_size);
    writeU32(&buf, &pos, idata_raw_size);
    writeU32(&buf, &pos, 0);
    writeU32(&buf, &pos, text_rva);
    writeU32(&buf, &pos, text_rva);
    writeU64(&buf, &pos, IMAGE_BASE);
    writeU32(&buf, &pos, SECTION_ALIGNMENT);
    writeU32(&buf, &pos, FILE_ALIGNMENT);
    writeU16(&buf, &pos, 6);
    writeU16(&buf, &pos, 0);
    writeU16(&buf, &pos, 0);
    writeU16(&buf, &pos, 0);
    writeU16(&buf, &pos, 6);
    writeU16(&buf, &pos, 0);
    writeU32(&buf, &pos, 0);
    writeU32(&buf, &pos, image_size);
    writeU32(&buf, &pos, headers_aligned);
    writeU32(&buf, &pos, 0);
    writeU16(&buf, &pos, IMAGE_SUBSYSTEM_CONSOLE);
    writeU16(&buf, &pos, IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE | IMAGE_DLLCHARACTERISTICS_NX_COMPAT | IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE);
    writeU64(&buf, &pos, 0x100000);
    writeU64(&buf, &pos, 0x1000);
    writeU64(&buf, &pos, 0x100000);
    writeU64(&buf, &pos, 0x1000);
    writeU32(&buf, &pos, 0);
    writeU32(&buf, &pos, 16);

    // Data directories
    writeU64(&buf, &pos, 0); // Export
    writeU32(&buf, &pos, idata_rva); // Import Table RVA
    writeU32(&buf, &pos, idt_size); // Import Table Size
    writeU64(&buf, &pos, 0); // Resource
    writeU64(&buf, &pos, 0); // Exception
    writeU64(&buf, &pos, 0); // Certificate
    writeU64(&buf, &pos, 0); // Base Relocation
    writeU64(&buf, &pos, 0); // Debug
    writeU64(&buf, &pos, 0); // Architecture
    writeU64(&buf, &pos, 0); // Global Ptr
    writeU64(&buf, &pos, 0); // TLS
    writeU64(&buf, &pos, 0); // Load Config
    writeU64(&buf, &pos, 0); // Bound Import
    writeU32(&buf, &pos, idata_rva + iat_offset); // IAT RVA
    writeU32(&buf, &pos, iat_size); // IAT Size
    writeU64(&buf, &pos, 0); // Delay Import
    writeU64(&buf, &pos, 0); // CLR
    writeU64(&buf, &pos, 0); // Reserved

    // --- Section Headers ---
    writeBytes(&buf, &pos, ".text\x00\x00\x00");
    writeU32(&buf, &pos, text_virtual_size);
    writeU32(&buf, &pos, text_rva);
    writeU32(&buf, &pos, text_raw_size);
    writeU32(&buf, &pos, text_file_offset);
    writeU32(&buf, &pos, 0);
    writeU32(&buf, &pos, 0);
    writeU16(&buf, &pos, 0);
    writeU16(&buf, &pos, 0);
    writeU32(&buf, &pos, IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ);

    writeBytes(&buf, &pos, ".idata\x00\x00");
    writeU32(&buf, &pos, idata_virtual_size);
    writeU32(&buf, &pos, idata_rva);
    writeU32(&buf, &pos, idata_raw_size);
    writeU32(&buf, &pos, idata_file_offset);
    writeU32(&buf, &pos, 0);
    writeU32(&buf, &pos, 0);
    writeU16(&buf, &pos, 0);
    writeU16(&buf, &pos, 0);
    writeU32(&buf, &pos, IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ);

    // Pad to .text section
    writeZeros(&buf, &pos, text_file_offset - pos);

    // --- .text section ---
    // Copy code, patching call [rip+disp] instructions
    var code_copy = try allocator.alloc(u8, code.len);
    defer allocator.free(code_copy);
    @memcpy(code_copy, code);

    // Patch all external call displacements
    for (dlls.items) |dll| {
        for (dll.functions.items) |func| {
            for (func.code_offsets.items) |offset| {
                // offset points to the 4-byte displacement in the code
                // RIP at call execution = text_rva + offset + 4
                const rip: i64 = @as(i64, text_rva) + @as(i64, @intCast(offset)) + 4;
                const target: i64 = @as(i64, func.iat_rva);
                const disp: i32 = @intCast(target - rip);

                // Write displacement
                const disp_bytes: [4]u8 = @bitCast(disp);
                code_copy[offset] = disp_bytes[0];
                code_copy[offset + 1] = disp_bytes[1];
                code_copy[offset + 2] = disp_bytes[2];
                code_copy[offset + 3] = disp_bytes[3];
            }
        }
    }

    // Copy patched code to buffer
    @memcpy(buf[pos .. pos + code.len], code_copy);
    pos += code.len;

    // Pad .text section
    writeZeros(&buf, &pos, text_raw_size - @as(u32, @intCast(code.len)));

    // --- .idata section ---
    // Import Directory Table (IDT)
    var iat_pos: u32 = iat_offset;
    var int_pos: u32 = int_offset;
    for (dlls.items, 0..) |dll, dll_idx| {
        writeU32(&buf, &pos, idata_rva + int_pos); // OriginalFirstThunk (INT)
        writeU32(&buf, &pos, 0); // TimeDateStamp
        writeU32(&buf, &pos, 0); // ForwarderChain
        writeU32(&buf, &pos, idata_rva + dll_name_offsets[dll_idx]); // Name RVA
        writeU32(&buf, &pos, idata_rva + iat_pos); // FirstThunk (IAT)

        iat_pos += @as(u32, @intCast(dll.functions.items.len + 1)) * 8;
        int_pos += @as(u32, @intCast(dll.functions.items.len + 1)) * 8;
    }
    // Null terminator IDT entry
    writeZeros(&buf, &pos, 20);

    // IAT entries
    func_idx = 0;
    for (dlls.items) |dll| {
        for (dll.functions.items) |_| {
            writeU64(&buf, &pos, idata_rva + func_hint_offsets[func_idx]);
            func_idx += 1;
        }
        writeU64(&buf, &pos, 0); // Null terminator
    }

    // INT entries (same as IAT before loading)
    func_idx = 0;
    for (dlls.items) |dll| {
        for (dll.functions.items) |_| {
            writeU64(&buf, &pos, idata_rva + func_hint_offsets[func_idx]);
            func_idx += 1;
        }
        writeU64(&buf, &pos, 0); // Null terminator
    }

    // String table
    for (dlls.items) |dll| {
        writeBytes(&buf, &pos, dll.name);
        buf[pos] = 0;
        pos += 1;

        for (dll.functions.items) |func| {
            // Align to 2 bytes
            if (pos % 2 != 0) {
                buf[pos] = 0;
                pos += 1;
            }
            writeU16(&buf, &pos, 0); // Hint
            writeBytes(&buf, &pos, func.name);
            buf[pos] = 0;
            pos += 1;
        }
    }

    // Pad .idata section
    const idata_written: u32 = @intCast(pos - (text_file_offset + text_raw_size));
    if (idata_written < idata_raw_size) {
        writeZeros(&buf, &pos, idata_raw_size - idata_written);
    }

    // Write to file
    const file = try std.fs.cwd().createFile(path, .{});
    defer file.close();
    try file.writeAll(buf[0..pos]);
}

fn addImport(dlls: *std.ArrayListUnmanaged(DllImport), allocator: std.mem.Allocator, dll_name: []const u8, func_name: []const u8, code_offset: ?usize) !void {
    // Find or create DLL entry
    var dll: ?*DllImport = null;
    for (dlls.items) |*d| {
        if (std.mem.eql(u8, d.name, dll_name)) {
            dll = d;
            break;
        }
    }
    if (dll == null) {
        try dlls.append(allocator, .{
            .name = dll_name,
            .functions = .empty,
        });
        dll = &dlls.items[dlls.items.len - 1];
    }

    // Find or create function entry
    var func: ?*ImportFunc = null;
    for (dll.?.functions.items) |*f| {
        if (std.mem.eql(u8, f.name, func_name)) {
            func = f;
            break;
        }
    }
    if (func == null) {
        try dll.?.functions.append(allocator, .{
            .name = func_name,
            .code_offsets = .empty,
        });
        func = &dll.?.functions.items[dll.?.functions.items.len - 1];
    }

    // Add code offset if provided
    if (code_offset) |offset| {
        try func.?.code_offsets.append(allocator, offset);
    }
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
