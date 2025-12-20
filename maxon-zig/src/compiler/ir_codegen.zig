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

    fn loadToXmm(self: *IrCodegen, val: ir.Value, target: x86.Xmm) !void {
        try self.loadValue(val, .{ .xmm = target });
    }

    // ------------------------------------------------------------------------
    // Module / Function Generation
    // ------------------------------------------------------------------------

    pub fn generateModule(self: *IrCodegen, module: ir.Module) !void {
        // Generate _start wrapper if tracking is enabled
        if (self.track_allocs) {
            try self.generateStartWrapper();
        }

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
        try self.enc.allocShadowSpace();
        const enable_patch = try self.enc.callRel32();
        try self.call_patches.append(self.allocator, .{
            .offset = enable_patch,
            .target_func = "__enable_alloc_tracking",
        });
        try self.enc.freeShadowSpace();

        // Call main
        try self.enc.allocShadowSpace();
        const main_patch = try self.enc.callRel32();
        try self.call_patches.append(self.allocator, .{
            .offset = main_patch,
            .target_func = "main",
        });
        try self.enc.freeShadowSpace();

        // Save main's return value to R12 (callee-saved)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC4 }); // mov r12, rax

        // Call __print_alloc_summary
        try self.enc.allocShadowSpace();
        const summary_patch = try self.enc.callRel32();
        try self.call_patches.append(self.allocator, .{
            .offset = summary_patch,
            .target_func = "__print_alloc_summary",
        });
        try self.enc.freeShadowSpace();

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

        // WriteFile(hFile=R14, lpBuffer=R13, nNumberOfBytesToWrite=len, lpNumberOfBytesWritten=NULL, lpOverlapped=NULL)
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF1 }); // mov rcx, r14 (handle)
        try self.enc.emit(&.{ 0x4C, 0x89, 0xEA }); // mov rdx, r13 (buffer)
        try self.enc.emit(&.{ 0x41, 0xB8 }); // mov r8d, imm32 (length)
        try self.enc.emitI32(@intCast(string_len));
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (NULL)
        // Push NULL for lpOverlapped (5th arg on stack)
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x28 }); // sub rsp, 40 (shadow + arg)
        try self.enc.emit(&.{ 0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00 }); // mov qword [rsp+32], 0
        const write_patch = try self.enc.callIndirectRip();
        try self.external_patches.append(self.allocator, .{
            .offset = write_patch,
            .dll = "kernel32.dll",
            .func_name = "WriteFile",
        });
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x28 }); // add rsp, 40
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
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF9 }); // mov rcx, r15 (handle)
        try self.enc.emit(&.{ 0x48, 0x89, 0xC2 }); // mov rdx, rax (buffer)
        try self.enc.emit(&.{ 0x41, 0xB8 }); // mov r8d, imm32 (length)
        try self.enc.emitI32(@intCast(string_len));
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (NULL)
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x28 }); // sub rsp, 40
        try self.enc.emit(&.{ 0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00 }); // mov qword [rsp+32], 0
        const write_patch = try self.enc.callIndirectRip();
        try self.external_patches.append(self.allocator, .{
            .offset = write_patch,
            .dll = "kernel32.dll",
            .func_name = "WriteFile",
        });
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x28 }); // add rsp, 40
        try self.enc.popRax();
    }

    /// Helper to print tag string from R12 (ptr) with length from [rbp-48]
    /// Assumes R15 = stdout handle
    fn printTagFromR12(self: *IrCodegen) !void {
        // Load tag length from stack
        try self.enc.emit(&.{ 0x48, 0x8B, 0x45, 0xD0 }); // mov rax, [rbp-48] (tag len)

        // WriteFile(hFile=R15, lpBuffer=R12, nBytes=rax, lpWritten=NULL, lpOverlapped=NULL)
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF9 }); // mov rcx, r15 (handle)
        try self.enc.emit(&.{ 0x4C, 0x89, 0xE2 }); // mov rdx, r12 (buffer = tag ptr)
        try self.enc.emit(&.{ 0x49, 0x89, 0xC0 }); // mov r8, rax (length)
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (NULL)
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x28 }); // sub rsp, 40
        try self.enc.emit(&.{ 0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00 }); // mov qword [rsp+32], 0
        const write_patch = try self.enc.callIndirectRip();
        try self.external_patches.append(self.allocator, .{
            .offset = write_patch,
            .dll = "kernel32.dll",
            .func_name = "WriteFile",
        });
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x28 }); // add rsp, 40
    }

    /// Emit WriteFile call with buffer in RDX and length in R8
    /// Assumes R15 = stdout handle
    fn emitWriteFileCall(self: *IrCodegen) !void {
        try self.enc.emit(&.{ 0x4C, 0x89, 0xF9 }); // mov rcx, r15 (handle)
        // RDX already has buffer, R8 already has length
        try self.enc.emit(&.{ 0x4D, 0x31, 0xC9 }); // xor r9, r9 (NULL)
        try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x28 }); // sub rsp, 40
        try self.enc.emit(&.{ 0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00 }); // mov qword [rsp+32], 0
        const write_patch = try self.enc.callIndirectRip();
        try self.external_patches.append(self.allocator, .{
            .offset = write_patch,
            .dll = "kernel32.dll",
            .func_name = "WriteFile",
        });
        try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x28 }); // add rsp, 40
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
        self.block_offsets.clearRetainingCapacity();
        self.jump_patches.clearRetainingCapacity();
        self.next_stack_offset = -8;
        self.current_func_name = func.name;
        self.current_func_ret_type = func.return_type;

        // Emit prologue with placeholder stack size (will be patched after)
        const func_start = self.code.items.len;
        try self.enc.prologue(0);

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

        // Calculate actual stack size and patch the prologue
        // next_stack_offset is negative, representing bytes below rbp
        // Add 8 for alignment and round up to 16-byte boundary (Windows x64 ABI)
        const stack_used: i32 = -self.next_stack_offset;
        const stack_size: i32 = (stack_used + 15) & ~@as(i32, 15); // Round up to 16
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
            .fabs => try self.genFabs(inst),
            .ret => try self.genRet(inst),
            .param => try self.genParam(inst),
            .call => try self.genCall(inst),
            .memcpy => try self.genMemcpy(inst),
            .heap_alloc => try self.genHeapAlloc(inst),
            .heap_free => try self.genHeapFree(inst),
            .heap_realloc => try self.genHeapRealloc(inst),
            .fcmp_eq => try self.genFcmpEq(inst),
            .icmp_eq => try self.genIcmp(inst, .eq),
            .icmp_ne => try self.genIcmp(inst, .ne),
            .icmp_lt => try self.genIcmp(inst, .lt),
            .icmp_le => try self.genIcmp(inst, .le),
            .icmp_gt => try self.genIcmp(inst, .gt),
            .icmp_ge => try self.genIcmp(inst, .ge),
            .br => try self.genBr(inst),
            .br_cond => try self.genBrCond(inst),
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
        try self.value_types.put(self.allocator, inst.result.?, inst.result_type);
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
        const val_type = self.value_types.get(val) orelse return error.ValueTypeNotFound;

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
                const val_offset = try self.getStackOffset(val);
                try self.enc.leaRaxRbpOffset(val_offset);
            } else {
                try self.loadToRax(val);
            }
            if (self.isIndirect(ptr)) {
                try self.enc.movMemRax(.rcx);
            } else {
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
        // When loading a pointer, mark result as indirect because the loaded value
        // is itself a pointer that points elsewhere (e.g., heap memory)
        if (ty == .ptr) {
            try self.markIndirect(result);
        }
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

    fn genFabs(self: *IrCodegen, inst: ir.Instruction) !void {
        // Load value to xmm0
        try self.loadToXmm(inst.operands[0].value, .xmm0);
        // Clear sign bit: AND with 0x7FFFFFFFFFFFFFFF
        try self.enc.fabsXmm0();
        try self.storeToStack(inst.result.?, .f64);
    }

    fn genRet(self: *IrCodegen, inst: ir.Instruction) !void {
        if (inst.operands[0] != .none) {
            const ret_val = inst.operands[0].value;
            const ret_type = self.value_types.get(ret_val) orelse return error.ValueTypeNotFound;

            debug.codegen("  ret: val=%{d}, type={s}, func={s}", .{ ret_val, ret_type.format(), self.current_func_name });

            if (ret_type == .f64) {
                try self.loadToXmm(ret_val, .xmm0);
            } else {
                try self.loadToRax(ret_val);
            }
        }

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

    /// Load arguments into registers. use_lea controls whether direct pointers use LEA.
    fn loadArgs(self: *IrCodegen, args: []const ir.Value, use_lea: bool) !void {
        for (args, 0..) |arg, i| {
            if (i >= 4) return error.TooManyArguments;

            const arg_type = self.value_types.get(arg) orelse return error.ValueTypeNotFound;

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

        // Check for zero-size allocation - skip allocation and return null
        // test r12, r12
        try self.enc.emit(&.{ 0x4D, 0x85, 0xE4 });
        // jnz do_alloc (jump if size != 0)
        const zero_check_jnz = try self.enc.jneRel32();

        // Size is 0 - set result to null and skip allocation
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
            try self.enc.allocShadowSpace();
            const patch_offset = try self.enc.callRel32();
            try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = "__track_alloc" });
            try self.enc.freeShadowSpace();
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
            try self.enc.allocShadowSpace();
            const patch_offset = try self.enc.callRel32();
            try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = "__track_free" });
            try self.enc.freeShadowSpace();
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
            try self.enc.allocShadowSpace();
            const track_patch = try self.enc.callRel32();
            try self.call_patches.append(self.allocator, .{ .offset = track_patch, .target_func = "__track_alloc" });
            try self.enc.freeShadowSpace();
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
            try self.enc.emit(&.{ 0x48, 0x83, 0xEC, 0x38 }); // sub rsp, 56 (shadow + 2 args)

            // LEA RAX, [RIP + disp32] for tag_ptr
            // RIP will point after the LEA instruction (7 bytes: 3 opcode + 4 disp)
            // We need to compute offset from that RIP to tag_pos
            const lea_pos = self.code.items.len;
            const rip_after_lea = lea_pos + 7;
            const rip_offset: i32 = @intCast(@as(i64, @intCast(tag_pos)) - @as(i64, @intCast(rip_after_lea)));
            try self.enc.emit(&.{ 0x48, 0x8D, 0x05 }); // lea rax, [rip+disp32]
            try self.enc.emitI32(rip_offset);
            try self.enc.emit(&.{ 0x48, 0x89, 0x44, 0x24, 0x20 }); // mov [rsp+32], rax (tag_ptr)

            // mov [rsp+40], tag_len
            try self.enc.emit(&.{ 0x48, 0xC7, 0x44, 0x24, 0x28 }); // mov qword [rsp+40], imm32
            try self.enc.emitI32(@intCast(tag.len));

            const track_patch = try self.enc.callRel32();
            try self.call_patches.append(self.allocator, .{ .offset = track_patch, .target_func = "__track_realloc" });
            try self.enc.emit(&.{ 0x48, 0x83, 0xC4, 0x38 }); // add rsp, 56

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
        try self.enc.allocShadowSpace();
        const patch_offset = try self.enc.callRel32();
        try self.call_patches.append(self.allocator, .{ .offset = patch_offset, .target_func = "__track_free" });
        try self.enc.freeShadowSpace();
    }

    fn genFcmpEq(self: *IrCodegen, inst: ir.Instruction) !void {
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

        // Set AL = 1 if equal (and not unordered)
        // SETE al + SETNP cl, then AND them
        // For simplicity, just use SETE (this works for non-NaN values)
        try self.enc.seteAl();
        try self.enc.movzxRaxAl();

        try self.storeToStack(result, .i64);
    }

    const CmpOp = enum { eq, ne, lt, le, gt, ge };

    fn genIcmp(self: *IrCodegen, inst: ir.Instruction, op: CmpOp) !void {
        const result = inst.result.?;
        const lhs = inst.operands[0].value;
        const rhs = inst.operands[1].value;

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
        const else_block: u32 = @intCast(inst.result.?); // else block stored in result field

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
