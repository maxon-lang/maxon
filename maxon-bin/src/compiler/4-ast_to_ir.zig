const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const mutation_analysis = @import("3-mutation_analysis.zig");
const err = @import("error.zig");

// Import types from dedicated types module
const types = @import("ast_to_ir_types.zig");

// Import intrinsics module
const intrinsics = @import("ast_to_ir_intrinsics.zig");
pub const TypedValue = types.TypedValue;
pub const ArrayStorage = types.ArrayStorage;
pub const ArrayInfo = types.ArrayInfo;
pub const OptionalInfo = types.OptionalInfo;
pub const ValueType = types.ValueType;
pub const FieldInfo = types.FieldInfo;
pub const StructTypeInfo = types.StructTypeInfo;
pub const EnumTypeInfo = types.EnumTypeInfo;
pub const TypeInfo = types.TypeInfo;
pub const FuncInfo = types.FuncInfo;
pub const ExternalFuncSignature = types.ExternalFuncSignature;
pub const ExternalTypeInfo = types.ExternalTypeInfo;
pub const ExternalFieldInfo = types.ExternalFieldInfo;
pub const ParamType = types.ParamType;
pub const OwnershipState = types.OwnershipState;
pub const VarInfo = types.VarInfo;
pub const ConvertError = types.ConvertError;
pub const InterfaceInfo = types.InterfaceInfo;
pub const PendingMethod = types.PendingMethod;

/// Helper for managing intrinsic loop blocks (cond/body/end pattern).
/// Ensures correct block ordering and branch emission to avoid infinite loops.
///
/// Usage:
/// 1. Create LoopBlocks
/// 2. Emit condition code (cond block is current)
/// 3. Call emitCondBranch with condition value
/// 4. Emit body code (body block is now current)
/// 5. Emit branch back to cond block
/// 6. Call finish() to restore end block
pub const LoopBlocks = struct {
    cond_block_idx: u32,
    body_block_idx: u32,
    end_block_idx: u32,
    end_block: ir.BasicBlock,
    body_block: ir.BasicBlock,

    /// Create loop blocks and emit entry branch to cond block.
    /// After this, cond block is the current block.
    fn create(self_ir: *AstToIr, cond_name: []const u8, body_name: []const u8, end_name: []const u8) !LoopBlocks {
        // Create cond block
        const cond_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
        _ = try self_ir.func().addBlock(cond_name);

        // Emit unconditional branch from previous block to cond block
        try self_ir.func().blocks.items[cond_block_idx - 1].instructions.append(self_ir.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = cond_block_idx }, .none, .none },
            .result = null,
        });

        // Create body and end blocks
        const body_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
        _ = try self_ir.func().addBlock(body_name);
        const end_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
        _ = try self_ir.func().addBlock(end_name);

        // Pop end and body blocks (cond block becomes current)
        const end_block = self_ir.func().blocks.pop().?;
        const body_block = self_ir.func().blocks.pop().?;

        return .{
            .cond_block_idx = cond_block_idx,
            .body_block_idx = body_block_idx,
            .end_block_idx = end_block_idx,
            .end_block = end_block,
            .body_block = body_block,
        };
    }

    /// Emit conditional branch in cond block, then restore body block as current.
    fn emitCondBranch(self: *LoopBlocks, self_ir: *AstToIr, cond_value: ir.Value) !void {
        try self_ir.func().blocks.items[self.cond_block_idx].instructions.append(self_ir.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = cond_value }, .{ .block_ref = self.body_block_idx }, .{ .block_ref = self.end_block_idx } },
        });

        // Restore body block as current
        try self_ir.func().blocks.append(self_ir.allocator, self.body_block);
    }

    /// Restore end block after body code is complete.
    fn finish(self: *LoopBlocks, self_ir: *AstToIr) !void {
        try self_ir.func().blocks.append(self_ir.allocator, self.end_block);
    }
};

/// Labeled loop info for break/continue with labels
const LabeledLoop = struct {
    label: []const u8,
    cond_block: u32,
    end_block: u32, // 0xFFFFFFFF sentinel until patched
};

// ============================================================================
// AST to IR Converter
// ============================================================================

