const std = @import("std");

/// General-purpose registers (64-bit)
pub const Gpr = enum(u4) {
    rax = 0,
    rcx = 1,
    rdx = 2,
    rbx = 3,
    rsp = 4,
    rbp = 5,
    rsi = 6,
    rdi = 7,
    r8 = 8,
    r9 = 9,
    r10 = 10,
    r11 = 11,
    r12 = 12,
    r13 = 13,
    r14 = 14,
    r15 = 15,
};

/// SSE registers
pub const Xmm = enum(u4) {
    xmm0 = 0,
    xmm1 = 1,
    xmm2 = 2,
    xmm3 = 3,
    xmm4 = 4,
    xmm5 = 5,
    xmm6 = 6,
    xmm7 = 7,
    xmm8 = 8,
    xmm9 = 9,
    xmm10 = 10,
    xmm11 = 11,
    xmm12 = 12,
    xmm13 = 13,
    xmm14 = 14,
    xmm15 = 15,
};

/// Windows x64 calling convention constants
pub const win64 = struct {
    pub const shadow_space: u8 = 32;
    pub const arg_regs = [_]Gpr{ .rcx, .rdx, .r8, .r9 };
    pub const arg_xmms = [_]Xmm{ .xmm0, .xmm1, .xmm2, .xmm3 };

    // Callee-saved registers (must be preserved across function calls)
    pub const callee_saved_gprs = [_]Gpr{ .rbx, .rsi, .rdi, .r12, .r13, .r14, .r15 };
    pub const callee_saved_xmms = [_]Xmm{ .xmm6, .xmm7, .xmm8, .xmm9, .xmm10, .xmm11, .xmm12, .xmm13, .xmm14, .xmm15 };

    // Caller-saved registers (may be clobbered by function calls)
    pub const caller_saved_gprs = [_]Gpr{ .rax, .rcx, .rdx, .r8, .r9, .r10, .r11 };
};

