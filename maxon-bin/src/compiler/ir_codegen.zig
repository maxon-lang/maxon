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

/// Jump to patch after block addresses are known
const JumpPatch = struct {
    offset: usize, // Offset in code where the rel32 displacement lives
    target_block: u32, // Block index to jump to
};

/// Tracking data field offsets (relative to tracking_data_offset)
const TrackingDataField = enum(usize) {
    next_alloc_id = 0, // i64: next allocation ID (starts at 0, incremented before use)
    total_allocated = 8, // i64: total bytes allocated
    total_freed = 16, // i64: total bytes freed
    table_count = 24, // i64: number of entries in tracking table
    table_base = 32, // start of tracking table (256 entries * 24 bytes each)
};

/// Patch for RIP-relative access to tracking data
const TrackingDataPatch = struct {
    code_offset: usize, // Where the RIP-relative displacement is in code
    field: TrackingDataField, // Which field we're accessing
};

/// Code generation options
pub const CodegenOptions = struct {
    track_allocs: bool = false,
};

/// Register allocation information for a function
/// Tracks register assignments, stack slots, and callee-saved register usage
/// for proper function prologue/epilogue generation
const RegAllocInfo = struct {
    /// Maps IR values to allocated GPRs
    reg_map: std.AutoHashMapUnmanaged(ir.Value, x86.Gpr),
    /// Maps IR values to allocated XMM registers
    xmm_map: std.AutoHashMapUnmanaged(ir.Value, x86.Xmm),
    /// Maps IR values to stack slot offsets (relative to RBP)
    stack_slots: std.AutoHashMapUnmanaged(ir.Value, i32),
    /// Calculated frame size
    frame_size: i32,
    /// Callee-saved GPRs that were used and need save/restore
    used_callee_saved_gprs: std.ArrayListUnmanaged(x86.Gpr),
    /// Callee-saved XMM registers that were used and need save/restore
    used_callee_saved_xmms: std.ArrayListUnmanaged(x86.Xmm),
    /// Maximum outgoing stack arguments (for calls with >4 args)
    outgoing_stack_args_size: u32,
    /// Whether function has hidden return pointer (large struct return)
    /// Windows x64 ABI: Large structs (> 8 bytes) are returned via hidden pointer in RCX
    has_hidden_ret_ptr: bool,
    /// Stack offset where hidden return pointer is saved (valid when has_hidden_ret_ptr is true)
    /// The caller passes the return buffer pointer in RCX, which we save here in the prologue
    hidden_ret_ptr_offset: i32,

    fn init() RegAllocInfo {
        return .{
            .reg_map = .{},
            .xmm_map = .{},
            .stack_slots = .{},
            .frame_size = 0,
            .used_callee_saved_gprs = .{},
            .used_callee_saved_xmms = .{},
            .outgoing_stack_args_size = 0,
            .has_hidden_ret_ptr = false,
            .hidden_ret_ptr_offset = 0,
        };
    }

    fn deinit(self: *RegAllocInfo, allocator: std.mem.Allocator) void {
        self.reg_map.deinit(allocator);
        self.xmm_map.deinit(allocator);
        self.stack_slots.deinit(allocator);
        self.used_callee_saved_gprs.deinit(allocator);
        self.used_callee_saved_xmms.deinit(allocator);
    }
};

/// Liveness analysis results
/// Tracks which values are live across function calls, which is critical
/// for register allocation - such values must be in callee-saved registers or spilled
const LivenessInfo = struct {
    /// Values that are live across at least one call instruction
    live_across_calls: std.AutoHashMapUnmanaged(ir.Value, void),

    fn init() LivenessInfo {
        return .{
            .live_across_calls = .{},
        };
    }

    fn deinit(self: *LivenessInfo, allocator: std.mem.Allocator) void {
        self.live_across_calls.deinit(allocator);
    }
};

/// Analyze which values are live across function calls
/// Algorithm from old compiler (x86_reg_alloc.cpp lines 88-189):
/// - For each basic block with calls, track where values are defined
/// - For each operand use, check if any call exists between definition and use
/// - If so, mark value as live across calls
/// - Parameters are conservatively marked live across all calls
fn analyzeLiveness(allocator: std.mem.Allocator, func: *const ir.Function) !LivenessInfo {
    var info = LivenessInfo.init();

    for (func.blocks.items) |block| {
        // Find all call instruction indices in this block
        var call_indices: std.ArrayListUnmanaged(usize) = .empty;
        defer call_indices.deinit(allocator);

        for (block.instructions.items, 0..) |inst, i| {
            if (inst.op == .call) {
                try call_indices.append(allocator, i);
            }
        }

        if (call_indices.items.len == 0) continue;

        // Track where each value is defined in this block
        var def_index: std.AutoHashMapUnmanaged(ir.Value, usize) = .{};
        defer def_index.deinit(allocator);

        for (block.instructions.items, 0..) |inst, i| {
            // Record definition point for this instruction's result
            if (inst.result) |result| {
                try def_index.put(allocator, result, i);
            }

            // Check each operand use to see if it crosses a call
            const operands = [_]ir.Instruction.Operand{ inst.operands[0], inst.operands[1] };
            for (operands) |operand| {
                switch (operand) {
                    .value => |val| {
                        if (def_index.get(val)) |def_idx| {
                            // Value defined in this block - check if any call is between def and use
                            for (call_indices.items) |call_idx| {
                                if (def_idx < call_idx and call_idx < i) {
                                    try info.live_across_calls.put(allocator, val, {});
                                    break;
                                }
                            }
                        } else {
                            // Value defined in previous block or is a parameter
                            // If there's any call before this use, value crosses it
                            for (call_indices.items) |call_idx| {
                                if (call_idx < i) {
                                    try info.live_across_calls.put(allocator, val, {});
                                    break;
                                }
                            }
                        }
                    },
                    .call_args => |args| {
                        // Check each argument in the call
                        for (args) |arg_val| {
                            if (def_index.get(arg_val)) |def_idx| {
                                for (call_indices.items) |call_idx| {
                                    if (def_idx < call_idx and call_idx < i) {
                                        try info.live_across_calls.put(allocator, arg_val, {});
                                        break;
                                    }
                                }
                            } else {
                                for (call_indices.items) |call_idx| {
                                    if (call_idx < i) {
                                        try info.live_across_calls.put(allocator, arg_val, {});
                                        break;
                                    }
                                }
                            }
                        }
                    },
                    else => {},
                }
            }
        }
    }

    return info;
}