pub const AstToIr = struct {
    allocator: std.mem.Allocator,
    module: ir.Module,
    current_func: ?*ir.Function,
    var_map: std.StringHashMapUnmanaged(VarInfo),
    type_map: std.StringHashMapUnmanaged(TypeInfo),
    func_map: std.StringHashMapUnmanaged(FuncInfo),
    interface_map: std.StringHashMapUnmanaged(InterfaceInfo),
    type_decl_map: std.StringHashMapUnmanaged(ast.TypeDecl), // Type declarations for conformance lookup
    current_decl_is_mutable: bool,
    mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer,
    current_line: usize,
    current_column: usize,
    // For struct returns: pointer passed by caller for return value
    sret_ptr: ?ir.Value,
    sret_size: i32,
    // For optional returns: info about the wrapped type (if returning optional)
    sret_optional_info: ?OptionalInfo = null,
    // Error tracking
    source_file: ?[]const u8,
    last_error: ?err.CompileError,
    // Loop context for break/continue
    loop_end_block: ?u32 = null,
    loop_cond_block: ?u32 = null,
    // Labeled loop stack: maps labels to (cond_block, end_block) pairs
    labeled_loops: std.ArrayListUnmanaged(LabeledLoop) = .{},
    // Current function name (for mutation analysis lookup)
    current_func_name: ?[]const u8 = null,
    // Method context: current type name when converting methods
    current_type_name: ?[]const u8 = null,
    // Self pointer value when inside a method
    self_ptr: ?ir.Value = null,
    // Generic type parameters (e.g., "Element" -> "int" for Array of int)
    generic_params: std.StringHashMapUnmanaged([]const u8) = .{},
    // Pending methods awaiting lazy generation (mangled_name -> PendingMethod)
    pending_methods: std.StringHashMapUnmanaged(PendingMethod) = .{},
    // Set of methods currently being generated (prevents infinite recursion)
    methods_in_progress: std.StringHashMapUnmanaged(void) = .{},
    // Flag for allowing stdlib-only builtins (set when converting monomorphized stdlib methods)
    in_stdlib_method: bool = false,
    // Borrow tracking: set when __string_slice is called, contains the source variable name
    pending_borrow_source: ?[]const u8 = null,
    // Track temporary String allocations that need cleanup after each statement
    temporary_strings: std.ArrayListUnmanaged(ir.Value) = .{},
    // Counter for generating unique anonymous closure names
    anon_closure_counter: u32 = 0,

    // ------------------------------------------------------------------------
    // Initialization / Cleanup
    // ------------------------------------------------------------------------

    pub fn init(allocator: std.mem.Allocator, mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer) AstToIr {
        return .{
            .allocator = allocator,
            .module = ir.Module.init(allocator),
            .current_func = null,
            .var_map = .{},
            .type_map = .{},
            .func_map = .{},
            .interface_map = .{},
            .type_decl_map = .{},
            .current_decl_is_mutable = false,
            .mutation_analyzer = mutation_analyzer,
            .current_line = 1,
            .current_column = 1,
            .sret_ptr = null,
            .sret_size = 0,
            .source_file = null,
            .last_error = null,
            .loop_end_block = null,
            .loop_cond_block = null,
            .current_func_name = null,
            .current_type_name = null,
            .self_ptr = null,
        };
    }

    /// Report a user error (their code is wrong)
    pub fn reportError(self: *AstToIr, code: err.ErrorCode, details: []const u8) void {
        // Format: "base message: 'details'"
        const base_msg = code.message();
        const formatted = std.fmt.allocPrint(self.allocator, "{s}: '{s}'", .{ base_msg, details }) catch {
            // Fallback if allocation fails
            self.last_error = .{
                .code = code,
                .message = base_msg,
                .location = .{
                    .file = self.source_file,
                    .line = @intCast(self.current_line),
                    .column = @intCast(self.current_column),
                },
            };
            return;
        };
        self.last_error = .{
            .code = code,
            .message = formatted,
            .location = .{
                .file = self.source_file,
                .line = @intCast(self.current_line),
                .column = @intCast(self.current_column),
            },
        };
    }

    /// Report an internal compiler error (compiler bug or limitation)
    pub fn reportInternalError(self: *AstToIr, message: []const u8) void {
        self.last_error = .{
            .code = null,
            .message = message,
            .location = .{
                .file = self.source_file,
                .line = @intCast(self.current_line),
                .column = 1,
            },
        };
    }

    // ------------------------------------------------------------------------
    // Loop Block Helpers (for intrinsics module)
    // ------------------------------------------------------------------------

    /// Create loop blocks for intrinsics. Wrapper around LoopBlocks.create.
    pub fn createLoopBlocks(self: *AstToIr, cond_name: []const u8, body_name: []const u8, end_name: []const u8) !LoopBlocks {
        return LoopBlocks.create(self, cond_name, body_name, end_name);
    }

    /// Emit conditional branch in loop. Wrapper around LoopBlocks.emitCondBranch.
    pub fn emitLoopCondBranch(self: *AstToIr, loop: *LoopBlocks, cond_value: ir.Value) !void {
        return loop.emitCondBranch(self, cond_value);
    }

    /// Finish loop by restoring end block. Wrapper around LoopBlocks.finish.
    pub fn finishLoop(self: *AstToIr, loop: *LoopBlocks) !void {
        return loop.finish(self);
    }

    // ------------------------------------------------------------------------
    // COW String Helpers
    // ------------------------------------------------------------------------

    /// Size of __ManagedString struct (24 bytes with COW support)
    /// Layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)
    const MANAGED_STRING_SIZE: i32 = 24;

    /// Size of __ManagedArray struct (24 bytes)
    /// Layout: buffer(8) + len(8) + capacity(8)
    const MANAGED_ARRAY_SIZE: i32 = 24;

    /// Create and initialize a __ManagedString on the stack from string bytes.
    /// Returns a pointer to the allocated struct.
    fn emitManagedStringFromBytes(self: *AstToIr, str_bytes: []const u8) !ir.Value {
        const managed_ptr = try self.func().emitAllocaSized(MANAGED_STRING_SIZE);

        if (str_bytes.len == 0) {
            // Empty string: SSO mode with zero length
            const null_ptr = try self.func().emitConstI64(0);
            const zero_i32 = try self.func().emitConstI32(0);

            try self.func().emitStore(try self.func().emitGetFieldPtr(managed_ptr, 0), null_ptr);
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 8), zero_i32);
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 12), zero_i32);
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 16), zero_i32);
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 20), zero_i32);
        } else {
            // Heap allocation for string data
            const buffer_size = try self.func().emitConstI64(@intCast(str_bytes.len + 1));
            const buffer = try self.func().emitHeapAlloc(buffer_size);

            // Store string bytes
            for (str_bytes, 0..) |byte, i| {
                const idx_val = try self.func().emitConstI64(@intCast(i));
                const byte_ptr = try self.func().emitGetElemPtr(buffer, idx_val, 1);
                try self.func().emitStoreI8(byte_ptr, try self.func().emitConstI8(byte));
            }
            // Null terminate
            const null_idx = try self.func().emitConstI64(@intCast(str_bytes.len));
            try self.func().emitStoreI8(try self.func().emitGetElemPtr(buffer, null_idx, 1), try self.func().emitConstI8(0));

            // Initialize __ManagedString fields
            // Note: refcount starts at 0, not 1. When this is passed to String$init,
            // the struct copy will increment refcount to 1. This ensures proper COW semantics
            // where the final owner has refcount=1 after initialization.
            try self.func().emitStore(try self.func().emitGetFieldPtr(managed_ptr, 0), buffer);
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 8), try self.func().emitConstI32(@intCast(str_bytes.len)));
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 12), try self.func().emitConstI32(@intCast((str_bytes.len << 2) | 0b01)));
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 16), try self.func().emitConstI32(0));
            try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 20), try self.func().emitConstI32(0));
        }

        return managed_ptr;
    }

    /// Create and initialize a __ManagedString from a runtime buffer pointer and length.
    /// Used for runtime string conversions (int to string, float to string, etc.)
    fn emitManagedStringFromBuffer(self: *AstToIr, buffer: ir.Value, len_i64: ir.Value) !ir.Value {
        const managed_ptr = try self.func().emitAllocaSized(MANAGED_STRING_SIZE);

        // buffer pointer at offset 0
        try self.func().emitStore(try self.func().emitGetFieldPtr(managed_ptr, 0), buffer);

        // length (i32) at offset 8
        const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, len_i64, .i32);
        try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 8), len_i32);

        // cap_flags = (len << 2) | 1 for heap mode at offset 12
        const four = try self.func().emitConstI32(4);
        const cap_shift = try self.func().emitBinaryOp(.mul, len_i32, four, .i32);
        const cap_flags = try self.func().emitBinaryOp(.bitor, cap_shift, try self.func().emitConstI32(1), .i32);
        try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 12), cap_flags);

        // refcount = 1 at offset 16
        // Note: This starts at 1 because emitStringFromManaged does raw memcpy without incref
        try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 16), try self.func().emitConstI32(1));

        // parent_off = 0 at offset 20
        try self.func().emitStoreI32(try self.func().emitGetFieldPtr(managed_ptr, 20), try self.func().emitConstI32(0));

        return managed_ptr;
    }

    /// Wrap a __ManagedString pointer in a full String struct (adds iter_pos field).
    pub fn emitStringFromManaged(self: *AstToIr, managed_ptr: ir.Value) !ir.Value {
        const string_ptr = try self.func().emitAllocaSized(32);
        try self.func().emitMemcpy(string_ptr, managed_ptr, MANAGED_STRING_SIZE);
        const iter_pos_ptr = try self.func().emitGetFieldPtr(string_ptr, 24);
        try self.func().emitStore(iter_pos_ptr, try self.func().emitConstI64(0));
        return string_ptr;
    }

    /// Check if a type is the String type (which contains __ManagedString)
    fn isStringType(self: *AstToIr, ty: ValueType) bool {
        _ = self;
        return ty == .struct_type and std.mem.eql(u8, ty.struct_type, "String");
    }

    /// Copy a struct and handle refcount increment for String/__ManagedString types.
    /// This is the single point of truth for struct copying with COW semantics.
    fn emitStructCopy(self: *AstToIr, dest_ptr: ir.Value, src_ptr: ir.Value, size: i32, struct_name: ?[]const u8) !void {
        try self.func().emitMemcpy(dest_ptr, src_ptr, size);

        // Handle refcount for String types (which contain __ManagedString at offset 0)
        if (struct_name) |name| {
            if (std.mem.eql(u8, name, "String") or std.mem.eql(u8, name, "__ManagedString")) {
                try self.emitStringIncref(dest_ptr);
            }
        }
    }

    /// Emit incref for a String variable if it's in heap mode.
    /// The string_ptr should point to a String struct (with _managed at offset 0).
    fn emitStringIncref(self: *AstToIr, string_ptr: ir.Value) !void {
        // String struct has _managed (__ManagedString) at offset 0
        // __ManagedString layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)

        // Load cap_flags (offset 12 within __ManagedString, which is at offset 0 in String)
        const cap_ptr = try self.func().emitGetFieldPtr(string_ptr, 12);
        const cap_flags = try self.func().emitLoad(cap_ptr, .i32);

        // Check if heap mode: (cap_flags & 0x3) == 1
        const three = try self.func().emitConstI32(3);
        const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
        const one_i32 = try self.func().emitConstI32(1);
        const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

        // Create blocks for conditional incref
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("incref");
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("incref_end");

        // Save end block, pop it temporarily
        const end_block = self.func().blocks.pop().?;

        // Emit conditional branch from entry block
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_heap }, .{ .block_ref = incref_block_idx }, .{ .block_ref = end_block_idx } },
        });

        // In incref block: increment refcount
        const ref_ptr = try self.func().emitGetFieldPtr(string_ptr, 16);
        const old_ref = try self.func().emitLoad(ref_ptr, .i32);
        const one = try self.func().emitConstI32(1);
        const new_ref = try self.func().emitBinaryOp(.add, old_ref, one, .i32);
        try self.func().emitStoreI32(ref_ptr, new_ref);

        // Branch to end
        try self.func().emitBr(end_block_idx);

        // Restore end block
        try self.func().blocks.append(self.allocator, end_block);
    }

    /// Emit decref for a String variable with conditional free when refcount reaches 0.
    /// The string_ptr should point to a String struct (with _managed at offset 0).
    fn emitStringDecref(self: *AstToIr, string_ptr: ir.Value) !void {
        // String struct has _managed (__ManagedString) at offset 0
        // __ManagedString layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)

        // Load cap_flags (offset 12)
        const cap_ptr = try self.func().emitGetFieldPtr(string_ptr, 12);
        const cap_flags = try self.func().emitLoad(cap_ptr, .i32);

        // Check if heap mode: (cap_flags & 0x3) == 1
        const three = try self.func().emitConstI32(3);
        const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
        const one_i32 = try self.func().emitConstI32(1);
        const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

        // Create blocks for conditional decref
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("decref");
        const free_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("decref_free");
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("decref_end");

        // Save blocks, pop them temporarily
        const end_block = self.func().blocks.pop().?;
        const free_block = self.func().blocks.pop().?;

        // Emit conditional branch from entry block to decref or end
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_heap }, .{ .block_ref = decref_block_idx }, .{ .block_ref = end_block_idx } },
        });

        // In decref block: decrement refcount and check if zero
        const ref_ptr = try self.func().emitGetFieldPtr(string_ptr, 16);
        const old_ref = try self.func().emitLoad(ref_ptr, .i32);
        const one = try self.func().emitConstI32(1);
        const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one, .i32);
        try self.func().emitStoreI32(ref_ptr, new_ref);

        // Check if refcount is now zero
        const zero = try self.func().emitConstI32(0);
        const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero, .i32);

        // Save current block (decref), add free block
        try self.func().blocks.append(self.allocator, free_block);

        // Emit branch from decref block to free or end
        try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_zero }, .{ .block_ref = free_block_idx }, .{ .block_ref = end_block_idx } },
        });

        // In free block: load buffer pointer and free it
        const buf_ptr = try self.func().emitLoad(string_ptr, .ptr);
        try self.func().emitHeapFree(buf_ptr);

        // Branch to end
        try self.func().emitBr(end_block_idx);

        // Restore end block
        try self.func().blocks.append(self.allocator, end_block);
    }

    /// Emit cleanup for a cstring variable.
    /// cstring struct: data(8) + length(8) + managed(8)
    /// If managed != null: decref the __ManagedString (cstring borrowed from String)
    /// If managed == null: free the data pointer (cstring owns buffer from slice copy)
    fn emitCstringCleanup(self: *AstToIr, cstring_ptr: ir.Value) !void {
        // Load managed pointer (offset 16)
        const managed_field = try self.func().emitGetFieldPtr(cstring_ptr, 16);
        const managed_ptr = try self.func().emitLoad(managed_field, .ptr);

        // Check if managed != null
        const null_val = try self.func().emitConstI64(0);
        const is_not_null = try self.func().emitBinaryOp(.icmp_ne, managed_ptr, null_val, .i64);

        // Create all blocks in execution order
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

        const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_decref");

        // === DECREF BLOCK: managed != null, decref the __ManagedString ===
        const cap_ptr = try self.func().emitGetFieldPtr(managed_ptr, 12);
        const cap_flags = try self.func().emitLoad(cap_ptr, .i32);
        const three = try self.func().emitConstI32(3);
        const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
        const one_i32 = try self.func().emitConstI32(1);
        const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

        const do_decref_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_do_decref");

        // In do_decref block: decrement refcount, free if zero
        const ref_ptr = try self.func().emitGetFieldPtr(managed_ptr, 16);
        const old_ref = try self.func().emitLoad(ref_ptr, .i32);
        const one_ref = try self.func().emitConstI32(1);
        const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one_ref, .i32);
        try self.func().emitStoreI32(ref_ptr, new_ref);
        const zero = try self.func().emitConstI32(0);
        const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero, .i32);

        const do_free_managed_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_free_managed");

        // In free managed block: free the buffer
        const buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
        try self.func().emitHeapFree(buf_ptr);

        const skip_decref_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_skip_decref");

        const free_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_free");

        // === FREE BLOCK: managed == null, free the data pointer ===
        const data_ptr = try self.func().emitLoad(cstring_ptr, .ptr);
        try self.func().emitHeapFree(data_ptr);

        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_cleanup_end");

        // Now go back and add all the branch instructions

        // Entry: if managed != null -> decref, else -> free
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_not_null }, .{ .block_ref = decref_block_idx }, .{ .block_ref = free_block_idx } },
        });

        // Decref block: if heap mode -> do_decref, else -> skip_decref
        try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_heap }, .{ .block_ref = do_decref_idx }, .{ .block_ref = skip_decref_idx } },
        });

        // Do_decref block: if refcount zero -> free_managed, else -> skip_decref
        try self.func().blocks.items[do_decref_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_zero }, .{ .block_ref = do_free_managed_idx }, .{ .block_ref = skip_decref_idx } },
        });

        // Free_managed block: goto end
        try self.func().blocks.items[do_free_managed_idx].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
            .result = null,
        });

        // Skip_decref block: goto end
        try self.func().blocks.items[skip_decref_idx].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
            .result = null,
        });

        // Free block: goto end
        try self.func().blocks.items[free_block_idx].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
            .result = null,
        });
    }

    // ------------------------------------------------------------------------
    // Temporary String Cleanup
    // ------------------------------------------------------------------------

    /// Clean up all temporary strings and clear the list
    fn cleanupTemporaryStrings(self: *AstToIr) !void {
        for (self.temporary_strings.items) |str_ptr| {
            try self.emitStringDecref(str_ptr);
        }
        self.temporary_strings.clearRetainingCapacity();
    }

    /// Remove a specific value from the temporary strings list (used when assigned to a variable)
    fn removeFromTemporaries(self: *AstToIr, value: ir.Value) void {
        var i: usize = 0;
        while (i < self.temporary_strings.items.len) {
            if (self.temporary_strings.items[i] == value) {
                _ = self.temporary_strings.swapRemove(i);
            } else {
                i += 1;
            }
        }
    }

    // ------------------------------------------------------------------------
    // Borrow Checking Helpers
    // ------------------------------------------------------------------------

    /// Mark a source string variable as borrowed and record the borrower
    fn markStringBorrowed(self: *AstToIr, source_var_name: []const u8) void {
        if (self.var_map.getPtr(source_var_name)) |var_info| {
            if (self.isStringType(var_info.ty)) {
                var_info.borrow_state = .borrowed;
            }
        }
    }

    /// Clear the borrow state from a parent variable when the slice goes out of scope
    fn clearBorrowFromParent(self: *AstToIr, slice_var_info: *const VarInfo) void {
        if (slice_var_info.borrowed_from) |parent_name| {
            if (self.var_map.getPtr(parent_name)) |parent_info| {
                parent_info.borrow_state = .none;
            }
        }
    }

    /// Check if a string variable can be modified (not borrowed)
    /// Returns an error if the variable is currently borrowed
    fn checkStringNotBorrowed(self: *AstToIr, var_name: []const u8) ConvertError!void {
        if (self.var_map.get(var_name)) |var_info| {
            if (self.isStringType(var_info.ty) and var_info.borrow_state == .borrowed) {
                const msg = std.fmt.allocPrint(self.allocator, "Cannot modify string '{s}' while it is borrowed", .{var_name}) catch {
                    self.reportError(.E020, var_name);
                    return error.SemanticError;
                };
                self.last_error = .{
                    .code = .E020,
                    .message = msg,
                    .location = err.SourceLocation.init(@intCast(self.current_line), 1),
                };
                return error.SemanticError;
            }
        }
    }

    /// Check that no borrowed strings go out of scope
    /// Called before freeing heap variables at scope end
    fn checkNoOutstandingBorrows(self: *AstToIr) ConvertError!void {
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            const var_info = entry.value_ptr.*;
            if (self.isStringType(var_info.ty) and var_info.borrow_state == .borrowed) {
                const msg = std.fmt.allocPrint(self.allocator, "String '{s}' goes out of scope while still borrowed", .{entry.key_ptr.*}) catch {
                    self.reportError(.E021, entry.key_ptr.*);
                    return error.SemanticError;
                };
                self.last_error = .{
                    .code = .E021,
                    .message = msg,
                    .location = err.SourceLocation.init(@intCast(self.current_line), 1),
                };
                return error.SemanticError;
            }
        }
    }

    /// Clear all slice borrows when slices go out of scope
    /// This should be called at the end of scopes to release borrows
    fn clearSliceBorrows(self: *AstToIr, exclude_vars: ?*std.StringHashMapUnmanaged(OwnershipState)) void {
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (exclude_vars) |excluded| {
                if (excluded.contains(entry.key_ptr.*)) continue;
            }
            const var_info = entry.value_ptr;
            if (var_info.borrow_state == .slice) {
                self.clearBorrowFromParent(var_info);
            }
        }
    }

    pub fn deinit(self: *AstToIr) void {
        // Clean up the IR module (important for error paths where convert() fails)
        // In success case, module was transferred out and replaced with empty module
        self.module.deinit();

        self.temporary_strings.deinit(self.allocator);
        self.var_map.deinit(self.allocator);

        var type_iter = self.type_map.iterator();
        while (type_iter.next()) |entry| {
            switch (entry.value_ptr.*) {
                .struct_type => |s| self.allocator.free(s.fields),
                .enum_type => |*e| e.members.deinit(self.allocator),
                .primitive => {},
            }
        }
        self.type_map.deinit(self.allocator);

        // Free param_types from func_map entries that we allocated
        // Use a set to track freed pointers since multiple entries may share the same param_types
        // (e.g., interface methods are registered under both regular and qualified names)
        var freed_ptrs = std.AutoHashMapUnmanaged(*const ParamType, void){};
        defer freed_ptrs.deinit(self.allocator);
        var func_iter = self.func_map.iterator();
        while (func_iter.next()) |entry| {
            if (entry.value_ptr.param_types.len > 0) {
                const ptr = &entry.value_ptr.param_types[0];
                if (freed_ptrs.get(ptr) == null) {
                    freed_ptrs.put(self.allocator, ptr, {}) catch {};
                    self.allocator.free(entry.value_ptr.param_types);
                }
            }
        }
        self.func_map.deinit(self.allocator);

        // Interface map doesn't own its data (references AST nodes)
        self.interface_map.deinit(self.allocator);

        // Type decl map doesn't own its data (references AST nodes)
        self.type_decl_map.deinit(self.allocator);

        // Generic params map doesn't own its data (references AST strings)
        self.generic_params.deinit(self.allocator);

        // Clean up labeled loops stack
        self.labeled_loops.deinit(self.allocator);
    }

    // ------------------------------------------------------------------------
    // Main Entry Point
    // ------------------------------------------------------------------------

    pub fn convert(self: *AstToIr, program: ast.Program) !ir.Module {
        // Register primitive types
        try self.type_map.put(self.allocator, "int", .{ .primitive = .i64 });
        try self.type_map.put(self.allocator, "float", .{ .primitive = .f64 });
        try self.type_map.put(self.allocator, "bool", .{ .primitive = .i64 }); // bool is represented as i64
        try self.type_map.put(self.allocator, "byte", .{ .primitive = .i64 }); // byte is stored as i64 (0-255)
        try self.type_map.put(self.allocator, "ptr", .{ .primitive = .ptr }); // raw pointer type

        // Register __ManagedArray compiler-internal type (24 bytes: ptr + len + capacity)
        // This is a parameterized type - element type is tracked separately in ArrayInfo
        const managed_array_fields = try self.allocator.alloc(FieldInfo, 3);
        managed_array_fields[0] = .{ .name = "_buffer", .offset = 0, .size = 8, .value_type = .{ .primitive = "ptr" } };
        managed_array_fields[1] = .{ .name = "_len", .offset = 8, .size = 8, .value_type = .{ .primitive = "int" } };
        managed_array_fields[2] = .{ .name = "_capacity", .offset = 16, .size = 8, .value_type = .{ .primitive = "int" } };
        self.type_map.put(self.allocator, "__ManagedArray", .{
            .struct_type = .{ .name = "__ManagedArray", .fields = managed_array_fields, .size = 24 },
        }) catch {
            self.allocator.free(managed_array_fields);
            return error.OutOfMemory;
        };

        // Register __ManagedString compiler-internal type (24 bytes with COW support)
        // Layout: ptr buffer (8) + i32 len (4) + i32 cap_flags (4) + i32 refcount (4) + i32 parent_off (4)
        // Mode detection via cap_flags & 0x3:
        //   0b00 = SSO (data inline in bytes 0-14, byte 15 = remaining capacity)
        //   0b01 = Heap (owned buffer with refcount)
        //   0b10 = Slice (borrowed view into parent string)
        const managed_string_fields = try self.allocator.alloc(FieldInfo, 5);
        managed_string_fields[0] = .{ .name = "_buffer", .offset = 0, .size = 8, .value_type = .{ .primitive = "ptr" } };
        managed_string_fields[1] = .{ .name = "_len", .offset = 8, .size = 4, .value_type = .{ .primitive = "int" } };
        managed_string_fields[2] = .{ .name = "_cap_flags", .offset = 12, .size = 4, .value_type = .{ .primitive = "int" } };
        managed_string_fields[3] = .{ .name = "_refcount", .offset = 16, .size = 4, .value_type = .{ .primitive = "int" } };
        managed_string_fields[4] = .{ .name = "_parent_off", .offset = 20, .size = 4, .value_type = .{ .primitive = "int" } };
        self.type_map.put(self.allocator, "__ManagedString", .{
            .struct_type = .{ .name = "__ManagedString", .fields = managed_string_fields, .size = 24 },
        }) catch {
            self.allocator.free(managed_string_fields);
            return error.OutOfMemory;
        };

        // Register cstring compiler-internal type (24 bytes)
        // Layout: data(8) + length(8) + managed(8)
        // For non-slice strings: data points to String's buffer, managed points to __ManagedString (incref'd)
        // For slice strings: data points to newly allocated null-terminated copy, managed = null
        const cstring_fields = try self.allocator.alloc(FieldInfo, 3);
        cstring_fields[0] = .{ .name = "data", .offset = 0, .size = 8, .value_type = .{ .primitive = "ptr" } };
        cstring_fields[1] = .{ .name = "length", .offset = 8, .size = 8, .value_type = .{ .primitive = "int" } };
        cstring_fields[2] = .{ .name = "managed", .offset = 16, .size = 8, .value_type = .{ .primitive = "ptr" } };
        self.type_map.put(self.allocator, "cstring", .{
            .struct_type = .{ .name = "cstring", .fields = cstring_fields, .size = 24 },
        }) catch {
            self.allocator.free(cstring_fields);
            return error.OutOfMemory;
        };

        // Register interfaces first (needed for conformance checking)
        for (program.interfaces) |iface| try self.registerInterface(iface);

        // Register declarations
        for (program.types) |type_decl| try self.registerType(type_decl);
        for (program.enums) |enum_decl| try self.registerEnum(enum_decl);
        for (program.functions) |fn_decl| try self.registerFunction(fn_decl);

        // Register methods from types
        for (program.types) |type_decl| {
            // Set up generic params for this type
            for (type_decl.generic_params) |param| {
                try self.generic_params.put(self.allocator, param, "int");
            }
            for (type_decl.methods) |method| {
                try self.registerMethod(type_decl.name, method);
            }
            // Clean up generic params
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Check interface conformance for all types
        for (program.types) |type_decl| {
            try self.checkInterfaceConformance(type_decl);
        }

        // Convert functions
        for (program.functions) |fn_decl| try self.convertFunction(fn_decl);

        // Convert methods from types
        for (program.types) |type_decl| {
            // Set up generic params for this type
            for (type_decl.generic_params) |param| {
                try self.generic_params.put(self.allocator, param, "int");
            }
            for (type_decl.methods) |method| {
                try self.convertMethod(type_decl.name, method);
            }
            // Clean up generic params
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Transfer ownership of module
        const module = self.module;
        self.module = ir.Module.init(self.allocator);
        return module;
    }

    // ------------------------------------------------------------------------
    // Type Lookup Helpers
    // ------------------------------------------------------------------------

    fn lookupIrType(self: *AstToIr, name: []const u8) !ir.Type {
        const type_info = self.type_map.get(name) orelse {
            self.reportError(.E006, name);
            return error.UnknownType;
        };
        return type_info.irType();
    }

    fn lookupStructInfo(self: *AstToIr, type_name: []const u8) !StructTypeInfo {
        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };
        return switch (type_info) {
            .struct_type => |s| s,
            .primitive, .enum_type => {
                self.reportError(.E006, type_name);
                return error.UnknownType;
            },
        };
    }

    fn lookupField(self: *AstToIr, struct_info: StructTypeInfo, field_name: []const u8) !FieldInfo {
        for (struct_info.fields) |f| {
            if (std.mem.eql(u8, f.name, field_name)) return f;
        }
        self.reportError(.E007, field_name);
        return error.UnknownField;
    }

    /// Get the size of a type from a TypeExpr. Primitives are 8 bytes, structs use their registered size.
    fn getTypeSize(self: *AstToIr, te: ast.TypeExpr) i32 {
        return switch (te) {
            .simple => |name| {
                // Check if it's a primitive type (all 8 bytes)
                if (std.mem.eql(u8, name, "int") or
                    std.mem.eql(u8, name, "float") or
                    std.mem.eql(u8, name, "bool") or
                    std.mem.eql(u8, name, "byte"))
                {
                    return 8;
                }
                // Resolve type aliases (string -> String, character -> Character)
                const resolved = self.resolveTypeName(name);
                // Look up struct size from type_map
                if (self.type_map.get(resolved)) |type_info| {
                    if (type_info == .struct_type) {
                        return type_info.struct_type.size;
                    }
                }
                // Default to 8 bytes for unknown types
                return 8;
            },
            .generic => |gen| {
                // Look up monomorphized type or base type
                const resolved = self.resolveTypeName(gen.base_type);
                if (self.type_map.get(resolved)) |type_info| {
                    if (type_info == .struct_type) {
                        return type_info.struct_type.size;
                    }
                }
                return 8;
            },
            .optional => |wrapped| {
                // Optional size = 8 (tag) + wrapped size
                return 8 + self.getTypeSize(wrapped.*);
            },
        };
    }

    /// Get the size of an optional type from its OptionalInfo.
    /// Uses wrapped_struct_type to look up struct sizes when the wrapped type is a struct.
    fn getOptionalSize(self: *AstToIr, opt_info: OptionalInfo) i32 {
        // 8 bytes for the tag (discriminant)
        const tag_size: i32 = 8;
        // If wrapped type is a struct, look up its size
        if (opt_info.wrapped_struct_type) |struct_name| {
            if (self.type_map.get(struct_name)) |type_info| {
                if (type_info == .struct_type) {
                    return tag_size + type_info.struct_type.size;
                }
            }
        }
        // Default: primitive types are 8 bytes
        return tag_size + 8;
    }

    pub fn func(self: *AstToIr) *ir.Function {
        return self.current_func.?;
    }

    // ------------------------------------------------------------------------
    // Registration (Types, Enums, Functions)
    // ------------------------------------------------------------------------

    fn registerType(self: *AstToIr, type_decl: ast.TypeDecl) !void {
        // Store the type declaration for conformance checking
        try self.type_decl_map.put(self.allocator, type_decl.name, type_decl);

        // Set up generic type parameters (e.g., "Element" for Array uses Element)
        // For now, map all generic params to i64 as a default
        for (type_decl.generic_params) |param| {
            try self.generic_params.put(self.allocator, param, "int");
        }
        defer {
            // Clean up generic params after type registration
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Check if this type was already registered externally - we'll need to free old fields after registering new ones
        const old_fields: ?[]const FieldInfo = if (self.type_map.get(type_decl.name)) |existing|
            switch (existing) {
                .struct_type => |s| s.fields,
                .primitive, .enum_type => null,
            }
        else
            null;

        var fields = try self.allocator.alloc(FieldInfo, type_decl.fields.len);
        errdefer self.allocator.free(fields);
        var offset: i32 = 0;

        for (type_decl.fields, 0..) |field, i| {
            const value_type: ValueType = switch (field.type_expr) {
                .simple => blk: {
                    const base_name = typeExprBaseTypeName(field.type_expr).?;
                    const resolved = self.resolveTypeName(base_name);
                    // Check for internal type access
                    try intrinsics.checkInternalTypeAccess(self, resolved);
                    const field_type_info = self.type_map.get(resolved) orelse {
                        self.reportError(.E006, resolved);
                        return error.UnknownType;
                    };
                    break :blk switch (field_type_info) {
                        .struct_type => .{ .struct_type = resolved },
                        .primitive => .{ .primitive = resolved },
                        .enum_type => .{ .enum_type = resolved },
                    };
                },
                .generic => |gen| blk: {
                    // For generic types like "Array of int", monomorphize them
                    const resolved_args = try self.allocator.alloc([]const u8, gen.type_args.len);
                    defer self.allocator.free(resolved_args);
                    for (gen.type_args, 0..) |arg, j| {
                        resolved_args[j] = self.resolveTypeName(arg);
                    }
                    const mono_name = try self.getOrCreateMonomorphizedType(gen.base_type, resolved_args);
                    break :blk .{ .struct_type = mono_name };
                },
                .optional => |wrapped| blk: {
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    break :blk .{ .optional_type = .{
                        .wrapped = wrapped_value_type.toPrimitiveType(),
                    } };
                },
            };
            const field_size: i32 = switch (value_type) {
                .struct_type => |name| blk: {
                    const info = self.type_map.get(name) orelse {
                        self.reportError(.E006, name);
                        return error.UnknownType;
                    };
                    break :blk switch (info) {
                        .struct_type => |s| s.size,
                        .primitive, .enum_type => 8,
                    };
                },
                .primitive, .enum_type, .array_type => 8, // arrays stored as pointers
                .optional_type => 8, // optionals stored as pointers to 16-byte structures
            };
            fields[i] = .{
                .name = field.name,
                .offset = offset,
                .size = field_size,
                .value_type = value_type,
            };
            offset += field_size;
        }

        try self.type_map.put(self.allocator, type_decl.name, .{
            .struct_type = .{ .name = type_decl.name, .fields = fields, .size = offset },
        });

        // Now that the new entry is in the map, free the old fields if this was a re-registration
        if (old_fields) |of| {
            self.allocator.free(of);
        }

        debug.astToIr("Registered type '{s}' with size {d}", .{ type_decl.name, offset });
    }

    /// Register an external type (from stdlib or other modules)
    fn registerExternalType(self: *AstToIr, ext_type: ExternalTypeInfo) !void {
        // Skip if already registered (avoid duplicate allocations)
        if (self.type_map.contains(ext_type.name)) return;

        var fields = try self.allocator.alloc(FieldInfo, ext_type.fields.len);
        errdefer self.allocator.free(fields);

        for (ext_type.fields, 0..) |field, i| {
            const value_type: ValueType = blk: {
                // Map type name to ValueType
                if (std.mem.eql(u8, field.type_name, "int")) {
                    break :blk .{ .primitive = "int" };
                } else if (std.mem.eql(u8, field.type_name, "float")) {
                    break :blk .{ .primitive = "float" };
                } else if (std.mem.eql(u8, field.type_name, "bool")) {
                    break :blk .{ .primitive = "bool" };
                } else if (std.mem.eql(u8, field.type_name, "byte")) {
                    break :blk .{ .primitive = "byte" };
                } else if (std.mem.eql(u8, field.type_name, "ptr")) {
                    break :blk .{ .primitive = "ptr" };
                } else if (std.mem.eql(u8, field.type_name, "__ManagedArray") or std.mem.eql(u8, field.type_name, "__ManagedString")) {
                    // Managed arrays/strings are structs, but we treat them specially
                    break :blk .{ .struct_type = field.type_name };
                } else {
                    // Assume it's a struct type
                    break :blk .{ .struct_type = field.type_name };
                }
            };

            fields[i] = .{
                .name = field.name,
                .offset = field.offset,
                .size = field.size,
                .value_type = value_type,
            };
        }

        try self.type_map.put(self.allocator, ext_type.name, .{
            .struct_type = .{ .name = ext_type.name, .fields = fields, .size = ext_type.size },
        });

        // Store type declaration for generic types (needed for monomorphization)
        if (ext_type.type_decl) |type_decl| {
            try self.type_decl_map.put(self.allocator, ext_type.name, type_decl.*);
        }

        debug.astToIr("Registered external type '{s}' with size {d}", .{ ext_type.name, ext_type.size });
    }

    fn registerEnum(self: *AstToIr, enum_decl: ast.EnumDecl) !void {
        var members: std.StringHashMapUnmanaged(i64) = .{};
        errdefer members.deinit(self.allocator);

        // Determine backing type
        const backing_type: types.EnumTypeInfo.BackingType = if (enum_decl.backing_type) |bt|
            (if (std.mem.eql(u8, bt, "string")) .string else .int)
        else
            .int;

        // For string-backed enums, store the string values
        var string_values: std.AutoHashMapUnmanaged(i64, []const u8) = .{};
        errdefer string_values.deinit(self.allocator);

        var next_value: i64 = 0;
        for (enum_decl.members) |member| {
            if (member.value) |value_expr| {
                // Explicit value - evaluate constant expression
                if (value_expr.* == .integer) {
                    next_value = value_expr.integer;
                } else if (value_expr.* == .string_literal and backing_type == .string) {
                    // Store the string backing value for this ordinal
                    try string_values.put(self.allocator, next_value, value_expr.string_literal);
                }
            }
            try members.put(self.allocator, member.name, next_value);
            next_value += 1;
        }
        try self.type_map.put(self.allocator, enum_decl.name, .{
            .enum_type = .{
                .name = enum_decl.name,
                .members = members,
                .backing_type = backing_type,
                .string_values = string_values,
            },
        });
        debug.astToIr("Registered enum '{s}' with {d} members (backing: {s})", .{ enum_decl.name, enum_decl.members.len, @tagName(backing_type) });
    }

    fn registerInterface(self: *AstToIr, iface: ast.InterfaceDecl) !void {
        try self.interface_map.put(self.allocator, iface.name, .{
            .name = iface.name,
            .methods = iface.methods,
            .associated_types = iface.generic_params,
        });
        debug.astToIr("Registered interface '{s}' with {d} methods, {d} associated types", .{ iface.name, iface.methods.len, iface.generic_params.len });
    }

    fn checkInterfaceConformance(self: *AstToIr, type_decl: ast.TypeDecl) !void {
        for (type_decl.conformances) |conformance| {
            const iface = self.interface_map.get(conformance.interface_name) orelse {
                // Unknown interface - skip conformance check
                // This allows stdlib types to declare conformance to interfaces
                // that may not be loaded yet (e.g., Collection, InitableFromArrayLiteral)
                debug.astToIr("Skipping unknown interface '{s}'", .{conformance.interface_name});
                continue;
            };

            // Build a map of associated type name -> bound type
            var type_bindings = std.StringHashMapUnmanaged([]const u8){};
            defer type_bindings.deinit(self.allocator);

            // Check that all required associated types are bound
            for (iface.associated_types, 0..) |assoc_type, i| {
                if (i < conformance.type_args.len) {
                    try type_bindings.put(self.allocator, assoc_type, conformance.type_args[i]);
                } else {
                    // Missing type binding
                    const msg = std.fmt.allocPrint(self.allocator, "Type '{s}' does not define required associated type '{s}' from interface '{s}'", .{
                        type_decl.name,
                        assoc_type,
                        conformance.interface_name,
                    }) catch {
                        self.reportError(.E015, assoc_type);
                        return error.SemanticError;
                    };
                    self.last_error = .{
                        .code = .E015,
                        .message = msg,
                        .location = .{
                            .file = self.source_file,
                            .line = 1,
                            .column = 1,
                        },
                    };
                    return error.SemanticError;
                }
            }

            // Check that all required interface methods are implemented
            var missing_methods = std.ArrayListUnmanaged([]const u8){};
            defer missing_methods.deinit(self.allocator);

            for (iface.methods) |iface_method| {
                // Skip methods with default implementations (they're optional to override)
                if (iface_method.has_default_impl) continue;

                // Look for matching method in type
                var found_method: ?ast.MethodDecl = null;
                for (type_decl.methods) |type_method| {
                    if (std.mem.eql(u8, type_method.name, iface_method.name)) {
                        found_method = type_method;
                        break;
                    }
                }

                if (found_method) |type_method| {
                    // Check return type match (with associated type substitution)
                    if (iface_method.return_type) |iface_ret| {
                        if (type_method.return_type) |impl_ret| {
                            const expected = self.resolveAssociatedTypeWithSelf(iface_ret, type_bindings, type_decl.name);
                            const actual = typeExprToString(impl_ret);
                            if (!std.mem.eql(u8, expected, actual)) {
                                const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' has return type '{s}' but interface '{s}' requires '{s}'", .{
                                    type_decl.name,
                                    iface_method.name,
                                    actual,
                                    conformance.interface_name,
                                    expected,
                                }) catch {
                                    self.reportError(.E015, iface_method.name);
                                    return error.SemanticError;
                                };
                                self.last_error = .{
                                    .code = .E015,
                                    .message = msg,
                                    .location = .{
                                        .file = self.source_file,
                                        .line = 1,
                                        .column = 1,
                                    },
                                };
                                return error.SemanticError;
                            }
                        }
                    }

                    // Check parameter types match (with associated type substitution)
                    if (iface_method.params.len == type_method.params.len) {
                        for (iface_method.params, 0..) |iface_param, i| {
                            const expected = self.resolveAssociatedTypeWithSelf(iface_param.type_expr, type_bindings, type_decl.name);
                            const actual = typeExprToString(type_method.params[i].type_expr);
                            if (!std.mem.eql(u8, expected, actual)) {
                                const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' parameter {d} has type '{s}' but interface '{s}' requires '{s}'", .{
                                    type_decl.name,
                                    iface_method.name,
                                    i + 1,
                                    actual,
                                    conformance.interface_name,
                                    expected,
                                }) catch {
                                    self.reportError(.E015, iface_method.name);
                                    return error.SemanticError;
                                };
                                self.last_error = .{
                                    .code = .E015,
                                    .message = msg,
                                    .location = .{
                                        .file = self.source_file,
                                        .line = 1,
                                        .column = 1,
                                    },
                                };
                                return error.SemanticError;
                            }
                        }
                    }
                } else {
                    // Missing method - collect for aggregate error
                    const resolved_ret = if (iface_method.return_type) |rt|
                        self.resolveAssociatedTypeWithSelf(rt, type_bindings, type_decl.name)
                    else
                        "void";
                    const method_sig = std.fmt.allocPrint(self.allocator, "{s}() returns {s}", .{
                        iface_method.name,
                        resolved_ret,
                    }) catch continue;
                    try missing_methods.append(self.allocator, method_sig);
                }
            }

            // Report missing methods as aggregate error
            if (missing_methods.items.len > 0) {
                var msg_buf: std.ArrayListUnmanaged(u8) = .empty;
                const writer = msg_buf.writer(self.allocator);
                writer.print("Partial interface implementation: type '{s}' is missing {d} method(s):", .{
                    type_decl.name,
                    missing_methods.items.len,
                }) catch {};
                for (missing_methods.items) |method_sig| {
                    writer.print("\n  - {s}", .{method_sig}) catch {};
                }
                self.last_error = .{
                    .code = .E015,
                    .message = msg_buf.toOwnedSlice(self.allocator) catch "",
                    .location = .{
                        .file = self.source_file,
                        .line = 1,
                        .column = 1,
                    },
                };
                return error.SemanticError;
            }
        }
    }

    /// Resolve a type expression by substituting associated types and Self with their bound types
    fn resolveAssociatedTypeWithSelf(self: *AstToIr, type_expr: ast.TypeExpr, bindings: std.StringHashMapUnmanaged([]const u8), self_type: []const u8) []const u8 {
        _ = self;
        switch (type_expr) {
            .simple => |name| {
                // Handle Self specially
                if (std.mem.eql(u8, name, "Self")) return self_type;
                // Check if this is an associated type that should be substituted
                return bindings.get(name) orelse name;
            },
            .generic => |g| {
                if (std.mem.eql(u8, g.base_type, "Self")) return self_type;
                return bindings.get(g.base_type) orelse g.base_type;
            },
            .optional => |wrapped| {
                const inner = switch (wrapped.*) {
                    .simple => |name| if (std.mem.eql(u8, name, "Self")) self_type else bindings.get(name) orelse name,
                    .generic => |g| if (std.mem.eql(u8, g.base_type, "Self")) self_type else bindings.get(g.base_type) orelse g.base_type,
                    .optional => "?",
                };
                return inner;
            },
        }
    }

    /// Check if a type conforms to a specific interface by name
    fn typeConformsTo(self: *AstToIr, type_name: []const u8, interface_name: []const u8) bool {
        const type_decl = self.type_decl_map.get(type_name) orelse return false;
        for (type_decl.conformances) |conformance| {
            if (std.mem.eql(u8, conformance.interface_name, interface_name)) {
                return true;
            }
        }
        return false;
    }

    fn registerFunction(self: *AstToIr, decl: ast.FunctionDecl) !void {
        var ret_type_name: ?[]const u8 = null;
        var ret_value_type: ?ValueType = null;
        const ret_type: ir.Type = if (decl.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const type_info = self.type_map.get(base_name) orelse {
                        self.reportError(.E006, base_name);
                        return error.UnknownType;
                    };
                    if (type_info.isStruct()) ret_type_name = base_name;
                    break :blk type_info.irType();
                },
                .optional => |wrapped| {
                    // Optional return types are returned as pointers
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    ret_value_type = .{
                        .optional_type = .{
                            .wrapped = wrapped_value_type.toPrimitiveType(),
                        },
                    };
                    break :blk .ptr;
                },
            }
        } else .void;

        var param_types = try self.allocator.alloc(ParamType, decl.params.len);
        errdefer self.allocator.free(param_types);
        for (decl.params, 0..) |param, i| {
            param_types[i] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
            };
        }

        // Free old param_types if overwriting an existing entry (e.g., from external registration)
        if (self.func_map.get(decl.name)) |old_info| {
            if (old_info.param_types.len > 0) {
                self.allocator.free(old_info.param_types);
            }
        }

        try self.func_map.put(self.allocator, decl.name, .{
            .return_type = ret_type,
            .return_type_name = ret_type_name,
            .return_value_type = ret_value_type,
            .param_types = param_types,
        });

        debug.astToIr("Registered function '{s}' returning {s}", .{ decl.name, ret_type.toIrName() });
    }

    /// Extract base type name from simple or generic type expressions
    fn typeExprBaseTypeName(type_expr: ast.TypeExpr) ?[]const u8 {
        return switch (type_expr) {
            .simple => |name| name,
            .generic => |gen| gen.base_type,
            .optional => null,
        };
    }

    /// Convert type expression to string representation for error messages
    fn typeExprToString(type_expr: ast.TypeExpr) []const u8 {
        return switch (type_expr) {
            .simple => |name| name,
            .generic => |gen| gen.base_type,
            .optional => |wrapped| switch (wrapped.*) {
                .simple => |name| name,
                .generic => |gen| gen.base_type,
                .optional => "?",
            },
        };
    }

    fn typeExprToValueType(self: *AstToIr, type_expr: ast.TypeExpr) !ValueType {
        switch (type_expr) {
            .simple => {
                const base_name = typeExprBaseTypeName(type_expr).?;
                const resolved = self.resolveTypeName(base_name);
                // Check for internal type access
                try intrinsics.checkInternalTypeAccess(self, resolved);
                const type_info = self.type_map.get(resolved) orelse {
                    self.reportError(.E006, resolved);
                    return error.UnknownType;
                };
                return if (type_info.isStruct())
                    .{ .struct_type = resolved }
                else if (type_info == .enum_type)
                    .{ .enum_type = resolved }
                else
                    .{ .primitive = resolved };
            },
            .generic => |gen| {
                // For generic types like "Array of int", monomorphize them
                // Resolve type arguments first (e.g., "Element" -> "int" when inside Set$int)
                const resolved_args = try self.allocator.alloc([]const u8, gen.type_args.len);
                defer self.allocator.free(resolved_args);
                for (gen.type_args, 0..) |arg, i| {
                    resolved_args[i] = self.resolveTypeName(arg);
                }
                const mono_name = try self.getOrCreateMonomorphizedType(gen.base_type, resolved_args);
                return .{ .struct_type = mono_name };
            },
            .optional => |wrapped| {
                const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                return .{ .optional_type = .{
                    .wrapped = wrapped_value_type.toPrimitiveType(),
                    .wrapped_struct_type = if (wrapped_value_type == .struct_type) wrapped_value_type.struct_type else null,
                } };
            },
        }
    }

    /// Resolve type name, handling 'Self' substitution and generic type parameters
    fn resolveTypeName(self: *AstToIr, type_name: []const u8) []const u8 {
        if (std.mem.eql(u8, type_name, "Self")) {
            return self.current_type_name orelse type_name;
        }
        // Check if this is a generic type parameter (e.g., "Element")
        if (self.generic_params.get(type_name)) |resolved| {
            return resolved;
        }
        return type_name;
    }

    /// Get or create a monomorphized version of a generic type
    /// e.g., "Container" + ["int"] -> "Container$int"
    fn getOrCreateMonomorphizedType(self: *AstToIr, base_type: []const u8, type_args: []const []const u8) ConvertError![]const u8 {
        // Build the monomorphized name: TypeName$Arg1$Arg2
        var name_parts: std.ArrayListUnmanaged(u8) = .empty;
        defer name_parts.deinit(self.allocator);

        name_parts.appendSlice(self.allocator, base_type) catch return error.OutOfMemory;
        for (type_args) |arg| {
            name_parts.append(self.allocator, '$') catch return error.OutOfMemory;
            name_parts.appendSlice(self.allocator, arg) catch return error.OutOfMemory;
        }

        const mono_name = self.allocator.dupe(u8, name_parts.items) catch return error.OutOfMemory;
        self.module.trackString(mono_name) catch {
            self.allocator.free(mono_name);
            return error.OutOfMemory;
        };

        // Check if already registered
        if (self.type_map.contains(mono_name)) {
            return mono_name;
        }

        // Get the original generic type declaration
        const type_decl = self.type_decl_map.get(base_type) orelse {
            self.reportError(.E006, base_type);
            return error.UnknownType;
        };

        // Verify we have the right number of type arguments
        if (type_decl.generic_params.len != type_args.len) {
            self.reportError(.E011, base_type);
            return error.TypeMismatch;
        }

        // Set up generic parameter mappings (needed for field type resolution AND method registration)
        // Save previous values to restore later (for nested generic types like Set<Array<Element>>)
        var saved_params = try self.allocator.alloc(?[]const u8, type_decl.generic_params.len);
        defer self.allocator.free(saved_params);
        for (type_decl.generic_params, type_args, 0..) |param, arg, i| {
            saved_params[i] = self.generic_params.get(param);
            try self.generic_params.put(self.allocator, param, arg);
        }
        // Note: We restore generic_params at the very end, after method registration

        // Allow stdlib builtins when monomorphizing stdlib types (e.g., Array$int)
        // This must be set BEFORE processing fields since field types may be internal types
        const saved_in_stdlib = self.in_stdlib_method;
        self.in_stdlib_method = true;

        // Register the monomorphized type
        var fields = try self.allocator.alloc(FieldInfo, type_decl.fields.len);
        errdefer self.allocator.free(fields);
        var offset: i32 = 0;

        for (type_decl.fields, 0..) |field, i| {
            const value_type: ValueType = self.typeExprToValueType(field.type_expr) catch |e| {
                self.in_stdlib_method = saved_in_stdlib;
                return e;
            };
            const field_size: i32 = switch (value_type) {
                .struct_type => |name| blk: {
                    const info = self.type_map.get(name) orelse {
                        self.reportError(.E006, name);
                        // Clean up on error
                        for (type_decl.generic_params) |param| {
                            _ = self.generic_params.remove(param);
                        }
                        return error.UnknownType;
                    };
                    break :blk switch (info) {
                        .struct_type => |s| s.size,
                        .primitive, .enum_type => 8,
                    };
                },
                .primitive, .enum_type, .array_type => 8,
                .optional_type => 8,
            };

            fields[i] = .{
                .name = field.name,
                .offset = offset,
                .value_type = value_type,
                .size = field_size,
            };
            offset += field_size;
        }

        const struct_info = StructTypeInfo{
            .name = mono_name,
            .fields = fields,
            .size = offset,
        };

        try self.type_map.put(self.allocator, mono_name, .{ .struct_type = struct_info });
        try self.type_decl_map.put(self.allocator, mono_name, type_decl);

        // Create generic bindings for lazy method generation
        var bindings = try self.allocator.alloc(PendingMethod.GenericBinding, type_decl.generic_params.len);
        for (type_decl.generic_params, type_args, 0..) |param, arg, i| {
            bindings[i] = .{ .param = param, .concrete = arg };
        }

        // Register method signatures only (lazy generation - IR generated on-demand)
        for (type_decl.methods) |*method| {
            try self.registerMethodSignatureOnly(mono_name, method, bindings);
        }

        // Restore in_stdlib_method flag
        self.in_stdlib_method = saved_in_stdlib;

        // Restore generic params after registration
        for (type_decl.generic_params, 0..) |param, i| {
            if (saved_params[i]) |prev_value| {
                try self.generic_params.put(self.allocator, param, prev_value);
            } else {
                _ = self.generic_params.remove(param);
            }
        }

        return mono_name;
    }

    /// Register a method as a function with mangled name: TypeName$methodName
    /// For interface methods (qualified_name set), also registers TypeName$Interface.methodName
    fn registerMethod(self: *AstToIr, type_name: []const u8, method: ast.MethodDecl) !void {
        // Save current type context for Self resolution
        const saved_type = self.current_type_name;
        self.current_type_name = type_name;
        defer self.current_type_name = saved_type;

        // Always register under short method name for regular calls (e.g., TypeName$count)
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Check if method is already registered (avoid double allocation)
        const already_registered = self.func_map.contains(mangled_name);

        // For already registered methods, still need to check if interface qualified name needs registering
        if (already_registered) {
            if (method.qualified_name) |qualified| {
                const qualified_mangled = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, qualified });
                try self.module.trackString(qualified_mangled);
                if (!self.func_map.contains(qualified_mangled)) {
                    // Get the existing func_info and register under qualified name
                    const func_info = self.func_map.get(mangled_name).?;
                    try self.func_map.put(self.allocator, qualified_mangled, func_info);
                    debug.astToIr("Registered interface method '{s}' as '{s}' (added qualified name)", .{ method.name, qualified_mangled });
                }
            }
            return;
        }

        // Determine return type
        var ret_type_name: ?[]const u8 = null;
        var ret_value_type: ?ValueType = null;
        const ret_type: ir.Type = if (method.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    const type_info = self.type_map.get(resolved) orelse {
                        self.reportError(.E006, resolved);
                        return error.UnknownType;
                    };
                    if (type_info.isStruct()) ret_type_name = resolved;
                    break :blk type_info.irType();
                },
                .optional => |wrapped| {
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    // Preserve struct type name for optional struct types (e.g., Character or nil)
                    const wrapped_struct_name: ?[]const u8 = if (wrapped_value_type == .struct_type)
                        wrapped_value_type.struct_type
                    else
                        null;
                    ret_value_type = .{
                        .optional_type = .{
                            .wrapped = wrapped_value_type.toPrimitiveType(),
                            .wrapped_struct_type = wrapped_struct_name,
                        },
                    };
                    break :blk .ptr;
                },
            }
        } else .void;

        // Count params: +1 for implicit self if not static
        const extra_params: usize = if (method.is_static) 0 else 1;
        var param_types = try self.allocator.alloc(ParamType, method.params.len + extra_params);

        // First param is self (pointer to type instance) for instance methods
        if (!method.is_static) {
            param_types[0] = .{ .ty = .{ .struct_type = type_name } };
        }

        // Register explicit params
        for (method.params, 0..) |param, i| {
            param_types[i + extra_params] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
            };
        }

        const func_info = FuncInfo{
            .return_type = ret_type,
            .return_type_name = ret_type_name,
            .return_value_type = ret_value_type,
            .param_types = param_types,
        };

        // Free old param_types if overwriting an existing entry (e.g., from external registration)
        if (self.func_map.get(mangled_name)) |old_info| {
            if (old_info.param_types.len > 0) {
                self.allocator.free(old_info.param_types);
            }
        }

        try self.func_map.put(self.allocator, mangled_name, func_info);

        // For interface methods, also register under qualified name (e.g., TypeName$Stringable.toString)
        if (method.qualified_name) |qualified| {
            const qualified_mangled = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, qualified });
            try self.module.trackString(qualified_mangled);
            // Note: qualified_mangled shares the same param_types, so don't free when overwriting
            try self.func_map.put(self.allocator, qualified_mangled, func_info);
            debug.astToIr("Registered interface method '{s}' as '{s}' and '{s}'", .{ method.name, mangled_name, qualified_mangled });
        } else {
            debug.astToIr("Registered method '{s}' as '{s}' returning {s}", .{ method.name, mangled_name, ret_type.toIrName() });
        }
    }

    /// Register method signature only (for lazy generation of monomorphized types).
    /// Stores the method in pending_methods for on-demand IR generation.
    fn registerMethodSignatureOnly(
        self: *AstToIr,
        type_name: []const u8,
        method: *const ast.MethodDecl,
        bindings: []const PendingMethod.GenericBinding,
    ) !void {
        // Save current type context for Self resolution
        const saved_type = self.current_type_name;
        self.current_type_name = type_name;
        defer self.current_type_name = saved_type;

        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Check if method is already registered
        if (self.func_map.contains(mangled_name)) {
            // Still need to check if interface qualified name needs registering
            if (method.qualified_name) |qualified| {
                const qualified_mangled = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, qualified });
                try self.module.trackString(qualified_mangled);
                if (!self.func_map.contains(qualified_mangled)) {
                    const func_info = self.func_map.get(mangled_name).?;
                    try self.func_map.put(self.allocator, qualified_mangled, func_info);
                    // Also add to pending_methods if original is pending
                    if (!func_info.ir_generated) {
                        try self.pending_methods.put(self.allocator, qualified_mangled, .{
                            .type_name = type_name,
                            .method = method,
                            .generic_bindings = bindings,
                        });
                    }
                }
            }
            return;
        }

        // Determine return type (same logic as registerMethod)
        var ret_type_name: ?[]const u8 = null;
        var ret_value_type: ?ValueType = null;
        const ret_type: ir.Type = if (method.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    const type_info = self.type_map.get(resolved) orelse {
                        self.reportError(.E006, resolved);
                        return error.UnknownType;
                    };
                    if (type_info.isStruct()) ret_type_name = resolved;
                    break :blk type_info.irType();
                },
                .optional => |wrapped| {
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    const wrapped_struct_name: ?[]const u8 = if (wrapped_value_type == .struct_type)
                        wrapped_value_type.struct_type
                    else
                        null;
                    ret_value_type = .{
                        .optional_type = .{
                            .wrapped = wrapped_value_type.toPrimitiveType(),
                            .wrapped_struct_type = wrapped_struct_name,
                        },
                    };
                    break :blk .ptr;
                },
            }
        } else .void;

        // Count params: +1 for implicit self if not static
        const extra_params: usize = if (method.is_static) 0 else 1;
        var param_types = try self.allocator.alloc(ParamType, method.params.len + extra_params);

        // First param is self (pointer to type instance) for instance methods
        if (!method.is_static) {
            param_types[0] = .{ .ty = .{ .struct_type = type_name } };
        }

        // Register explicit params
        for (method.params, 0..) |param, i| {
            param_types[i + extra_params] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
            };
        }

        // Create FuncInfo with ir_generated = false (key difference from registerMethod)
        const func_info = FuncInfo{
            .return_type = ret_type,
            .return_type_name = ret_type_name,
            .return_value_type = ret_value_type,
            .param_types = param_types,
            .ir_generated = false,
        };

        try self.func_map.put(self.allocator, mangled_name, func_info);

        // Queue for lazy generation
        try self.pending_methods.put(self.allocator, mangled_name, .{
            .type_name = type_name,
            .method = method,
            .generic_bindings = bindings,
        });

        // For interface methods, also register under qualified name
        if (method.qualified_name) |qualified| {
            const qualified_mangled = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, qualified });
            try self.module.trackString(qualified_mangled);
            try self.func_map.put(self.allocator, qualified_mangled, func_info);
            try self.pending_methods.put(self.allocator, qualified_mangled, .{
                .type_name = type_name,
                .method = method,
                .generic_bindings = bindings,
            });
            debug.astToIr("Registered lazy method '{s}' as '{s}' and '{s}'", .{ method.name, mangled_name, qualified_mangled });
        } else {
            debug.astToIr("Registered lazy method '{s}' as '{s}' returning {s}", .{ method.name, mangled_name, ret_type.toIrName() });
        }
    }

    // ------------------------------------------------------------------------
    // Context Save/Restore for Lazy Method Generation
    // ------------------------------------------------------------------------

    /// Saved conversion context for reentrant method generation
    const SavedContext = struct {
        func_idx: ?usize,
        func_name: ?[]const u8,
        sret_ptr: ?ir.Value,
        sret_size: i32,
        sret_optional_info: ?OptionalInfo,
        self_ptr: ?ir.Value,
        var_map: std.StringHashMapUnmanaged(VarInfo),
        in_stdlib_method: bool,
        loop_end_block: ?u32,
        loop_cond_block: ?u32,
    };

    /// Save current conversion context before generating a method
    fn saveConversionContext(self: *AstToIr) SavedContext {
        // Compute function index if current_func exists
        // (we save index, not pointer, because ArrayList may reallocate)
        const func_idx: ?usize = if (self.current_func) |f| blk: {
            for (self.module.functions.items, 0..) |*fn_ptr, i| {
                if (fn_ptr == f) break :blk i;
            }
            break :blk null;
        } else null;

        const saved = SavedContext{
            .func_idx = func_idx,
            .func_name = self.current_func_name,
            .sret_ptr = self.sret_ptr,
            .sret_size = self.sret_size,
            .sret_optional_info = self.sret_optional_info,
            .self_ptr = self.self_ptr,
            .var_map = self.var_map,
            .in_stdlib_method = self.in_stdlib_method,
            .loop_end_block = self.loop_end_block,
            .loop_cond_block = self.loop_cond_block,
        };

        // Clear var_map for new method
        self.var_map = .{};

        return saved;
    }

    /// Restore conversion context after generating a method
    fn restoreConversionContext(self: *AstToIr, saved: SavedContext) void {
        // Free the temporary var_map used for method conversion
        self.var_map.deinit(self.allocator);

        // Restore all saved state
        self.current_func = if (saved.func_idx) |idx| &self.module.functions.items[idx] else null;
        self.current_func_name = saved.func_name;
        self.sret_ptr = saved.sret_ptr;
        self.sret_size = saved.sret_size;
        self.sret_optional_info = saved.sret_optional_info;
        self.self_ptr = saved.self_ptr;
        self.var_map = saved.var_map;
        self.in_stdlib_method = saved.in_stdlib_method;
        self.loop_end_block = saved.loop_end_block;
        self.loop_cond_block = saved.loop_cond_block;
    }

    /// Ensure a method has been generated (lazy generation entry point).
    /// Called when a method is about to be invoked but hasn't had its IR generated yet.
    fn ensureMethodGenerated(self: *AstToIr, mangled_name: []const u8) ConvertError!void {
        // Check if already in progress (prevents infinite recursion for mutual calls)
        if (self.methods_in_progress.contains(mangled_name)) {
            return;
        }

        const pending = self.pending_methods.get(mangled_name) orelse {
            // Already generated or not pending
            return;
        };

        // Mark as in-progress before generation
        try self.methods_in_progress.put(self.allocator, mangled_name, {});
        defer _ = self.methods_in_progress.remove(mangled_name);

        // Save context before generating the method
        const saved_context = self.saveConversionContext();

        // Save previous generic param values (for reentrancy with nested generic types)
        var saved_generic_params = try self.allocator.alloc(?[]const u8, pending.generic_bindings.len);
        defer self.allocator.free(saved_generic_params);
        for (pending.generic_bindings, 0..) |binding, i| {
            saved_generic_params[i] = self.generic_params.get(binding.param);
            try self.generic_params.put(self.allocator, binding.param, binding.concrete);
        }

        // Allow stdlib builtins for stdlib types
        self.in_stdlib_method = true;

        // Generate the method IR
        var gen_err: ?ConvertError = null;
        self.convertMethod(pending.type_name, pending.method.*) catch |e| {
            gen_err = e;
        };

        // Restore generic params to their previous values
        for (pending.generic_bindings, 0..) |binding, i| {
            if (saved_generic_params[i]) |prev_value| {
                self.generic_params.put(self.allocator, binding.param, prev_value) catch {};
            } else {
                _ = self.generic_params.remove(binding.param);
            }
        }

        // Restore context after generation
        self.restoreConversionContext(saved_context);

        // Propagate error if method generation failed
        if (gen_err) |e| {
            return e;
        }

        // Mark as generated in func_map
        if (self.func_map.getPtr(mangled_name)) |info_ptr| {
            info_ptr.ir_generated = true;
        }

        // Remove from pending
        _ = self.pending_methods.remove(mangled_name);

        // Also handle qualified name variant if present
        if (pending.method.qualified_name) |qualified| {
            const qualified_mangled = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ pending.type_name, qualified });
            _ = self.pending_methods.remove(qualified_mangled);
            if (self.func_map.getPtr(qualified_mangled)) |info_ptr| {
                info_ptr.ir_generated = true;
            }
        }

        debug.astToIr("Lazily generated method '{s}'", .{mangled_name});
    }

    // ------------------------------------------------------------------------
    // Method Conversion
    // ------------------------------------------------------------------------

    /// Convert a method to IR
    fn convertMethod(self: *AstToIr, type_name: []const u8, method: ast.MethodDecl) !void {
        // Save current type context
        const saved_type = self.current_type_name;
        self.current_type_name = type_name;
        defer self.current_type_name = saved_type;

        // Generate mangled name using short method name (matches registerMethod)
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Determine return type (same as registerMethod)
        var uses_sret = false;
        var sret_struct_size: i32 = 0;
        var sret_opt_info: ?OptionalInfo = null;
        if (method.return_type) |rt| {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    if (self.type_map.get(resolved)) |type_info| {
                        if (type_info == .struct_type) {
                            uses_sret = true;
                            sret_struct_size = type_info.struct_type.size;
                        }
                    }
                },
                .optional => |wrapped| {
                    // Optional size = 8 (tag) + wrapped_value_size
                    // For struct types (e.g., Character), this correctly accounts for their size
                    uses_sret = true;
                    sret_struct_size = 8 + self.getTypeSize(wrapped.*);
                    // Track the wrapped type info for proper return handling
                    const wrapped_struct_name = getStructNameFromTypeExpr(wrapped.*);
                    sret_opt_info = .{
                        .wrapped = getIrTypeFromTypeExpr(wrapped.*),
                        .wrapped_struct_type = wrapped_struct_name,
                    };
                },
            }
        }

        const ret_type: ir.Type = if (method.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    break :blk try self.lookupIrType(resolved);
                },
                .optional => break :blk .ptr,
            }
        } else .void;

        const ir_func = try self.module.addFunctionWithExport(mangled_name, ret_type, method.is_export);
        // Set alias for interface methods (e.g., Type$Interface.method)
        if (method.qualified_name) |qualified| {
            const alias = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, qualified });
            try self.module.trackString(alias);
            ir_func.alias = alias;
        }

        self.current_func = ir_func;
        self.current_func_name = mangled_name;
        self.var_map.clearRetainingCapacity();
        _ = try ir_func.addBlock("entry");

        // Reset sret state
        self.sret_ptr = null;
        self.sret_size = 0;
        self.sret_optional_info = null;
        self.self_ptr = null;

        // Parameter offset for sret
        var param_offset: i32 = 0;
        if (uses_sret) {
            self.sret_ptr = try ir_func.emitParam(0, .ptr);
            self.sret_size = sret_struct_size;
            self.sret_optional_info = sret_opt_info;
            param_offset = 1;
        }

        // Register implicit self parameter for instance methods
        if (!method.is_static) {
            const self_val = try ir_func.emitParam(param_offset, .ptr);
            try ir_func.setValueName(self_val, "self");
            self.self_ptr = self_val;

            // Register self as a variable for explicit self.field access
            // Use initParam since self is a parameter (caller owns the memory)
            try self.var_map.put(self.allocator, "self", VarInfo.initParam(
                self_val,
                .{ .struct_type = type_name },
                false, // self is immutable
                false,
            ));

            param_offset += 1;
        }

        // Register explicit parameters
        for (method.params, 0..) |param, i| {
            try self.registerParameter(param, @as(i32, @intCast(i)) + param_offset);
        }

        // Convert body
        for (method.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // For void methods, add implicit return
        if (ret_type == .void) {
            const block = self.func().currentBlock() orelse return;
            const needs_implicit_ret = block.instructions.items.len == 0 or
                block.instructions.items[block.instructions.items.len - 1].op != .ret;
            if (needs_implicit_ret) {
                try self.func().emitRet(null);
            }
        }

        // Skip unused variable check for 'self' - it's always implicitly used
        if (self.var_map.getPtr("self")) |self_info| {
            self_info.used = true;
        }

        // Check for unused variables (skip '_' which is the conventional "ignore" name)
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (!entry.value_ptr.used and !std.mem.eql(u8, entry.key_ptr.*, "_")) {
                debug.astToIr("error: unused variable '{s}'\n", .{entry.key_ptr.*});
                self.reportError(.E014, entry.key_ptr.*);
                return error.UnusedVariable;
            }
        }

        // Clear method context
        self.self_ptr = null;
    }

    // ------------------------------------------------------------------------
    // Function Conversion
    // ------------------------------------------------------------------------

    fn convertFunction(self: *AstToIr, decl: ast.FunctionDecl) !void {
        // Check if this function returns a struct (needs sret)
        var uses_sret = false;
        var sret_struct_size: i32 = 0;
        var sret_opt_info: ?OptionalInfo = null;
        if (decl.return_type) |rt| {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    if (self.type_map.get(base_name)) |type_info| {
                        if (type_info == .struct_type) {
                            uses_sret = true;
                            sret_struct_size = type_info.struct_type.size;
                        }
                    }
                },
                .optional => |wrapped| {
                    // Optional size = 8 (tag) + wrapped_value_size
                    // For struct types (e.g., Character), this correctly accounts for their size
                    uses_sret = true;
                    sret_struct_size = 8 + self.getTypeSize(wrapped.*);
                    // Track the wrapped type info for proper return handling
                    const wrapped_struct_name = getStructNameFromTypeExpr(wrapped.*);
                    sret_opt_info = .{
                        .wrapped = getIrTypeFromTypeExpr(wrapped.*),
                        .wrapped_struct_type = wrapped_struct_name,
                    };
                },
            }
        }

        const ret_type: ir.Type = if (decl.return_type) |rt| blk: {
            switch (rt) {
                .simple, .generic => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    break :blk try self.lookupIrType(base_name);
                },
                .optional => break :blk .ptr,
            }
        } else .void;

        const ir_func = try self.module.addFunctionWithExport(decl.name, ret_type, decl.is_export);
        self.current_func = ir_func;
        self.current_func_name = decl.name;
        self.var_map.clearRetainingCapacity();
        _ = try ir_func.addBlock("entry");

        // Reset sret state
        self.sret_ptr = null;
        self.sret_size = 0;
        self.sret_optional_info = null;

        // If returning struct, first parameter is sret pointer
        var param_offset: i32 = 0;
        if (uses_sret) {
            self.sret_ptr = try ir_func.emitParam(0, .ptr);
            self.sret_size = sret_struct_size;
            self.sret_optional_info = sret_opt_info;
            param_offset = 1;
        }

        // Register parameters (offset by 1 if using sret)
        for (decl.params, 0..) |param, i| {
            try self.registerParameter(param, @as(i32, @intCast(i)) + param_offset);
        }

        // Convert body
        for (decl.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // For void functions, add implicit return if the body doesn't end with a return
        if (ret_type == .void) {
            const block = self.func().currentBlock() orelse return;
            const needs_implicit_ret = block.instructions.items.len == 0 or
                block.instructions.items[block.instructions.items.len - 1].op != .ret;
            if (needs_implicit_ret) {
                try self.func().emitRet(null);
            }
        }

        // Check for unused variables (skip '_' which is the conventional "ignore" name)
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (!entry.value_ptr.used and !std.mem.eql(u8, entry.key_ptr.*, "_")) {
                debug.astToIr("error: unused variable '{s}'\n", .{entry.key_ptr.*});
                self.reportError(.E014, entry.key_ptr.*);
                return error.UnusedVariable;
            }
        }
    }

    fn registerParameter(self: *AstToIr, param: ast.ParamDecl, idx: i32) !void {
        const value_type = try self.typeExprToValueType(param.type_expr);

        switch (value_type) {
            .struct_type => |struct_name| {
                const param_val = try self.func().emitParam(idx, .ptr);
                try self.func().setValueName(param_val, param.name);

                // For stdlib Array parameters, check if mutation transfers ownership
                var owns_memory = false;
                if (std.mem.startsWith(u8, struct_name, "Array$")) {
                    if (self.mutation_analyzer) |analyzer| {
                        if (self.current_func_name) |func_name| {
                            const source_param_idx: usize = if (self.sret_ptr != null)
                                @intCast(idx - 1)
                            else
                                @intCast(idx);
                            if (analyzer.doesMutateParam(func_name, source_param_idx)) {
                                owns_memory = true;
                            }
                        }
                    }
                }

                // Use initParam unless ownership was transferred via mutation
                var var_info = VarInfo.initParam(
                    param_val,
                    .{ .struct_type = struct_name },
                    true,
                    false,
                );
                if (owns_memory) {
                    var_info.is_parameter = false; // We now own it, should free on return
                }
                try self.var_map.put(self.allocator, param.name, var_info);
            },
            .array_type, .optional_type => {
                // Reference types are passed as pointers - store in a stack slot
                const param_val = try self.func().emitParam(idx, .ptr);
                const slot_ptr = try self.func().emitAlloca(.ptr);
                try self.func().setValueName(slot_ptr, param.name);
                try self.func().emitStore(slot_ptr, param_val);

                // For dynamic arrays, check if mutation transfers ownership
                var final_type = value_type;
                if (value_type == .array_type) {
                    const arr_info = value_type.array_type;
                    if (arr_info.size == null) {
                        if (self.mutation_analyzer) |analyzer| {
                            if (self.current_func_name) |func_name| {
                                const source_param_idx: usize = if (self.sret_ptr != null)
                                    @intCast(idx - 1)
                                else
                                    @intCast(idx);
                                if (analyzer.doesMutateParam(func_name, source_param_idx)) {
                                    var modified = arr_info;
                                    modified.storage = .heap;
                                    final_type = .{ .array_type = modified };
                                }
                            }
                        }
                    }
                }

                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    slot_ptr,
                    final_type,
                    true,
                    true,
                ));
            },
            .primitive, .enum_type => {
                const ir_type = value_type.toPrimitiveType();
                const param_val = try self.func().emitParam(idx, ir_type);
                try self.func().setValueName(param_val, param.name);
                const ptr = try self.func().emitAlloca(ir_type);
                try self.func().setValueName(ptr, param.name);
                try self.func().emitStore(ptr, param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    ptr,
                    value_type,
                    true,
                    false,
                ));
            },
        }
    }

    // ------------------------------------------------------------------------
    // Statement Conversion
    // ------------------------------------------------------------------------

    fn convertStatement(self: *AstToIr, stmt: ast.Statement) !void {
        // Track current line and column from AST for error reporting
        self.current_line = stmt.line;
        self.current_column = stmt.column;

        switch (stmt.kind) {
            .let_decl => |decl| {
                self.current_decl_is_mutable = false;
                try self.convertVarDecl(decl);
            },
            .var_decl => |decl| {
                self.current_decl_is_mutable = true;
                try self.convertVarDecl(decl);
            },
            .@"return" => |ret| try self.convertReturn(ret),
            .assign => |assign| try self.convertAssignment(assign),
            .field_assign => |assign| try self.convertFieldAssign(assign),
            .index_assign => |assign| {
                debug.astToIr("Converting index_assign", .{});
                try self.convertIndexAssign(assign);
            },
            .call => |call| {
                _ = try self.convertCall(call);
                // Clean up any temporary strings created during this call
                try self.cleanupTemporaryStrings();
            },
            .method_call => |mcall| {
                try self.convertMethodCall(mcall);
                // Clean up any temporary strings created during this call
                try self.cleanupTemporaryStrings();
            },
            .if_stmt => |if_s| try self.convertIfStmt(if_s),
            .while_stmt => |while_s| try self.convertWhileStmt(while_s),
            .for_stmt => |for_s| try self.convertForStmt(for_s),
            .break_stmt => |brk| try self.convertBreakStmt(brk),
            .continue_stmt => |cont| try self.convertContinueStmt(cont),
            .else_unwrap_decl => |unwrap| try self.convertElseUnwrapDecl(unwrap),
        }
    }

    fn convertVarDecl(self: *AstToIr, decl: ast.VarDecl) !void {
        debug.astToIr("Converting var decl: {s}", .{decl.name});

        // Array types cannot be immutable (they have no initial contents)
        if (!self.current_decl_is_mutable and decl.value == .array_type) {
            self.reportError(.E013, decl.name);
            return error.SemanticError;
        }

        // Check for InitableFromArrayLiteral transformation
        // Syntax: var arr Array of int = [1, 2, 3] (generic) or var arr IntArray = [...] (simple)
        if (decl.type_annotation) |type_ann| {
            const base_type_name: ?[]const u8 = switch (type_ann) {
                .generic => |gen| gen.base_type,
                .simple => |name| name,
                .optional => null,
            };
            if (base_type_name) |t_name| {
                if (self.typeConformsTo(t_name, "InitableFromArrayLiteral")) {
                    if (decl.value == .array_literal) {
                        // For generic types, get the monomorphized type name
                        const resolved_type_name: []const u8 = switch (type_ann) {
                            .generic => |gen| blk: {
                                var resolved_args = try self.allocator.alloc([]const u8, gen.type_args.len);
                                defer self.allocator.free(resolved_args);
                                for (gen.type_args, 0..) |arg, i| {
                                    resolved_args[i] = self.resolveTypeName(arg);
                                }
                                break :blk try self.getOrCreateMonomorphizedType(gen.base_type, resolved_args);
                            },
                            .simple => |name| name,
                            .optional => unreachable,
                        };
                        try self.convertInitableFromArrayLiteralSimple(decl, resolved_type_name);
                        return;
                    }
                }

                // Check for InitableFromMapLiteral transformation
                // Syntax: var m Map from K to V = ["a": 1, "b": 2] (generic) or var m StringIntMap = [...] (simple)
                if (self.typeConformsTo(t_name, "InitableFromMapLiteral")) {
                    if (decl.value == .map_literal) {
                        // For generic types, get the monomorphized type name
                        const resolved_type_name: []const u8 = switch (type_ann) {
                            .generic => |gen| blk: {
                                var resolved_args = try self.allocator.alloc([]const u8, gen.type_args.len);
                                defer self.allocator.free(resolved_args);
                                for (gen.type_args, 0..) |arg, i| {
                                    resolved_args[i] = self.resolveTypeName(arg);
                                }
                                break :blk try self.getOrCreateMonomorphizedType(gen.base_type, resolved_args);
                            },
                            .simple => |name| name,
                            .optional => unreachable,
                        };
                        try self.convertInitableFromMapLiteralSimple(decl, resolved_type_name);
                        return;
                    }
                }

                // Check for InitableFromStringLiteral transformation
                // Syntax: var s string = "hello" or var s String = "hello"
                if (self.typeConformsTo(t_name, "InitableFromStringLiteral")) {
                    if (decl.value == .string_literal) {
                        const resolved_type_name = self.resolveTypeName(t_name);
                        try self.convertInitableFromStringLiteral(decl, resolved_type_name);
                        return;
                    }
                }

                // Check for InitableFromCharLiteral transformation
                // Syntax: var c character = 'a' or var c Character = 'a'
                if (self.typeConformsTo(t_name, "InitableFromCharLiteral")) {
                    if (decl.value == .char_literal) {
                        const resolved_type_name = self.resolveTypeName(t_name);
                        try self.convertInitableFromCharLiteral(decl, resolved_type_name);
                        return;
                    }
                }
            }
        }

        const init_typed = try self.convertExpression(decl.value);

        // Heap arrays and optionals use a slot to hold the pointer
        const uses_slot = (init_typed.ty == .array_type and init_typed.ty.array_type.storage == .heap) or
            init_typed.ty == .optional_type;

        // Primitives/enums need alloca for the value; slot types need alloca for the pointer
        const needs_alloca = init_typed.ty == .primitive or init_typed.ty == .enum_type or uses_slot;

        // Struct types from field accesses need to be copied to a new local allocation
        // to avoid aliasing issues (e.g., var oldElements = elements before overwriting elements)
        const needs_struct_copy = init_typed.ty == .struct_type and
            (decl.value == .identifier or decl.value == .field_access);

        const ptr = if (needs_struct_copy) blk: {
            // Allocate new memory for the struct and copy data
            const struct_name = init_typed.ty.struct_type;
            const struct_info = self.type_map.get(struct_name) orelse {
                self.reportError(.E006, struct_name);
                return error.UnknownType;
            };
            const size = struct_info.struct_type.size;
            const p = try self.func().emitAllocaSized(size);
            try self.func().setValueName(p, decl.name);
            try self.emitStructCopy(p, init_typed.value, size, struct_name);
            break :blk p;
        } else if (needs_alloca) blk: {
            const alloca_type = if (uses_slot) .ptr else init_typed.ty.toPrimitiveType();
            const p = try self.func().emitAlloca(alloca_type);
            try self.func().setValueName(p, decl.name);
            try self.func().emitStore(p, init_typed.value);
            break :blk p;
        } else blk: {
            try self.func().setValueName(init_typed.value, decl.name);
            break :blk init_typed.value;
        };

        try self.var_map.put(self.allocator, decl.name, VarInfo.init(
            ptr,
            init_typed.ty,
            self.current_decl_is_mutable,
            uses_slot,
        ));

        // If this was a temporary String, the variable now owns it - remove from temporaries
        if (self.isStringType(init_typed.ty)) {
            self.removeFromTemporaries(init_typed.value);
        }

        // Handle borrow tracking: if this variable was initialized from a slice operation,
        // establish the borrow relationship between source and this variable
        // Handle borrow tracking
        if (self.pending_borrow_source) |source_name| {
            // Mark this new variable as a slice that borrows from source
            if (self.var_map.getPtr(decl.name)) |new_var_info| {
                new_var_info.borrow_state = .slice;
                new_var_info.borrowed_from = source_name;
            }
            // Mark the source variable as borrowed
            self.markStringBorrowed(source_name);
            // Clear the pending borrow source
            self.pending_borrow_source = null;
        }
    }

    fn convertReturn(self: *AstToIr, ret: ast.ReturnStmt) !void {
        // Evaluate return expression first (before cleanup)
        var ret_value: ?ir.Value = null;

        if (ret.value) |expr| {
            // If using sret and returning a struct literal, write directly to sret buffer
            if (self.sret_ptr) |sret| {
                if (expr == .struct_init) {
                    if (self.sret_optional_info != null) {
                        // Returning struct init from optional-returning function
                        // Write tag = 1 (has value) first
                        const one = try self.func().emitConstI64(1);
                        try self.func().emitStore(sret, one);
                        // Initialize struct at offset 8 (after the tag)
                        const value_ptr = try self.func().emitGetFieldPtr(sret, 8);
                        try self.initStructInto(expr.struct_init, value_ptr);
                    } else {
                        // Initialize struct directly into sret buffer (no intermediate copy)
                        try self.initStructInto(expr.struct_init, sret);
                    }
                    try self.freeHeapAllocations();
                    try self.func().emitRet(sret);
                    return;
                }

                // Special handling for returning nil from optional-returning function
                if (expr == .nil_lit and self.sret_optional_info != null) {
                    // Write tag = 0 (nil) directly to sret
                    const zero = try self.func().emitConstI64(0);
                    try self.func().emitStore(sret, zero);
                    try self.freeHeapAllocations();
                    try self.func().emitRet(sret);
                    return;
                }

                // Returning an existing struct variable or optional - copy to sret buffer
                const typed_val = try self.convertExpression(expr);

                // If returning a non-optional value from an optional-returning function,
                // wrap the value in a Some optional
                if (self.sret_optional_info != null and typed_val.ty != .optional_type) {
                    const opt_info = self.sret_optional_info.?;
                    // Write tag = 1 (has value) to sret
                    const one = try self.func().emitConstI64(1);
                    try self.func().emitStore(sret, one);
                    // Write value at offset 8
                    const value_ptr = try self.func().emitGetFieldPtr(sret, 8);
                    // For struct types, memcpy the contents; for primitives, store the value
                    if (opt_info.wrapped_struct_type) |struct_name| {
                        if (self.type_map.get(struct_name)) |type_info| {
                            if (type_info == .struct_type) {
                                try self.func().emitMemcpy(value_ptr, typed_val.value, type_info.struct_type.size);
                            } else {
                                try self.func().emitStore(value_ptr, typed_val.value);
                            }
                        } else {
                            try self.func().emitStore(value_ptr, typed_val.value);
                        }
                    } else {
                        try self.func().emitStore(value_ptr, typed_val.value);
                    }
                } else {
                    // Check if returning __ManagedString to a String-returning function
                    const is_managed_string = typed_val.ty == .primitive and
                        std.mem.eql(u8, typed_val.ty.primitive, "__ManagedString");
                    const expects_string = self.current_func_name != null and
                        self.type_map.get("String") != null and self.sret_size == 32;

                    if (is_managed_string and expects_string) {
                        // Wrap __ManagedString (24 bytes) into String (32 bytes)
                        // Copy managed string to offset 0-23
                        try self.func().emitMemcpy(sret, typed_val.value, MANAGED_STRING_SIZE);
                        // Set _iterPos to 0 at offset 24
                        const iter_pos_ptr = try self.func().emitGetFieldPtr(sret, 24);
                        try self.func().emitStore(iter_pos_ptr, try self.func().emitConstI64(0));
                    } else {
                        try self.func().emitMemcpy(sret, typed_val.value, self.sret_size);
                    }
                }
                try self.freeHeapAllocations();
                try self.func().emitRet(sret);
                return;
            }

            const typed_val = try self.convertExpression(expr);

            // Convert float to int if needed
            if (self.func().return_type == .i64 and typed_val.ty.toPrimitiveType() == .f64) {
                ret_value = try self.func().emitUnaryOp(.fptosi, typed_val.value, .i64);
            } else {
                ret_value = typed_val.value;
            }
        }

        // Free heap allocations before return
        try self.freeHeapAllocations();

        try self.func().emitRet(ret_value);
    }

    /// Free heap-allocated variables, optionally filtering to only loop-scoped vars
    fn freeHeapVars(self: *AstToIr, exclude_vars: ?*std.StringHashMapUnmanaged(OwnershipState)) !void {
        // Borrow checking: first clear borrows from slice variables that are going out of scope
        self.clearSliceBorrows(exclude_vars);

        // Borrow checking: verify no borrowed strings go out of scope
        try self.checkNoOutstandingBorrows();

        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (exclude_vars) |excluded| {
                if (excluded.contains(entry.key_ptr.*)) continue;
            }
            const var_info = entry.value_ptr.*;
            if (var_info.ty == .array_type) {
                const arr_info = var_info.ty.array_type;
                if (arr_info.storage == .heap and var_info.state != .moved) {
                    // var_info.ptr points to a stack slot holding a ptr to __ManagedArray
                    // __ManagedArray layout: [buffer_ptr, len, capacity] at offsets [0, 8, 16]
                    // We need to free the buffer_ptr, not the __ManagedArray itself
                    const managed_ptr = try self.func().emitLoad(var_info.ptr, .ptr);
                    const buffer_ptr = try self.func().emitLoad(managed_ptr, .ptr);
                    try self.func().emitHeapFree(buffer_ptr);
                }
            }
            // Handle stdlib Array types (Array$int, etc.)
            // These contain an inlined __ManagedArray with a heap-allocated buffer
            if (var_info.ty == .struct_type) {
                if (std.mem.startsWith(u8, var_info.ty.struct_type, "Array$") and var_info.state != .moved) {
                    // Only free if we own the memory (not a borrowed parameter)
                    if (var_info.is_parameter) continue;

                    // var_info.ptr points to the Array struct (32 bytes)
                    // The managed field (__ManagedArray) is inlined at offset 0
                    // The _buffer field (heap ptr) is at offset 0 within __ManagedArray
                    // So the buffer pointer is at the very start of the struct
                    const buf_ptr = try self.func().emitLoad(var_info.ptr, .ptr);
                    try self.func().emitHeapFree(buf_ptr);
                }
                // Handle String type with COW semantics
                // Uses decref instead of direct free to support reference counting
                if (self.isStringType(var_info.ty) and var_info.state != .moved) {
                    // Only free if we own the memory (not a borrowed parameter)
                    if (var_info.is_parameter) continue;

                    // Use COW decref which checks heap mode and frees if refcount reaches 0
                    try self.emitStringDecref(var_info.ptr);
                }
                // Handle cstring type cleanup
                // cstring struct: data(8) + length(8) + managed(8)
                // If managed != null: decref the __ManagedString
                // If managed == null: free the data pointer (cstring owns its buffer from slice copy)
                if (std.mem.eql(u8, var_info.ty.struct_type, "cstring") and var_info.state != .moved) {
                    if (var_info.is_parameter) continue;
                    try self.emitCstringCleanup(var_info.ptr);
                }
            }
        }
    }

    fn freeHeapAllocations(self: *AstToIr) !void {
        try self.freeHeapVars(null);
    }

    fn freeLoopScopedHeapVars(self: *AstToIr, pre_loop_vars: *std.StringHashMapUnmanaged(OwnershipState)) !void {
        try self.freeHeapVars(pre_loop_vars);
    }

    fn convertAssignment(self: *AstToIr, assign: ast.AssignStmt) ConvertError!void {
        // First try as a regular variable
        if (self.var_map.getPtr(assign.target)) |var_info| {
            if (!var_info.is_mutable) {
                debug.astToIr("cannot assign to immutable variable '{s}'\n", .{assign.target});
                self.reportError(.E009, assign.target);
                return error.ImmutableAssign;
            }
            // Check if this string is borrowed - cannot modify while borrowed
            try self.checkStringNotBorrowed(assign.target);

            var_info.used = true;

            // IMPORTANT: Evaluate RHS BEFORE freeing old value, since RHS may reference the old value
            // (e.g., result = result.concat(b) needs result's buffer while evaluating RHS)
            const value_typed = try self.convertExpression(assign.value);

            // For heap arrays, free old memory AFTER evaluating RHS
            if (var_info.ty == .array_type) {
                const arr_info = var_info.ty.array_type;
                if (arr_info.storage == .heap and var_info.state != .moved) {
                    // var_info.ptr points to a stack slot holding a ptr to __ManagedArray
                    // __ManagedArray layout: [buffer_ptr, len, capacity] at offsets [0, 8, 16]
                    // We need to free the buffer_ptr, not the __ManagedArray itself
                    const managed_ptr = try self.func().emitLoad(var_info.ptr, .ptr);
                    const buffer_ptr = try self.func().emitLoad(managed_ptr, .ptr);
                    try self.func().emitHeapFree(buffer_ptr);
                }
            }
            // For stdlib Arrays/Strings, free old buffer AFTER evaluating RHS
            if (var_info.ty == .struct_type) {
                if (std.mem.startsWith(u8, var_info.ty.struct_type, "Array$") and var_info.state != .moved) {
                    // Load the buffer pointer from offset 0 and free it
                    const old_buf_ptr = try self.func().emitLoad(var_info.ptr, .ptr);
                    try self.func().emitHeapFree(old_buf_ptr);
                }
                // For String, use COW decref AFTER evaluating RHS
                if (self.isStringType(var_info.ty) and var_info.state != .moved) {
                    try self.emitStringDecref(var_info.ptr);
                }
            }

            const is_reference = var_info.ty == .struct_type or var_info.ty == .array_type or var_info.ty == .optional_type;
            if (is_reference and !var_info.uses_slot) {
                // For struct reassignment, we need to copy the data to the original stack location
                // Just updating var_info.ptr would cause loops to use stale data
                if (var_info.ty == .struct_type) {
                    const struct_name = var_info.ty.struct_type;
                    const struct_info = self.type_map.get(struct_name) orelse {
                        self.reportError(.E006, struct_name);
                        return error.UnknownType;
                    };
                    const size = struct_info.struct_type.size;
                    try self.func().emitMemcpy(var_info.ptr, value_typed.value, size);
                    // Handle String refcount - the new value's refcount is already correct from expression evaluation
                    // but we need to incref if copying from another variable
                    if (assign.value == .identifier or assign.value == .field_access) {
                        if (std.mem.eql(u8, struct_name, "String") or std.mem.eql(u8, struct_name, "__ManagedString")) {
                            try self.emitStringIncref(var_info.ptr);
                        }
                    }
                    // For String assignment from a temporary (literal/interpolation/etc),
                    // remove the source from temporaries since ownership was transferred to the variable
                    if (self.isStringType(var_info.ty)) {
                        self.removeFromTemporaries(value_typed.value);
                    }
                } else {
                    var_info.ptr = value_typed.value;
                }
            } else {
                try self.func().emitStore(var_info.ptr, value_typed.value);
            }
            if (is_reference) {
                var_info.ty = value_typed.ty;
            }

            // COW: If assigning a String from another variable/field, incref the new value
            if (self.isStringType(value_typed.ty) and (assign.value == .identifier or assign.value == .field_access)) {
                try self.emitStringIncref(var_info.ptr);
            }
            var_info.resetOwnership();
            return;
        }

        // If inside a method, check if target is a field (implicit self)
        if (self.self_ptr != null and self.current_type_name != null) {
            const type_name = self.current_type_name.?;
            if (self.type_map.get(type_name)) |type_info| {
                if (type_info == .struct_type) {
                    const struct_info = type_info.struct_type;
                    for (struct_info.fields) |field| {
                        if (std.mem.eql(u8, field.name, assign.target)) {
                            // Check if field is mutable
                            if (!self.isFieldMutable(type_name, assign.target)) {
                                debug.astToIr("cannot assign to immutable field '{s}'\n", .{assign.target});
                                self.reportError(.E009, assign.target);
                                return error.ImmutableAssign;
                            }

                            // Mark self as used
                            if (self.var_map.getPtr("self")) |info| {
                                info.used = true;
                            }

                            const value_typed = try self.convertExpression(assign.value);
                            const self_val = self.self_ptr.?;
                            const field_ptr = try self.func().emitGetFieldPtr(self_val, field.offset);

                            // Track ownership transfer: source variable is moved to field
                            try self.trackFieldOwnershipTransfer(assign.value, type_name);

                            if (field.isStruct()) {
                                // Struct fields are embedded inline - copy the full data
                                try self.emitStructCopy(field_ptr, value_typed.value, field.size, field.structName());
                            } else {
                                try self.func().emitStore(field_ptr, value_typed.value);
                            }
                            return;
                        }
                    }
                }
            }
        }

        // Variable not found
        debug.astToIr("error: undefined variable '{s}'\n", .{assign.target});
        self.reportError(.E005, assign.target);
        return error.UndefinedVariable;
    }

    /// Check if a field is mutable in a type declaration
    fn isFieldMutable(self: *AstToIr, type_name: []const u8, field_name: []const u8) bool {
        _ = self;
        _ = type_name;
        _ = field_name;
        // For now, assume all fields are mutable (var fields)
        // TODO: Track field mutability in FieldInfo
        return true;
    }

    fn convertFieldAssign(self: *AstToIr, assign: ast.FieldAssign) ConvertError!void {
        const base = try self.convertExpression(assign.base.*);
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type => {
                std.debug.print("[AST->IR] convertFieldAssign: expected struct type for field '{s}'\n", .{assign.field_name});
                self.reportError(.E006, assign.field_name);
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try self.lookupField(struct_info, assign.field_name);
        const field_ptr = try self.func().emitGetFieldPtr(base.value, field_info.offset);
        const val_typed = try self.convertExpression(assign.value);

        if (field_info.isStruct()) {
            // Struct fields are embedded inline - copy the full data
            try self.emitStructCopy(field_ptr, val_typed.value, field_info.size, field_info.structName());
        } else {
            try self.func().emitStore(field_ptr, val_typed.value);
        }
    }

    fn convertIndexAssign(self: *AstToIr, assign: ast.IndexAssign) ConvertError!void {
        // Check if base is an immutable variable
        if (assign.base.* == .identifier) {
            const var_name = assign.base.identifier;
            if (self.var_map.get(var_name)) |var_info| {
                if (!var_info.is_mutable) {
                    self.reportError(.E009, var_name);
                    return error.ImmutableAssign;
                }
            }
        }

        const base_typed = try self.convertExpression(assign.base.*);

        // Handle stdlib Array types (Array$int, etc.) - call .set() method
        if (base_typed.ty == .struct_type) {
            if (std.mem.startsWith(u8, base_typed.ty.struct_type, "Array$")) {
                try self.convertStdlibArrayIndexAssign(base_typed, assign.index.*, assign.value);
                return;
            }
        }

        const idx_typed = try self.convertExpression(assign.index.*);
        const val_typed = try self.convertExpression(assign.value);

        // Calculate element size for struct arrays
        const arr_info = switch (base_typed.ty) {
            .array_type => |a| a,
            else => {
                // Not an array type, use default size
                const elem_ptr = try self.func().emitGetElemPtr(base_typed.value, idx_typed.value, 8);
                try self.func().emitStore(elem_ptr, val_typed.value);
                return;
            },
        };

        // For heap arrays, base_typed.value points to the __ManagedArray structure
        // We need to load the buffer pointer from offset 0 to index into the actual data
        const buffer_ptr: ir.Value = if (arr_info.storage == .heap)
            try self.func().emitLoad(base_typed.value, .ptr)
        else
            base_typed.value;

        const elem_size: i32 = if (arr_info.element_struct_type) |struct_name| blk: {
            if (self.type_map.get(struct_name)) |type_info| {
                if (type_info == .struct_type) {
                    break :blk type_info.struct_type.size;
                }
            }
            break :blk 8;
        } else 8;

        const elem_ptr = try self.func().emitGetElemPtr(buffer_ptr, idx_typed.value, elem_size);
        // For structs, copy the entire struct data; for primitives, store the value
        if (arr_info.element_struct_type) |struct_name| {
            try self.emitStructCopy(elem_ptr, val_typed.value, elem_size, struct_name);
        } else {
            try self.func().emitStore(elem_ptr, val_typed.value);
        }
    }

    fn convertIfStmt(self: *AstToIr, if_stmt: ast.IfStmt) ConvertError!void {
        // Convert condition expression
        const cond_typed = try self.convertExpression(if_stmt.condition);

        // For if-let, we need to check the optional's tag
        const is_if_let = if_stmt.binding_name != null;
        const condition_value = if (is_if_let) blk: {
            // Load tag from optional (offset 0) and compare with 1
            const tag = try self.func().emitLoad(cond_typed.value, .i64);
            const one = try self.func().emitConstI64(1);
            break :blk try self.func().emitBinaryOp(.icmp_eq, tag, one, .i64);
        } else cond_typed.value;

        // Determine what blocks we need
        const has_else = if_stmt.else_body != null or if_stmt.else_if != null;

        // Save entry block index - we'll emit br_cond later with correct target indices
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

        // Create then block
        const then_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock(if (is_if_let) "if_let_then" else "then");

        // Create else block (if needed) - actual index determined after then body
        if (has_else or is_if_let) {
            _ = try self.func().addBlock(if (is_if_let) "if_let_else" else "else");
        }

        // Create end block - actual index determined after all bodies
        _ = try self.func().addBlock(if (is_if_let) "if_let_end" else "end");

        // DON'T emit br_cond yet - nested ifs in body will shift block indices
        // We'll emit it after all blocks are restored with correct indices

        // Save end block, remove it temporarily
        const end_block = self.func().blocks.pop().?;

        // If we have else, save it too
        var else_block: ?ir.BasicBlock = null;
        if (has_else or is_if_let) {
            else_block = self.func().blocks.pop().?;
        }

        // Now current block is "then" block
        // For if-let, bind the unwrapped value before executing body
        if (if_stmt.binding_name) |binding_name| {
            const opt_info = switch (cond_typed.ty) {
                .optional_type => |info| info,
                .primitive, .struct_type, .array_type, .enum_type => OptionalInfo{ .wrapped = .i64 },
            };
            const wrapped_type = opt_info.wrapped;
            const value_ptr = try self.func().emitGetFieldPtr(cond_typed.value, 8);

            if (opt_info.wrapped_struct_type) |struct_name| {
                // For struct types, the struct data is stored inline after the tag (at offset 8).
                // value_ptr already points to this inline struct data.
                try self.func().setValueName(value_ptr, binding_name);
                try self.var_map.put(self.allocator, binding_name, VarInfo.init(value_ptr, .{ .struct_type = struct_name }, true, false));
            } else {
                // For primitive types, load the value and store in a new slot
                const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);
                const binding_ptr = try self.func().emitAlloca(wrapped_type);
                try self.func().setValueName(binding_ptr, binding_name);
                try self.func().emitStore(binding_ptr, unwrapped_value);
                try self.var_map.put(self.allocator, binding_name, VarInfo.init(
                    binding_ptr,
                    .{ .primitive = wrapped_type.toMaxonName() },
                    true,
                    false,
                ));
            }
        }

        // Convert then body statements
        for (if_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Remove binding from var_map after then body
        if (if_stmt.binding_name) |binding_name| {
            _ = self.var_map.remove(binding_name);
        }

        // Track the block that needs a branch to end (we'll add it after restoring end block)
        var then_exit_block_idx: ?u32 = null;
        if (self.func().currentBlock()) |block| {
            if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                then_exit_block_idx = @intCast(self.func().blocks.items.len - 1);
            }
        }

        // Restore and generate else block if needed
        var else_exit_block_idx: ?u32 = null;
        var actual_else_block_idx: u32 = undefined;
        if (has_else or is_if_let) {
            actual_else_block_idx = @intCast(self.func().blocks.items.len);
            try self.func().blocks.append(self.allocator, else_block.?);

            // Check for else-if chain (not allowed for if-let)
            if (if_stmt.else_if) |else_if| {
                try self.convertIfStmt(else_if.*);
            } else if (if_stmt.else_body) |else_body| {
                for (else_body) |stmt| {
                    try self.convertStatement(stmt);
                }
            }

            // Track the block that needs a branch to end
            if (self.func().currentBlock()) |block| {
                if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                    else_exit_block_idx = @intCast(self.func().blocks.items.len - 1);
                }
            }
        }

        // Restore end block - NOW get its actual index
        const actual_end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        try self.func().blocks.append(self.allocator, end_block);

        // NOW emit br_cond with correct block indices (after nested ifs shifted things)
        const branch_target_if_false = if (has_else or is_if_let) actual_else_block_idx else actual_end_block_idx;
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = condition_value }, .{ .block_ref = then_block_idx }, .{ .block_ref = branch_target_if_false } },
        });

        // Now emit the deferred branches with the correct end block index
        if (then_exit_block_idx) |idx| {
            try self.func().blocks.items[idx].instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = actual_end_block_idx }, .none, .none },
                .result = null,
            });
        }
        if (else_exit_block_idx) |idx| {
            try self.func().blocks.items[idx].instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = actual_end_block_idx }, .none, .none },
                .result = null,
            });
        }
    }

    fn convertWhileStmt(self: *AstToIr, while_stmt: ast.WhileStmt) ConvertError!void {
        // Create condition block - this will be the current block after creation
        const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("whilecond");

        // Emit unconditional branch from previous block to condition block
        try self.func().blocks.items[cond_block_idx - 1].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = cond_block_idx }, .none, .none },
            .result = null,
        });

        // Convert condition expression in condition block
        const cond_typed = try self.convertExpression(while_stmt.condition);

        // Create body block
        const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("whilebody");

        // Save loop context
        const saved_end_block = self.loop_end_block;
        const saved_cond_block = self.loop_cond_block;
        self.loop_cond_block = cond_block_idx;
        // We use a sentinel value for loop_end_block that we'll patch later
        // Use max u32 as sentinel to indicate "needs patching"
        self.loop_end_block = 0xFFFFFFFF;

        // Register labeled loop for break/continue with labels
        if (while_stmt.label.len > 0) {
            try self.labeled_loops.append(self.allocator, .{
                .label = while_stmt.label,
                .cond_block = cond_block_idx,
                .end_block = 0xFFFFFFFE - @as(u32, @intCast(self.labeled_loops.items.len)),
            });
        }

        // Save variable names and ownership state before entering loop body
        var pre_loop_vars = std.StringHashMapUnmanaged(OwnershipState){};
        defer pre_loop_vars.deinit(self.allocator);
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            try pre_loop_vars.put(self.allocator, entry.key_ptr.*, entry.value_ptr.state);
        }

        // Convert body statements (body block is current)
        for (while_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Check for variables moved inside loop body - they would be invalid on next iteration
        // Only flag an error if the variable was OWNED before the loop and is now MOVED
        var pre_iter = pre_loop_vars.iterator();
        while (pre_iter.next()) |entry| {
            if (self.var_map.getPtr(entry.key_ptr.*)) |var_info| {
                const was_owned = entry.value_ptr.* == .owned;
                if (was_owned and var_info.state == .moved) {
                    // Variable was moved in loop body - next iteration would use moved value
                    debug.astToIr("variable '{s}' was moved in loop body\n", .{entry.key_ptr.*});
                    self.reportError(.E008, entry.key_ptr.*);
                    return error.UseAfterMove;
                }
            }
        }

        // Free heap-allocated loop-scoped variables before branching back
        try self.freeLoopScopedHeapVars(&pre_loop_vars);

        // Emit unconditional branch back to condition block (if not already terminated)
        if (self.func().currentBlock()) |block| {
            const len = block.instructions.items.len;
            if (len == 0 or (block.instructions.items[len - 1].op != .ret and block.instructions.items[len - 1].op != .br and block.instructions.items[len - 1].op != .br_cond)) {
                try self.func().emitBr(cond_block_idx);
            }
        }

        // Remove loop-scoped variables from var_map
        var to_remove = std.ArrayListUnmanaged([]const u8){};
        defer to_remove.deinit(self.allocator);
        var var_iter = self.var_map.keyIterator();
        while (var_iter.next()) |key| {
            if (!pre_loop_vars.contains(key.*)) {
                try to_remove.append(self.allocator, key.*);
            }
        }
        for (to_remove.items) |key| {
            _ = self.var_map.remove(key);
        }

        // Now create continuation block - this is its final index
        const cont_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("whilecont");

        // Now emit the conditional branch in the condition block with the correct cont_block_idx
        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = cond_typed.value }, .{ .block_ref = body_block_idx }, .{ .block_ref = cont_block_idx } },
        });

        // Patch any break statements that used the sentinel value
        for (self.func().blocks.items[body_block_idx..cont_block_idx]) |*block| {
            for (block.instructions.items) |*instr| {
                if (instr.op == .br) {
                    if (instr.operands[0] == .block_ref and instr.operands[0].block_ref == 0xFFFFFFFF) {
                        instr.operands[0] = .{ .block_ref = cont_block_idx };
                    }
                }
            }
        }


        // Pop labeled loop from stack and patch its sentinel if we pushed one
        if (while_stmt.label.len > 0) {
            const labeled_loop = self.labeled_loops.pop().?;
            // Patch any labeled break statements that targeted this loop's sentinel
            for (self.func().blocks.items[body_block_idx..cont_block_idx]) |*block| {
                for (block.instructions.items) |*instr| {
                    if (instr.op == .br) {
                        if (instr.operands[0] == .block_ref and instr.operands[0].block_ref == labeled_loop.end_block) {
                            instr.operands[0] = .{ .block_ref = cont_block_idx };
                        }
                    }
                }
            }
        }

        // Restore loop context
        self.loop_end_block = saved_end_block;
        self.loop_cond_block = saved_cond_block;
    }

    /// Convert a for-in loop by desugaring to iterator pattern:
    /// for item in collection 'loop'
    ///     body
    /// end 'loop'
    ///
    /// Desugars to:
    /// while true 'loop'
    ///     var __next_result = collection.next()
    ///     if __next_result == nil then break
    ///     var item = unwrap(__next_result)
    ///     body
    /// end 'loop'
    fn convertForStmt(self: *AstToIr, for_stmt: ast.ForStmt) ConvertError!void {
        // Convert the iterable expression ONCE, before the loop
        // This is crucial for iterators like ByteView that have mutable state (_pos)
        const iterable_typed = try self.convertExpression(for_stmt.iterable);

        // For struct iterators, we need to store the iterator in a local variable
        // so that mutations to _pos persist across iterations
        // We use stack allocation since the iterator lives only for the loop duration
        const iterator_slot: ir.Value = switch (iterable_typed.ty) {
            .struct_type => |struct_name| blk: {
                const struct_info = try self.lookupStructInfo(struct_name);
                // Allocate space for the struct on the stack
                const slot = try self.func().emitAllocaSized(@intCast(struct_info.size));
                // Copy the struct data into our slot
                try self.emitStructCopy(slot, iterable_typed.value, @intCast(struct_info.size), struct_name);
                break :blk slot;
            },
            else => iterable_typed.value,
        };

        // Create condition block
        const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forcond");

        // Emit unconditional branch from previous block to condition block
        try self.func().blocks.items[cond_block_idx - 1].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = cond_block_idx }, .none, .none },
            .result = null,
        });

        // In condition block: call next() on the stored iterator
        // The iterator_slot already points to our mutable iterator struct
        const iterator_for_call = TypedValue{ .value = iterator_slot, .ty = iterable_typed.ty };

        // Call the next() method on the iterator
        const next_result = try self.convertMethodCallOnTyped(iterator_for_call, "next", &.{});

        // Load the tag from the optional result (offset 0) to check if nil
        const tag = try self.func().emitLoad(next_result.value, .i64);

        // Compare tag with 0 (nil - no more elements)
        const zero = try self.func().emitConstI64(0);
        const is_nil = try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64);

        // Compute is_not_nil (is_nil == 0) while still in condition block
        const is_not_nil = try self.func().emitBinaryOp(.icmp_eq, is_nil, zero, .i64);

        // Create body block
        const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forbody");

        // Save loop context
        const saved_end_block = self.loop_end_block;
        const saved_cond_block = self.loop_cond_block;
        self.loop_cond_block = cond_block_idx;
        self.loop_end_block = 0xFFFFFFFF; // sentinel for patching

        // Register labeled loop for break/continue with labels
        if (for_stmt.label.len > 0) {
            try self.labeled_loops.append(self.allocator, .{
                .label = for_stmt.label,
                .cond_block = cond_block_idx,
                .end_block = 0xFFFFFFFE - @as(u32, @intCast(self.labeled_loops.items.len)),
            });
        }

        // Save variable names and ownership state before entering loop body
        var pre_loop_vars = std.StringHashMapUnmanaged(OwnershipState){};
        defer pre_loop_vars.deinit(self.allocator);
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            try pre_loop_vars.put(self.allocator, entry.key_ptr.*, entry.value_ptr.state);
        }

        // Extract the value from the optional result (offset 8)
        const opt_info = switch (next_result.ty) {
            .optional_type => |info| info,
            .primitive, .struct_type, .array_type, .enum_type => OptionalInfo{ .wrapped = .i64 },
        };
        const value_offset = try self.func().emitConstI64(8);
        const value_ptr = try self.func().emitBinaryOp(.add, next_result.value, value_offset, .ptr);

        // Register the loop variable
        // For struct types, use the pointer directly (struct data is inline in the optional)
        // For primitive types, load the value and store in a new slot
        const var_slot: ir.Value = if (opt_info.wrapped_struct_type) |_| blk: {
            // For struct types, the payload is the struct data inline
            break :blk value_ptr;
        } else blk: {
            const element_value = try self.func().emitLoad(value_ptr, opt_info.wrapped);
            const slot = try self.func().emitAlloca(opt_info.wrapped);
            try self.func().emitStore(slot, element_value);
            break :blk slot;
        };

        const var_type: ValueType = if (opt_info.wrapped_struct_type) |struct_name|
            .{ .struct_type = struct_name }
        else
            .{ .primitive = opt_info.wrapped.toMaxonName() };

        try self.var_map.put(self.allocator, for_stmt.var_name, VarInfo.init(var_slot, var_type, false, false));

        // Convert body statements
        for (for_stmt.body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Check for variables moved inside loop body
        // Only flag an error if the variable was OWNED before the loop and is now MOVED
        var pre_iter = pre_loop_vars.iterator();
        while (pre_iter.next()) |entry| {
            if (self.var_map.getPtr(entry.key_ptr.*)) |var_info| {
                const was_owned = entry.value_ptr.* == .owned;
                if (was_owned and var_info.state == .moved) {
                    debug.astToIr("variable '{s}' was moved in loop body\n", .{entry.key_ptr.*});
                    self.reportError(.E008, entry.key_ptr.*);
                    return error.UseAfterMove;
                }
            }
        }

        // Free heap-allocated loop-scoped variables
        try self.freeLoopScopedHeapVars(&pre_loop_vars);

        // Emit unconditional branch back to condition block
        if (self.func().currentBlock()) |block| {
            const len = block.instructions.items.len;
            if (len == 0 or (block.instructions.items[len - 1].op != .ret and block.instructions.items[len - 1].op != .br and block.instructions.items[len - 1].op != .br_cond)) {
                try self.func().emitBr(cond_block_idx);
            }
        }

        // Remove loop-scoped variables from var_map
        var to_remove = std.ArrayListUnmanaged([]const u8){};
        defer to_remove.deinit(self.allocator);
        var var_iter = self.var_map.keyIterator();
        while (var_iter.next()) |key| {
            if (!pre_loop_vars.contains(key.*)) {
                try to_remove.append(self.allocator, key.*);
            }
        }
        for (to_remove.items) |key| {
            _ = self.var_map.remove(key);
        }

        // Now create continuation block
        const cont_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forcont");

        // Emit conditional branch in condition block: if is_not_nil, go to body; else fall through to cont
        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_not_nil }, .{ .block_ref = body_block_idx }, .{ .block_ref = cont_block_idx } },
        });

        // Patch any break statements
        for (self.func().blocks.items[body_block_idx..cont_block_idx]) |*block| {
            for (block.instructions.items) |*instr| {
                if (instr.op == .br) {
                    if (instr.operands[0] == .block_ref and instr.operands[0].block_ref == 0xFFFFFFFF) {
                        instr.operands[0] = .{ .block_ref = cont_block_idx };
                    }
                }
            }
        }


        // Pop labeled loop from stack and patch its sentinel if we pushed one
        if (for_stmt.label.len > 0) {
            const labeled_loop = self.labeled_loops.pop().?;
            // Patch any labeled break statements that targeted this loop's sentinel
            for (self.func().blocks.items[body_block_idx..cont_block_idx]) |*block| {
                for (block.instructions.items) |*instr| {
                    if (instr.op == .br) {
                        if (instr.operands[0] == .block_ref and instr.operands[0].block_ref == labeled_loop.end_block) {
                            instr.operands[0] = .{ .block_ref = cont_block_idx };
                        }
                    }
                }
            }
        }

        // Restore loop context
        self.loop_end_block = saved_end_block;
        self.loop_cond_block = saved_cond_block;
    }

    fn convertBreakStmt(self: *AstToIr, brk: ast.BreakStmt) ConvertError!void {
        // Check for labeled break
        if (brk.label) |label| {
            // Search labeled_loops stack from top (innermost) to bottom (outermost)
            var i: usize = self.labeled_loops.items.len;
            while (i > 0) {
                i -= 1;
                if (std.mem.eql(u8, self.labeled_loops.items[i].label, label)) {
                    // Found the target loop - emit branch to its end block
                    // The end_block is 0xFFFFFFFF sentinel which gets patched later
                    try self.func().emitBr(self.labeled_loops.items[i].end_block);
                    return;
                }
            }
            // Label not found
            self.reportError(.E012, label);
            return error.SemanticError;
        }
        // Unlabeled break - use current loop
        if (self.loop_end_block) |end_block| {
            try self.func().emitBr(end_block);
        } else {
            self.reportError(.E012, "break");
            return error.SemanticError;
        }
    }

    fn convertContinueStmt(self: *AstToIr, cont: ast.ContinueStmt) ConvertError!void {
        // Check for labeled continue
        if (cont.label) |label| {
            // Search labeled_loops stack from top (innermost) to bottom (outermost)
            var i: usize = self.labeled_loops.items.len;
            while (i > 0) {
                i -= 1;
                if (std.mem.eql(u8, self.labeled_loops.items[i].label, label)) {
                    // Found the target loop - emit branch to its condition block
                    try self.func().emitBr(self.labeled_loops.items[i].cond_block);
                    return;
                }
            }
            // Label not found
            self.reportError(.E012, label);
            return error.SemanticError;
        }
        // Unlabeled continue - use current loop
        if (self.loop_cond_block) |cond_block| {
            try self.func().emitBr(cond_block);
        } else {
            self.reportError(.E012, "continue");
            return error.SemanticError;
        }
    }

    fn convertElseUnwrapDecl(self: *AstToIr, unwrap: ast.ElseUnwrapDecl) ConvertError!void {
        // Convert the optional expression
        const opt_typed = try self.convertExpression(unwrap.optional_expr.*);

        // Load the tag from the optional (offset 0)
        const tag = try self.func().emitLoad(opt_typed.value, .i64);

        // Compare tag with 1 (has value)
        const one = try self.func().emitConstI64(1);
        const has_value = try self.func().emitBinaryOp(.icmp_eq, tag, one, .i64);

        // Get the wrapped type info
        const opt_info = switch (opt_typed.ty) {
            .optional_type => |info| info,
            .primitive, .struct_type, .array_type, .enum_type => OptionalInfo{ .wrapped = .i64 },
        };
        const wrapped_type = opt_info.wrapped;

        // Create a stack slot for the variable being declared
        // For struct types, allocate enough space for the struct
        const var_ptr = if (opt_info.wrapped_struct_type) |struct_name| blk: {
            const struct_size = if (self.type_map.get(struct_name)) |type_info| s: {
                if (type_info == .struct_type) {
                    break :s type_info.struct_type.size;
                }
                break :s 8;
            } else 8;
            break :blk try self.func().emitAllocaSized(struct_size);
        } else try self.func().emitAlloca(wrapped_type);
        try self.func().setValueName(var_ptr, unwrap.var_name);

        // Add to var_map so the default body can assign to it
        if (opt_info.wrapped_struct_type) |struct_name| {
            try self.var_map.put(self.allocator, unwrap.var_name, VarInfo.init(
                var_ptr,
                .{ .struct_type = struct_name },
                true, // mutable
                true,
            ));
        } else {
            // Primitive binding: use init (no slot indirection needed)
            try self.var_map.put(self.allocator, unwrap.var_name, VarInfo.init(var_ptr, .{ .primitive = wrapped_type.toMaxonName() }, true, // mutable
                false));
        }

        // Create blocks
        const some_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("else_unwrap_some");

        const nil_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("else_unwrap_nil");

        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("else_unwrap_end");

        // Emit conditional branch
        const entry_block = &self.func().blocks.items[some_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = has_value }, .{ .block_ref = some_block_idx }, .{ .block_ref = nil_block_idx } },
        });

        // Save end and nil blocks
        const end_block = self.func().blocks.pop().?;
        const nil_block = self.func().blocks.pop().?;

        // In "some" block: unwrap and store value
        const value_ptr = try self.func().emitGetFieldPtr(opt_typed.value, 8);
        if (opt_info.wrapped_struct_type) |struct_name| {
            // For struct types, copy the struct data from the optional payload to var_ptr
            // Get the struct size
            const struct_size = if (self.type_map.get(struct_name)) |type_info| blk: {
                if (type_info == .struct_type) {
                    break :blk type_info.struct_type.size;
                }
                break :blk 8; // fallback
            } else 8;
            try self.emitStructCopy(var_ptr, value_ptr, struct_size, struct_name);
        } else {
            // For primitive types, load and store
            const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);
            try self.func().emitStore(var_ptr, unwrapped_value);
        }
        try self.func().emitBr(end_block_idx);

        // Restore nil block
        try self.func().blocks.append(self.allocator, nil_block);

        // Execute default body (which should assign to var_name)
        for (unwrap.default_body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Branch to end
        if (self.func().currentBlock()) |block| {
            if (block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret) {
                try self.func().emitBr(end_block_idx);
            }
        }

        // Restore end block
        try self.func().blocks.append(self.allocator, end_block);
    }

    // ------------------------------------------------------------------------
    // Expression Conversion
    // ------------------------------------------------------------------------

    pub fn convertExpression(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        return switch (expr) {
            .integer => |v| .{ .value = try self.func().emitConstI64(v), .ty = .{ .primitive = "int" } },
            .float_lit => |v| .{ .value = try self.func().emitConstF64(v), .ty = .{ .primitive = "float" } },
            .bool_lit => |v| .{ .value = try self.func().emitConstI64(if (v) 1 else 0), .ty = .{ .primitive = "bool" } },
            .nil_lit => self.createNilOptional(.i64),
            .self_expr => self.convertSelfExpr(),
            .identifier => |name| self.convertIdentifierOrField(name),
            .string_literal => |str| self.convertStringLiteral(str),
            .char_literal => |c| self.convertCharLiteral(c),
            .unary => |un| self.convertUnary(un),
            .binary => |bin| self.convertBinary(bin),
            .compare => |cmp| self.convertCompare(cmp),
            .logical => |log| self.convertLogical(log),
            .call => |call| self.convertCall(call),
            .struct_init => |sinit| self.convertStructInit(sinit),
            .field_access => |fa| self.convertFieldAccess(fa),
            .array_literal => |arr| self.convertArrayLiteral(arr),
            .map_literal => |map| self.convertMapLiteral(map),
            .index => |idx| self.convertIndex(idx),
            .array_type => |arr| self.convertArrayType(arr),
            .method_call => |mcall| self.convertMethodCallExpr(mcall),
            .nil_coalesce => |nc| self.convertNilCoalesce(nc),
            .cast => |c| self.convertCast(c),
            .interpolated_string => |interp| self.convertInterpolatedString(interp),
            .closure => |clos| self.convertClosure(clos),
        };
    }

    /// Convert 'self' expression - reference to current instance
    fn convertSelfExpr(self: *AstToIr) ConvertError!TypedValue {
        const self_val = self.self_ptr orelse {
            self.reportError(.E005, "self");
            return error.UndefinedVariable;
        };
        const type_name = self.current_type_name orelse {
            self.reportError(.E005, "self (no type context)");
            return error.UndefinedVariable;
        };
        // Mark self as used
        if (self.var_map.getPtr("self")) |info| {
            info.used = true;
        }
        return .{ .value = self_val, .ty = .{ .struct_type = type_name } };
    }

    /// Convert identifier, with implicit self field resolution for methods
    fn convertIdentifierOrField(self: *AstToIr, name: []const u8) ConvertError!TypedValue {
        // First try to find it as a regular variable
        if (self.var_map.contains(name)) {
            return self.convertIdentifier(name);
        }

        // If inside a method, check if it's a field of the current type (implicit self)
        if (self.self_ptr != null and self.current_type_name != null) {
            const type_name = self.current_type_name.?;
            if (self.type_map.get(type_name)) |type_info| {
                if (type_info == .struct_type) {
                    const struct_info = type_info.struct_type;
                    // Check if name matches a field
                    for (struct_info.fields) |field| {
                        if (std.mem.eql(u8, field.name, name)) {
                            // It's a field - access via self
                            const self_val = self.self_ptr.?;
                            const field_ptr = try self.func().emitGetFieldPtr(self_val, field.offset);

                            // Mark self as used
                            if (self.var_map.getPtr("self")) |info| {
                                info.used = true;
                            }

                            // Struct fields are embedded; others need to be loaded
                            const value = if (field.value_type == .struct_type)
                                field_ptr
                            else
                                try self.func().emitLoad(field_ptr, field.value_type.toPrimitiveType());

                            return .{ .value = value, .ty = field.value_type };
                        }
                    }
                }
            }
        }

        // Check if it's a function name - return function pointer
        if (self.func_map.contains(name)) {
            const func_ptr = try self.func().emitFuncAddr(name);
            return .{
                .value = func_ptr,
                .ty = .{ .primitive = "ptr" },
            };
        }

        // Fall back to original identifier behavior (will produce error for undefined)
        return self.convertIdentifier(name);
    }

    /// Allocate storage for an optional based on its OptionalInfo.
    /// Returns the pointer to the allocated optional.
    fn allocateOptional(self: *AstToIr, opt_info: OptionalInfo) ConvertError!ir.Value {
        const size = self.getOptionalSize(opt_info);
        return self.func().emitAllocaSized(size);
    }

    /// Create a "some" optional from a primitive value
    fn createSomeOptional(self: *AstToIr, value: ir.Value, wrapped_type: ir.Type) ConvertError!TypedValue {
        const opt_info = OptionalInfo{ .wrapped = wrapped_type };
        const opt_ptr = try self.allocateOptional(opt_info);

        // Store tag = 1 (has value)
        const one = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one);

        // Store value at offset 8
        const value_ptr = try self.func().emitGetFieldPtr(opt_ptr, 8);
        try self.func().emitStore(value_ptr, value);

        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = opt_info },
        };
    }

    /// Create a some optional with a struct pointer value (copies struct data inline)
    fn createSomeOptionalWithStructType(self: *AstToIr, struct_ptr: ir.Value, wrapped_type: ir.Type, struct_type: ?[]const u8) ConvertError!TypedValue {
        const opt_info = OptionalInfo{ .wrapped = wrapped_type, .wrapped_struct_type = struct_type };
        const opt_ptr = try self.allocateOptional(opt_info);

        // Store tag = 1 (has value)
        const one = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one);

        // Copy struct data inline at offset 8
        const value_ptr = try self.func().emitGetFieldPtr(opt_ptr, 8);
        const struct_size = self.getOptionalSize(opt_info) - 8; // Total size minus tag
        try self.emitStructCopy(value_ptr, struct_ptr, struct_size, struct_type);

        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = opt_info },
        };
    }

    /// Create a nil optional with a specific OptionalInfo (handles both primitives and structs)
    fn createNilOptionalWithInfo(self: *AstToIr, opt_info: OptionalInfo) ConvertError!TypedValue {
        const opt_ptr = try self.allocateOptional(opt_info);
        const zero = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero); // tag = 0 (nil)
        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = opt_info },
        };
    }

    /// Create a nil optional with a primitive wrapped type (legacy helper)
    fn createNilOptional(self: *AstToIr, wrapped_type: ir.Type) ConvertError!TypedValue {
        return self.createNilOptionalWithInfo(.{ .wrapped = wrapped_type });
    }

    /// Convert string literal expression to a string type
    /// This creates a String struct with the literal data
    fn convertStringLiteral(self: *AstToIr, str_bytes: []const u8) ConvertError!TypedValue {
        // Process escape sequences
        const processed = try self.processEscapeSequences(str_bytes);
        defer self.allocator.free(processed);

        // Check if "String" type exists and conforms to InitableFromStringLiteral
        const type_name = "String";

        // If the String type exists, create a managed string and call init
        if (self.type_map.get(type_name)) |type_info| {
            if (type_info == .struct_type) {
                const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
                try self.module.trackString(init_func_name);

                if (self.func_map.get(init_func_name)) |func_info| {
                    // Trigger lazy generation if needed
                    if (!func_info.ir_generated) {
                        try self.ensureMethodGenerated(init_func_name);
                    }

                    const managed_ptr = try self.emitManagedStringFromBytes(processed);

                    // Allocate result struct and call init
                    const struct_size = type_info.struct_type.size;
                    const result_ptr = try self.func().emitAllocaSized(struct_size);

                    var args = try self.func().allocator.alloc(ir.Value, 2);
                    args[0] = result_ptr;
                    args[1] = managed_ptr;

                    _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

                    // Track this as a temporary String that may need cleanup
                    try self.temporary_strings.append(self.allocator, result_ptr);

                    return .{ .value = result_ptr, .ty = .{ .struct_type = type_name } };
                }
            }
        }

        // Fallback: store string bytes as a pointer to constant data
        // This is used when String type is not available
        const str_ptr = try self.func().emitStringConstant(processed);
        return .{ .value = str_ptr, .ty = .{ .primitive = "string" } };
    }

    /// Emit a string literal directly into a pre-allocated String pointer.
    /// Unlike convertStringLiteral, this does NOT add to temporary_strings tracking.
    /// Used for control-flow constructs where only one path executes at runtime.
    fn emitStringLiteralIntoPtr(self: *AstToIr, dest_ptr: ir.Value, str_bytes: []const u8) ConvertError!void {
        // Process escape sequences
        const processed = try self.processEscapeSequences(str_bytes);
        defer self.allocator.free(processed);

        // Create managed string from bytes
        const managed_ptr = try self.emitManagedStringFromBytes(processed);

        // Call String$init to initialize the destination pointer
        const init_func_name = "String$init";
        if (self.func_map.get(init_func_name)) |func_info| {
            // Trigger lazy generation if needed
            if (!func_info.ir_generated) {
                try self.ensureMethodGenerated(init_func_name);
            }

            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = dest_ptr;
            args[1] = managed_ptr;
            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);
        }
    }

    /// Convert an interpolated string expression to a String
    /// Processes each part (literal or expression), converts to strings, and concatenates
    fn convertInterpolatedString(self: *AstToIr, interp: ast.InterpolatedStringExpr) ConvertError!TypedValue {
        if (interp.parts.len == 0) {
            // Empty interpolated string - just return empty string
            return self.convertStringLiteral("");
        }

        // Convert each part to a String value (pointer to String struct)
        var string_parts: std.ArrayListUnmanaged(ir.Value) = .empty;
        defer string_parts.deinit(self.allocator);

        for (interp.parts) |part| {
            if (part.is_expression) {
                // Expression part - evaluate and convert to string
                const expr = part.expr orelse continue;
                const typed_val = try self.convertExpression(expr.*);

                // Convert the value to a String based on its type
                const str_val = try self.convertToString(typed_val, part.format_spec);
                try string_parts.append(self.allocator, str_val);
            } else {
                // Literal part - process escape sequences and convert to String
                const literal = part.literal_value orelse continue;
                const processed = try self.processEscapeSequences(literal);
                defer self.allocator.free(processed);

                const str_typed = try self.convertStringLiteral(processed);
                try string_parts.append(self.allocator, str_typed.value);
            }
        }

        if (string_parts.items.len == 0) {
            return self.convertStringLiteral("");
        }

        if (string_parts.items.len == 1) {
            return .{ .value = string_parts.items[0], .ty = .{ .struct_type = "String" } };
        }

        // Concatenate all parts: result = concat(concat(a, b), c)...
        var result = string_parts.items[0];
        for (string_parts.items[1..]) |next_val| {
            result = try self.emitStringConcatCall(result, next_val);
        }

        return .{ .value = result, .ty = .{ .struct_type = "String" } };
    }

    /// Convert a TypedValue to a String pointer
    /// Handles different types: String (passthrough), int, float, bool
    fn convertToString(self: *AstToIr, typed_val: TypedValue, format_spec: ?[]const u8) ConvertError!ir.Value {
        switch (typed_val.ty) {
            .struct_type => |type_name| {
                if (std.mem.eql(u8, type_name, "String")) {
                    // Already a String, just return it
                    return typed_val.value;
                }
                if (std.mem.eql(u8, type_name, "Character")) {
                    // Convert Character to String by copying its _managed field
                    return self.convertCharacterToString(typed_val.value);
                }
                // Check for Stringable interface conformance
                if (self.typeConformsTo(type_name, "Stringable")) {
                    return self.callStringableToString(type_name, typed_val.value, format_spec);
                }
                const msg = std.fmt.allocPrint(self.allocator, "cannot convert type '{s}' to string for interpolation", .{type_name}) catch "cannot convert type to string";
                self.reportInternalError(msg);
                return error.UnknownType;
            },
            .primitive => |prim| {
                // Check bool first since it also has ir_type == .i64
                if (std.mem.eql(u8, prim, "bool")) {
                    return self.convertBoolToString(typed_val.value);
                } else if (std.mem.eql(u8, prim, "int") or types.nameToIrType(prim) == .i64) {
                    return self.convertIntToString(typed_val.value);
                } else if (std.mem.eql(u8, prim, "float") or types.nameToIrType(prim) == .f64) {
                    return self.convertFloatToString(typed_val.value);
                }
                const msg = std.fmt.allocPrint(self.allocator, "cannot convert primitive type '{s}' to string for interpolation", .{prim}) catch "cannot convert primitive to string";
                self.reportInternalError(msg);
                return error.UnknownType;
            },
            .enum_type => |enum_type_name| {
                // Look up the enum type info
                if (self.type_map.get(enum_type_name)) |type_info| {
                    if (type_info.enum_type.backing_type == .string) {
                        // String-backed enum: generate switch on ordinal to select raw string value
                        return self.convertStringEnumToString(typed_val.value, type_info.enum_type);
                    }
                }
                // Int-backed or unknown enum: convert ordinal to string
                return self.convertIntToString(typed_val.value);
            },
            .optional_type => |opt_info| {
                // For optional types, unwrap and convert the inner value
                // Load the value slot (offset 8 from optional pointer)
                const value_slot = try self.func().emitGetFieldPtr(typed_val.value, 8);
                const inner_value = try self.func().emitLoad(value_slot, opt_info.wrapped);
                const inner_typed = TypedValue{
                    .value = inner_value,
                    .ty = if (opt_info.wrapped_struct_type) |struct_name|
                        .{ .struct_type = struct_name }
                    else
                        .{ .primitive = opt_info.wrapped.toMaxonName() },
                };
                return self.convertToString(inner_typed, format_spec);
            },
            .array_type => {
                self.reportInternalError("cannot convert array to string for interpolation");
                return error.UnknownType;
            },
        }
    }

    /// Convert an int to a String
    fn convertIntToString(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        return self.emitIntToStringCall(value);
    }

    /// Convert a float to a String
    fn convertFloatToString(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        return self.emitFloatToStringCall(value);
    }

    /// Convert a bool to a String
    fn convertBoolToString(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        return self.emitBoolToStringCall(value);
    }

    /// Convert a string-backed enum to its string value
    /// Generates a switch-like cascade on the ordinal to select the raw string value
    fn convertStringEnumToString(self: *AstToIr, ordinal_value: ir.Value, enum_info: types.EnumTypeInfo) ConvertError!ir.Value {
        // Collect string values into a sorted list by ordinal
        const num_members = enum_info.string_values.count();
        if (num_members == 0) {
            // No string values, fall back to int conversion
            return self.convertIntToString(ordinal_value);
        }

        // Collect entries sorted by ordinal
        const EnumEntry = struct { ordinal: i64, string_val: []const u8 };
        var entries = try self.allocator.alloc(EnumEntry, num_members);
        defer self.allocator.free(entries);

        var iter = enum_info.string_values.iterator();
        var idx: usize = 0;
        while (iter.next()) |entry| {
            entries[idx] = .{ .ordinal = entry.key_ptr.*, .string_val = entry.value_ptr.* };
            idx += 1;
        }

        // Sort by ordinal for consistent block order
        std.mem.sort(EnumEntry, entries, {}, struct {
            fn lessThan(_: void, a: EnumEntry, b: EnumEntry) bool {
                return a.ordinal < b.ordinal;
            }
        }.lessThan);

        // Allocate a result pointer for the String (32 bytes) in entry block
        const result_ptr = try self.func().emitAllocaSized(32);

        // For single-entry enum, just create the string directly
        if (num_members == 1) {
            try self.emitStringLiteralIntoPtr(result_ptr, entries[0].string_val);
            return result_ptr;
        }

        // Track blocks that need branches to end block (we'll patch them later)
        var case_exit_blocks = try self.allocator.alloc(u32, num_members);
        defer self.allocator.free(case_exit_blocks);

        // For N entries with N > 1:
        // - Entry 0: compare ordinal==0, if true goto case0, else goto cmp1
        // - Entry 1: compare ordinal==1, if true goto case1, else goto cmp2 (or case_last if N-2)
        // - ...
        // - Entry N-1: fallback case (no comparison needed)

        // Process entry 0 in current (entry) block
        {
            const ord_const = try self.func().emitConstI64(entries[0].ordinal);
            const cmp = try self.func().emitBinaryOp(.icmp_eq, ordinal_value, ord_const, .i64);

            // Create case0 block
            const case0_block_idx: u32 = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("string_enum_case0");

            // We need to emit branch from entry to case0 or next_cmp
            // But we don't know next_cmp index yet - need to emit branch after creating it

            // For 2-entry enum, else goes directly to case1 (fallback)
            // For 3+ entry enum, else goes to cmp1 block
            if (num_members == 2) {
                // Create case1 block (fallback)
                const case1_block_idx: u32 = @intCast(self.func().blocks.items.len);
                _ = try self.func().addBlock("string_enum_case1");

                // Create end block
                const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
                _ = try self.func().addBlock("string_enum_end");

                // Pop end and case1, keep case0 as current
                const end_block = self.func().blocks.pop().?;
                const case1_block = self.func().blocks.pop().?;

                // Emit branch from entry (which is blocks[case0_idx - 1])
                try self.func().blocks.items[case0_block_idx - 1].instructions.append(self.allocator, .{
                    .op = .br_cond,
                    .operands = .{ .{ .value = cmp }, .{ .block_ref = case0_block_idx }, .{ .block_ref = case1_block_idx } },
                    .result = null,
                });

                // Now in case0 block - emit string and branch to end
                try self.emitStringLiteralIntoPtr(result_ptr, entries[0].string_val);
                try self.func().emitBr(end_block_idx);

                // Restore case1 block and emit string
                try self.func().blocks.append(self.allocator, case1_block);
                try self.emitStringLiteralIntoPtr(result_ptr, entries[1].string_val);
                try self.func().emitBr(end_block_idx);

                // Restore end block
                try self.func().blocks.append(self.allocator, end_block);

                return result_ptr;
            }

            // For 3+ entries, we need multiple comparison blocks
            // Create cmp1 block
            const cmp1_block_idx: u32 = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("string_enum_cmp1");

            // Pop cmp1 to emit branch from entry
            const cmp1_block = self.func().blocks.pop().?;

            // Emit branch from entry
            try self.func().blocks.items[case0_block_idx - 1].instructions.append(self.allocator, .{
                .op = .br_cond,
                .operands = .{ .{ .value = cmp }, .{ .block_ref = case0_block_idx }, .{ .block_ref = cmp1_block_idx } },
                .result = null,
            });

            // Now in case0 block - emit string
            try self.emitStringLiteralIntoPtr(result_ptr, entries[0].string_val);
            case_exit_blocks[0] = @intCast(self.func().blocks.items.len - 1);
            // Don't emit branch yet - we need end block index

            // Restore cmp1 block
            try self.func().blocks.append(self.allocator, cmp1_block);
        }

        // Process entries 1 through N-2 (each gets a comparison block)
        for (1..num_members - 1) |i| {
            // Current block is cmp[i] or we just restored it
            const ord_const = try self.func().emitConstI64(entries[i].ordinal);
            const cmp = try self.func().emitBinaryOp(.icmp_eq, ordinal_value, ord_const, .i64);

            // Create case[i] block
            const case_block_idx: u32 = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("string_enum_case");

            // Determine else target
            if (i == num_members - 2) {
                // Last comparison - else goes to case[last] (fallback)
                const case_last_block_idx: u32 = @intCast(self.func().blocks.items.len);
                _ = try self.func().addBlock("string_enum_case_last");

                // Create end block
                const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
                _ = try self.func().addBlock("string_enum_end");

                // Pop end and case_last
                const end_block = self.func().blocks.pop().?;
                const case_last_block = self.func().blocks.pop().?;

                // Emit branch from cmp[i] (which is blocks[case_block_idx - 1])
                try self.func().blocks.items[case_block_idx - 1].instructions.append(self.allocator, .{
                    .op = .br_cond,
                    .operands = .{ .{ .value = cmp }, .{ .block_ref = case_block_idx }, .{ .block_ref = case_last_block_idx } },
                    .result = null,
                });

                // Now in case[i] block - emit string
                try self.emitStringLiteralIntoPtr(result_ptr, entries[i].string_val);
                try self.func().emitBr(end_block_idx);

                // Restore case_last and emit string
                try self.func().blocks.append(self.allocator, case_last_block);
                try self.emitStringLiteralIntoPtr(result_ptr, entries[num_members - 1].string_val);
                try self.func().emitBr(end_block_idx);

                // Now patch the earlier case blocks to branch to end
                for (0..i) |j| {
                    try self.func().blocks.items[case_exit_blocks[j]].instructions.append(self.allocator, .{
                        .op = .br,
                        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
                        .result = null,
                    });
                }

                // Restore end block
                try self.func().blocks.append(self.allocator, end_block);

                return result_ptr;
            } else {
                // Not last comparison - else goes to next cmp block
                const next_cmp_block_idx: u32 = @intCast(self.func().blocks.items.len);
                _ = try self.func().addBlock("string_enum_cmp");

                // Pop next_cmp
                const next_cmp_block = self.func().blocks.pop().?;

                // Emit branch from current cmp block
                try self.func().blocks.items[case_block_idx - 1].instructions.append(self.allocator, .{
                    .op = .br_cond,
                    .operands = .{ .{ .value = cmp }, .{ .block_ref = case_block_idx }, .{ .block_ref = next_cmp_block_idx } },
                    .result = null,
                });

                // Now in case[i] block - emit string
                try self.emitStringLiteralIntoPtr(result_ptr, entries[i].string_val);
                case_exit_blocks[i] = @intCast(self.func().blocks.items.len - 1);
                // Don't emit branch yet

                // Restore next_cmp block
                try self.func().blocks.append(self.allocator, next_cmp_block);
            }
        }

        // Should not reach here - the loop handles the last comparison
        unreachable;
    }

    /// Call Stringable.toString() on a type that conforms to Stringable
    /// Returns a pointer to the resulting String
    fn callStringableToString(self: *AstToIr, type_name: []const u8, value_ptr: ir.Value, format_spec: ?[]const u8) ConvertError!ir.Value {
        // Build the method name: TypeName$Stringable.toString
        const method_name = try std.fmt.allocPrint(self.allocator, "{s}$Stringable.toString", .{type_name});
        try self.module.trackString(method_name);

        const func_info = self.func_map.get(method_name) orelse {
            self.reportError(.E005, method_name);
            return error.UndefinedVariable;
        };

        // Allocate space for the return value (String is 32 bytes)
        const result_ptr = try self.func().emitAllocaSized(32);

        // Prepare the format argument (optional String)
        const format_opt_info = OptionalInfo{ .wrapped = .ptr, .wrapped_struct_type = "String" };
        if (format_spec) |spec| {
            const format_str = try self.convertStringLiteral(spec);
            const format_typed = try self.createSomeOptionalWithStructType(format_str.value, .ptr, "String");
            // Use the allocated optional directly
            var args = try self.func().allocator.alloc(ir.Value, 3);
            args[0] = result_ptr;
            args[1] = value_ptr;
            args[2] = format_typed.value;
            _ = try self.func().emitCall(method_name, args, func_info.return_type);
            return result_ptr;
        }
        // nil case
        const format_opt = try self.allocateOptional(format_opt_info);
        const zero = try self.func().emitConstI64(0);
        try self.func().emitStore(format_opt, zero);

        // Call toString(self, format) -> result_ptr contains the String
        var args = try self.func().allocator.alloc(ir.Value, 3);
        args[0] = result_ptr; // Return slot
        args[1] = value_ptr; // self
        args[2] = format_opt; // format argument

        _ = try self.func().emitCall(method_name, args, func_info.return_type);

        return result_ptr;
    }

    /// Convert a Character to a String
    /// Character layout: { _managed: __ManagedString } (24 bytes)
    /// String layout: { _managed: __ManagedString, _iterPos: int } (32 bytes)
    fn convertCharacterToString(self: *AstToIr, char_ptr: ir.Value) ConvertError!ir.Value {
        // Allocate a String struct (32 bytes)
        const string_ptr = try self.func().emitAllocaSized(32);

        // Copy the _managed field from Character to String (24 bytes at offset 0)
        const char_managed = try self.func().emitGetFieldPtr(char_ptr, 0);
        const string_managed = try self.func().emitGetFieldPtr(string_ptr, 0);
        try self.emitStructCopy(string_managed, char_managed, 24, "__ManagedString");

        // Set _iterPos to 0 (at offset 24)
        const iter_pos_ptr = try self.func().emitGetFieldPtr(string_ptr, 24);
        const zero = try self.func().emitConstI64(0);
        try self.func().emitStore(iter_pos_ptr, zero);

        return string_ptr;
    }

    /// Emit a call to concatenate two String values
    fn emitStringConcatCall(self: *AstToIr, a: ir.Value, b: ir.Value) ConvertError!ir.Value {
        // Get the _managed field from both String structs (offset 0)
        const a_managed = try self.func().emitGetFieldPtr(a, 0);
        const b_managed = try self.func().emitGetFieldPtr(b, 0);

        // Get lengths (stored as i32 at offset 8)
        const a_len_ptr = try self.func().emitGetFieldPtr(a_managed, 8);
        const a_len_i32 = try self.func().emitLoad(a_len_ptr, .i32);
        const b_len_ptr = try self.func().emitGetFieldPtr(b_managed, 8);
        const b_len_i32 = try self.func().emitLoad(b_len_ptr, .i32);

        // Sign-extend lengths to i64 for arithmetic
        const a_len = try self.func().emitUnaryOp(.sext_i32_i64, a_len_i32, .i64);
        const b_len = try self.func().emitUnaryOp(.sext_i32_i64, b_len_i32, .i64);
        const total_len = try self.func().emitBinaryOp(.add, a_len, b_len, .i64);

        // Allocate new buffer: total_len + 1 for null terminator
        const one = try self.func().emitConstI64(1);
        const buffer_size = try self.func().emitBinaryOp(.add, total_len, one, .i64);
        const new_buffer = try self.func().emitHeapAlloc(buffer_size);

        // Copy first string
        const a_buf_ptr = try self.func().emitGetFieldPtr(a_managed, 0);
        const a_buf = try self.func().emitLoad(a_buf_ptr, .ptr);
        try self.func().emitMemcpyDynamic(new_buffer, a_buf, a_len);

        // Copy second string at offset a_len
        const b_buf_ptr = try self.func().emitGetFieldPtr(b_managed, 0);
        const b_buf = try self.func().emitLoad(b_buf_ptr, .ptr);
        const offset_ptr = try self.func().emitGetElemPtr(new_buffer, a_len, 1);
        try self.func().emitMemcpyDynamic(offset_ptr, b_buf, b_len);

        // Null terminate
        const null_offset = try self.func().emitGetElemPtr(new_buffer, total_len, 1);
        try self.func().emitStoreI8(null_offset, try self.func().emitConstI8(0));

        const result_managed = try self.emitManagedStringFromBuffer(new_buffer, total_len);
        const string_ptr = try self.emitStringFromManaged(result_managed);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr);
        return string_ptr;
    }

    /// Emit code to convert an int to a String
    fn emitIntToStringCall(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        // Allocate buffer on heap (22 bytes max: sign + 20 digits + null)
        const buffer_size = try self.func().emitConstI64(22);
        const buffer = try self.func().emitHeapAlloc(buffer_size);

        // Call __runtime_int_to_string(buffer, value) -> returns length
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = buffer;
        args[1] = value;
        const len = (try self.func().emitCall("__runtime_int_to_string", args, .i64)).?;

        const managed_ptr = try self.emitManagedStringFromBuffer(buffer, len);
        const string_ptr = try self.emitStringFromManaged(managed_ptr);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr);
        return string_ptr;
    }

    /// Emit code to convert a float to a String
    fn emitFloatToStringCall(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        // Allocate buffer (32 bytes should be enough for most floats)
        const buffer_size = try self.func().emitConstI64(32);
        const buffer = try self.func().emitHeapAlloc(buffer_size);

        // Call __runtime_float_to_string(buffer, value) -> returns length
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = buffer;
        args[1] = value;
        const len = (try self.func().emitCall("__runtime_float_to_string", args, .i64)).?;

        const managed_ptr = try self.emitManagedStringFromBuffer(buffer, len);
        const string_ptr = try self.emitStringFromManaged(managed_ptr);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr);
        return string_ptr;
    }

    /// Emit code to convert a bool to a String
    fn emitBoolToStringCall(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        // Create a buffer for the result (6 bytes max: "false\0")
        const buffer_size = try self.func().emitConstI64(6);
        const buffer = try self.func().emitHeapAlloc(buffer_size);

        // Call __runtime_bool_to_string(buffer, value) -> returns length
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = buffer;
        args[1] = value;
        const len = (try self.func().emitCall("__runtime_bool_to_string", args, .i64)).?;

        const managed_ptr = try self.emitManagedStringFromBuffer(buffer, len);
        const string_ptr = try self.emitStringFromManaged(managed_ptr);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr);
        return string_ptr;
    }

    /// Process escape sequences in a string literal
    /// Converts \n, \t, \r, \\, \", \{, \} to their actual characters
    fn processEscapeSequences(self: *AstToIr, raw: []const u8) ![]u8 {
        var result: std.ArrayListUnmanaged(u8) = .empty;
        errdefer result.deinit(self.allocator);

        var i: usize = 0;
        while (i < raw.len) {
            if (raw[i] == '\\' and i + 1 < raw.len) {
                const next = raw[i + 1];
                switch (next) {
                    'n' => try result.append(self.allocator, '\n'),
                    't' => try result.append(self.allocator, '\t'),
                    'r' => try result.append(self.allocator, '\r'),
                    '\\' => try result.append(self.allocator, '\\'),
                    '\'' => try result.append(self.allocator, '\''),
                    '"' => try result.append(self.allocator, '"'),
                    '{' => try result.append(self.allocator, '{'),
                    '}' => try result.append(self.allocator, '}'),
                    '0' => try result.append(self.allocator, 0),
                    else => {
                        // Unknown escape, keep both characters
                        try result.append(self.allocator, '\\');
                        try result.append(self.allocator, next);
                    },
                }
                i += 2;
            } else {
                try result.append(self.allocator, raw[i]);
                i += 1;
            }
        }

        return result.toOwnedSlice(self.allocator);
    }

    /// Convert character literal expression to a character type
    /// This creates a Character struct with the literal data
    fn convertCharLiteral(self: *AstToIr, char_bytes: []const u8) ConvertError!TypedValue {
        const type_name = "Character";

        // Process escape sequences in the character literal
        const processed_bytes = try self.processEscapeSequences(char_bytes);

        // If the Character type exists, create a managed string and call init
        if (self.type_map.get(type_name)) |type_info| {
            if (type_info == .struct_type) {
                const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
                try self.module.trackString(init_func_name);

                if (self.func_map.get(init_func_name)) |func_info| {
                    // Trigger lazy generation if needed
                    if (!func_info.ir_generated) {
                        try self.ensureMethodGenerated(init_func_name);
                    }

                    const managed_ptr = try self.emitManagedStringFromBytes(processed_bytes);

                    // Allocate result struct and call init
                    const struct_size = type_info.struct_type.size;
                    const result_ptr = try self.func().emitAllocaSized(struct_size);

                    var args = try self.func().allocator.alloc(ir.Value, 2);
                    args[0] = result_ptr;
                    args[1] = managed_ptr;

                    _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

                    return .{ .value = result_ptr, .ty = .{ .struct_type = type_name } };
                }
            }
        }

        // Fallback: store char bytes as constant data pointer
        const char_ptr = try self.func().emitStringConstant(processed_bytes);
        return .{ .value = char_ptr, .ty = .{ .primitive = "Character" } };
    }

    fn convertIdentifier(self: *AstToIr, name: []const u8) ConvertError!TypedValue {
        const info = self.var_map.getPtr(name) orelse {
            self.reportError(.E005, name);
            return error.UndefinedVariable;
        };

        if (info.state == .moved) {
            debug.astToIr("variable '{s}' was moved\n", .{name});
            self.reportError(.E008, name);
            return error.UseAfterMove;
        }

        info.used = true;

        // Reference types may use a slot indirection; value types always load
        const value = if (info.ty == .struct_type or info.ty == .array_type or info.ty == .optional_type)
            if (info.uses_slot) try self.func().emitLoad(info.ptr, .ptr) else info.ptr
        else
            try self.func().emitLoad(info.ptr, info.ty.toPrimitiveType());

        return .{ .value = value, .ty = info.ty };
    }

    fn convertUnary(self: *AstToIr, un: ast.UnaryExpr) ConvertError!TypedValue {
        const operand = try self.convertExpression(un.operand.*);
        const is_float = operand.ty.toPrimitiveType() == .f64;

        switch (un.op) {
            .negate => {
                // Negate: -x
                const zero = if (is_float) try self.func().emitConstF64(0.0) else try self.func().emitConstI64(0);
                const op: ir.Instruction.Op = if (is_float) .fsub else .sub;
                const ty: ir.Type = if (is_float) .f64 else .i64;
                const result = try self.func().emitBinaryOp(op, zero, operand.value, ty);
                // Preserve the operand's type name for negate
                const type_name = if (operand.ty == .primitive) operand.ty.primitive else if (is_float) "float" else "int";
                return .{ .value = result, .ty = .{ .primitive = type_name } };
            },
            .not => {
                // Logical not: x == 0
                const zero = try self.func().emitConstI64(0);
                const result = try self.func().emitBinaryOp(.icmp_eq, operand.value, zero, .i64);
                return .{ .value = result, .ty = .{ .primitive = "bool" } };
            },
        }
    }

    /// Convert type cast expression (x as Type)
    fn convertCast(self: *AstToIr, cast: ast.CastExpr) ConvertError!TypedValue {
        const source = try self.convertExpression(cast.expr.*);
        const source_type = source.ty.toPrimitiveType();
        const target_type_name = cast.target_type;

        // Determine target IR type from type name
        const target_ir_type: ir.Type = if (std.mem.eql(u8, target_type_name, "int"))
            .i64
        else if (std.mem.eql(u8, target_type_name, "byte"))
            .i64 // byte is stored as i64 in IR, just truncated
        else if (std.mem.eql(u8, target_type_name, "float"))
            .f64
        else if (std.mem.eql(u8, target_type_name, "bool"))
            .i64 // bool is stored as i64
        else {
            // Unknown cast target type
            debug.astToIr("Unknown cast target type: {s}\n", .{target_type_name});
            self.reportError(.E006, target_type_name);
            return error.TypeMismatch;
        };

        // Perform the actual conversion
        const result = blk: {
            // Same type - no-op
            if (source_type == target_ir_type and !std.mem.eql(u8, target_type_name, "byte")) {
                break :blk source.value;
            }

            // Float to int: fptosi
            if (source_type == .f64 and target_ir_type == .i64) {
                break :blk try self.func().emitUnaryOp(.fptosi, source.value, .i64);
            }

            // Int to float: sitofp
            if (source_type == .i64 and target_ir_type == .f64) {
                break :blk try self.func().emitUnaryOp(.sitofp, source.value, .f64);
            }

            // Byte cast (truncate to 8 bits by masking with 0xFF)
            if (std.mem.eql(u8, target_type_name, "byte")) {
                // If source is float, convert to int first
                const int_val = if (source_type == .f64)
                    try self.func().emitUnaryOp(.fptosi, source.value, .i64)
                else
                    source.value;
                // Mask with 0xFF to truncate to byte
                const mask = try self.func().emitConstI64(0xFF);
                break :blk try self.func().emitBinaryOp(.band, int_val, mask, .i64);
            }

            // Default: same IR type, just use the value
            break :blk source.value;
        };

        return .{
            .value = result,
            .ty = .{ .primitive = target_type_name },
        };
    }

    /// Convert a closure expression to a function pointer
    /// Closures are compiled to anonymous functions
    fn convertClosure(self: *AstToIr, clos: ast.ClosureExpr) ConvertError!TypedValue {
        // Generate a unique name for the anonymous function
        const anon_name = try std.fmt.allocPrint(self.allocator, "__anon_closure_{d}", .{self.anon_closure_counter});
        self.anon_closure_counter += 1;
        try self.module.trackString(anon_name);

        // Build parameter list for the synthesized function
        var param_ir_types = try self.allocator.alloc(ir.Type, clos.params.len);
        var func_param_types = try self.allocator.alloc(ParamType, clos.params.len);
        for (clos.params, 0..) |param, i| {
            param_ir_types[i] = types.nameToIrType(param.type_name);
            func_param_types[i] = .{ .ty = .{ .primitive = param.type_name } };
        }

        // Determine return type from the body expression type
        // For now, assume it matches the first param type (works for map transforms)
        const return_type: ir.Type = if (clos.params.len > 0) param_ir_types[0] else .i64;

        // Save current function context - MUST capture index BEFORE addFunction which may reallocate
        const saved_func_idx: ?usize = if (self.current_func) |curr| blk: {
            for (self.module.functions.items, 0..) |*f, i| {
                if (f == curr) break :blk i;
            }
            break :blk null;
        } else null;
        const saved_var_map = self.var_map;

        // Create the function in the module (may reallocate functions array)
        const func_ir = try self.module.addFunction(anon_name, return_type);
        _ = try func_ir.addBlock("entry");
        self.current_func = func_ir;
        self.var_map = .empty;

        // Add parameters to variable map
        for (clos.params, 0..) |param, i| {
            // Parameters are passed by value in registers/stack - allocate local storage
            const param_ptr = try func_ir.emitAllocaSized(8);
            const param_val = try func_ir.emitParam(@intCast(i), param_ir_types[i]);
            try func_ir.emitStore(param_ptr, param_val);
            try self.var_map.put(self.allocator, param.name, VarInfo.initParam(
                param_ptr,
                .{ .primitive = param.type_name },
                false, // immutable
                false, // doesn't use slot
            ));
        }

        // Convert the body expression
        const body_result = try self.convertExpression(clos.body.*);

        // Emit return
        try func_ir.emitRet(body_result.value);

        // Restore context - recompute pointer from saved index (array may have been reallocated)
        self.current_func = if (saved_func_idx) |idx| &self.module.functions.items[idx] else null;
        self.var_map.deinit(self.allocator);
        self.var_map = saved_var_map;

        // Register this function in the function map
        try self.func_map.put(self.allocator, anon_name, .{
            .return_type = return_type,
            .return_type_name = null,
            .return_value_type = null,
            .param_types = func_param_types,
        });

        // Free the IR types array since it's no longer needed
        self.allocator.free(param_ir_types);

        // Return the function pointer - emit func.addr to get address of the closure function
        const func_ptr = try self.func().emitFuncAddr(anon_name);
        return .{
            .value = func_ptr,
            .ty = .{ .primitive = "ptr" },
        };
    }

    fn convertBinary(self: *AstToIr, bin: ast.BinaryExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(bin.left.*);
        const right = try self.convertExpression(bin.right.*);

        const left_prim = left.ty.toPrimitiveType();
        const right_prim = right.ty.toPrimitiveType();

        // Division always returns float, other ops depend on operand types
        const is_division = bin.op == .div;
        const result_ty: ir.Type = if (is_division or left_prim == .f64 or right_prim == .f64) .f64 else .i64;

        // Promote operands if needed
        const left_val = if (result_ty == .f64 and left_prim == .i64)
            try self.func().emitUnaryOp(.sitofp, left.value, .f64)
        else
            left.value;
        const right_val = if (result_ty == .f64 and right_prim == .i64)
            try self.func().emitUnaryOp(.sitofp, right.value, .f64)
        else
            right.value;

        const result = if (result_ty == .f64)
            try self.emitFloatOp(bin.op, left_val, right_val)
        else
            try self.emitIntOp(bin.op, left_val, right_val);

        // Determine the result type name based on IR type
        const type_name = if (result_ty == .f64) "float" else "int";
        return .{ .value = result, .ty = .{ .primitive = type_name } };
    }

    fn emitFloatOp(self: *AstToIr, op: ast.BinaryOp, left: ir.Value, right: ir.Value) ConvertError!ir.Value {
        return switch (op) {
            .add => self.func().emitBinaryOp(.fadd, left, right, .f64),
            .sub => self.func().emitBinaryOp(.fsub, left, right, .f64),
            .mul => self.func().emitBinaryOp(.fmul, left, right, .f64),
            .div => self.func().emitBinaryOp(.fdiv, left, right, .f64),
            .mod, .band, .bitor, .bxor, .shl, .shr => error.FloatModNotSupported,
        };
    }

    fn emitIntOp(self: *AstToIr, op: ast.BinaryOp, left: ir.Value, right: ir.Value) ConvertError!ir.Value {
        return switch (op) {
            .add => self.func().emitBinaryOp(.add, left, right, .i64),
            .sub => self.func().emitBinaryOp(.sub, left, right, .i64),
            .mul => self.func().emitBinaryOp(.mul, left, right, .i64),
            .div => self.func().emitBinaryOp(.div, left, right, .i64),
            .mod => self.func().emitBinaryOp(.mod, left, right, .i64),
            .band => self.func().emitBinaryOp(.band, left, right, .i64),
            .bitor => self.func().emitBinaryOp(.bitor, left, right, .i64),
            .bxor => self.func().emitBinaryOp(.bxor, left, right, .i64),
            .shl => self.func().emitBinaryOp(.shl, left, right, .i64),
            .shr => self.func().emitBinaryOp(.shr, left, right, .i64),
        };
    }

    fn convertCompare(self: *AstToIr, cmp: ast.CompareExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(cmp.left.*);
        const right = try self.convertExpression(cmp.right.*);

        // Handle optional == nil or optional != nil comparisons
        // An optional's tag is at offset 0: 0 means nil, 1 means has value
        const left_is_optional = left.ty == .optional_type;
        const right_is_optional = right.ty == .optional_type;

        if (left_is_optional and right_is_optional) {
            // Both are optional - comparing optional to nil (or nil to optional)
            // Compare the tag field to 0
            const opt_value = left.value;
            const tag = try self.func().emitLoad(opt_value, .i64);
            const zero = try self.func().emitConstI64(0);

            const result = switch (cmp.op) {
                .eq => try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64),
                .ne => try self.func().emitBinaryOp(.icmp_ne, tag, zero, .i64),
                .lt, .le, .gt, .ge => {
                    // < > <= >= don't make sense for optional/nil comparison
                    self.reportError(.E003, "comparison operators < > <= >= not valid for optional/nil");
                    return error.TypeMismatch;
                },
            };
            return .{ .value = result, .ty = .{ .primitive = "bool" } };
        } else if (left_is_optional) {
            // Left is optional, right is a value - unwrap left and compare
            // Load the value from offset 8 of the optional
            const value_ptr = try self.func().emitGetFieldPtr(left.value, 8);
            const left_val = try self.func().emitLoad(value_ptr, left.ty.optional_type.wrapped);

            const op: ir.Instruction.Op = switch (cmp.op) {
                .eq => .icmp_eq,
                .ne => .icmp_ne,
                .lt => .icmp_lt,
                .le => .icmp_le,
                .gt => .icmp_gt,
                .ge => .icmp_ge,
            };
            const result = try self.func().emitBinaryOp(op, left_val, right.value, .i64);
            return .{ .value = result, .ty = .{ .primitive = "bool" } };
        } else if (right_is_optional) {
            // Right is optional, left is a value - unwrap right and compare
            // Load the value from offset 8 of the optional
            const value_ptr = try self.func().emitGetFieldPtr(right.value, 8);
            const right_val = try self.func().emitLoad(value_ptr, right.ty.optional_type.wrapped);

            const op: ir.Instruction.Op = switch (cmp.op) {
                .eq => .icmp_eq,
                .ne => .icmp_ne,
                .lt => .icmp_lt,
                .le => .icmp_le,
                .gt => .icmp_gt,
                .ge => .icmp_ge,
            };
            const result = try self.func().emitBinaryOp(op, left.value, right_val, .i64);
            return .{ .value = result, .ty = .{ .primitive = "bool" } };
        }

        // Check if left operand is a struct type that implements Equatable
        // If so, use the equals() method for == and != operators
        if (left.ty == .struct_type) {
            const type_name = left.ty.struct_type;
            if (self.typeConformsTo(type_name, "Equatable")) {
                // Only handle == and != for Equatable
                if (cmp.op == .eq or cmp.op == .ne) {
                    // Call the equals() method: left.equals(right)
                    const equals_result = try self.emitMethodCallWithIrArgs(type_name, "equals", &.{right.value}, left.value);

                    // For ==, return the result directly; for !=, negate it
                    if (cmp.op == .eq) {
                        return .{ .value = equals_result.value, .ty = .{ .primitive = "bool" } };
                    } else {
                        // != is the negation of equals
                        const zero = try self.func().emitConstI64(0);
                        const negated = try self.func().emitBinaryOp(.icmp_eq, equals_result.value, zero, .i64);
                        return .{ .value = negated, .ty = .{ .primitive = "bool" } };
                    }
                }
            }

            // Check for Comparable for ordering operators (<, <=, >, >=)
            if (self.typeConformsTo(type_name, "Comparable")) {
                if (cmp.op != .eq and cmp.op != .ne) {
                    // Call the compare() method: left.compare(right)
                    // Returns -1 for less, 0 for equal, 1 for greater
                    const compare_result = try self.emitMethodCallWithIrArgs(type_name, "compare", &.{right.value}, left.value);

                    const zero = try self.func().emitConstI64(0);
                    const result = switch (cmp.op) {
                        .lt => try self.func().emitBinaryOp(.icmp_lt, compare_result.value, zero, .i64),
                        .le => try self.func().emitBinaryOp(.icmp_le, compare_result.value, zero, .i64),
                        .gt => try self.func().emitBinaryOp(.icmp_gt, compare_result.value, zero, .i64),
                        .ge => try self.func().emitBinaryOp(.icmp_ge, compare_result.value, zero, .i64),
                        else => unreachable,
                    };
                    return .{ .value = result, .ty = .{ .primitive = "bool" } };
                }
            }
        }

        const left_prim = left.ty.toPrimitiveType();
        const right_prim = right.ty.toPrimitiveType();

        // If either operand is float, use float comparison
        if (left_prim == .f64 or right_prim == .f64) {
            // Promote int to float if needed
            const left_val = if (left_prim == .i64)
                try self.func().emitUnaryOp(.sitofp, left.value, .f64)
            else
                left.value;
            const right_val = if (right_prim == .i64)
                try self.func().emitUnaryOp(.sitofp, right.value, .f64)
            else
                right.value;

            const op: ir.Instruction.Op = switch (cmp.op) {
                .eq => .fcmp_eq,
                .ne => .fcmp_ne,
                .lt => .fcmp_lt,
                .le => .fcmp_le,
                .gt => .fcmp_gt,
                .ge => .fcmp_ge,
            };
            const result = try self.func().emitBinaryOp(op, left_val, right_val, .i64);
            return .{ .value = result, .ty = .{ .primitive = "bool" } };
        }

        // Integer comparison
        const op: ir.Instruction.Op = switch (cmp.op) {
            .eq => .icmp_eq,
            .ne => .icmp_ne,
            .lt => .icmp_lt,
            .le => .icmp_le,
            .gt => .icmp_gt,
            .ge => .icmp_ge,
        };
        const result = try self.func().emitBinaryOp(op, left.value, right.value, .i64);
        return .{ .value = result, .ty = .{ .primitive = "bool" } };
    }

    fn convertLogical(self: *AstToIr, log: ast.LogicalExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(log.left.*);
        const right = try self.convertExpression(log.right.*);

        // Logical operators - both values must be booleans (truthy/falsy)
        switch (log.op) {
            .@"and" => {
                // Logical 'and' - both values must be non-zero (truthy)
                // We implement this as: (left != 0) & (right != 0)
                // Using multiplication to combine two boolean results (0*x=0, 1*1=1)
                const zero = try self.func().emitConstI64(0);
                const left_bool = try self.func().emitBinaryOp(.icmp_ne, left.value, zero, .i64);
                const right_bool = try self.func().emitBinaryOp(.icmp_ne, right.value, zero, .i64);
                const result = try self.func().emitBinaryOp(.mul, left_bool, right_bool, .i64);
                return .{ .value = result, .ty = .{ .primitive = "bool" } };
            },
            .@"or" => {
                // Logical 'or' - at least one value must be non-zero (truthy)
                // We implement this as: (left != 0) | (right != 0)
                // Using bitwise OR: if either is 1, result is 1
                const zero = try self.func().emitConstI64(0);
                const left_bool = try self.func().emitBinaryOp(.icmp_ne, left.value, zero, .i64);
                const right_bool = try self.func().emitBinaryOp(.icmp_ne, right.value, zero, .i64);
                const result = try self.func().emitBinaryOp(.bitor, left_bool, right_bool, .i64);
                return .{ .value = result, .ty = .{ .primitive = "bool" } };
            },
        }
    }

    /// Convert nil coalescing expression: `optional or default`
    /// Returns the unwrapped value if optional has a value, otherwise returns default
    fn convertNilCoalesce(self: *AstToIr, nc: ast.NilCoalesceExpr) ConvertError!TypedValue {
        // Evaluate the optional expression
        const opt_typed = try self.convertExpression(nc.optional.*);

        // Get optional type info - must be an optional type
        const opt_info = switch (opt_typed.ty) {
            .optional_type => |info| info,
            .primitive, .struct_type, .array_type, .enum_type => {
                // Left operand is not an optional type - this is an error
                self.reportError(.E017, "left operand of ?? must be optional type");
                return error.TypeMismatch;
            },
        };

        const wrapped_type = opt_info.wrapped;

        // Allocate result storage in entry block BEFORE branching
        const result_ptr = try self.func().emitAlloca(wrapped_type);

        // Load tag from optional (offset 0) and compare with 1 (has value)
        const tag = try self.func().emitLoad(opt_typed.value, .i64);
        const one = try self.func().emitConstI64(1);
        const has_value = try self.func().emitBinaryOp(.icmp_eq, tag, one, .i64);

        // Create blocks for branching
        const has_value_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("coalesce_has_value");
        const use_default_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("coalesce_default");
        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("coalesce_merge");

        // Emit conditional branch from current block
        const entry_block = &self.func().blocks.items[has_value_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = has_value }, .{ .block_ref = has_value_block_idx }, .{ .block_ref = use_default_block_idx } },
        });

        // Pop merge block temporarily
        const merge_block = self.func().blocks.pop().?;
        // Pop default block temporarily
        const default_block = self.func().blocks.pop().?;

        // Now in has_value block: extract the unwrapped value
        const value_ptr = try self.func().emitGetFieldPtr(opt_typed.value, 8);
        const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);

        // Store unwrapped value to result
        try self.func().emitStore(result_ptr, unwrapped_value);

        // Branch to merge
        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
            .result = null,
        });

        // Push default block back and switch to it
        try self.func().blocks.append(self.allocator, default_block);

        // Evaluate default expression here (short-circuit evaluation)
        const default_typed = try self.convertExpression(nc.default.*);

        // Store default value to result
        try self.func().emitStore(result_ptr, default_typed.value);

        // Branch to merge
        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
            .result = null,
        });

        // Push merge block back and switch to it
        try self.func().blocks.append(self.allocator, merge_block);

        // Load result
        const result = try self.func().emitLoad(result_ptr, wrapped_type);
        return .{ .value = result, .ty = default_typed.ty };
    }

    fn convertStructInit(self: *AstToIr, sinit: ast.StructInitExpr) ConvertError!TypedValue {
        // Build monomorphized type name if there are type arguments
        const type_name = if (sinit.type_args.len > 0) blk: {
            // Check if monomorphized version exists, if not create it
            const mono_name = try self.getOrCreateMonomorphizedType(sinit.type_name, sinit.type_args);
            break :blk mono_name;
        } else self.resolveTypeName(sinit.type_name);

        const struct_info = try self.lookupStructInfo(type_name);
        const struct_ptr = try self.func().emitAllocaSized(struct_info.size);
        try self.initStructIntoResolved(sinit, struct_ptr, type_name);
        return .{ .value = struct_ptr, .ty = .{ .struct_type = type_name } };
    }

    /// Initialize struct fields into an existing pointer (used for sret returns)
    fn initStructInto(self: *AstToIr, sinit: ast.StructInitExpr, dest_ptr: ir.Value) ConvertError!void {
        // Build monomorphized type name if there are type arguments
        const type_name = if (sinit.type_args.len > 0) blk: {
            const mono_name = try self.getOrCreateMonomorphizedType(sinit.type_name, sinit.type_args);
            break :blk mono_name;
        } else self.resolveTypeName(sinit.type_name);
        try self.initStructIntoResolved(sinit, dest_ptr, type_name);
    }

    /// Initialize struct fields into an existing pointer with resolved type name
    fn initStructIntoResolved(self: *AstToIr, sinit: ast.StructInitExpr, dest_ptr: ir.Value, type_name: []const u8) ConvertError!void {
        const struct_info = try self.lookupStructInfo(type_name);

        // Zero the entire struct first to ensure uninitialized fields are zero
        // This is important for types like Array that need zeroed memory
        try self.func().emitMemset(dest_ptr, 0, struct_info.size);

        for (sinit.fields) |field_init| {
            const field_info = try self.lookupField(struct_info, field_init.name);
            const field_ptr = try self.func().emitGetFieldPtr(dest_ptr, field_info.offset);
            const field_val = try self.convertExpression(field_init.value.*);

            // Track ownership transfer for array/struct fields
            try self.trackFieldOwnershipTransfer(field_init.value.*, type_name);

            if (field_info.isStruct()) {
                // Struct fields are embedded inline - copy the data
                try self.emitStructCopy(field_ptr, field_val.value, field_info.size, field_info.structName());
            } else {
                try self.func().emitStore(field_ptr, field_val.value);
            }
        }
    }

    /// Track ownership when a variable is moved into a struct field
    fn trackFieldOwnershipTransfer(self: *AstToIr, expr: ast.Expression, target_type: []const u8) ConvertError!void {
        if (expr != .identifier) return;

        const var_name = expr.identifier;
        const var_info = self.var_map.getPtr(var_name) orelse return;

        // Reference types are moved, not copied
        const is_reference = var_info.ty == .array_type or var_info.ty == .struct_type or var_info.ty == .optional_type;
        if (!is_reference) return;

        if (!var_info.is_mutable) {
            debug.astToIr("cannot move immutable variable '{s}' into struct\n", .{var_name});
            self.reportError(.E010, var_name);
            return error.ImmutableMove;
        }
        var_info.markMoved(target_type, self.current_line);
    }

    fn convertFieldAccess(self: *AstToIr, faccess: ast.FieldAccessExpr) ConvertError!TypedValue {
        // Check for enum member access (e.g., Colors.Green)
        if (faccess.base.* == .identifier) {
            if (self.type_map.get(faccess.base.identifier)) |type_info| {
                if (type_info == .enum_type) {
                    const member_value = type_info.enum_type.members.get(faccess.field_name) orelse {
                        self.reportError(.E007, faccess.field_name);
                        return error.UnknownField;
                    };
                    return .{
                        .value = try self.func().emitConstI64(member_value),
                        .ty = .{ .enum_type = faccess.base.identifier },
                    };
                }
            }
        }

        // Struct field access
        const base = try self.convertExpression(faccess.base.*);
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type => {
                std.debug.print("[AST->IR] convertFieldAccess: expected struct type for field '{s}'\n", .{faccess.field_name});
                self.reportError(.E006, faccess.field_name);
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try self.lookupField(struct_info, faccess.field_name);
        const field_ptr = try self.func().emitGetFieldPtr(base.value, field_info.offset);

        // Struct fields are embedded (return ptr directly); others are loaded
        const value = if (field_info.value_type == .struct_type)
            field_ptr
        else
            try self.func().emitLoad(field_ptr, field_info.value_type.toPrimitiveType());

        return .{ .value = value, .ty = field_info.value_type };
    }

    fn convertCall(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        // Handle built-in functions
        if (intrinsics.convertBuiltin(self, call)) |result| {
            return result;
        } else |builtin_err| switch (builtin_err) {
            error.NotABuiltin => {},
            else => return builtin_err,
        }

        // If inside a method body, check if calling a method of the current type (implicit self)
        // Only do this if we have an active function (not during registration)
        if (self.self_ptr != null and self.current_type_name != null and self.current_func != null) {
            const type_name = self.current_type_name.?;
            // Build mangled name on stack to avoid allocation
            var mangled_buf: [256]u8 = undefined;
            if (std.fmt.bufPrint(&mangled_buf, "{s}${s}", .{ type_name, call.func_name })) |mangled_name| {
                if (self.func_map.contains(mangled_name)) {
                    // It's a method of the current type - call via self
                    debug.astToIr("Implicit method call: {s} (self_ptr=%{d})", .{ mangled_name, self.self_ptr.? });
                    return self.emitMethodCall(type_name, call.func_name, call.args, self.self_ptr.?);
                }
            } else |_| {
                // Name too long, skip implicit method check
            }
        }

        const func_info = self.func_map.get(call.func_name) orelse {
            // Unknown function - still pass the arguments (no type info for promotion)
            const args = try self.func().allocator.alloc(ir.Value, call.args.len);
            for (call.args, 0..) |arg_expr, i| {
                const arg = try self.convertExpression(arg_expr);
                args[i] = arg.value;
            }
            // Assume i64 return type - this will be resolved at link time
            const result = try self.func().emitCall(call.func_name, args, .i64);
            return .{ .value = result orelse 0, .ty = .{ .primitive = "int" } };
        };

        // Check if return type is optional (needs sret for 16-byte struct)
        const returns_optional = if (func_info.return_value_type) |vt|
            vt == .optional_type
        else
            false;

        // Check if callee returns a struct or optional (needs sret)
        const returns_struct = func_info.return_type_name != null or returns_optional;
        var sret_buffer: ?ir.Value = null;

        // Allocate args: +1 if using sret for hidden first parameter
        const num_args = call.args.len + @as(usize, if (returns_struct) 1 else 0);
        const args = try self.func().allocator.alloc(ir.Value, num_args);

        // If returning struct or optional, allocate buffer in caller and pass as first arg
        if (returns_struct) {
            if (returns_optional) {
                // Optional size = 8 (tag) + wrapped_value_size
                const opt_info = func_info.return_value_type.?.optional_type;
                sret_buffer = try self.func().emitAllocaSized(self.getOptionalSize(opt_info));
            } else {
                const struct_name = func_info.return_type_name.?;
                const struct_info = try self.lookupStructInfo(struct_name);
                sret_buffer = try self.func().emitAllocaSized(struct_info.size);
            }
            args[0] = sret_buffer.?;
        }

        const arg_offset: usize = if (returns_struct) 1 else 0;
        for (call.args, 0..) |arg_expr, i| {
            const arg = try self.convertExpression(arg_expr);
            var arg_value = arg.value;

            // Implicit int→float promotion: if parameter expects float but arg is int
            if (i < func_info.param_types.len) {
                const param_type = func_info.param_types[i].ty;
                const arg_prim = arg.ty.toPrimitiveType();
                const param_expects_float = switch (param_type) {
                    .primitive => |name| std.mem.eql(u8, name, "float"),
                    else => false,
                };
                if (param_expects_float and arg_prim == .i64) {
                    arg_value = self.func().emitUnaryOp(.sitofp, arg.value, .f64) catch return error.OutOfMemory;
                }
            }

            args[i + arg_offset] = arg_value;
            try self.checkOwnershipTransfer(call.func_name, arg_expr, i);
        }

        const result = try self.func().emitCall(call.func_name, args, func_info.return_type);

        if (func_info.return_type_name) |struct_name| {
            // Return the sret buffer we allocated (not the call result)
            return .{ .value = sret_buffer.?, .ty = .{ .struct_type = struct_name } };
        }
        // If the function returns an optional with sret, return the sret buffer
        if (returns_optional) {
            return .{ .value = sret_buffer.?, .ty = func_info.return_value_type.? };
        }
        // If the function returns an array, use the full array type info
        if (func_info.return_value_type) |vtype| {
            return .{ .value = result orelse 0, .ty = vtype };
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = func_info.return_type.toMaxonName() } };
    }

    fn checkOwnershipTransfer(self: *AstToIr, func_name: []const u8, arg_expr: ast.Expression, param_idx: usize) ConvertError!void {
        const analyzer = self.mutation_analyzer orelse return;
        if (!analyzer.doesMutateParam(func_name, param_idx)) return;

        if (arg_expr != .identifier) return;
        const var_name = arg_expr.identifier;
        const var_info = self.var_map.getPtr(var_name) orelse return;

        if (!var_info.is_mutable) {
            debug.astToIr("cannot move immutable variable '{s}'\n", .{var_name});
            self.reportError(.E010, var_name);
            return error.ImmutableMove;
        }

        var_info.markMoved(func_name, self.current_line);
    }

    /// Index into a __ManagedArray: managed[i]
    /// Returns the element as an optional (bounds-checked)
    fn convertManagedArrayIndex(self: *AstToIr, managed_ptr: ir.Value, index_expr: ast.Expression) ConvertError!TypedValue {
        const idx_typed = try self.convertExpression(index_expr);

        // Load buffer pointer (offset 0) and length (offset 8)
        const buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
        const len_ptr = try self.func().emitGetFieldPtr(managed_ptr, 8);
        const len = try self.func().emitLoad(len_ptr, .i64);

        // Allocate the result optional (16 bytes)
        const opt_ptr = try self.func().emitAllocaSized(16);

        // Bounds check: index >= 0 AND index < len
        const zero = try self.func().emitConstI64(0);
        const is_non_negative = try self.func().emitBinaryOp(.icmp_ge, idx_typed.value, zero, .i64);
        const is_less_than_len = try self.func().emitBinaryOp(.icmp_lt, idx_typed.value, len, .i64);
        const in_bounds = try self.func().emitBinaryOp(.mul, is_non_negative, is_less_than_len, .i64);

        // Create blocks for branching
        const in_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("managed_index_in_bounds");

        const out_of_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("managed_index_out_of_bounds");

        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("managed_index_merge");

        // Emit conditional branch in previous block
        const entry_block = &self.func().blocks.items[in_bounds_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = in_bounds }, .{ .block_ref = in_bounds_block_idx }, .{ .block_ref = out_of_bounds_block_idx } },
        });

        // Save merge and out_of_bounds blocks
        const merge_block = self.func().blocks.pop().?;
        const out_of_bounds_block = self.func().blocks.pop().?;

        // In-bounds block: create some(element)
        const one_val = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one_val); // tag = 1

        // Calculate element pointer: buf_ptr + index * 8
        const eight = try self.func().emitConstI64(8);
        const offset = try self.func().emitBinaryOp(.mul, idx_typed.value, eight, .i64);
        const elem_ptr = try self.func().emitBinaryOp(.add, buf_ptr, offset, .ptr);
        const val = try self.func().emitLoad(elem_ptr, .i64);

        const value_slot = try self.func().emitGetFieldPtr(opt_ptr, 8);
        try self.func().emitStore(value_slot, val);

        try self.func().emitBr(merge_block_idx);

        // Restore out-of-bounds block
        try self.func().blocks.append(self.allocator, out_of_bounds_block);

        // Out-of-bounds block: create nil
        const zero_val = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero_val); // tag = 0

        try self.func().emitBr(merge_block_idx);

        // Restore merge block
        try self.func().blocks.append(self.allocator, merge_block);

        // Return the optional (element type is i64 for now)
        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = .{
                .wrapped = .i64,
                .wrapped_struct_type = null,
            } },
        };
    }

    /// Convert indexing on stdlib Array types (Array$int, etc.) by calling the .get() method
    fn convertStdlibArrayIndex(self: *AstToIr, base_typed: TypedValue, index_expr: ast.Expression) ConvertError!TypedValue {
        const type_name = base_typed.ty.struct_type;

        // Convert the index expression
        const idx_typed = try self.convertExpression(index_expr);

        // Get the get method: Array$int$get
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}$get", .{type_name});
        try self.module.trackString(mangled_name);

        const func_info = self.func_map.get(mangled_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "stdlib Array type '{s}' missing 'get' method", .{type_name}) catch "stdlib Array missing get method";
            self.reportInternalError(msg);
            return error.SemanticError;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(mangled_name);
        }

        // The get method returns Element or nil (an optional)
        // Args: sret_ptr, self_ptr, index
        const default_type: ValueType = .{ .optional_type = .{ .wrapped = .i64 } };
        const return_type = func_info.return_value_type orelse default_type;
        const opt_info = if (return_type == .optional_type) return_type.optional_type else OptionalInfo{ .wrapped = .i64 };
        const sret_buffer = try self.allocateOptional(opt_info);

        var args = try self.allocator.alloc(ir.Value, 3);
        args[0] = sret_buffer;
        args[1] = base_typed.value;
        args[2] = idx_typed.value;

        _ = try self.func().emitCall(mangled_name, args, .ptr);

        // Return the optional
        return .{
            .value = sret_buffer,
            .ty = return_type,
        };
    }

    /// Convert index assignment on stdlib Array types (Array$int, etc.) by calling the .set() method
    fn convertStdlibArrayIndexAssign(self: *AstToIr, base_typed: TypedValue, index_expr: ast.Expression, value_expr: ast.Expression) ConvertError!void {
        const type_name = base_typed.ty.struct_type;

        // Convert index and value expressions
        const idx_typed = try self.convertExpression(index_expr);
        const val_typed = try self.convertExpression(value_expr);

        // Get the set method: Array$int$set
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}$set", .{type_name});
        try self.module.trackString(mangled_name);

        const func_info = self.func_map.get(mangled_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "stdlib Array type '{s}' missing 'set' method", .{type_name}) catch "stdlib Array missing set method";
            self.reportInternalError(msg);
            return error.SemanticError;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(mangled_name);
        }

        // The set method returns Self (the array), which is a struct
        // Args: sret_ptr, self_ptr, index, value
        const sret_buffer = try self.func().emitAllocaSized(32); // Array struct size (managed: 24 + iterIndex: 8)

        var args = try self.allocator.alloc(ir.Value, 4);
        args[0] = sret_buffer;
        args[1] = base_typed.value;
        args[2] = idx_typed.value;
        args[3] = val_typed.value;

        _ = try self.func().emitCall(mangled_name, args, func_info.return_type);
        // We don't need to do anything with the return value since it's just for chaining
    }

    // ------------------------------------------------------------------------
    // Array Expression Conversion
    // ------------------------------------------------------------------------

    /// Convert InitableFromArrayLiteral with simple type annotation: var arr IntArray = [1, 2, 3]
    fn convertInitableFromArrayLiteralSimple(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        try self.convertInitableFromArrayLiteralImpl(decl, type_name);
    }

    /// Convert InitableFromArrayLiteral: var arr Array of int = [1, 2, 3]
    /// Creates a __ManagedArray and calls Type$init(managed)
    fn convertInitableFromArrayLiteral(self: *AstToIr, decl: ast.VarDecl, gen: ast.GenericTypeExpr) !void {
        try self.convertInitableFromArrayLiteralImpl(decl, gen.base_type);
    }

    /// Implementation of InitableFromArrayLiteral transformation
    fn convertInitableFromArrayLiteralImpl(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const arr_lit = decl.value.array_literal;
        const elements = arr_lit.elements;

        debug.astToIr("InitableFromArrayLiteral: {s} with {d} elements", .{ type_name, elements.len });

        // Allocate __ManagedArray on stack
        const managed_ptr = try self.func().emitAllocaSized(MANAGED_ARRAY_SIZE);

        if (elements.len == 0) {
            // Empty array: buffer=null, len=0, capacity=0
            const null_ptr = try self.func().emitConstI64(0);
            const buffer_slot = try self.func().emitGetElemPtr(managed_ptr, null_ptr, 0);
            try self.func().emitStore(buffer_slot, null_ptr);

            const len_offset = try self.func().emitConstI64(1);
            const len_slot = try self.func().emitGetElemPtr(managed_ptr, len_offset, 8);
            try self.func().emitStore(len_slot, null_ptr);

            const cap_offset = try self.func().emitConstI64(2);
            const cap_slot = try self.func().emitGetElemPtr(managed_ptr, cap_offset, 8);
            try self.func().emitStore(cap_slot, null_ptr);
        } else {
            // Allocate buffer for elements (on heap for ownership)
            const elem_count = try self.func().emitConstI64(@intCast(elements.len));
            const elem_size: i64 = 8; // All elements are 8 bytes (i64, f64, or pointer)
            const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
            const buffer = try self.func().emitHeapAlloc(buffer_size);

            // Store elements into buffer
            for (elements, 0..) |elem, i| {
                const typed = try self.convertExpression(elem);
                const idx_val = try self.func().emitConstI64(@intCast(i));
                const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(elem_ptr, typed.value);
            }

            // Store buffer pointer into __ManagedArray
            const zero = try self.func().emitConstI64(0);
            const buffer_slot = try self.func().emitGetElemPtr(managed_ptr, zero, 0);
            try self.func().emitStore(buffer_slot, buffer);

            // Store length
            const len_offset = try self.func().emitConstI64(1);
            const len_slot = try self.func().emitGetElemPtr(managed_ptr, len_offset, 8);
            try self.func().emitStore(len_slot, elem_count);

            // Store capacity (same as length for literals)
            const cap_offset = try self.func().emitConstI64(2);
            const cap_slot = try self.func().emitGetElemPtr(managed_ptr, cap_offset, 8);
            try self.func().emitStore(cap_slot, elem_count);
        }

        // Call Type$init(managed) - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        // Look up the function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            self.reportError(.E003, init_func_name);
            return error.UnknownFunction;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        // Check if return type is a struct (needs sret)
        const uses_sret = type_info == .struct_type;

        if (uses_sret) {
            // Allocate space for returned struct
            const struct_size = type_info.struct_type.size;
            const result_ptr = try self.func().emitAllocaSized(struct_size);

            // Build args: [sret_ptr, managed_ptr]
            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = result_ptr;
            args[1] = managed_ptr;

            // Call init with sret
            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

            // Store result in var_map
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .struct_type = type_name },
                self.current_decl_is_mutable,
                false,
            ));
        } else {
            // Non-struct return type (unlikely for InitableFromArrayLiteral)
            var args = try self.func().allocator.alloc(ir.Value, 1);
            args[0] = managed_ptr;
            const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
            const result_ptr = try self.func().emitAlloca(func_info.return_type);
            try self.func().emitStore(result_ptr, result orelse 0);
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .primitive = func_info.return_type.toMaxonName() },
                self.current_decl_is_mutable,
                false,
            ));
        }
    }

    /// Convert InitableFromMapLiteral with type annotation: var m Map from K to V = ["a": 1, "b": 2]
    /// Creates two __ManagedArrays (keys and values) and calls Type$InitableFromMapLiteral$init(keys, values)
    fn convertInitableFromMapLiteralSimple(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const map_lit = decl.value.map_literal;
        const entries = map_lit.entries;

        debug.astToIr("InitableFromMapLiteral: {s} with {d} entries", .{ type_name, entries.len });

        // Allocate two __ManagedArrays on stack
        const keys_managed_ptr = try self.func().emitAllocaSized(MANAGED_ARRAY_SIZE);
        const values_managed_ptr = try self.func().emitAllocaSized(MANAGED_ARRAY_SIZE);

        if (entries.len == 0) {
            // Empty map: buffer=null, len=0, capacity=0 for both arrays
            const null_ptr = try self.func().emitConstI64(0);

            // Initialize keys __ManagedArray
            const keys_buffer_slot = try self.func().emitGetElemPtr(keys_managed_ptr, null_ptr, 0);
            try self.func().emitStore(keys_buffer_slot, null_ptr);
            const len_offset = try self.func().emitConstI64(1);
            const keys_len_slot = try self.func().emitGetElemPtr(keys_managed_ptr, len_offset, 8);
            try self.func().emitStore(keys_len_slot, null_ptr);
            const cap_offset = try self.func().emitConstI64(2);
            const keys_cap_slot = try self.func().emitGetElemPtr(keys_managed_ptr, cap_offset, 8);
            try self.func().emitStore(keys_cap_slot, null_ptr);

            // Initialize values __ManagedArray
            const values_buffer_slot = try self.func().emitGetElemPtr(values_managed_ptr, null_ptr, 0);
            try self.func().emitStore(values_buffer_slot, null_ptr);
            const values_len_slot = try self.func().emitGetElemPtr(values_managed_ptr, len_offset, 8);
            try self.func().emitStore(values_len_slot, null_ptr);
            const values_cap_slot = try self.func().emitGetElemPtr(values_managed_ptr, cap_offset, 8);
            try self.func().emitStore(values_cap_slot, null_ptr);
        } else {
            // Allocate buffers for keys and values on heap
            const elem_count = try self.func().emitConstI64(@intCast(entries.len));
            const elem_size: i64 = 8; // All elements are 8 bytes
            const buffer_size = try self.func().emitConstI64(@intCast(entries.len * @as(usize, @intCast(elem_size))));
            const keys_buffer = try self.func().emitHeapAlloc(buffer_size);
            const values_buffer = try self.func().emitHeapAlloc(buffer_size);

            // Store entries into buffers
            for (entries, 0..) |entry, i| {
                const key_typed = try self.convertExpression(entry.key.*);
                const value_typed = try self.convertExpression(entry.value.*);
                const idx_val = try self.func().emitConstI64(@intCast(i));

                const key_ptr = try self.func().emitGetElemPtr(keys_buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(key_ptr, key_typed.value);

                const value_ptr = try self.func().emitGetElemPtr(values_buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(value_ptr, value_typed.value);
            }

            // Initialize keys __ManagedArray: {buffer, len, capacity}
            const zero = try self.func().emitConstI64(0);
            const keys_buffer_slot = try self.func().emitGetElemPtr(keys_managed_ptr, zero, 0);
            try self.func().emitStore(keys_buffer_slot, keys_buffer);
            const len_offset = try self.func().emitConstI64(1);
            const keys_len_slot = try self.func().emitGetElemPtr(keys_managed_ptr, len_offset, 8);
            try self.func().emitStore(keys_len_slot, elem_count);
            const cap_offset = try self.func().emitConstI64(2);
            const keys_cap_slot = try self.func().emitGetElemPtr(keys_managed_ptr, cap_offset, 8);
            try self.func().emitStore(keys_cap_slot, elem_count);

            // Initialize values __ManagedArray: {buffer, len, capacity}
            const values_buffer_slot = try self.func().emitGetElemPtr(values_managed_ptr, zero, 0);
            try self.func().emitStore(values_buffer_slot, values_buffer);
            const values_len_slot = try self.func().emitGetElemPtr(values_managed_ptr, len_offset, 8);
            try self.func().emitStore(values_len_slot, elem_count);
            const values_cap_slot = try self.func().emitGetElemPtr(values_managed_ptr, cap_offset, 8);
            try self.func().emitStore(values_cap_slot, elem_count);
        }

        // Call Type$init(keys, values) - the static init method from InitableFromMapLiteral interface
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        // Look up the function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for InitableFromMapLiteral", .{type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        // Check if return type is a struct (needs sret)
        const uses_sret = type_info == .struct_type;

        if (uses_sret) {
            // Allocate space for returned struct
            const struct_size = type_info.struct_type.size;
            const result_ptr = try self.func().emitAllocaSized(struct_size);

            // Build args: [sret_ptr, keys_managed_ptr, values_managed_ptr]
            var args = try self.func().allocator.alloc(ir.Value, 3);
            args[0] = result_ptr;
            args[1] = keys_managed_ptr;
            args[2] = values_managed_ptr;

            // Call init with sret
            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

            // Store result in var_map
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .struct_type = type_name },
                self.current_decl_is_mutable,
                false,
            ));
        } else {
            // Non-struct return type (unlikely for InitableFromMapLiteral)
            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = keys_managed_ptr;
            args[1] = values_managed_ptr;
            const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
            const result_ptr = try self.func().emitAlloca(func_info.return_type);
            try self.func().emitStore(result_ptr, result orelse 0);
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .primitive = func_info.return_type.toMaxonName() },
                self.current_decl_is_mutable,
                false,
            ));
        }
    }

    /// Convert InitableFromStringLiteral: var s string = "hello"
    /// Creates a __ManagedString and calls Type$init(managed)
    fn convertInitableFromStringLiteral(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const str_bytes = decl.value.string_literal;

        const processed = try self.processEscapeSequences(str_bytes);
        defer self.allocator.free(processed);
        debug.astToIr("InitableFromStringLiteral: {s} with {d} bytes", .{ type_name, processed.len });

        const managed_ptr = try self.emitManagedStringFromBytes(processed);

        // Call Type$init(managed) - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        // Look up the function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for InitableFromStringLiteral", .{type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        // Check if return type is a struct (needs sret)
        const uses_sret = type_info == .struct_type;

        if (uses_sret) {
            // Allocate space for returned struct
            const struct_size = type_info.struct_type.size;
            const result_ptr = try self.func().emitAllocaSized(struct_size);

            // Build args: [sret_ptr, managed_ptr]
            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = result_ptr;
            args[1] = managed_ptr;

            // Call init with sret
            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

            // Store result in var_map
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .struct_type = type_name },
                self.current_decl_is_mutable,
                false,
            ));
        } else {
            // Non-struct return type
            var args = try self.func().allocator.alloc(ir.Value, 1);
            args[0] = managed_ptr;
            const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
            const result_ptr = try self.func().emitAlloca(func_info.return_type);
            try self.func().emitStore(result_ptr, result orelse 0);
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .primitive = func_info.return_type.toMaxonName() },
                self.current_decl_is_mutable,
                false,
            ));
        }
    }

    /// Convert InitableFromCharLiteral: var c character = 'a'
    /// Creates a __ManagedString from the character bytes and calls Type$init(managed)
    fn convertInitableFromCharLiteral(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const char_bytes = decl.value.char_literal;
        // Process escape sequences in the character literal
        const processed_bytes = try self.processEscapeSequences(char_bytes);

        debug.astToIr("InitableFromCharLiteral: {s} with {d} bytes", .{ type_name, processed_bytes.len });

        const managed_ptr = try self.emitManagedStringFromBytes(processed_bytes);

        // Call Type$init(managed) - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for InitableFromCharLiteral", .{type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        const uses_sret = type_info == .struct_type;

        if (uses_sret) {
            const struct_size = type_info.struct_type.size;
            const result_ptr = try self.func().emitAllocaSized(struct_size);

            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = result_ptr;
            args[1] = managed_ptr;

            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .struct_type = type_name },
                self.current_decl_is_mutable,
                false,
            ));
        } else {
            var args = try self.func().allocator.alloc(ir.Value, 1);
            args[0] = managed_ptr;
            const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
            const result_ptr = try self.func().emitAlloca(func_info.return_type);
            try self.func().emitStore(result_ptr, result orelse 0);
            try self.func().setValueName(result_ptr, decl.name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.init(
                result_ptr,
                .{ .primitive = func_info.return_type.toMaxonName() },
                self.current_decl_is_mutable,
                false,
            ));
        }
    }

    fn convertArrayLiteral(self: *AstToIr, arr_lit: ast.ArrayLiteralExpr) ConvertError!TypedValue {
        const elements = arr_lit.elements;

        // Allocate __ManagedArray on stack (24 bytes: buffer ptr, len, capacity)
        const managed_ptr = try self.func().emitAllocaSized(MANAGED_ARRAY_SIZE);

        if (elements.len == 0) {
            // Empty array: buffer=null, len=0, capacity=0
            const null_ptr = try self.func().emitConstI64(0);
            const buffer_slot = try self.func().emitGetElemPtr(managed_ptr, null_ptr, 0);
            try self.func().emitStore(buffer_slot, null_ptr);

            const len_offset = try self.func().emitConstI64(1);
            const len_slot = try self.func().emitGetElemPtr(managed_ptr, len_offset, 8);
            try self.func().emitStore(len_slot, null_ptr);

            const cap_offset = try self.func().emitConstI64(2);
            const cap_slot = try self.func().emitGetElemPtr(managed_ptr, cap_offset, 8);
            try self.func().emitStore(cap_slot, null_ptr);

            return .{
                .value = managed_ptr,
                .ty = .{ .array_type = .{ .element_type = .i64, .size = 0, .storage = .heap } },
            };
        }

        const first_typed = try self.convertExpression(elements[0]);
        const elem_type = first_typed.ty.toPrimitiveType();
        const elem_struct_type: ?[]const u8 = switch (first_typed.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type => null,
        };

        // Calculate element size: structs use their actual size, primitives use 8 bytes
        const elem_size: i64 = if (elem_struct_type) |struct_name| blk: {
            if (self.type_map.get(struct_name)) |type_info| {
                if (type_info == .struct_type) {
                    break :blk @intCast(type_info.struct_type.size);
                }
            }
            break :blk 8;
        } else 8;

        // Allocate buffer for elements on heap
        const elem_count = try self.func().emitConstI64(@intCast(elements.len));
        const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
        const buffer = try self.func().emitHeapAlloc(buffer_size);

        // Store elements into buffer
        for (elements, 0..) |elem, i| {
            const typed = if (i == 0) first_typed else try self.convertExpression(elem);
            const idx_val = try self.func().emitConstI64(@intCast(i));
            const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
            if (elem_struct_type) |struct_name| {
                // For structs, copy the struct data
                try self.emitStructCopy(elem_ptr, typed.value, @intCast(elem_size), struct_name);
            } else {
                // For primitives, store the value directly
                try self.func().emitStore(elem_ptr, typed.value);
            }
        }

        // Store buffer pointer into __ManagedArray
        const zero = try self.func().emitConstI64(0);
        const buffer_slot = try self.func().emitGetElemPtr(managed_ptr, zero, 0);
        try self.func().emitStore(buffer_slot, buffer);

        // Store length
        const len_offset = try self.func().emitConstI64(1);
        const len_slot = try self.func().emitGetElemPtr(managed_ptr, len_offset, 8);
        try self.func().emitStore(len_slot, elem_count);

        // Store capacity (same as length for literals)
        const cap_offset = try self.func().emitConstI64(2);
        const cap_slot = try self.func().emitGetElemPtr(managed_ptr, cap_offset, 8);
        try self.func().emitStore(cap_slot, elem_count);

        return .{
            .value = managed_ptr,
            .ty = .{ .array_type = .{ .element_type = elem_type, .size = elements.len, .storage = .heap, .element_struct_type = elem_struct_type } },
        };
    }

    fn convertMapLiteral(self: *AstToIr, map_lit: ast.MapLiteralExpr) ConvertError!TypedValue {
        const entries = map_lit.entries;

        // Empty map literals require type annotation (cannot infer types)
        if (entries.len == 0) {
            debug.astToIr("error: empty map literal requires type annotation", .{});
            self.reportError(.E006, "empty map literal requires type annotation");
            return error.UnknownType;
        }

        // Infer key and value types from the first entry
        const first_key_typed = try self.convertExpression(entries[0].key.*);
        const first_value_typed = try self.convertExpression(entries[0].value.*);

        const key_type_name = first_key_typed.ty.getTypeName() orelse {
            debug.astToIr("error: map key type must be a named type (primitive or struct)", .{});
            self.reportError(.E006, "map key type must be a named type");
            return error.UnknownType;
        };
        const value_type_name = first_value_typed.ty.getTypeName() orelse {
            debug.astToIr("error: map value type must be a named type (primitive or struct)", .{});
            self.reportError(.E006, "map value type must be a named type");
            return error.UnknownType;
        };

        debug.astToIr("Map literal: {d} entries, key type={s}, value type={s}", .{ entries.len, key_type_name, value_type_name });

        // Build the monomorphized Map type name: Map$KeyType$ValueType
        var type_args = [_][]const u8{ key_type_name, value_type_name };
        const map_type_name = try self.getOrCreateMonomorphizedType("Map", &type_args);

        // Create two __ManagedArrays on stack
        const keys_managed_ptr = try self.func().emitAllocaSized(MANAGED_ARRAY_SIZE);
        const values_managed_ptr = try self.func().emitAllocaSized(MANAGED_ARRAY_SIZE);

        // Allocate buffers for keys and values on heap
        const elem_count = try self.func().emitConstI64(@intCast(entries.len));
        const elem_size: i64 = 8; // All elements are 8 bytes
        const buffer_size = try self.func().emitConstI64(@intCast(entries.len * @as(usize, @intCast(elem_size))));
        const keys_buffer = try self.func().emitHeapAlloc(buffer_size);
        const values_buffer = try self.func().emitHeapAlloc(buffer_size);

        // Store entries into buffers
        for (entries, 0..) |entry, i| {
            const key_typed = if (i == 0) first_key_typed else try self.convertExpression(entry.key.*);
            const value_typed = if (i == 0) first_value_typed else try self.convertExpression(entry.value.*);
            const idx_val = try self.func().emitConstI64(@intCast(i));

            const key_ptr = try self.func().emitGetElemPtr(keys_buffer, idx_val, @intCast(elem_size));
            try self.func().emitStore(key_ptr, key_typed.value);

            const value_ptr = try self.func().emitGetElemPtr(values_buffer, idx_val, @intCast(elem_size));
            try self.func().emitStore(value_ptr, value_typed.value);
        }

        // Initialize keys __ManagedArray: {buffer, len, capacity}
        const zero = try self.func().emitConstI64(0);
        const keys_buffer_slot = try self.func().emitGetElemPtr(keys_managed_ptr, zero, 0);
        try self.func().emitStore(keys_buffer_slot, keys_buffer);
        const len_offset = try self.func().emitConstI64(1);
        const keys_len_slot = try self.func().emitGetElemPtr(keys_managed_ptr, len_offset, 8);
        try self.func().emitStore(keys_len_slot, elem_count);
        const cap_offset = try self.func().emitConstI64(2);
        const keys_cap_slot = try self.func().emitGetElemPtr(keys_managed_ptr, cap_offset, 8);
        try self.func().emitStore(keys_cap_slot, elem_count);

        // Initialize values __ManagedArray: {buffer, len, capacity}
        const values_buffer_slot = try self.func().emitGetElemPtr(values_managed_ptr, zero, 0);
        try self.func().emitStore(values_buffer_slot, values_buffer);
        const values_len_slot = try self.func().emitGetElemPtr(values_managed_ptr, len_offset, 8);
        try self.func().emitStore(values_len_slot, elem_count);
        const values_cap_slot = try self.func().emitGetElemPtr(values_managed_ptr, cap_offset, 8);
        try self.func().emitStore(values_cap_slot, elem_count);

        // Build init function name: Map$K$V$init (static init from InitableFromMapLiteral interface)
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{map_type_name});
        try self.module.trackString(init_func_name);

        // Look up function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "Map type '{s}' missing init method for InitableFromMapLiteral", .{map_type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(map_type_name) orelse {
            self.reportError(.E006, map_type_name);
            return error.UnknownType;
        };

        // Map$init returns a struct, so use sret calling convention
        const struct_size = type_info.struct_type.size;
        const result_ptr = try self.func().emitAllocaSized(struct_size);

        // Build args: [sret_ptr, keys_managed_ptr, values_managed_ptr]
        var args = try self.allocator.alloc(ir.Value, 3);
        args[0] = result_ptr;
        args[1] = keys_managed_ptr;
        args[2] = values_managed_ptr;

        // Call init with sret
        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        return .{
            .value = result_ptr,
            .ty = .{ .struct_type = map_type_name },
        };
    }

    fn convertIndex(self: *AstToIr, idx: ast.IndexExpr) ConvertError!TypedValue {
        const base_typed = try self.convertExpression(idx.base.*);

        // Handle __ManagedArray indexing (stdlib only)
        if (base_typed.ty == .struct_type) {
            if (std.mem.eql(u8, base_typed.ty.struct_type, "__ManagedArray")) {
                return self.convertManagedArrayIndex(base_typed.value, idx.index.*);
            }
            // Handle stdlib Array types (Array$int, etc.) - call .get() method
            if (std.mem.startsWith(u8, base_typed.ty.struct_type, "Array$")) {
                return self.convertStdlibArrayIndex(base_typed, idx.index.*);
            }
        }

        const arr_info = switch (base_typed.ty) {
            .array_type => |a| a,
            .primitive, .struct_type, .enum_type, .optional_type => {
                std.debug.print("[AST->IR] convertIndexExpr: expected array type\n", .{});
                self.reportError(.E006, "expected array type for indexing");
                return error.UnknownType;
            },
        };

        // For heap arrays, base_typed.value points to the __ManagedArray structure
        // We need to load the buffer pointer from offset 0 to index into the actual data
        const buffer_ptr: ir.Value = if (arr_info.storage == .heap)
            try self.func().emitLoad(base_typed.value, .ptr)
        else
            base_typed.value;

        const idx_typed = try self.convertExpression(idx.index.*);

        // Calculate element size: for structs, use actual struct size; for primitives, use 8 bytes
        const elem_size: i32 = if (arr_info.element_struct_type) |struct_name| blk: {
            if (self.type_map.get(struct_name)) |type_info| {
                if (type_info == .struct_type) {
                    break :blk type_info.struct_type.size;
                }
            }
            break :blk 8;
        } else 8;

        // Get the array size for bounds checking
        const arr_size: ir.Value = if (arr_info.size) |size|
            try self.func().emitConstI64(@intCast(size))
        else blk: {
            // Dynamic array - need to get the size from companion variable
            // First, find the array name from the base expression
            const arr_name = switch (idx.base.*) {
                .identifier => |name| name,
                // For non-identifier bases, we can't do bounds checking with dynamic size
                // Fall back to direct access (unsafe)
                .integer,
                .float_lit,
                .bool_lit,
                .nil_lit,
                .self_expr,
                .string_literal,
                .char_literal,
                .unary,
                .binary,
                .compare,
                .logical,
                .call,
                .struct_init,
                .field_access,
                .array_literal,
                .map_literal,
                .index,
                .array_type,
                .method_call,
                .nil_coalesce,
                .cast,
                .interpolated_string,
                .closure,
                => {
                    const elem_ptr = try self.func().emitGetElemPtr(buffer_ptr, idx_typed.value, elem_size);
                    if (arr_info.element_struct_type != null) {
                        // For structs, return pointer to the struct data directly
                        return self.createSomeOptionalWithStructType(elem_ptr, arr_info.element_type, arr_info.element_struct_type);
                    } else {
                        const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);
                        return self.createSomeOptional(val, arr_info.element_type);
                    }
                },
            };
            const size_var_name = try std.fmt.allocPrint(self.allocator, "{s}_size", .{arr_name});
            defer self.allocator.free(size_var_name);

            const size_var_entry = self.var_map.getPtr(size_var_name) orelse {
                // No size variable - can't do bounds checking, fall back to direct access
                const elem_ptr = try self.func().emitGetElemPtr(buffer_ptr, idx_typed.value, elem_size);
                if (arr_info.element_struct_type != null) {
                    // For structs, return pointer to the struct data directly
                    return self.createSomeOptionalWithStructType(elem_ptr, arr_info.element_type, arr_info.element_struct_type);
                } else {
                    const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);
                    return self.createSomeOptional(val, arr_info.element_type);
                }
            };
            size_var_entry.used = true;
            break :blk try self.func().emitLoad(size_var_entry.ptr, .i64);
        };

        // Allocate the result optional: 8 bytes tag + element size
        const opt_size: i32 = if (arr_info.element_struct_type) |struct_name| blk: {
            if (self.type_map.get(struct_name)) |type_info| {
                if (type_info == .struct_type) {
                    break :blk 8 + type_info.struct_type.size;
                }
            }
            break :blk 16;
        } else 16;
        const opt_ptr = try self.func().emitAllocaSized(opt_size);

        // Bounds check: index < size AND index >= 0
        // Since we use i64, negative check is: index >= 0
        const zero = try self.func().emitConstI64(0);
        const is_non_negative = try self.func().emitBinaryOp(.icmp_ge, idx_typed.value, zero, .i64);
        const is_less_than_size = try self.func().emitBinaryOp(.icmp_lt, idx_typed.value, arr_size, .i64);

        // Combine both conditions with AND
        // Since we don't have a direct AND instruction, use multiplication (both are 0 or 1)
        const in_bounds = try self.func().emitBinaryOp(.mul, is_non_negative, is_less_than_size, .i64);

        // Create blocks for branching
        const in_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("index_in_bounds");

        const out_of_bounds_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("index_out_of_bounds");

        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("index_merge");

        // Emit conditional branch in previous block
        const entry_block = &self.func().blocks.items[in_bounds_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = in_bounds }, .{ .block_ref = in_bounds_block_idx }, .{ .block_ref = out_of_bounds_block_idx } },
        });

        // Save merge and out_of_bounds blocks
        const merge_block = self.func().blocks.pop().?;
        const out_of_bounds_block = self.func().blocks.pop().?;

        // In-bounds block: create some(element)
        const one_val = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one_val); // tag = 1

        const elem_ptr = try self.func().emitGetElemPtr(buffer_ptr, idx_typed.value, elem_size);

        const value_slot = try self.func().emitGetFieldPtr(opt_ptr, 8);
        if (arr_info.element_struct_type) |struct_name| {
            // For structs, copy the struct data inline
            const struct_size: i32 = if (self.type_map.get(struct_name)) |type_info| blk: {
                if (type_info == .struct_type) {
                    break :blk type_info.struct_type.size;
                }
                break :blk 8;
            } else 8;
            try self.emitStructCopy(value_slot, elem_ptr, struct_size, struct_name);
        } else {
            // For primitives, load the value and store it
            const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);
            try self.func().emitStore(value_slot, val);
        }

        try self.func().emitBr(merge_block_idx);

        // Restore out-of-bounds block
        try self.func().blocks.append(self.allocator, out_of_bounds_block);

        // Out-of-bounds block: create nil
        const zero_val = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero_val); // tag = 0

        try self.func().emitBr(merge_block_idx);

        // Restore merge block
        try self.func().blocks.append(self.allocator, merge_block);

        // Return the optional
        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = .{
                .wrapped = arr_info.element_type,
                .wrapped_struct_type = arr_info.element_struct_type,
            } },
        };
    }

    fn convertArrayType(self: *AstToIr, arr: ast.ArrayTypeExpr) ConvertError!TypedValue {
        // Convert size expression (capacity)
        const capacity_typed = try self.convertExpression(arr.size.*);

        // Get or create monomorphized Array type for the element type
        // Resolve element type first (e.g., "Element" -> "int" when inside Set$int)
        const resolved_elem = self.resolveTypeName(arr.element_type);
        var type_args = [_][]const u8{resolved_elem};
        const array_type_name = try self.getOrCreateMonomorphizedType("Array", &type_args);

        // Get struct info for the monomorphized Array type
        const type_info = self.type_map.get(array_type_name) orelse {
            debug.astToIr("error: Array type not found after monomorphization: {s}", .{array_type_name});
            return error.UnknownType;
        };

        const struct_info = switch (type_info) {
            .struct_type => |s| s,
            .primitive, .enum_type => {
                debug.astToIr("error: Array type is not a struct: {s}", .{array_type_name});
                return error.UnknownType;
            },
        };

        // Build __ManagedArray with the given capacity
        // For Array of N int, len = capacity so all elements are immediately accessible
        // Allocate __ManagedArray on stack
        const managed_ptr = try self.func().emitAllocaSized(MANAGED_ARRAY_SIZE);

        // Allocate buffer on heap: capacity * 8 bytes (all elements are 8 bytes)
        const elem_size = try self.func().emitConstI64(8);
        const buffer_size = try self.func().emitBinaryOp(.mul, capacity_typed.value, elem_size, .i64);
        const buffer = try self.func().emitHeapAlloc(buffer_size);

        // Store buffer pointer at offset 0
        try self.func().emitStore(managed_ptr, buffer);

        // Store len = capacity at offset 8 (arrays have all elements accessible)
        const len_ptr = try self.func().emitGetFieldPtr(managed_ptr, 8);
        try self.func().emitStore(len_ptr, capacity_typed.value);

        // Store capacity at offset 16
        const cap_ptr = try self.func().emitGetFieldPtr(managed_ptr, 16);
        try self.func().emitStore(cap_ptr, capacity_typed.value);

        // Call Array$init(result_ptr, managed_ptr) via InitableFromArrayLiteral interface
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
        try self.module.trackString(init_func_name);

        // Trigger lazy generation if needed
        if (self.func_map.get(init_func_name)) |func_info| {
            if (!func_info.ir_generated) {
                try self.ensureMethodGenerated(init_func_name);
            }
        }

        // Allocate space for the Array struct (sret)
        const result_ptr = try self.func().emitAllocaSized(@intCast(struct_info.size));

        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = result_ptr;
        args[1] = managed_ptr;

        _ = try self.func().emitCall(init_func_name, args, .ptr);

        return .{
            .value = result_ptr,
            .ty = .{ .struct_type = array_type_name },
        };
    }

    fn convertMethodCall(self: *AstToIr, mcall: ast.MethodCallExpr) ConvertError!void {
        // Check if base is a type name (static method call: TypeName.method())
        if (mcall.base.* == .identifier) {
            const base_name = mcall.base.identifier;
            if (self.type_map.contains(base_name)) {
                _ = try self.emitMethodCall(base_name, mcall.method_name, mcall.args, null);
                return;
            }
        }

        // Convert base and get its type
        const base_typed = try self.convertExpression(mcall.base.*);

        // Check if this is a struct type with methods
        if (base_typed.ty == .struct_type) {
            // Use emitMethodCall which handles sret correctly for struct-returning methods
            _ = try self.emitMethodCall(base_typed.ty.struct_type, mcall.method_name, mcall.args, base_typed.value);
            return;
        }

        // Check if this is an array type - route to Array$ElementType methods
        if (base_typed.ty == .array_type) {
            const arr_info = base_typed.ty.array_type;
            // Get element type name: use struct name if available, else use ir.Type.toMaxonName()
            const elem_name = arr_info.element_struct_type orelse arr_info.element_type.toMaxonName();
            const array_type_name = try std.fmt.allocPrint(self.allocator, "Array${s}", .{elem_name});
            try self.module.trackString(array_type_name);

            // Trigger monomorphization to create Array$ElementType and its methods
            _ = self.getOrCreateMonomorphizedType("Array", &[_][]const u8{elem_name}) catch |e| {
                debug.astToIr("note: could not monomorphize Array${s}: {}", .{ elem_name, e });
            };
            _ = try self.emitMethodCall(array_type_name, mcall.method_name, mcall.args, base_typed.value);
            return;
        }

        debug.astToIr("error: method call on non-struct type", .{});
        self.reportError(.E003, "method call on non-struct type in statement");
        return error.SemanticError;
    }

    /// Emit a method call (static or instance)
    /// self_value is null for static methods, otherwise it's the instance pointer
    fn emitMethodCall(self: *AstToIr, type_name: []const u8, method_name: []const u8, arg_exprs: []const ast.Expression, self_value: ?ir.Value) ConvertError!TypedValue {
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method_name });
        try self.module.trackString(mangled_name);

        const func_info = self.func_map.get(mangled_name) orelse {
            debug.astToIr("error: unknown method '{s}' on type '{s}'", .{ method_name, type_name });
            self.reportError(.E003, mangled_name);
            return error.SemanticError;
        };

        // Trigger lazy generation if method IR hasn't been generated yet
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(mangled_name);
        }

        // Check if return type is optional (needs sret for 16-byte struct)
        const returns_optional = if (func_info.return_value_type) |vt|
            vt == .optional_type
        else
            false;

        // Determine argument layout
        const returns_struct = func_info.return_type_name != null or returns_optional;
        const has_self = self_value != null;
        const sret_offset: usize = if (returns_struct) 1 else 0;
        const self_offset: usize = if (has_self) 1 else 0;
        const num_args = arg_exprs.len + sret_offset + self_offset;

        const args = try self.allocator.alloc(ir.Value, num_args);
        var arg_idx: usize = 0;

        // Sret buffer as first arg if returning struct or optional
        var sret_buffer: ?ir.Value = null;
        if (returns_struct) {
            if (returns_optional) {
                // Optional size = 8 (tag) + wrapped_value_size
                const opt_info = func_info.return_value_type.?.optional_type;
                sret_buffer = try self.func().emitAllocaSized(self.getOptionalSize(opt_info));
            } else {
                const struct_info = try self.lookupStructInfo(func_info.return_type_name.?);
                sret_buffer = try self.func().emitAllocaSized(struct_info.size);
            }
            args[arg_idx] = sret_buffer.?;
            arg_idx += 1;
        }

        // Self pointer for instance methods
        if (self_value) |sv| {
            args[arg_idx] = sv;
            arg_idx += 1;
        }

        // Explicit arguments
        for (arg_exprs) |arg_expr| {
            const arg = try self.convertExpression(arg_expr);
            args[arg_idx] = arg.value;
            arg_idx += 1;
        }

        const result = try self.func().emitCall(mangled_name, args, func_info.return_type);

        // Return appropriate value
        if (sret_buffer) |buf| {
            // For optionals, return the buffer with optional type
            if (returns_optional) {
                return .{ .value = buf, .ty = func_info.return_value_type.? };
            }
            // For structs, return the buffer with struct type
            return .{ .value = buf, .ty = .{ .struct_type = func_info.return_type_name.? } };
        }
        const ret_ty: ValueType = if (func_info.return_value_type) |vt|
            vt
        else if (func_info.return_type_name) |name|
            .{ .struct_type = name }
        else
            .{ .primitive = func_info.return_type.toMaxonName() };

        return .{ .value = result orelse try self.func().emitConstI64(0), .ty = ret_ty };
    }

    /// Emit a method call with pre-converted IR values as arguments
    /// Used when we already have typed values (e.g., in operator overloading for Equatable)
    fn emitMethodCallWithIrArgs(self: *AstToIr, type_name: []const u8, method_name: []const u8, arg_values: []const ir.Value, self_value: ?ir.Value) ConvertError!TypedValue {
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method_name });
        try self.module.trackString(mangled_name);

        const func_info = self.func_map.get(mangled_name) orelse {
            debug.astToIr("error: unknown method '{s}' on type '{s}'", .{ method_name, type_name });
            self.reportError(.E003, mangled_name);
            return error.SemanticError;
        };

        // Trigger lazy generation if method IR hasn't been generated yet
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(mangled_name);
        }

        // Check if return type is optional
        const returns_optional = if (func_info.return_value_type) |vt|
            vt == .optional_type
        else
            false;

        // Determine argument layout
        const returns_struct = func_info.return_type_name != null or returns_optional;
        const has_self = self_value != null;
        const sret_offset: usize = if (returns_struct) 1 else 0;
        const self_offset: usize = if (has_self) 1 else 0;
        const num_args = arg_values.len + sret_offset + self_offset;

        const args = try self.allocator.alloc(ir.Value, num_args);
        var arg_idx: usize = 0;

        // Sret buffer as first arg if returning struct or optional
        var sret_buffer: ?ir.Value = null;
        if (returns_struct) {
            if (returns_optional) {
                // Optional size = 8 (tag) + wrapped_value_size
                const opt_info = func_info.return_value_type.?.optional_type;
                sret_buffer = try self.func().emitAllocaSized(self.getOptionalSize(opt_info));
            } else {
                const struct_info = try self.lookupStructInfo(func_info.return_type_name.?);
                sret_buffer = try self.func().emitAllocaSized(struct_info.size);
            }
            args[arg_idx] = sret_buffer.?;
            arg_idx += 1;
        }

        // Self pointer for instance methods
        if (self_value) |sv| {
            args[arg_idx] = sv;
            arg_idx += 1;
        }

        // Pre-converted IR value arguments
        for (arg_values) |arg_val| {
            args[arg_idx] = arg_val;
            arg_idx += 1;
        }

        const result = try self.func().emitCall(mangled_name, args, func_info.return_type);

        // Return appropriate value
        if (sret_buffer) |buf| {
            if (returns_optional) {
                return .{ .value = buf, .ty = func_info.return_value_type.? };
            }
            return .{ .value = buf, .ty = .{ .struct_type = func_info.return_type_name.? } };
        }
        const ret_ty: ValueType = if (func_info.return_value_type) |vt|
            vt
        else if (func_info.return_type_name) |name|
            .{ .struct_type = name }
        else
            .{ .primitive = func_info.return_type.toMaxonName() };

        return .{ .value = result orelse try self.func().emitConstI64(0), .ty = ret_ty };
    }

    /// Call a method on an already-converted TypedValue (used for for-in loop desugaring)
    fn convertMethodCallOnTyped(self: *AstToIr, base_typed: TypedValue, method_name: []const u8, arg_exprs: []const ast.Expression) ConvertError!TypedValue {
        // Get the type name from the TypedValue
        const type_name = switch (base_typed.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type => {
                debug.astToIr("error: method call on non-struct type", .{});
                self.reportError(.E003, method_name);
                return error.SemanticError;
            },
        };

        return self.emitMethodCall(type_name, method_name, arg_exprs, base_typed.value);
    }

    fn convertMethodCallExpr(self: *AstToIr, mcall: ast.MethodCallExpr) ConvertError!TypedValue {
        // Check if base is a type name (static method call: TypeName.method())
        if (mcall.base.* == .identifier) {
            const base_name = mcall.base.identifier;
            if (self.type_map.contains(base_name)) {
                return self.emitMethodCall(base_name, mcall.method_name, mcall.args, null);
            }
        }

        // Convert base and get its type (instance method call)
        const base_typed = try self.convertExpression(mcall.base.*);

        // Check if this is a struct type with methods
        if (base_typed.ty == .struct_type) {
            return self.emitMethodCall(base_typed.ty.struct_type, mcall.method_name, mcall.args, base_typed.value);
        }

        // Check if this is a primitive type with hash/equals methods
        if (base_typed.ty == .primitive) {
            return self.emitPrimitiveMethodCall(base_typed, mcall.method_name, mcall.args);
        }

        // Check if this is an array type - route to Array$ElementType methods
        if (base_typed.ty == .array_type) {
            const arr_info = base_typed.ty.array_type;
            // Get element type name: use struct name if available, else use ir.Type.toMaxonName()
            const elem_name = arr_info.element_struct_type orelse arr_info.element_type.toMaxonName();
            const array_type_name = try std.fmt.allocPrint(self.allocator, "Array${s}", .{elem_name});
            try self.module.trackString(array_type_name);

            // Trigger monomorphization to create Array$ElementType and its methods
            _ = self.getOrCreateMonomorphizedType("Array", &[_][]const u8{elem_name}) catch |e| {
                debug.astToIr("note: could not monomorphize Array${s}: {}", .{ elem_name, e });
            };
            return self.emitMethodCall(array_type_name, mcall.method_name, mcall.args, base_typed.value);
        }

        debug.astToIr("error: method call on non-struct type", .{});
        self.reportError(.E003, mcall.method_name);
        return error.SemanticError;
    }

    /// Emit a method call on a primitive type (hash, equals)
    fn emitPrimitiveMethodCall(self: *AstToIr, base_typed: TypedValue, method_name: []const u8, arg_exprs: []const ast.Expression) ConvertError!TypedValue {
        const type_name = base_typed.ty.primitive;

        if (std.mem.eql(u8, method_name, "hash")) {
            return self.emitPrimitiveHash(base_typed.value, type_name);
        } else if (std.mem.eql(u8, method_name, "equals")) {
            if (arg_exprs.len != 1) {
                self.reportError(.E011, "equals() requires exactly 1 argument");
                return error.WrongArgumentCount;
            }
            const other = try self.convertExpression(arg_exprs[0]);
            return self.emitPrimitiveEquals(base_typed.value, other.value, type_name);
        }

        debug.astToIr("error: unknown method '{s}' on primitive type '{s}'", .{ method_name, type_name });
        self.reportError(.E003, method_name);
        return error.SemanticError;
    }

    /// Emit hash() for primitive types
    fn emitPrimitiveHash(self: *AstToIr, value: ir.Value, type_name: []const u8) ConvertError!TypedValue {
        if (std.mem.eql(u8, type_name, "int")) {
            // int.hash() returns the value directly
            return .{ .value = value, .ty = .{ .primitive = "int" } };
        } else if (std.mem.eql(u8, type_name, "float")) {
            // float.hash() - normalize -0.0 to +0.0, then bitcast to i64
            // Following Swift's approach: if isZero, use 0, else use bitPattern
            const zero = try self.func().emitConstF64(0.0);
            const is_zero = try self.func().emitBinaryOp(.fcmp_eq, value, zero, .i64);

            // Allocate result storage
            const result_ptr = try self.func().emitAlloca(.i64);

            // Entry block is the current last block (before we add new blocks)
            const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

            // Create blocks for branching
            const zero_block_idx: u32 = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("float_hash_zero");
            const nonzero_block_idx: u32 = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("float_hash_nonzero");
            const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("float_hash_end");

            // Branch based on is_zero (from entry block)
            const entry_block = &self.func().blocks.items[entry_block_idx];
            try entry_block.instructions.append(self.allocator, .{
                .op = .br_cond,
                .operands = .{ .{ .value = is_zero }, .{ .block_ref = zero_block_idx }, .{ .block_ref = nonzero_block_idx } },
            });

            // Pop end_block temporarily so we can work with zero/nonzero blocks
            const end_block = self.func().blocks.pop().?;
            const nonzero_block = self.func().blocks.pop().?;

            // Now current block is zero_block
            // Zero case: store 0
            const zero_i64 = try self.func().emitConstI64(0);
            try self.func().emitStore(result_ptr, zero_i64);
            try self.func().emitBr(end_block_idx);

            // Restore nonzero block and work with it
            try self.func().blocks.append(self.allocator, nonzero_block);

            // Non-zero case: bitcast float to i64
            const bitcast_value = try self.func().emitUnaryOp(.bitcast_f64_to_i64, value, .i64);
            try self.func().emitStore(result_ptr, bitcast_value);
            try self.func().emitBr(end_block_idx);

            // Restore end block
            try self.func().blocks.append(self.allocator, end_block);

            // End block: load result
            const result = try self.func().emitLoad(result_ptr, .i64);

            return .{ .value = result, .ty = .{ .primitive = "int" } };
        } else if (std.mem.eql(u8, type_name, "bool")) {
            // bool.hash() returns 1 for true, 0 for false (value is already 0 or 1)
            return .{ .value = value, .ty = .{ .primitive = "int" } };
        } else if (std.mem.eql(u8, type_name, "byte")) {
            // byte.hash() returns the value directly
            return .{ .value = value, .ty = .{ .primitive = "int" } };
        }

        debug.astToIr("error: hash not supported for type '{s}'", .{type_name});
        self.reportError(.E003, type_name);
        return error.SemanticError;
    }

    /// Emit equals() for primitive types
    fn emitPrimitiveEquals(self: *AstToIr, left: ir.Value, right: ir.Value, type_name: []const u8) ConvertError!TypedValue {
        if (std.mem.eql(u8, type_name, "int") or std.mem.eql(u8, type_name, "bool") or std.mem.eql(u8, type_name, "byte")) {
            // Integer comparison
            const result = try self.func().emitBinaryOp(.icmp_eq, left, right, .i64);
            return .{ .value = result, .ty = .{ .primitive = "bool" } };
        } else if (std.mem.eql(u8, type_name, "float")) {
            // Float comparison (follows IEEE semantics: NaN != NaN)
            const result = try self.func().emitBinaryOp(.fcmp_eq, left, right, .i64);
            return .{ .value = result, .ty = .{ .primitive = "bool" } };
        }

        debug.astToIr("error: equals not supported for type '{s}'", .{type_name});
        self.reportError(.E003, type_name);
        return error.SemanticError;
    }
};

