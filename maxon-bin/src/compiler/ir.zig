const std = @import("std");
const debug = @import("debug.zig");

/// IR Value reference - %0, %1, etc.
pub const Value = u32;

// ============================================================================
// Type-Safe Pointer Wrappers
//
// These provide compile-time safety for distinguishing pointer kinds.
// All are zero-cost at runtime (same size as Value = u32).
// ============================================================================

/// Generic pointer wrapper - parameterized by a marker type for specific structs
pub fn Ptr(comptime Tag: type) type {
    return struct {
        val: Value,

        const Self = @This();

        pub fn raw(self: Self) Value {
            return self.val;
        }

        /// Convert to generic StructPtr when needed
        pub fn asStructPtr(self: Self) StructPtr {
            return .{ .val = self.val };
        }

        /// Convert to RawPtr for memcpy operations
        pub fn asRawPtr(self: Self) RawPtr {
            return .{ .val = self.val };
        }

        // Marker type is used only for compile-time distinction
        pub const marker = Tag;
    };
}

// Marker types for specific structs (zero-size, compile-time only)
pub const ManagedMemoryTag = struct {};
pub const MapTag = struct {};
pub const ErrorUnionTag = struct {};

// Type aliases for common pointer types
pub const ManagedMemoryPtr = Ptr(ManagedMemoryTag); // Pointer to __ManagedMemory
pub const MapPtr = Ptr(MapTag); // Pointer to Map struct
pub const ErrorUnionPtr = Ptr(ErrorUnionTag); // Pointer to error union

/// Generic struct pointer (for field access on unknown struct types)
pub const StructPtr = struct {
    val: Value,

    pub fn raw(self: StructPtr) Value {
        return self.val;
    }

    /// Convert to RawPtr for memcpy operations
    pub fn asRawPtr(self: StructPtr) RawPtr {
        return .{ .val = self.val };
    }
};

/// Pointer to raw memory buffer (no layout assumptions)
pub const RawPtr = struct {
    val: Value,

    pub fn raw(self: RawPtr) Value {
        return self.val;
    }

    /// Convert raw memory to struct pointer (after you've initialized it as a struct)
    pub fn asStruct(self: RawPtr) StructPtr {
        return .{ .val = self.val };
    }

    pub fn asManagedMemoryPtr(self: RawPtr) ManagedMemoryPtr {
        return .{ .val = self.val };
    }

    pub fn asMapPtr(self: RawPtr) MapPtr {
        return .{ .val = self.val };
    }

    pub fn asErrorUnionPtr(self: RawPtr) ErrorUnionPtr {
        return .{ .val = self.val };
    }
};

/// Pointer to an array element
pub const ElemPtr = struct {
    val: Value,

    pub fn raw(self: ElemPtr) Value {
        return self.val;
    }

    /// Convert to StructPtr (when element is a struct)
    pub fn asStructPtr(self: ElemPtr) StructPtr {
        return .{ .val = self.val };
    }

    /// Convert to RawPtr
    pub fn asRawPtr(self: ElemPtr) RawPtr {
        return .{ .val = self.val };
    }
};

/// Pointer to a slot containing another pointer (indirection)
pub const SlotPtr = struct {
    val: Value,

    pub fn raw(self: SlotPtr) Value {
        return self.val;
    }
};

/// Pointer to a function
pub const FuncPtr = struct {
    val: Value,

    pub fn raw(self: FuncPtr) Value {
        return self.val;
    }
};

// ============================================================================
// Conversion helpers - for explicit conversions when you know the pointer kind
// ============================================================================

/// Convert raw Value to StructPtr (use when you know it's a struct pointer)
pub fn toStructPtr(val: Value) StructPtr {
    return .{ .val = val };
}

/// Convert raw Value to RawPtr (use when you know it's a raw pointer)
pub fn toRawPtr(val: Value) RawPtr {
    return .{ .val = val };
}

/// Convert raw Value to ElemPtr
pub fn toElemPtr(val: Value) ElemPtr {
    return .{ .val = val };
}

/// Convert raw Value to ManagedMemoryPtr
pub fn toManagedMemoryPtr(val: Value) ManagedMemoryPtr {
    return .{ .val = val };
}

/// Convert raw Value to MapPtr
pub fn toMapPtr(val: Value) MapPtr {
    return .{ .val = val };
}

/// Convert raw Value to ErrorUnionPtr
pub fn toErrorUnionPtr(val: Value) ErrorUnionPtr {
    return .{ .val = val };
}

/// Convert raw Value to FuncPtr
pub fn toFuncPtr(val: Value) FuncPtr {
    return .{ .val = val };
}

