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
};

/// Windows x64 calling convention constants
pub const win64 = struct {
    pub const shadow_space: u8 = 32;
    pub const arg_regs = [_]Gpr{ .rcx, .rdx, .r8, .r9 };
    pub const arg_xmms = [_]Xmm{ .xmm0, .xmm1, .xmm2, .xmm3 };
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
            else => return error.UnsupportedRegister,
        }
    }

    pub fn movMemRax(self: *Encoder, reg: Gpr) !void {
        switch (reg) {
            .rcx => try self.emit(&.{ 0x48, 0x89, 0x01 }),
            .rdi => try self.emit(&.{ 0x48, 0x89, 0x07 }),
            else => return error.UnsupportedRegister,
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

    pub fn addRaxImm8(self: *Encoder, imm: u8) !void {
        try self.emit(&.{ 0x48, 0x83, 0xC0, imm });
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

    pub fn movsdXmm0Xmm1(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x10, 0xC1 });
    }

    pub fn movsdXmm1Xmm0(self: *Encoder) !void {
        try self.emit(&.{ 0xF2, 0x0F, 0x10, 0xC8 });
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

    /// MOVZX rax, al - zero-extend al to rax
    pub fn movzxRaxAl(self: *Encoder) !void {
        try self.emit(&.{ 0x48, 0x0F, 0xB6, 0xC0 });
    }
};