// ============================================================================
// Public API
// ============================================================================

pub fn convert(program: ast.Program, allocator: std.mem.Allocator, mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer) !ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    defer converter.deinit();
    return try converter.convert(program);
}

pub fn convertWithFile(program: ast.Program, allocator: std.mem.Allocator, mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer, source_file: ?[]const u8, out_error: *?err.CompileError) ConvertError!ir.Module {
    return convertWithExternals(program, allocator, mutation_analyzer, source_file, null, out_error);
}

/// Convert AST to IR with external function signatures from other modules
pub fn convertWithExternals(
    program: ast.Program,
    allocator: std.mem.Allocator,
    mutation_analyzer: ?*const mutation_analysis.MutationAnalyzer,
    source_file: ?[]const u8,
    external_funcs: ?[]const ExternalFuncSignature,
    external_types: ?[]const ExternalTypeInfo,
    out_error: *?err.CompileError,
) ConvertError!ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    converter.source_file = source_file;
    defer converter.deinit();

    // Register external types before conversion
    if (external_types) |ext_types| {
        for (ext_types) |ext_type| {
            try converter.registerExternalType(ext_type);
        }
    }

    // Register external function signatures before conversion
    if (external_funcs) |funcs| {
        for (funcs) |ext_func| {
            // Skip if already registered (avoid duplicates from multiple source files)
            if (converter.func_map.contains(ext_func.name)) continue;

            // Make our own copy of param_types so AstToIr owns all param_types in func_map
            const param_types_copy = try allocator.dupe(ParamType, ext_func.param_types);

            try converter.func_map.put(allocator, ext_func.name, .{
                .return_type = ext_func.return_type,
                .return_type_name = ext_func.return_type_name,
                .return_value_type = ext_func.return_value_type,
                .param_types = param_types_copy,
            });
            debug.astToIr("Registered external function '{s}' returning {s}", .{ ext_func.name, ext_func.return_type.toIrName() });
        }
    }

    const result = converter.convert(program) catch |e| {
        out_error.* = converter.last_error;
        return e;
    };
    return result;
}