/// IR Type
pub const Type = enum {
    i8, // byte type
    i32,
    i64,
    f64,
    void,
    ptr,

    /// Returns the IR-level type name (i8, i32, i64, f64, ptr, void)
    pub fn toIrName(self: Type) []const u8 {
        return switch (self) {
            .i8 => "i8",
            .i32 => "i32",
            .i64 => "i64",
            .f64 => "f64",
            .void => "void",
            .ptr => "ptr",
        };
    }

    /// Returns the user-facing Maxon type name (byte, int, float, ptr, void)
    pub fn toMaxonName(self: Type) []const u8 {
        return switch (self) {
            .i8 => "byte",
            .i32 => "int",
            .i64 => "int",
            .f64 => "float",
            .ptr => "ptr",
            .void => "void",
        };
    }

    /// Returns the size in bytes for this IR type
    /// Consolidates 5+ duplicated switch statements across the codebase
    pub fn sizeInBytes(self: Type) i32 {
        return switch (self) {
            .i64, .f64, .ptr => 8,
            .i32 => 4,
            .i8 => 1,
            .void => 0,
        };
    }

    /// Returns true if this is an integral type (i8, i32, i64)
    pub fn isIntegral(self: Type) bool {
        return switch (self) {
            .i8, .i32, .i64 => true,
            .f64, .void, .ptr => false,
        };
    }

    /// Returns true if this is a floating-point type (f64)
    pub fn isFloatingPoint(self: Type) bool {
        return self == .f64;
    }

    /// Returns true if this is a numeric type (integral or floating-point)
    pub fn isNumeric(self: Type) bool {
        return self.isIntegral() or self.isFloatingPoint();
    }

    /// Returns true if this is a signed type
    /// Note: All Maxon integral types are signed (i8, i32, i64)
    pub fn isSigned(self: Type) bool {
        return self.isIntegral();
    }

    /// Returns the alignment requirement in bytes
    pub fn alignment(self: Type) i32 {
        return self.sizeInBytes(); // Natural alignment
    }
};

