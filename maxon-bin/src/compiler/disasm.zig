const std = @import("std");

const zydis = @cImport({
    @cInclude("Zydis.h");
});

/// Disassemble x86-64 machine code to Intel-syntax assembly text.
/// Returns a string with one instruction per line, prefixed with address.
pub fn disassemble(code: []const u8, base_addr: u64, allocator: std.mem.Allocator) ![]const u8 {
    var output: std.ArrayListUnmanaged(u8) = .empty;
    errdefer output.deinit(allocator);
    const writer = output.writer(allocator);

    // Header
    try writer.writeAll("; Maxon Compiler - Disassembled x86-64 (Intel syntax)\n");
    try writer.writeAll("; Disassembled from generated machine code\n\n");

    var offset: usize = 0;
    while (offset < code.len) {
        var instruction: zydis.ZydisDisassembledInstruction = undefined;

        const status = zydis.ZydisDisassembleIntel(
            zydis.ZYDIS_MACHINE_MODE_LONG_64,
            base_addr + offset,
            code.ptr + offset,
            code.len - offset,
            &instruction,
        );

        if (zydis.ZYAN_SUCCESS(status)) {
            // Print address and instruction text
            try writer.print("0x{X:0>8}  {s}\n", .{
                base_addr + offset,
                @as([*:0]const u8, @ptrCast(&instruction.text)),
            });
            offset += instruction.info.length;
        } else {
            // Invalid/unknown instruction - print raw byte
            try writer.print("0x{X:0>8}  db 0x{X:0>2}\n", .{
                base_addr + offset,
                code[offset],
            });
            offset += 1;
        }
    }

    return output.toOwnedSlice(allocator);
}

/// Disassemble with function labels from codegen result.
/// Takes function offsets to add labels in the output.
pub fn disassembleWithLabels(
    code: []const u8,
    base_addr: u64,
    func_offsets: []const FuncOffset,
    allocator: std.mem.Allocator,
) ![]const u8 {
    var output: std.ArrayListUnmanaged(u8) = .empty;
    errdefer output.deinit(allocator);
    const writer = output.writer(allocator);

    // Header
    try writer.writeAll("; Maxon Compiler - Disassembled x86-64 (Intel syntax)\n");
    try writer.writeAll("; Disassembled from generated machine code\n\n");

    var offset: usize = 0;
    var func_idx: usize = 0;

    while (offset < code.len) {
        // Check if we're at a function start
        while (func_idx < func_offsets.len and func_offsets[func_idx].offset == offset) {
            try writer.print("\n; Function: {s}\n", .{func_offsets[func_idx].name});
            try writer.print("{s}:\n", .{func_offsets[func_idx].name});
            func_idx += 1;
        }

        var instruction: zydis.ZydisDisassembledInstruction = undefined;

        const status = zydis.ZydisDisassembleIntel(
            zydis.ZYDIS_MACHINE_MODE_LONG_64,
            base_addr + offset,
            code.ptr + offset,
            code.len - offset,
            &instruction,
        );

        if (zydis.ZYAN_SUCCESS(status)) {
            try writer.print("    {s}\n", .{
                @as([*:0]const u8, @ptrCast(&instruction.text)),
            });
            offset += instruction.info.length;
        } else {
            try writer.print("    db 0x{X:0>2}\n", .{code[offset]});
            offset += 1;
        }
    }

    return output.toOwnedSlice(allocator);
}

pub const FuncOffset = struct {
    name: []const u8,
    offset: usize,
};