/// x86-64 instruction encoder
pub const Encoder = struct {
    code: *std.ArrayListUnmanaged(u8),
    allocator: std.mem.Allocator,

    pub fn init(allocator: std.mem.Allocator, code: *std.ArrayListUnmanaged(u8)) Encoder {
        return .{ .allocator = allocator, .code = code };
    }

    pub fn emit(self: *Encoder, bytes: []const u8) !void {
        try self.code.appendSlice(self.allocator, bytes);
    }

    pub fn emitByte(self: *Encoder, byte: u8) !void {
        try self.code.append(self.allocator, byte);
    }

    pub fn emitI32(self: *Encoder, value: i32) !void {
        try self.code.appendSlice(self.allocator, &@as([4]u8, @bitCast(value)));
    }

    pub fn emitI64(self: *Encoder, value: i64) !void {
        try self.code.appendSlice(self.allocator, &@as([8]u8, @bitCast(value)));
    }

    /// Emit instruction with [rbp+offset] addressing
    /// Automatically switches between 8-bit and 32-bit displacement encoding
    pub fn emitWithRbpOffset(self: *Encoder, prefix: []const u8, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(prefix);
            try self.emitByte(@bitCast(@as(i8, @intCast(offset))));
        } else {
            // Change mod=01 to mod=10 in the ModR/M byte
            if (prefix.len > 0) {
                try self.emit(prefix[0 .. prefix.len - 1]);
                try self.emitByte(prefix[prefix.len - 1] + 0x40);
            }
            try self.emitI32(offset);
        }
    }

    // -------------------------------------------------------------------------
    // Common instruction patterns
    // -------------------------------------------------------------------------

    pub fn movRaxImm64(self: *Encoder, imm: i64) !void {
        try self.emit(&.{ 0x48, 0xB8 });
        try self.emitI64(imm);
    }

    pub fn movRaxRbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x48, 0x8B, 0x45 }, offset);
    }

    pub fn movRbpOffsetRax(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x48, 0x89, 0x45 }, offset);
    }

    pub fn movRcxRbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x48, 0x8B, 0x4D }, offset);
    }

    pub fn leaRaxRbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x48, 0x8D, 0x45 }, offset);
    }

    pub fn leaRcxRbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x48, 0x8D, 0x4D }, offset);
    }

    pub fn movRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x89, 0xC8 });
    }

    pub fn movRaxRdx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x89, 0xD0 });
    }

    pub fn movRcxRax(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x89, 0xC1 });
    }

    pub fn movRcxImm32(self: *Encoder, imm: i32) !void {
        try self.emit(&.{ 0x48, 0xC7, 0xC1 }); // mov rcx, imm32 (sign-extended)
        try self.emitI32(imm);
    }

    pub fn movRdxRax(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x89, 0xC2 });
    }

    pub fn movR8Rax(self: *Encoder) !void {
        try self.emit(&.{ 0x49, 0x89, 0xC0 });
    }

    pub fn movR9Rax(self: *Encoder) !void {
        try self.emit(&.{ 0x49, 0x89, 0xC1 });
    }

    pub fn movR12Rax(self: *Encoder) !void {
        try self.emit(&.{ 0x49, 0x89, 0xC4 });
    }

    pub fn movR8R12(self: *Encoder) !void {
        try self.emit(&.{ 0x4D, 0x89, 0xE0 });
    }

    pub fn movRaxMem(self: *Encoder, reg: Gpr) !void {
        switch (reg) {
            .rcx => try self.emit(&.{ 0x48, 0x8B, 0x01 }),
            .rsi => try self.emit(&.{ 0x48, 0x8B, 0x06 }),
            .r10 => try self.emit(&.{ 0x49, 0x8B, 0x02 }),
            else => return error.UnsupportedRegister,
        }
    }

    pub fn movMemRax(self: *Encoder, reg: Gpr) !void {
        switch (reg) {
            .rcx => try self.emit(&.{ 0x48, 0x89, 0x01 }),
            .rdi => try self.emit(&.{ 0x48, 0x89, 0x07 }),
            .r10 => try self.emit(&.{ 0x49, 0x89, 0x02 }),
            .r11 => try self.emit(&.{ 0x49, 0x89, 0x03 }),
            else => return error.UnsupportedRegister,
        }
    }

    // R10/R11 helpers for memcpy/memset (caller-saved scratch registers on Windows x64)
    pub fn movR10RbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x4C, 0x8B, 0x55 }, offset); // mov r10, [rbp+off]
    }

    pub fn leaR10RbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x4C, 0x8D, 0x55 }, offset); // lea r10, [rbp+off]
    }

    pub fn movR11RbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x4C, 0x8B, 0x5D }, offset); // mov r11, [rbp+off]
    }

    pub fn leaR11RbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0x4C, 0x8D, 0x5D }, offset); // lea r11, [rbp+off]
    }

    pub fn movRaxMemR10Offset(self: *Encoder, offset: i32) !void {
        if (offset == 0) {
            try self.movRaxMem(.r10);
        } else if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x49, 0x8B, 0x42, @bitCast(@as(i8, @intCast(offset))) }); // mov rax, [r10+off8]
        } else {
            try self.emit(&.{ 0x49, 0x8B, 0x82 }); // mov rax, [r10+off32]
            try self.emitI32(offset);
        }
    }

    pub fn movMemR11OffsetRax(self: *Encoder, offset: i32) !void {
        if (offset == 0) {
            try self.movMemRax(.r11);
        } else if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x49, 0x89, 0x43, @bitCast(@as(i8, @intCast(offset))) }); // mov [r11+off8], rax
        } else {
            try self.emit(&.{ 0x49, 0x89, 0x83 }); // mov [r11+off32], rax
            try self.emitI32(offset);
        }
    }

    pub fn pushRax(self: *Encoder) !void {
        try self.emitByte(0x50);
    }

    pub fn popRax(self: *Encoder) !void {
        try self.emitByte(0x58);
    }

    pub fn pushRbp(self: *Encoder) !void {
        try self.emitByte(0x55);
    }

    pub fn popRbp(self: *Encoder) !void {
        try self.emitByte(0x5D);
    }

    /// Push a general-purpose register onto the stack
    /// For r8-r15, emits REX prefix (0x41) followed by push opcode
    pub fn pushGpr(self: *Encoder, reg: Gpr) !void {
        const reg_num: u8 = @intFromEnum(reg);
        if (reg_num >= 8) {
            // r8-r15 need REX.B prefix
            try self.emitByte(0x41);
            try self.emitByte(0x50 + (reg_num - 8));
        } else {
            // rax-rdi use standard encoding
            try self.emitByte(0x50 + reg_num);
        }
    }

    /// Pop a general-purpose register from the stack
    /// For r8-r15, emits REX prefix (0x41) followed by pop opcode
    pub fn popGpr(self: *Encoder, reg: Gpr) !void {
        const reg_num: u8 = @intFromEnum(reg);
        if (reg_num >= 8) {
            // r8-r15 need REX.B prefix
            try self.emitByte(0x41);
            try self.emitByte(0x58 + (reg_num - 8));
        } else {
            // rax-rdi use standard encoding
            try self.emitByte(0x58 + reg_num);
        }
    }

    pub fn ret(self: *Encoder) !void {
        try self.emitByte(0xC3);
    }

    pub fn xorRdxRdx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x31, 0xD2 });
    }

    // -------------------------------------------------------------------------
    // Arithmetic
    // -------------------------------------------------------------------------

    pub fn addRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x01, 0xC8 });
    }

    pub fn subRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x29, 0xC8 });
    }

    pub fn imulRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x0F, 0xAF, 0xC1 });
    }

    pub fn idivRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x99, 0x48, 0xF7, 0xF9 }); // cqo; idiv rcx
    }

    pub fn andRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x21, 0xC8 }); // and rax, rcx
    }

    pub fn orRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x09, 0xC8 }); // or rax, rcx
    }

    pub fn xorRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x31, 0xC8 }); // xor rax, rcx
    }

    pub fn shlRaxCl(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0xD3, 0xE0 }); // shl rax, cl
    }

    pub fn sarRaxCl(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0xD3, 0xF8 }); // sar rax, cl (arithmetic right shift)
    }

    pub fn addRaxImm8(self: *Encoder, imm: u8) !void {
        try self.emit(&.{ 0x48, 0x83, 0xC0, imm });
    }

    pub fn addRaxImm32(self: *Encoder, imm: i32) !void {
        try self.emit(&.{ 0x48, 0x05 });
        try self.emitI32(imm);
    }

    pub fn andRaxImm8(self: *Encoder, imm: u8) !void {
        try self.emit(&.{ 0x48, 0x83, 0xE0, imm });
    }

    pub fn subRspRax(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x29, 0xC4 });
    }

    pub fn subRspImm8(self: *Encoder, imm: u8) !void {
        try self.emit(&.{ 0x48, 0x83, 0xEC, imm });
    }

    pub fn addRspImm8(self: *Encoder, imm: u8) !void {
        try self.emit(&.{ 0x48, 0x83, 0xC4, imm });
    }

    pub fn subRspImm32(self: *Encoder, imm: i32) !void {
        try self.emit(&.{ 0x48, 0x81, 0xEC });
        try self.emitI32(imm);
    }

    pub fn addRspImm32(self: *Encoder, imm: i32) !void {
        try self.emit(&.{ 0x48, 0x81, 0xC4 });
        try self.emitI32(imm);
    }

    /// MOV [RSP+offset], RAX - store RAX to stack relative to RSP
    pub fn movRspOffsetRax(self: *Encoder, offset: i32) !void {
        // RSP-relative addressing requires SIB byte (base=RSP, index=none)
        if (offset >= -128 and offset <= 127) {
            // mod=01 (disp8), reg=000 (rax), rm=100 (SIB follows)
            // SIB: scale=00, index=100 (none), base=100 (rsp)
            try self.emit(&.{ 0x48, 0x89, 0x44, 0x24, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            // mod=10 (disp32), reg=000 (rax), rm=100 (SIB follows)
            try self.emit(&.{ 0x48, 0x89, 0x84, 0x24 });
            try self.emitI32(offset);
        }
    }

    /// MOV RAX, [RSP+offset] - load RAX from stack relative to RSP
    pub fn movRaxRspOffset(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x48, 0x8B, 0x44, 0x24, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            try self.emit(&.{ 0x48, 0x8B, 0x84, 0x24 });
            try self.emitI32(offset);
        }
    }

    /// MOVSD [RSP+offset], XMM0 - store XMM0 to stack relative to RSP
    pub fn movsdRspOffsetXmm0(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0xF2, 0x0F, 0x11, 0x44, 0x24, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            try self.emit(&.{ 0xF2, 0x0F, 0x11, 0x84, 0x24 });
            try self.emitI32(offset);
        }
    }

    /// MOVSD XMM0, [RSP+offset] - load XMM0 from stack relative to RSP
    pub fn movsdXmm0RspOffset(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0xF2, 0x0F, 0x10, 0x44, 0x24, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            try self.emit(&.{ 0xF2, 0x0F, 0x10, 0x84, 0x24 });
            try self.emitI32(offset);
        }
    }

    /// LEA RAX, [RSP+offset] - load effective address relative to RSP
    pub fn leaRaxRspOffset(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x48, 0x8D, 0x44, 0x24, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            try self.emit(&.{ 0x48, 0x8D, 0x84, 0x24 });
            try self.emitI32(offset);
        }
    }

    pub fn shlRaxImm8(self: *Encoder, imm: u8) !void {
        try self.emit(&.{ 0x48, 0xC1, 0xE0, imm });
    }

    // -------------------------------------------------------------------------
    // SSE (floating point)
    // -------------------------------------------------------------------------

    pub fn movsdRbpOffsetXmm0(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0xF2, 0x0F, 0x11, 0x45 }, offset);
    }

    pub fn movsdXmm0RbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0xF2, 0x0F, 0x10, 0x45 }, offset);
    }

    pub fn movsdXmm1RbpOffset(self: *Encoder, offset: i32) !void {
        try self.emitWithRbpOffset(&.{ 0xF2, 0x0F, 0x10, 0x4D }, offset);
    }

    /// Store XMM register to [rbp+offset]
    /// MOVSD [rbp+offset], xmm
    pub fn movsdRbpOffsetXmm(self: *Encoder, offset: i32, xmm: Xmm) !void {
        const xmm_num: u8 = @intFromEnum(xmm);
        if (xmm_num >= 8) {
            // xmm8-xmm15 need REX.R prefix (0x44)
            if (offset >= -128 and offset <= 127) {
                try self.emit(&.{ 0xF2, 0x44, 0x0F, 0x11, 0x45 + ((xmm_num - 8) << 3) });
                try self.emitByte(@bitCast(@as(i8, @intCast(offset))));
            } else {
                try self.emit(&.{ 0xF2, 0x44, 0x0F, 0x11, 0x85 + ((xmm_num - 8) << 3) });
                try self.emitI32(offset);
            }
        } else {
            // xmm0-xmm7 use standard encoding
            // ModR/M: mod=01 (disp8), reg=xmm, r/m=101 (rbp)
            if (offset >= -128 and offset <= 127) {
                try self.emit(&.{ 0xF2, 0x0F, 0x11, 0x45 + (xmm_num << 3) });
                try self.emitByte(@bitCast(@as(i8, @intCast(offset))));
            } else {
                try self.emit(&.{ 0xF2, 0x0F, 0x11, 0x85 + (xmm_num << 3) });
                try self.emitI32(offset);
            }
        }
    }

    /// Load XMM register from [rbp+offset]
    /// MOVSD xmm, [rbp+offset]
    pub fn movsdXmmRbpOffset(self: *Encoder, xmm: Xmm, offset: i32) !void {
        const xmm_num: u8 = @intFromEnum(xmm);
        if (xmm_num >= 8) {
            // xmm8-xmm15 need REX.R prefix (0x44)
            if (offset >= -128 and offset <= 127) {
                try self.emit(&.{ 0xF2, 0x44, 0x0F, 0x10, 0x45 + ((xmm_num - 8) << 3) });
                try self.emitByte(@bitCast(@as(i8, @intCast(offset))));
            } else {
                try self.emit(&.{ 0xF2, 0x44, 0x0F, 0x10, 0x85 + ((xmm_num - 8) << 3) });
                try self.emitI32(offset);
            }
        } else {
            // xmm0-xmm7 use standard encoding
            // ModR/M: mod=01 (disp8), reg=xmm, r/m=101 (rbp)
            if (offset >= -128 and offset <= 127) {
                try self.emit(&.{ 0xF2, 0x0F, 0x10, 0x45 + (xmm_num << 3) });
                try self.emitByte(@bitCast(@as(i8, @intCast(offset))));
            } else {
                try self.emit(&.{ 0xF2, 0x0F, 0x10, 0x85 + (xmm_num << 3) });
                try self.emitI32(offset);
            }
        }
    }

    pub fn movsdXmm0Xmm1(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x10, 0xC1 });
    }

    pub fn movsdXmm1Xmm0(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x10, 0xC8 });
    }

    /// movsd xmm_dst, xmm_src - Move scalar double between XMM registers
    pub fn movsdXmmXmm(self: *Encoder, dst: Xmm, src: Xmm) !void {
        const dst_idx = @intFromEnum(dst);
        const src_idx = @intFromEnum(src);

        // Determine REX prefix
        // REX.R = 1 if dst >= xmm8
        // REX.B = 1 if src >= xmm8
        const rex_r: u8 = if (dst_idx >= 8) 0x44 else 0;
        const rex_b: u8 = if (src_idx >= 8) 0x41 else 0;
        const rex = rex_r | rex_b;

        // movsd xmm_dst, xmm_src: F2 [REX] 0F 10 /r (ModR/M = 0xC0 + (dst&7)<<3 + (src&7))
        const modrm: u8 = 0xC0 | (@as(u8, @intCast(dst_idx & 7)) << 3) | @as(u8, @intCast(src_idx & 7));

        if (rex != 0) {
            try self.emit(&.{ 0xF2, rex, 0x0F, 0x10, modrm });
        } else {
            try self.emit(&.{ 0xF2, 0x0F, 0x10, modrm });
        }
    }

    pub fn movsdMemRcxXmm0(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x11, 0x01 });
    }

    pub fn movsdXmm0MemRcx(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x10, 0x01 });
    }

    pub fn addsdXmm0Xmm1(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x58, 0xC1 });
    }

    pub fn subsdXmm0Xmm1(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x5C, 0xC1 });
    }

    pub fn mulsdXmm0Xmm1(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x59, 0xC1 });
    }

    pub fn divsdXmm0Xmm1(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x5E, 0xC1 });
    }

    pub fn cvttsd2siRaxXmm0(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x48, 0x0F, 0x2C, 0xC0 });
    }

    pub fn cvtsi2sdXmm0Rax(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x48, 0x0F, 0x2A, 0xC0 });
    }

    /// Absolute value of xmm0 (clear sign bit)
    /// ANDPD xmm0, [rip+const] where const = 0x7FFFFFFFFFFFFFFF
    pub fn fabsXmm0(self: *Encoder) !void {
        // Load mask 0x7FFFFFFFFFFFFFFF into rax
        try self.emit(&.{ 0x48, 0xB8 }); // mov rax, imm64
        try self.emit(&.{ 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }); // 0x7FFFFFFFFFFFFFFF
        // Push rax to stack
        try self.emitByte(0x50); // push rax
        // MOVQ xmm1, [rsp] - load mask into xmm1
        try self.emit(&.{ 0xF3, 0x0F, 0x7E, 0x0C, 0x24 }); // movq xmm1, [rsp]
        // ANDPD xmm0, xmm1
        try self.emit(&.{ 0x66, 0x0F, 0x54, 0xC1 }); // andpd xmm0, xmm1
        // Pop (restore stack)
        try self.emitByte(0x58); // pop rax
    }

    /// Square root of xmm0
    /// SQRTSD xmm0, xmm0 = F2 0F 51 C0
    pub fn sqrtsdXmm0(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x51, 0xC0 });
    }

    /// Round xmm0 toward positive infinity (ceiling)
    /// ROUNDSD xmm0, xmm0, 0x02 = 66 0F 3A 0B C0 02
    pub fn roundsdCeilXmm0(self: *Encoder) !void {
        try self.emit(&.{ 0x66, 0x0F, 0x3A, 0x0B, 0xC0, 0x02 });
    }

    /// Round xmm0 toward negative infinity (floor)
    /// ROUNDSD xmm0, xmm0, 0x01 = 66 0F 3A 0B C0 01
    pub fn roundsdFloorXmm0(self: *Encoder) !void {
        try self.emit(&.{ 0x66, 0x0F, 0x3A, 0x0B, 0xC0, 0x01 });
    }

    /// Round xmm0 half away from zero (standard mathematical rounding)
    /// Implements: round(x) = trunc(x + copysign(0.5, x))
    /// This adds 0.5 with the same sign as x, then truncates
    pub fn roundsdRoundHalfAwayXmm0(self: *Encoder) !void {
        // Load 0.5 into xmm1
        // mov rax, 0x3FE0000000000000 (0.5 in IEEE 754 double)
        try self.emit(&.{ 0x48, 0xB8 }); // mov rax, imm64
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE0, 0x3F }); // 0.5
        // Push rax to stack
        try self.emitByte(0x50); // push rax
        // MOVQ xmm1, [rsp] - load 0.5 into xmm1
        try self.emit(&.{ 0xF3, 0x0F, 0x7E, 0x0C, 0x24 }); // movq xmm1, [rsp]
        // Pop (restore stack)
        try self.emitByte(0x58); // pop rax

        // Copy sign from xmm0 to xmm1 (copysign(0.5, x))
        // Load sign mask 0x8000000000000000 into xmm2
        try self.emit(&.{ 0x48, 0xB8 }); // mov rax, imm64
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }); // sign bit mask
        try self.emitByte(0x50); // push rax
        try self.emit(&.{ 0xF3, 0x0F, 0x7E, 0x14, 0x24 }); // movq xmm2, [rsp]
        try self.emitByte(0x58); // pop rax

        // xmm2 = xmm0 & sign_mask (extract sign of x)
        // ANDPD xmm2, xmm0: 66 0F 54 D0
        try self.emit(&.{ 0x66, 0x0F, 0x54, 0xD0 }); // andpd xmm2, xmm0

        // xmm1 = xmm1 | xmm2 (apply sign to 0.5)
        // ORPD xmm1, xmm2: 66 0F 56 CA
        try self.emit(&.{ 0x66, 0x0F, 0x56, 0xCA }); // orpd xmm1, xmm2

        // xmm0 = xmm0 + xmm1 (x + copysign(0.5, x))
        // ADDSD xmm0, xmm1: F2 0F 58 C1
        try self.emit(&.{ 0xF2, 0x0F, 0x58, 0xC1 }); // addsd xmm0, xmm1

        // ROUNDSD xmm0, xmm0, 0x03 (truncate toward zero)
        try self.emit(&.{ 0x66, 0x0F, 0x3A, 0x0B, 0xC0, 0x03 });
    }

    /// Move 64 bits from xmm0 to rax (for bitcast f64 to i64)
    /// MOVQ rax, xmm0 = 66 48 0F 7E C0
    pub fn movqRaxXmm0(self: *Encoder) !void {
        try self.emit(&.{ 0x66, 0x48, 0x0F, 0x7E, 0xC0 });
    }

    /// Move 64 bits from rax to xmm0 (for bitcast i64 to f64)
    /// MOVQ xmm0, rax = 66 48 0F 6E C0
    pub fn movqXmm0Rax(self: *Encoder) !void {
        try self.emit(&.{ 0x66, 0x48, 0x0F, 0x6E, 0xC0 });
    }

    // -------------------------------------------------------------------------
    // Calls
    // -------------------------------------------------------------------------

    /// Emit call rel32 and return offset of the displacement for patching
    pub fn callRel32(self: *Encoder) !usize {
        try self.emitByte(0xE8);
        const offset = self.code.items.len;
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00 });
        return offset;
    }

    /// Emit call [rip+disp32] and return offset of the displacement for patching
    pub fn callIndirectRip(self: *Encoder) !usize {
        try self.emit(&.{ 0xFF, 0x15 });
        const offset = self.code.items.len;
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00 });
        return offset;
    }

    /// Emit call rax (indirect call through register)
    pub fn callRax(self: *Encoder) !void {
        try self.emit(&.{ 0xFF, 0xD0 }); // call rax
    }

    /// Emit shadow space allocation (32 bytes for Windows x64)
    pub fn allocShadowSpace(self: *Encoder) !void {
        try self.subRspImm8(win64.shadow_space);
    }

    /// Emit shadow space deallocation
    pub fn freeShadowSpace(self: *Encoder) !void {
        try self.addRspImm8(win64.shadow_space);
    }

    // -------------------------------------------------------------------------
    // Function prologue/epilogue
    // -------------------------------------------------------------------------

    pub fn prologue(self: *Encoder, stack_size: i32) !void {
        try self.pushRbp();
        try self.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.emit(&.{ 0x48, 0x81, 0xEC }); // sub rsp, imm32
        try self.emitI32(stack_size);
    }

    /// Reserve space for prologue - we don't know the stack size yet
    /// Reserves 17 bytes (max prologue size) which will be filled in later
    /// Returns the offset where the prologue starts
    pub fn prologuePlaceholder(self: *Encoder) !void {
        // Reserve 17 bytes (size of __chkstk prologue, the largest variant)
        try self.emit(&.{ 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 });
        try self.emit(&.{ 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 });
    }

    /// Fill in the prologue at func_start with the appropriate variant
    /// For small stacks (< 4096), uses simple prologue with NOPs padding
    /// For large stacks (>= 4096), uses __chkstk prologue
    pub fn fillPrologue(code: []u8, func_start: usize, stack_size: i32, needs_chkstk: bool) void {
        var pos = func_start;
        if (needs_chkstk) {
            // __chkstk prologue: push rbp (1) + mov rbp,rsp (3) + mov eax,imm32 (5) + call rel32 (5) + sub rsp,rax (3) = 17 bytes
            code[pos] = 0x55; // push rbp
            pos += 1;
            code[pos] = 0x48;
            code[pos + 1] = 0x89;
            code[pos + 2] = 0xE5; // mov rbp, rsp
            pos += 3;
            code[pos] = 0xB8; // mov eax, imm32
            pos += 1;
            const size_bytes: [4]u8 = @bitCast(stack_size);
            code[pos] = size_bytes[0];
            code[pos + 1] = size_bytes[1];
            code[pos + 2] = size_bytes[2];
            code[pos + 3] = size_bytes[3];
            pos += 4;
            code[pos] = 0xE8; // call rel32 (placeholder, patched later)
            code[pos + 1] = 0x00;
            code[pos + 2] = 0x00;
            code[pos + 3] = 0x00;
            code[pos + 4] = 0x00;
            pos += 5;
            code[pos] = 0x48;
            code[pos + 1] = 0x29;
            code[pos + 2] = 0xC4; // sub rsp, rax
        } else {
            // Simple prologue: push rbp (1) + mov rbp,rsp (3) + sub rsp,imm32 (7) = 11 bytes + 6 NOPs = 17 bytes
            code[pos] = 0x55; // push rbp
            pos += 1;
            code[pos] = 0x48;
            code[pos + 1] = 0x89;
            code[pos + 2] = 0xE5; // mov rbp, rsp
            pos += 3;
            code[pos] = 0x48;
            code[pos + 1] = 0x81;
            code[pos + 2] = 0xEC; // sub rsp, imm32
            pos += 3;
            const size_bytes: [4]u8 = @bitCast(stack_size);
            code[pos] = size_bytes[0];
            code[pos + 1] = size_bytes[1];
            code[pos + 2] = size_bytes[2];
            code[pos + 3] = size_bytes[3];
            pos += 4;
            // Pad with 6 NOPs to match __chkstk prologue size
            code[pos] = 0x90;
            code[pos + 1] = 0x90;
            code[pos + 2] = 0x90;
            code[pos + 3] = 0x90;
            code[pos + 4] = 0x90;
            code[pos + 5] = 0x90;
        }
    }

    pub fn epilogue(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x89, 0xEC }); // mov rsp, rbp
        try self.popRbp();
        try self.ret();
    }

    // -------------------------------------------------------------------------
    // Comparison
    // -------------------------------------------------------------------------

    /// UCOMISD xmm0, xmm1 - compare floating point values, set flags
    pub fn ucomisdXmm0Xmm1(self: *Encoder) !void {
        try self.emit(&.{ 0x66, 0x0F, 0x2E, 0xC1 });
    }

    /// CMP rax, rcx - compare integers, set flags
    pub fn cmpRaxRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x39, 0xC8 });
    }

    // -------------------------------------------------------------------------
    // Conditional jumps
    // -------------------------------------------------------------------------

    /// JE rel32 - jump if equal (ZF=1), returns offset of displacement for patching
    pub fn jeRel32(self: *Encoder) !usize {
        try self.emit(&.{ 0x0F, 0x84 });
        const offset = self.code.items.len;
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00 });
        return offset;
    }

    /// JNE rel32 - jump if not equal (ZF=0), returns offset for patching
    pub fn jneRel32(self: *Encoder) !usize {
        try self.emit(&.{ 0x0F, 0x85 });
        const offset = self.code.items.len;
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00 });
        return offset;
    }

    /// JMP rel32 - unconditional jump, returns offset for patching
    pub fn jmpRel32(self: *Encoder) !usize {
        try self.emitByte(0xE9);
        const offset = self.code.items.len;
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00 });
        return offset;
    }

    /// SETE al - set byte if equal (ZF=1)
    pub fn seteAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x94, 0xC0 });
    }

    /// SETNE al - set byte if not equal (ZF=0)
    pub fn setneAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x95, 0xC0 });
    }

    /// SETL al - set byte if less (SF!=OF)
    pub fn setlAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x9C, 0xC0 });
    }

    /// SETLE al - set byte if less or equal (ZF=1 or SF!=OF)
    pub fn setleAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x9E, 0xC0 });
    }

    /// SETG al - set byte if greater (ZF=0 and SF=OF)
    pub fn setgAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x9F, 0xC0 });
    }

    /// SETGE al - set byte if greater or equal (SF=OF)
    pub fn setgeAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x9D, 0xC0 });
    }

    /// SETA al - set byte if above (CF=0 and ZF=0) - for unsigned/float comparison
    pub fn setaAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x97, 0xC0 });
    }

    /// SETAE al - set byte if above or equal (CF=0) - for unsigned/float comparison
    pub fn setaeAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x93, 0xC0 });
    }

    /// SETB al - set byte if below (CF=1) - for unsigned/float comparison
    pub fn setbAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x92, 0xC0 });
    }

    /// SETBE al - set byte if below or equal (CF=1 or ZF=1) - for unsigned/float comparison
    pub fn setbeAl(self: *Encoder) !void {
        try self.emit(&.{ 0x0F, 0x96, 0xC0 });
    }

    /// MOVZX rax, al - zero-extend al to rax
    pub fn movzxRaxAl(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x0F, 0xB6, 0xC0 });
    }

    /// MOVSXD rax, eax - sign-extend eax to rax
    pub fn movsxdRaxEax(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x63, 0xC0 });
    }

    /// MOV rsi, [rbp+offset] - load qword to rsi
    pub fn movRsiRbpOffset(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x48, 0x8B, 0x75 });
            try self.emit(&.{@bitCast(@as(i8, @intCast(offset)))});
        } else {
            try self.emit(&.{ 0x48, 0x8B, 0xB5 });
            try self.emit(&@as([4]u8, @bitCast(offset)));
        }
    }

    /// LEA rsi, [rbp+offset] - load effective address to rsi
    pub fn leaRsiRbpOffset(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x48, 0x8D, 0x75 });
            try self.emit(&.{@bitCast(@as(i8, @intCast(offset)))});
        } else {
            try self.emit(&.{ 0x48, 0x8D, 0xB5 });
            try self.emit(&@as([4]u8, @bitCast(offset)));
        }
    }

    /// MOV rdi, [rbp+offset] - load qword to rdi
    pub fn movRdiRbpOffset(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x48, 0x8B, 0x7D });
            try self.emit(&.{@bitCast(@as(i8, @intCast(offset)))});
        } else {
            try self.emit(&.{ 0x48, 0x8B, 0xBD });
            try self.emit(&@as([4]u8, @bitCast(offset)));
        }
    }

    /// LEA rdi, [rbp+offset] - load effective address to rdi
    pub fn leaRdiRbpOffset(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x48, 0x8D, 0x7D });
            try self.emit(&.{@bitCast(@as(i8, @intCast(offset)))});
        } else {
            try self.emit(&.{ 0x48, 0x8D, 0xBD });
            try self.emit(&@as([4]u8, @bitCast(offset)));
        }
    }

    /// MOV rcx, [rbp+offset] - load qword to rcx (for loadToRcx)
    pub fn movRcxRbpOffsetQ(self: *Encoder, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            try self.emit(&.{ 0x48, 0x8B, 0x4D });
            try self.emit(&.{@bitCast(@as(i8, @intCast(offset)))});
        } else {
            try self.emit(&.{ 0x48, 0x8B, 0x8D });
            try self.emit(&@as([4]u8, @bitCast(offset)));
        }
    }

    /// REP MOVSB - repeat move string (byte)
    pub fn repMovsb(self: *Encoder) !void {
        try self.emit(&.{ 0xF3, 0xA4 });
    }

    /// REP STOSB - repeat store string (byte) - fills memory with al
    pub fn repStosb(self: *Encoder) !void {
        try self.emit(&.{ 0xF3, 0xAA });
    }

    /// REPNE SCASB - scan string for byte (searches for AL in [RDI], decrements RCX, increments RDI)
    pub fn repneScasb(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0xAE });
    }

    /// XOR al, al - zero al register
    pub fn xorAlAl(self: *Encoder) !void {
        try self.emit(&.{ 0x30, 0xC0 });
    }

    /// NOT rcx - bitwise NOT of rcx
    pub fn notRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0xF7, 0xD1 });
    }

    /// DEC rcx - decrement rcx
    pub fn decRcx(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0xFF, 0xC9 });
    }

    /// MOV rcx, imm64 - load 64-bit immediate to rcx
    pub fn movRcxImm64(self: *Encoder, imm: i64) !void {
        try self.emit(&.{ 0x48, 0xB9 });
        try self.emit(&@as([8]u8, @bitCast(imm)));
    }

    // -------------------------------------------------------------------------
    // Generic register operations for linear-scan register allocator
    // -------------------------------------------------------------------------

    /// MOV dst, src - 64-bit register-to-register move
    /// Emits REX prefix + MOV with proper ModR/M encoding
    pub fn movGprGpr(self: *Encoder, dst: Gpr, src: Gpr) !void {
        const dst_num: u8 = @intFromEnum(dst);
        const src_num: u8 = @intFromEnum(src);

        // REX prefix: 0100 WRXB
        // W=1 (64-bit), R=src extension, B=dst extension
        var rex: u8 = 0x48;
        if (src_num >= 8) rex |= 0x04; // REX.R
        if (dst_num >= 8) rex |= 0x01; // REX.B

        // ModR/M: mod=11 (reg-reg), reg=src, rm=dst
        const modrm: u8 = 0xC0 | ((src_num & 0x07) << 3) | (dst_num & 0x07);

        try self.emit(&.{ rex, 0x89, modrm });
    }

    /// MOV [rbp+offset], src - store GPR to stack
    /// Handles any GPR as source
    pub fn movRbpOffsetGpr(self: *Encoder, offset: i32, src: Gpr) !void {
        const src_num: u8 = @intFromEnum(src);

        // REX prefix: W=1, R=src extension, B=0 (rbp doesn't need extension)
        var rex: u8 = 0x48;
        if (src_num >= 8) rex |= 0x04; // REX.R

        // ModR/M: mod depends on offset size, reg=src, rm=101 (rbp)
        const reg_bits: u8 = (src_num & 0x07) << 3;

        if (offset >= -128 and offset <= 127) {
            // 8-bit displacement: mod=01, rm=101
            const modrm: u8 = 0x45 | reg_bits;
            try self.emit(&.{ rex, 0x89, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            // 32-bit displacement: mod=10, rm=101
            const modrm: u8 = 0x85 | reg_bits;
            try self.emit(&.{ rex, 0x89, modrm });
            try self.emitI32(offset);
        }
    }

    /// MOV dst, [rbp+offset] - load GPR from stack
    /// Handles any GPR as destination
    pub fn movGprRbpOffset(self: *Encoder, dst: Gpr, offset: i32) !void {
        const dst_num: u8 = @intFromEnum(dst);

        // REX prefix: W=1, R=dst extension, B=0 (rbp doesn't need extension)
        var rex: u8 = 0x48;
        if (dst_num >= 8) rex |= 0x04; // REX.R

        // ModR/M: mod depends on offset size, reg=dst, rm=101 (rbp)
        const reg_bits: u8 = (dst_num & 0x07) << 3;

        if (offset >= -128 and offset <= 127) {
            // 8-bit displacement: mod=01, rm=101
            const modrm: u8 = 0x45 | reg_bits;
            try self.emit(&.{ rex, 0x8B, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            // 32-bit displacement: mod=10, rm=101
            const modrm: u8 = 0x85 | reg_bits;
            try self.emit(&.{ rex, 0x8B, modrm });
            try self.emitI32(offset);
        }
    }

    /// MOV dst, imm64 - load 64-bit immediate to any GPR
    /// Uses the movabs encoding (REX.W + B8+rd + imm64)
    pub fn movGprImm64(self: *Encoder, dst: Gpr, imm: i64) !void {
        const dst_num: u8 = @intFromEnum(dst);

        // REX prefix: W=1 (64-bit), B=dst extension
        var rex: u8 = 0x48;
        if (dst_num >= 8) rex |= 0x01; // REX.B

        // Opcode: B8+rd (low 3 bits of register number)
        const opcode: u8 = 0xB8 + (dst_num & 0x07);

        try self.emit(&.{ rex, opcode });
        try self.emitI64(imm);
    }

    /// LEA dst, [rbp+offset] - load effective address to any GPR
    pub fn leaGprRbpOffset(self: *Encoder, dst: Gpr, offset: i32) !void {
        const dst_num: u8 = @intFromEnum(dst);

        // REX prefix: W=1, R=dst extension, B=0 (rbp doesn't need extension)
        var rex: u8 = 0x48;
        if (dst_num >= 8) rex |= 0x04; // REX.R

        // ModR/M: mod depends on offset size, reg=dst, rm=101 (rbp)
        const reg_bits: u8 = (dst_num & 0x07) << 3;

        if (offset >= -128 and offset <= 127) {
            // 8-bit displacement: mod=01, rm=101
            const modrm: u8 = 0x45 | reg_bits;
            try self.emit(&.{ rex, 0x8D, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            // 32-bit displacement: mod=10, rm=101
            const modrm: u8 = 0x85 | reg_bits;
            try self.emit(&.{ rex, 0x8D, modrm });
            try self.emitI32(offset);
        }
    }

    /// ADD dst, src - 64-bit register addition
    pub fn addGprGpr(self: *Encoder, dst: Gpr, src: Gpr) !void {
        const dst_num: u8 = @intFromEnum(dst);
        const src_num: u8 = @intFromEnum(src);

        var rex: u8 = 0x48;
        if (src_num >= 8) rex |= 0x04; // REX.R
        if (dst_num >= 8) rex |= 0x01; // REX.B

        const modrm: u8 = 0xC0 | ((src_num & 0x07) << 3) | (dst_num & 0x07);

        try self.emit(&.{ rex, 0x01, modrm });
    }

    /// SUB dst, src - 64-bit register subtraction
    pub fn subGprGpr(self: *Encoder, dst: Gpr, src: Gpr) !void {
        const dst_num: u8 = @intFromEnum(dst);
        const src_num: u8 = @intFromEnum(src);

        var rex: u8 = 0x48;
        if (src_num >= 8) rex |= 0x04; // REX.R
        if (dst_num >= 8) rex |= 0x01; // REX.B

        const modrm: u8 = 0xC0 | ((src_num & 0x07) << 3) | (dst_num & 0x07);

        try self.emit(&.{ rex, 0x29, modrm });
    }

    /// CMP gpr1, gpr2 - compare two 64-bit registers
    pub fn cmpGprGpr(self: *Encoder, gpr1: Gpr, gpr2: Gpr) !void {
        const gpr1_num: u8 = @intFromEnum(gpr1);
        const gpr2_num: u8 = @intFromEnum(gpr2);

        var rex: u8 = 0x48;
        if (gpr2_num >= 8) rex |= 0x04; // REX.R
        if (gpr1_num >= 8) rex |= 0x01; // REX.B

        const modrm: u8 = 0xC0 | ((gpr2_num & 0x07) << 3) | (gpr1_num & 0x07);

        try self.emit(&.{ rex, 0x39, modrm });
    }
};