/// IR Instruction
pub const Instruction = struct {
    op: Op,
    result: ?Value = null,
    result_type: Type = .void,
    operands: [3]Operand = .{ .none, .none, .none },

    pub const Op = enum {
        // Constants
        const_i8,
        const_i32,
        const_i64,
        const_f64,
        const_string, // string constant (pointer to data section)

        // Memory
        alloca,
        alloca_sized,
        alloca_dynamic, // alloca with runtime size value
        load,
        store,
        store_i8, // store byte
        store_i32, // store 32-bit int
        getfieldptr,

        // Integer arithmetic
        add,
        sub,
        mul,
        div,
        mod,

        // Bitwise operations
        band, // bitwise AND
        bitor, // bitwise OR
        bxor, // bitwise XOR
        shl, // left shift
        shr, // right shift (arithmetic)

        // Float arithmetic
        fadd,
        fsub,
        fmul,
        fdiv,

        // Conversions
        fptosi, // float to signed int
        sitofp, // signed int to float
        fabs, // float absolute value
        fsqrt, // float square root
        fceil, // ceiling of float to int (round toward positive infinity)
        ffloor, // floor of float to int (round toward negative infinity)
        fround, // round float to nearest int (round to nearest even)
        bitcast_f64_to_i64, // reinterpret f64 bits as i64 (for hashing)
        bitcast_i64_to_f64, // reinterpret i64 bits as f64 (for float-backed enums)
        sext_i32_i64, // sign-extend i32 to i64
        trunc_i64_i32, // truncate i64 to i32
        trunc_i64_i8, // truncate i64 to i8 (byte)
        zext_i8_i64, // zero-extend i8 (byte) to i64

        // Control flow
        ret,
        br,
        br_cond,

        // Integer comparison
        icmp_eq,
        icmp_ne,
        icmp_lt,
        icmp_le,
        icmp_gt,
        icmp_ge,

        // Float comparison
        fcmp_eq,
        fcmp_ne,
        fcmp_lt,
        fcmp_le,
        fcmp_gt,
        fcmp_ge,

        // Function call
        call,
        call_indirect, // Indirect function call through pointer
        func_addr, // Get address of named function

        // Parameters
        param,

        // Array operations
        getelemptr, // Get pointer to array element

        // Memory operations
        memcpy, // Copy memory: dest, src, size (static i32 size in result field)
        memcpy_dyn, // Copy memory with dynamic size: dest, src, size_value
        memset, // Set memory: dest, value, size (value is typically 0)
        memset_dyn, // Set memory with dynamic size: dest, value, size_value
        cstr_len, // Get length of null-terminated C string: ptr -> i64

        // Heap allocation
        heap_alloc, // Allocate heap memory, returns ptr
        heap_free, // Free heap memory
        heap_realloc, // Reallocate heap memory: old_ptr, new_size -> new_ptr

        // Memory tracking (for --track-memory)
        track_move, // Track ownership transfer: tag_ptr, tag_len
        track_incref, // Track reference count increment: tag_ptr, tag_len, new_refcount
        track_decref, // Track reference count decrement: tag_ptr, tag_len, new_refcount

        // External DLL calls
        extern_call, // Call external DLL function: dll_name:func_name, args -> result

        pub fn format(self: Op) []const u8 {
            return switch (self) {
                .const_i8 => "const.i8",
                .const_i32 => "const.i32",
                .const_i64 => "const.i64",
                .const_f64 => "const.f64",
                .const_string => "const.string",
                .alloca => "alloca",
                .alloca_sized => "alloca.sized",
                .alloca_dynamic => "alloca.dynamic",
                .load => "load",
                .store => "store",
                .store_i8 => "store.i8",
                .store_i32 => "store.i32",
                .getfieldptr => "getfieldptr",
                .add => "add",
                .sub => "sub",
                .mul => "mul",
                .div => "div",
                .mod => "mod",
                .band => "band",
                .bitor => "bitor",
                .bxor => "bxor",
                .shl => "shl",
                .shr => "shr",
                .fadd => "fadd",
                .fsub => "fsub",
                .fmul => "fmul",
                .fdiv => "fdiv",
                .fptosi => "fptosi",
                .sitofp => "sitofp",
                .fabs => "fabs",
                .fsqrt => "fsqrt",
                .fceil => "fceil",
                .ffloor => "ffloor",
                .fround => "fround",
                .bitcast_f64_to_i64 => "bitcast.f64.i64",
                .bitcast_i64_to_f64 => "bitcast.i64.f64",
                .sext_i32_i64 => "sext.i32.i64",
                .trunc_i64_i32 => "trunc.i64.i32",
                .trunc_i64_i8 => "trunc.i64.i8",
                .zext_i8_i64 => "zext.i8.i64",
                .ret => "ret",
                .br => "br",
                .br_cond => "br.cond",
                .icmp_eq => "icmp.eq",
                .icmp_ne => "icmp.ne",
                .icmp_lt => "icmp.lt",
                .icmp_le => "icmp.le",
                .icmp_gt => "icmp.gt",
                .icmp_ge => "icmp.ge",
                .fcmp_eq => "fcmp.eq",
                .fcmp_ne => "fcmp.ne",
                .fcmp_lt => "fcmp.lt",
                .fcmp_le => "fcmp.le",
                .fcmp_gt => "fcmp.gt",
                .fcmp_ge => "fcmp.ge",
                .call => "call",
                .call_indirect => "call.indirect",
                .func_addr => "func.addr",
                .param => "param",
                .getelemptr => "getelemptr",
                .memcpy => "memcpy",
                .memcpy_dyn => "memcpy.dyn",
                .memset => "memset",
                .memset_dyn => "memset.dyn",
                .cstr_len => "cstr.len",
                .heap_alloc => "heap.alloc",
                .heap_free => "heap.free",
                .heap_realloc => "heap.realloc",
                .track_move => "track.move",
                .track_incref => "track.incref",
                .track_decref => "track.decref",
                .extern_call => "extern.call",
            };
        }
    };

    pub const Operand = union(enum) {
        none,
        value: Value,
        immediate_i32: i32,
        immediate_i64: i64,
        immediate_f64: f64,
        block_ref: u32,
        func_name: []const u8,
        call_args: []const Value,
        elem_size: i32, // Element size for getelemptr
        string_data: []const u8, // String constant data
        alloc_tag: []const u8, // Tag for heap allocations (for tracking)
        extern_func: struct { // External DLL function reference
            dll_name: []const u8,
            func_name: []const u8,
        },
    };
};

/// Basic Block
pub const BasicBlock = struct {
    name: []const u8,
    instructions: std.ArrayListUnmanaged(Instruction),

    pub fn init(name: []const u8) BasicBlock {
        return .{
            .name = name,
            .instructions = .empty,
        };
    }

    pub fn deinit(self: *BasicBlock, allocator: std.mem.Allocator) void {
        self.instructions.deinit(allocator);
    }
};

