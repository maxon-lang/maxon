const std = @import("std");
const ir = @import("ir.zig");
const debug = @import("debug.zig");

/// Call site to patch after all functions are generated
const CallPatch = struct {
    offset: usize, // Offset of the 4-byte relative address in the code
    target_func: []const u8,
};

/// x86-64 code generator from IR
pub const IrCodegen = struct {
    allocator: std.mem.Allocator,
    code: *std.ArrayListUnmanaged(u8),
    value_locations: std.AutoHashMapUnmanaged(ir.Value, ValueLocation),
    value_types: std.AutoHashMapUnmanaged(ir.Value, ir.Type),
    next_stack_offset: i32,
    // Track function start offsets for call resolution
    func_offsets: std.StringHashMapUnmanaged(usize),
    // Call sites that need patching
    call_patches: std.ArrayListUnmanaged(CallPatch),
    // Current function being generated
    current_func_name: []const u8,
    current_func_ret_type: ir.Type,
    // Values that are indirect pointers (stack slot contains a pointer, not the actual struct)
    indirect_ptrs: std.AutoHashMapUnmanaged(ir.Value, void),

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
            .func_offsets = .{},
            .call_patches = .{},
            .current_func_name = "",
            .current_func_ret_type = .void,
            .indirect_ptrs = .{},
        };
    }

    pub fn deinit(self: *IrCodegen) void {
        self.value_locations.deinit(self.allocator);
        self.value_types.deinit(self.allocator);
        self.func_offsets.deinit(self.allocator);
        self.call_patches.deinit(self.allocator);
        self.indirect_ptrs.deinit(self.allocator);
    }

    // Emit helpers
    fn emit(self: *IrCodegen, bytes: []const u8) !void {
        try self.code.appendSlice(self.allocator, bytes);
    }

    fn emitByte(self: *IrCodegen, byte: u8) !void {
        try self.code.append(self.allocator, byte);
    }

    /// Emit instruction with [rbp+offset] addressing
    /// prefix should end with ModR/M byte for 8-bit offset (mod=01, r/m=101)
    /// This will automatically switch to 32-bit offset encoding when needed
    fn emitWithOffset(self: *IrCodegen, prefix: []const u8, offset: i32) !void {
        if (offset >= -128 and offset <= 127) {
            // Use 8-bit displacement (mod=01)
            try self.emit(prefix);
            try self.emitByte(@bitCast(@as(i8, @intCast(offset))));
        } else {
            // Use 32-bit displacement (mod=10)
            // Change the last byte from mod=01 to mod=10
            if (prefix.len > 0) {
                try self.emit(prefix[0 .. prefix.len - 1]);
                const modrm = prefix[prefix.len - 1];
                // Change mod bits from 01 to 10: add 0x40 to the ModR/M byte
                try self.emitByte(modrm + 0x40);
            }
            // Emit 32-bit displacement
            try self.code.appendSlice(self.allocator, &@as([4]u8, @bitCast(offset)));
        }
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
        // Generate main function first (entry point)
        if (module.getFunction("main")) |func| {
            try self.func_offsets.put(self.allocator, func.name, self.code.items.len);
            try self.generateFunction(func);
        }

        // Generate other functions
        for (module.functions.items) |*func| {
            if (!std.mem.eql(u8, func.name, "main")) {
                try self.func_offsets.put(self.allocator, func.name, self.code.items.len);
                try self.generateFunction(func);
            }
        }

        // Patch call sites
        try self.patchCalls();
    }

    fn patchCalls(self: *IrCodegen) !void {
        for (self.call_patches.items) |patch| {
            const target_offset = self.func_offsets.get(patch.target_func) orelse continue;
            // Calculate relative offset: target - (patch_location + 4)
            const rel_offset: i32 = @intCast(@as(i64, @intCast(target_offset)) - @as(i64, @intCast(patch.offset + 4)));
            // Write the relative offset at the patch location
            const bytes: [4]u8 = @bitCast(rel_offset);
            self.code.items[patch.offset] = bytes[0];
            self.code.items[patch.offset + 1] = bytes[1];
            self.code.items[patch.offset + 2] = bytes[2];
            self.code.items[patch.offset + 3] = bytes[3];
        }
    }

    fn generateFunction(self: *IrCodegen, func: *const ir.Function) !void {
        self.value_locations.clearRetainingCapacity();
        self.value_types.clearRetainingCapacity();
        self.indirect_ptrs.clearRetainingCapacity();
        self.next_stack_offset = -8;
        self.current_func_name = func.name;
        self.current_func_ret_type = func.return_type;

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
            .getelemptr => try self.genGetElemPtr(inst),
            .store => try self.genStore(inst),
            .load => try self.genLoad(inst),
            .add, .sub, .mul, .div, .mod => try self.genIntBinaryOp(inst),
            .fadd, .fsub, .fmul, .fdiv => try self.genFloatBinaryOp(inst),
            .fptosi => try self.genFpToSi(inst),
            .sitofp => try self.genSiToFp(inst),
            .ret => try self.genRet(inst),
            .param => try self.genParam(inst),
            .call => try self.genCall(inst),
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

        // Check if base is an indirect pointer (e.g., a param that contains a pointer value)
        if (self.indirect_ptrs.contains(base_val)) {
            // Base is indirect - load the pointer, add offset, store the result
            // mov rax, [rbp+base_offset]  ; load the pointer value
            try self.emitWithOffset(&.{ 0x48, 0x8B, 0x45 }, base_stack_offset);
            // add rax, field_offset       ; add field offset
            if (field_offset != 0) {
                try self.emit(&.{ 0x48, 0x83, 0xC0 }); // add rax, imm8
                try self.emitByte(@intCast(@as(u32, @bitCast(field_offset)) & 0xFF));
            }
            // Store computed pointer to stack
            const result_offset = self.allocStackSlot();
            try self.emitWithOffset(&.{ 0x48, 0x89, 0x45 }, result_offset); // mov [rbp+off], rax
            try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = result_offset });
            try self.value_types.put(self.allocator, inst.result.?, .ptr);
            // The result is also indirect (it's a computed pointer stored on stack)
            try self.indirect_ptrs.put(self.allocator, inst.result.?, {});
        } else {
            // Base is direct - the stack slot IS the struct, just compute offset
            const field_stack_offset = base_stack_offset + field_offset;
            try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = field_stack_offset });
            try self.value_types.put(self.allocator, inst.result.?, .ptr);
        }
    }

    fn genGetElemPtr(self: *IrCodegen, inst: ir.Instruction) !void {
        const base_val = inst.operands[0].value;
        const index_val = inst.operands[1].value;
        const result = inst.result.?;

        // All elements are 8 bytes
        const elem_size: i64 = 8;

        // Load base pointer to RAX
        // First check if base is on stack (direct) or indirect
        if (self.value_locations.get(base_val)) |base_loc| {
            switch (base_loc) {
                .stack => |base_offset| {
                    // LEA rax, [rbp+base_offset] - get address of array start
                    try self.emitWithOffset(&.{ 0x48, 0x8D, 0x45 }, base_offset); // lea rax, [rbp+off]
                },
                else => {
                    try self.loadValueToRax(base_val);
                },
            }
        } else {
            try self.loadValueToRax(base_val);
        }

        // Save base to RCX
        try self.emit(&.{ 0x48, 0x89, 0xC1 }); // mov rcx, rax

        // Load index to RAX
        try self.loadValueToRax(index_val);

        // Multiply index by element size (8): shl rax, 3
        try self.emit(&.{ 0x48, 0xC1, 0xE0, 0x03 }); // shl rax, 3
        _ = elem_size;

        // Add base: add rax, rcx
        try self.emit(&.{ 0x48, 0x01, 0xC8 }); // add rax, rcx

        // Store result pointer to stack
        const result_offset = self.allocStackSlot();
        try self.emitWithOffset(&.{ 0x48, 0x89, 0x45 }, result_offset); // mov [rbp+off], rax
        try self.value_locations.put(self.allocator, result, .{ .stack = result_offset });
        try self.value_types.put(self.allocator, result, .ptr);

        // Mark as indirect - the stack slot contains a pointer
        try self.indirect_ptrs.put(self.allocator, result, {});
    }

    fn genStore(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr = inst.operands[0].value;
        const val = inst.operands[1].value;
        const offset = try self.getStackOffset(ptr);
        const val_type = self.value_types.get(val) orelse .i64;

        // Check if ptr is indirect (stack slot contains a pointer we need to load first)
        if (self.indirect_ptrs.contains(ptr)) {
            // Load the actual pointer value to rcx
            try self.emitWithOffset(&.{ 0x48, 0x8B, 0x4D }, offset); // mov rcx, [rbp+off]
            if (val_type == .f64) {
                try self.loadValueToXmm(val, .xmm0);
                try self.emit(&.{ 0xF2, 0x0F, 0x11, 0x01 }); // movsd [rcx], xmm0
            } else {
                try self.loadValueToRax(val);
                try self.emit(&.{ 0x48, 0x89, 0x01 }); // mov [rcx], rax
            }
        } else {
            // Direct store to stack location
            if (val_type == .f64) {
                try self.loadValueToXmm(val, .xmm0);
                try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x11, 0x45 }, offset); // movsd [rbp+off], xmm0
            } else {
                try self.loadValueToRax(val);
                try self.emitWithOffset(&.{ 0x48, 0x89, 0x45 }, offset); // mov [rbp+off], rax
            }
        }
    }

    fn genLoad(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const ptr = inst.operands[0].value;
        const offset = try self.getStackOffset(ptr);
        const ty = inst.result_type;

        // Check if ptr is indirect (stack slot contains a pointer we need to load first)
        if (self.indirect_ptrs.contains(ptr)) {
            // Load the actual pointer value to rcx
            try self.emitWithOffset(&.{ 0x48, 0x8B, 0x4D }, offset); // mov rcx, [rbp+off]
            if (ty == .f64) {
                try self.emit(&.{ 0xF2, 0x0F, 0x10, 0x01 }); // movsd xmm0, [rcx]
                try self.storeXmm0ToStack(result);
            } else {
                try self.emit(&.{ 0x48, 0x8B, 0x01 }); // mov rax, [rcx]
                try self.storeRaxToStack(result, ty);
            }
        } else {
            // Direct load from stack location
            if (ty == .f64) {
                try self.emitWithOffset(&.{ 0xF2, 0x0F, 0x10, 0x45 }, offset); // movsd xmm0, [rbp+off]
                try self.storeXmm0ToStack(result);
            } else {
                try self.emitWithOffset(&.{ 0x48, 0x8B, 0x45 }, offset); // mov rax, [rbp+off]
                try self.storeRaxToStack(result, ty);
            }
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

        // Store result to stack to avoid clobbering by subsequent operations
        try self.storeRaxToStack(result, .i64);
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
        // Load return value
        if (inst.operands[0] != .none) {
            const ret_val = inst.operands[0].value;
            const ret_type = self.value_types.get(ret_val) orelse .i64;

            debug.codegen("  ret: val=%{d}, type={s}, func={s}", .{ ret_val, ret_type.format(), self.current_func_name });

            if (ret_type == .f64) {
                // Float return - load to XMM0
                try self.loadValueToXmm(ret_val, .xmm0);
            } else {
                // Integer return - load to RAX
                try self.loadValueToRax(ret_val);
            }
        }

        // For main, call ExitProcess; for other functions, emit ret
        if (std.mem.eql(u8, self.current_func_name, "main")) {
            // mov rcx, rax (exit code in RCX for ExitProcess)
            try self.emit(&.{ 0x48, 0x89, 0xC1 });
            // call [rip+0] - will be patched by PE writer for ExitProcess
            try self.emit(&.{ 0xFF, 0x15, 0, 0, 0, 0 });
        } else {
            // Epilogue: mov rsp, rbp; pop rbp; ret
            try self.emit(&.{ 0x48, 0x89, 0xEC }); // mov rsp, rbp
            try self.emit(&.{ 0x5D }); // pop rbp
            try self.emit(&.{ 0xC3 }); // ret
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

            // Pointer params are indirect - the stack slot contains a pointer, not the struct itself
            if (ty == .ptr) {
                try self.indirect_ptrs.put(self.allocator, result, {});
            }
        }
    }

    fn genCall(self: *IrCodegen, inst: ir.Instruction) !void {
        const func_name = inst.operands[0].func_name;
        const args = inst.operands[1].call_args;
        const ret_type = inst.result_type;

        debug.codegen("  Calling {s} with {d} args", .{ func_name, args.len });

        // Load arguments into registers (Windows x64 calling convention)
        // First 4 args: RCX, RDX, R8, R9 (or XMM0-3 for floats)
        for (args, 0..) |arg, i| {
            const arg_type = self.value_types.get(arg) orelse .i64;

            if (arg_type == .f64) {
                // Float arg -> XMM register
                try self.loadValueToXmm(arg, if (i == 0) .xmm0 else .xmm1);
            } else {
                // Integer/pointer arg -> GPR
                // For struct pointers, we need LEA instead of MOV
                const loc = self.value_locations.get(arg) orelse continue;
                switch (loc) {
                    .stack => |offset| {
                        // lea reg, [rbp+offset] for pointers, mov reg, [rbp+offset] for values
                        if (arg_type == .ptr) {
                            // LEA - load effective address (get pointer to struct)
                            switch (i) {
                                0 => try self.emitWithOffset(&.{ 0x48, 0x8D, 0x4D }, offset), // lea rcx, [rbp+off]
                                1 => try self.emitWithOffset(&.{ 0x48, 0x8D, 0x55 }, offset), // lea rdx, [rbp+off]
                                2 => try self.emitWithOffset(&.{ 0x4C, 0x8D, 0x45 }, offset), // lea r8, [rbp+off]
                                3 => try self.emitWithOffset(&.{ 0x4C, 0x8D, 0x4D }, offset), // lea r9, [rbp+off]
                                else => {},
                            }
                        } else {
                            // MOV - load value
                            try self.loadValueToRax(arg);
                            switch (i) {
                                0 => try self.emit(&.{ 0x48, 0x89, 0xC1 }), // mov rcx, rax
                                1 => try self.emit(&.{ 0x48, 0x89, 0xC2 }), // mov rdx, rax
                                2 => try self.emit(&.{ 0x49, 0x89, 0xC0 }), // mov r8, rax
                                3 => try self.emit(&.{ 0x49, 0x89, 0xC1 }), // mov r9, rax
                                else => {},
                            }
                        }
                    },
                    else => {},
                }
            }
        }

        // Allocate shadow space (32 bytes) - Windows x64 requires this
        try self.emit(&.{ 0x48, 0x83, 0xEC, 0x20 }); // sub rsp, 32

        // Emit call with placeholder relative offset
        try self.emitByte(0xE8); // call rel32
        const patch_offset = self.code.items.len;
        try self.emit(&.{ 0x00, 0x00, 0x00, 0x00 }); // placeholder

        // Record patch site
        try self.call_patches.append(self.allocator, .{
            .offset = patch_offset,
            .target_func = func_name,
        });

        // Restore stack (remove shadow space)
        try self.emit(&.{ 0x48, 0x83, 0xC4, 0x20 }); // add rsp, 32

        // Handle return value
        if (inst.result) |result| {
            debug.codegen("  call result: %{d} of type {s}", .{ result, ret_type.format() });
            if (ret_type == .f64) {
                // Float return in XMM0 - store to stack
                try self.storeXmm0ToStack(result);
            } else {
                // Integer return in RAX - store to stack
                try self.storeRaxToStack(result, ret_type);
            }
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