/// Check if a return type requires hidden pointer (large struct/optional > 8 bytes)
/// Windows x64 ABI: Large structs and optionals are returned via a hidden pointer
/// passed in RCX, which shifts all other parameters right by one register.
///
/// Currently ir.Type doesn't have struct size info, so this is infrastructure
/// for future when we add struct types with size information. For now, this
/// returns false as all current types fit in 8 bytes or less.
fn hasHiddenReturnPointer(ret_type: ir.Type) bool {
    // Current IR types are all <= 8 bytes:
    // - i32, i64, f64: scalar types that fit in a register
    // - ptr: 8-byte pointer
    // - void: no return value
    //
    // Future enhancement: When struct types are added to ir.Type with size info,
    // this should return true for structs/optionals > 8 bytes.
    _ = ret_type;
    return false;
}

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
    // Block tracking for branches
    block_offsets: std.ArrayListUnmanaged(usize),
    jump_patches: std.ArrayListUnmanaged(JumpPatch),
    // Allocation tracking
    track_allocs: bool,
    // Offsets to tracking data (set after all code is generated)
    tracking_data_offset: ?usize = null,
    // Patches for RIP-relative tracking data access
    tracking_data_patches: std.ArrayListUnmanaged(TrackingDataPatch),
    // Function return types from the module
    func_return_types: std.StringHashMapUnmanaged(ir.Type),
    // Track the type of value stored at each alloca location
    stored_types: std.AutoHashMapUnmanaged(ir.Value, ir.Type),
    // Register allocation info for current function (used by future register allocator)
    reg_alloc: RegAllocInfo,
    // Maximum outgoing stack args size for current function (for calls with >4 args)
    // This is computed at the start of function generation by scanning all calls
    outgoing_stack_args_size: i32,
    // Pending stack space for external calls with 5+ parameters
    pending_stack_space: u32 = 0,

    pub fn init(allocator: std.mem.Allocator, code: *std.ArrayListUnmanaged(u8), options: CodegenOptions) IrCodegen {
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
            .block_offsets = .{},
            .jump_patches = .{},
            .track_allocs = options.track_allocs,
            .tracking_data_patches = .{},
            .func_return_types = .{},
            .stored_types = .{},
            .reg_alloc = RegAllocInfo.init(),
            .outgoing_stack_args_size = 0,
            .pending_stack_space = 0,
        };
    }

    pub fn deinit(self: *IrCodegen) void {
        self.value_locations.deinit(self.allocator);
        self.value_types.deinit(self.allocator);
        self.func_offsets.deinit(self.allocator);
        self.call_patches.deinit(self.allocator);
        self.indirect_ptrs.deinit(self.allocator);
        self.external_patches.deinit(self.allocator);
        self.block_offsets.deinit(self.allocator);
        self.jump_patches.deinit(self.allocator);
        self.tracking_data_patches.deinit(self.allocator);
        self.func_return_types.deinit(self.allocator);
        self.stored_types.deinit(self.allocator);
        self.reg_alloc.deinit(self.allocator);
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
            .stack => |offset| {
                debug.codegen("    loadValue: val=%{d}, stack offset={d}, target={s}", .{ val, offset, @tagName(target) });
                switch (target) {
                    .rax => try self.enc.movRaxRbpOffset(offset),
                    .xmm => |xmm| if (xmm == .xmm0) try self.enc.movsdXmm0RbpOffset(offset) else try self.enc.movsdXmm1RbpOffset(offset),
                }
            },
            .register => |reg| switch (target) {
                .rax => {
                    if (reg == .rcx) try self.enc.movRaxRcx() else if (reg == .rdx) try self.enc.movRaxRdx() else if (reg != .rax) return error.UnsupportedRegister;
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

    fn loadToRcx(self: *IrCodegen, val: ir.Value) !void {
        const loc = self.value_locations.get(val) orelse return error.InvalidValue;
        switch (loc) {
            .stack => |offset| try self.enc.movRcxRbpOffsetQ(offset),
            .register => |reg| {
                if (reg == .rax) try self.enc.movRcxRax() else if (reg == .rdx) {
                    // mov rcx, rdx
                    try self.enc.emit(&.{ 0x48, 0x89, 0xD1 });
                } else if (reg != .rcx) return error.UnsupportedRegister;
            },
            .xmm => return error.CannotLoadXmmToGpr,
        }
    }

    fn loadToXmm(self: *IrCodegen, val: ir.Value, target: x86.Xmm) !void {
        try self.loadValue(val, .{ .xmm = target });
    }

    // ------------------------------------------------------------------------
    // Module / Function Generation
    // ------------------------------------------------------------------------

    pub fn generateModule(self: *IrCodegen, module: ir.Module) !void {
        // Collect function return types for cross-function calls
        for (module.functions.items) |*func| {
            try self.func_return_types.put(self.allocator, func.name, func.return_type);
        }

        // Generate _start wrapper if tracking is enabled
        if (self.track_allocs) {
            try self.generateStartWrapper();
        }

        // Generate main function first (entry point)
        if (module.getFunction("main")) |func| {
            try self.func_offsets.put(self.allocator, func.name, self.code.items.len);
            if (func.alias) |alias| {
                try self.func_offsets.put(self.allocator, alias, self.code.items.len);
            }
            try self.generateFunction(func);
        }

        // Generate other functions
        for (module.functions.items) |*func| {
            if (!std.mem.eql(u8, func.name, "main")) {
                try self.func_offsets.put(self.allocator, func.name, self.code.items.len);
                if (func.alias) |alias| {
                    try self.func_offsets.put(self.allocator, alias, self.code.items.len);
                }
                try self.generateFunction(func);
            }
        }

        // Generate __write_stdout runtime function (always needed for print)
        try self.generateWriteStdout();

        // Generate runtime conversion functions for string interpolation
        try self.generateRuntimeIntToString();
        try self.generateRuntimeFloatToString();
        try self.generateRuntimeBoolToString();

        // Generate tracking support functions if enabled
        if (self.track_allocs) {
            try self.generateTrackingFunctions();
            // Generate tracking data section at the end
            try self.generateTrackingData();
            // Patch tracking data references
            try self.patchTrackingDataRefs();
        }

        // Patch call sites
        try self.patchCalls();
    }

    /// Patch all RIP-relative references to tracking data
    fn patchTrackingDataRefs(self: *IrCodegen) !void {
        const data_base = self.tracking_data_offset orelse return;

        for (self.tracking_data_patches.items) |patch| {
            // Calculate where the data field is
            const field_offset = data_base + @intFromEnum(patch.field);
            // RIP at time of instruction is patch.code_offset + 4 (after the disp32)
            const rip = patch.code_offset + 4;
            // Calculate relative offset: target - rip
            const rel: i32 = @intCast(@as(i64, @intCast(field_offset)) - @as(i64, @intCast(rip)));

            // Write the displacement
            const bytes: [4]u8 = @bitCast(rel);
            self.code.items[patch.code_offset] = bytes[0];
            self.code.items[patch.code_offset + 1] = bytes[1];
            self.code.items[patch.code_offset + 2] = bytes[2];
            self.code.items[patch.code_offset + 3] = bytes[3];
        }
    }

    /// Generate tracking data section (counters and table)
    /// Layout at tracking_data_offset:
    ///   +0:  next_alloc_id (i64)
    ///   +8:  total_allocated (i64)
    ///   +16: total_freed (i64)
    ///   +24: tracking_table_count (i32)
    ///   +32: tracking_table[256] - each entry is {ptr: i64, size: i64, id: i64} = 24 bytes
    fn generateTrackingData(self: *IrCodegen) !void {
        // Align to 8 bytes
        while (self.code.items.len % 8 != 0) {
            try self.code.append(self.allocator, 0);
        }
        self.tracking_data_offset = self.code.items.len;

        // next_alloc_id (8 bytes) - starts at 0
        try self.code.appendNTimes(self.allocator, 0, 8);
        // total_allocated (8 bytes)
        try self.code.appendNTimes(self.allocator, 0, 8);
        // total_freed (8 bytes)
        try self.code.appendNTimes(self.allocator, 0, 8);
        // tracking_table_count (8 bytes, though only using 4)
        try self.code.appendNTimes(self.allocator, 0, 8);
        // tracking_table - 256 entries * 24 bytes each = 6144 bytes
        try self.code.appendNTimes(self.allocator, 0, 256 * 24);
    }

    /// Emit: mov rax, [rip+disp32] - load tracking data field to RAX
    /// Adds a patch entry to be resolved later
    fn emitLoadTrackingField(self: *IrCodegen, field: TrackingDataField) !void {
        // mov rax, [rip+disp32]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x05 }); // REX.W mov rax, [rip+disp32]
        const patch_offset = self.code.items.len;
        try self.enc.emitI32(0); // placeholder displacement
        try self.tracking_data_patches.append(self.allocator, .{
            .code_offset = patch_offset,
            .field = field,
        });
    }

    /// Emit: mov [rip+disp32], rax - store RAX to tracking data field
    fn emitStoreTrackingField(self: *IrCodegen, field: TrackingDataField) !void {
        // mov [rip+disp32], rax
        try self.enc.emit(&.{ 0x48, 0x89, 0x05 }); // REX.W mov [rip+disp32], rax
        const patch_offset = self.code.items.len;
        try self.enc.emitI32(0); // placeholder displacement
        try self.tracking_data_patches.append(self.allocator, .{
            .code_offset = patch_offset,
            .field = field,
        });
    }

    /// Emit: lea rax, [rip+disp32] - load address of tracking data field to RAX
    fn emitLeaTrackingField(self: *IrCodegen, field: TrackingDataField) !void {
        // lea rax, [rip+disp32]
        try self.enc.emit(&.{ 0x48, 0x8D, 0x05 }); // REX.W lea rax, [rip+disp32]
        const patch_offset = self.code.items.len;
        try self.enc.emitI32(0); // placeholder displacement
        try self.tracking_data_patches.append(self.allocator, .{
            .code_offset = patch_offset,
            .field = field,
        });
    }

    /// Emit: add [rip+disp32], reg - add register to tracking data field
    fn emitAddToTrackingField(self: *IrCodegen, field: TrackingDataField, reg: x86.Gpr) !void {
        // For simplicity, use: load to temp, add, store back
        try self.enc.pushRax(); // save rax

        // Load field to rax
        try self.emitLoadTrackingField(field);

        // add rax, reg
        switch (reg) {
            .rcx => try self.enc.emit(&.{ 0x48, 0x01, 0xC8 }), // add rax, rcx
            .rdx => try self.enc.emit(&.{ 0x48, 0x01, 0xD0 }), // add rax, rdx
            .r12 => try self.enc.emit(&.{ 0x4C, 0x01, 0xE0 }), // add rax, r12
            .r13 => try self.enc.emit(&.{ 0x4C, 0x01, 0xE8 }), // add rax, r13
            .r14 => try self.enc.emit(&.{ 0x4C, 0x01, 0xF0 }), // add rax, r14
            else => unreachable,
        }

        // Store back
        try self.emitStoreTrackingField(field);

        try self.enc.popRax(); // restore rax
    }

    /// Generate _start wrapper that enables tracking, calls main, prints summary
    fn generateStartWrapper(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "_start", self.code.items.len);

        // Prologue
        try self.enc.prologue(64);

        // Call __enable_alloc_tracking
        try self.emitInternalCall("__enable_alloc_tracking");

        // Call main
        try self.emitInternalCall("main");

        // Save main's return value to R12 (callee-saved)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC4 }); // mov r12, rax

        // Call __print_alloc_summary
        try self.emitInternalCall("__print_alloc_summary");

        // Call ExitProcess with main's return value
        try self.enc.emit(&.{ 0x4C, 0x89, 0xE1 }); // mov rcx, r12
        try self.emitExternalCall("kernel32.dll", "ExitProcess");

        // Epilogue (not reached but for completeness)
        try self.enc.epilogue();
    }

    /// Generate tracking support functions
    fn generateTrackingFunctions(self: *IrCodegen) !void {
        // __enable_alloc_tracking - sets tracking enabled flag
        try self.generateEnableAllocTracking();

        // __print_alloc_summary - prints allocation statistics
        try self.generatePrintAllocSummary();

        // __track_alloc - tracks an allocation
        try self.generateTrackAlloc();

        // __track_free - tracks a free
        try self.generateTrackFree();

        // __track_realloc - tracks a reallocation
        try self.generateTrackRealloc();
    }

    /// Generate __enable_alloc_tracking function
    fn generateEnableAllocTracking(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__enable_alloc_tracking", self.code.items.len);

        // Simple function that just returns for now (tracking happens inline)
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x89, 0xEC }); // mov rsp, rbp
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __print_alloc_summary function - prints alloc stats
    /// Stack layout:
    ///   [rbp-56] = total_allocated
    ///   [rbp-64] = total_freed
    ///   [rbp-72] = leaked (allocated - freed)
    fn generatePrintAllocSummary(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__print_alloc_summary", self.code.items.len);

        // Prologue - save callee-saved registers we'll use
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x40 }); // sub rsp, 64 (for buffer + alignment)

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Load tracking values to stack
        try self.emitLoadTrackingField(.total_allocated);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xC8 }); // mov [rbp-56], rax
        try self.emitLoadTrackingField(.total_freed);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xC0 }); // mov [rbp-64], rax

        // Calculate leaked = allocated - freed, store in [rbp-72]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xC8 }); // mov rcx, [rbp-56] (allocated)
        try self.enc.emit(&.{ 0x48, 0x2B, 0x4D, 0xC0 }); // sub rcx, [rbp-64] (freed)
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xB8 }); // mov [rbp-72], rcx (leaked)

        // Print "\n=== ALLOC STATS ===\nAllocated: "
        try self.printStaticString("\n=== ALLOC STATS ===\nAllocated: ");

        // Print total_allocated from stack
        try self.printNumberFromStack(-56);

        // Print " bytes\nFreed:     "
        try self.printStaticString(" bytes\nFreed:     ");

        // Print total_freed from stack
        try self.printNumberFromStack(-64);

        // Print " bytes\nLeaked:    "
        try self.printStaticString(" bytes\nLeaked:    ");

        // Print leaked from stack
        try self.printNumberFromStack(-72);

        // Print " bytes\n"
        try self.printStaticString(" bytes\n");

        // Epilogue
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x40 }); // add rsp, 64
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Helper to print a constant string via WriteFile
    fn printConstString(self: *IrCodegen, str: []const u8) !void {
        // Get stdout handle: call GetStdHandle(-11)
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");

        // Save handle to R14 (use R14 instead of R12 to not clobber _start's saved return value)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC6 }); // mov r14, rax

        // Embed string data after a jump over it
        const string_len: u32 = @intCast(str.len);
        const jmp_offset = try self.enc.jmpRel32();

        // Record string position for RIP-relative addressing
        const string_pos = self.code.items.len;
        try self.code.appendSlice(self.allocator, str);

        // Patch jump to skip over string
        const after_string = self.code.items.len;
        const rel: i32 = @intCast(after_string - jmp_offset - 4);
        self.code.items[jmp_offset] = @bitCast(@as(i8, @intCast(rel & 0xFF)));
        self.code.items[jmp_offset + 1] = @bitCast(@as(i8, @intCast((rel >> 8) & 0xFF)));
        self.code.items[jmp_offset + 2] = @bitCast(@as(i8, @intCast((rel >> 16) & 0xFF)));
        self.code.items[jmp_offset + 3] = @bitCast(@as(i8, @intCast((rel >> 24) & 0xFF)));

        // LEA RCX, [RIP - offset_to_string] ; buffer pointer
        const rip_offset: i32 = -@as(i32, @intCast(after_string - string_pos + 7)); // +7 for LEA instruction size
        try self.enc.emit(&.{ 0x48, 0x8D, 0x0D }); // lea rcx, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // Save buffer ptr to R13
        try self.enc.emit(&.{ 0x49, 0x89, 0xCD }); // mov r13, rcx

        // WriteFile(hFile=R14, lpBuffer=R13, nBytes=len, lpWritten=NULL, lpOverlapped=NULL)
        try self.beginExternalCall(1);
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF1 }); // mov rcx, r14 (handle)
        try self.enc.emit(&.{ 0x4C, 0x89, 0xEA }); // mov rdx, r13 (buffer)
        try self.enc.emit(&.{ 0x41, 0xB8 }); // mov r8d, imm32 (length)
        try self.enc.emitI32(@intCast(string_len));
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (lpNumberOfBytesWritten = NULL)
        try self.emitStackParam(0, 0); // lpOverlapped = NULL
        try self.emitExternalCallAfterSetup("kernel32.dll", "WriteFile");
        try self.endExternalCall();
    }

    /// Generate __track_alloc - tracks an allocation
    /// Input: RCX = ptr, RDX = size, R8 = tag ptr, R9 = tag len
    /// Stack layout:
    ///   [rbp-48] = tag_len (for printTagFromR12)
    ///   [rbp-56] = ptr
    ///   [rbp-64] = size
    ///   [rbp-72] = alloc_id
    fn generateTrackAlloc(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_alloc", self.code.items.len);

        // Prologue - save callee-saved registers
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x40 }); // sub rsp, 64

        // Save inputs to stack
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xC8 }); // mov [rbp-56], rcx (ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xC0 }); // mov [rbp-64], rdx (size)
        try self.enc.emit(&.{ 0x4D, 0x89, 0xC4 }); // mov r12, r8 (tag ptr)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x4D, 0xD0 }); // mov [rbp-48], r9 (tag len)

        // Increment alloc ID and save to stack
        try self.emitLoadTrackingField(.next_alloc_id);
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax
        try self.emitStoreTrackingField(.next_alloc_id);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xB8 }); // mov [rbp-72], rax (alloc ID)

        // Add size to total_allocated
        try self.emitLoadTrackingField(.total_allocated);
        try self.enc.emit(&.{ 0x48, 0x03, 0x45, 0xC0 }); // add rax, [rbp-64] (size)
        try self.emitStoreTrackingField(.total_allocated);

        // Store entry in tracking table: {ptr, size, id} at table_base + count*24
        // Get current table count
        try self.emitLoadTrackingField(.table_count);
        try self.enc.emit(&.{ 0x48, 0x89, 0xC1 }); // mov rcx, rax (count)

        // Calculate offset = count * 24
        try self.enc.emit(&.{ 0x48, 0x6B, 0xC9, 0x18 }); // imul rcx, rcx, 24

        // Load table base address to RAX
        try self.emitLeaTrackingField(.table_base);
        // RAX = table_base, RCX = offset
        try self.enc.emit(&.{ 0x48, 0x01, 0xC8 }); // add rax, rcx (RAX = entry address)

        // Store ptr at [rax+0]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xC8 }); // mov rcx, [rbp-56] (ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x08 }); // mov [rax], rcx

        // Store size at [rax+8]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xC0 }); // mov rcx, [rbp-64] (size)
        try self.enc.emit(&.{ 0x48, 0x89, 0x48, 0x08 }); // mov [rax+8], rcx

        // Store id at [rax+16]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xB8 }); // mov rcx, [rbp-72] (alloc ID)
        try self.enc.emit(&.{ 0x48, 0x89, 0x48, 0x10 }); // mov [rax+16], rcx

        // Increment table count
        try self.emitLoadTrackingField(.table_count);
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax
        try self.emitStoreTrackingField(.table_count);

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Print "ALLOC #"
        try self.printStaticString("ALLOC #");

        // Print alloc ID from stack
        try self.printNumberFromStack(-72);

        // Print ": "
        try self.printStaticString(": ");

        // Print size from stack
        try self.printNumberFromStack(-64);

        // Print " bytes ("
        try self.printStaticString(" bytes (");

        // Print tag from R12 with length from stack
        try self.printTagFromR12();

        // Print ")\n"
        try self.printStaticString(")\n");

        // Epilogue
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x40 }); // add rsp, 64
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __track_free - tracks a free
    /// Input: RCX = ptr, RDX = size, R8 = tag ptr, R9 = tag len
    /// Stack layout:
    ///   [rbp-48] = tag_len (for printTagFromR12)
    ///   [rbp-56] = ptr
    ///   [rbp-64] = size
    ///   [rbp-72] = alloc_id (saved for printing)
    fn generateTrackFree(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_free", self.code.items.len);

        // Prologue - save callee-saved registers
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x40 }); // sub rsp, 64

        // Save inputs to stack and registers
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xC8 }); // mov [rbp-56], rcx (ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xC0 }); // mov [rbp-64], rdx (size)
        try self.enc.emit(&.{ 0x4D, 0x89, 0xC4 }); // mov r12, r8 (tag ptr)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x4D, 0xD0 }); // mov [rbp-48], r9 (tag len)

        // Add size to total_freed
        try self.emitLoadTrackingField(.total_freed);
        try self.enc.emit(&.{ 0x48, 0x03, 0x45, 0xC0 }); // add rax, [rbp-64] (size)
        try self.emitStoreTrackingField(.total_freed);

        // Look up ptr in tracking table using helper
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xC8 }); // mov rax, [rbp-56] (ptr)
        try self.emitTableLookup();
        // R14 = alloc ID, RDX = entry address (or 0 if not found)

        // Clear entry ptr to prevent reuse (if found)
        try self.enc.emit(&.{ 0x48, 0x85, 0xD2 }); // test rdx, rdx
        const skip_clear = try self.enc.jeRel32();
        try self.enc.emit(&.{ 0x48, 0xC7, 0x02, 0x00, 0x00, 0x00, 0x00 }); // mov qword [rdx], 0
        const after_clear = self.code.items.len;
        const skip_rel: i32 = @intCast(@as(i64, @intCast(after_clear)) - @as(i64, @intCast(skip_clear)) - 4);
        self.code.items[skip_clear] = @bitCast(@as(i8, @intCast(skip_rel & 0xFF)));
        self.code.items[skip_clear + 1] = @bitCast(@as(i8, @intCast((skip_rel >> 8) & 0xFF)));
        self.code.items[skip_clear + 2] = @bitCast(@as(i8, @intCast((skip_rel >> 16) & 0xFF)));
        self.code.items[skip_clear + 3] = @bitCast(@as(i8, @intCast((skip_rel >> 24) & 0xFF)));

        // Save alloc_id to stack for printing
        try self.enc.emit(&.{ 0x4C, 0x89, 0x75, 0xB8 }); // mov [rbp-72], r14

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Print "FREE #"
        try self.printStaticString("FREE #");

        // Print alloc ID from stack
        try self.printNumberFromStack(-72);

        // Print ": "
        try self.printStaticString(": ");

        // Print size from stack
        try self.printNumberFromStack(-64);

        // Print " bytes ("
        try self.printStaticString(" bytes (");

        // Print tag from R12 with length from stack
        try self.printTagFromR12();

        // Print ")\n"
        try self.printStaticString(")\n");

        // Epilogue
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x40 }); // add rsp, 64
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __track_realloc - tracks a reallocation
    /// Input: RCX = old_ptr, RDX = old_size, R8 = new_ptr, R9 = new_size
    ///        [rsp+40] = tag_ptr, [rsp+48] = tag_len (after shadow space)
    /// Stack layout:
    ///   [rbp-48] = tag_len (for printTagFromR12)
    ///   [rbp-56] = old_ptr
    ///   [rbp-64] = old_size
    ///   [rbp-72] = new_ptr
    ///   [rbp-80] = new_size  <- KEY FIX: save new_size to stack
    ///   [rbp-88] = tag_ptr
    ///   [rbp-96] = tag_len (original)
    ///   [rbp-104] = stdout_handle
    ///   [rbp-112] = alloc_id (R14 value saved for later)
    fn generateTrackRealloc(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_realloc", self.code.items.len);

        // Prologue - save callee-saved registers
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x60 }); // sub rsp, 96

        // Save all inputs to stack immediately
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xC8 }); // mov [rbp-56], rcx (old_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xC0 }); // mov [rbp-64], rdx (old_size)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x45, 0xB8 }); // mov [rbp-72], r8 (new_ptr)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x4D, 0xB0 }); // mov [rbp-80], r9 (new_size) <- THE FIX
        // Copy tag_ptr from [rbp+48] to [rbp-88]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0x30 }); // mov rax, [rbp+48]
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xA8 }); // mov [rbp-88], rax
        // Copy tag_len from [rbp+56] to [rbp-96]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0x38 }); // mov rax, [rbp+56]
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xA0 }); // mov [rbp-96], rax

        // Look up old_ptr in tracking table using helper
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xC8 }); // mov rax, [rbp-56] (old_ptr)
        try self.emitTableLookup();
        // R14 = alloc ID, RDX = entry address (or 0 if not found)

        // Save alloc_id to stack for later printing
        try self.enc.emit(&.{ 0x4C, 0x89, 0x75, 0x90 }); // mov [rbp-112], r14

        // Update the table entry with new_ptr and new_size (keep same ID)
        // RDX = entry address from lookup
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xB8 }); // mov rax, [rbp-72] (new_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x02 }); // mov [rdx], rax (new_ptr)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xB0 }); // mov rax, [rbp-80] (new_size)
        try self.enc.emit(&.{ 0x48, 0x89, 0x42, 0x08 }); // mov [rdx+8], rax (new_size)

        // Update allocation stats:
        // - Add new_size to total_allocated
        // - Add old_size to total_freed
        try self.emitLoadTrackingField(.total_allocated);
        try self.enc.emit(&.{ 0x48, 0x03, 0x45, 0xB0 }); // add rax, [rbp-80] (new_size)
        try self.emitStoreTrackingField(.total_allocated);

        try self.emitLoadTrackingField(.total_freed);
        try self.enc.emit(&.{ 0x48, 0x03, 0x45, 0xC0 }); // add rax, [rbp-64] (old_size)
        try self.emitStoreTrackingField(.total_freed);

        // Get stdout handle and save to stack
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0x98 }); // mov [rbp-104], rax (stdout)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax

        // Print "REALLOC #"
        try self.printStaticString("REALLOC #");

        // Print alloc ID from stack
        try self.printNumberFromStack(-112);

        // Print ": "
        try self.printStaticString(": ");

        // Print old_size from stack
        try self.printNumberFromStack(-64);

        // Print " -> "
        try self.printStaticString(" -> ");

        // Print new_size from stack (THE FIX - no more second table search!)
        try self.printNumberFromStack(-80);

        // Print " bytes ("
        try self.printStaticString(" bytes (");

        // Print tag - load tag_ptr to R12 and tag_len to [rbp-48] for printTagFromR12
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x65, 0xA8 }); // mov r12, [rbp-88] (tag_ptr)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xA0 }); // mov rax, [rbp-96] (tag_len)
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xD0 }); // mov [rbp-48], rax (for printTagFromR12)
        try self.printTagFromR12();

        // Print ")\n"
        try self.printStaticString(")\n");

        // Epilogue
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x60 }); // add rsp, 96
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __write_stdout(buffer, length) -> bytes_written
    /// Writes buffer to stdout using WriteFile
    /// Parameters: RCX = buffer pointer, RDX = length
    /// Returns: RAX = bytes written (same as length on success)
    fn generateWriteStdout(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__write_stdout", self.code.items.len);

        // Simplified version - just call GetStdHandle and WriteFile
        // Parameters: RCX = buffer pointer, RDX = length

        // Prologue
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x40 }); // sub rsp, 64

        // Save args to stack: [rbp-16] = buffer, [rbp-8] = length
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx (buffer)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xF8 }); // mov [rbp-8], rdx (length)

        // Initialize bytes_written to 0 at [rbp-24]
        try self.enc.emit(&.{ 0x48, 0xC7, 0x45, 0xE8, 0x00, 0x00, 0x00, 0x00 }); // mov qword [rbp-24], 0

        // GetStdHandle(-11) to get stdout
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE = -11
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        // RAX now has stdout handle, save to [rbp-32]
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xE0 }); // mov [rbp-32], rax

        // DEBUG: Return the handle to see if GetStdHandle worked
        // A valid handle should be a non-zero value
        // For debugging: skip WriteFile and just return handle
        // Uncomment below to skip WriteFile and return handle
        // try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xE0 }); // mov rax, [rbp-32] (return handle)
        // For now, continue with WriteFile call

        // WriteFile(handle, buffer, length, &bytes_written, NULL)
        // WriteFile(handle, buffer, length, &bytes_written, lpOverlapped=NULL)
        try self.beginExternalCall(1); // 5 params = 4 reg + 1 stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xE0 }); // mov rcx, [rbp-32] (handle)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x55, 0xF0 }); // mov rdx, [rbp-16] (buffer)
        try self.enc.emit(&.{ 0x44, 0x8B, 0x45, 0xF8 }); // mov r8d, [rbp-8] (length as DWORD)
        try self.enc.emit(&.{ 0x4C, 0x8D, 0x4D, 0xE8 }); // lea r9, [rbp-24] (&bytes_written)
        try self.emitStackParam(0, 0); // lpOverlapped = NULL
        try self.emitExternalCallAfterSetup("kernel32.dll", "WriteFile");
        try self.endExternalCall();

        // Return bytes_written
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xE8 }); // mov rax, [rbp-24]

        // Epilogue
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x40 }); // add rsp, 64
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __runtime_int_to_string(buffer, value) -> length
    /// Converts an i64 integer to its decimal string representation
    /// Parameters: RCX = buffer pointer, RDX = i64 value
    /// Returns: RAX = length of string written
    fn generateRuntimeIntToString(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__runtime_int_to_string", self.code.items.len);

        // Prologue
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x30 }); // sub rsp, 48

        // Save args: [rbp-8] = buffer, [rbp-16] = value
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF8 }); // mov [rbp-8], rcx (buffer)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xF0 }); // mov [rbp-16], rdx (value)

        // Handle negative numbers
        // if value < 0: write '-', negate value
        try self.enc.emit(&.{ 0x48, 0x85, 0xD2 }); // test rdx, rdx
        const jns_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x79, 0x00 }); // jns skip_neg (patch later)
        const neg_start = self.code.items.len;

        // Write '-' to buffer
        try self.enc.emit(&.{ 0xC6, 0x01, 0x2D }); // mov byte [rcx], '-'
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF8 }); // mov [rbp-8], rcx (update buffer ptr)
        // Negate value
        try self.enc.emit(&.{ 0x48, 0xF7, 0xDA }); // neg rdx
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xF0 }); // mov [rbp-16], rdx

        // Patch jns
        const after_neg = self.code.items.len;
        self.code.items[jns_offset + 1] = @intCast(after_neg - neg_start);

        // Now convert abs(value) to string
        // We write digits in reverse, then reverse them
        // Use [rbp-24] as temp digit count

        // Save original buffer position for later reversal
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xF8 }); // mov rcx, [rbp-8] (buffer)
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xE8 }); // mov [rbp-24], rcx (save start)

        // Special case: value == 0
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xF0 }); // mov rax, [rbp-16] (value)
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax
        const jnz_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x75, 0x00 }); // jnz not_zero (patch later)
        const zero_start = self.code.items.len;

        // Value is zero, write '0' and return 1
        try self.enc.emit(&.{ 0xC6, 0x01, 0x30 }); // mov byte [rcx], '0'
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC0, 0x01, 0x00, 0x00, 0x00 }); // mov rax, 1
        // Jump directly to epilogue
        const jmp_to_epilogue = self.code.items.len;
        try self.enc.emit(&.{ 0xE9, 0x00, 0x00, 0x00, 0x00 }); // jmp epilogue (patch later)

        // Patch jnz
        const not_zero = self.code.items.len;
        self.code.items[jnz_offset + 1] = @intCast(not_zero - zero_start);

        // Loop: extract digits
        // rax = value, rcx = buffer ptr
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xF0 }); // mov rax, [rbp-16] (value)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xF8 }); // mov rcx, [rbp-8] (buffer)

        const loop_start = self.code.items.len;
        // while (rax != 0)
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax
        const jz_end_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x74, 0x00 }); // jz end_loop (patch later)
        const jz_start = self.code.items.len;

        // digit = rax % 10
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC2, 0x0A, 0x00, 0x00, 0x00 }); // mov rdx, 10
        try self.enc.emit(&.{ 0x49, 0x89, 0xC0 }); // mov r8, rax (save dividend)
        try self.enc.emit(&.{ 0x48, 0x31, 0xD2 }); // xor rdx, rdx (clear high bits for div)
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC3, 0x0A, 0x00, 0x00, 0x00 }); // mov rbx, 10
        try self.enc.emit(&.{ 0x48, 0xF7, 0xF3 }); // div rbx (rax = quotient, rdx = remainder)

        // Write digit: '0' + remainder
        try self.enc.emit(&.{ 0x80, 0xC2, 0x30 }); // add dl, '0'
        try self.enc.emit(&.{ 0x88, 0x11 }); // mov [rcx], dl
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx

        // Loop back
        const loop_back: i8 = @intCast(@as(i64, @intCast(loop_start)) - @as(i64, @intCast(self.code.items.len)) - 2);
        try self.enc.emit(&.{ 0xEB, @bitCast(loop_back) }); // jmp loop_start

        // End loop
        const end_loop = self.code.items.len;
        self.code.items[jz_end_offset + 1] = @intCast(end_loop - jz_start);

        // Now reverse the digits
        // rcx = end of string, [rbp-24] = start of digits
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF8 }); // mov [rbp-8], rcx (save end)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xE8 }); // mov rsi, [rbp-24] (start)
        try self.enc.emit(&.{ 0x48, 0x8D, 0x79, 0xFF }); // lea rdi, [rcx-1] (end-1)

        // while (rsi < rdi) swap
        const rev_loop = self.code.items.len;
        try self.enc.emit(&.{ 0x48, 0x39, 0xFE }); // cmp rsi, rdi
        const jae_end_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x73, 0x00 }); // jae end_reverse (patch later)
        const jae_start = self.code.items.len;

        // Swap bytes
        try self.enc.emit(&.{ 0x8A, 0x06 }); // mov al, [rsi]
        try self.enc.emit(&.{ 0x8A, 0x1F }); // mov bl, [rdi]
        try self.enc.emit(&.{ 0x88, 0x1E }); // mov [rsi], bl
        try self.enc.emit(&.{ 0x88, 0x07 }); // mov [rdi], al
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC6 }); // inc rsi
        try self.enc.emit(&.{ 0x48, 0xFF, 0xCF }); // dec rdi

        const rev_back: i8 = @intCast(@as(i64, @intCast(rev_loop)) - @as(i64, @intCast(self.code.items.len)) - 2);
        try self.enc.emit(&.{ 0xEB, @bitCast(rev_back) }); // jmp rev_loop

        const end_reverse = self.code.items.len;
        self.code.items[jae_end_offset + 1] = @intCast(end_reverse - jae_start);

        // Calculate length for non-zero case
        // [rbp-8] = end, [rbp-24] = start of digits (after any '-')
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xF8 }); // mov rax, [rbp-8]
        try self.enc.emit(&.{ 0x48, 0x2B, 0x45, 0xE8 }); // sub rax, [rbp-24] (end - digit_start)
        // Check if original value was negative (check byte before digit start)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xE8 }); // mov rcx, [rbp-24]
        try self.enc.emit(&.{ 0x80, 0x79, 0xFF, 0x2D }); // cmp byte [rcx-1], '-'
        try self.enc.emit(&.{ 0x75, 0x03 }); // jne skip_inc
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax

        // Epilogue (zero case jumps here with rax already set to 1)
        const epilogue_start = self.code.items.len;
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x30 }); // add rsp, 48
        try self.enc.popRbp();
        try self.enc.ret();

        // Patch jump from zero case to epilogue
        const jmp_rel: i32 = @intCast(@as(i64, @intCast(epilogue_start)) - @as(i64, @intCast(jmp_to_epilogue)) - 5);
        self.code.items[jmp_to_epilogue + 1] = @bitCast(@as(i8, @intCast(jmp_rel & 0xFF)));
        self.code.items[jmp_to_epilogue + 2] = @bitCast(@as(i8, @intCast((jmp_rel >> 8) & 0xFF)));
        self.code.items[jmp_to_epilogue + 3] = @bitCast(@as(i8, @intCast((jmp_rel >> 16) & 0xFF)));
        self.code.items[jmp_to_epilogue + 4] = @bitCast(@as(i8, @intCast((jmp_rel >> 24) & 0xFF)));
    }

    /// Generate __runtime_float_to_string(buffer, value) -> length
    /// Converts an f64 float to its string representation
    /// Native implementation - no C runtime dependency
    /// Format: up to 6 decimal places, trailing zeros removed (but keeps at least one decimal)
    fn generateRuntimeFloatToString(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__runtime_float_to_string", self.code.items.len);

        // Parameters: RCX = buffer, XMM1 = float value (Windows x64 calling convention)
        // Returns: RAX = length of string written
        //
        // Stack layout:
        //   [rbp-8]  = original buffer pointer
        //   [rbp-16] = current write position
        //   [rbp-24] = integer part digit start (for reversal)
        //   [rbp-32] = first fractional digit position (for trimming)

        // Prologue - save callee-saved registers we'll use
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x40 }); // sub rsp, 64
        try self.enc.emit(&.{0x53}); // push rbx
        try self.enc.emit(&.{0x56}); // push rsi
        try self.enc.emit(&.{0x57}); // push rdi

        // Save original buffer pointer
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF8 }); // mov [rbp-8], rcx
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx (current pos = buffer start)

        // ========== SECTION 1: Check for special values (NaN, Inf) ==========
        // Extract float bits to RAX
        try self.enc.emit(&.{ 0x66, 0x48, 0x0F, 0x7E, 0xC8 }); // movq rax, xmm1

        // Check exponent: if (exponent == 0x7FF) then special value
        try self.enc.emit(&.{ 0x48, 0x89, 0xC2 }); // mov rdx, rax
        try self.enc.emit(&.{ 0x48, 0xC1, 0xEA, 0x34 }); // shr rdx, 52 (get exponent + sign in bits 0-11)
        try self.enc.emit(&.{ 0x81, 0xE2, 0xFF, 0x07, 0x00, 0x00 }); // and edx, 0x7FF (mask exponent)
        try self.enc.emit(&.{ 0x81, 0xFA, 0xFF, 0x07, 0x00, 0x00 }); // cmp edx, 0x7FF
        const jne_not_special = self.code.items.len;
        try self.enc.emit(&.{ 0x0F, 0x85, 0x00, 0x00, 0x00, 0x00 }); // jne not_special (patch later)

        // It's a special value - check mantissa for NaN vs Inf
        // Mantissa is bits 0-51 of rax
        try self.enc.emit(&.{ 0x48, 0xB9 }); // mov rcx, imm64 (mantissa mask)
        try self.enc.emitI64(0x000FFFFFFFFFFFFF);
        try self.enc.emit(&.{ 0x48, 0x21, 0xC8 }); // and rax, rcx (isolate mantissa)
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax

        // Reload buffer pointer for writing
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xF0 }); // mov rcx, [rbp-16]

        const jz_is_inf = self.code.items.len;
        try self.enc.emit(&.{ 0x74, 0x00 }); // jz is_infinity (patch later)

        // ===== Write "nan" =====
        try self.enc.emit(&.{ 0xC6, 0x01, 0x6E }); // mov byte [rcx], 'n'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x01, 0x61 }); // mov byte [rcx+1], 'a'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x02, 0x6E }); // mov byte [rcx+2], 'n'
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC0, 0x03, 0x00, 0x00, 0x00 }); // mov rax, 3
        const jmp_to_epilogue_nan = self.code.items.len;
        try self.enc.emit(&.{ 0xE9, 0x00, 0x00, 0x00, 0x00 }); // jmp epilogue

        // ===== is_infinity: check sign and write "inf" or "-inf" =====
        const is_inf_pos = self.code.items.len;
        self.code.items[jz_is_inf + 1] = @intCast(is_inf_pos - (jz_is_inf + 2));

        // Get original bits again to check sign
        try self.enc.emit(&.{ 0x66, 0x48, 0x0F, 0x7E, 0xC8 }); // movq rax, xmm1
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax (sign bit in bit 63)
        const jns_pos_inf = self.code.items.len;
        try self.enc.emit(&.{ 0x79, 0x00 }); // jns positive_inf

        // Negative infinity: write "-inf"
        try self.enc.emit(&.{ 0xC6, 0x01, 0x2D }); // mov byte [rcx], '-'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x01, 0x69 }); // mov byte [rcx+1], 'i'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x02, 0x6E }); // mov byte [rcx+2], 'n'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x03, 0x66 }); // mov byte [rcx+3], 'f'
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC0, 0x04, 0x00, 0x00, 0x00 }); // mov rax, 4
        const jmp_to_epilogue_neginf = self.code.items.len;
        try self.enc.emit(&.{ 0xE9, 0x00, 0x00, 0x00, 0x00 }); // jmp epilogue

        // Positive infinity: write "inf"
        const pos_inf_pos = self.code.items.len;
        self.code.items[jns_pos_inf + 1] = @intCast(pos_inf_pos - (jns_pos_inf + 2));

        try self.enc.emit(&.{ 0xC6, 0x01, 0x69 }); // mov byte [rcx], 'i'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x01, 0x6E }); // mov byte [rcx+1], 'n'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x02, 0x66 }); // mov byte [rcx+2], 'f'
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC0, 0x03, 0x00, 0x00, 0x00 }); // mov rax, 3
        const jmp_to_epilogue_posinf = self.code.items.len;
        try self.enc.emit(&.{ 0xE9, 0x00, 0x00, 0x00, 0x00 }); // jmp epilogue

        // ========== not_special: Normal float processing ==========
        const not_special_pos = self.code.items.len;
        // Patch the jne not_special
        const jne_rel: i32 = @intCast(@as(i64, @intCast(not_special_pos)) - @as(i64, @intCast(jne_not_special)) - 6);
        self.code.items[jne_not_special + 2] = @truncate(@as(u32, @bitCast(jne_rel)));
        self.code.items[jne_not_special + 3] = @truncate(@as(u32, @bitCast(jne_rel)) >> 8);
        self.code.items[jne_not_special + 4] = @truncate(@as(u32, @bitCast(jne_rel)) >> 16);
        self.code.items[jne_not_special + 5] = @truncate(@as(u32, @bitCast(jne_rel)) >> 24);

        // ========== SECTION 2: Handle sign ==========
        // Check if negative (bit 63 set)
        try self.enc.emit(&.{ 0x66, 0x48, 0x0F, 0x7E, 0xC8 }); // movq rax, xmm1
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax
        const jns_not_neg = self.code.items.len;
        try self.enc.emit(&.{ 0x79, 0x00 }); // jns not_negative

        // Write '-' and take absolute value
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xF0 }); // mov rcx, [rbp-16]
        try self.enc.emit(&.{ 0xC6, 0x01, 0x2D }); // mov byte [rcx], '-'
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx

        // Clear sign bit in XMM1: andpd xmm1, [sign_mask]
        // Use movabs to load mask, then clear sign bit via integer ops
        try self.enc.emit(&.{ 0x48, 0xB8 }); // mov rax, imm64 (sign mask: clear bit 63)
        try self.enc.emitI64(0x7FFFFFFFFFFFFFFF);
        try self.enc.emit(&.{ 0x66, 0x48, 0x0F, 0x7E, 0xCA }); // movq rdx, xmm1
        try self.enc.emit(&.{ 0x48, 0x21, 0xC2 }); // and rdx, rax
        try self.enc.emit(&.{ 0x66, 0x48, 0x0F, 0x6E, 0xCA }); // movq xmm1, rdx

        const not_neg_pos = self.code.items.len;
        self.code.items[jns_not_neg + 1] = @intCast(not_neg_pos - (jns_not_neg + 2));

        // ========== SECTION 3: Handle zero case ==========
        // Compare xmm1 with 0.0
        try self.enc.emit(&.{ 0x66, 0x0F, 0xEF, 0xC0 }); // pxor xmm0, xmm0 (xmm0 = 0.0)
        try self.enc.emit(&.{ 0x66, 0x0F, 0x2E, 0xC8 }); // ucomisd xmm1, xmm0
        const jne_not_zero = self.code.items.len;
        try self.enc.emit(&.{ 0x75, 0x00 }); // jne not_zero

        // Value is zero: write "0.0"
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xF0 }); // mov rcx, [rbp-16]
        try self.enc.emit(&.{ 0xC6, 0x01, 0x30 }); // mov byte [rcx], '0'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x01, 0x2E }); // mov byte [rcx+1], '.'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x02, 0x30 }); // mov byte [rcx+2], '0'
        try self.enc.emit(&.{ 0x48, 0x83, 0xC1, 0x03 }); // add rcx, 3
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx
        // Calculate length
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xF0 }); // mov rax, [rbp-16]
        try self.enc.emit(&.{ 0x48, 0x2B, 0x45, 0xF8 }); // sub rax, [rbp-8]
        const jmp_to_epilogue_zero = self.code.items.len;
        try self.enc.emit(&.{ 0xE9, 0x00, 0x00, 0x00, 0x00 }); // jmp epilogue

        const not_zero_pos = self.code.items.len;
        self.code.items[jne_not_zero + 1] = @intCast(not_zero_pos - (jne_not_zero + 2));

        // ========== SECTION 4: Extract integer part ==========
        // cvttsd2si rax, xmm1 (truncate to integer)
        try self.enc.emit(&.{ 0xF2, 0x48, 0x0F, 0x2C, 0xC1 }); // cvttsd2si rax, xmm1
        try self.enc.emit(&.{ 0x48, 0x89, 0xC3 }); // mov rbx, rax (save integer part)

        // Extract fractional part: xmm1 = xmm1 - (double)integer_part
        try self.enc.emit(&.{ 0xF2, 0x48, 0x0F, 0x2A, 0xC0 }); // cvtsi2sd xmm0, rax
        try self.enc.emit(&.{ 0xF2, 0x0F, 0x5C, 0xC8 }); // subsd xmm1, xmm0 (xmm1 = fractional part)

        // ========== SECTION 5: Convert integer part to string ==========
        // Similar to generateRuntimeIntToString but for unsigned value in rbx
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xF0 }); // mov rcx, [rbp-16] (current buffer pos)
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xE8 }); // mov [rbp-24], rcx (save digit start)

        // Special case: integer part == 0
        try self.enc.emit(&.{ 0x48, 0x85, 0xDB }); // test rbx, rbx
        const jnz_int_not_zero = self.code.items.len;
        try self.enc.emit(&.{ 0x75, 0x00 }); // jnz int_not_zero

        // Integer is zero, write '0'
        try self.enc.emit(&.{ 0xC6, 0x01, 0x30 }); // mov byte [rcx], '0'
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx
        const jmp_past_int_loop = self.code.items.len;
        try self.enc.emit(&.{ 0xE9, 0x00, 0x00, 0x00, 0x00 }); // jmp past_int_loop (use 32-bit jump)

        const int_not_zero_pos = self.code.items.len;
        self.code.items[jnz_int_not_zero + 1] = @intCast(int_not_zero_pos - (jnz_int_not_zero + 2));

        // Loop: extract digits from rbx (integer part)
        try self.enc.emit(&.{ 0x48, 0x89, 0xD8 }); // mov rax, rbx

        const int_loop_start = self.code.items.len;
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax
        const jz_int_end = self.code.items.len;
        try self.enc.emit(&.{ 0x74, 0x00 }); // jz int_loop_end

        // digit = rax % 10, quotient in rax
        try self.enc.emit(&.{ 0x48, 0x31, 0xD2 }); // xor rdx, rdx
        try self.enc.emit(&.{ 0x49, 0xC7, 0xC0, 0x0A, 0x00, 0x00, 0x00 }); // mov r8, 10
        try self.enc.emit(&.{ 0x49, 0xF7, 0xF0 }); // div r8 (rax = quotient, rdx = remainder)

        // Write digit
        try self.enc.emit(&.{ 0x80, 0xC2, 0x30 }); // add dl, '0'
        try self.enc.emit(&.{ 0x88, 0x11 }); // mov [rcx], dl
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx

        // Loop back
        const int_loop_back: i8 = @intCast(@as(i64, @intCast(int_loop_start)) - @as(i64, @intCast(self.code.items.len)) - 2);
        try self.enc.emit(&.{ 0xEB, @bitCast(int_loop_back) }); // jmp int_loop_start

        const int_loop_end_pos = self.code.items.len;
        self.code.items[jz_int_end + 1] = @intCast(int_loop_end_pos - (jz_int_end + 2));

        // Save end position and reverse digits
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xE8 }); // mov rsi, [rbp-24] (start)
        try self.enc.emit(&.{ 0x48, 0x8D, 0x79, 0xFF }); // lea rdi, [rcx-1] (end-1)

        // Reverse loop
        const rev_loop_start = self.code.items.len;
        try self.enc.emit(&.{ 0x48, 0x39, 0xFE }); // cmp rsi, rdi
        const jae_rev_end = self.code.items.len;
        try self.enc.emit(&.{ 0x73, 0x00 }); // jae rev_end

        // Swap bytes
        try self.enc.emit(&.{ 0x8A, 0x06 }); // mov al, [rsi]
        try self.enc.emit(&.{ 0x8A, 0x1F }); // mov bl, [rdi]
        try self.enc.emit(&.{ 0x88, 0x1E }); // mov [rsi], bl
        try self.enc.emit(&.{ 0x88, 0x07 }); // mov [rdi], al
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC6 }); // inc rsi
        try self.enc.emit(&.{ 0x48, 0xFF, 0xCF }); // dec rdi

        const rev_loop_back: i8 = @intCast(@as(i64, @intCast(rev_loop_start)) - @as(i64, @intCast(self.code.items.len)) - 2);
        try self.enc.emit(&.{ 0xEB, @bitCast(rev_loop_back) }); // jmp rev_loop

        const rev_end_pos = self.code.items.len;
        self.code.items[jae_rev_end + 1] = @intCast(rev_end_pos - (jae_rev_end + 2));

        // Patch jmp_past_int_loop (32-bit jump)
        const past_int_loop_pos = self.code.items.len;
        const jmp_past_rel: i32 = @intCast(@as(i64, @intCast(past_int_loop_pos)) - @as(i64, @intCast(jmp_past_int_loop)) - 5);
        self.code.items[jmp_past_int_loop + 1] = @truncate(@as(u32, @bitCast(jmp_past_rel)));
        self.code.items[jmp_past_int_loop + 2] = @truncate(@as(u32, @bitCast(jmp_past_rel)) >> 8);
        self.code.items[jmp_past_int_loop + 3] = @truncate(@as(u32, @bitCast(jmp_past_rel)) >> 16);
        self.code.items[jmp_past_int_loop + 4] = @truncate(@as(u32, @bitCast(jmp_past_rel)) >> 24);

        // ========== SECTION 6: Write decimal point and fractional digits ==========
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xF0 }); // mov rcx, [rbp-16]
        try self.enc.emit(&.{ 0xC6, 0x01, 0x2E }); // mov byte [rcx], '.'
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xE0 }); // mov [rbp-32], rcx (save first frac digit pos)

        // Load constant 10.0 into xmm0
        // 10.0 in IEEE 754: 0x4024000000000000
        try self.enc.emit(&.{ 0x48, 0xB8 }); // mov rax, imm64
        try self.enc.emitI64(0x4024000000000000);
        try self.enc.emit(&.{ 0x66, 0x48, 0x0F, 0x6E, 0xC0 }); // movq xmm0, rax

        // Loop 6 times to extract 6 fractional digits
        try self.enc.emit(&.{ 0x41, 0xB8, 0x06, 0x00, 0x00, 0x00 }); // mov r8d, 6

        const frac_loop_start = self.code.items.len;
        // frac *= 10
        try self.enc.emit(&.{ 0xF2, 0x0F, 0x59, 0xC8 }); // mulsd xmm1, xmm0

        // digit = (int)frac
        try self.enc.emit(&.{ 0xF2, 0x48, 0x0F, 0x2C, 0xC1 }); // cvttsd2si rax, xmm1

        // frac -= digit
        try self.enc.emit(&.{ 0xF2, 0x48, 0x0F, 0x2A, 0xD0 }); // cvtsi2sd xmm2, rax
        try self.enc.emit(&.{ 0xF2, 0x0F, 0x5C, 0xCA }); // subsd xmm1, xmm2

        // Write digit
        try self.enc.emit(&.{ 0x04, 0x30 }); // add al, '0'
        try self.enc.emit(&.{ 0x88, 0x01 }); // mov [rcx], al
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx

        // Decrement counter and loop
        try self.enc.emit(&.{ 0x41, 0xFF, 0xC8 }); // dec r8d
        const jnz_frac_loop: i8 = @intCast(@as(i64, @intCast(frac_loop_start)) - @as(i64, @intCast(self.code.items.len)) - 2);
        try self.enc.emit(&.{ 0x75, @bitCast(jnz_frac_loop) }); // jnz frac_loop

        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx

        // ========== SECTION 7: Trim trailing zeros ==========
        // rcx points past last digit, [rbp-32] = first fractional digit
        try self.enc.emit(&.{ 0x48, 0x8B, 0x7D, 0xE0 }); // mov rdi, [rbp-32] (first frac digit)

        const trim_loop_start = self.code.items.len;
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC9 }); // dec rcx
        try self.enc.emit(&.{ 0x48, 0x39, 0xF9 }); // cmp rcx, rdi (at first digit?)
        const je_trim_keep_first = self.code.items.len;
        try self.enc.emit(&.{ 0x74, 0x00 }); // je trim_keep_first (keep at least one digit)

        try self.enc.emit(&.{ 0x80, 0x39, 0x30 }); // cmp byte [rcx], '0'
        const je_trim_loop: i8 = @intCast(@as(i64, @intCast(trim_loop_start)) - @as(i64, @intCast(self.code.items.len)) - 2);
        try self.enc.emit(&.{ 0x74, @bitCast(je_trim_loop) }); // je trim_loop

        // Not a zero, move past it
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx
        const jmp_trim_done = self.code.items.len;
        try self.enc.emit(&.{ 0xEB, 0x00 }); // jmp trim_done

        // At first digit - include it by incrementing past it
        const trim_keep_first_pos = self.code.items.len;
        self.code.items[je_trim_keep_first + 1] = @intCast(trim_keep_first_pos - (je_trim_keep_first + 2));
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC1 }); // inc rcx (move past first digit to include it)

        const trim_done_pos = self.code.items.len;
        self.code.items[jmp_trim_done + 1] = @intCast(trim_done_pos - (jmp_trim_done + 2));

        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xF0 }); // mov [rbp-16], rcx

        // ========== SECTION 8: Calculate length and return ==========
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xF0 }); // mov rax, [rbp-16] (end)
        try self.enc.emit(&.{ 0x48, 0x2B, 0x45, 0xF8 }); // sub rax, [rbp-8] (start)

        // ========== Epilogue ==========
        const epilogue_start = self.code.items.len;
        try self.enc.emit(&.{0x5F}); // pop rdi
        try self.enc.emit(&.{0x5E}); // pop rsi
        try self.enc.emit(&.{0x5B}); // pop rbx
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x40 }); // add rsp, 64
        try self.enc.popRbp();
        try self.enc.ret();

        // Patch all jumps to epilogue
        const patchJmpToEpilogue = struct {
            fn patch(code_items: []u8, jmp_pos: usize, target: usize) void {
                const rel: i32 = @intCast(@as(i64, @intCast(target)) - @as(i64, @intCast(jmp_pos)) - 5);
                code_items[jmp_pos + 1] = @truncate(@as(u32, @bitCast(rel)));
                code_items[jmp_pos + 2] = @truncate(@as(u32, @bitCast(rel)) >> 8);
                code_items[jmp_pos + 3] = @truncate(@as(u32, @bitCast(rel)) >> 16);
                code_items[jmp_pos + 4] = @truncate(@as(u32, @bitCast(rel)) >> 24);
            }
        }.patch;

        patchJmpToEpilogue(self.code.items, jmp_to_epilogue_nan, epilogue_start);
        patchJmpToEpilogue(self.code.items, jmp_to_epilogue_neginf, epilogue_start);
        patchJmpToEpilogue(self.code.items, jmp_to_epilogue_posinf, epilogue_start);
        patchJmpToEpilogue(self.code.items, jmp_to_epilogue_zero, epilogue_start);
    }

    /// Generate __runtime_bool_to_string(buffer, value) -> length
    /// Converts a bool (0 or 1) to "false" or "true"
    fn generateRuntimeBoolToString(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__runtime_bool_to_string", self.code.items.len);

        // Prologue
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp

        // Parameters: RCX = buffer, RDX = bool value (0 or non-zero)
        // Check if value is zero
        try self.enc.emit(&.{ 0x48, 0x85, 0xD2 }); // test rdx, rdx
        const jz_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x74, 0x00 }); // jz write_false (patch later)
        const jz_start = self.code.items.len;

        // Write "true"
        try self.enc.emit(&.{ 0xC6, 0x01, 0x74 }); // mov byte [rcx], 't'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x01, 0x72 }); // mov byte [rcx+1], 'r'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x02, 0x75 }); // mov byte [rcx+2], 'u'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x03, 0x65 }); // mov byte [rcx+3], 'e'
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC0, 0x04, 0x00, 0x00, 0x00 }); // mov rax, 4
        const jmp_end_offset = self.code.items.len;
        try self.enc.emit(&.{ 0xEB, 0x00 }); // jmp end (patch later)

        // Write "false"
        const write_false = self.code.items.len;
        self.code.items[jz_offset + 1] = @intCast(write_false - jz_start);

        try self.enc.emit(&.{ 0xC6, 0x01, 0x66 }); // mov byte [rcx], 'f'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x01, 0x61 }); // mov byte [rcx+1], 'a'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x02, 0x6C }); // mov byte [rcx+2], 'l'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x03, 0x73 }); // mov byte [rcx+3], 's'
        try self.enc.emit(&.{ 0xC6, 0x41, 0x04, 0x65 }); // mov byte [rcx+4], 'e'
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC0, 0x05, 0x00, 0x00, 0x00 }); // mov rax, 5

        // Patch jump
        const end_func = self.code.items.len;
        self.code.items[jmp_end_offset + 1] = @intCast(end_func - jmp_end_offset - 2);

        // Epilogue (no stack frame needed for this simple function)
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Helper to print a static string - assumes R15 = stdout handle
    fn printStaticString(self: *IrCodegen, str: []const u8) !void {
        const string_len: u32 = @intCast(str.len);

        // Jump over string data
        const jmp_offset = try self.enc.jmpRel32();
        const string_pos = self.code.items.len;
        try self.code.appendSlice(self.allocator, str);
        const after_string = self.code.items.len;

        // Patch jump
        const rel: i32 = @intCast(after_string - jmp_offset - 4);
        self.code.items[jmp_offset] = @bitCast(@as(i8, @intCast(rel & 0xFF)));
        self.code.items[jmp_offset + 1] = @bitCast(@as(i8, @intCast((rel >> 8) & 0xFF)));
        self.code.items[jmp_offset + 2] = @bitCast(@as(i8, @intCast((rel >> 16) & 0xFF)));
        self.code.items[jmp_offset + 3] = @bitCast(@as(i8, @intCast((rel >> 24) & 0xFF)));

        // LEA RCX, [RIP - offset_to_string] for buffer pointer
        const rip_offset: i32 = -@as(i32, @intCast(after_string - string_pos + 7));
        try self.enc.emit(&.{ 0x48, 0x8D, 0x0D }); // lea rcx, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // Save buffer ptr
        try self.enc.pushRax();
        try self.enc.emit(&.{ 0x48, 0x89, 0xC8 }); // mov rax, rcx (save buffer)

        // WriteFile(hFile=R15, lpBuffer=buffer, nBytes=len, lpWritten=NULL, lpOverlapped=NULL)
        try self.beginExternalCall(1);
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF9 }); // mov rcx, r15 (handle)
        try self.enc.emit(&.{ 0x48, 0x89, 0xC2 }); // mov rdx, rax (buffer)
        try self.enc.emit(&.{ 0x41, 0xB8 }); // mov r8d, imm32 (length)
        try self.enc.emitI32(@intCast(string_len));
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (lpNumberOfBytesWritten = NULL)
        try self.emitStackParam(0, 0); // lpOverlapped = NULL
        try self.emitExternalCallAfterSetup("kernel32.dll", "WriteFile");
        try self.endExternalCall();
        try self.enc.popRax();
    }

    /// Helper to print tag string from R12 (ptr) with length from [rbp-48]
    /// Assumes R15 = stdout handle
    fn printTagFromR12(self: *IrCodegen) !void {
        // Load tag length from stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xD0 }); // mov rax, [rbp-48] (tag len)

        // WriteFile(hFile=R15, lpBuffer=R12, nBytes=rax, lpWritten=NULL, lpOverlapped=NULL)
        try self.beginExternalCall(1);
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF9 }); // mov rcx, r15 (handle)
        try self.enc.emit(&.{ 0x4C, 0x89, 0xE2 }); // mov rdx, r12 (buffer = tag ptr)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC0 }); // mov r8, rax (length)
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (lpNumberOfBytesWritten = NULL)
        try self.emitStackParam(0, 0); // lpOverlapped = NULL
        try self.emitExternalCallAfterSetup("kernel32.dll", "WriteFile");
        try self.endExternalCall();
    }

    /// Emit WriteFile call with buffer in RDX and length in R8
    /// Assumes R15 = stdout handle, RDX = buffer, R8 = length
    fn emitWriteFileCall(self: *IrCodegen) !void {
        try self.beginExternalCall(1);
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF9 }); // mov rcx, r15 (handle)
        // RDX already has buffer, R8 already has length
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (lpNumberOfBytesWritten = NULL)
        try self.emitStackParam(0, 0); // lpOverlapped = NULL
        try self.emitExternalCallAfterSetup("kernel32.dll", "WriteFile");
        try self.endExternalCall();
    }

    /// Helper to print number from a stack offset - assumes R15 = stdout handle
    /// Uses [rbp-40] as conversion buffer. Converts number at [rbp+stack_offset] to decimal and prints.
    fn printNumberFromStack(self: *IrCodegen, stack_offset: i8) !void {
        // Load number from stack to RAX
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, @bitCast(stack_offset) }); // mov rax, [rbp+offset]

        // lea rdi, [rbp-40] (end of buffer - we write backwards)
        try self.enc.emit(&.{ 0x48, 0x8D, 0x7D, 0xD8 }); // lea rdi, [rbp-40]

        // Store null terminator
        try self.enc.emit(&.{ 0xC6, 0x07, 0x00 }); // mov byte [rdi], 0

        // mov rcx, 10 (divisor)
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC1, 0x0A, 0x00, 0x00, 0x00 }); // mov rcx, 10

        // mov rsi, rdi (save end position)
        try self.enc.emit(&.{ 0x48, 0x89, 0xFE }); // mov rsi, rdi

        // Convert loop
        const loop_start = self.code.items.len;

        // dec rdi
        try self.enc.emit(&.{ 0x48, 0xFF, 0xCF }); // dec rdi

        // xor rdx, rdx; div rcx => rax = quotient, rdx = remainder
        try self.enc.emit(&.{ 0x48, 0x31, 0xD2 }); // xor rdx, rdx
        try self.enc.emit(&.{ 0x48, 0xF7, 0xF1 }); // div rcx

        // add dl, '0'; mov [rdi], dl
        try self.enc.emit(&.{ 0x80, 0xC2, 0x30 }); // add dl, '0'
        try self.enc.emit(&.{ 0x88, 0x17 }); // mov [rdi], dl

        // test rax, rax; jnz loop
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax
        const jnz_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x75, 0x00 }); // jnz rel8 (placeholder)
        const jump_back: i8 = @intCast(@as(i64, @intCast(loop_start)) - @as(i64, @intCast(self.code.items.len)));
        self.code.items[jnz_offset + 1] = @bitCast(jump_back);

        // Now rdi points to start of number string, rsi points to end
        // Length = rsi - rdi
        try self.enc.emit(&.{ 0x48, 0x89, 0xF0 }); // mov rax, rsi
        try self.enc.emit(&.{ 0x48, 0x29, 0xF8 }); // sub rax, rdi (rax = length)

        // Set up WriteFile: RDX = buffer (rdi), R8 = length (rax)
        try self.enc.emit(&.{ 0x48, 0x89, 0xFA }); // mov rdx, rdi (buffer)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC0 }); // mov r8, rax (length)
        try self.emitWriteFileCall();
    }

    /// Emit table lookup for a pointer - searches tracking table for ptr in RAX
    /// Output: R14 = allocation ID (0 if not found), RDX = entry address (0 if not found)
    fn emitTableLookup(self: *IrCodegen) !void {
        // Save search ptr to R8
        try self.enc.emit(&.{ 0x49, 0x89, 0xC0 }); // mov r8, rax

        // Default R14 = 0 (not found)
        try self.enc.emit(&.{ 0x4D, 0x31, 0xF6 }); // xor r14, r14

        // Load table count to RCX
        try self.emitLoadTrackingField(.table_count);
        try self.enc.emit(&.{ 0x48, 0x89, 0xC1 }); // mov rcx, rax (count)

        // Load table base to RDX
        try self.emitLeaTrackingField(.table_base);
        try self.enc.emit(&.{ 0x48, 0x89, 0xC2 }); // mov rdx, rax (table base)

        // Restore search ptr to RAX
        try self.enc.emit(&.{ 0x4C, 0x89, 0xC0 }); // mov rax, r8

        // Loop: search for ptr in table
        const loop_start = self.code.items.len;

        // Check if count == 0
        try self.enc.emit(&.{ 0x48, 0x85, 0xC9 }); // test rcx, rcx
        const exit_jmp = try self.enc.jeRel32(); // je to exit

        // Compare [rdx+0] (entry ptr) with rax (search ptr)
        try self.enc.emit(&.{ 0x48, 0x3B, 0x02 }); // cmp rax, [rdx]
        const not_found_jmp = try self.enc.jneRel32(); // jne to next iteration

        // Found! Load ID from [rdx+16] into R14
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x72, 0x10 }); // mov r14, [rdx+16]
        const found_jmp = try self.enc.jmpRel32(); // jmp to after_loop

        // Not found - advance to next entry
        const next_iter = self.code.items.len;
        try self.enc.emit(&.{ 0x48, 0x83, 0xC2, 0x18 }); // add rdx, 24 (next entry)
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC9 }); // dec rcx

        // Jump back to loop start
        const back_offset: i32 = @intCast(@as(i64, @intCast(loop_start)) - @as(i64, @intCast(self.code.items.len)) - 5);
        try self.enc.emit(&.{0xE9}); // jmp rel32
        try self.enc.emitI32(back_offset);

        // Not found exit - clear RDX to indicate not found
        const not_found_exit = self.code.items.len;
        try self.enc.emit(&.{ 0x48, 0x31, 0xD2 }); // xor rdx, rdx
        const skip_found = try self.enc.jmpRel32();

        // Found exit - RDX already has entry address
        const found_exit = self.code.items.len;

        // After loop (for skip_found jump)
        const after_loop = self.code.items.len;

        // Patch exit_jmp -> not_found_exit
        const exit_rel: i32 = @intCast(@as(i64, @intCast(not_found_exit)) - @as(i64, @intCast(exit_jmp)) - 4);
        self.code.items[exit_jmp] = @bitCast(@as(i8, @intCast(exit_rel & 0xFF)));
        self.code.items[exit_jmp + 1] = @bitCast(@as(i8, @intCast((exit_rel >> 8) & 0xFF)));
        self.code.items[exit_jmp + 2] = @bitCast(@as(i8, @intCast((exit_rel >> 16) & 0xFF)));
        self.code.items[exit_jmp + 3] = @bitCast(@as(i8, @intCast((exit_rel >> 24) & 0xFF)));

        // Patch not_found_jmp -> next_iter
        const not_found_rel: i32 = @intCast(@as(i64, @intCast(next_iter)) - @as(i64, @intCast(not_found_jmp)) - 4);
        self.code.items[not_found_jmp] = @bitCast(@as(i8, @intCast(not_found_rel & 0xFF)));
        self.code.items[not_found_jmp + 1] = @bitCast(@as(i8, @intCast((not_found_rel >> 8) & 0xFF)));
        self.code.items[not_found_jmp + 2] = @bitCast(@as(i8, @intCast((not_found_rel >> 16) & 0xFF)));
        self.code.items[not_found_jmp + 3] = @bitCast(@as(i8, @intCast((not_found_rel >> 24) & 0xFF)));

        // Patch found_jmp -> found_exit
        const found_rel: i32 = @intCast(@as(i64, @intCast(found_exit)) - @as(i64, @intCast(found_jmp)) - 4);
        self.code.items[found_jmp] = @bitCast(@as(i8, @intCast(found_rel & 0xFF)));
        self.code.items[found_jmp + 1] = @bitCast(@as(i8, @intCast((found_rel >> 8) & 0xFF)));
        self.code.items[found_jmp + 2] = @bitCast(@as(i8, @intCast((found_rel >> 16) & 0xFF)));
        self.code.items[found_jmp + 3] = @bitCast(@as(i8, @intCast((found_rel >> 24) & 0xFF)));

        // Patch skip_found -> after_loop
        const skip_rel: i32 = @intCast(@as(i64, @intCast(after_loop)) - @as(i64, @intCast(skip_found)) - 4);
        self.code.items[skip_found] = @bitCast(@as(i8, @intCast(skip_rel & 0xFF)));
        self.code.items[skip_found + 1] = @bitCast(@as(i8, @intCast((skip_rel >> 8) & 0xFF)));
        self.code.items[skip_found + 2] = @bitCast(@as(i8, @intCast((skip_rel >> 16) & 0xFF)));
        self.code.items[skip_found + 3] = @bitCast(@as(i8, @intCast((skip_rel >> 24) & 0xFF)));
    }

    fn patchCalls(self: *IrCodegen) !void {
        for (self.call_patches.items) |patch| {
            const target_offset = self.func_offsets.get(patch.target_func) orelse {
                std.debug.print("error: call to undefined function '{s}'\n", .{patch.target_func});
                return error.UndefinedFunction;
            };
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

    /// Calculate frame size with proper alignment considering callee-saved pushes
    /// From old compiler (x86_reg_alloc.cpp lines 517-578):
    /// - After push rbp: RSP % 16 == 0
    /// - After N callee-saved pushes: RSP % 16 == (N % 2) * 8
    /// - If N is odd, frame_size must be 8 mod 16
    /// - If N is even, frame_size must be 0 mod 16
    fn calculateAlignedFrameSize(base_size: i32, num_callee_saved: usize) i32 {
        var frame = base_size;
        if (num_callee_saved % 2 == 1) {
            // Odd pushes: need frame % 16 == 8
            if (@mod(frame, 16) == 0) {
                frame += 8;
            } else if (@mod(frame, 16) != 8) {
                frame = (frame + 15) & ~@as(i32, 15);
                frame += 8;
            }
        } else {
            // Even pushes: need frame % 16 == 0
            if (@mod(frame, 16) != 0) {
                frame = (frame + 15) & ~@as(i32, 15);
            }
        }
        return frame;
    }

    /// Allocate registers for a function based on liveness analysis
    /// This determines which callee-saved registers will be used and need save/restore
    fn allocateRegisters(self: *IrCodegen, func: *const ir.Function, liveness: *const LivenessInfo) !void {
        // Reset reg_alloc for this function
        self.reg_alloc.deinit(self.allocator);
        self.reg_alloc = RegAllocInfo.init();

        // Check if this function returns a large struct or Optional (> 8 bytes)
        // Windows x64 ABI: Large structs/optionals are returned via hidden pointer in RCX
        // This shifts all other parameters right by one register
        self.reg_alloc.has_hidden_ret_ptr = hasHiddenReturnPointer(func.return_type);

        // Track available callee-saved registers
        var available_gprs: std.ArrayListUnmanaged(x86.Gpr) = .empty;
        defer available_gprs.deinit(self.allocator);
        for (x86.win64.callee_saved_gprs) |reg| {
            try available_gprs.append(self.allocator, reg);
        }

        var available_xmms: std.ArrayListUnmanaged(x86.Xmm) = .empty;
        defer available_xmms.deinit(self.allocator);
        for (x86.win64.callee_saved_xmms) |reg| {
            try available_xmms.append(self.allocator, reg);
        }

        // Allocate values that are live across calls to callee-saved registers
        // For now, just track which values need callee-saved regs
        // The actual value locations will still use the existing stack-based approach
        // but we record which callee-saved regs are used for the prologue/epilogue

        // Future enhancement: iterate over liveness.live_across_calls to assign
        // callee-saved registers to values that are live across function calls.
        // For now, the stack-based approach handles all values, so no callee-saved
        // registers are actually used yet. This infrastructure is in place for
        // when we implement true register allocation.
        _ = liveness;
    }

    /// Emit callee-saved register saves after prologue
    /// GPRs are pushed, XMMs are stored via movsd to stack slots
    fn emitCalleeSavedSaves(self: *IrCodegen) !void {
        // Push GPRs (adjusts RSP)
        for (self.reg_alloc.used_callee_saved_gprs.items) |reg| {
            try self.enc.pushGpr(reg);
        }

        // Store XMM registers to stack (need to allocate space first)
        // Each XMM save needs 8 bytes (movsd saves 64-bit double)
        for (self.reg_alloc.used_callee_saved_xmms.items, 0..) |xmm, i| {
            // Store at [rbp - (base_offset + i*8)]
            // XMM save slots are allocated after GPR pushes in frame calculation
            const offset: i32 = self.reg_alloc.frame_size - @as(i32, @intCast(i * 8 + 8));
            try self.enc.movsdRbpOffsetXmm(offset, xmm);
        }
    }

    /// Emit callee-saved register restores before epilogue
    /// XMMs are loaded via movsd, GPRs are popped in reverse order
    fn emitCalleeSavedRestores(self: *IrCodegen) !void {
        // Restore XMM registers from stack (in any order)
        for (self.reg_alloc.used_callee_saved_xmms.items, 0..) |xmm, i| {
            const offset: i32 = self.reg_alloc.frame_size - @as(i32, @intCast(i * 8 + 8));
            try self.enc.movsdXmmRbpOffset(xmm, offset);
        }

        // Pop GPRs in reverse order
        var i: usize = self.reg_alloc.used_callee_saved_gprs.items.len;
        while (i > 0) {
            i -= 1;
            try self.enc.popGpr(self.reg_alloc.used_callee_saved_gprs.items[i]);
        }
    }

    fn generateFunction(self: *IrCodegen, func: *const ir.Function) !void {
        self.value_locations.clearRetainingCapacity();
        self.value_types.clearRetainingCapacity();
        self.indirect_ptrs.clearRetainingCapacity();
        self.block_offsets.clearRetainingCapacity();
        self.jump_patches.clearRetainingCapacity();
        self.stored_types.clearRetainingCapacity();
        self.next_stack_offset = -8;
        self.current_func_name = func.name;
        self.current_func_ret_type = func.return_type;

        // Scan all calls to find maximum number of arguments
        // Windows x64 ABI: first 4 args in registers, rest on stack
        var max_args: usize = 0;
        for (func.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                if (inst.op == .call) {
                    const args = inst.operands[1].call_args;
                    if (args.len > max_args) {
                        max_args = args.len;
                    }
                }
            }
        }
        // Calculate space needed for stack arguments (args beyond first 4)
        // Each stack argument takes 8 bytes, aligned to 8 bytes
        if (max_args > 4) {
            self.outgoing_stack_args_size = @as(i32, @intCast((max_args - 4) * 8));
        } else {
            self.outgoing_stack_args_size = 0;
        }

        // Perform liveness analysis to determine which values are live across calls
        var liveness = try analyzeLiveness(self.allocator, func);
        defer liveness.deinit(self.allocator);

        // Allocate registers based on liveness (determines callee-saved reg usage)
        try self.allocateRegisters(func, &liveness);

        // Calculate number of callee-saved registers that will be pushed
        const num_callee_saved_gprs = self.reg_alloc.used_callee_saved_gprs.items.len;
        const num_callee_saved_xmms = self.reg_alloc.used_callee_saved_xmms.items.len;

        // Emit prologue with placeholder stack size (will be patched after)
        const func_start = self.code.items.len;
        try self.enc.prologue(0);

        // Push callee-saved GPRs after prologue but before stack frame allocation
        // Note: The stack frame size will be patched to account for these pushes
        try self.emitCalleeSavedSaves();

        // If function has hidden return pointer, save RCX to a stack slot
        // Windows x64 ABI: Large struct returns have the destination pointer in RCX
        // We must save it immediately as RCX will be clobbered by the function body
        if (self.reg_alloc.has_hidden_ret_ptr) {
            const hidden_ptr_offset = self.allocStackSlots(1);
            self.reg_alloc.hidden_ret_ptr_offset = hidden_ptr_offset;
            // mov [rbp+offset], rcx - save the hidden return pointer
            try self.enc.emitWithRbpOffset(&.{ 0x48, 0x89, 0x4D }, hidden_ptr_offset);
        }

        // Generate code for each block, recording block start offsets
        for (func.blocks.items) |block| {
            // Record the start offset of this block
            try self.block_offsets.append(self.allocator, self.code.items.len);

            for (block.instructions.items) |inst| {
                try self.generateInstruction(inst);
            }
        }

        // Patch all jumps now that we know block offsets
        try self.patchJumps();

        // Calculate actual stack size accounting for callee-saved registers
        // next_stack_offset is negative, representing bytes below rbp
        var stack_used: i32 = -self.next_stack_offset;

        // Add space for XMM saves (8 bytes each, stored in frame)
        stack_used += @as(i32, @intCast(num_callee_saved_xmms * 8));

        // Add space for outgoing stack arguments (for calls with >4 args)
        // This reserves space at the bottom of the frame for passing stack arguments
        stack_used += self.outgoing_stack_args_size;

        // Calculate frame size with proper alignment considering GPR pushes
        // GPR pushes happen after prologue, so they affect RSP alignment
        const stack_size = calculateAlignedFrameSize(stack_used, num_callee_saved_gprs);

        // Store frame size in reg_alloc for use by emitCalleeSavedRestores
        self.reg_alloc.frame_size = stack_size;

        // Prologue layout: push rbp (1) + mov rbp,rsp (3) + sub rsp,imm32 (3+4)
        // The imm32 is at offset 7 from function start
        const stack_size_offset = func_start + 7;
        const bytes: [4]u8 = @bitCast(stack_size);
        self.code.items[stack_size_offset] = bytes[0];
        self.code.items[stack_size_offset + 1] = bytes[1];
        self.code.items[stack_size_offset + 2] = bytes[2];
        self.code.items[stack_size_offset + 3] = bytes[3];
    }

    fn patchJumps(self: *IrCodegen) !void {
        for (self.jump_patches.items) |patch| {
            if (patch.target_block >= self.block_offsets.items.len) continue;

            const target_offset = self.block_offsets.items[patch.target_block];
            // Calculate relative offset: target - (patch_location + 4)
            const rel_offset: i32 = @intCast(@as(i64, @intCast(target_offset)) - @as(i64, @intCast(patch.offset + 4)));
            const bytes: [4]u8 = @bitCast(rel_offset);
            self.code.items[patch.offset] = bytes[0];
            self.code.items[patch.offset + 1] = bytes[1];
            self.code.items[patch.offset + 2] = bytes[2];
            self.code.items[patch.offset + 3] = bytes[3];
        }
    }

    fn generateInstruction(self: *IrCodegen, inst: ir.Instruction) !void {
        debug.codegen("Generating: {s}, result: {?}", .{ inst.op.format(), inst.result });
        switch (inst.op) {
            .const_i8 => try self.genConst32(inst, inst.operands[0].immediate_i32, .i8),
            .const_i32 => try self.genConst32(inst, inst.operands[0].immediate_i32, .i32),
            .const_i64 => try self.genConst64(inst, inst.operands[0].immediate_i64, .i64),
            .const_f64 => try self.genConst64(inst, @bitCast(inst.operands[0].immediate_f64), .f64),
            .alloca => try self.genAlloca(inst),
            .alloca_sized => try self.genAllocaSized(inst),
            .alloca_dynamic => try self.genAllocaDynamic(inst),
            .getfieldptr => try self.genGetFieldPtr(inst),
            .getelemptr => try self.genGetElemPtr(inst),
            .store => try self.genStore(inst),
            .store_i8 => try self.genStoreI8(inst),
            .store_i32 => try self.genStoreI32(inst),
            .load => try self.genLoad(inst),
            .add, .sub, .mul, .div, .mod, .band, .bitor, .bxor, .shl, .shr => try self.genIntBinaryOp(inst),
            .fadd, .fsub, .fmul, .fdiv => try self.genFloatBinaryOp(inst),
            .fptosi => try self.genFpToSi(inst),
            .sitofp => try self.genSiToFp(inst),
            .fabs => try self.genFabs(inst),
            .bitcast_f64_to_i64 => try self.genBitcastF64ToI64(inst),
            .sext_i32_i64 => try self.genSextI32I64(inst),
            .trunc_i64_i32 => try self.genTruncI64I32(inst),
            .trunc_i64_i8 => try self.genTruncI64I8(inst),
            .zext_i8_i64 => try self.genZextI8I64(inst),
            .ret => try self.genRet(inst),
            .param => try self.genParam(inst),
            .call => try self.genCall(inst),
            .memcpy => try self.genMemcpy(inst),
            .memcpy_dyn => try self.genMemcpyDyn(inst),
            .memset => try self.genMemset(inst),
            .heap_alloc => try self.genHeapAlloc(inst),
            .heap_free => try self.genHeapFree(inst),
            .heap_realloc => try self.genHeapRealloc(inst),
            .fcmp_eq => try self.genFcmp(inst, .eq),
            .fcmp_ne => try self.genFcmp(inst, .ne),
            .fcmp_lt => try self.genFcmp(inst, .lt),
            .fcmp_le => try self.genFcmp(inst, .le),
            .fcmp_gt => try self.genFcmp(inst, .gt),
            .fcmp_ge => try self.genFcmp(inst, .ge),
            .icmp_eq => try self.genIcmp(inst, .eq),
            .icmp_ne => try self.genIcmp(inst, .ne),
            .icmp_lt => try self.genIcmp(inst, .lt),
            .icmp_le => try self.genIcmp(inst, .le),
            .icmp_gt => try self.genIcmp(inst, .gt),
            .icmp_ge => try self.genIcmp(inst, .ge),
            .br => try self.genBr(inst),
            .br_cond => try self.genBrCond(inst),
            else => {
                std.debug.print("ERROR: Unhandled IR instruction: {s}\n", .{inst.op.format()});
                return error.UnhandledInstruction;
            },
        }
    }

    // ------------------------------------------------------------------------
    // Instruction Generators
    // ------------------------------------------------------------------------

    fn genConst32(self: *IrCodegen, inst: ir.Instruction, value: i32, ty: ir.Type) !void {
        const result = inst.result.?;
        const offset = self.allocStackSlots(1);
        // Sign-extend to 64-bit for storage (stack slots are 8 bytes)
        try self.enc.movRaxImm64(@as(i64, value));
        try self.enc.movRbpOffsetRax(offset);
        try self.setValueLocation(result, .{ .stack = offset }, ty);
    }

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
        try self.value_types.put(self.allocator, inst.result.?, inst.result_type);
        debug.codegen("  alloca: result=%{d}, offset={d}, type={s}", .{ inst.result.?, offset, inst.result_type.toIrName() });
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

        // Allocate another slot to store the ADDRESS of the allocated space
        // This is necessary because when we use this value, we want the address, not the contents
        const ptr_slot = self.allocStackSlots(1);
        try self.enc.leaRaxRbpOffset(struct_offset);
        try self.enc.movRbpOffsetRax(ptr_slot);

        try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = ptr_slot });
        try self.value_types.put(self.allocator, inst.result.?, .ptr);
        // Mark as indirect because the slot contains a pointer that needs to be dereferenced
        try self.markIndirect(inst.result.?);
        debug.codegen("  alloca.sized: result=%{d}, size={d}, num_slots={d}, base_offset={d}, struct_offset={d}, ptr_slot={d}", .{ inst.result.?, size, num_slots, base_offset, struct_offset, ptr_slot });
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

        debug.codegen("  getfieldptr: base=%{d}, offset={d}, base_stack_offset={d}, indirect={}", .{ base_val, field_offset, base_stack_offset, self.isIndirect(base_val) });

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
            debug.codegen("    -> indirect: result=%{d}, result_stack_offset={d}", .{ inst.result.?, result_offset });
        } else {
            // Base is direct - the stack slot IS the struct, just compute offset
            const field_stack_offset = base_stack_offset + field_offset;
            try self.value_locations.put(self.allocator, inst.result.?, .{ .stack = field_stack_offset });
            try self.value_types.put(self.allocator, inst.result.?, .ptr);
            debug.codegen("    -> direct: result=%{d}, field_stack_offset={d}", .{ inst.result.?, field_stack_offset });
        }
    }

    fn genGetElemPtr(self: *IrCodegen, inst: ir.Instruction) !void {
        const base_val = inst.operands[0].value;
        const index_val = inst.operands[1].value;
        const elem_size = inst.operands[2].elem_size;
        const result = inst.result.?;

        debug.codegen("  getelemptr: base=%{d}, index=%{d}, elem_size={d}", .{ base_val, index_val, elem_size });

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

        // Multiply index by element size
        if (elem_size == 8) {
            // shl rax, 3 (multiply by 8)
            try self.enc.shlRaxImm8(3);
        } else if (elem_size == 4) {
            // shl rax, 2 (multiply by 4)
            try self.enc.shlRaxImm8(2);
        } else if (elem_size == 2) {
            // shl rax, 1 (multiply by 2)
            try self.enc.shlRaxImm8(1);
        } else if (elem_size == 1) {
            // No multiplication needed for byte access
        } else {
            // General case: imul rax, elem_size
            try self.enc.emit(&.{ 0x48, 0x6B, 0xC0 }); // imul rax, rax, imm8
            try self.enc.emit(&.{@as(u8, @intCast(@as(u32, @bitCast(elem_size)) & 0xFF))});
        }

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
        const val_type = self.value_types.get(val) orelse return error.ValueTypeNotFound;
        const val_offset = self.getStackOffset(val) catch -999;

        debug.codegen("  store: ptr=%{d}, val=%{d}, ptr_offset={d}, val_offset={d}, ptr_indirect={}, val_indirect={}, val_type={s}", .{ ptr, val, offset, val_offset, self.isIndirect(ptr), self.isIndirect(val), val_type.toIrName() });

        // Track what type was stored at this location for later loads
        try self.stored_types.put(self.allocator, ptr, val_type);

        if (self.isIndirect(ptr)) {
            try self.enc.movRcxRbpOffset(offset);
        }

        if (val_type == .f64) {
            try self.loadToXmm(val, .xmm0);
            if (self.isIndirect(ptr)) {
                try self.enc.movsdMemRcxXmm0();
            } else {
                try self.enc.movsdRbpOffsetXmm0(offset);
            }
        } else {
            // If value is a ptr type that's NOT indirect, we need its address (lea)
            // not its contents (mov)
            if (val_type == .ptr and !self.isIndirect(val)) {
                const val_off = try self.getStackOffset(val);
                try self.enc.leaRaxRbpOffset(val_off);
                debug.codegen("    -> lea for non-indirect ptr: val_offset={d}", .{val_off});
            } else {
                try self.loadToRax(val);
                debug.codegen("    -> loadToRax for value", .{});
            }
            if (self.isIndirect(ptr)) {
                try self.enc.movMemRax(.rcx);
                debug.codegen("    -> indirect store: mov [rcx], rax", .{});
            } else {
                try self.enc.movRbpOffsetRax(offset);
                debug.codegen("    -> direct store: mov [rbp+{d}], rax", .{offset});
            }
        }
    }

    fn genStoreI8(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr = inst.operands[0].value;
        const val = inst.operands[1].value;
        const offset = try self.getStackOffset(ptr);

        // Load value to AL (low byte of RAX)
        try self.loadToRax(val);

        if (self.isIndirect(ptr)) {
            // Load pointer to RCX, store byte to [RCX]
            try self.enc.movRcxRbpOffset(offset);
            // mov [rcx], al
            try self.enc.emit(&.{ 0x88, 0x01 });
        } else {
            // Store byte directly to stack: mov [rbp+offset], al
            const off_bytes: [4]u8 = @bitCast(offset);
            try self.enc.emit(&.{ 0x88, 0x85 }); // mov [rbp+disp32], al
            try self.enc.emit(&off_bytes);
        }
    }

    fn genStoreI32(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr = inst.operands[0].value;
        const val = inst.operands[1].value;
        const offset = try self.getStackOffset(ptr);

        debug.codegen("  store.i32: ptr=%{d}, val=%{d}, offset={d}, indirect={}", .{ ptr, val, offset, self.isIndirect(ptr) });

        // Load value to EAX (low 32 bits of RAX)
        try self.loadToRax(val);

        if (self.isIndirect(ptr)) {
            // Load pointer to RCX, store dword to [RCX]
            try self.enc.movRcxRbpOffset(offset);
            // mov [rcx], eax
            try self.enc.emit(&.{ 0x89, 0x01 });
            debug.codegen("    -> indirect store: mov [rcx], eax", .{});
        } else {
            // Store dword directly to stack: mov [rbp+offset], eax
            const off_bytes: [4]u8 = @bitCast(offset);
            try self.enc.emit(&.{ 0x89, 0x85 }); // mov [rbp+disp32], eax
            try self.enc.emit(&off_bytes);
            debug.codegen("    -> direct store: mov [rbp+{d}], eax", .{offset});
        }
    }

    fn genMemcpy(self: *IrCodegen, inst: ir.Instruction) !void {
        const dest_val = inst.operands[0].value;
        const src_val = inst.operands[1].value;
        const size: i32 = inst.operands[2].immediate_i32;

        // Load src pointer to r10 (caller-saved, safe to clobber)
        const src_offset = try self.getStackOffset(src_val);
        if (self.isIndirect(src_val)) {
            try self.enc.movR10RbpOffset(src_offset);
        } else {
            try self.enc.leaR10RbpOffset(src_offset);
        }

        // Load dest pointer to r11 (caller-saved, safe to clobber)
        const dest_offset = try self.getStackOffset(dest_val);
        if (self.isIndirect(dest_val)) {
            try self.enc.movR11RbpOffset(dest_offset);
        } else {
            try self.enc.leaR11RbpOffset(dest_offset);
        }

        // Copy 8 bytes at a time using r10 (src) and r11 (dest)
        var copied: i32 = 0;
        while (copied < size) : (copied += 8) {
            try self.enc.movRaxMemR10Offset(copied);
            try self.enc.movMemR11OffsetRax(copied);
        }
    }

    fn genMemcpyDyn(self: *IrCodegen, inst: ir.Instruction) !void {
        // Dynamic-sized memcpy using rep movsb
        const dest_val = inst.operands[0].value;
        const src_val = inst.operands[1].value;
        const size_val = inst.operands[2].value;

        // Load size to rcx (rep count)
        try self.loadToRcx(size_val);

        // Load src pointer to rsi
        const src_offset = try self.getStackOffset(src_val);
        if (self.isIndirect(src_val)) {
            try self.enc.movRsiRbpOffset(src_offset);
        } else {
            try self.enc.leaRsiRbpOffset(src_offset);
        }

        // Load dest pointer to rdi
        const dest_offset = try self.getStackOffset(dest_val);
        if (self.isIndirect(dest_val)) {
            try self.enc.movRdiRbpOffset(dest_offset);
        } else {
            try self.enc.leaRdiRbpOffset(dest_offset);
        }

        // rep movsb: copy rcx bytes from [rsi] to [rdi]
        try self.enc.repMovsb();
    }

    fn genMemset(self: *IrCodegen, inst: ir.Instruction) !void {
        const dest_val = inst.operands[0].value;
        const byte_val: u8 = @intCast(inst.operands[1].immediate_i32);
        const size: i32 = inst.operands[2].immediate_i32;

        // Load dest pointer to r11 (caller-saved, safe to clobber)
        const dest_offset = try self.getStackOffset(dest_val);
        if (self.isIndirect(dest_val)) {
            try self.enc.movR11RbpOffset(dest_offset);
        } else {
            try self.enc.leaR11RbpOffset(dest_offset);
        }

        // For byte_val = 0, use xor rax, rax; otherwise mov rax, val*0x0101010101010101
        if (byte_val == 0) {
            try self.enc.emit(&.{ 0x48, 0x31, 0xC0 }); // xor rax, rax
        } else {
            // Fill rax with the byte value repeated
            const fill_val: u64 = @as(u64, byte_val) * 0x0101010101010101;
            try self.enc.emit(&.{ 0x48, 0xB8 }); // mov rax, imm64
            try self.enc.emit(&@as([8]u8, @bitCast(fill_val)));
        }

        // Store 8 bytes at a time using r11 (dest)
        var stored: i32 = 0;
        while (stored < size) : (stored += 8) {
            try self.enc.movMemR11OffsetRax(stored);
        }
    }

    fn genLoad(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const ptr = inst.operands[0].value;
        const offset = try self.getStackOffset(ptr);
        // Prefer the type that was actually stored at this location, otherwise use IR type
        const ty = self.stored_types.get(ptr) orelse inst.result_type;

        debug.codegen("  load: result=%{d}, ptr=%{d}, offset={d}, type={s}, indirect={}", .{ result, ptr, offset, ty.toIrName(), self.isIndirect(ptr) });

        if (self.isIndirect(ptr)) {
            try self.enc.movRcxRbpOffset(offset);
            if (ty == .f64) {
                try self.enc.movsdXmm0MemRcx();
            } else if (ty == .i8) {
                // Load 1 byte with zero extension: movzx rax, byte [rcx]
                try self.enc.emit(&.{ 0x48, 0x0F, 0xB6, 0x01 }); // movzx rax, byte [rcx]
                debug.codegen("    -> indirect i8 load: movzx rax, byte [rcx]", .{});
            } else if (ty == .i32) {
                // Load 4 bytes with sign extension: movsxd rax, [rcx]
                try self.enc.emit(&.{ 0x48, 0x63, 0x01 }); // movsxd rax, [rcx]
                debug.codegen("    -> indirect i32 load: movsxd rax, [rcx]", .{});
            } else {
                try self.enc.movRaxMem(.rcx);
                debug.codegen("    -> indirect ptr load: mov rax, [rcx]", .{});
            }
        } else {
            if (ty == .f64) {
                try self.enc.movsdXmm0RbpOffset(offset);
            } else if (ty == .i8) {
                // Load 1 byte with zero extension: movzx rax, byte [rbp+offset]
                const off_bytes: [4]u8 = @bitCast(offset);
                try self.enc.emit(&.{ 0x48, 0x0F, 0xB6, 0x85 }); // movzx rax, byte [rbp+disp32]
                try self.enc.emit(&off_bytes);
                debug.codegen("    -> direct i8 load: movzx rax, byte [rbp+{d}]", .{offset});
            } else if (ty == .i32) {
                // Load 4 bytes with sign extension: movsxd rax, [rbp+offset]
                const off_bytes: [4]u8 = @bitCast(offset);
                try self.enc.emit(&.{ 0x48, 0x63, 0x85 }); // movsxd rax, [rbp+disp32]
                try self.enc.emit(&off_bytes);
                debug.codegen("    -> direct i32 load: movsxd rax, [rbp+{d}]", .{offset});
            } else {
                try self.enc.movRaxRbpOffset(offset);
                debug.codegen("    -> direct load: mov rax, [rbp+{d}]", .{offset});
            }
        }
        const result_offset = self.allocStackSlots(1);
        debug.codegen("    -> storing result to stack offset {d}", .{result_offset});
        if (ty == .f64) {
            try self.enc.movsdRbpOffsetXmm0(result_offset);
        } else {
            try self.enc.movRbpOffsetRax(result_offset);
        }
        try self.setValueLocation(result, .{ .stack = result_offset }, ty);
        // When loading a pointer, mark result as indirect because the loaded value
        // is itself a pointer that points elsewhere (e.g., heap memory)
        if (ty == .ptr) {
            try self.markIndirect(result);
        }
    }

    fn genIntBinaryOp(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const result_type = inst.result_type;

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
            .band => try self.enc.andRaxRcx(),
            .bitor => try self.enc.orRaxRcx(),
            .bxor => try self.enc.xorRaxRcx(),
            .shl => try self.enc.shlRaxCl(),
            .shr => try self.enc.sarRaxCl(),
            else => unreachable,
        }

        try self.storeToStack(result, result_type);
        // If the result is a pointer (e.g., from pointer arithmetic), inherit indirectness from operand
        // A direct stack pointer + offset = direct stack pointer
        // An indirect heap pointer + offset = indirect heap pointer
        if (result_type == .ptr) {
            // Check if either operand is marked indirect (the pointer operand)
            const lhs = inst.operands[0].value;
            if (self.isIndirect(lhs)) {
                try self.markIndirect(result);
            }
        }
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

    fn genFabs(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Clear sign bit: AND with 0x7FFFFFFFFFFFFFFF
        try self.enc.fabsXmm0();
        try self.storeToStack(inst.result.?, .f64);
    }

    fn genBitcastF64ToI64(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load f64 value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Move bits from xmm0 to rax using movq
        try self.enc.movqRaxXmm0();
        // Store as i64
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genSextI32I64(self: *IrCodegen, inst: ir.Instruction) !void {
        // Sign-extend i32 to i64: movsxd rax, eax
        try self.loadToRax(inst.operands[0].value);
        // The value is already in rax as i64 due to 64-bit load, but we need to sign-extend
        // movsxd rax, eax (sign-extend lower 32 bits to 64 bits)
        try self.enc.movsxdRaxEax();
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genTruncI64I32(self: *IrCodegen, inst: ir.Instruction) !void {
        // Truncate i64 to i32: just use lower 32 bits
        try self.loadToRax(inst.operands[0].value);
        // Store as i32 (lower 32 bits)
        try self.storeToStack(inst.result.?, .i32);
    }

    fn genTruncI64I8(self: *IrCodegen, inst: ir.Instruction) !void {
        // Truncate i64 to i8: just use lower 8 bits
        try self.loadToRax(inst.operands[0].value);
        // Store as i8 (lower 8 bits)
        try self.storeToStack(inst.result.?, .i8);
    }

    fn genZextI8I64(self: *IrCodegen, inst: ir.Instruction) !void {
        // Zero-extend i8 (byte) to i64: movzx rax, al
        try self.loadToRax(inst.operands[0].value);
        // movzx rax, al (zero-extend byte to 64 bits)
        try self.enc.movzxRaxAl();
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genRet(self: *IrCodegen, inst: ir.Instruction) !void {
        if (inst.operands[0] != .none) {
            const ret_val = inst.operands[0].value;
            const ret_type = self.value_types.get(ret_val) orelse return error.ValueTypeNotFound;

            debug.codegen("  ret: val=%{d}, type={s}, func={s}", .{ ret_val, ret_type.toIrName(), self.current_func_name });

            if (self.reg_alloc.has_hidden_ret_ptr) {
                // Large struct return: copy result to caller's buffer via hidden pointer
                // The hidden pointer was saved in prologue at hidden_ret_ptr_offset
                //
                // For now, this handles single-value returns by storing through the pointer.
                // When struct types with size info are added, this should use memcpy for
                // multi-slot structs.
                //
                // Load hidden pointer into a scratch register (R10)
                try self.enc.movR10RbpOffset(self.reg_alloc.hidden_ret_ptr_offset);
                // Load return value into RAX
                try self.loadToRax(ret_val);
                // Store through hidden pointer: mov [r10], rax
                try self.enc.movMemRax(.r10);
                // Return the pointer in RAX (as per Windows x64 ABI for struct returns)
                try self.enc.emit(&.{ 0x4C, 0x89, 0xD0 }); // mov rax, r10
            } else if (ret_type == .f64) {
                try self.loadToXmm(ret_val, .xmm0);
            } else {
                try self.loadToRax(ret_val);
            }
        }

        // Restore callee-saved registers before epilogue
        // XMMs are loaded from stack, GPRs are popped in reverse order
        try self.emitCalleeSavedRestores();

        if (std.mem.eql(u8, self.current_func_name, "main") and !self.track_allocs) {
            // Exit code in RCX for ExitProcess (when not using _start wrapper)
            try self.enc.movRcxRax();
            // call [rip+0] - patched by PE writer for ExitProcess
            try self.enc.emit(&.{ 0xFF, 0x15, 0, 0, 0, 0 });
        } else {
            // Normal return - _start will handle ExitProcess when tracking
            try self.enc.epilogue();
        }
    }

    fn genParam(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        var param_idx = inst.operands[0].immediate_i32;
        const ty = inst.result_type;
        const offset = self.allocStackSlots(1);

        // If function has hidden return pointer, shift parameter indices
        // Windows x64 ABI: RCX has hidden ptr, RDX has param 0, R8 has param 1, etc.
        if (self.reg_alloc.has_hidden_ret_ptr) {
            param_idx += 1;
        }

        if (param_idx < 4) {
            // First 4 parameters come in registers
            if (ty == .f64) {
                // Float param in XMM register - store to stack
                const xmm_modrm: u8 = switch (param_idx) {
                    0 => 0x45, // xmm0
                    1 => 0x4D, // xmm1
                    2 => 0x55, // xmm2
                    3 => 0x5D, // xmm3
                    else => unreachable,
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
                    else => unreachable,
                }
                try self.enc.movRbpOffsetRax(offset);
                try self.setValueLocation(result, .{ .stack = offset }, ty);

                if (ty == .ptr) {
                    try self.markIndirect(result);
                }
            }
        } else {
            // Parameters 5+ come from the caller's stack frame
            // Located at [RBP + 16 + 32 + (param_idx - 4) * 8]
            // 16 = return address (8) + saved RBP (8)
            // 32 = shadow space (Windows x64 ABI)
            const stack_param_offset: i32 = 16 + 32 + @as(i32, @intCast((param_idx - 4) * 8));

            if (ty == .f64) {
                // Load float from caller's stack to xmm0, then store to our local slot
                // movsd xmm0, [rbp+stack_param_offset]
                try self.enc.movsdXmm0RbpOffset(stack_param_offset);
                // movsd [rbp+offset], xmm0
                try self.enc.movsdRbpOffsetXmm0(offset);
                try self.setValueLocation(result, .{ .stack = offset }, .f64);
            } else {
                // Load integer/pointer from caller's stack frame to our local slot
                try self.enc.movRaxRbpOffset(stack_param_offset);
                try self.enc.movRbpOffsetRax(offset);
                try self.setValueLocation(result, .{ .stack = offset }, ty);

                if (ty == .ptr) {
                    try self.markIndirect(result);
                }
            }
        }
    }

    fn genCall(self: *IrCodegen, inst: ir.Instruction) !void {
        const func_name = inst.operands[0].func_name;
        const args = inst.operands[1].call_args;
        // Use the actual function's return type from the module if available
        const ret_type = self.func_return_types.get(func_name) orelse inst.result_type;

        debug.codegen("  Calling {s} with {d} args", .{ func_name, args.len });

        // Check if called function returns large struct requiring hidden pointer
        const callee_has_hidden_ret = hasHiddenReturnPointer(ret_type);

        if (callee_has_hidden_ret) {
            // Allocate stack space for result buffer
            // For now, allocate 1 slot (8 bytes). When struct types with size info
            // are added, this should allocate based on the struct size.
            const result_offset = self.allocStackSlots(1);

            // Pass pointer to result space as first argument (RCX)
            try self.enc.leaRcxRbpOffset(result_offset);

            // Load remaining args shifted by 1 (RDX gets arg 0, R8 gets arg 1, etc.)
            try self.loadArgsShifted(args, 1, true);

            try self.emitInternalCall(func_name);

            // Result is in the stack buffer we allocated
            // RAX contains the pointer to it (as per ABI), but we already know where it is
            if (inst.result) |result| {
                debug.codegen("  call result (hidden ptr): %{d} of type {s}", .{ result, ret_type.toIrName() });
                // The result is already at result_offset on the stack
                try self.setValueLocation(result, .{ .stack = result_offset }, ret_type);
                if (ret_type == .ptr) {
                    try self.markIndirect(result);
                }
            }
        } else {
            // Normal call without hidden return pointer
            try self.loadArgs(args, true);
            try self.emitInternalCall(func_name);

            if (inst.result) |result| {
                debug.codegen("  call result: %{d} of type {s}", .{ result, ret_type.toIrName() });
                try self.storeReturnValue(result, ret_type);
            }
        }
    }

    /// Load arguments into registers (first 4) and stack (5+).
    /// use_lea controls whether direct pointers use LEA for the first 4 args.
    /// Windows x64 ABI: RCX, RDX, R8, R9 for first 4 integer/pointer args
    /// XMM0-XMM3 for first 4 float args (same position as corresponding GPR)
    /// Args 5+ go on stack at [RSP + 32 + (i-4)*8] after shadow space allocation
    fn loadArgs(self: *IrCodegen, args: []const ir.Value, use_lea: bool) !void {
        for (args, 0..) |arg, i| {
            const arg_type = self.value_types.get(arg) orelse return error.ValueTypeNotFound;

            if (i < 4) {
                // First 4 args go in registers
                if (arg_type == .f64) {
                    const xmm: x86.Xmm = switch (i) {
                        0 => .xmm0,
                        1 => .xmm1,
                        2 => .xmm2,
                        3 => .xmm3,
                        else => unreachable,
                    };
                    try self.loadToXmm(arg, xmm);
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
            } else {
                // Args 5+ go on stack. We write at [RSP + (i-4)*8] BEFORE shadow
                // space allocation (sub rsp, 32). After allocation, these will be
                // at [RSP + 32 + (i-4)*8] which is where the callee expects them.
                const stack_offset: i32 = @as(i32, @intCast((i - 4) * 8));

                if (arg_type == .f64) {
                    try self.loadToXmm(arg, .xmm0);
                    // movsd [rsp+offset], xmm0
                    try self.enc.movsdRspOffsetXmm0(stack_offset);
                } else if (use_lea and arg_type == .ptr and !self.isIndirect(arg)) {
                    // LEA for stack argument - load address to rax then store to stack
                    const loc = self.value_locations.get(arg) orelse return error.ValueNotFound;
                    const bp_offset = switch (loc) {
                        .stack => |o| o,
                        else => return error.UnsupportedArgumentLocation,
                    };
                    try self.enc.leaRaxRbpOffset(bp_offset);
                    try self.enc.movRspOffsetRax(stack_offset);
                } else {
                    try self.loadToRax(arg);
                    // mov [rsp+offset], rax
                    try self.enc.movRspOffsetRax(stack_offset);
                }
            }
        }
    }

    /// Load arguments into registers with an offset (for hidden return pointer calls).
    /// Similar to loadArgs but shifts register assignments by `shift` positions.
    /// When shift=1: arg 0 goes to RDX (position 1), arg 1 goes to R8 (position 2), etc.
    /// This is used when RCX is occupied by the hidden return pointer.
    fn loadArgsShifted(self: *IrCodegen, args: []const ir.Value, shift: usize, use_lea: bool) !void {
        for (args, 0..) |arg, i| {
            const arg_type = self.value_types.get(arg) orelse return error.ValueTypeNotFound;
            const shifted_idx = i + shift;

            if (shifted_idx < 4) {
                // Args that fit in registers (after shift)
                if (arg_type == .f64) {
                    const xmm: x86.Xmm = switch (shifted_idx) {
                        0 => .xmm0,
                        1 => .xmm1,
                        2 => .xmm2,
                        3 => .xmm3,
                        else => unreachable,
                    };
                    try self.loadToXmm(arg, xmm);
                } else if (use_lea and arg_type == .ptr and !self.isIndirect(arg)) {
                    // LEA - get pointer to struct on stack
                    const loc = self.value_locations.get(arg) orelse return error.ValueNotFound;
                    const offset = switch (loc) {
                        .stack => |o| o,
                        else => return error.UnsupportedArgumentLocation,
                    };
                    try self.emitLeaArgReg(shifted_idx, offset);
                } else {
                    try self.loadToRax(arg);
                    try self.movArgRegRax(shifted_idx);
                }
            } else {
                // Args that go on stack (shifted_idx >= 4)
                // Stack offset is based on shifted index, not original
                const stack_offset: i32 = @as(i32, @intCast((shifted_idx - 4) * 8));

                if (arg_type == .f64) {
                    try self.loadToXmm(arg, .xmm0);
                    try self.enc.movsdRspOffsetXmm0(stack_offset);
                } else if (use_lea and arg_type == .ptr and !self.isIndirect(arg)) {
                    const loc = self.value_locations.get(arg) orelse return error.ValueNotFound;
                    const bp_offset = switch (loc) {
                        .stack => |o| o,
                        else => return error.UnsupportedArgumentLocation,
                    };
                    try self.enc.leaRaxRbpOffset(bp_offset);
                    try self.enc.movRspOffsetRax(stack_offset);
                } else {
                    try self.loadToRax(arg);
                    try self.enc.movRspOffsetRax(stack_offset);
                }
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

    // ============================================================================
    // Call Helpers (Windows x64 Calling Convention)
    // ============================================================================
    //
    // Windows x64 Calling Convention:
    // - First 4 args: RCX, RDX, R8, R9
    // - Args 5+: Stack at [rsp+0x20], [rsp+0x28], etc. (after 32-byte shadow space)
    // - 32-byte shadow space is ALWAYS required, even for <4 args
    // - Stack must be 16-byte aligned at the call instruction
    //
    // IMPORTANT: Stack parameters must be set AFTER allocating stack space!
    //
    // Simple calls (≤4 params, no stack params needed after call setup):
    //   emitInternalCall(func_name)     - for Maxon functions
    //   emitExternalCall(dll, func)     - for DLL functions
    //
    // Complex calls (5+ params OR need to set params after stack allocation):
    //   beginCall(num_stack_params)     - allocate shadow + stack space
    //   // set up RCX, RDX, R8, R9
    //   emitStackParam(0, value)        - set 5th param at [rsp+0x20]
    //   emitInternalCallAfterSetup(func) OR emitExternalCallAfterSetup(dll, func)
    //   endCall()                       - restore stack
    // ============================================================================

    /// Allocate stack space for a call (shadow space + stack parameters)
    /// Use this when you need to set up parameters AFTER allocation (e.g., 5+ params)
    /// Call endCall() after the call to restore the stack
    fn beginCall(self: *IrCodegen, num_stack_params: u32) !void {
        // Shadow space (32) + stack params (8 each), aligned to 16 bytes
        const total_space = 32 + num_stack_params * 8;
        const aligned_space = (total_space + 15) & ~@as(u32, 15);

        if (aligned_space <= 127) {
            try self.enc.emit(&.{ 0x48, 0x83, 0xEC, @intCast(aligned_space) });
        } else {
            try self.enc.emit(&.{ 0x48, 0x81, 0xEC });
            try self.enc.emitI32(@intCast(aligned_space));
        }
        self.pending_stack_space = aligned_space;
    }

    /// Restore stack after beginCall()
    fn endCall(self: *IrCodegen) !void {
        const space = self.pending_stack_space;
        if (space <= 127) {
            try self.enc.emit(&.{ 0x48, 0x83, 0xC4, @intCast(space) });
        } else {
            try self.enc.emit(&.{ 0x48, 0x81, 0xC4 });
            try self.enc.emitI32(@intCast(space));
        }
        self.pending_stack_space = 0;
    }

    /// Emit a stack parameter for calls with 5+ arguments
    /// param_index: 0 = 5th param at [rsp+0x20], 1 = 6th param at [rsp+0x28], etc.
    fn emitStackParam(self: *IrCodegen, param_index: u32, value: i64) !void {
        const offset: u8 = @intCast(0x20 + param_index * 8);
        if (value == 0) {
            try self.enc.emit(&.{ 0x48, 0xC7, 0x44, 0x24, offset, 0x00, 0x00, 0x00, 0x00 });
        } else {
            try self.enc.emit(&.{ 0x48, 0xC7, 0x44, 0x24, offset });
            try self.enc.emitI32(@intCast(value));
        }
    }

    // --- Simple call helpers (handle shadow space internally) ---

    /// Simple internal call for Maxon functions with ≤4 parameters
    /// Caller must set up RCX, RDX, R8, R9 BEFORE calling this
    fn emitInternalCall(self: *IrCodegen, func_name: []const u8) !void {
        try self.enc.allocShadowSpace();
        const patch_offset = try self.enc.callRel32();
        try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = func_name });
        try self.enc.freeShadowSpace();
    }

    /// Simple external call for DLL functions with ≤4 parameters
    /// Caller must set up RCX, RDX, R8, R9 BEFORE calling this
    fn emitExternalCall(self: *IrCodegen, dll: []const u8, func_name: []const u8) !void {
        try self.enc.allocShadowSpace();
        const patch_offset = try self.enc.callIndirectRip();
        try self.external_patches.append(self.allocator, .{ .offset = patch_offset, .dll = dll, .func_name = func_name });
        try self.enc.freeShadowSpace();
    }

    // --- Call-after-setup helpers (use after beginCall) ---

    /// Emit internal call after beginCall() - for Maxon functions
    fn emitInternalCallAfterSetup(self: *IrCodegen, func_name: []const u8) !void {
        const patch_offset = try self.enc.callRel32();
        try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = func_name });
    }

    /// Emit external call after beginCall() - for DLL functions
    fn emitExternalCallAfterSetup(self: *IrCodegen, dll: []const u8, func_name: []const u8) !void {
        const patch_offset = try self.enc.callIndirectRip();
        try self.external_patches.append(self.allocator, .{ .offset = patch_offset, .dll = dll, .func_name = func_name });
    }

    // Legacy alias
    const beginExternalCall = beginCall;
    const endExternalCall = endCall;

    fn genHeapAlloc(self: *IrCodegen, inst: ir.Instruction) !void {
        const size_val = inst.operands[0].value;
        debug.codegen("  HeapAlloc: size=%{d}", .{size_val});

        // Save size to R12 (callee-saved)
        try self.loadToRax(size_val);
        try self.enc.movR12Rax();

        // Check for zero-size allocation - skip allocation and return null
        // test r12, r12
        try self.enc.emit(&.{ 0x4D, 0x85, 0xE4 });
        // jnz do_alloc (jump if size != 0)
        const zero_check_jnz = try self.enc.jneRel32();

        // Size is -set result to null and skip allocation
        try self.enc.emit(&.{ 0x48, 0x31, 0xC0 }); // xor rax, rax (null pointer)
        const skip_alloc_jmp = try self.enc.jmpRel32(); // jump to after allocation

        // Patch the jnz to jump here (start of actual allocation)
        const do_alloc_pos = self.code.items.len;
        const zero_check_rel: i32 = @intCast(do_alloc_pos - zero_check_jnz - 4);
        self.code.items[zero_check_jnz] = @truncate(@as(u32, @bitCast(zero_check_rel)));
        self.code.items[zero_check_jnz + 1] = @truncate(@as(u32, @bitCast(zero_check_rel)) >> 8);
        self.code.items[zero_check_jnz + 2] = @truncate(@as(u32, @bitCast(zero_check_rel)) >> 16);
        self.code.items[zero_check_jnz + 3] = @truncate(@as(u32, @bitCast(zero_check_rel)) >> 24);

        try self.emitExternalCall("kernel32.dll", "GetProcessHeap");

        // HeapAlloc(hHeap=RAX, dwFlags=0, dwBytes=size)
        try self.enc.movRcxRax();
        try self.enc.xorRdxRdx();
        try self.enc.movR8R12();

        try self.emitExternalCall("kernel32.dll", "HeapAlloc");

        // If tracking enabled, call __track_alloc(ptr=RCX, size=RDX, tag_ptr=R8, tag_len=R9)
        if (self.track_allocs) {
            // Save ptr to R13 across the tracking call
            try self.enc.emit(&.{ 0x49, 0x89, 0xC5 }); // mov r13, rax

            // Embed tag string "dynamic array" after a jump
            const tag = "dynamic array";
            const jmp_offset = try self.enc.jmpRel32();
            const tag_pos = self.code.items.len;
            try self.code.appendSlice(self.allocator, tag);
            const after_tag = self.code.items.len;
            // Patch jump
            const rel: i32 = @intCast(after_tag - jmp_offset - 4);
            self.code.items[jmp_offset] = @bitCast(@as(i8, @intCast(rel & 0xFF)));
            self.code.items[jmp_offset + 1] = @bitCast(@as(i8, @intCast((rel >> 8) & 0xFF)));
            self.code.items[jmp_offset + 2] = @bitCast(@as(i8, @intCast((rel >> 16) & 0xFF)));
            self.code.items[jmp_offset + 3] = @bitCast(@as(i8, @intCast((rel >> 24) & 0xFF)));

            // LEA R8, [RIP - offset_to_tag]
            const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
            try self.enc.emit(&.{ 0x4C, 0x8D, 0x05 }); // lea r8, [rip+disp32]
            try self.enc.emitI32(rip_offset);

            // mov r9, tag_len
            try self.enc.emit(&.{ 0x49, 0xC7, 0xC1 }); // mov r9, imm32
            try self.enc.emitI32(@intCast(tag.len));

            try self.enc.emit(&.{ 0x4C, 0x89, 0xE9 }); // mov rcx, r13 (ptr)
            try self.enc.emit(&.{ 0x4C, 0x89, 0xE2 }); // mov rdx, r12 (size)
            try self.emitInternalCall("__track_alloc");
            // Restore ptr from R13 to RAX
            try self.enc.emit(&.{ 0x4C, 0x89, 0xE8 }); // mov rax, r13
        }

        // Patch the skip_alloc_jmp to jump here (after allocation, before store)
        const after_alloc_pos = self.code.items.len;
        const skip_alloc_rel: i32 = @intCast(after_alloc_pos - skip_alloc_jmp - 4);
        self.code.items[skip_alloc_jmp] = @truncate(@as(u32, @bitCast(skip_alloc_rel)));
        self.code.items[skip_alloc_jmp + 1] = @truncate(@as(u32, @bitCast(skip_alloc_rel)) >> 8);
        self.code.items[skip_alloc_jmp + 2] = @truncate(@as(u32, @bitCast(skip_alloc_rel)) >> 16);
        self.code.items[skip_alloc_jmp + 3] = @truncate(@as(u32, @bitCast(skip_alloc_rel)) >> 24);

        try self.storeToStack(inst.result.?, .ptr);
        try self.markIndirect(inst.result.?);
    }

    fn genHeapFree(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr_val = inst.operands[0].value;
        debug.codegen("  HeapFree: ptr=%{d}", .{ptr_val});

        // Save ptr to R12
        try self.loadToRax(ptr_val);
        try self.enc.movR12Rax();

        // Check for null pointer - skip free if null (zero-size allocation)
        // test r12, r12
        try self.enc.emit(&.{ 0x4D, 0x85, 0xE4 });
        // jnz do_free (jump if ptr != null)
        const null_check_jnz = try self.enc.jneRel32();
        // Ptr is null - skip to end
        const skip_free_jmp = try self.enc.jmpRel32();

        // Patch the jnz to jump here (start of actual free)
        const do_free_pos = self.code.items.len;
        const null_check_rel: i32 = @intCast(do_free_pos - null_check_jnz - 4);
        self.code.items[null_check_jnz] = @truncate(@as(u32, @bitCast(null_check_rel)));
        self.code.items[null_check_jnz + 1] = @truncate(@as(u32, @bitCast(null_check_rel)) >> 8);
        self.code.items[null_check_jnz + 2] = @truncate(@as(u32, @bitCast(null_check_rel)) >> 16);
        self.code.items[null_check_jnz + 3] = @truncate(@as(u32, @bitCast(null_check_rel)) >> 24);

        // If tracking enabled, get size via HeapSize and call __track_free
        if (self.track_allocs) {
            // Get heap handle first
            try self.emitExternalCall("kernel32.dll", "GetProcessHeap");
            try self.enc.emit(&.{ 0x49, 0x89, 0xC5 }); // mov r13, rax (heap handle)

            // HeapSize(hHeap=R13, dwFlags=0, lpMem=R12) -> returns size in RAX
            try self.enc.emit(&.{ 0x4C, 0x89, 0xE9 }); // mov rcx, r13 (heap)
            try self.enc.xorRdxRdx(); // flags = 0
            try self.enc.emit(&.{ 0x4C, 0x89, 0xE0 }); // mov rax, r12
            try self.enc.emit(&.{ 0x49, 0x89, 0xC0 }); // mov r8, rax (ptr)
            try self.emitExternalCall("kernel32.dll", "HeapSize");

            // Save size to R14
            try self.enc.emit(&.{ 0x49, 0x89, 0xC6 }); // mov r14, rax (size)

            // Embed tag string "dynamic array" after a jump
            const tag = "dynamic array";
            const jmp_offset = try self.enc.jmpRel32();
            const tag_pos = self.code.items.len;
            try self.code.appendSlice(self.allocator, tag);
            const after_tag = self.code.items.len;
            // Patch jump
            const rel: i32 = @intCast(after_tag - jmp_offset - 4);
            self.code.items[jmp_offset] = @bitCast(@as(i8, @intCast(rel & 0xFF)));
            self.code.items[jmp_offset + 1] = @bitCast(@as(i8, @intCast((rel >> 8) & 0xFF)));
            self.code.items[jmp_offset + 2] = @bitCast(@as(i8, @intCast((rel >> 16) & 0xFF)));
            self.code.items[jmp_offset + 3] = @bitCast(@as(i8, @intCast((rel >> 24) & 0xFF)));

            // LEA R8, [RIP - offset_to_tag]
            const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
            try self.enc.emit(&.{ 0x4C, 0x8D, 0x05 }); // lea r8, [rip+disp32]
            try self.enc.emitI32(rip_offset);

            // mov r9, tag_len
            try self.enc.emit(&.{ 0x49, 0xC7, 0xC1 }); // mov r9, imm32
            try self.enc.emitI32(@intCast(tag.len));

            // Call __track_free(ptr=RCX, size=RDX, tag_ptr=R8, tag_len=R9)
            try self.enc.emit(&.{ 0x4C, 0x89, 0xE1 }); // mov rcx, r12 (ptr)
            try self.enc.emit(&.{ 0x4C, 0x89, 0xF2 }); // mov rdx, r14 (size)
            try self.emitInternalCall("__track_free");
        }

        try self.emitExternalCall("kernel32.dll", "GetProcessHeap");

        // HeapFree(hHeap=RAX, dwFlags=0, lpMem=ptr)
        try self.enc.movRcxRax();
        try self.enc.xorRdxRdx();
        try self.enc.movR8R12();

        try self.emitExternalCall("kernel32.dll", "HeapFree");

        // Patch skip_free_jmp to jump here (after free)
        const after_free_pos = self.code.items.len;
        const skip_free_rel: i32 = @intCast(after_free_pos - skip_free_jmp - 4);
        self.code.items[skip_free_jmp] = @truncate(@as(u32, @bitCast(skip_free_rel)));
        self.code.items[skip_free_jmp + 1] = @truncate(@as(u32, @bitCast(skip_free_rel)) >> 8);
        self.code.items[skip_free_jmp + 2] = @truncate(@as(u32, @bitCast(skip_free_rel)) >> 16);
        self.code.items[skip_free_jmp + 3] = @truncate(@as(u32, @bitCast(skip_free_rel)) >> 24);
    }

    fn genHeapRealloc(self: *IrCodegen, inst: ir.Instruction) !void {
        const old_ptr = inst.operands[0].value;
        const new_size = inst.operands[1].value;
        debug.codegen("  HeapRealloc: old_ptr=%{d}, new_size=%{d}", .{ old_ptr, new_size });

        // Save old_ptr to R12, new_size to R13 (callee-saved)
        try self.loadToRax(old_ptr);
        try self.enc.movR12Rax();
        try self.loadToRax(new_size);
        try self.enc.emit(&.{ 0x49, 0x89, 0xC5 }); // mov r13, rax (new_size)

        // Check if old_ptr is NULL - if so, use HeapAlloc instead
        // test r12, r12
        try self.enc.emit(&.{ 0x4D, 0x85, 0xE4 });
        // jnz do_realloc (jump if old_ptr != NULL)
        const null_check_jnz = try self.enc.jneRel32();

        // === NULL PATH: old_ptr is NULL - call HeapAlloc(hHeap, 0, new_size) ===
        try self.emitExternalCall("kernel32.dll", "GetProcessHeap");
        try self.enc.movRcxRax(); // hHeap
        try self.enc.xorRdxRdx(); // dwFlags = 0
        try self.enc.emit(&.{ 0x4D, 0x89, 0xE8 }); // mov r8, r13 (dwBytes = new_size)
        try self.emitExternalCall("kernel32.dll", "HeapAlloc");
        // RAX = new_ptr, R13 = new_size

        // If tracking enabled, call __track_alloc for this new allocation
        if (self.track_allocs) {
            // Save ptr to R12 across the tracking call
            try self.enc.movR12Rax();

            // Embed tag string "dynamic array" after a jump
            const tag = "dynamic array";
            const jmp_offset = try self.enc.jmpRel32();
            const tag_pos = self.code.items.len;
            try self.code.appendSlice(self.allocator, tag);
            const after_tag = self.code.items.len;
            // Patch jump
            const jmp_rel: i32 = @intCast(after_tag - jmp_offset - 4);
            self.code.items[jmp_offset] = @bitCast(@as(i8, @intCast(jmp_rel & 0xFF)));
            self.code.items[jmp_offset + 1] = @bitCast(@as(i8, @intCast((jmp_rel >> 8) & 0xFF)));
            self.code.items[jmp_offset + 2] = @bitCast(@as(i8, @intCast((jmp_rel >> 16) & 0xFF)));
            self.code.items[jmp_offset + 3] = @bitCast(@as(i8, @intCast((jmp_rel >> 24) & 0xFF)));

            // LEA R8, [RIP - offset_to_tag]
            const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
            try self.enc.emit(&.{ 0x4C, 0x8D, 0x05 }); // lea r8, [rip+disp32]
            try self.enc.emitI32(rip_offset);

            // mov r9, tag_len
            try self.enc.emit(&.{ 0x49, 0xC7, 0xC1 }); // mov r9, imm32
            try self.enc.emitI32(@intCast(tag.len));

            try self.enc.emit(&.{ 0x4C, 0x89, 0xE1 }); // mov rcx, r12 (ptr)
            try self.enc.emit(&.{ 0x4C, 0x89, 0xEA }); // mov rdx, r13 (size)
            try self.emitInternalCall("__track_alloc");
            // Restore ptr from R12 to RAX
            try self.enc.emit(&.{ 0x4C, 0x89, 0xE0 }); // mov rax, r12
        }

        // Result is in RAX, jump to end
        const skip_realloc_jmp = try self.enc.jmpRel32();

        // Patch the jnz to jump here (start of realloc path)
        const do_realloc_pos = self.code.items.len;
        const null_check_rel: i32 = @intCast(do_realloc_pos - null_check_jnz - 4);
        self.code.items[null_check_jnz] = @truncate(@as(u32, @bitCast(null_check_rel)));
        self.code.items[null_check_jnz + 1] = @truncate(@as(u32, @bitCast(null_check_rel)) >> 8);
        self.code.items[null_check_jnz + 2] = @truncate(@as(u32, @bitCast(null_check_rel)) >> 16);
        self.code.items[null_check_jnz + 3] = @truncate(@as(u32, @bitCast(null_check_rel)) >> 24);

        // === REALLOC PATH: old_ptr is not NULL ===
        // If tracking, get old_size first (before realloc invalidates old_ptr)
        if (self.track_allocs) {
            // Get heap handle
            try self.emitExternalCall("kernel32.dll", "GetProcessHeap");
            try self.enc.emit(&.{ 0x49, 0x89, 0xC6 }); // mov r14, rax (save heap handle)

            // HeapSize to get old size
            try self.enc.emit(&.{ 0x4C, 0x89, 0xF1 }); // mov rcx, r14 (heap)
            try self.enc.xorRdxRdx(); // flags = 0
            try self.enc.movR8R12(); // ptr
            try self.emitExternalCall("kernel32.dll", "HeapSize");
            // RAX now has old size, save to R15
            try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (old size)

            // Now do the actual realloc - restore heap handle
            try self.enc.emit(&.{ 0x4C, 0x89, 0xF1 }); // mov rcx, r14 (hHeap)
        } else {
            // Get process heap
            try self.emitExternalCall("kernel32.dll", "GetProcessHeap");
            try self.enc.movRcxRax(); // hHeap
        }

        // HeapReAlloc(hHeap=RCX, dwFlags=0, lpMem=R12, dwBytes=R13)
        try self.enc.xorRdxRdx(); // dwFlags = 0
        try self.enc.movR8R12(); // lpMem (old pointer)
        try self.enc.emit(&.{ 0x4D, 0x89, 0xE9 }); // mov r9, r13 (dwBytes = new size)

        try self.emitExternalCall("kernel32.dll", "HeapReAlloc");
        // RAX = new_ptr. Save to R14 (reuse, heap handle no longer needed)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC6 }); // mov r14, rax (new_ptr)

        // If tracking, call __track_realloc(old_ptr, old_size, new_ptr, new_size, tag_ptr, tag_len)
        // R12 = old_ptr, R15 = old_size, R14 = new_ptr, R13 = new_size
        if (self.track_allocs) {
            // Embed tag string "dynamic array" after a jump
            const tag = "dynamic array";
            const jmp_offset = try self.enc.jmpRel32();
            const tag_pos = self.code.items.len;
            try self.code.appendSlice(self.allocator, tag);
            const after_tag = self.code.items.len;
            // Patch jump
            const rel: i32 = @intCast(after_tag - jmp_offset - 4);
            self.code.items[jmp_offset] = @bitCast(@as(i8, @intCast(rel & 0xFF)));
            self.code.items[jmp_offset + 1] = @bitCast(@as(i8, @intCast((rel >> 8) & 0xFF)));
            self.code.items[jmp_offset + 2] = @bitCast(@as(i8, @intCast((rel >> 16) & 0xFF)));
            self.code.items[jmp_offset + 3] = @bitCast(@as(i8, @intCast((rel >> 24) & 0xFF)));

            // Set up args: RCX=old_ptr, RDX=old_size, R8=new_ptr, R9=new_size
            try self.enc.emit(&.{ 0x4C, 0x89, 0xE1 }); // mov rcx, r12 (old_ptr)
            try self.enc.emit(&.{ 0x4C, 0x89, 0xFA }); // mov rdx, r15 (old_size)
            try self.enc.emit(&.{ 0x4D, 0x89, 0xF0 }); // mov r8, r14 (new_ptr)
            try self.enc.emit(&.{ 0x4D, 0x89, 0xE9 }); // mov r9, r13 (new_size)

            // Stack args: tag_ptr at [rsp+32], tag_len at [rsp+40]
            try self.beginCall(2); // 6 params = 4 reg + 2 stack

            // LEA RAX, [RIP + disp32] for tag_ptr
            const lea_pos = self.code.items.len;
            const rip_after_lea = lea_pos + 7;
            const rip_offset: i32 = @intCast(@as(i64, @intCast(tag_pos)) - @as(i64, @intCast(rip_after_lea)));
            try self.enc.emit(&.{ 0x48, 0x8D, 0x05 }); // lea rax, [rip+disp32]
            try self.enc.emitI32(rip_offset);
            try self.enc.emit(&.{ 0x48, 0x89, 0x44, 0x24, 0x20 }); // mov [rsp+32], rax (tag_ptr)

            // Set tag_len at [rsp+40]
            try self.emitStackParam(1, @intCast(tag.len));

            try self.emitInternalCallAfterSetup("__track_realloc");
            try self.endCall();

            // Restore new_ptr from R14 to RAX
            try self.enc.emit(&.{ 0x4C, 0x89, 0xF0 }); // mov rax, r14
        } else {
            // Restore new_ptr from R14 to RAX
            try self.enc.emit(&.{ 0x4C, 0x89, 0xF0 }); // mov rax, r14
        }

        // Patch the skip_realloc_jmp to jump here (both paths converge)
        const end_pos = self.code.items.len;
        const skip_rel: i32 = @intCast(end_pos - skip_realloc_jmp - 4);
        self.code.items[skip_realloc_jmp] = @truncate(@as(u32, @bitCast(skip_rel)));
        self.code.items[skip_realloc_jmp + 1] = @truncate(@as(u32, @bitCast(skip_rel)) >> 8);
        self.code.items[skip_realloc_jmp + 2] = @truncate(@as(u32, @bitCast(skip_rel)) >> 16);
        self.code.items[skip_realloc_jmp + 3] = @truncate(@as(u32, @bitCast(skip_rel)) >> 24);

        // Result is in RAX
        try self.storeToStack(inst.result.?, .ptr);
        try self.markIndirect(inst.result.?);
    }

    fn emitTrackFreeCall(self: *IrCodegen) !void {
        // Track free: ptr in R12, size in R15
        // Embed tag string after a jump
        const tag = "dynamic array";
        const jmp_offset = try self.enc.jmpRel32();
        const tag_pos = self.code.items.len;
        try self.code.appendSlice(self.allocator, tag);
        const after_tag = self.code.items.len;
        // Patch jump
        const rel: i32 = @intCast(after_tag - jmp_offset - 4);
        self.code.items[jmp_offset] = @bitCast(@as(i8, @intCast(rel & 0xFF)));
        self.code.items[jmp_offset + 1] = @bitCast(@as(i8, @intCast((rel >> 8) & 0xFF)));
        self.code.items[jmp_offset + 2] = @bitCast(@as(i8, @intCast((rel >> 16) & 0xFF)));
        self.code.items[jmp_offset + 3] = @bitCast(@as(i8, @intCast((rel >> 24) & 0xFF)));

        // LEA R8, [RIP - offset_to_tag]
        const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
        try self.enc.emit(&.{ 0x4C, 0x8D, 0x05 }); // lea r8, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // mov r9, tag_len
        try self.enc.emit(&.{ 0x49, 0xC7, 0xC1 }); // mov r9, imm32
        try self.enc.emitI32(@intCast(tag.len));

        try self.enc.emit(&.{ 0x4C, 0x89, 0xE1 }); // mov rcx, r12 (ptr)
        try self.enc.emit(&.{ 0x4C, 0x89, 0xFA }); // mov rdx, r15 (size)
        try self.emitInternalCall("__track_free");
    }

    const CmpOp = enum { eq, ne, lt, le, gt, ge };

    fn genFcmp(self: *IrCodegen, inst: ir.Instruction, op: CmpOp) !void {
        const result = inst.result.?;
        const lhs = inst.operands[0].value;
        const rhs = inst.operands[1].value;

        // Load operands to xmm0 and xmm1
        try self.loadToXmm(lhs, .xmm0);
        const temp = self.allocStackSlots(1);
        try self.enc.movsdRbpOffsetXmm0(temp);
        try self.loadToXmm(rhs, .xmm1);
        try self.enc.movsdXmm0RbpOffset(temp);

        // Compare: ucomisd xmm0, xmm1
        try self.enc.ucomisdXmm0Xmm1();

        // Set AL based on comparison result
        // ucomisd sets CF and ZF for unsigned comparison semantics:
        // - CF=0, ZF=0: xmm0 > xmm1 (above)
        // - CF=0, ZF=1: xmm0 == xmm1 (equal)
        // - CF=1, ZF=0: xmm0 < xmm1 (below)
        switch (op) {
            .eq => try self.enc.seteAl(), // ZF=1
            .ne => try self.enc.setneAl(), // ZF=0
            .lt => try self.enc.setbAl(), // CF=1 (below)
            .le => try self.enc.setbeAl(), // CF=1 or ZF=1 (below or equal)
            .gt => try self.enc.setaAl(), // CF=0 and ZF=0 (above)
            .ge => try self.enc.setaeAl(), // CF=0 (above or equal)
        }

        try self.enc.movzxRaxAl();
        try self.storeToStack(result, .i64);
    }

    fn genIcmp(self: *IrCodegen, inst: ir.Instruction, op: CmpOp) !void {
        const result = inst.result.?;
        const lhs = inst.operands[0].value;
        const rhs = inst.operands[1].value;

        const lhs_offset = self.getStackOffset(lhs) catch -999;
        const rhs_offset = self.getStackOffset(rhs) catch -999;
        debug.codegen("  icmp: lhs=%{d}(off={d},ind={}), rhs=%{d}(off={d},ind={}), op={s}", .{ lhs, lhs_offset, self.isIndirect(lhs), rhs, rhs_offset, self.isIndirect(rhs), @tagName(op) });

        // Load lhs to rax, save it, load rhs to rcx, restore lhs
        try self.loadToRax(lhs);
        try self.enc.pushRax();
        try self.loadToRax(rhs);
        try self.enc.movRcxRax();
        try self.enc.popRax();

        // Compare: cmp rax, rcx
        try self.enc.cmpRaxRcx();

        // Set AL based on comparison result
        switch (op) {
            .eq => try self.enc.seteAl(),
            .ne => try self.enc.setneAl(),
            .lt => try self.enc.setlAl(),
            .le => try self.enc.setleAl(),
            .gt => try self.enc.setgAl(),
            .ge => try self.enc.setgeAl(),
        }

        // Zero-extend AL to RAX
        try self.enc.movzxRaxAl();

        try self.storeToStack(result, .i64);
    }

    fn genBr(self: *IrCodegen, inst: ir.Instruction) !void {
        const target_block = inst.operands[0].block_ref;

        // Emit jmp rel32 and record patch
        const patch_offset = try self.enc.jmpRel32();
        try self.jump_patches.append(self.allocator, .{
            .offset = patch_offset,
            .target_block = target_block,
        });
    }

    fn genBrCond(self: *IrCodegen, inst: ir.Instruction) !void {
        const cond = inst.operands[0].value;
        const then_block = inst.operands[1].block_ref;
        const else_block = inst.operands[2].block_ref;

        // Load condition to rax
        try self.loadToRax(cond);

        // TEST rax, rax (sets ZF if rax == 0)
        try self.enc.emit(&.{ 0x48, 0x85, 0xC0 }); // test rax, rax

        // JNE then_block (jump if condition is non-zero, i.e., true)
        const then_patch = try self.enc.jneRel32();
        try self.jump_patches.append(self.allocator, .{
            .offset = then_patch,
            .target_block = then_block,
        });

        // JMP else_block (fall through to else)
        const else_patch = try self.enc.jmpRel32();
        try self.jump_patches.append(self.allocator, .{
            .offset = else_patch,
            .target_block = else_block,
        });
    }
};

pub const CodegenResult = struct {
    code: []u8,
    external_patches: []const ExternalCallPatch,
};

pub fn generate(module: ir.Module, allocator: std.mem.Allocator, options: CodegenOptions) !CodegenResult {
    var code: std.ArrayListUnmanaged(u8) = .empty;
    errdefer code.deinit(allocator);
    var codegen = IrCodegen.init(allocator, &code, options);
    defer codegen.deinit();
    try codegen.generateModule(module);

    // Copy external patches to owned slice so they outlive codegen
    const patches = try allocator.dupe(ExternalCallPatch, codegen.external_patches.items);

    return .{
        .code = try code.toOwnedSlice(allocator),
        .external_patches = patches,
    };
}
