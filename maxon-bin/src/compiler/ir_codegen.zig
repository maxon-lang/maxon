const std = @import("std");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const x86 = @import("x86.zig");

/// Call site to patch after all functions are generated
const CallPatch = struct {
    offset: usize,
    target_func: []const u8,
    source_file: ?[]const u8 = null,
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
    move_count = 32, // i64: number of ownership moves
    incref_count = 40, // i64: number of incref operations
    decref_count = 48, // i64: number of decref operations
    copy_count = 56, // i64: number of struct copy operations
    cleanup_count = 64, // i64: number of cleanup operations
    table_base = 72, // start of tracking table (256 entries * 24 bytes each)
};

/// Patch for RIP-relative access to tracking data
const TrackingDataPatch = struct {
    code_offset: usize, // Where the RIP-relative displacement is in code
    field: TrackingDataField, // Which field we're accessing
};

/// String constant entry for data section
const StringConstant = struct {
    data: []const u8, // String bytes
    offset: usize, // Offset in data section (set after code generation)
};

/// Patch for RIP-relative access to string constant
const StringConstantPatch = struct {
    code_offset: usize, // Where the RIP-relative displacement is in code
    string_index: usize, // Index into string_constants array
};

/// Patch for __chkstk call in function prologues
const ChkstkPatch = struct {
    call_offset: usize, // Where the call rel32 displacement is in code
};

/// Global variable entry for mutable data section
const GlobalVarEntry = struct {
    size: usize, // Size in bytes
    offset: usize, // Offset in data section (set after code generation)
    init_data: ?[]const u8, // Initial data bytes (null for zero-init)
};

/// Patch for RIP-relative access to global variable
const GlobalVarPatch = struct {
    code_offset: usize, // Where the RIP-relative displacement is in code
    name: []const u8, // Global variable name
};

/// Code generation options
pub const CodegenOptions = struct {
    track_memory: bool = false,
    track_registers: bool = false,
};

/// Live range for a single value - tracks the span of instructions where it's live
const LiveRange = struct {
    value: ir.Value,
    start: u32, // First instruction index where value is defined
    end: u32, // Last instruction index where value is used
    is_float: bool, // Whether this is a float value (uses XMM registers)
    live_across_call: bool, // Whether the range crosses a call instruction
    assigned_reg: ?x86.Gpr, // Assigned GPR (null if spilled or XMM)
    assigned_xmm: ?x86.Xmm, // Assigned XMM (null if spilled or GPR)
    spill_slot: ?i32, // Stack offset if spilled (null if in register)
};