/// IR Function
pub const Function = struct {
    name: []const u8,
    return_type: Type,
    is_exported: bool,
    blocks: std.ArrayListUnmanaged(BasicBlock),
    next_value: Value,
    allocator: std.mem.Allocator,
    // SSA value naming for readable IR output
    value_names: std.AutoHashMapUnmanaged(Value, []const u8),
    name_counters: std.StringHashMapUnmanaged(u32),
    allocated_names: std.ArrayListUnmanaged([]const u8), // Track strings we allocated

    pub fn init(allocator: std.mem.Allocator, name: []const u8, return_type: Type) Function {
        return initWithExport(allocator, name, return_type, false);
    }

    pub fn initWithExport(allocator: std.mem.Allocator, name: []const u8, return_type: Type, is_exported: bool) Function {
        return .{
            .name = name,
            .return_type = return_type,
            .is_exported = is_exported,
            .blocks = .empty,
            .next_value = 0,
            .allocator = allocator,
            .value_names = .empty,
            .name_counters = .empty,
            .allocated_names = .empty,
        };
    }

    pub fn deinit(self: *Function) void {
        for (self.blocks.items) |*block| {
            // Free call_args slices in instructions
            for (block.instructions.items) |inst| {
                for (inst.operands) |op| {
                    if (op == .call_args) {
                        self.allocator.free(op.call_args);
                    }
                }
            }
            block.deinit(self.allocator);
        }
        self.blocks.deinit(self.allocator);
        // Free allocated name strings
        for (self.allocated_names.items) |name| {
            self.allocator.free(name);
        }
        self.allocated_names.deinit(self.allocator);
        self.value_names.deinit(self.allocator);
        self.name_counters.deinit(self.allocator);
    }

    pub fn newValue(self: *Function) Value {
        const v = self.next_value;
        self.next_value += 1;
        return v;
    }

    pub fn addBlock(self: *Function, name: []const u8) !*BasicBlock {
        try self.blocks.append(self.allocator, BasicBlock.init(name));
        return &self.blocks.items[self.blocks.items.len - 1];
    }

    pub fn currentBlock(self: *Function) ?*BasicBlock {
        if (self.blocks.items.len > 0) {
            return &self.blocks.items[self.blocks.items.len - 1];
        }
        return null;
    }

    /// Set a debug name for an SSA value. Names are made unique automatically.
    pub fn setValueName(self: *Function, val: Value, base_name: []const u8) !void {
        const entry = try self.name_counters.getOrPut(self.allocator, base_name);
        if (!entry.found_existing) {
            entry.value_ptr.* = 0;
        }
        const counter = entry.value_ptr.*;
        entry.value_ptr.* = counter + 1;

        // Generate unique name: "x" -> "x", then "x.1", "x.2", etc.
        const unique_name = if (counter == 0)
            base_name
        else blk: {
            const allocated = try std.fmt.allocPrint(self.allocator, "{s}.{d}", .{ base_name, counter });
            try self.allocated_names.append(self.allocator, allocated);
            break :blk allocated;
        };

        try self.value_names.put(self.allocator, val, unique_name);
    }

    /// Get the debug name for an SSA value, if one exists.
    pub fn getValueName(self: *const Function, val: Value) ?[]const u8 {
        return self.value_names.get(val);
    }

    /// Try to get the constant i64 value if this Value is a const_i64 instruction.
    /// Also traces through loads to find the stored constant value.
    /// Returns null if the value is not a constant or cannot be determined.
    pub fn getConstantI64(self: *const Function, val: Value) ?i64 {
        for (self.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                if (inst.result == val) {
                    if (inst.op == .const_i64) {
                        return inst.operands[0].immediate_i64;
                    }
                    // Trace through loads: find what was stored to this address
                    if (inst.op == .load) {
                        const ptr = inst.operands[0].value;
                        // Find the most recent store to this pointer
                        return self.getStoredConstant(ptr);
                    }
                }
            }
        }
        return null;
    }

    /// Find the constant value stored to a pointer, if any.
    fn getStoredConstant(self: *const Function, ptr: Value) ?i64 {
        // Search backwards through instructions to find the store
        var last_stored_val: ?Value = null;
        for (self.blocks.items) |block| {
            for (block.instructions.items) |inst| {
                if (inst.op == .store and inst.operands[0].value == ptr) {
                    last_stored_val = inst.operands[1].value;
                }
            }
        }
        if (last_stored_val) |stored_val| {
            // Recursively check if the stored value is a constant
            return self.getConstantI64(stored_val);
        }
        return null;
    }

    /// Get a base name for an operation type (temporary values).
    fn opToBaseName(op: Instruction.Op) []const u8 {
        return switch (op) {
            .const_i8 => "tmp_const",
            .const_i32 => "tmp_const",
            .const_i64 => "tmp_const",
            .const_f64 => "tmp_fconst",
            .const_string => "tmp_strconst",
            .alloca, .alloca_sized, .alloca_dynamic => "tmp_alloca",
            .load => "tmp_load",
            .store, .store_i8, .store_i32 => "tmp_store",
            .getfieldptr => "tmp_fieldptr",
            .add => "tmp_add",
            .sub => "tmp_sub",
            .mul => "tmp_mul",
            .div => "tmp_div",
            .mod => "tmp_mod",
            .band => "tmp_band",
            .bitor => "tmp_bitor",
            .bxor => "tmp_bxor",
            .shl => "tmp_shl",
            .shr => "tmp_shr",
            .fadd => "tmp_fadd",
            .fsub => "tmp_fsub",
            .fmul => "tmp_fmul",
            .fdiv => "tmp_fdiv",
            .fptosi => "tmp_fptosi",
            .sitofp => "tmp_sitofp",
            .fabs => "tmp_fabs",
            .fsqrt => "tmp_fsqrt",
            .fceil => "tmp_fceil",
            .ffloor => "tmp_ffloor",
            .fround => "tmp_fround",
            .bitcast_f64_to_i64, .bitcast_i64_to_f64 => "tmp_bitcast",
            .sext_i32_i64 => "tmp_sext",
            .trunc_i64_i32 => "tmp_trunc",
            .trunc_i64_i8 => "tmp_trunc_i8",
            .zext_i8_i64 => "tmp_zext",
            .ret => "tmp_ret",
            .br, .br_cond => "tmp_br",
            .icmp_eq, .icmp_ne, .icmp_lt, .icmp_le, .icmp_gt, .icmp_ge => "tmp_icmp",
            .fcmp_eq, .fcmp_ne, .fcmp_lt, .fcmp_le, .fcmp_gt, .fcmp_ge => "tmp_fcmp",
            .call => "tmp_call",
            .call_indirect => "tmp_call_ind",
            .extern_call => "tmp_extern",
            .func_addr => "tmp_funcaddr",
            .param => "tmp_param",
            .getelemptr => "tmp_elemptr",
            .memcpy => "tmp_memcpy",
            .memcpy_dyn => "tmp_memcpy",
            .memset => "tmp_memset",
            .memset_dyn => "tmp_memset",
            .cstr_len => "tmp_strlen",
            .heap_alloc => "tmp_heap",
            .heap_free => "tmp_free",
            .heap_realloc => "tmp_realloc",
            .track_move => "tmp_track",
            .track_incref => "tmp_track",
            .track_decref => "tmp_track",
        };
    }

    pub fn emit(self: *Function, inst: Instruction) !void {
        if (self.currentBlock()) |block| {
            try block.instructions.append(self.allocator, inst);
        }
    }

    // Core emit helpers
    fn emitWithResult(self: *Function, op: Instruction.Op, result_type: Type, operands: [3]Instruction.Operand) !Value {
        const result = self.newValue();
        try self.emit(.{ .op = op, .result = result, .result_type = result_type, .operands = operands });
        // Auto-name based on operation type
        try self.setValueName(result, opToBaseName(op));
        return result;
    }

    pub fn emitBinaryOp(self: *Function, op: Instruction.Op, lhs: Value, rhs: Value, ty: Type) !Value {
        return self.emitWithResult(op, ty, .{ .{ .value = lhs }, .{ .value = rhs }, .none });
    }

    pub fn emitUnaryOp(self: *Function, op: Instruction.Op, src: Value, ty: Type) !Value {
        return self.emitWithResult(op, ty, .{ .{ .value = src }, .none, .none });
    }

    // Constants
    pub fn emitConstI8(self: *Function, value: u8) !Value {
        return self.emitWithResult(.const_i8, .i8, .{ .{ .immediate_i32 = @intCast(value) }, .none, .none });
    }

    pub fn emitConstI32(self: *Function, value: i32) !Value {
        return self.emitWithResult(.const_i32, .i32, .{ .{ .immediate_i32 = value }, .none, .none });
    }

    pub fn emitConstI64(self: *Function, value: i64) !Value {
        return self.emitWithResult(.const_i64, .i64, .{ .{ .immediate_i64 = value }, .none, .none });
    }

    pub fn emitConstF64(self: *Function, value: f64) !Value {
        return self.emitWithResult(.const_f64, .f64, .{ .{ .immediate_f64 = value }, .none, .none });
    }

    /// Emit a string constant (pointer to data section)
    pub fn emitStringConstant(self: *Function, str: []const u8) !RawPtr {
        const val = try self.emitWithResult(.const_string, .ptr, .{ .{ .string_data = str }, .none, .none });
        return .{ .val = val };
    }

    // Memory - allocations return RawPtr (raw memory, no layout yet)
    pub fn emitAlloca(self: *Function, ty: Type) !RawPtr {
        _ = ty;
        const val = try self.emitWithResult(.alloca, .ptr, .{ .none, .none, .none });
        return .{ .val = val };
    }

    pub fn emitAllocaSized(self: *Function, size_bytes: i32) !RawPtr {
        if (size_bytes <= 0) return error.ZeroSizeAllocation;
        const val = try self.emitWithResult(.alloca_sized, .ptr, .{ .{ .immediate_i32 = size_bytes }, .none, .none });
        return .{ .val = val };
    }

    pub fn emitAllocaDynamic(self: *Function, size_value: Value) !RawPtr {
        // Note: for dynamic sizes, zero-check must happen at runtime
        const val = try self.emitWithResult(.alloca_dynamic, .ptr, .{ .{ .value = size_value }, .none, .none });
        return .{ .val = val };
    }

    // Load/Store - work with raw Value for now (callers use .raw())
    pub fn emitLoad(self: *Function, ptr: Value, ty: Type) !Value {
        return self.emitUnaryOp(.load, ptr, ty);
    }

    pub fn emitStore(self: *Function, ptr: Value, value: Value) !void {
        try self.emit(.{ .op = .store, .operands = .{ .{ .value = ptr }, .{ .value = value }, .none } });
    }

    pub fn emitStoreI8(self: *Function, ptr: Value, value: Value) !void {
        try self.emit(.{ .op = .store_i8, .operands = .{ .{ .value = ptr }, .{ .value = value }, .none } });
    }

    pub fn emitStoreI32(self: *Function, ptr: Value, value: Value) !void {
        try self.emit(.{ .op = .store_i32, .operands = .{ .{ .value = ptr }, .{ .value = value }, .none } });
    }

    // Struct field access - takes StructPtr, returns StructPtr
    pub fn emitGetFieldPtr(self: *Function, base_ptr: StructPtr, field_offset: i32) !StructPtr {
        const val = try self.emitWithResult(.getfieldptr, .ptr, .{ .{ .value = base_ptr.val }, .{ .immediate_i32 = field_offset }, .none });
        return .{ .val = val };
    }

    // Array element access - takes RawPtr, returns ElemPtr
    pub fn emitGetElemPtr(self: *Function, base_ptr: RawPtr, index: Value, elem_size: i32) !ElemPtr {
        const result = self.newValue();
        try self.emit(.{
            .op = .getelemptr,
            .result = result,
            .result_type = .ptr,
            .operands = .{ .{ .value = base_ptr.val }, .{ .value = index }, .{ .elem_size = elem_size } },
        });
        try self.setValueName(result, opToBaseName(.getelemptr));
        return .{ .val = result };
    }

    // Control flow
    pub fn emitRet(self: *Function, value: ?Value) !void {
        try self.emit(.{ .op = .ret, .operands = .{ if (value) |v| .{ .value = v } else .none, .none, .none } });
    }

    pub fn emitBr(self: *Function, block_idx: u32) !void {
        try self.emit(.{ .op = .br, .operands = .{ .{ .block_ref = block_idx }, .none, .none } });
    }

    pub fn emitBrCond(self: *Function, cond: Value, then_block: u32, else_block: u32) !void {
        try self.emit(.{
            .op = .br_cond,
            .operands = .{ .{ .value = cond }, .{ .block_ref = then_block }, .{ .block_ref = else_block } },
        });
    }

    // Function calls
    pub fn emitCall(self: *Function, func_name: []const u8, args: []const Value, ret_type: Type) !?Value {
        if (ret_type == .void) {
            try self.emit(.{ .op = .call, .operands = .{ .{ .func_name = func_name }, .{ .call_args = args }, .none } });
            return null;
        }
        return try self.emitWithResult(.call, ret_type, .{ .{ .func_name = func_name }, .{ .call_args = args }, .none });
    }

    // Indirect function call (through function pointer)
    pub fn emitCallIndirect(self: *Function, func_ptr: FuncPtr, args: []const Value, ret_type: Type) !?Value {
        if (ret_type == .void) {
            try self.emit(.{ .op = .call_indirect, .operands = .{ .{ .value = func_ptr.val }, .{ .call_args = args }, .none } });
            return null;
        }
        return try self.emitWithResult(.call_indirect, ret_type, .{ .{ .value = func_ptr.val }, .{ .call_args = args }, .none });
    }

    // External DLL function call
    pub fn emitExternCall(self: *Function, dll_name: []const u8, func_name: []const u8, args: []const Value, ret_type: Type) !?Value {
        const extern_func = Instruction.Operand{ .extern_func = .{ .dll_name = dll_name, .func_name = func_name } };
        if (ret_type == .void) {
            try self.emit(.{ .op = .extern_call, .operands = .{ extern_func, .{ .call_args = args }, .none } });
            return null;
        }
        return try self.emitWithResult(.extern_call, ret_type, .{ extern_func, .{ .call_args = args }, .none });
    }

    // Get address of a named function - returns FuncPtr
    pub fn emitFuncAddr(self: *Function, func_name: []const u8) !FuncPtr {
        const val = try self.emitWithResult(.func_addr, .ptr, .{ .{ .func_name = func_name }, .none, .none });
        return .{ .val = val };
    }

    // Parameters
    pub fn emitParam(self: *Function, param_index: i32, ty: Type) !Value {
        return self.emitWithResult(.param, ty, .{ .{ .immediate_i32 = param_index }, .none, .none });
    }

    // Memory copy (static size) - takes RawPtr
    pub fn emitMemcpy(self: *Function, dest: RawPtr, src: RawPtr, size: i32) !void {
        try self.emit(.{
            .op = .memcpy,
            .operands = .{ .{ .value = dest.val }, .{ .value = src.val }, .{ .immediate_i32 = size } },
        });
    }

    // Memory copy (dynamic size value) - takes RawPtr
    pub fn emitMemcpyDynamic(self: *Function, dest: RawPtr, src: RawPtr, size: Value) !void {
        try self.emit(.{
            .op = .memcpy_dyn,
            .operands = .{ .{ .value = dest.val }, .{ .value = src.val }, .{ .value = size } },
        });
    }

    // Get length of a null-terminated C string - returns i64
    pub fn emitCstrLen(self: *Function, ptr: Value) !Value {
        return self.emitWithResult(.cstr_len, .i64, .{ .{ .value = ptr }, .none, .none });
    }

    // Memory set (typically used to zero memory) - takes RawPtr
    pub fn emitMemset(self: *Function, dest: RawPtr, byte_value: u8, size: i32) !void {
        try self.emit(.{
            .op = .memset,
            .operands = .{ .{ .value = dest.val }, .{ .immediate_i32 = @intCast(byte_value) }, .{ .immediate_i32 = size } },
        });
    }

    // Memory set with dynamic size - takes RawPtr
    pub fn emitMemsetDynamic(self: *Function, dest: RawPtr, byte_value: u8, size: Value) !void {
        try self.emit(.{
            .op = .memset_dyn,
            .operands = .{ .{ .value = dest.val }, .{ .immediate_i32 = @intCast(byte_value) }, .{ .value = size } },
        });
    }

    // Heap allocation - returns RawPtr
    pub fn emitHeapAlloc(self: *Function, size: Value, tag: []const u8) !RawPtr {
        const val = try self.emitWithResult(.heap_alloc, .ptr, .{ .{ .value = size }, .{ .alloc_tag = tag }, .none });
        return .{ .val = val };
    }

    // Heap free - takes RawPtr
    pub fn emitHeapFree(self: *Function, ptr: RawPtr, tag: []const u8) !void {
        try self.emit(.{ .op = .heap_free, .operands = .{ .{ .value = ptr.val }, .{ .alloc_tag = tag }, .none } });
    }

    // Heap realloc - takes and returns RawPtr
    pub fn emitHeapRealloc(self: *Function, old_ptr: RawPtr, new_size: Value, tag: []const u8) !RawPtr {
        const val = try self.emitWithResult(.heap_realloc, .ptr, .{ .{ .value = old_ptr.val }, .{ .value = new_size }, .{ .alloc_tag = tag } });
        return .{ .val = val };
    }

    // Memory tracking (for --track-memory)
    pub fn emitTrackMove(self: *Function, tag: []const u8) !void {
        try self.emit(.{ .op = .track_move, .operands = .{ .{ .alloc_tag = tag }, .none, .none } });
    }

    pub fn emitTrackIncref(self: *Function, tag: []const u8, new_refcount: Value) !void {
        try self.emit(.{ .op = .track_incref, .operands = .{ .{ .alloc_tag = tag }, .{ .value = new_refcount }, .none } });
    }

    pub fn emitTrackDecref(self: *Function, tag: []const u8, new_refcount: Value) !void {
        try self.emit(.{ .op = .track_decref, .operands = .{ .{ .alloc_tag = tag }, .{ .value = new_refcount }, .none } });
    }
};

