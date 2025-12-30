const std = @import("std");
const ir = @import("ir.zig");

/// Generate x86-64 assembly text from IR module
/// Produces NASM-compatible syntax for Windows x64 ABI
pub fn generateAssembly(module: ir.Module, allocator: std.mem.Allocator) ![]const u8 {
    var output: std.ArrayListUnmanaged(u8) = .empty;
    errdefer output.deinit(allocator);
    const writer = output.writer(allocator);

    // Emit header
    try writer.writeAll("; Maxon Compiler - Generated x86-64 Assembly\n");
    try writer.writeAll("; Target: Windows x64 ABI (NASM syntax)\n\n");

    // Emit section declarations
    try writer.writeAll("bits 64\n");
    try writer.writeAll("default rel\n\n");

    // Emit external references
    try writer.writeAll("; External functions (Windows API)\n");
    try writer.writeAll("extern ExitProcess\n");
    try writer.writeAll("extern GetProcessHeap\n");
    try writer.writeAll("extern HeapAlloc\n");
    try writer.writeAll("extern HeapFree\n");
    try writer.writeAll("extern HeapReAlloc\n");
    try writer.writeAll("extern GetStdHandle\n");
    try writer.writeAll("extern WriteFile\n\n");

    try writer.writeAll("section .data\n\n");

    try writer.writeAll("section .text\n");

    // Generate each function
    for (module.functions.items) |*func| {
        try generateFunction(writer, func);
    }

    return output.toOwnedSlice(allocator);
}

fn generateFunction(writer: anytype, func: *const ir.Function) !void {
    try writer.print("\n; Function: {s}\n", .{func.name});

    if (func.is_exported) {
        try writer.print("global {s}\n", .{func.name});
    }
    try writer.print("{s}:\n", .{func.name});

    // Prologue
    try writer.writeAll("    push rbp\n");
    try writer.writeAll("    mov rbp, rsp\n");
    try writer.writeAll("    sub rsp, 64            ; Stack frame\n\n");

    // Generate blocks
    for (func.blocks.items, 0..) |block, block_idx| {
        if (block_idx > 0 or block.name.len > 0) {
            try writer.print(".{s}:\n", .{if (block.name.len > 0) block.name else "L0"});
        }

        for (block.instructions.items) |inst| {
            try generateInstruction(writer, inst);
        }
    }

    try writer.writeAll("\n");
}