/// Extract function signatures from an IR module (for cross-module compilation)
pub fn extractFunctionSignatures(module: *const ir.Module, allocator: std.mem.Allocator) ![]ExternalFuncSignature {
    var signatures = std.ArrayListUnmanaged(ExternalFuncSignature){};
    errdefer signatures.deinit(allocator);

    for (module.functions.items) |func| {
        try signatures.append(allocator, .{
            .name = func.name,
            .return_type = func.return_type,
        });
    }

    return signatures.toOwnedSlice(allocator);
}

/// Extract type info from a parsed program (for cross-module compilation)
pub fn extractTypeInfo(program: ast.Program, allocator: std.mem.Allocator) ![]ExternalTypeInfo {
    var type_infos = std.ArrayListUnmanaged(ExternalTypeInfo){};
    errdefer type_infos.deinit(allocator);

    for (program.types) |*type_decl| {
        var fields = std.ArrayListUnmanaged(ExternalFieldInfo){};
        errdefer fields.deinit(allocator);

        // Calculate fields and offsets
        var offset: i32 = 0;
        for (type_decl.fields) |field| {
            const type_name: []const u8 = switch (field.type_expr) {
                .simple => |name| name,
                .generic => |gen| gen.base_type, // Use base type for generics
                .optional => "ptr", // Optionals stored as pointers
            };

            // Most types are 8 bytes (i64, f64, ptr)
            // __ManagedArray is a special compiler-internal type that is 24 bytes
            // __ManagedString is a special compiler-internal type that is 24 bytes
            const field_size: i32 = if (std.mem.eql(u8, type_name, "__ManagedArray"))
                24
            else if (std.mem.eql(u8, type_name, "__ManagedString"))
                24
            else
                8;

            try fields.append(allocator, .{
                .name = field.name,
                .offset = offset,
                .size = field_size,
                .type_name = type_name,
            });

            offset += field_size;
        }

        // Include type declaration for all types (needed for conformance checks and monomorphization)
        const decl_ptr: ?*const ast.TypeDecl = type_decl;

        try type_infos.append(allocator, .{
            .name = type_decl.name,
            .size = offset,
            .fields = try fields.toOwnedSlice(allocator),
            .type_decl = decl_ptr,
            .is_exported = type_decl.is_export,
        });
    }

    return type_infos.toOwnedSlice(allocator);
}