/// IR Module
pub const Module = struct {
    functions: std.ArrayListUnmanaged(Function),
    allocator: std.mem.Allocator,
    // Allocated strings that must be freed when module is deinitialized
    allocated_strings: std.ArrayListUnmanaged([]const u8) = .empty,
    source_file: ?[]const u8 = null,

    pub fn init(allocator: std.mem.Allocator) Module {
        return .{
            .functions = .empty,
            .allocator = allocator,
            .allocated_strings = .empty,
        };
    }

    pub fn deinit(self: *Module) void {
        for (self.functions.items) |*func| {
            func.deinit();
        }
        self.functions.deinit(self.allocator);

        // Free allocated strings (e.g., mangled method names)
        for (self.allocated_strings.items) |s| {
            self.allocator.free(s);
        }
        self.allocated_strings.deinit(self.allocator);
    }

    /// Track an allocated string for cleanup when module is deinitialized
    pub fn trackString(self: *Module, s: []const u8) !void {
        try self.allocated_strings.append(self.allocator, s);
    }

    pub fn addFunction(self: *Module, name: []const u8, return_type: Type) !*Function {
        return self.addFunctionWithExport(name, return_type, false);
    }

    pub fn addFunctionWithExport(self: *Module, name: []const u8, return_type: Type, is_exported: bool) !*Function {
        try self.functions.append(self.allocator, Function.initWithExport(self.allocator, name, return_type, is_exported));
        return &self.functions.items[self.functions.items.len - 1];
    }

    pub fn getFunction(self: *const Module, name: []const u8) ?*const Function {
        for (self.functions.items) |*func| {
            if (std.mem.eql(u8, func.name, name)) {
                return func;
            }
        }
        return null;
    }

    /// Print IR to a writer
    pub fn print(self: *const Module, writer: anytype) !void {
        for (self.functions.items) |func| {
            try writer.print("function {s}() -> {s} {{\n", .{ func.name, func.return_type.toIrName() });

            for (func.blocks.items) |block| {
                try writer.print("{s}:\n", .{block.name});
                debug.ir("Block {s} has {d} instructions", .{ block.name, block.instructions.items.len });

                for (block.instructions.items, 0..) |inst, idx| {
                    debug.ir("  Instruction {d}: op={s}", .{ idx, @tagName(inst.op) });
                    try writer.writeAll("    ");
                    try printInstruction(writer, inst, &func);
                    try writer.writeAll("\n");
                }
            }

            try writer.writeAll("}\n\n");
        }
    }

    /// Print IR to string
    pub fn printToString(self: *const Module, allocator: std.mem.Allocator) ![]u8 {
        var list: std.ArrayListUnmanaged(u8) = .empty;
        errdefer list.deinit(allocator);
        try self.print(list.writer(allocator));
        return list.toOwnedSlice(allocator);
    }

    /// Merge another module's functions into this module.
    /// Functions from the other module are moved (not copied).
    /// Duplicate function names are skipped (first definition wins).
    /// If exports_only is true, only exported functions are included.
    pub fn merge(self: *Module, other: *Module) !void {
        return self.mergeWithOptions(other, .{});
    }

    pub const MergeOptions = struct {
        exports_only: bool = false,
    };

    pub fn mergeWithOptions(self: *Module, other: *Module, options: MergeOptions) !void {
        for (other.functions.items) |*func| {
            // Skip non-exported functions if exports_only is set
            if (options.exports_only and !func.is_exported) {
                // Free the skipped function's resources
                func.deinit();
                continue;
            }

            // Skip if we already have a function with this name
            var exists = false;
            for (self.functions.items) |existing| {
                if (std.mem.eql(u8, existing.name, func.name)) {
                    exists = true;
                    break;
                }
            }
            if (!exists) {
                try self.functions.append(self.allocator, func.*);
            } else {
                // Free the duplicate function's resources
                func.deinit();
            }
        }
        // Clear other's functions list without freeing items (they were moved or freed)
        other.functions.items.len = 0;

        // Move allocated strings from other module to this one
        for (other.allocated_strings.items) |s| {
            try self.allocated_strings.append(self.allocator, s);
        }
        other.allocated_strings.items.len = 0;
    }
};

