const std = @import("std");
const ir = @import("ir.zig");
const debug = @import("debug.zig");

/// x86-64 code generator from IR
pub const IrCodegen = struct {
    allocator: std.mem.Allocator,
    code: *std.ArrayListUnmanaged(u8),
    value_locations: std.AutoHashMapUnmanaged(ir.Value, ValueLocation),
    value_types: std.AutoHashMapUnmanaged(ir.Value, ir.Type),
    next_stack_offset: i32,

    const ValueLocation = union(enum) {
        stack: i32,
        register: Register,
        xmm: XmmRegister,
    };

    const Register = enum { rax, rcx, rdx, r8, r9 };
    const XmmRegister = enum { xmm0, xmm1 };

    pub fn init(allocator: std.mem.Allocator, code: *std.ArrayListUnmanaged(u8)) IrCodegen {
        return .{
            .allocator = allocator,
            .code = code,
            .value_locations = .{},
            .value_types = .{},
            .next_stack_offset = -8,
        };
    }

    pub fn deinit(self: *IrCodegen) void {
        self.value_locations.deinit(self.allocator);
        self.value_types.deinit(self.allocator);
    }

    // Emit helpers
    fn emit(self: *IrCodegen, bytes: []const u8) !void {
        try self.code.appendSlice(self.allocator, bytes);
    }

    fn emitByte(self: *IrCodegen, byte: u8) !void {
        try self.code.append(self.allocator, byte);
    }

    fn emitWithOffset(self: *IrCodegen, prefix: []const u8, offset: i32) !void {
        try self.emit(prefix);
        try self.emitByte(@bitCast(@as(i8, @intCast(offset))));
    }

    fn allocStackSlot(self: *IrCodegen) i32 {
        const offset = self.next_stack_offset;
        self.next_stack_offset -= 8;
        return offset;
    }

    fn setValueLocation(self: *IrCodegen, val: ir.Value, loc: ValueLocation, ty: ir.Type) !void {
        try self.value_locations.put(self.allocator, val, loc);
        try self.value_types.put(self.allocator, val, ty);
    }

    fn storeRaxToStack(self: *IrCodegen, result: ir.Value, ty: ir.Type) !void {
        const offset = self.allocStackSlot();
        try self.emitWithOffset(&.{ 0x48, 0x89, 0x45 }, offset); // mov [rbp+off], rax
        try self.setValueLocation(result, .{ .stack = offset }, ty);
    }

    fn storeXmm0ToStack(self: *IrCodegen, result: ir.Value) !void {
        const offset = self.allocStackSlot();
        try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x11, 0x45 }, offset); // movsd [rbp+off], xmm0
        try self.setValueLocation(result, .{ .stack = offset }, .f64);
    }

    fn getStackOffset(self: *IrCodegen, val: ir.Value) !i32 {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        return switch (loc) {
            .stack => |o| o,
            else => error.ExpectedStackLocation,
        };
    }

    // Value loading
    fn loadValueToRax(self: *IrCodegen, val: ir.Value) !void {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        switch (loc) {
            .register => |reg| {
                if (reg == .rcx) try self.emit(&.{ 0x48, 0x89, 0xC8 }) // mov rax, rcx
                else if (reg == .rdx) try self.emit(&.{ 0x48, 0x89, 0xD0 }) // mov rax, rdx
                else if (reg != .rax) return error.UnsupportedRegister;
            },
            .stack => |offset| try self.emitWithOffset(&.{ 0x48, 0x8B, 0x45 }, offset), // mov rax, [rbp+off]
            .xmm => return error.CannotLoadXmmToRax,
        }
    }

    fn loadValueToXmm(self: *IrCodegen, val: ir.Value, target: XmmRegister) !void {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        switch (loc) {
            .xmm => |reg| {
                if (reg != target) {
                    // movsd xmmN, xmmM
                    const opcode: u8 = if (target == .xmm0) 0xC1 else 0xC8;
                    try self.emit(&.{ 0xF2, 0x0F, 0x10, opcode });
                }
            },
            .stack => |offset| {
                const modrm: u8 = if (target == .xmm0) 0x45 else 0x4D;
                try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x10, modrm }, offset);
            },
            .register => return error.CannotLoadRegToXmm,
        }
    }

    // Code generation
    pub fn generateModule(self: *IrCodegen, module: ir.Module) !void {
        if (module.getFunction("main")) |func| {
            try self.generateFunction(func);
        }
    }

    fn generateFunction(self: *IrCodegen, func: *const ir.Function) !void {
        self.value_locations.clearRetainingCapacity();
        self.value_types.clearRetainingCapacity();
        self.next_stack_offset = -8;

        // Prologue: push rbp; mov rbp, rsp; sub rsp, 64
        try self.emit(&.{ 0x55, 0x48, 0x89, 0xE5, 0x48, 0x83, 0xEC, 0x40 });

        for (func.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                try self.generateInstruction(inst);
            }
        }
    }

    fn generateInstruction(self: *IrCodegen, inst: ir.Instruction) !void {
        debug.codegen("Generating: {s}, result: {?}", .{ inst.op.format(), inst.result });
        switch (inst.op) {
            .const_i64 => try self.genConst64(inst, inst.operands[0].immediate_i64, .i64),
            .const_f64 => try self.genConst64(inst, @bitCast(inst.operands[0].immediate_f64), .f64),
            .alloca => try self.genAlloca(inst),
            .alloca_sized => try self.genAllocaSized(inst),
            .getfieldptr => try self.genGetFieldPtr(inst),
            .store => try self.genStore(inst),
            .load => try self.genLoad(inst),
            .add, .sub, .mul, .div, .mod => try self.genIntBinaryOp(inst),
            .fadd, .fsub, .fmul, .fdiv => try self.genFloatBinaryOp(inst),
            .fptosi => try self.genFpToSi(inst),
            .sitofp => try self.genSiToFp(inst),
            .ret => try self.genRet(inst),
            .param => try self.genParam(inst),
            else => debug.codegen("  Skipping unhandled instruction", .{}),
        }
    }

    fn genConst64(self: *IrCodegen, inst: ir.Instruction, value: i64, ty: ir.Type) !void {
        const result = inst.result.?;
        const offset = self.allocStackSlot();
        try self.emit(&.{ 0x48, 0xB8 }); // mov rax, imm64
        try self.emit(&@as([8]u8, @bitCast(value)));
        try self.emitWithOffset(&.{ 0x48, 0x89, 0x45 }, offset); // mov [rbp+off], rax
        try self.setValueLocation(result, .{ .stack = offset }, ty);
    }

    fn genAlloca(self: *IrCodegen, inst: ir.Instruction) !void {
        const offset = self.allocStackSlot();
        try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = offset });
    }

    fn genAllocaSized(self: *IrCodegen, inst: ir.Instruction) !void {
        const size = inst.operands[0].immediate_i32;
        // Allocate enough slots for the struct (round up to 8 bytes)
        const num_slots: i32 = @divTrunc(size + 7, 8);
        // Allocate space going downward
        self.next_stack_offset -= num_slots * 8;
        // The struct base is at the new (lower) offset
        const base_offset = self.next_stack_offset;
        // The value is the base offset (pointer to start of struct)
        try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = base_offset });
        try self.value_types.put(self.allocator, inst.result.?, .ptr);
    }

    fn genGetFieldPtr(self: *IrCodegen, inst: ir.Instruction) !void {
        const base_val = inst.operands[0].value;
        const field_offset = inst.operands[1].immediate_i32;
        const base_stack_offset = try self.getStackOffset(base_val);
        // Field at struct offset N is at stack address (base + N)
        // e.g., struct at rbp-16: field 0 at rbp-16, field 1 at rbp-8
        const field_stack_offset = base_stack_offset + field_offset;
        try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = field_stack_offset });
        try self.value_types.put(self.allocator, inst.result.?, .ptr);
    }

    fn genStore(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr = inst.operands[0].value;
        const val = inst.operands[1].value;
        const offset = try self.getStackOffset(ptr);
        const val_type = self.value_types.get(val) orelse .i64;

        if (val_type == .f64) {
            try self.loadValueToXmm(val, .xmm0);
            try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x11, 0x45 }, offset); // movsd [rbp+off], xmm0
        } else {
            try self.loadValueToRax(val);
            try self.emitWithOffset(&.{ 0x48, 0x89, 0x45 }, offset); // mov [rbp+off], rax
        }
    }

    fn genLoad(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const offset = try self.getStackOffset(inst.operands[0].value);
        const ty = inst.result_type;

        if (ty == .f64) {
            // Load float to xmm0 then spill to stack (to avoid register clobbering)
            try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x10, 0x45 }, offset); // movsd xmm0, [rbp+off]
            try self.storeXmm0ToStack(result);
        } else {
            // Load int to rax then spill to stack (to avoid register clobbering)
            try self.emitWithOffset(&.{ 0x48, 0x8B, 0x45 }, offset); // mov rax, [rbp+off]
            try self.storeRaxToStack(result, ty);
        }
    }

    fn genIntBinaryOp(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;

        // Load lhs -> rax, rhs -> rcx
        try self.loadValueToRax(inst.operands[0].value);
        try self.emitByte(0x50); // push rax
        try self.loadValueToRax(inst.operands[1].value);
        try self.emit(&.{ 0x48, 0x89, 0xC1 }); // mov rcx, rax
        try self.emitByte(0x58); // pop rax

        switch (inst.op) {
            .add => try self.emit(&.{ 0x48, 0x01, 0xC8 }), // add rax, rcx
            .sub => try self.emit(&.{ 0x48, 0x29, 0xC8 }), // sub rax, rcx
            .mul => try self.emit(&.{ 0x48, 0x0F, 0xAF, 0xC1 }), // imul rax, rcx
            .div => try self.emit(&.{ 0x48, 0x99, 0x48, 0xF7, 0xF9 }), // cqo; idiv rcx
            .mod => {
                try self.emit(&.{ 0x48, 0x99, 0x48, 0xF7, 0xF9 }); // cqo; idiv rcx
                try self.emit(&.{ 0x48, 0x89, 0xD0 }); // mov rax, rdx
            },
            else => unreachable,
        }

        try self.setValueLocation(result, .{ .register = .rax }, .i64);
    }

    fn genFloatBinaryOp(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;

        // Load lhs to xmm0, save to temp, load rhs to xmm1, reload lhs
        try self.loadValueToXmm(inst.operands[0].value, .xmm0);
        const temp = self.allocStackSlot();
        try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x11, 0x45 }, temp); // movsd [rbp+temp], xmm0
        try self.loadValueToXmm(inst.operands[1].value, .xmm1);
        try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x10, 0x45 }, temp); // movsd xmm0, [rbp+temp]

        const opcode: u8 = switch (inst.op) {
            .fadd => 0x58,
            .fsub => 0x5C,
            .fmul => 0x59,
            .fdiv => 0x5E,
            else => unreachable,
        };
        try self.emit(&.{ 0xF2, 0x0F, opcode, 0xC1 }); // op xmm0, xmm1

        try self.storeXmm0ToStack(result);
    }

    fn genFpToSi(self: *IrCodegen, inst: ir.Instruction) !void {
        try self.loadValueToXmm(inst.operands[0].value, .xmm0);
        try self.emit(&.{ 0xF2, 0x48, 0x0F, 0x2C, 0xC0 }); // cvttsd2si rax, xmm0
        try self.storeRaxToStack(inst.result.?, .i64);
    }

    fn genSiToFp(self: *IrCodegen, inst: ir.Instruction) !void {
        try self.loadValueToRax(inst.operands[0].value);
        try self.emit(&.{ 0xF2, 0x48, 0x0F, 0x2A, 0xC0 }); // cvtsi2sd xmm0, rax
        try self.storeXmm0ToStack(inst.result.?);
    }

    fn genRet(self: *IrCodegen, inst: ir.Instruction) !void {
        if (inst.operands[0] != .none) {
            try self.loadValueToRax(inst.operands[0].value);
            try self.emit(&.{ 0x48, 0x89, 0xC1 }); // mov rcx, rax
            try self.emit(&.{ 0xFF, 0x15, 0, 0, 0, 0 }); // call [rip+0] (ExitProcess)
        }
    }

    fn genParam(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const param_idx = inst.operands[0].immediate_i32;
        const ty = inst.result_type;

        // Windows x64 calling convention: RCX, RDX, R8, R9 for first 4 args
        // Float params use XMM0-XMM3
        // For simplicity, store param to stack immediately

        const offset = self.allocStackSlot();

        if (ty == .f64) {
            // Float param in XMM register - store to stack
            const xmm_modrm: u8 = switch (param_idx) {
                0 => 0x45, // xmm0
                1 => 0x4D, // xmm1
                2 => 0x55, // xmm2
                3 => 0x5D, // xmm3
                else => 0x45, // fallback
            };
            try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x11, xmm_modrm }, offset); // movsd [rbp+off], xmmN
            try self.setValueLocation(result, .{ .stack = offset }, .f64);
        } else {
            // Integer/pointer param in GPR - mov to rax then store
            switch (param_idx) {
                0 => try self.emit(&.{ 0x48, 0x89, 0xC8 }), // mov rax, rcx
                1 => try self.emit(&.{ 0x48, 0x89, 0xD0 }), // mov rax, rdx
                2 => try self.emit(&.{ 0x4C, 0x89, 0xC0 }), // mov rax, r8
                3 => try self.emit(&.{ 0x4C, 0x89, 0xC8 }), // mov rax, r9
                else => {}, // stack params not supported yet
            }
            try self.emitWithOffset(&.{ 0x48, 0x89, 0x45 }, offset); // mov [rbp+off], rax
            try self.setValueLocation(result, .{ .stack = offset }, ty);
        }
    }
};

pub fn generate(module: ir.Module, allocator: std.mem.Allocator) ![]u8 {
    var code: std.ArrayListUnmanaged(u8) = .empty;
    errdefer code.deinit(allocator);
    var codegen = IrCodegen.init(allocator, &code);
    defer codegen.deinit();
    try codegen.generateModule(module);
    return code.toOwnedSlice(allocator);
}
