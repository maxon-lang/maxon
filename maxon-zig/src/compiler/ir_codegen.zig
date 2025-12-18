const std = @import("std");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const x86 = @import("x86.zig");

/// Call site to patch after all functions are generated
const CallPatch = struct {
    offset: usize,
    target_func: []const u8,
};

/// External call site to patch with IAT address
pub const ExternalCallPatch = struct {
    offset: usize,
    dll: []const u8,
    func_name: []const u8,
};

const ValueLocation = union(enum) {
    stack: i32,
    register: x86.Gpr,
    xmm: x86.Xmm,
};

/// x86-64 code generator from IR
pub const IrCodegen = struct {
    allocator: std.mem.Allocator,
    code: *std.ArrayListUnmanaged(u8),
    enc: x86.Encoder,
    value_locations: std.AutoHashMapUnmanaged(ir.Value, ValueLocation),
    value_types: std.AutoHashMapUnmanaged(ir.Value, ir.Type),
    next_stack_offset: i32,
    func_offsets: std.StringHashMapUnmanaged(usize),
    call_patches: std.ArrayListUnmanaged(CallPatch),
    current_func_name: []const u8,
    current_func_ret_type: ir.Type,
    indirect_ptrs: std.AutoHashMapUnmanaged(ir.Value, void),
    external_patches: std.ArrayListUnmanaged(ExternalCallPatch),

    pub fn init(allocator: std.mem.Allocator, code: *std.ArrayListUnmanaged(u8)) IrCodegen {
        return .{
            .allocator = allocator,
            .code = code,
            .enc = x86.Encoder.init(allocator, code),
            .value_locations = .{},
            .value_types = .{},
            .next_stack_offset = -8,
            .func_offsets = .{},
            .call_patches = .{},
            .current_func_name = "",
            .current_func_ret_type = .void,
            .indirect_ptrs = .{},
            .external_patches = .{},
        };
    }

    pub fn deinit(self: *IrCodegen) void {
        self.value_locations.deinit(self.allocator);
        self.value_types.deinit(self.allocator);
        self.func_offsets.deinit(self.allocator);
        self.call_patches.deinit(self.allocator);
        self.indirect_ptrs.deinit(self.allocator);
        self.external_patches.deinit(self.allocator);
    }

    /// Get external call patches for PE writer
    pub fn getExternalPatches(self: *IrCodegen) []const ExternalCallPatch {
        return self.external_patches.items;
    }

    // ------------------------------------------------------------------------
    // Stack Allocation
    // ------------------------------------------------------------------------

    fn allocStackSlots(self: *IrCodegen, count: i32) i32 {
        const offset = self.next_stack_offset;
        self.next_stack_offset -= count * 8;
        return offset;
    }

    // ------------------------------------------------------------------------
    // Value Location Tracking
    // ------------------------------------------------------------------------

    fn setValueLocation(self: *IrCodegen, val: ir.Value, loc: ValueLocation, ty: ir.Type) !void {
        try self.value_locations.put(self.allocator, val, loc);
        try self.value_types.put(self.allocator, val, ty);
    }

    fn getStackOffset(self: *IrCodegen, val: ir.Value) !i32 {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        return switch (loc) {
            .stack => |o| o,
            else => error.ExpectedStackLocation,
        };
    }

    fn isIndirect(self: *IrCodegen, val: ir.Value) bool {
        return self.indirect_ptrs.contains(val);
    }

    fn markIndirect(self: *IrCodegen, val: ir.Value) !void {
        try self.indirect_ptrs.put(self.allocator, val, {});
    }

    // ------------------------------------------------------------------------
    // Value Loading / Storing
    // ------------------------------------------------------------------------

    const LoadTarget = union(enum) { rax, xmm: x86.Xmm };

    fn storeToStack(self: *IrCodegen, result: ir.Value, ty: ir.Type) !void {
        const offset = self.allocStackSlots(1);
        if (ty == .f64) {
            try self.enc.movsdRbpOffsetXmm0(offset);
        } else {
            try self.enc.movRbpOffsetRax(offset);
        }
        try self.setValueLocation(result, .{ .stack = offset }, ty);
    }

    fn loadValue(self: *IrCodegen, val: ir.Value, target: LoadTarget) !void {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        switch (loc) {
            .stack => |offset| switch (target) {
                .rax => try self.enc.movRaxRbpOffset(offset),
                .xmm => |xmm| if (xmm == .xmm0) try self.enc.movsdXmm0RbpOffset(offset) else try self.enc.movsdXmm1RbpOffset(offset),
            },
            .register => |reg| switch (target) {
                .rax => {
                    if (reg == .rcx) try self.enc.movRaxRcx()
                    else if (reg == .rdx) try self.enc.movRaxRdx()
                    else if (reg != .rax) return error.UnsupportedRegister;
                },
                .xmm => return error.CannotLoadRegToXmm,
            },
            .xmm => |reg| switch (target) {
                .rax => return error.CannotLoadXmmToRax,
                .xmm => |xmm| if (reg != xmm) {
                    if (xmm == .xmm0) try self.enc.movsdXmm0Xmm1() else try self.enc.movsdXmm1Xmm0();
                },
            },
        }
    }

    fn loadToRax(self: *IrCodegen, val: ir.Value) !void {
        try self.loadValue(val, .rax);
    }

    fn loadToXmm(self: *IrCodegen, val: ir.Value, target: x86.Xmm) !void {
        try self.loadValue(val, .{ .xmm = target });
    }

    // ------------------------------------------------------------------------
    // Module / Function Generation
    // ------------------------------------------------------------------------

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

        try self.enc.prologue(256);

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
            .alloca_dynamic => try self.genAllocaDynamic(inst),
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
            .memcpy => try self.genMemcpy(inst),
            .call_external => try self.genCallExternal(inst),
            .heap_alloc => try self.genHeapAlloc(inst),
            .heap_free => try self.genHeapFree(inst),
            else => debug.codegen("  Skipping unhandled instruction", .{}),
        }
    }

    // ------------------------------------------------------------------------
    // Instruction Generators
    // ------------------------------------------------------------------------

    fn genConst64(self: *IrCodegen, inst: ir.Instruction, value: i64, ty: ir.Type) !void {
        const result = inst.result.?;
        const offset = self.allocStackSlots(1);
        try self.enc.movRaxImm64(value);
        try self.enc.movRbpOffsetRax(offset);
        try self.setValueLocation(result, .{ .stack = offset }, ty);
    }

    fn genAlloca(self: *IrCodegen, inst: ir.Instruction) !void {
        const offset = self.allocStackSlots(1);
        try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = offset });
    }

    fn genAllocaSized(self: *IrCodegen, inst: ir.Instruction) !void {
        const size = inst.operands[0].immediate_i32;
        // Allocate enough slots for the struct (round up to 8 bytes)
        const num_slots: i32 = @divTrunc(size + 7, 8);
        // allocStackSlots returns the start of the allocated region
        // For a struct, field 0 is at the start (lowest address on stack = highest rbp offset)
        const base_offset = self.allocStackSlots(num_slots);
        // Adjust to point to the actual struct start (lowest stack address)
        const struct_offset = base_offset - (num_slots - 1) * 8;
        try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = struct_offset });
        try self.value_types.put(self.allocator, inst.result.?, .ptr);
    }

    fn genAllocaDynamic(self: *IrCodegen, inst: ir.Instruction) !void {
        // Dynamic stack allocation: size comes from a runtime value
        const size_val = inst.operands[0].value;
        try self.loadToRax(size_val);

        // Round up to 16-byte alignment: (size + 15) & ~15
        try self.enc.addRaxImm8(0x0F);
        try self.enc.andRaxImm8(0xF0);

        // Reserve space on stack
        try self.enc.subRspRax();

        // Save RSP to a stack slot
        const result_offset = self.allocStackSlots(1);
        try self.enc.emitWithRbpOffset(&.{ 0x48, 0x89, 0x65 }, result_offset); // mov [rbp+off], rsp

        try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = result_offset });
        try self.value_types.put(self.allocator, inst.result.?, .ptr);
        try self.markIndirect(inst.result.?);
    }

    fn genGetFieldPtr(self: *IrCodegen, inst: ir.Instruction) !void {
        const base_val = inst.operands[0].value;
        const field_offset = inst.operands[1].immediate_i32;
        const base_stack_offset = try self.getStackOffset(base_val);

        if (self.isIndirect(base_val)) {
            // Base is indirect - load the pointer, add offset, store the result
            try self.enc.movRaxRbpOffset(base_stack_offset);
            if (field_offset != 0) {
                try self.enc.addRaxImm8(@intCast(@as(u32, @bitCast(field_offset)) & 0xFF));
            }
            const result_offset = self.allocStackSlots(1);
            try self.enc.movRbpOffsetRax(result_offset);
            try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = result_offset });
            try self.value_types.put(self.allocator, inst.result.?, .ptr);
            try self.markIndirect(inst.result.?);
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

        // Load base pointer to RAX
        if (self.value_locations.get(base_val)) |base_loc| {
            switch (base_loc) {
                .stack => |base_offset| {
                    if (self.isIndirect(base_val)) {
                        try self.enc.movRaxRbpOffset(base_offset);
                    } else {
                        try self.enc.leaRaxRbpOffset(base_offset);
                    }
                },
                else => try self.loadToRax(base_val),
            }
        } else {
            try self.loadToRax(base_val);
        }

        // Save base to RCX
        try self.enc.movRcxRax();

        // Load index to RAX
        try self.loadToRax(index_val);

        // Multiply index by element size (8): shl rax, 3
        try self.enc.shlRaxImm8(3);

        // Add base: add rax, rcx
        try self.enc.addRaxRcx();

        // Store result pointer to stack
        const result_offset = self.allocStackSlots(1);
        try self.enc.movRbpOffsetRax(result_offset);
        try self.value_locations.put(self.allocator, result, .{ .stack = result_offset });
        try self.value_types.put(self.allocator, result, .ptr);
        try self.markIndirect(result);
    }

    fn genStore(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr = inst.operands[0].value;
        const val = inst.operands[1].value;
        const offset = try self.getStackOffset(ptr);
        const val_type = self.value_types.get(val) orelse .i64;

        if (self.isIndirect(ptr)) {
            try self.enc.movRcxRbpOffset(offset);
            if (val_type == .f64) {
                try self.loadToXmm(val, .xmm0);
                try self.enc.movsdMemRcxXmm0();
            } else {
                try self.loadToRax(val);
                try self.enc.movMemRax(.rcx);
            }
        } else {
            if (val_type == .f64) {
                try self.loadToXmm(val, .xmm0);
                try self.enc.movsdRbpOffsetXmm0(offset);
            } else {
                try self.loadToRax(val);
                try self.enc.movRbpOffsetRax(offset);
            }
        }
    }

    fn genMemcpy(self: *IrCodegen, inst: ir.Instruction) !void {
        const dest_val = inst.operands[0].value;
        const src_val = inst.operands[1].value;
        const size: i32 = @intCast(inst.result.?);

        // Load dest pointer to rdi
        const dest_offset = try self.getStackOffset(dest_val);
        if (self.isIndirect(dest_val)) {
            try self.enc.emitWithRbpOffset(&.{ 0x48, 0x8B, 0x7D }, dest_offset); // mov rdi, [rbp+off]
        } else {
            try self.enc.emitWithRbpOffset(&.{ 0x48, 0x8D, 0x7D }, dest_offset); // lea rdi, [rbp+off]
        }

        // Load src pointer to rsi
        const src_offset = try self.getStackOffset(src_val);
        if (self.isIndirect(src_val)) {
            try self.enc.emitWithRbpOffset(&.{ 0x48, 0x8B, 0x75 }, src_offset); // mov rsi, [rbp+off]
        } else {
            try self.enc.emitWithRbpOffset(&.{ 0x48, 0x8D, 0x75 }, src_offset); // lea rsi, [rbp+off]
        }

        // Copy 8 bytes at a time
        var copied: i32 = 0;
        while (copied < size) : (copied += 8) {
            if (copied == 0) {
                try self.enc.movRaxMem(.rsi);
            } else {
                try self.enc.emit(&.{ 0x48, 0x8B, 0x46, @intCast(copied) }); // mov rax, [rsi+off]
            }
            if (copied == 0) {
                try self.enc.movMemRax(.rdi);
            } else {
                try self.enc.emit(&.{ 0x48, 0x89, 0x47, @intCast(copied) }); // mov [rdi+off], rax
            }
        }
    }

    fn genLoad(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const ptr = inst.operands[0].value;
        const offset = try self.getStackOffset(ptr);
        const ty = inst.result_type;

        if (self.isIndirect(ptr)) {
            try self.enc.movRcxRbpOffset(offset);
            if (ty == .f64) {
                try self.enc.movsdXmm0MemRcx();
            } else {
                try self.enc.movRaxMem(.rcx);
            }
        } else {
            if (ty == .f64) {
                try self.enc.movsdXmm0RbpOffset(offset);
            } else {
                try self.enc.movRaxRbpOffset(offset);
            }
        }
        try self.storeToStack(result, ty);
    }

    fn genIntBinaryOp(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;

        // Load lhs -> rax, rhs -> rcx
        try self.loadToRax(inst.operands[0].value);
        try self.enc.pushRax();
        try self.loadToRax(inst.operands[1].value);
        try self.enc.movRcxRax();
        try self.enc.popRax();

        switch (inst.op) {
            .add => try self.enc.addRaxRcx(),
            .sub => try self.enc.subRaxRcx(),
            .mul => try self.enc.imulRaxRcx(),
            .div => try self.enc.idivRcx(),
            .mod => {
                try self.enc.idivRcx();
                try self.enc.movRaxRdx();
            },
            else => unreachable,
        }

        try self.storeToStack(result, .i64);
    }

    fn genFloatBinaryOp(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;

        // Load lhs to xmm0, save to temp, load rhs to xmm1, reload lhs
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        const temp = self.allocStackSlots(1);
        try self.enc.movsdRbpOffsetXmm0(temp);
        try self.loadToXmm(inst.operands[1].value, .xmm1);
        try self.enc.movsdXmm0RbpOffset(temp);

        switch (inst.op) {
            .fadd => try self.enc.addsdXmm0Xmm1(),
            .fsub => try self.enc.subsdXmm0Xmm1(),
            .fmul => try self.enc.mulsdXmm0Xmm1(),
            .fdiv => try self.enc.divsdXmm0Xmm1(),
            else => unreachable,
        }

        try self.storeToStack(result, .f64);
    }

    fn genFpToSi(self: *IrCodegen, inst: ir.Instruction) !void {
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        try self.enc.cvttsd2siRaxXmm0();
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genSiToFp(self: *IrCodegen, inst: ir.Instruction) !void {
        try self.loadToRax(inst.operands[0].value);
        try self.enc.cvtsi2sdXmm0Rax();
        try self.storeToStack(inst.result.?, .f64);
    }

    fn genRet(self: *IrCodegen, inst: ir.Instruction) !void {
        if (inst.operands[0] != .none) {
            const ret_val = inst.operands[0].value;
            const ret_type = self.value_types.get(ret_val) orelse .i64;

            debug.codegen("  ret: val=%{d}, type={s}, func={s}", .{ ret_val, ret_type.format(), self.current_func_name });

            if (ret_type == .f64) {
                try self.loadToXmm(ret_val, .xmm0);
            } else {
                try self.loadToRax(ret_val);
            }
        }

        if (std.mem.eql(u8, self.current_func_name, "main")) {
            // Exit code in RCX for ExitProcess
            try self.enc.movRcxRax();
            // call [rip+0] - patched by PE writer for ExitProcess
            try self.enc.emit(&.{ 0xFF, 0x15, 0, 0, 0, 0 });
        } else {
            try self.enc.epilogue();
        }
    }

    fn genParam(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const param_idx = inst.operands[0].immediate_i32;
        const ty = inst.result_type;
        const offset = self.allocStackSlots(1);

        if (ty == .f64) {
            // Float param in XMM register - store to stack
            const xmm_modrm: u8 = switch (param_idx) {
                0 => 0x45, // xmm0
                1 => 0x4D, // xmm1
                2 => 0x55, // xmm2
                3 => 0x5D, // xmm3
                else => return error.TooManyParameters,
            };
            try self.enc.emitWithRbpOffset(&.{ 0xF2, 0x0F, 0x11, xmm_modrm }, offset);
            try self.setValueLocation(result, .{ .stack = offset }, .f64);
        } else {
            // Integer/pointer param in GPR - mov to rax then store
            switch (param_idx) {
                0 => try self.enc.movRaxRcx(),
                1 => try self.enc.movRaxRdx(),
                2 => try self.enc.emit(&.{ 0x4C, 0x89, 0xC0 }), // mov rax, r8
                3 => try self.enc.emit(&.{ 0x4C, 0x89, 0xC8 }), // mov rax, r9
                else => return error.TooManyParameters,
            }
            try self.enc.movRbpOffsetRax(offset);
            try self.setValueLocation(result, .{ .stack = offset }, ty);

            if (ty == .ptr) {
                try self.markIndirect(result);
            }
        }
    }

    fn genCall(self: *IrCodegen, inst: ir.Instruction) !void {
        const func_name = inst.operands[0].func_name;
        const args = inst.operands[1].call_args;
        const ret_type = inst.result_type;

        debug.codegen("  Calling {s} with {d} args", .{ func_name, args.len });

        try self.loadArgs(args, true);
        try self.enc.allocShadowSpace();
        const patch_offset = try self.enc.callRel32();
        try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = func_name });
        try self.enc.freeShadowSpace();

        if (inst.result) |result| {
            debug.codegen("  call result: %{d} of type {s}", .{ result, ret_type.format() });
            try self.storeReturnValue(result, ret_type);
        }
    }

    fn genCallExternal(self: *IrCodegen, inst: ir.Instruction) !void {
        const ext_func = inst.operands[0].external_func;
        const args = inst.operands[1].call_args;
        const ret_type = inst.result_type;

        debug.codegen("  Calling external {s}!{s} with {d} args", .{ ext_func.dll, ext_func.name, args.len });

        try self.loadArgs(args, false);
        try self.emitExternalCall(ext_func.dll, ext_func.name);

        if (inst.result) |result| {
            try self.storeReturnValue(result, ret_type);
        }
    }

    /// Load arguments into registers. use_lea controls whether direct pointers use LEA.
    fn loadArgs(self: *IrCodegen, args: []const ir.Value, use_lea: bool) !void {
        for (args, 0..) |arg, i| {
            if (i >= 4) return error.TooManyArguments;

            const arg_type = self.value_types.get(arg) orelse .i64;

            if (arg_type == .f64) {
                try self.loadToXmm(arg, if (i == 0) .xmm0 else .xmm1);
            } else if (use_lea and arg_type == .ptr and !self.isIndirect(arg)) {
                // LEA - get pointer to struct on stack
                const loc = self.value_locations.get(arg) orelse return error.ValueNotFound;
                const offset = switch (loc) {
                    .stack => |o| o,
                    else => return error.UnsupportedArgumentLocation,
                };
                try self.emitLeaArgReg(i, offset);
            } else {
                try self.loadToRax(arg);
                try self.movArgRegRax(i);
            }
        }
    }

    fn emitLeaArgReg(self: *IrCodegen, arg_idx: usize, offset: i32) !void {
        switch (arg_idx) {
            0 => try self.enc.emitWithRbpOffset(&.{ 0x48, 0x8D, 0x4D }, offset), // lea rcx
            1 => try self.enc.emitWithRbpOffset(&.{ 0x48, 0x8D, 0x55 }, offset), // lea rdx
            2 => try self.enc.emitWithRbpOffset(&.{ 0x4C, 0x8D, 0x45 }, offset), // lea r8
            3 => try self.enc.emitWithRbpOffset(&.{ 0x4C, 0x8D, 0x4D }, offset), // lea r9
            else => unreachable,
        }
    }

    fn movArgRegRax(self: *IrCodegen, arg_idx: usize) !void {
        switch (arg_idx) {
            0 => try self.enc.movRcxRax(),
            1 => try self.enc.movRdxRax(),
            2 => try self.enc.movR8Rax(),
            3 => try self.enc.movR9Rax(),
            else => unreachable,
        }
    }

    fn storeReturnValue(self: *IrCodegen, result: ir.Value, ret_type: ir.Type) !void {
        try self.storeToStack(result, ret_type);
        if (ret_type == .ptr) {
            try self.markIndirect(result);
        }
    }

    /// Emit an external call and record patch site
    fn emitExternalCall(self: *IrCodegen, dll: []const u8, func_name: []const u8) !void {
        try self.enc.allocShadowSpace();
        const patch_offset = try self.enc.callIndirectRip();
        try self.external_patches.append(self.allocator, .{ .offset = patch_offset, .dll = dll, .func_name = func_name });
        try self.enc.freeShadowSpace();
    }

    fn genHeapAlloc(self: *IrCodegen, inst: ir.Instruction) !void {
        const size_val = inst.operands[0].value;
        debug.codegen("  HeapAlloc: size=%{d}", .{size_val});

        // Save size to R12 (callee-saved)
        try self.loadToRax(size_val);
        try self.enc.movR12Rax();

        try self.emitExternalCall("kernel32.dll", "GetProcessHeap");

        // HeapAlloc(hHeap=RAX, dwFlags=0, dwBytes=size)
        try self.enc.movRcxRax();
        try self.enc.xorRdxRdx();
        try self.enc.movR8R12();

        try self.emitExternalCall("kernel32.dll", "HeapAlloc");

        try self.storeToStack(inst.result.?, .ptr);
        try self.markIndirect(inst.result.?);
    }

    fn genHeapFree(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr_val = inst.operands[0].value;
        debug.codegen("  HeapFree: ptr=%{d}", .{ptr_val});

        // Save ptr to R12
        try self.loadToRax(ptr_val);
        try self.enc.movR12Rax();

        try self.emitExternalCall("kernel32.dll", "GetProcessHeap");

        // HeapFree(hHeap=RAX, dwFlags=0, lpMem=ptr)
        try self.enc.movRcxRax();
        try self.enc.xorRdxRdx();
        try self.enc.movR8R12();

        try self.emitExternalCall("kernel32.dll", "HeapFree");
    }
};

pub const CodegenResult = struct {
    code: []u8,
    external_patches: []const ExternalCallPatch,
};

pub fn generate(module: ir.Module, allocator: std.mem.Allocator) !CodegenResult {
    var code: std.ArrayListUnmanaged(u8) = .empty;
    errdefer code.deinit(allocator);
    var codegen = IrCodegen.init(allocator, &code);
    defer codegen.deinit();
    try codegen.generateModule(module);

    // Copy external patches to owned slice so they outlive codegen
    const patches = try allocator.dupe(ExternalCallPatch, codegen.external_patches.items);

    return .{
        .code = try code.toOwnedSlice(allocator),
        .external_patches = patches,
    };
}