/// Extract function signatures from a parsed program (for cross-module compilation)
/// This includes methods from type declarations
pub fn extractFunctionSignaturesFromAst(program: ast.Program, allocator: std.mem.Allocator) ![]ExternalFuncSignature {
    var signatures = std.ArrayListUnmanaged(ExternalFuncSignature){};
    errdefer {
        // Free allocated names and param_types in case of error
        for (signatures.items) |sig| {
            allocator.free(sig.name);
            if (sig.param_types.len > 0) {
                allocator.free(sig.param_types);
            }
        }
        signatures.deinit(allocator);
    }

    // Extract standalone functions
    for (program.functions) |func| {
        const return_type: ir.Type = if (func.return_type) |rt|
            getIrTypeFromTypeExpr(rt)
        else
            .void;

        const return_type_name: ?[]const u8 = if (func.return_type) |rt|
            getStructNameFromTypeExpr(rt)
        else
            null;

        const return_value_type: ?ValueType = if (func.return_type) |rt|
            getValueTypeFromTypeExpr(rt)
        else
            null;

        // Extract parameter types
        const param_types = try allocator.alloc(ParamType, func.params.len);
        for (func.params, 0..) |param, i| {
            param_types[i] = .{
                .ty = getValueTypeFromTypeExprForParam(param.type_expr),
            };
        }

        // Allocate copy of name for uniform ownership (all names are owned)
        const name_copy = try allocator.dupe(u8, func.name);

        try signatures.append(allocator, .{
            .name = name_copy,
            .return_type = return_type,
            .return_type_name = return_type_name,
            .return_value_type = return_value_type,
            .is_exported = func.is_export,
            .param_types = param_types,
        });
    }

    // Extract methods from types
    for (program.types) |type_decl| {
        for (type_decl.methods) |method| {
            const mangled_name = try std.fmt.allocPrint(allocator, "{s}${s}", .{ type_decl.name, method.name });

            const return_type: ir.Type = if (method.return_type) |rt|
                getIrTypeFromTypeExpr(rt)
            else
                .void;

            // For methods returning the containing type (like init() -> Self)
            var return_type_name: ?[]const u8 = if (method.return_type) |rt|
                getStructNameFromTypeExpr(rt)
            else
                null;

            // Handle "Self" return type
            if (return_type_name) |name| {
                if (std.mem.eql(u8, name, "Self")) {
                    return_type_name = type_decl.name;
                }
            }

            const return_value_type: ?ValueType = if (method.return_type) |rt|
                getValueTypeFromTypeExpr(rt)
            else
                null;

            // Extract parameter types for methods
            const param_types = try allocator.alloc(ParamType, method.params.len);
            for (method.params, 0..) |param, i| {
                param_types[i] = .{
                    .ty = getValueTypeFromTypeExprForParam(param.type_expr),
                };
            }

            // Methods inherit export status from their containing type
            try signatures.append(allocator, .{
                .name = mangled_name,
                .return_type = return_type,
                .return_type_name = return_type_name,
                .return_value_type = return_value_type,
                .is_exported = type_decl.is_export or method.is_export,
                .param_types = param_types,
            });
        }
    }

    return signatures.toOwnedSlice(allocator);
}