fn generateInstruction(writer: anytype, inst: ir.Instruction) !void {
    const op_name = inst.op.format();

    switch (inst.op) {
        .const_i32 => {
            if (inst.result) |result| {
                const val = inst.operands[0].immediate_i32;
                try writer.print("    ; %{d} = {s} {d}\n", .{ result, op_name, val });
                try writer.print("    mov eax, {d}\n", .{val});
                try writer.print("    mov [rbp-{d}], eax\n", .{(result + 1) * 8});
            }
        },
        .const_i64 => {
            if (inst.result) |result| {
                const val = inst.operands[0].immediate_i64;
                try writer.print("    ; %{d} = {s} {d}\n", .{ result, op_name, val });
                try writer.print("    mov rax, {d}\n", .{val});
                try writer.print("    mov [rbp-{d}], rax\n", .{(result + 1) * 8});
            }
        },
        .const_f64 => {
            if (inst.result) |result| {
                const val = inst.operands[0].immediate_f64;
                const bits: u64 = @bitCast(val);
                try writer.print("    ; %{d} = {s} {d}\n", .{ result, op_name, val });
                try writer.print("    mov rax, 0x{X}\n", .{bits});
                try writer.print("    mov [rbp-{d}], rax\n", .{(result + 1) * 8});
            }
        },
        .add, .sub, .mul => {
            if (inst.result) |result| {
                const lhs = inst.operands[0].value;
                const rhs = inst.operands[1].value;
                try writer.print("    ; %{d} = {s} %{d}, %{d}\n", .{ result, op_name, lhs, rhs });
                try writer.print("    mov rax, [rbp-{d}]\n", .{(lhs + 1) * 8});
                try writer.print("    mov rcx, [rbp-{d}]\n", .{(rhs + 1) * 8});
                const asm_op = switch (inst.op) {
                    .add => "add",
                    .sub => "sub",
                    .mul => "imul",
                    else => unreachable,
                };
                if (inst.op == .mul) {
                    try writer.print("    imul rax, rcx\n", .{});
                } else {
                    try writer.print("    {s} rax, rcx\n", .{asm_op});
                }
                try writer.print("    mov [rbp-{d}], rax\n", .{(result + 1) * 8});
            }
        },
        .div, .mod => {
            if (inst.result) |result| {
                const lhs = inst.operands[0].value;
                const rhs = inst.operands[1].value;
                try writer.print("    ; %{d} = {s} %{d}, %{d}\n", .{ result, op_name, lhs, rhs });
                try writer.print("    mov rax, [rbp-{d}]\n", .{(lhs + 1) * 8});
                try writer.writeAll("    cqo\n");
                try writer.print("    mov rcx, [rbp-{d}]\n", .{(rhs + 1) * 8});
                try writer.writeAll("    idiv rcx\n");
                if (inst.op == .mod) {
                    try writer.print("    mov [rbp-{d}], rdx    ; remainder\n", .{(result + 1) * 8});
                } else {
                    try writer.print("    mov [rbp-{d}], rax    ; quotient\n", .{(result + 1) * 8});
                }
            }
        },
        .ret => {
            try writer.print("    ; {s}\n", .{op_name});
            if (inst.operands[0] != .none) {
                const val = inst.operands[0].value;
                try writer.print("    mov rax, [rbp-{d}]\n", .{(val + 1) * 8});
            }
            try writer.writeAll("    mov rsp, rbp\n");
            try writer.writeAll("    pop rbp\n");
            try writer.writeAll("    ret\n");
        },
        .call => {
            const func_name = inst.operands[0].func_name;
            if (inst.result) |result| {
                try writer.print("    ; %{d} = call {s}\n", .{ result, func_name });
            } else {
                try writer.print("    ; call {s}\n", .{func_name});
            }
            try writer.writeAll("    sub rsp, 32           ; shadow space\n");
            try writer.print("    call {s}\n", .{func_name});
            try writer.writeAll("    add rsp, 32\n");
            if (inst.result) |result| {
                try writer.print("    mov [rbp-{d}], rax\n", .{(result + 1) * 8});
            }
        },
        .alloca => {
            if (inst.result) |result| {
                try writer.print("    ; %{d} = alloca\n", .{result});
                try writer.print("    lea rax, [rbp-{d}]\n", .{(result + 1) * 8});
                try writer.print("    mov [rbp-{d}], rax\n", .{(result + 1) * 8});
            }
        },
        .load => {
            if (inst.result) |result| {
                const ptr = inst.operands[0].value;
                try writer.print("    ; %{d} = load %{d}\n", .{ result, ptr });
                try writer.print("    mov rax, [rbp-{d}]\n", .{(ptr + 1) * 8});
                try writer.writeAll("    mov rax, [rax]\n");
                try writer.print("    mov [rbp-{d}], rax\n", .{(result + 1) * 8});
            }
        },
        .store => {
            const ptr = inst.operands[0].value;
            const val = inst.operands[1].value;
            try writer.print("    ; store %{d}, %{d}\n", .{ ptr, val });
            try writer.print("    mov rax, [rbp-{d}]\n", .{(val + 1) * 8});
            try writer.print("    mov rcx, [rbp-{d}]\n", .{(ptr + 1) * 8});
            try writer.writeAll("    mov [rcx], rax\n");
        },
        .br => {
            const target = inst.operands[0].block_ref;
            try writer.print("    ; br block {d}\n", .{target});
            try writer.print("    jmp .L{d}\n", .{target});
        },
        else => {
            // For other instructions, just emit a comment
            try writer.print("    ; {s} (not yet implemented)\n", .{op_name});
        },
    }
}