fn printInstruction(writer: anytype, inst: Instruction, func: *const Function) !void {
    // Print result if present
    if (inst.result) |r| {
        if (func.getValueName(r)) |name| {
            try writer.print("%{s} = ", .{name});
        } else {
            try writer.print("%{d} = ", .{r});
        }
    }

    // Print opcode
    try writer.writeAll(inst.op.format());

    // Print type for typed operations
    if (inst.result != null and inst.result_type != .void) {
        try writer.print(" {s}", .{inst.result_type.toIrName()});
    }

    // Print operands
    for (inst.operands) |op| {
        switch (op) {
            .none => {},
            .value => |v| {
                if (func.getValueName(v)) |name| {
                    try writer.print(" %{s}", .{name});
                } else {
                    try writer.print(" %{d}", .{v});
                }
            },
            .immediate_i32 => |i| try writer.print(" {d}", .{i}),
            .immediate_i64 => |i| try writer.print(" {d}", .{i}),
            .immediate_f64 => |f| try writer.print(" {d}", .{f}),
            .block_ref => |b| try writer.print(" @block{d}", .{b}),
            .func_name => |n| try writer.print(" @{s}", .{n}),
            .call_args => |args| {
                try writer.writeAll("(");
                for (args, 0..) |arg, i| {
                    if (i > 0) try writer.writeAll(", ");
                    if (func.getValueName(arg)) |name| {
                        try writer.print("%{s}", .{name});
                    } else {
                        try writer.print("%{d}", .{arg});
                    }
                }
                try writer.writeAll(")");
            },
            .elem_size => |size| try writer.print(" elemsize={d}", .{size}),
            .string_data => |str| try writer.print(" \"{s}\"", .{str}),
            .alloc_tag => |tag| try writer.print(" tag=\"{s}\"", .{tag}),
            .extern_func => |ef| try writer.print(" @\"{s}:{s}\"", .{ ef.dll_name, ef.func_name }),
        }
    }
}