/// Helper: get IR type from TypeExpr (simplified)
fn getIrTypeFromTypeExpr(te: ast.TypeExpr) ir.Type {
    return switch (te) {
        .simple => |name| {
            if (std.mem.eql(u8, name, "int")) return .i64;
            if (std.mem.eql(u8, name, "float")) return .f64;
            if (std.mem.eql(u8, name, "bool")) return .i64;
            if (std.mem.eql(u8, name, "string")) return .ptr;
            if (std.mem.eql(u8, name, "character")) return .i64;
            if (std.mem.eql(u8, name, "byte")) return .i64;
            // Struct types return ptr
            return .ptr;
        },
        .generic => .ptr,
        .optional => .ptr,
    };
}

/// Helper: get ValueType from TypeExpr (for optional/array types)
fn getValueTypeFromTypeExpr(te: ast.TypeExpr) ?ValueType {
    return switch (te) {
        .optional => |wrapped| {
            const wrapped_ir_type = getIrTypeFromTypeExpr(wrapped.*);
            // Also get the struct name for struct types
            const wrapped_struct_name = getStructNameFromTypeExpr(wrapped.*);
            return .{
                .optional_type = .{
                    .wrapped = wrapped_ir_type,
                    .wrapped_struct_type = wrapped_struct_name,
                },
            };
        },
        .simple, .generic => null,
    };
}

/// Helper: get struct name from TypeExpr if it's a struct type
fn getStructNameFromTypeExpr(te: ast.TypeExpr) ?[]const u8 {
    return switch (te) {
        .simple => |name| {
            // Known primitives don't have struct names
            if (std.mem.eql(u8, name, "int") or
                std.mem.eql(u8, name, "float") or
                std.mem.eql(u8, name, "bool") or
                std.mem.eql(u8, name, "string") or
                std.mem.eql(u8, name, "character") or
                std.mem.eql(u8, name, "byte"))
            {
                return null;
            }
            return name;
        },
        .generic => |gen| gen.base_type,
        .optional => null,
    };
}

/// Helper: get ValueType from TypeExpr for function parameters
fn getValueTypeFromTypeExprForParam(te: ast.TypeExpr) ValueType {
    return switch (te) {
        .simple => |name| .{ .primitive = name },
        .generic => |gen| .{ .struct_type = gen.base_type },
        .optional => |wrapped| .{
            .optional_type = .{
                .wrapped = getIrTypeFromTypeExpr(wrapped.*),
                .wrapped_struct_type = getStructNameFromTypeExpr(wrapped.*),
            },
        },
    };
}