/// Per-block liveness sets for dataflow analysis
const BlockLiveness = struct {
    live_in: std.AutoHashMapUnmanaged(ir.Value, void),
    live_out: std.AutoHashMapUnmanaged(ir.Value, void),
    def_set: std.AutoHashMapUnmanaged(ir.Value, void),
    use_set: std.AutoHashMapUnmanaged(ir.Value, void),

    fn init() BlockLiveness {
        return .{
            .live_in = .{},
            .live_out = .{},
            .def_set = .{},
            .use_set = .{},
        };
    }

    fn deinit(self: *BlockLiveness, allocator: std.mem.Allocator) void {
        self.live_in.deinit(allocator);
        self.live_out.deinit(allocator);
        self.def_set.deinit(allocator);
        self.use_set.deinit(allocator);
    }
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
    /// Live ranges for all values in the function (computed by linear-scan allocator)
    live_ranges: std.ArrayListUnmanaged(LiveRange),
    /// Next available spill slot offset (grows downward from frame base)
    next_spill_offset: i32,
    /// Whether linear-scan allocation is enabled (vs stack-only fallback)
    use_linear_scan: bool,

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
            .live_ranges = .{},
            .next_spill_offset = -8,
            .use_linear_scan = false, // Disabled by default for now
        };
    }

    fn deinit(self: *RegAllocInfo, allocator: std.mem.Allocator) void {
        self.reg_map.deinit(allocator);
        self.xmm_map.deinit(allocator);
        self.stack_slots.deinit(allocator);
        self.used_callee_saved_gprs.deinit(allocator);
        self.used_callee_saved_xmms.deinit(allocator);
        self.live_ranges.deinit(allocator);
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
            // These instructions all make internal/external calls that clobber caller-saved registers
            const is_call_like = switch (inst.op) {
                .call, .call_indirect, .extern_call => true,
                .track_incref, .track_decref, .track_move, .track_copy, .track_cleanup => true,
                // heap operations call external DLL functions
                .heap_alloc, .heap_free, .heap_realloc => true,
                else => false,
            };
            if (is_call_like) {
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

/// Build live ranges from liveness analysis for linear-scan allocation
/// Assigns instruction indices and computes start/end for each value
fn buildLiveRanges(
    allocator: std.mem.Allocator,
    func: *const ir.Function,
    value_types: *const std.AutoHashMapUnmanaged(ir.Value, ir.Type),
) !std.ArrayListUnmanaged(LiveRange) {
    var ranges: std.ArrayListUnmanaged(LiveRange) = .empty;
    var value_to_range: std.AutoHashMapUnmanaged(ir.Value, usize) = .{};
    defer value_to_range.deinit(allocator);

    // Global instruction index across all blocks
    var inst_idx: u32 = 0;

    // Find call instruction positions for live_across_call detection
    var call_positions: std.ArrayListUnmanaged(u32) = .empty;
    defer call_positions.deinit(allocator);

    for (func.blocks.items) |block| {
        for (block.instructions.items) |inst| {
            if (inst.op == .call or inst.op == .call_indirect or inst.op == .extern_call) {
                try call_positions.append(allocator, inst_idx);
            }
            inst_idx += 1;
        }
    }

    // Reset for main pass
    inst_idx = 0;

    for (func.blocks.items) |block| {
        for (block.instructions.items) |inst| {
            // Record definition - but skip values that MUST be on stack
            // These operations produce pointer values that represent stack addresses
            // and are used by getStackOffset later
            if (inst.result) |result| {
                const result_type = if (value_types.get(result)) |t| t else ir.Type.i64;

                // Pointer values must stay on stack - they're used by getStackOffset
                const must_be_on_stack = result_type == .ptr or switch (inst.op) {
                    // alloca creates a stack slot and returns a pointer to it
                    .alloca, .alloca_sized, .alloca_dynamic => true,
                    // These compute addresses that may need to be on stack
                    .getelemptr, .getfieldptr => true,
                    // Parameters need special handling (they arrive in registers but are often stored)
                    .param => true,
                    // String constants are addresses
                    .const_string => true,
                    // Function addresses
                    .func_addr => true,
                    // Global variable addresses
                    .global_addr => true,
                    // Heap operations return pointers
                    .heap_alloc, .heap_realloc => true,
                    // Loads from pointers might produce pointers
                    .load => true,
                    // Call results can be pointers
                    .call, .call_indirect, .extern_call => true,
                    else => false,
                };

                if (!must_be_on_stack) {
                    const is_float = result_type == .f64;
                    try ranges.append(allocator, .{
                        .value = result,
                        .start = inst_idx,
                        .end = inst_idx, // Will be extended by uses
                        .is_float = is_float,
                        .live_across_call = false,
                        .assigned_reg = null,
                        .assigned_xmm = null,
                        .spill_slot = null,
                    });
                    try value_to_range.put(allocator, result, ranges.items.len - 1);
                }
            }

            // Extend ranges for each use
            for (inst.operands) |operand| {
                switch (operand) {
                    .value => |val| {
                        if (value_to_range.get(val)) |range_idx| {
                            ranges.items[range_idx].end = inst_idx;
                        }
                    },
                    .call_args => |args| {
                        for (args) |arg| {
                            if (value_to_range.get(arg)) |range_idx| {
                                ranges.items[range_idx].end = inst_idx;
                            }
                        }
                    },
                    else => {},
                }
            }

            inst_idx += 1;
        }
    }

    // Mark ranges that cross calls
    for (ranges.items) |*range| {
        for (call_positions.items) |call_pos| {
            if (range.start < call_pos and call_pos < range.end) {
                range.live_across_call = true;
                break;
            }
        }
    }

    return ranges;
}

/// Linear-scan register allocation
/// Allocates physical registers to live ranges, preferring caller-saved for short-lived
/// values and callee-saved for values live across calls
fn linearScanAllocate(
    allocator: std.mem.Allocator,
    ranges: *std.ArrayListUnmanaged(LiveRange),
    used_callee_saved_gprs: *std.ArrayListUnmanaged(x86.Gpr),
    used_callee_saved_xmms: *std.ArrayListUnmanaged(x86.Xmm),
) !i32 {
    // Sort ranges by start position
    std.mem.sort(LiveRange, ranges.items, {}, struct {
        fn lessThan(_: void, a: LiveRange, b: LiveRange) bool {
            return a.start < b.start;
        }
    }.lessThan);

    // Available registers (Windows x64 ABI)
    // Caller-saved: RAX, RCX, RDX, R8, R9, R10, R11
    // Callee-saved: RBX, RSI, RDI, R12, R13, R14, R15
    // We exclude RAX (used for return values), RCX/RDX/R8/R9 (argument passing), RSP, RBP
    // Available GPRs for allocation: R10, R11 (caller-saved), RBX, RSI, RDI, R12-R15 (callee-saved)
    var free_caller_saved_gprs: std.ArrayListUnmanaged(x86.Gpr) = .empty;
    defer free_caller_saved_gprs.deinit(allocator);
    try free_caller_saved_gprs.appendSlice(allocator, &[_]x86.Gpr{ .r10, .r11 });

    var free_callee_saved_gprs: std.ArrayListUnmanaged(x86.Gpr) = .empty;
    defer free_callee_saved_gprs.deinit(allocator);
    try free_callee_saved_gprs.appendSlice(allocator, &[_]x86.Gpr{ .rbx, .rsi, .rdi, .r12, .r13, .r14, .r15 });

    // XMM registers: xmm0-5 caller-saved, xmm6-15 callee-saved
    // xmm0-3 used for args, so available: xmm4, xmm5 (caller), xmm6-15 (callee)
    var free_caller_saved_xmms: std.ArrayListUnmanaged(x86.Xmm) = .empty;
    defer free_caller_saved_xmms.deinit(allocator);
    try free_caller_saved_xmms.appendSlice(allocator, &[_]x86.Xmm{ .xmm4, .xmm5 });

    var free_callee_saved_xmms: std.ArrayListUnmanaged(x86.Xmm) = .empty;
    defer free_callee_saved_xmms.deinit(allocator);
    try free_callee_saved_xmms.appendSlice(allocator, &[_]x86.Xmm{ .xmm6, .xmm7, .xmm8, .xmm9, .xmm10, .xmm11, .xmm12, .xmm13, .xmm14, .xmm15 });

    // Active list - ranges currently in registers, sorted by end position
    var active: std.ArrayListUnmanaged(*LiveRange) = .empty;
    defer active.deinit(allocator);

    var next_spill_offset: i32 = -8;

    for (ranges.items) |*range| {
        // Expire old intervals
        var i: usize = 0;
        while (i < active.items.len) {
            if (active.items[i].end < range.start) {
                // This interval has expired, free its register
                const expired = active.items[i];
                if (expired.is_float) {
                    if (expired.assigned_xmm) |xmm| {
                        // Return to appropriate pool
                        if (isCalleeSavedXmm(xmm)) {
                            try free_callee_saved_xmms.append(allocator, xmm);
                        } else {
                            try free_caller_saved_xmms.append(allocator, xmm);
                        }
                    }
                } else {
                    if (expired.assigned_reg) |reg| {
                        if (isCalleeSavedGpr(reg)) {
                            try free_callee_saved_gprs.append(allocator, reg);
                        } else {
                            try free_caller_saved_gprs.append(allocator, reg);
                        }
                    }
                }
                _ = active.orderedRemove(i);
            } else {
                i += 1;
            }
        }

        // Allocate register for current range
        if (range.is_float) {
            // Float allocation
            if (range.live_across_call) {
                // Prefer callee-saved for values live across calls
                if (free_callee_saved_xmms.items.len > 0) {
                    range.assigned_xmm = free_callee_saved_xmms.pop();
                    try markCalleeSavedXmmUsed(allocator, range.assigned_xmm.?, used_callee_saved_xmms);
                } else if (free_caller_saved_xmms.items.len > 0) {
                    // Fall back to caller-saved (will need save/restore around calls)
                    range.assigned_xmm = free_caller_saved_xmms.pop();
                } else {
                    // Spill
                    range.spill_slot = next_spill_offset;
                    next_spill_offset -= 8;
                }
            } else {
                // Prefer caller-saved for short-lived values
                if (free_caller_saved_xmms.items.len > 0) {
                    range.assigned_xmm = free_caller_saved_xmms.pop();
                } else if (free_callee_saved_xmms.items.len > 0) {
                    range.assigned_xmm = free_callee_saved_xmms.pop();
                    try markCalleeSavedXmmUsed(allocator, range.assigned_xmm.?, used_callee_saved_xmms);
                } else {
                    // Spill
                    range.spill_slot = next_spill_offset;
                    next_spill_offset -= 8;
                }
            }
        } else {
            // GPR allocation
            if (range.live_across_call) {
                // Prefer callee-saved for values live across calls
                if (free_callee_saved_gprs.items.len > 0) {
                    range.assigned_reg = free_callee_saved_gprs.pop();
                    try markCalleeSavedGprUsed(allocator, range.assigned_reg.?, used_callee_saved_gprs);
                } else if (free_caller_saved_gprs.items.len > 0) {
                    range.assigned_reg = free_caller_saved_gprs.pop();
                } else {
                    // Spill
                    range.spill_slot = next_spill_offset;
                    next_spill_offset -= 8;
                }
            } else {
                // Prefer caller-saved for short-lived values
                if (free_caller_saved_gprs.items.len > 0) {
                    range.assigned_reg = free_caller_saved_gprs.pop();
                } else if (free_callee_saved_gprs.items.len > 0) {
                    range.assigned_reg = free_callee_saved_gprs.pop();
                    try markCalleeSavedGprUsed(allocator, range.assigned_reg.?, used_callee_saved_gprs);
                } else {
                    // Spill
                    range.spill_slot = next_spill_offset;
                    next_spill_offset -= 8;
                }
            }
        }

        // Add to active list if allocated to a register
        if (range.assigned_reg != null or range.assigned_xmm != null) {
            try active.append(allocator, range);
            // Keep active list sorted by end position
            std.mem.sort(*LiveRange, active.items, {}, struct {
                fn lessThan(_: void, a: *LiveRange, b: *LiveRange) bool {
                    return a.end < b.end;
                }
            }.lessThan);
        }
    }

    return next_spill_offset;
}

fn isCalleeSavedGpr(reg: x86.Gpr) bool {
    return switch (reg) {
        .rbx, .rsi, .rdi, .r12, .r13, .r14, .r15 => true,
        else => false,
    };
}

fn isCalleeSavedXmm(xmm: x86.Xmm) bool {
    return switch (xmm) {
        .xmm6, .xmm7, .xmm8, .xmm9, .xmm10, .xmm11, .xmm12, .xmm13, .xmm14, .xmm15 => true,
        else => false,
    };
}

fn markCalleeSavedGprUsed(allocator: std.mem.Allocator, reg: x86.Gpr, used: *std.ArrayListUnmanaged(x86.Gpr)) !void {
    for (used.items) |r| {
        if (r == reg) return; // Already recorded
    }
    try used.append(allocator, reg);
}

fn markCalleeSavedXmmUsed(allocator: std.mem.Allocator, xmm: x86.Xmm, used: *std.ArrayListUnmanaged(x86.Xmm)) !void {
    for (used.items) |x| {
        if (x == xmm) return; // Already recorded
    }
    try used.append(allocator, xmm);
}

/// Check if a return type requires hidden pointer (large struct/error union > 8 bytes)
/// Windows x64 ABI: Large structs and error unions are returned via a hidden pointer
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
    // this should return true for structs/error unions > 8 bytes.
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
    track_memory: bool,
    // Register/stack tracking
    track_registers: bool,
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
    // Source file for error reporting
    source_file: ?[]const u8 = null,
    // String constants for data section
    string_constants: std.ArrayListUnmanaged(StringConstant),
    // Patches for string constant references
    string_constant_patches: std.ArrayListUnmanaged(StringConstantPatch),
    // Offset where string data section starts (set after code generation)
    string_data_offset: ?usize = null,
    // Patches for __chkstk calls in function prologues
    chkstk_patches: std.ArrayListUnmanaged(ChkstkPatch),
    // Global variables (mutable data section)
    global_variables: std.StringHashMapUnmanaged(GlobalVarEntry) = .{},
    // Patches for global variable references
    global_var_patches: std.ArrayListUnmanaged(GlobalVarPatch) = .{},
    // Offset where global data section starts (set after code generation)
    global_data_offset: ?usize = null,

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
            .track_memory = options.track_memory,
            .track_registers = options.track_registers,
            .tracking_data_patches = .{},
            .func_return_types = .{},
            .stored_types = .{},
            .reg_alloc = RegAllocInfo.init(),
            .outgoing_stack_args_size = 0,
            .pending_stack_space = 0,
            .string_constants = .{},
            .string_constant_patches = .{},
            .chkstk_patches = .{},
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
        self.string_constants.deinit(self.allocator);
        self.string_constant_patches.deinit(self.allocator);
        self.chkstk_patches.deinit(self.allocator);
        self.global_variables.deinit(self.allocator);
        self.global_var_patches.deinit(self.allocator);
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
        if (self.track_registers) {
            switch (loc) {
                .stack => |offset| std.debug.print("[REG] {s}: %{d} -> stack[{d}] ({s})\n", .{ self.current_func_name, val, offset, ty.toIrName() }),
                .register => |reg| std.debug.print("[REG] {s}: %{d} -> {s} ({s})\n", .{ self.current_func_name, val, @tagName(reg), ty.toIrName() }),
                .xmm => |xmm| std.debug.print("[REG] {s}: %{d} -> {s} ({s})\n", .{ self.current_func_name, val, @tagName(xmm), ty.toIrName() }),
            }
        }
        try self.value_locations.put(self.allocator, val, loc);
        try self.value_types.put(self.allocator, val, ty);
    }

    fn getStackOffset(self: *IrCodegen, val: ir.Value) !i32 {
        const loc = self.value_locations.get(val) orelse {
            debug.log("InvalidValue in getStackOffset: val=%{d} not found (func={s})", .{ val, self.current_func_name });
            return error.InvalidValue;
        };
        return switch (loc) {
            .stack => |o| o,
            else => {
                debug.log("ExpectedStackLocation in getStackOffset: val=%{d} has location {s} (func={s})", .{ val, @tagName(loc), self.current_func_name });
                return error.ExpectedStackLocation;
            },
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

    /// Store result from RAX (or XMM0 for floats) to assigned location
    /// Linear-scan allocator determines whether this goes to:
    /// - A GPR register (reg_map)
    /// - An XMM register (xmm_map)
    /// - A stack slot (stack_slots from spill)
    /// If not pre-allocated, falls back to allocating a new stack slot
    fn storeResult(self: *IrCodegen, result: ir.Value, ty: ir.Type) !void {
        // Check if linear-scan allocated a register for this value
        if (self.reg_alloc.reg_map.get(result)) |reg| {
            // Value has an assigned GPR - move from RAX to that register
            if (reg != .rax) {
                try self.enc.movGprGpr(reg, .rax);
            }
            try self.setValueLocation(result, .{ .register = reg }, ty);
            if (self.track_registers) {
                std.debug.print("[STORE] {s}: %{d} -> {s} (regalloc)\n", .{ self.current_func_name, result, @tagName(reg) });
            }
        } else if (self.reg_alloc.xmm_map.get(result)) |xmm| {
            // Value has an assigned XMM register
            if (xmm != .xmm0) {
                // movsd xmm_target, xmm0
                try self.enc.movsdXmmXmm(xmm, .xmm0);
            }
            try self.setValueLocation(result, .{ .xmm = xmm }, ty);
            if (self.track_registers) {
                std.debug.print("[STORE] {s}: %{d} -> {s} (regalloc)\n", .{ self.current_func_name, result, @tagName(xmm) });
            }
        } else if (self.reg_alloc.stack_slots.get(result)) |offset| {
            // Value was spilled by linear-scan - use pre-allocated spill slot
            if (ty == .f64) {
                try self.enc.movsdRbpOffsetXmm0(offset);
            } else {
                try self.enc.movRbpOffsetRax(offset);
            }
            try self.setValueLocation(result, .{ .stack = offset }, ty);
            if (self.track_registers) {
                std.debug.print("[STORE] {s}: %{d} -> stack[{d}] (spill)\n", .{ self.current_func_name, result, offset });
            }
        } else {
            // Fallback: value not pre-allocated, allocate new stack slot
            // This handles parameters and other values not in the live ranges
            const offset = self.allocStackSlots(1);
            if (ty == .f64) {
                try self.enc.movsdRbpOffsetXmm0(offset);
            } else {
                try self.enc.movRbpOffsetRax(offset);
            }
            try self.setValueLocation(result, .{ .stack = offset }, ty);
        }
    }

    /// Legacy function - redirects to storeResult
    fn storeToStack(self: *IrCodegen, result: ir.Value, ty: ir.Type) !void {
        try self.storeResult(result, ty);
    }

    fn loadValue(self: *IrCodegen, val: ir.Value, target: LoadTarget) !void {
        const loc = self.value_locations.get(val) orelse {
            debug.log("InvalidValue: val=%{d} not found in value_locations (func={s})", .{ val, self.current_func_name });
            return error.InvalidValue;
        };
        if (self.track_registers) {
            switch (loc) {
                .stack => |offset| std.debug.print("[LOAD] {s}: %{d} from stack[{d}] -> {s}\n", .{ self.current_func_name, val, offset, @tagName(target) }),
                .register => |reg| std.debug.print("[LOAD] {s}: %{d} from {s} -> {s}\n", .{ self.current_func_name, val, @tagName(reg), @tagName(target) }),
                .xmm => |xmm| std.debug.print("[LOAD] {s}: %{d} from {s} -> {s}\n", .{ self.current_func_name, val, @tagName(xmm), @tagName(target) }),
            }
        }
        switch (loc) {
            .stack => |offset| {
                debug.codegen("    loadValue: val=%{d}, stack offset={d}, target={s}", .{ val, offset, @tagName(target) });
                switch (target) {
                    .rax => try self.enc.movRaxRbpOffset(offset),
                    .xmm => |xmm| try self.enc.movsdXmmRbpOffset(xmm, offset),
                }
            },
            .register => |reg| switch (target) {
                .rax => {
                    if (reg != .rax) {
                        try self.enc.movGprGpr(.rax, reg);
                    }
                },
                .xmm => return error.CannotLoadRegToXmm,
            },
            .xmm => |reg| switch (target) {
                .rax => return error.CannotLoadXmmToRax,
                .xmm => |xmm| if (reg != xmm) {
                    try self.enc.movsdXmmXmm(xmm, reg);
                },
            },
        }
    }

    fn loadToRax(self: *IrCodegen, val: ir.Value) !void {
        try self.loadValue(val, .rax);
    }

    fn loadToRcx(self: *IrCodegen, val: ir.Value) !void {
        const loc = self.value_locations.get(val) orelse {
            debug.log("InvalidValue in loadToRcx: val=%{d} not found (func={s})", .{ val, self.current_func_name });
            return error.InvalidValue;
        };
        switch (loc) {
            .stack => |offset| try self.enc.movRcxRbpOffsetQ(offset),
            .register => |reg| {
                if (reg != .rcx) {
                    try self.enc.movGprGpr(.rcx, reg);
                }
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
        self.source_file = module.source_file;
        // Collect function return types for cross-function calls
        for (module.functions.items) |*func| {
            try self.func_return_types.put(self.allocator, func.name, func.return_type);
        }

        // Generate _start wrapper (entry point that calls main and ExitProcess)
        try self.generateStartWrapper();

        // Generate main function
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

        // Generate __write_stdout runtime function (always needed for print)
        try self.generateWriteStdout();

        // Generate __panic runtime function (for unhandled errors)
        try self.generatePanic();

        // Generate runtime conversion functions for string interpolation
        try self.generateRuntimeIntToString();
        try self.generateRuntimeFloatToString();
        try self.generateRuntimeBoolToString();

        // Generate __chkstk for large stack allocations (Windows x64)
        try self.generateChkstk();
        // Patch all __chkstk calls now that we know its address
        try self.patchChkstkCalls();

        // Generate tracking support functions if enabled
        if (self.track_memory) {
            try self.generateTrackingFunctions();
            // Generate tracking data section at the end
            try self.generateTrackingData();
            // Patch tracking data references
            try self.patchTrackingDataRefs();
        }

        // Generate string constants data section and patch references
        if (self.string_constants.items.len > 0) {
            try self.generateStringData();
            try self.patchStringConstantRefs();
        }

        // Generate global variables data section and patch references
        if (self.global_variables.count() > 0) {
            try self.generateGlobalData();
            try self.patchGlobalVarRefs();
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

    /// Generate string constants data section
    /// Appends all string data to the code buffer and records their offsets
    fn generateStringData(self: *IrCodegen) !void {
        // Align to 8 bytes for safety
        while (self.code.items.len % 8 != 0) {
            try self.code.append(self.allocator, 0);
        }
        self.string_data_offset = self.code.items.len;

        // Append each string and record its offset
        for (self.string_constants.items) |*str_const| {
            str_const.offset = self.code.items.len;
            try self.code.appendSlice(self.allocator, str_const.data);
        }
    }

    /// Patch all RIP-relative references to string constants
    fn patchStringConstantRefs(self: *IrCodegen) !void {
        for (self.string_constant_patches.items) |patch| {
            // Get the string's actual offset in code
            const str_offset = self.string_constants.items[patch.string_index].offset;
            // RIP at time of instruction is patch.code_offset + 4 (after the disp32)
            const rip = patch.code_offset + 4;
            // Calculate relative offset: target - rip
            const rel: i32 = @intCast(@as(i64, @intCast(str_offset)) - @as(i64, @intCast(rip)));

            // Write the displacement
            const bytes: [4]u8 = @bitCast(rel);
            self.code.items[patch.code_offset] = bytes[0];
            self.code.items[patch.code_offset + 1] = bytes[1];
            self.code.items[patch.code_offset + 2] = bytes[2];
            self.code.items[patch.code_offset + 3] = bytes[3];
        }
    }

    /// Generate global variables data section
    /// Appends storage for all global variables to the code buffer
    fn generateGlobalData(self: *IrCodegen) !void {
        // Align to 8 bytes for safety
        while (self.code.items.len % 8 != 0) {
            try self.code.append(self.allocator, 0);
        }
        self.global_data_offset = self.code.items.len;

        // Allocate space for each global variable and record its offset
        var iter = self.global_variables.iterator();
        while (iter.next()) |entry| {
            const var_entry = entry.value_ptr;
            var_entry.offset = self.code.items.len;

            if (var_entry.init_data) |init_bytes| {
                // Write initial value
                try self.code.appendSlice(self.allocator, init_bytes);
                // Pad to alignment if needed
                const padding = (8 - (init_bytes.len % 8)) % 8;
                try self.code.appendNTimes(self.allocator, 0, padding);
            } else {
                // Zero-initialize
                try self.code.appendNTimes(self.allocator, 0, var_entry.size);
            }
        }
    }

    /// Patch all RIP-relative references to global variables
    fn patchGlobalVarRefs(self: *IrCodegen) !void {
        for (self.global_var_patches.items) |patch| {
            // Get the global variable's actual offset in code
            const var_entry = self.global_variables.get(patch.name) orelse continue;
            const var_offset = var_entry.offset;
            // RIP at time of instruction is patch.code_offset + 4 (after the disp32)
            const rip = patch.code_offset + 4;
            // Calculate relative offset: target - rip
            const rel: i32 = @intCast(@as(i64, @intCast(var_offset)) - @as(i64, @intCast(rip)));

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
    ///   +24: tracking_table_count (i64)
    ///   +32: move_count (i64)
    ///   +40: incref_count (i64)
    ///   +48: decref_count (i64)
    ///   +56: tracking_table[256] - each entry is {ptr: i64, size: i64, id: i64} = 24 bytes
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
        // move_count (8 bytes)
        try self.code.appendNTimes(self.allocator, 0, 8);
        // incref_count (8 bytes)
        try self.code.appendNTimes(self.allocator, 0, 8);
        // decref_count (8 bytes)
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

    /// Generate _start wrapper - entry point that calls main and ExitProcess
    /// When tracking is enabled, also calls tracking setup/summary functions
    fn generateStartWrapper(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "_start", self.code.items.len);

        // Prologue - need stack frame for callee-saved register
        try self.enc.prologue(64);

        if (self.track_memory) {
            // Call __enable_alloc_tracking
            try self.emitInternalCall("__enable_alloc_tracking");
        }

        // Call main
        try self.emitInternalCall("main");

        // Save main's return value to R12 (callee-saved)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC4 }); // mov r12, rax

        if (self.track_memory) {
            // Call __print_alloc_summary
            try self.emitInternalCall("__print_alloc_summary");
        }

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

        // __track_move - tracks ownership transfer
        try self.generateTrackMove();

        // __track_incref - tracks reference count increment
        try self.generateTrackIncref();

        // __track_decref - tracks reference count decrement
        try self.generateTrackDecref();

        // __track_copy - tracks struct copy
        try self.generateTrackCopy();

        // __track_cleanup - tracks cleanup start
        try self.generateTrackCleanup();
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

    /// Generate __print_alloc_summary function - prints memory tracking stats
    /// Stack layout:
    ///   [rbp-56] = total_allocated
    ///   [rbp-64] = total_freed
    ///   [rbp-72] = leaked (allocated - freed)
    ///   [rbp-80] = move_count
    ///   [rbp-88] = incref_count
    ///   [rbp-96] = decref_count
    fn generatePrintAllocSummary(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__print_alloc_summary", self.code.items.len);

        // Prologue - save callee-saved registers
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xA0, 0x00, 0x00, 0x00 }); // sub rsp, 160

        // Save RSI and RDI to stack (callee-saved on Windows x64)
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0x90 }); // mov [rbp-112], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0x98 }); // mov [rbp-104], rdi

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Load allocated and freed for leak calculation
        try self.emitLoadTrackingField(.total_allocated);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xC8 }); // mov [rbp-56], rax (allocated)
        try self.emitLoadTrackingField(.total_freed);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xC0 }); // mov [rbp-64], rax (freed)

        // Calculate leaked = allocated - freed, store in [rbp-72]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x4D, 0xC8 }); // mov rcx, [rbp-56] (allocated)
        try self.enc.emit(&.{ 0x48, 0x2B, 0x4D, 0xC0 }); // sub rcx, [rbp-64] (freed)
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xB8 }); // mov [rbp-72], rcx (leaked)

        // Print "\n=== MEMORY STATS ===\n"
        try self.printStaticString("\n=== MEMORY STATS ===\n");

        // Print "Allocated: "
        try self.printStaticString("Allocated: ");
        try self.printNumberFromStack(-56);
        try self.printStaticString(" bytes\n");

        // Print "Freed:     "
        try self.printStaticString("Freed:     ");
        try self.printNumberFromStack(-64);
        try self.printStaticString(" bytes\n");

        // Print "Leaked:    "
        try self.printStaticString("Leaked:    ");
        try self.printNumberFromStack(-72);
        try self.printStaticString(" bytes\n");

        // Load and print Moves
        try self.emitLoadTrackingField(.move_count);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xB0 }); // mov [rbp-80], rax
        try self.printStaticString("Moves:     ");
        try self.printNumberFromStack(-80);
        try self.printStaticString("\n");

        // Load and print Increfs
        try self.emitLoadTrackingField(.incref_count);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xB0 }); // mov [rbp-80], rax (reuse slot)
        try self.printStaticString("Increfs:   ");
        try self.printNumberFromStack(-80);
        try self.printStaticString("\n");

        // Load and print Decrefs
        try self.emitLoadTrackingField(.decref_count);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xB0 }); // mov [rbp-80], rax (reuse slot)
        try self.printStaticString("Decrefs:   ");
        try self.printNumberFromStack(-80);
        try self.printStaticString("\n");

        // Load and print Copies
        try self.emitLoadTrackingField(.copy_count);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xB0 }); // mov [rbp-80], rax (reuse slot)
        try self.printStaticString("Copies:    ");
        try self.printNumberFromStack(-80);
        try self.printStaticString("\n");

        // Load and print Cleanups
        try self.emitLoadTrackingField(.cleanup_count);
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xB0 }); // mov [rbp-80], rax (reuse slot)
        try self.printStaticString("Cleanups:  ");
        try self.printNumberFromStack(-80);
        try self.printStaticString("\n");

        // Epilogue - restore RSI/RDI from stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0x90 }); // mov rsi, [rbp-112]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x7D, 0x98 }); // mov rdi, [rbp-104]
        try self.enc.emit(&.{ 0x48, 0x81, 0xC4, 0xA0, 0x00, 0x00, 0x00 }); // add rsp, 160 (must match prologue)
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
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xA0, 0x00, 0x00, 0x00 }); // sub rsp, 160 (for buffer at rbp-160 to rbp-180)

        // Save RSI and RDI to stack (callee-saved on Windows x64)
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0xA0 }); // mov [rbp-96], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0xA8 }); // mov [rbp-88], rdi

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
        // First check if table is full (256 entries max)
        try self.emitLoadTrackingField(.table_count);
        try self.enc.emit(&.{ 0x48, 0x3D, 0x00, 0x01, 0x00, 0x00 }); // cmp rax, 256
        // jge rel32 (skip if count >= 256) - opcode 0F 8D
        try self.enc.emit(&.{ 0x0F, 0x8D, 0x00, 0x00, 0x00, 0x00 }); // jge rel32 (placeholder)
        const skip_store = self.code.items.len - 4; // offset to patch

        // Get current table count again
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

        // Patch skip jump - calculate offset from end of jge instruction to here
        const after_store = self.code.items.len;
        const skip_rel: i32 = @intCast(@as(i64, @intCast(after_store)) - @as(i64, @intCast(skip_store)) - 4);
        self.code.items[skip_store] = @bitCast(@as(i8, @intCast(skip_rel & 0xFF)));
        self.code.items[skip_store + 1] = @bitCast(@as(i8, @intCast((skip_rel >> 8) & 0xFF)));
        self.code.items[skip_store + 2] = @bitCast(@as(i8, @intCast((skip_rel >> 16) & 0xFF)));
        self.code.items[skip_store + 3] = @bitCast(@as(i8, @intCast((skip_rel >> 24) & 0xFF)));

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

        // Epilogue - restore RSI/RDI from stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xA0 }); // mov rsi, [rbp-96]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x7D, 0xA8 }); // mov rdi, [rbp-88]
        try self.enc.emit(&.{ 0x48, 0x81, 0xC4, 0xA0, 0x00, 0x00, 0x00 }); // add rsp, 160 (must match prologue)
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
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xA0, 0x00, 0x00, 0x00 }); // sub rsp, 160 (for buffer at rbp-160 to rbp-180)

        // Save RSI and RDI to stack (callee-saved on Windows x64)
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0xA0 }); // mov [rbp-96], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0xA8 }); // mov [rbp-88], rdi

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

        // Epilogue - restore RSI/RDI from stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xA0 }); // mov rsi, [rbp-96]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x7D, 0xA8 }); // mov rdi, [rbp-88]
        try self.enc.emit(&.{ 0x48, 0x81, 0xC4, 0xA0, 0x00, 0x00, 0x00 }); // add rsp, 160 (must match prologue)
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
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xA0, 0x00, 0x00, 0x00 }); // sub rsp, 160 (for buffer at rbp-160 to rbp-180)

        // Save RSI and RDI to stack (callee-saved on Windows x64)
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0x80 }); // mov [rbp-128], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0x88 }); // mov [rbp-120], rdi

        // Save all inputs to stack immediately
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xC8 }); // mov [rbp-56], rcx (old_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xC0 }); // mov [rbp-64], rdx (old_size)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x45, 0xB8 }); // mov [rbp-72], r8 (new_ptr)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x4D, 0xB0 }); // mov [rbp-80], r9 (new_size)
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

        // Epilogue - restore RSI/RDI from stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0x80 }); // mov rsi, [rbp-128]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x7D, 0x88 }); // mov rdi, [rbp-120]
        try self.enc.emit(&.{ 0x48, 0x81, 0xC4, 0xA0, 0x00, 0x00, 0x00 }); // add rsp, 160 (must match prologue)
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __track_move - tracks ownership transfer
    /// Input: RCX = from_var_ptr, RDX = from_var_len, R8 = to_var_ptr, R9 = to_var_len
    /// Stack layout:
    ///   [rbp-48] = to_var_len (for printTagFromR12)
    ///   [rbp-56] = from_var_ptr
    ///   [rbp-64] = from_var_len
    fn generateTrackMove(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_move", self.code.items.len);

        // Prologue - save callee-saved registers
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xA0, 0x00, 0x00, 0x00 }); // sub rsp, 160 (for buffer at rbp-160 to rbp-180)

        // Save RSI and RDI to stack (callee-saved on Windows x64)
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0xA0 }); // mov [rbp-96], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0xA8 }); // mov [rbp-88], rdi

        // Stack layout:
        //   [rbp-48] = temp for printTagFromR12
        //   [rbp-56] = from_var_ptr (RCX)
        //   [rbp-64] = from_var_len (RDX)
        //   [rbp-72] = to_var_ptr (R8)
        //   [rbp-80] = to_var_len (R9)
        //   [rbp-88] = saved RDI
        //   [rbp-96] = saved RSI
        //   [rbp-160] to [rbp-180] = printNumberFromStack buffer

        // Save all inputs to stack immediately
        try self.enc.emit(&.{ 0x48, 0x89, 0x4D, 0xC8 }); // mov [rbp-56], rcx (from_var_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xC0 }); // mov [rbp-64], rdx (from_var_len)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x45, 0xB8 }); // mov [rbp-72], r8 (to_var_ptr)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x4D, 0xB0 }); // mov [rbp-80], r9 (to_var_len)

        // Increment move_count
        try self.emitLoadTrackingField(.move_count);
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax
        try self.emitStoreTrackingField(.move_count);

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Print "MOVE: "
        try self.printStaticString("MOVE: ");

        // Print from_var (source variable name)
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x65, 0xC8 }); // mov r12, [rbp-56] (from_var_ptr)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xC0 }); // mov rax, [rbp-64] (from_var_len)
        try self.enc.emit(&.{ 0x48, 0x89, 0x45, 0xD0 }); // mov [rbp-48], rax
        try self.printTagFromR12();

        // Print newline
        try self.printStaticString("\n");

        // Epilogue - restore RSI/RDI from stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xA0 }); // mov rsi, [rbp-96]
        try self.enc.emit(&.{ 0x48, 0x8B, 0x7D, 0xA8 }); // mov rdi, [rbp-88]
        try self.enc.emit(&.{ 0x48, 0x81, 0xC4, 0xA0, 0x00, 0x00, 0x00 }); // add rsp, 160 (must match prologue)
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __track_incref - tracks reference count increment
    /// Input: RCX = var_ptr, RDX = var_len, R8 = new_refcount
    /// Stack layout (all storage after sub rsp, 192):
    ///   [rbp-8]  = saved r12
    ///   [rbp-16] = saved r13
    ///   [rbp-24] = saved r14
    ///   [rbp-32] = saved r15
    ///   [rbp-40] = saved rsi
    ///   [rbp-48] = var_len (for printTagFromR12)
    ///   [rbp-56] = new_refcount
    ///   [rbp-160] to [rbp-180] = printNumberFromStack buffer
    fn generateTrackIncref(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_incref", self.code.items.len);

        // Prologue - use sub rsp approach so all offsets are predictable
        // On Windows x64, RSI and RDI are callee-saved - we use RSI/RDI in printNumberFromStack
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xC0, 0x00, 0x00, 0x00 }); // sub rsp, 192 (for buffer at rbp-160 to rbp-180)

        // Save callee-saved registers to known stack slots
        try self.enc.emit(&.{ 0x4C, 0x89, 0x65, 0xF8 }); // mov [rbp-8], r12
        try self.enc.emit(&.{ 0x4C, 0x89, 0x6D, 0xF0 }); // mov [rbp-16], r13
        try self.enc.emit(&.{ 0x4C, 0x89, 0x75, 0xE8 }); // mov [rbp-24], r14
        try self.enc.emit(&.{ 0x4C, 0x89, 0x7D, 0xE0 }); // mov [rbp-32], r15
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0xD8 }); // mov [rbp-40], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0xC0 }); // mov [rbp-64], rdi (CRITICAL: RDI is callee-saved!)

        // Save inputs
        try self.enc.emit(&.{ 0x49, 0x89, 0xCC }); // mov r12, rcx (var_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xD0 }); // mov [rbp-48], rdx (var_len for printTagFromR12)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x45, 0xC8 }); // mov [rbp-56], r8 (new_refcount)

        // Increment incref_count
        try self.emitLoadTrackingField(.incref_count);
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax
        try self.emitStoreTrackingField(.incref_count);

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Print "INCREF: "
        try self.printStaticString("INCREF: ");

        // Print var name from R12
        try self.printTagFromR12();

        // Print " -> rc="
        try self.printStaticString(" -> rc=");

        // Print new refcount from stack (at [rbp-56])
        try self.printNumberFromStack(-56);

        // Print newline
        try self.printStaticString("\n");

        // Epilogue - restore callee-saved registers from stack slots
        try self.enc.emit(&.{ 0x48, 0x8B, 0x7D, 0xC0 }); // mov rdi, [rbp-64] (CRITICAL: restore RDI!)
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xD8 }); // mov rsi, [rbp-40]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x7D, 0xE0 }); // mov r15, [rbp-32]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x75, 0xE8 }); // mov r14, [rbp-24]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x6D, 0xF0 }); // mov r13, [rbp-16]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x65, 0xF8 }); // mov r12, [rbp-8]
        try self.enc.emit(&.{ 0x48, 0x89, 0xEC }); // mov rsp, rbp
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __track_decref - tracks reference count decrement
    /// Input: RCX = var_ptr, RDX = var_len, R8 = new_refcount
    /// Stack layout (all storage after sub rsp, 192):
    ///   [rbp-8]  = saved r12
    ///   [rbp-16] = saved r13
    ///   [rbp-24] = saved r14
    ///   [rbp-32] = saved r15
    ///   [rbp-40] = saved rsi
    ///   [rbp-64] = saved rdi
    ///   [rbp-48] = var_len (for printTagFromR12)
    ///   [rbp-56] = new_refcount
    ///   [rbp-160] to [rbp-180] = printNumberFromStack buffer
    fn generateTrackDecref(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_decref", self.code.items.len);

        // Prologue - use sub rsp approach so all offsets are predictable
        // On Windows x64, RSI and RDI are callee-saved - we use RSI/RDI in printNumberFromStack
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xC0, 0x00, 0x00, 0x00 }); // sub rsp, 192 (for buffer at rbp-160 to rbp-180)

        // Save callee-saved registers to known stack slots
        try self.enc.emit(&.{ 0x4C, 0x89, 0x65, 0xF8 }); // mov [rbp-8], r12
        try self.enc.emit(&.{ 0x4C, 0x89, 0x6D, 0xF0 }); // mov [rbp-16], r13
        try self.enc.emit(&.{ 0x4C, 0x89, 0x75, 0xE8 }); // mov [rbp-24], r14
        try self.enc.emit(&.{ 0x4C, 0x89, 0x7D, 0xE0 }); // mov [rbp-32], r15
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0xD8 }); // mov [rbp-40], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0xC0 }); // mov [rbp-64], rdi (CRITICAL: RDI is callee-saved!)

        // Save inputs
        try self.enc.emit(&.{ 0x49, 0x89, 0xCC }); // mov r12, rcx (var_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xD0 }); // mov [rbp-48], rdx (var_len for printTagFromR12)
        try self.enc.emit(&.{ 0x4C, 0x89, 0x45, 0xC8 }); // mov [rbp-56], r8 (new_refcount)

        // Increment decref_count
        try self.emitLoadTrackingField(.decref_count);
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax
        try self.emitStoreTrackingField(.decref_count);

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Print "DECREF: "
        try self.printStaticString("DECREF: ");

        // Print var name from R12
        try self.printTagFromR12();

        // Print " -> rc="
        try self.printStaticString(" -> rc=");

        // Print new refcount from stack (at [rbp-56])
        try self.printNumberFromStack(-56);

        // Print newline
        try self.printStaticString("\n");

        // Epilogue - restore callee-saved registers from stack slots
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xD8 }); // mov rsi, [rbp-40]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x7D, 0xE0 }); // mov r15, [rbp-32]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x75, 0xE8 }); // mov r14, [rbp-24]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x6D, 0xF0 }); // mov r13, [rbp-16]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x65, 0xF8 }); // mov r12, [rbp-8]
        try self.enc.emit(&.{ 0x48, 0x89, 0xEC }); // mov rsp, rbp
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __track_copy(var_ptr, var_len)
    /// Called when a struct/managed value is copied (e.g., struct assignment, pass by value)
    /// Parameters: RCX = var name pointer, RDX = var name length
    fn generateTrackCopy(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_copy", self.code.items.len);

        // Prologue
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xC0, 0x00, 0x00, 0x00 }); // sub rsp, 192

        // Save callee-saved registers
        try self.enc.emit(&.{ 0x4C, 0x89, 0x65, 0xF8 }); // mov [rbp-8], r12
        try self.enc.emit(&.{ 0x4C, 0x89, 0x6D, 0xF0 }); // mov [rbp-16], r13
        try self.enc.emit(&.{ 0x4C, 0x89, 0x75, 0xE8 }); // mov [rbp-24], r14
        try self.enc.emit(&.{ 0x4C, 0x89, 0x7D, 0xE0 }); // mov [rbp-32], r15
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0xD8 }); // mov [rbp-40], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0xC0 }); // mov [rbp-64], rdi

        // Save inputs
        try self.enc.emit(&.{ 0x49, 0x89, 0xCC }); // mov r12, rcx (var_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xD0 }); // mov [rbp-48], rdx (var_len)

        // Increment copy_count
        try self.emitLoadTrackingField(.copy_count);
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax
        try self.emitStoreTrackingField(.copy_count);

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Print "COPY: "
        try self.printStaticString("COPY: ");

        // Print var name from R12
        try self.printTagFromR12();

        // Print newline
        try self.printStaticString("\n");

        // Epilogue - restore callee-saved registers
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xD8 }); // mov rsi, [rbp-40]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x7D, 0xE0 }); // mov r15, [rbp-32]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x75, 0xE8 }); // mov r14, [rbp-24]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x6D, 0xF0 }); // mov r13, [rbp-16]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x65, 0xF8 }); // mov r12, [rbp-8]
        try self.enc.emit(&.{ 0x48, 0x89, 0xEC }); // mov rsp, rbp
        try self.enc.popRbp();
        try self.enc.ret();
    }

    /// Generate __track_cleanup(var_ptr, var_len)
    /// Called when a struct/managed value is cleaned up (e.g., going out of scope)
    /// Parameters: RCX = var name pointer, RDX = var name length
    fn generateTrackCleanup(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__track_cleanup", self.code.items.len);

        // Prologue
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x81, 0xEC, 0xC0, 0x00, 0x00, 0x00 }); // sub rsp, 192

        // Save callee-saved registers
        try self.enc.emit(&.{ 0x4C, 0x89, 0x65, 0xF8 }); // mov [rbp-8], r12
        try self.enc.emit(&.{ 0x4C, 0x89, 0x6D, 0xF0 }); // mov [rbp-16], r13
        try self.enc.emit(&.{ 0x4C, 0x89, 0x75, 0xE8 }); // mov [rbp-24], r14
        try self.enc.emit(&.{ 0x4C, 0x89, 0x7D, 0xE0 }); // mov [rbp-32], r15
        try self.enc.emit(&.{ 0x48, 0x89, 0x75, 0xD8 }); // mov [rbp-40], rsi
        try self.enc.emit(&.{ 0x48, 0x89, 0x7D, 0xC0 }); // mov [rbp-64], rdi

        // Save inputs
        try self.enc.emit(&.{ 0x49, 0x89, 0xCC }); // mov r12, rcx (var_ptr)
        try self.enc.emit(&.{ 0x48, 0x89, 0x55, 0xD0 }); // mov [rbp-48], rdx (var_len)

        // Increment cleanup_count
        try self.emitLoadTrackingField(.cleanup_count);
        try self.enc.emit(&.{ 0x48, 0xFF, 0xC0 }); // inc rax
        try self.emitStoreTrackingField(.cleanup_count);

        // Get stdout handle
        try self.enc.movRcxImm32(-11); // STD_OUTPUT_HANDLE
        try self.emitExternalCall("kernel32.dll", "GetStdHandle");
        try self.enc.emit(&.{ 0x49, 0x89, 0xC7 }); // mov r15, rax (stdout handle)

        // Print "CLEANUP: "
        try self.printStaticString("CLEANUP: ");

        // Print var name from R12
        try self.printTagFromR12();

        // Print newline
        try self.printStaticString("\n");

        // Epilogue - restore callee-saved registers
        try self.enc.emit(&.{ 0x48, 0x8B, 0x75, 0xD8 }); // mov rsi, [rbp-40]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x7D, 0xE0 }); // mov r15, [rbp-32]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x75, 0xE8 }); // mov r14, [rbp-24]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x6D, 0xF0 }); // mov r13, [rbp-16]
        try self.enc.emit(&.{ 0x4C, 0x8B, 0x65, 0xF8 }); // mov r12, [rbp-8]
        try self.enc.emit(&.{ 0x48, 0x89, 0xEC }); // mov rsp, rbp
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

    /// Generate __panic(message_ptr, message_len) -> noreturn
    /// Writes panic message to stdout and exits with code 1
    /// Parameters: RCX = message pointer, RDX = message length
    fn generatePanic(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__panic", self.code.items.len);

        // Prologue
        try self.enc.pushRbp();
        try self.enc.emit(&.{ 0x48, 0x89, 0xE5 }); // mov rbp, rsp
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x20 }); // sub rsp, 32

        // Parameters already in RCX (message_ptr) and RDX (message_len)
        // Call __write_stdout to print the message
        try self.emitInternalCall("__write_stdout");

        // Call ExitProcess(1) to terminate with error code
        try self.enc.movRcxImm32(1);
        try self.emitExternalCall("kernel32.dll", "ExitProcess");

        // Epilogue (never reached but for completeness)
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x20 }); // add rsp, 32
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

    /// Generate __chkstk for Windows x64 stack probing
    /// When allocating more than 4096 bytes of stack, we must probe each page
    /// to ensure the OS commits the memory. Without this, large stack allocations
    /// can jump over guard pages and crash.
    ///
    /// Input: RAX = number of bytes to allocate
    /// This function ONLY probes pages - the caller is responsible for
    /// doing `sub rsp, stack_size` AFTER the call returns.
    /// Preserves all registers including RAX.
    fn generateChkstk(self: *IrCodegen) !void {
        try self.func_offsets.put(self.allocator, "__chkstk", self.code.items.len);

        // __chkstk implementation for Windows x64
        // We need to touch each page from current RSP down to RSP - RAX
        // to trigger the OS to commit those pages.
        //
        // IMPORTANT: We do NOT modify RSP here. The caller does `sub rsp, size`
        // after we return. We just probe the pages.
        //
        // Algorithm:
        //   save rcx, r10
        //   rcx = rax (bytes to probe)
        //   r10 = rsp (current position, adjusted for our pushes + return addr)
        // loop:
        //   if rcx < 4096: goto done
        //   r10 -= 4096
        //   touch [r10]
        //   rcx -= 4096
        //   goto loop
        // done:
        //   restore rcx, r10
        //   ret

        // Save registers we'll use
        try self.enc.emit(&.{0x51}); // push rcx
        try self.enc.emit(&.{ 0x41, 0x52 }); // push r10

        // mov rcx, rax (rcx = bytes to probe)
        try self.enc.emit(&.{ 0x48, 0x89, 0xC1 }); // mov rcx, rax

        // lea r10, [rsp + 24] ; r10 = original RSP before call
        // Stack layout: [return addr][saved rcx][saved r10] <- RSP
        // So original RSP = RSP + 24 (3 * 8 bytes)
        try self.enc.emit(&.{ 0x4C, 0x8D, 0x54, 0x24, 0x18 }); // lea r10, [rsp+24]

        // loop_start:
        const loop_start = self.code.items.len;

        // cmp rcx, 0x1000
        try self.enc.emit(&.{ 0x48, 0x81, 0xF9, 0x00, 0x10, 0x00, 0x00 }); // cmp rcx, 0x1000

        // jb done (jump if below, i.e., remaining < 4096)
        const jb_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x72, 0x00 }); // jb done (patch later)

        // sub r10, 0x1000 (move probe position down one page)
        try self.enc.emit(&.{ 0x49, 0x81, 0xEA, 0x00, 0x10, 0x00, 0x00 }); // sub r10, 0x1000

        // Touch the page - use test byte [r10], 0 (non-destructive read)
        try self.enc.emit(&.{ 0x41, 0xF6, 0x02, 0x00 }); // test byte [r10], 0

        // sub rcx, 0x1000
        try self.enc.emit(&.{ 0x48, 0x81, 0xE9, 0x00, 0x10, 0x00, 0x00 }); // sub rcx, 0x1000

        // jmp loop_start
        const jmp_back: i8 = @intCast(@as(i64, @intCast(loop_start)) - @as(i64, @intCast(self.code.items.len + 2)));
        try self.enc.emit(&.{ 0xEB, @bitCast(jmp_back) }); // jmp loop_start

        // done:
        const done_pos = self.code.items.len;
        self.code.items[jb_offset + 1] = @intCast(done_pos - jb_offset - 2);

        // Restore registers
        try self.enc.emit(&.{ 0x41, 0x5A }); // pop r10
        try self.enc.emit(&.{0x59}); // pop rcx

        try self.enc.ret();
    }

    /// Patch all __chkstk call sites with the actual address
    fn patchChkstkCalls(self: *IrCodegen) !void {
        const chkstk_addr = self.func_offsets.get("__chkstk") orelse return error.ChkstkNotFound;

        for (self.chkstk_patches.items) |patch| {
            // RIP after call instruction = call_offset + 4
            const rel: i32 = @intCast(@as(i64, @intCast(chkstk_addr)) - @as(i64, @intCast(patch.call_offset + 4)));
            const call_bytes: [4]u8 = @bitCast(rel);
            self.code.items[patch.call_offset] = call_bytes[0];
            self.code.items[patch.call_offset + 1] = call_bytes[1];
            self.code.items[patch.call_offset + 2] = call_bytes[2];
            self.code.items[patch.call_offset + 3] = call_bytes[3];
        }
    }

    // ========================================================================
    // String Formatting Runtime Helpers
    // ========================================================================

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
    /// Uses [rbp-160] as conversion buffer end. Converts number at [rbp+stack_offset] to decimal and prints.
    /// Buffer spans [rbp-160] to [rbp-180] for up to 20 digit numbers.
    /// NOTE: Stack slots [rbp-8] to [rbp-128] are used by tracking funcs for saved regs and data.
    fn printNumberFromStack(self: *IrCodegen, stack_offset: i8) !void {
        // Load number from stack to RAX
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, @bitCast(stack_offset) }); // mov rax, [rbp+offset]

        // lea rdi, [rbp-160] (end of buffer - we write backwards)
        // Using disp32 because -160 doesn't fit in disp8 (-128 to +127)
        // ModRM: mod=10 (disp32), reg=RDI (111), r/m=RBP (101) = 0xBD
        // disp32 = -160 = 0xFFFFFF60 (little-endian: 60 FF FF FF)
        try self.enc.emit(&.{ 0x48, 0x8D, 0xBD, 0x60, 0xFF, 0xFF, 0xFF }); // lea rdi, [rbp-160]

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
                // NOTE: This should not happen for user code anymore since undefined functions
                // are now caught during AST->IR conversion (error E024). If this is reached,
                // it indicates an internal compiler error or missing function registration.
                if (patch.source_file) |file| {
                    std.debug.print("internal error: {s}: call to undefined function '{s}' (should have been caught earlier)\n", .{ file, patch.target_func });
                } else {
                    std.debug.print("internal error: call to undefined function '{s}' (should have been caught earlier)\n", .{patch.target_func});
                }
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
        _ = liveness; // We use buildLiveRanges which does its own liveness analysis

        // Reset reg_alloc for this function
        self.reg_alloc.deinit(self.allocator);
        self.reg_alloc = RegAllocInfo.init();

        // Check if this function returns a large struct or error union (> 8 bytes)
        // Windows x64 ABI: Large structs/error unions are returned via hidden pointer in RCX
        // This shifts all other parameters right by one register
        self.reg_alloc.has_hidden_ret_ptr = hasHiddenReturnPointer(func.return_type);

        // Collect value types from all instructions for live range analysis
        var value_types: std.AutoHashMapUnmanaged(ir.Value, ir.Type) = .{};
        defer value_types.deinit(self.allocator);

        for (func.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                if (inst.result) |result| {
                    const ty = inferResultType(inst);
                    try value_types.put(self.allocator, result, ty);
                }
            }
        }

        // Build live ranges for all values
        self.reg_alloc.live_ranges = try buildLiveRanges(self.allocator, func, &value_types);

        // Run linear-scan register allocation
        self.reg_alloc.next_spill_offset = try linearScanAllocate(
            self.allocator,
            &self.reg_alloc.live_ranges,
            &self.reg_alloc.used_callee_saved_gprs,
            &self.reg_alloc.used_callee_saved_xmms,
        );

        // For now, don't populate reg_map/xmm_map - keep using stack-based storage
        // The codegen assumes values can be addressed via getStackOffset(), which expects
        // stack locations. Enabling register allocation requires updating all places that
        // use getStackOffset to also handle register locations.
        //
        // TODO: Enable register allocation by updating genStore, genLoad, etc. to handle
        // register-allocated values. For now, values go through the storeResult fallback
        // which allocates new stack slots.
        //
        // Populate only stack_slots for spilled values - this doesn't change behavior
        // but prepares for when we fully enable register allocation
        for (self.reg_alloc.live_ranges.items) |range| {
            if (range.spill_slot) |slot| {
                try self.reg_alloc.stack_slots.put(self.allocator, range.value, slot);
            }
            // Note: We intentionally skip assigned_reg and assigned_xmm here
            // so that storeResult falls back to stack allocation
        }

        // Scan for heap operations which use callee-saved registers internally.
        // These registers must be saved/restored in the prologue/epilogue.
        // We add these AFTER linear-scan so they don't get clobbered.
        // - heap_alloc: uses R12 (always), R13 (when tracking)
        // - heap_free: uses R12 (always), R13+R14 (when tracking)
        // - heap_realloc: uses R12+R13 (always), R14+R15 (when tracking)
        var uses_heap_alloc = false;
        var uses_heap_free = false;
        var uses_heap_realloc = false;

        for (func.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                switch (inst.op) {
                    .heap_alloc => uses_heap_alloc = true,
                    .heap_free => uses_heap_free = true,
                    .heap_realloc => uses_heap_realloc = true,
                    else => {},
                }
            }
        }

        // Add callee-saved registers used by heap operations
        if (uses_heap_alloc or uses_heap_free or uses_heap_realloc) {
            // R12 is used by all heap operations
            try markCalleeSavedGprUsed(self.allocator, .r12, &self.reg_alloc.used_callee_saved_gprs);
            if (self.track_registers) {
                std.debug.print("[CALLEE-SAVED] {s}: adding R12 (heap ops)\n", .{func.name});
            }
        }
        if (uses_heap_realloc) {
            // R13 is used by heap_realloc to hold new_size across HeapReAlloc call
            try markCalleeSavedGprUsed(self.allocator, .r13, &self.reg_alloc.used_callee_saved_gprs);
            // R14 is used by heap_realloc to hold result (new_ptr) after HeapReAlloc
            try markCalleeSavedGprUsed(self.allocator, .r14, &self.reg_alloc.used_callee_saved_gprs);
            if (self.track_registers) {
                std.debug.print("[CALLEE-SAVED] {s}: adding R13, R14 (heap_realloc)\n", .{func.name});
            }
        }
        if (self.track_memory) {
            // When memory tracking is enabled, additional registers are used
            if (uses_heap_alloc and !uses_heap_realloc) {
                // heap_alloc uses R13 for tracking (but realloc already added it)
                try markCalleeSavedGprUsed(self.allocator, .r13, &self.reg_alloc.used_callee_saved_gprs);
            }
            if (uses_heap_free) {
                // heap_free uses R13+R14 for tracking
                if (!uses_heap_alloc and !uses_heap_realloc) {
                    try markCalleeSavedGprUsed(self.allocator, .r13, &self.reg_alloc.used_callee_saved_gprs);
                }
                // R14 is only added here if not already added by heap_realloc
                if (!uses_heap_realloc) {
                    try markCalleeSavedGprUsed(self.allocator, .r14, &self.reg_alloc.used_callee_saved_gprs);
                }
            }
            if (uses_heap_realloc) {
                // heap_realloc uses R15 for tracking old_size
                // R14 was already added above (always used by heap_realloc)
                try markCalleeSavedGprUsed(self.allocator, .r15, &self.reg_alloc.used_callee_saved_gprs);
            }
        }
    }

    /// Infer the result type of an instruction based on its opcode
    fn inferResultType(inst: ir.Instruction) ir.Type {
        return switch (inst.op) {
            // Float operations
            .fadd, .fsub, .fmul, .fdiv => .f64,
            .sitofp => .f64, // signed int to float
            .fabs, .fsqrt, .fceil, .ffloor, .fround => .f64,
            .bitcast_i64_to_f64 => .f64,
            // Integer operations
            .add, .sub, .mul, .div, .mod => .i64,
            .fptosi => .i64, // float to signed int
            .bitcast_f64_to_i64 => .i64,
            .band, .bitor, .bxor, .shl, .shr => .i64,
            // Boolean/comparison results (i64 for 0/1)
            .icmp_eq, .icmp_ne, .icmp_lt, .icmp_le, .icmp_gt, .icmp_ge => .i64,
            .fcmp_eq, .fcmp_ne, .fcmp_lt, .fcmp_le, .fcmp_gt, .fcmp_ge => .i64,
            // Pointer operations
            .load, .heap_alloc, .heap_realloc, .alloca, .alloca_sized, .alloca_dynamic => .ptr,
            .getelemptr, .getfieldptr, .func_addr, .global_addr => .ptr,
            // Constants
            .const_i64 => .i64,
            .const_i32 => .i32,
            .const_i8 => .i8,
            .const_f64 => .f64,
            .const_string => .ptr,
            // Truncations and extensions
            .trunc_i64_i32 => .i32,
            .trunc_i64_i8 => .i8,
            .sext_i32_i64, .zext_i8_i64 => .i64,
            // Default to i64 for other operations (param, call, extern_call, etc.)
            else => .i64,
        };
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

        // Emit prologue placeholder (17 bytes reserved, filled in after we know stack size)
        const func_start = self.code.items.len;
        try self.enc.prologuePlaceholder();

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

        // Determine if we need __chkstk (stack >= 4096 bytes)
        const needs_chkstk = stack_size >= 4096;

        // Fill in the prologue with the appropriate variant
        x86.Encoder.fillPrologue(self.code.items, func_start, stack_size, needs_chkstk);

        // If using __chkstk, record the call offset for later patching
        if (needs_chkstk) {
            const call_offset = func_start + 10;
            try self.chkstk_patches.append(self.allocator, .{ .call_offset = call_offset });
        }
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
            .const_string => try self.genConstString(inst),
            .const_data => try self.genConstData(inst),
            .global_addr => try self.genGlobalAddr(inst),
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
            .fsqrt => try self.genFsqrt(inst),
            .fceil => try self.genFceil(inst),
            .ffloor => try self.genFfloor(inst),
            .fround => try self.genFround(inst),
            .bitcast_f64_to_i64 => try self.genBitcastF64ToI64(inst),
            .bitcast_i64_to_f64 => try self.genBitcastI64ToF64(inst),
            .sext_i32_i64 => try self.genSextI32I64(inst),
            .trunc_i64_i32 => try self.genTruncI64I32(inst),
            .trunc_i64_i8 => try self.genTruncI64I8(inst),
            .zext_i8_i64 => try self.genZextI8I64(inst),
            .ret => try self.genRet(inst),
            .param => try self.genParam(inst),
            .call => try self.genCall(inst),
            .call_indirect => try self.genCallIndirect(inst),
            .func_addr => try self.genFuncAddr(inst),
            .memcpy => try self.genMemcpy(inst),
            .memcpy_dyn => try self.genMemcpyDyn(inst),
            .memset => try self.genMemset(inst),
            .memset_dyn => try self.genMemsetDyn(inst),
            .cstr_len => try self.genCstrLen(inst),
            .heap_alloc => try self.genHeapAlloc(inst),
            .heap_free => try self.genHeapFree(inst),
            .heap_realloc => try self.genHeapRealloc(inst),
            .track_move => try self.genTrackMove(inst),
            .track_incref => try self.genTrackIncref(inst),
            .track_decref => try self.genTrackDecref(inst),
            .track_copy => try self.genTrackCopy(inst),
            .track_cleanup => try self.genTrackCleanup(inst),
            .extern_call => try self.genExternCall(inst),
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

    fn genConstString(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const str_data = inst.operands[0].string_data;

        // Check if this string already exists (deduplication)
        var string_index: usize = self.string_constants.items.len;
        for (self.string_constants.items, 0..) |existing, idx| {
            if (std.mem.eql(u8, existing.data, str_data)) {
                string_index = idx;
                break;
            }
        } else {
            // Add new string to constants table
            try self.string_constants.append(self.allocator, .{
                .data = str_data,
                .offset = 0, // Will be set after code generation
            });
        }

        // Allocate stack slot for the pointer
        const offset = self.allocStackSlots(1);

        // Emit LEA with placeholder RIP-relative offset (will be patched later)
        // LEA RAX, [RIP + disp32]
        try self.enc.emit(&.{ 0x48, 0x8D, 0x05 }); // LEA RAX, [RIP + disp32]
        // Record patch location (current position is where disp32 goes)
        try self.string_constant_patches.append(self.allocator, .{
            .code_offset = self.code.items.len,
            .string_index = string_index,
        });
        // Emit placeholder displacement
        try self.enc.emit(&.{ 0x00, 0x00, 0x00, 0x00 });

        // Store pointer to stack
        try self.enc.movRbpOffsetRax(offset);
        try self.setValueLocation(result, .{ .stack = offset }, .ptr);
        // Mark as indirect so loadArgs loads the value (the string pointer) rather than LEA
        try self.markIndirect(result);
    }

    fn genGlobalAddr(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const name = inst.operands[0].global_name;
        const var_size: usize = @intCast(inst.operands[1].elem_size);

        // Ensure the global variable is registered in our tracking table
        if (!self.global_variables.contains(name)) {
            try self.global_variables.put(self.allocator, name, .{
                .size = var_size,
                .offset = 0, // Will be set during generateGlobalData
                .init_data = null,
            });
        }

        // Allocate stack slot for the pointer
        const offset = self.allocStackSlots(1);

        // Emit LEA with placeholder RIP-relative offset (will be patched later)
        // LEA RAX, [RIP + disp32]
        try self.enc.emit(&.{ 0x48, 0x8D, 0x05 }); // LEA RAX, [RIP + disp32]
        // Record patch location (current position is where disp32 goes)
        try self.global_var_patches.append(self.allocator, .{
            .code_offset = self.code.items.len,
            .name = name,
        });
        // Emit placeholder displacement
        try self.enc.emit(&.{ 0x00, 0x00, 0x00, 0x00 });

        // Store pointer to stack
        try self.enc.movRbpOffsetRax(offset);
        try self.setValueLocation(result, .{ .stack = offset }, .ptr);
        // Mark as indirect because the slot contains a pointer that needs to be dereferenced
        try self.markIndirect(result);
    }

    fn genConstData(self: *IrCodegen, inst: ir.Instruction) !void {
        const result = inst.result.?;
        const data = inst.operands[0].string_data;

        // Check if this data already exists (deduplication)
        var string_index: usize = self.string_constants.items.len;
        for (self.string_constants.items, 0..) |existing, idx| {
            if (std.mem.eql(u8, existing.data, data)) {
                string_index = idx;
                break;
            }
        } else {
            // Add new data to constants table
            try self.string_constants.append(self.allocator, .{
                .data = data,
                .offset = 0, // Will be set after code generation
            });
        }

        // Allocate stack slot for the pointer
        const offset = self.allocStackSlots(1);

        // Emit LEA with placeholder RIP-relative offset (will be patched later)
        // LEA RAX, [RIP + disp32]
        try self.enc.emit(&.{ 0x48, 0x8D, 0x05 }); // LEA RAX, [RIP + disp32]
        // Record patch location (current position is where disp32 goes)
        try self.string_constant_patches.append(self.allocator, .{
            .code_offset = self.code.items.len,
            .string_index = string_index,
        });
        // Emit placeholder displacement
        try self.enc.emit(&.{ 0x00, 0x00, 0x00, 0x00 });

        // Store pointer to stack
        try self.enc.movRbpOffsetRax(offset);
        try self.setValueLocation(result, .{ .stack = offset }, .ptr);
        // Mark as indirect so loadArgs loads the value (the data pointer) rather than LEA
        try self.markIndirect(result);
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
                // Use imm8 for offsets in range -128..127, otherwise use imm32
                if (field_offset >= -128 and field_offset < 128) {
                    try self.enc.addRaxImm8(@intCast(@as(u32, @bitCast(field_offset)) & 0xFF));
                } else {
                    try self.enc.addRaxImm32(field_offset);
                }
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
        } else if (elem_size < 128) {
            // Small element sizes that fit in signed imm8: use imul rax, rax, imm8
            // imm8 is sign-extended, so we can only safely use values 0-127
            try self.enc.emit(&.{ 0x48, 0x6B, 0xC0 }); // imul rax, rax, imm8
            try self.enc.emit(&.{@as(u8, @intCast(elem_size))});
        } else {
            // Large element sizes: use imul rax, rax, imm32 to avoid sign-extension issues
            try self.enc.emit(&.{ 0x48, 0x69, 0xC0 }); // imul rax, rax, imm32
            const size_bytes = std.mem.toBytes(@as(i32, elem_size));
            try self.enc.emit(&size_bytes);
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

    /// Emit inline struct copy: copies size bytes from src_reg to dst_reg
    /// Uses RAX as scratch register. Handles 8-byte, 4-byte, 2-byte, and 1-byte moves.
    /// This is used for large struct returns via hidden pointer (Windows x64 ABI).
    fn emitStructCopy(self: *IrCodegen, src_reg: x86.Gpr, dst_reg: x86.Gpr, size: u32) !void {
        if (size == 0) return;

        var offset: u32 = 0;

        // Copy 8 bytes at a time
        while (offset + 8 <= size) : (offset += 8) {
            // mov rax, [src_reg + offset]
            try self.emitMovRaxMemRegOffset(src_reg, @intCast(offset));
            // mov [dst_reg + offset], rax
            try self.emitMovMemRegOffsetRax(dst_reg, @intCast(offset));
        }

        // Copy remaining 4 bytes if any
        if (offset + 4 <= size) {
            // mov eax, [src_reg + offset]
            try self.emitMovEaxMemRegOffset(src_reg, @intCast(offset));
            // mov [dst_reg + offset], eax
            try self.emitMovMemRegOffsetEax(dst_reg, @intCast(offset));
            offset += 4;
        }

        // Copy remaining 2 bytes if any
        if (offset + 2 <= size) {
            // mov ax, [src_reg + offset]
            try self.emitMovAxMemRegOffset(src_reg, @intCast(offset));
            // mov [dst_reg + offset], ax
            try self.emitMovMemRegOffsetAx(dst_reg, @intCast(offset));
            offset += 2;
        }

        // Copy remaining 1 byte if any
        if (offset < size) {
            // mov al, [src_reg + offset]
            try self.emitMovAlMemRegOffset(src_reg, @intCast(offset));
            // mov [dst_reg + offset], al
            try self.emitMovMemRegOffsetAl(dst_reg, @intCast(offset));
        }
    }

    /// Emit: mov rax, [reg + offset] (64-bit load from memory)
    fn emitMovRaxMemRegOffset(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        var rex: u8 = 0x48; // REX.W
        if (reg_num >= 8) rex |= 0x01; // REX.B

        if (offset == 0 and reg_num != 5 and reg_num != 13) { // rbp and r13 require displacement
            const modrm: u8 = 0x00 | (reg_num & 0x07); // mod=00, reg=rax(0), rm=reg
            try self.enc.emit(&.{ rex, 0x8B, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07); // mod=01, disp8
            try self.enc.emit(&.{ rex, 0x8B, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07); // mod=10, disp32
            try self.enc.emit(&.{ rex, 0x8B, modrm });
            try self.enc.emitI32(offset);
        }
    }

    /// Emit: mov [reg + offset], rax (64-bit store to memory)
    fn emitMovMemRegOffsetRax(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        var rex: u8 = 0x48; // REX.W
        if (reg_num >= 8) rex |= 0x01; // REX.B

        if (offset == 0 and reg_num != 5 and reg_num != 13) {
            const modrm: u8 = 0x00 | (reg_num & 0x07);
            try self.enc.emit(&.{ rex, 0x89, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07);
            try self.enc.emit(&.{ rex, 0x89, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07);
            try self.enc.emit(&.{ rex, 0x89, modrm });
            try self.enc.emitI32(offset);
        }
    }

    /// Emit: mov eax, [reg + offset] (32-bit load)
    fn emitMovEaxMemRegOffset(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        var prefix: ?u8 = null;
        if (reg_num >= 8) prefix = 0x41; // REX.B

        if (offset == 0 and reg_num != 5 and reg_num != 13) {
            const modrm: u8 = 0x00 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x8B, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x8B, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x8B, modrm });
            try self.enc.emitI32(offset);
        }
    }

    /// Emit: mov [reg + offset], eax (32-bit store)
    fn emitMovMemRegOffsetEax(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        var prefix: ?u8 = null;
        if (reg_num >= 8) prefix = 0x41;

        if (offset == 0 and reg_num != 5 and reg_num != 13) {
            const modrm: u8 = 0x00 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x89, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x89, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x89, modrm });
            try self.enc.emitI32(offset);
        }
    }

    /// Emit: mov ax, [reg + offset] (16-bit load)
    fn emitMovAxMemRegOffset(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        const prefix_66: u8 = 0x66; // Operand-size override

        if (reg_num >= 8) {
            try self.enc.emit(&.{ prefix_66, 0x41 }); // 66h + REX.B
        } else {
            try self.enc.emitByte(prefix_66);
        }

        if (offset == 0 and reg_num != 5 and reg_num != 13) {
            const modrm: u8 = 0x00 | (reg_num & 0x07);
            try self.enc.emit(&.{ 0x8B, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07);
            try self.enc.emit(&.{ 0x8B, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07);
            try self.enc.emit(&.{ 0x8B, modrm });
            try self.enc.emitI32(offset);
        }
    }

    /// Emit: mov [reg + offset], ax (16-bit store)
    fn emitMovMemRegOffsetAx(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        const prefix_66: u8 = 0x66;

        if (reg_num >= 8) {
            try self.enc.emit(&.{ prefix_66, 0x41 });
        } else {
            try self.enc.emitByte(prefix_66);
        }

        if (offset == 0 and reg_num != 5 and reg_num != 13) {
            const modrm: u8 = 0x00 | (reg_num & 0x07);
            try self.enc.emit(&.{ 0x89, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07);
            try self.enc.emit(&.{ 0x89, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07);
            try self.enc.emit(&.{ 0x89, modrm });
            try self.enc.emitI32(offset);
        }
    }

    /// Emit: mov al, [reg + offset] (8-bit load)
    fn emitMovAlMemRegOffset(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        var prefix: ?u8 = null;
        if (reg_num >= 8) prefix = 0x41;

        if (offset == 0 and reg_num != 5 and reg_num != 13) {
            const modrm: u8 = 0x00 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x8A, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x8A, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x8A, modrm });
            try self.enc.emitI32(offset);
        }
    }

    /// Emit: mov [reg + offset], al (8-bit store)
    fn emitMovMemRegOffsetAl(self: *IrCodegen, reg: x86.Gpr, offset: i32) !void {
        const reg_num: u8 = @intFromEnum(reg);
        var prefix: ?u8 = null;
        if (reg_num >= 8) prefix = 0x41;

        if (offset == 0 and reg_num != 5 and reg_num != 13) {
            const modrm: u8 = 0x00 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x88, modrm });
        } else if (offset >= -128 and offset <= 127) {
            const modrm: u8 = 0x40 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x88, modrm, @bitCast(@as(i8, @intCast(offset))) });
        } else {
            const modrm: u8 = 0x80 | (reg_num & 0x07);
            if (prefix) |p| try self.enc.emitByte(p);
            try self.enc.emit(&.{ 0x88, modrm });
            try self.enc.emitI32(offset);
        }
    }

    fn genMemcpyDyn(self: *IrCodegen, inst: ir.Instruction) !void {
        // Dynamic-sized memcpy using rep movsb
        const dest_val = inst.operands[0].value;
        const src_val = inst.operands[1].value;
        const size_val = inst.operands[2].value;

        // Save RSI and RDI (callee-saved on Windows x64)
        // Push two to maintain 16-byte stack alignment
        try self.enc.emit(&.{0x56}); // push rsi
        try self.enc.emit(&.{0x57}); // push rdi

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

        // Restore RDI and RSI
        try self.enc.emit(&.{0x5F}); // pop rdi
        try self.enc.emit(&.{0x5E}); // pop rsi
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

    fn genMemsetDyn(self: *IrCodegen, inst: ir.Instruction) !void {
        // Dynamic-sized memset using rep stosb
        const dest_val = inst.operands[0].value;
        const byte_val: u8 = @intCast(inst.operands[1].immediate_i32);
        const size_val = inst.operands[2].value;

        // Save RDI (callee-saved on Windows x64)
        // Push two to maintain 16-byte stack alignment
        try self.enc.emit(&.{0x57}); // push rdi
        try self.enc.emit(&.{0x56}); // push rsi (for alignment only)

        // Load size to rcx (rep count)
        try self.loadToRcx(size_val);

        // Load dest pointer to rdi
        const dest_offset = try self.getStackOffset(dest_val);
        if (self.isIndirect(dest_val)) {
            try self.enc.movRdiRbpOffset(dest_offset);
        } else {
            try self.enc.leaRdiRbpOffset(dest_offset);
        }

        // Set al to the byte value
        if (byte_val == 0) {
            try self.enc.emit(&.{ 0x30, 0xC0 }); // xor al, al
        } else {
            try self.enc.emit(&.{ 0xB0, byte_val }); // mov al, byte_val
        }

        // rep stosb: store al to [rdi], rcx times
        try self.enc.repStosb();

        // Restore RSI and RDI
        try self.enc.emit(&.{0x5E}); // pop rsi
        try self.enc.emit(&.{0x5F}); // pop rdi
    }

    fn genCstrLen(self: *IrCodegen, inst: ir.Instruction) !void {
        // Calculate length of null-terminated C string using repne scasb
        // Algorithm: set rcx to max, search for null byte, length = ~rcx - 1
        const result = inst.result.?;
        const ptr_val = inst.operands[0].value;

        // Save RDI (callee-saved on Windows x64)
        // Push two to maintain 16-byte stack alignment
        try self.enc.emit(&.{0x57}); // push rdi
        try self.enc.emit(&.{0x56}); // push rsi (for alignment only)

        // Load string pointer value to rdi (always use mov, not lea - we want the pointer value)
        const ptr_offset = try self.getStackOffset(ptr_val);
        try self.enc.movRdiRbpOffset(ptr_offset);

        // Set rcx to -1 (max count)
        try self.enc.movRcxImm64(-1);

        // Set al to 0 (null byte we're searching for)
        try self.enc.xorAlAl();

        // repne scasb: scan for null byte
        try self.enc.repneScasb();

        // NOT rcx to get length (rcx was decremented for each byte including null)
        try self.enc.notRcx();

        // DEC rcx to exclude the null terminator from the count
        try self.enc.decRcx();

        // Move result from rcx to rax
        try self.enc.movRaxRcx();

        // Restore RSI and RDI
        try self.enc.emit(&.{0x5E}); // pop rsi
        try self.enc.emit(&.{0x5F}); // pop rdi

        // Store result
        try self.storeToStack(result, .i64);
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

    fn genFsqrt(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load float value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Square root: SQRTSD xmm0, xmm0
        try self.enc.sqrtsdXmm0();
        // Result is float, store as f64
        try self.storeToStack(inst.result.?, .f64);
    }

    fn genFceil(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load float value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Round toward positive infinity using ROUNDSD with mode 0x02
        try self.enc.roundsdCeilXmm0();
        // Convert rounded float to signed int: CVTTSD2SI rax, xmm0
        try self.enc.cvttsd2siRaxXmm0();
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genFfloor(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load float value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Round toward negative infinity using ROUNDSD with mode 0x01
        try self.enc.roundsdFloorXmm0();
        // Convert rounded float to signed int: CVTTSD2SI rax, xmm0
        try self.enc.cvttsd2siRaxXmm0();
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genFround(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load float value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Add 0.5 and floor to get "round half away from zero" for positive numbers
        // For negative numbers, we need: floor(x + 0.5) also works correctly
        // Actually, for proper "round half away from zero":
        //   round(x) = floor(x + 0.5) for x >= 0
        //   round(x) = ceil(x - 0.5) for x < 0
        // Simpler: round(x) = trunc(x + copysign(0.5, x))
        // We use: copysign(0.5, x) + x, then truncate
        try self.enc.roundsdRoundHalfAwayXmm0();
        // Convert rounded float to signed int: CVTTSD2SI rax, xmm0
        try self.enc.cvttsd2siRaxXmm0();
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genBitcastF64ToI64(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load f64 value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Move bits from xmm0 to rax using movq
        try self.enc.movqRaxXmm0();
        // Store as i64
        try self.storeToStack(inst.result.?, .i64);
    }

    fn genBitcastI64ToF64(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load i64 value to rax
        try self.loadToRax(inst.operands[0].value);
        // Move bits from rax to xmm0 using movq
        try self.enc.movqXmm0Rax();
        // Store as f64 (storeToStack knows to use movsd for f64)
        try self.storeToStack(inst.result.?, .f64);
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

        // Normal return - restore callee-saved registers and return
        // _start wrapper handles calling ExitProcess after main returns
        try self.emitCalleeSavedRestores();
        try self.enc.epilogue();
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

        if (self.track_registers) {
            std.debug.print("[CALL] {s}: calling {s} with {d} args, stack_offset={d}\n", .{ self.current_func_name, func_name, args.len, self.next_stack_offset });
        }

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

    /// Generate indirect function call through pointer
    fn genCallIndirect(self: *IrCodegen, inst: ir.Instruction) !void {
        const func_ptr = inst.operands[0].value;
        const args = inst.operands[1].call_args;
        const ret_type = inst.result_type;

        const func_ptr_offset = self.getStackOffset(func_ptr) catch -999;
        const func_ptr_indirect = self.isIndirect(func_ptr);
        debug.codegen("  Indirect call through %{d} (offset={d}, indirect={}) with {d} args", .{ func_ptr, func_ptr_offset, func_ptr_indirect, args.len });

        // Load function pointer to R10 first (to preserve it during arg setup)
        debug.codegen("    Loading func_ptr to RAX then R10", .{});
        try self.loadToRax(func_ptr);
        try self.enc.emit(&.{ 0x49, 0x89, 0xC2 }); // mov r10, rax

        // Load arguments (uses RCX, RDX, R8, R9)
        debug.codegen("    Loading {d} args", .{args.len});
        try self.loadArgs(args, true);

        // Move function pointer to RAX for the call
        debug.codegen("    Moving R10 back to RAX for call", .{});
        try self.enc.emit(&.{ 0x4C, 0x89, 0xD0 }); // mov rax, r10

        // Emit indirect call through RAX
        debug.codegen("    Emitting call rax", .{});
        try self.enc.allocShadowSpace();
        try self.enc.callRax();
        try self.enc.freeShadowSpace();

        if (inst.result) |result| {
            debug.codegen("  indirect call result: %{d} of type {s}", .{ result, ret_type.toIrName() });
            try self.storeReturnValue(result, ret_type);
        }
    }

    /// Generate external DLL function call
    fn genExternCall(self: *IrCodegen, inst: ir.Instruction) !void {
        const extern_func = inst.operands[0].extern_func;
        const args = inst.operands[1].call_args;
        const ret_type = inst.result_type;

        debug.codegen("  ExternCall: {s}:{s} with {d} args", .{ extern_func.dll_name, extern_func.func_name, args.len });

        // Load arguments into registers (RCX, RDX, R8, R9) and stack for 5+
        try self.loadArgs(args, true);

        // Allocate shadow space and make the external call
        try self.enc.allocShadowSpace();
        try self.emitExternalCallAfterSetup(extern_func.dll_name, extern_func.func_name);
        try self.enc.freeShadowSpace();

        // Store return value if there is one
        if (inst.result) |result| {
            debug.codegen("  extern call result: %{d} of type {s}", .{ result, ret_type.toIrName() });
            try self.storeReturnValue(result, ret_type);
        }
    }

    /// Generate function address (get pointer to function)
    fn genFuncAddr(self: *IrCodegen, inst: ir.Instruction) !void {
        const func_name = inst.operands[0].func_name;
        const result = inst.result.?;

        debug.codegen("  FuncAddr: {s} -> %{d}", .{ func_name, result });

        // Emit lea rax, [rip+0] with patch offset
        // 48 8D 05 xx xx xx xx - lea rax, [rip+disp32]
        try self.enc.emit(&.{ 0x48, 0x8D, 0x05 });
        const patch_offset = self.code.items.len;
        try self.enc.emit(&.{ 0x00, 0x00, 0x00, 0x00 });

        // Record patch for later resolution
        try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = func_name, .source_file = self.source_file });

        // Store result to stack
        const offset = self.allocStackSlots(1);
        try self.enc.movRbpOffsetRax(offset);
        try self.setValueLocation(result, .{ .stack = offset }, .ptr);
        // Mark as indirect so when passed as argument, we load the value (function address)
        // rather than doing LEA which would give us the address of the stack slot
        try self.markIndirect(result);
    }

    /// Load arguments into registers (first 4) and stack (5+).
    /// use_lea controls whether direct pointers use LEA for the first 4 args.
    /// Windows x64 ABI: RCX, RDX, R8, R9 for first 4 integer/pointer args
    /// XMM0-XMM3 for first 4 float args (same position as corresponding GPR)
    /// Args 5+ go on stack at [RSP + 32 + (i-4)*8] after shadow space allocation
    fn loadArgs(self: *IrCodegen, args: []const ir.Value, use_lea: bool) !void {
        for (args, 0..) |arg, i| {
            const arg_type = self.value_types.get(arg) orelse return error.ValueTypeNotFound;
            const arg_offset = self.getStackOffset(arg) catch -999;
            const arg_indirect = self.isIndirect(arg);
            debug.codegen("    loadArgs[{d}]: %{d} type={s} offset={d} indirect={} use_lea={}", .{ i, arg, arg_type.toIrName(), arg_offset, arg_indirect, use_lea });

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
                    debug.codegen("      -> float arg to xmm{d}", .{i});
                    try self.loadToXmm(arg, xmm);
                } else if (use_lea and arg_type == .ptr and !self.isIndirect(arg)) {
                    // LEA - get pointer to struct on stack
                    const loc = self.value_locations.get(arg) orelse return error.ValueNotFound;
                    const offset = switch (loc) {
                        .stack => |o| o,
                        else => return error.UnsupportedArgumentLocation,
                    };
                    debug.codegen("      -> LEA (struct ptr) offset={d}", .{offset});
                    try self.emitLeaArgReg(i, offset);
                } else {
                    debug.codegen("      -> load value to arg reg", .{});
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
        try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = func_name, .source_file = self.source_file });
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
        try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = func_name, .source_file = self.source_file });
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

        // Save callee-saved registers we'll use (R12, R13)
        // Push two to maintain 16-byte stack alignment
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13

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

        // HeapAlloc(hHeap=RAX, dwFlags=HEAP_ZERO_MEMORY, dwBytes=size)
        // HEAP_ZERO_MEMORY (0x8) ensures allocated memory is zeroed
        try self.enc.movRcxRax();
        // mov rdx, 8 (HEAP_ZERO_MEMORY flag)
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC2, 0x08, 0x00, 0x00, 0x00 }); // mov rdx, imm32
        try self.enc.movR8R12();

        try self.emitExternalCall("kernel32.dll", "HeapAlloc");

        // If tracking enabled, call __track_alloc(ptr=RCX, size=RDX, tag_ptr=R8, tag_len=R9)
        if (self.track_memory) {
            // Save ptr to R13 across the tracking call
            try self.enc.emit(&.{ 0x49, 0x89, 0xC5 }); // mov r13, rax

            // Get tag from IR instruction (genHeapAlloc)
            const tag = inst.operands[1].alloc_tag;
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

        // Restore callee-saved registers
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12

        try self.storeToStack(inst.result.?, .ptr);
        try self.markIndirect(inst.result.?);
    }

    fn genHeapFree(self: *IrCodegen, inst: ir.Instruction) !void {
        const ptr_val = inst.operands[0].value;
        debug.codegen("  HeapFree: ptr=%{d}", .{ptr_val});

        // Save callee-saved registers we'll use (R12, R13, R14, R15)
        // Push four to maintain 16-byte stack alignment
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15

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
        if (self.track_memory) {
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

            // Get tag from IR instruction
            const tag = inst.operands[1].alloc_tag;
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

        // Restore callee-saved registers
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
    }

    fn genHeapRealloc(self: *IrCodegen, inst: ir.Instruction) !void {
        const old_ptr = inst.operands[0].value;
        const new_size = inst.operands[1].value;
        debug.codegen("  HeapRealloc: old_ptr=%{d}, new_size=%{d}", .{ old_ptr, new_size });

        // Save callee-saved registers we'll use (R12, R13, R14, R15)
        // Push four to maintain 16-byte stack alignment
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13
        try self.enc.emit(&.{ 0x41, 0x56 }); // push r14
        try self.enc.emit(&.{ 0x41, 0x57 }); // push r15

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
        if (self.track_memory) {
            // Save ptr to R12 across the tracking call
            try self.enc.movR12Rax();

            // Get tag from IR instruction
            const tag = inst.operands[2].alloc_tag;
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
        if (self.track_memory) {
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
        if (self.track_memory) {
            // Get tag from IR instruction
            const tag = inst.operands[2].alloc_tag;
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

        // Restore callee-saved registers
        try self.enc.emit(&.{ 0x41, 0x5F }); // pop r15
        try self.enc.emit(&.{ 0x41, 0x5E }); // pop r14
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12

        // Result is in RAX
        try self.storeToStack(inst.result.?, .ptr);
        try self.markIndirect(inst.result.?);
    }

    fn genTrackMove(self: *IrCodegen, inst: ir.Instruction) !void {
        if (!self.track_memory) return;

        const tag = inst.operands[0].alloc_tag;
        debug.codegen("  TrackMove: tag={s}", .{tag});

        // Embed tag string after a jump
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

        // Call __track_move(from_var_ptr=RCX, from_var_len=RDX, to_var_ptr=R8, to_var_len=R9)
        // For simplicity, we use the same tag for both from and to (the destination variable name)
        // LEA RCX, [RIP - offset_to_tag]
        const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
        try self.enc.emit(&.{ 0x48, 0x8D, 0x0D }); // lea rcx, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // mov rdx, tag_len
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC2 }); // mov rdx, imm32
        try self.enc.emitI32(@intCast(tag.len));

        // Same tag for to_var (we could extend this later to track source->dest)
        // lea r8, [rip+disp32] - need new lea
        const lea2_pos = self.code.items.len;
        const rip_offset2: i32 = @intCast(@as(i64, @intCast(tag_pos)) - @as(i64, @intCast(lea2_pos + 7)));
        try self.enc.emit(&.{ 0x4C, 0x8D, 0x05 }); // lea r8, [rip+disp32]
        try self.enc.emitI32(rip_offset2);

        // mov r9, tag_len
        try self.enc.emit(&.{ 0x49, 0xC7, 0xC1 }); // mov r9, imm32
        try self.enc.emitI32(@intCast(tag.len));

        try self.emitInternalCall("__track_move");
    }

    fn genTrackIncref(self: *IrCodegen, inst: ir.Instruction) !void {
        if (!self.track_memory) return;

        const tag = inst.operands[0].alloc_tag;
        const new_refcount = inst.operands[1].value;
        debug.codegen("  TrackIncref: tag={s}, new_rc=%{d}", .{ tag, new_refcount });

        // Save R12 and R13 since we use R12 temporarily (R12 is callee-saved)
        // Push two registers to maintain 16-byte stack alignment
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13

        // Save new_refcount to R12 before string embedding
        try self.loadToRax(new_refcount);
        try self.enc.movR12Rax();

        // Embed tag string after a jump
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

        // Call __track_incref(var_ptr=RCX, var_len=RDX, new_refcount=R8)
        // LEA RCX, [RIP - offset_to_tag]
        const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
        try self.enc.emit(&.{ 0x48, 0x8D, 0x0D }); // lea rcx, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // mov rdx, tag_len
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC2 }); // mov rdx, imm32
        try self.enc.emitI32(@intCast(tag.len));

        // mov r8, r12 (new_refcount)
        try self.enc.emit(&.{ 0x4D, 0x89, 0xE0 }); // mov r8, r12

        try self.emitInternalCall("__track_incref");

        // Restore R13 and R12
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
    }

    fn genTrackDecref(self: *IrCodegen, inst: ir.Instruction) !void {
        if (!self.track_memory) return;

        const tag = inst.operands[0].alloc_tag;
        const new_refcount = inst.operands[1].value;
        debug.codegen("  TrackDecref: tag={s}, new_rc=%{d}", .{ tag, new_refcount });

        // Save R12 and R13 since we use R12 temporarily (R12 is callee-saved)
        // Push two registers to maintain 16-byte stack alignment
        try self.enc.emit(&.{ 0x41, 0x54 }); // push r12
        try self.enc.emit(&.{ 0x41, 0x55 }); // push r13

        // Save new_refcount to R12 before string embedding
        try self.loadToRax(new_refcount);
        try self.enc.movR12Rax();

        // Embed tag string after a jump
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

        // Call __track_decref(var_ptr=RCX, var_len=RDX, new_refcount=R8)
        // LEA RCX, [RIP - offset_to_tag]
        const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
        try self.enc.emit(&.{ 0x48, 0x8D, 0x0D }); // lea rcx, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // mov rdx, tag_len
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC2 }); // mov rdx, imm32
        try self.enc.emitI32(@intCast(tag.len));

        // mov r8, r12 (new_refcount)
        try self.enc.emit(&.{ 0x4D, 0x89, 0xE0 }); // mov r8, r12

        try self.emitInternalCall("__track_decref");

        // Restore R13 and R12
        try self.enc.emit(&.{ 0x41, 0x5D }); // pop r13
        try self.enc.emit(&.{ 0x41, 0x5C }); // pop r12
    }

    fn genTrackCopy(self: *IrCodegen, inst: ir.Instruction) !void {
        if (!self.track_memory) return;

        const tag = inst.operands[0].alloc_tag;
        debug.codegen("  TrackCopy: tag={s}", .{tag});

        // Embed tag string after a jump
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

        // Call __track_copy(var_ptr=RCX, var_len=RDX)
        const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
        try self.enc.emit(&.{ 0x48, 0x8D, 0x0D }); // lea rcx, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // mov rdx, tag_len
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC2 }); // mov rdx, imm32
        try self.enc.emitI32(@intCast(tag.len));

        try self.emitInternalCall("__track_copy");
    }

    fn genTrackCleanup(self: *IrCodegen, inst: ir.Instruction) !void {
        if (!self.track_memory) return;

        const tag = inst.operands[0].alloc_tag;
        debug.codegen("  TrackCleanup: tag={s}", .{tag});

        // Embed tag string after a jump
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

        // Call __track_cleanup(var_ptr=RCX, var_len=RDX)
        const rip_offset: i32 = -@as(i32, @intCast(after_tag - tag_pos + 7));
        try self.enc.emit(&.{ 0x48, 0x8D, 0x0D }); // lea rcx, [rip+disp32]
        try self.enc.emitI32(rip_offset);

        // mov rdx, tag_len
        try self.enc.emit(&.{ 0x48, 0xC7, 0xC2 }); // mov rdx, imm32
        try self.enc.emitI32(@intCast(tag.len));

        try self.emitInternalCall("__track_cleanup");
    }

    fn emitTrackFreeCall(self: *IrCodegen, tag: []const u8) !void {
        // Track free: ptr in R12, size in R15
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
