const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const semantic_analysis = @import("3-semantic_analysis.zig");
const err = @import("error.zig");

// Import types from dedicated types module
const types = @import("ast_to_ir_types.zig");

// Import intrinsics module
const intrinsics = @import("ast_to_ir_intrinsics.zig");

// Import shared struct helpers
const struct_helpers = @import("ir_struct_helpers.zig");
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
pub const ExternalInterfaceInfo = types.ExternalInterfaceInfo;
pub const ParamType = types.ParamType;
pub const OwnershipState = types.OwnershipState;
pub const VarInfo = types.VarInfo;
pub const ConvertError = types.ConvertError;
pub const InterfaceInfo = types.InterfaceInfo;
pub const PendingMethod = types.PendingMethod;
pub const SemanticInfo = types.SemanticInfo;
pub const SemanticVarInfo = types.SemanticVarInfo;

// ============================================================================
// AST ChildBlock Helper Functions
// ============================================================================

/// Get the primary body statements from children (role = .primary)
fn getPrimaryBody(children: []const ast.ChildBlock) []const ast.Statement {
    for (children) |child| {
        if (child.role == .primary) return child.statements;
    }
    return &.{};
}

/// Get the else body statements if present (role = .else_clause)
fn getElseBody(children: []const ast.ChildBlock) ?[]const ast.Statement {
    for (children) |child| {
        if (child.role == .else_clause) return child.statements;
    }
    return null;
}

/// Get the else-if statement if present (role = .else_if)
/// The else-if is stored as a single if_stmt in the child's statements
fn getElseIf(children: []const ast.ChildBlock) ?ast.IfStmt {
    for (children) |child| {
        if (child.role == .else_if and child.statements.len > 0) {
            return child.statements[0].kind.if_stmt;
        }
    }
    return null;
}

/// Check if children contain an else clause or else-if
fn hasElse(children: []const ast.ChildBlock) bool {
    for (children) |child| {
        if (child.role == .else_clause or child.role == .else_if) return true;
    }
    return false;
}

/// Get the default case statements if present (role = .default_case)
fn getDefaultCase(children: []const ast.ChildBlock) ?[]const ast.Statement {
    for (children) |child| {
        if (child.role == .default_case) return child.statements;
    }
    return null;
}

/// Get the nil handler body (for guard-let, role = .nil_handler)
fn getNilHandler(children: []const ast.ChildBlock) []const ast.Statement {
    for (children) |child| {
        if (child.role == .nil_handler) return child.statements;
    }
    return &.{};
}

/// Get the default value body (for else-unwrap, role = .default_value)
fn getDefaultValueBody(children: []const ast.ChildBlock) []const ast.Statement {
    for (children) |child| {
        if (child.role == .default_value) return child.statements;
    }
    return &.{};
}

/// Check if match statement has a default case
fn hasDefaultCase(children: []const ast.ChildBlock) bool {
    for (children) |child| {
        if (child.role == .default_case) return true;
    }
    return false;
}

/// Count the number of match cases (not including default)
fn countMatchCases(children: []const ast.ChildBlock) usize {
    var count: usize = 0;
    for (children) |child| {
        if (child.role == .match_case) count += 1;
    }
    return count;
}

/// Count the number of catch handlers
fn countCatchHandlers(children: []const ast.ChildBlock) usize {
    var count: usize = 0;
    for (children) |child| {
        if (child.role == .catch_handler) count += 1;
    }
    return count;
}

/// Get the single statement body of a ChildBlock (for match cases)
fn getChildBody(child: ast.ChildBlock) ast.Statement {
    return child.statements[0];
}

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

/// Helper for managing deferred blocks in control flow structures.
/// Blocks are created, then popped to be restored later in the right order.
///
/// Usage:
/// 1. Create blocks with addBlock (they're added to the function's block list)
/// 2. Call deferBlocks(n) to pop n blocks (stores them internally)
/// 3. Do work in the current block
/// 4. Call restore(i) to append the i-th deferred block back (making it current)
/// 5. Repeat step 4 for remaining blocks in order
///
/// This replaces the manual pop().?/append() dance used throughout the codebase.
pub const DeferredBlocks = struct {
    blocks: []ir.BasicBlock,
    indices: []u32,
    allocator: std.mem.Allocator,
    count: usize,

    /// Create a DeferredBlocks container that can hold up to `max_blocks` deferred blocks.
    fn init(allocator: std.mem.Allocator, max_blocks: usize) !DeferredBlocks {
        const blocks = try allocator.alloc(ir.BasicBlock, max_blocks);
        errdefer allocator.free(blocks);
        const indices = try allocator.alloc(u32, max_blocks);
        return .{
            .blocks = blocks,
            .indices = indices,
            .allocator = allocator,
            .count = 0,
        };
    }

    /// Pop `n` blocks from the function's block list and store them.
    /// Blocks are stored in reverse order (last created first).
    fn deferBlocks(self: *DeferredBlocks, self_ir: *AstToIr, n: usize) void {
        var i: usize = 0;
        while (i < n) : (i += 1) {
            self.blocks[i] = self_ir.func().blocks.pop().?;
            // Index is calculated as: current length (after pop) + (n - 1 - i)
            // But we already popped, so it's the position where this block will be restored
            self.indices[i] = @intCast(self_ir.func().blocks.items.len + n - 1 - i);
        }
        self.count = n;
    }

    /// Get the block index for the i-th deferred block (0 = last popped, n-1 = first popped).
    fn idx(self: DeferredBlocks, n: usize) u32 {
        return self.indices[n];
    }

    /// Restore the i-th deferred block (0 = last popped = first to restore).
    /// This appends it back to the function's block list, making it the current block.
    fn restore(self: *DeferredBlocks, self_ir: *AstToIr, n: usize) !void {
        try self_ir.func().blocks.append(self_ir.allocator, self.blocks[n]);
    }

    /// Clean up allocated memory.
    fn deinit(self: *DeferredBlocks) void {
        self.allocator.free(self.blocks);
        self.allocator.free(self.indices);
    }
};

/// Builder for common 2-way conditional branching pattern.
/// Handles creating blocks, deferring them, emitting conditional branch, and restoring.
/// Pattern: entry -> (condition) -> then_block or else_block -> merge_block
pub const BranchBuilder = struct {
    deferred: DeferredBlocks,
    then_block_idx: u32,
    entry_block_idx: u32,
    condition: ir.Value,
    self_ir: *AstToIr,
    branch_emitted: bool,

    /// Create a 2-way branch from the current block.
    /// Creates then, else, and merge blocks, defers else and merge.
    /// Current block becomes the then block after this call.
    /// NOTE: The conditional branch is NOT emitted until switchToElse is called,
    /// to allow nested code to create blocks without invalidating indices.
    pub fn init(
        self_ir: *AstToIr,
        condition: ir.Value,
        then_name: []const u8,
        else_name: []const u8,
        merge_name: []const u8,
    ) !BranchBuilder {
        const entry_block_idx: u32 = @intCast(self_ir.func().blocks.items.len - 1);
        const then_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
        _ = try self_ir.func().addBlock(then_name);
        _ = try self_ir.func().addBlock(else_name);
        _ = try self_ir.func().addBlock(merge_name);

        // Defer merge and else blocks (merge=0, else=1 after pop order)
        var deferred = try DeferredBlocks.init(self_ir.allocator, 2);
        deferred.deferBlocks(self_ir, 2);

        // NOTE: Conditional branch is deferred until switchToElse to handle nested blocks

        return .{
            .deferred = deferred,
            .then_block_idx = then_block_idx,
            .entry_block_idx = entry_block_idx,
            .condition = condition,
            .self_ir = self_ir,
            .branch_emitted = false,
        };
    }

    /// Switch to the else block. Call this after generating then block code.
    /// Optionally emits a branch to merge from the then block.
    /// This also emits the deferred conditional branch from the entry block.
    pub fn switchToElse(self: *BranchBuilder, branch_to_merge: bool) !void {
        // Get the current block index (end of then/nested code) BEFORE restoring else
        const then_end_block_idx: u32 = @intCast(self.self_ir.func().blocks.items.len - 1);

        // Restore else block so we know its actual index
        try self.deferred.restore(self.self_ir, 1);
        const else_block_idx: u32 = @intCast(self.self_ir.func().blocks.items.len - 1);

        // Merge will be at current len after we restore it
        const merge_block_idx: u32 = @intCast(self.self_ir.func().blocks.items.len);

        if (branch_to_merge) {
            // Emit branch to merge from the end of the then block
            try self.self_ir.func().blocks.items[then_end_block_idx].instructions.append(self.self_ir.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
            });
        }

        // Now emit the conditional branch with correct else_block_idx
        if (!self.branch_emitted) {
            try self.self_ir.func().blocks.items[self.entry_block_idx].instructions.append(self.self_ir.allocator, .{
                .op = .br_cond,
                .operands = .{ .{ .value = self.condition }, .{ .block_ref = self.then_block_idx }, .{ .block_ref = else_block_idx } },
            });
            self.branch_emitted = true;
        }
    }

    /// Switch to the merge block. Call this after generating else block code.
    /// Optionally emits a branch to merge from the else block.
    pub fn switchToMerge(self: *BranchBuilder, branch_to_merge: bool) !void {
        // Merge will be at blocks.items.len after restore
        const merge_block_idx: u32 = @intCast(self.self_ir.func().blocks.items.len);

        if (branch_to_merge) {
            try self.self_ir.func().emitBr(merge_block_idx);
        }
        try self.deferred.restore(self.self_ir, 0);
    }

    /// Clean up. Must be called when done.
    pub fn deinit(self: *BranchBuilder) void {
        self.deferred.deinit();
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
    enum_decl_map: std.StringHashMapUnmanaged(ast.EnumDecl), // Enum declarations for conformance lookup
    current_decl_is_mutable: bool,
    mutation_analyzer: ?*const semantic_analysis.MutationAnalyzer,
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
    // Track function type allocations for cleanup (param_types slices and return_type pointers)
    function_type_allocs: std.ArrayListUnmanaged(FunctionTypeAlloc) = .{},
    // Track temporary string allocations during type checking (freed in deinit)
    allocated_type_strings: std.ArrayListUnmanaged([]const u8) = .{},
    // External type declarations pending conformance check (checked after interfaces are registered)
    pending_conformance_checks: std.ArrayListUnmanaged(*const ast.TypeDecl) = .{},
    // Error handling: the error type the current function throws (null if non-throwing)
    current_func_throws_type: ?[]const u8 = null,
    // Do-catch context: buffer to store caught error (null if not in do-catch)
    do_catch_error_buffer: ?ir.Value = null,
    // Do-catch context: block to jump to when an error is caught
    do_catch_catch_block: ?u32 = null,
    // Do-catch context: block after the entire do-catch (for successful completion)
    do_catch_end_block: ?u32 = null,
    // Do-catch context: error type name for panic message (tracked from try expressions)
    do_catch_error_type: ?[]const u8 = null,
    // Pending try error branches to patch (used when catch_dispatch index is determined late)
    pending_try_branches: ?*std.ArrayListUnmanaged(TryBranchInfo) = null,
    // Scope label tracking: stack of sets for each scope level (to detect duplicate block identifiers)
    scope_labels: std.ArrayListUnmanaged(std.StringHashMapUnmanaged(void)) = .{},
    // Global constants: evaluated compile-time constant values
    global_constants: std.StringHashMapUnmanaged(ConstantValue) = .{},
    // Global constant AST nodes (for forward reference resolution)
    global_constant_asts: std.StringHashMapUnmanaged(ast.GlobalConstant) = .{},
    // Set of constants currently being evaluated (for circular dependency detection)
    constants_in_progress: std.StringHashMapUnmanaged(void) = .{},
    // IR values for runtime-initialized constants (created once per function)
    converted_runtime_constants: std.StringHashMapUnmanaged(TypedValue) = .{},
    // Memory tracking option: emit track.move/track.incref/track.decref instructions
    track_memory: bool = false,

    /// A compile-time constant value
    pub const ConstantValue = union(enum) {
        int: i64,
        float: f64,
        bool: bool,
        string: []const u8,
        array: []const ConstantValue,
        enum_member: struct {
            enum_name: []const u8,
            member_name: []const u8,
            value: i64,
        },
        // For runtime-initialized constants (like maps), store the AST expression
        runtime_expr: ast.Expression,
    };

    /// Info about a branch instruction that needs to be patched to point to catch_dispatch
    const TryBranchInfo = struct {
        block_idx: u32, // Index of the block containing the branch
        instr_idx: usize, // Index of the instruction within the block
    };

    const FunctionTypeAlloc = union(enum) {
        param_types: []const ValueType,
        return_type: *const ValueType,
    };

    // ------------------------------------------------------------------------
    // Initialization / Cleanup
    // ------------------------------------------------------------------------

    pub fn init(allocator: std.mem.Allocator, mutation_analyzer: ?*const semantic_analysis.MutationAnalyzer) AstToIr {
        return .{
            .allocator = allocator,
            .module = ir.Module.init(allocator),
            .current_func = null,
            .var_map = .{},
            .type_map = .{},
            .func_map = .{},
            .interface_map = .{},
            .type_decl_map = .{},
            .enum_decl_map = .{},
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
            .message_allocated = true,
        };
    }

    /// Report an error at a specific source location
    pub fn reportErrorAt(self: *AstToIr, code: err.ErrorCode, details: []const u8, line: u32, column: u32) void {
        const base_msg = code.message();
        const formatted = std.fmt.allocPrint(self.allocator, "{s}: '{s}'", .{ base_msg, details }) catch {
            self.last_error = .{
                .code = code,
                .message = base_msg,
                .location = .{ .file = self.source_file, .line = line, .column = column },
            };
            return;
        };
        self.last_error = .{
            .code = code,
            .message = formatted,
            .location = .{ .file = self.source_file, .line = line, .column = column },
            .message_allocated = true,
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
    // COW String Helpers (delegates to ir_struct_helpers)
    // ------------------------------------------------------------------------

    /// Allocate a __ManagedArray on the stack and initialize it as empty.
    fn emitEmptyManagedArray(self: *AstToIr) !ir.Value {
        return struct_helpers.emitEmptyManagedArray(self.func());
    }

    /// Initialize an existing __ManagedArray as empty.
    fn initManagedArrayEmpty(self: *AstToIr, managed_ptr: ir.Value) !void {
        return struct_helpers.initManagedArrayEmpty(self.func(), managed_ptr);
    }

    /// Initialize an existing __ManagedArray with buffer, length, and capacity values.
    fn initManagedArray(self: *AstToIr, managed_ptr: ir.Value, buffer: ir.Value, len: ir.Value, capacity: ir.Value) !void {
        return struct_helpers.initManagedArray(self.func(), managed_ptr, buffer, len, capacity);
    }

    /// Allocate a __ManagedArray on the stack and initialize with buffer, length, and capacity.
    fn emitManagedArray(self: *AstToIr, buffer: ir.Value, len: ir.Value, capacity: ir.Value) !ir.Value {
        return struct_helpers.emitManagedArray(self.func(), buffer, len, capacity);
    }

    /// Create and initialize a __ManagedString on the stack from string bytes.
    /// Returns a pointer to the allocated struct.
    fn emitManagedStringFromBytes(self: *AstToIr, str_bytes: []const u8) !ir.Value {
        const managed_ptr = try self.func().emitAllocaSized(struct_helpers.MANAGED_STRING_SIZE);

        if (str_bytes.len == 0) {
            try struct_helpers.initManagedStringEmpty(self.func(), managed_ptr);
        } else {
            // Heap allocation for string data
            const buffer_size = try self.func().emitConstI64(@intCast(str_bytes.len + 1));
            const buffer = try self.func().emitHeapAlloc(buffer_size, "string buffer");

            // Store string bytes
            for (str_bytes, 0..) |byte, i| {
                const idx_val = try self.func().emitConstI64(@intCast(i));
                const byte_ptr = try self.func().emitGetElemPtr(buffer, idx_val, 1);
                try self.func().emitStoreI8(byte_ptr, try self.func().emitConstI8(byte));
            }
            // Null terminate
            const null_idx = try self.func().emitConstI64(@intCast(str_bytes.len));
            try self.func().emitStoreI8(try self.func().emitGetElemPtr(buffer, null_idx, 1), try self.func().emitConstI8(0));

            // Initialize __ManagedString fields with refcount=0
            // Note: refcount starts at 0, not 1. When this is passed to String$init,
            // the struct copy will increment refcount to 1. This ensures proper COW semantics.
            try self.func().emitStore(managed_ptr, buffer);
            const zero_i32 = try self.func().emitConstI32(0);
            try struct_helpers.storeI32Field(self.func(), managed_ptr, 8, try self.func().emitConstI32(@intCast(str_bytes.len)));
            try struct_helpers.storeI32Field(self.func(), managed_ptr, 12, try self.func().emitConstI32(@intCast((str_bytes.len << 2) | 0b01)));
            try struct_helpers.storeI32Field(self.func(), managed_ptr, 16, zero_i32);
            try struct_helpers.storeI32Field(self.func(), managed_ptr, 20, zero_i32);
        }

        return managed_ptr;
    }

    /// Create and initialize a __ManagedString from a runtime buffer pointer and length.
    /// Used for runtime string conversions (int to string, float to string, etc.)
    fn emitManagedStringFromBuffer(self: *AstToIr, buffer: ir.Value, len_i64: ir.Value) !ir.Value {
        return struct_helpers.emitManagedString(self.func(), buffer, len_i64);
    }

    /// Wrap a __ManagedString pointer in a full String struct (adds iter_pos field).
    pub fn emitStringFromManaged(self: *AstToIr, managed_ptr: ir.Value) !ir.Value {
        const string_ptr = try self.func().emitAllocaSized(32);
        try self.func().emitMemcpy(string_ptr, managed_ptr, struct_helpers.MANAGED_STRING_SIZE);
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
                try self.emitStringIncref(dest_ptr, "<struct copy>");
            }
        }
    }

    /// Check if a string is in heap mode by examining cap_flags.
    /// Returns an IR value representing the boolean result (cap_flags & 0x3 == 1).
    /// String layout: _managed at offset 0, cap_flags at offset 12 within _managed.
    fn emitStringIsHeapMode(self: *AstToIr, string_ptr: ir.Value) !ir.Value {
        const cap_ptr = try self.func().emitGetFieldPtr(string_ptr, 12);
        const cap_flags = try self.func().emitLoad(cap_ptr, .i32);
        const three = try self.func().emitConstI32(3);
        const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
        const one_i32 = try self.func().emitConstI32(1);
        return try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);
    }

    /// Emit incref for a String variable if it's in heap mode.
    /// The string_ptr should point to a String struct (with _managed at offset 0).
    /// tag is used for memory tracking (variable name or description).
    pub fn emitStringIncref(self: *AstToIr, string_ptr: ir.Value, tag: []const u8) !void {
        const is_heap = try self.emitStringIsHeapMode(string_ptr);

        // Create blocks for conditional incref
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("incref");
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("incref_end");

        // Defer end block
        var deferred = try DeferredBlocks.init(self.allocator, 1);
        defer deferred.deinit();
        deferred.deferBlocks(self, 1);

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

        // Emit tracking call for incref (new_ref is the refcount after increment)
        if (self.track_memory) {
            try self.func().emitTrackIncref(tag, new_ref);
        }

        // Branch to end
        try self.func().emitBr(end_block_idx);

        // Restore end block
        try deferred.restore(self, 0);
    }

    /// Emit decref for a String variable with conditional free when refcount reaches 0.
    /// The string_ptr should point to a String struct (with _managed at offset 0).
    /// tag is used for memory tracking (variable name or description).
    fn emitStringDecref(self: *AstToIr, string_ptr: ir.Value, tag: []const u8) !void {
        const is_heap = try self.emitStringIsHeapMode(string_ptr);

        // Create blocks for conditional decref
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("decref");
        const free_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("decref_free");
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("decref_end");

        debug.astToIr("emitStringDecref: entry={d}, decref={d}, free={d}, end={d}", .{ entry_block_idx, decref_block_idx, free_block_idx, end_block_idx });

        // Defer end and free blocks (end=0, free=1 after pop order)
        var deferred = try DeferredBlocks.init(self.allocator, 2);
        defer deferred.deinit();
        deferred.deferBlocks(self, 2);

        // Emit conditional branch from entry block to decref or end
        debug.astToIr("  br_cond in block {d}: if heap -> {d}, else -> {d}", .{ entry_block_idx, decref_block_idx, end_block_idx });
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

        // Emit tracking call for decref (new_ref is the refcount after decrement)
        if (self.track_memory) {
            try self.func().emitTrackDecref(tag, new_ref);
        }

        // Check if refcount is now zero
        const zero = try self.func().emitConstI32(0);
        const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero, .i32);

        // Restore free block (index 1 = second popped = free_block)
        try deferred.restore(self, 1);

        // Emit branch from decref block to free or end
        debug.astToIr("  br_cond in block {d}: if zero -> {d}, else -> {d}", .{ decref_block_idx, free_block_idx, end_block_idx });
        try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_zero }, .{ .block_ref = free_block_idx }, .{ .block_ref = end_block_idx } },
        });

        // In free block: load buffer pointer and free it
        const buf_ptr = try self.func().emitLoad(string_ptr, .ptr);
        try self.func().emitHeapFree(buf_ptr, "string cleanup");

        // Branch to end
        debug.astToIr("  br in block {d}: -> {d}", .{ free_block_idx, end_block_idx });
        try self.func().emitBr(end_block_idx);

        // Restore end block (index 0 = first popped = end_block)
        try deferred.restore(self, 0);
        debug.astToIr("  restored end block, current block is now {d}", .{self.func().blocks.items.len - 1});
    }

    /// Emit cleanup for all String elements in an Array$String.
    /// Iterates through each element and decrefs its buffer using COW semantics.
    /// array_ptr points to an Array$String struct (with __ManagedArray at offset 0).
    fn emitArrayStringElementsCleanup(self: *AstToIr, array_ptr: ir.Value) !void {
        // Array$String layout: __ManagedArray (24 bytes) + iterIndex (8 bytes)
        // __ManagedArray layout: buffer_ptr (8) + length (8) + capacity (8)
        // String layout: __ManagedString (24 bytes) + _iterPos (8 bytes) = 32 bytes per element
        // __ManagedString layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)

        // Load length first to check if empty
        const len_ptr = try self.func().emitGetFieldPtr(array_ptr, 8);
        const len = try self.func().emitLoad(len_ptr, .i64);

        // Skip cleanup if empty
        const zero = try self.func().emitConstI64(0);
        const is_empty = try self.func().emitBinaryOp(.icmp_eq, len, zero, .i64);

        // Allocate loop counter on stack (BEFORE creating new blocks - still in entry block)
        const counter_ptr = try self.func().emitAlloca(.i64);
        try self.func().emitStore(counter_ptr, zero);

        // Create blocks: entry -> cond -> body -> decref -> heap_free -> next -> end
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("arr_str_cleanup_cond");
        const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("arr_str_cleanup_body");
        const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("arr_str_cleanup_decref");
        const heap_free_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("arr_str_cleanup_heap_free");
        const next_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("arr_str_cleanup_next");
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("arr_str_cleanup_end");

        // Branch from entry: if empty, skip to end; otherwise go to condition
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_empty }, .{ .block_ref = end_block_idx }, .{ .block_ref = cond_block_idx } },
        });

        // Defer blocks in reverse order: end(0), next(1), heap_free(2), decref(3), body(4)
        var deferred = try DeferredBlocks.init(self.allocator, 5);
        defer deferred.deinit();
        deferred.deferBlocks(self, 5);

        // Condition block: reload len and check if i < len
        const cond_len_ptr = try self.func().emitGetFieldPtr(array_ptr, 8);
        const cond_len = try self.func().emitLoad(cond_len_ptr, .i64);
        const cond_i = try self.func().emitLoad(counter_ptr, .i64);
        const done = try self.func().emitBinaryOp(.icmp_ge, cond_i, cond_len, .i64);

        // Branch from cond: if done, go to end; otherwise go to body
        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = done }, .{ .block_ref = end_block_idx }, .{ .block_ref = body_block_idx } },
        });

        // Restore body block to emit loop body
        try deferred.restore(self, 4);

        // Body block: calculate element pointer and check if heap mode
        const body_buf_ptr = try self.func().emitLoad(array_ptr, .ptr);
        const body_i = try self.func().emitLoad(counter_ptr, .i64);

        // Calculate element pointer: buf_ptr + i * 32 (String size)
        const elem_size = try self.func().emitConstI64(32);
        const offset = try self.func().emitBinaryOp(.mul, body_i, elem_size, .i64);
        const elem_ptr = try self.func().emitBinaryOp(.add, body_buf_ptr, offset, .ptr);

        // Check if heap mode: cap_flags & 3 == 1
        // cap_flags is at offset 12 in the String (which contains __ManagedString at offset 0)
        const cap_flags_ptr = try self.func().emitGetFieldPtr(elem_ptr, 12);
        const cap_flags = try self.func().emitLoad(cap_flags_ptr, .i32);
        const three = try self.func().emitConstI32(3);
        const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
        const one_i32 = try self.func().emitConstI32(1);
        const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

        // Branch: if heap mode, go to decref block; otherwise go to next
        try self.func().blocks.items[body_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_heap }, .{ .block_ref = decref_block_idx }, .{ .block_ref = next_block_idx } },
        });

        // Restore decref block
        try deferred.restore(self, 3);

        // Decref block: decrement refcount and check if zero
        const decref_buf_ptr = try self.func().emitLoad(array_ptr, .ptr);
        const decref_i = try self.func().emitLoad(counter_ptr, .i64);
        const decref_offset = try self.func().emitBinaryOp(.mul, decref_i, elem_size, .i64);
        const decref_elem_ptr = try self.func().emitBinaryOp(.add, decref_buf_ptr, decref_offset, .ptr);

        // Refcount is at offset 16 in the String
        const ref_ptr = try self.func().emitGetFieldPtr(decref_elem_ptr, 16);
        const old_ref = try self.func().emitLoad(ref_ptr, .i32);
        const one_ref = try self.func().emitConstI32(1);
        const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one_ref, .i32);
        try self.func().emitStoreI32(ref_ptr, new_ref);

        // Emit tracking call for decref
        if (self.track_memory) {
            try self.func().emitTrackDecref("<array element>", new_ref);
        }

        // Check if refcount is now zero
        const zero_ref = try self.func().emitConstI32(0);
        const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero_ref, .i32);

        // Branch: if zero, go to heap_free; otherwise go to next
        try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_zero }, .{ .block_ref = heap_free_block_idx }, .{ .block_ref = next_block_idx } },
        });

        // Restore heap_free block
        try deferred.restore(self, 2);

        // Heap free block: reload element ptr and free its buffer
        const free_buf_ptr = try self.func().emitLoad(array_ptr, .ptr);
        const free_i = try self.func().emitLoad(counter_ptr, .i64);
        const free_offset = try self.func().emitBinaryOp(.mul, free_i, elem_size, .i64);
        const free_elem_ptr = try self.func().emitBinaryOp(.add, free_buf_ptr, free_offset, .ptr);
        const str_buf_ptr = try self.func().emitLoad(free_elem_ptr, .ptr);
        try self.func().emitHeapFree(str_buf_ptr, "string cleanup");

        // Branch to next
        try self.func().emitBr(next_block_idx);

        // Restore next block
        try deferred.restore(self, 1);

        // Next block: increment counter and loop back
        const one = try self.func().emitConstI64(1);
        const curr_i = try self.func().emitLoad(counter_ptr, .i64);
        const next_i = try self.func().emitBinaryOp(.add, curr_i, one, .i64);
        try self.func().emitStore(counter_ptr, next_i);

        // Branch back to condition
        try self.func().emitBr(cond_block_idx);

        // Switch to end block for subsequent code
        try deferred.restore(self, 0);
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
        try self.func().emitHeapFree(buf_ptr, "string cleanup");

        const skip_decref_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_skip_decref");

        const free_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("cstr_free");

        // === FREE BLOCK: managed == null, free the data pointer ===
        const data_ptr = try self.func().emitLoad(cstring_ptr, .ptr);
        try self.func().emitHeapFree(data_ptr, "cstring release");

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
            try self.emitStringDecref(str_ptr, "<temp>");
        }
        self.temporary_strings.clearRetainingCapacity();
    }

    /// Remove a specific value from the temporary strings list (used when assigned to a variable
    /// or when ownership transfers to an array via __managed_array_set_at)
    pub fn removeFromTemporaries(self: *AstToIr, value: ir.Value) void {
        var i: usize = 0;
        while (i < self.temporary_strings.items.len) {
            if (self.temporary_strings.items[i] == value) {
                _ = self.temporary_strings.swapRemove(i);
            } else {
                i += 1;
            }
        }
    }

    /// Check if a value is in the temporary strings list
    fn isInTemporaries(self: *AstToIr, value: ir.Value) bool {
        for (self.temporary_strings.items) |temp| {
            if (temp == value) return true;
        }
        return false;
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
                    .message_allocated = true,
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
                    .message_allocated = true,
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

        // Free temporary type strings allocated during type checking
        for (self.allocated_type_strings.items) |str| {
            self.allocator.free(str);
        }
        self.allocated_type_strings.deinit(self.allocator);

        var type_iter = self.type_map.iterator();
        while (type_iter.next()) |entry| {
            switch (entry.value_ptr.*) {
                .struct_type => |s| self.allocator.free(s.fields),
                .enum_type => |*e| {
                    e.members.deinit(self.allocator);
                    e.case_info.deinit(self.allocator);
                },
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

        // Enum decl map doesn't own its data (references AST nodes)
        self.enum_decl_map.deinit(self.allocator);

        // Generic params map doesn't own its data (references AST strings)
        self.generic_params.deinit(self.allocator);

        // Clean up labeled loops stack
        self.labeled_loops.deinit(self.allocator);

        // Clean up scope labels stack
        for (self.scope_labels.items) |*scope_set| {
            scope_set.deinit(self.allocator);
        }
        self.scope_labels.deinit(self.allocator);

        // Clean up pending_methods generic_bindings (shared across methods of same type)
        var freed_bindings = std.AutoHashMapUnmanaged([*]const PendingMethod.GenericBinding, void){};
        defer freed_bindings.deinit(self.allocator);
        var pending_iter = self.pending_methods.iterator();
        while (pending_iter.next()) |entry| {
            if (entry.value_ptr.generic_bindings.len > 0) {
                const ptr = entry.value_ptr.generic_bindings.ptr;
                if (freed_bindings.get(ptr) == null) {
                    freed_bindings.put(self.allocator, ptr, {}) catch {};
                    self.allocator.free(entry.value_ptr.generic_bindings);
                }
            }
        }
        self.pending_methods.deinit(self.allocator);

        // Clean up methods_in_progress (usually empty, but clean up in case of error paths)
        self.methods_in_progress.deinit(self.allocator);

        // Clean up function type allocations tracked in function_type_allocs
        for (self.function_type_allocs.items) |alloc_info| {
            switch (alloc_info) {
                .param_types => |pt| self.allocator.free(pt),
                .return_type => |rt| self.allocator.destroy(rt),
            }
        }
        self.function_type_allocs.deinit(self.allocator);

        // Pending conformance checks list doesn't own its data (references AST nodes)
        self.pending_conformance_checks.deinit(self.allocator);

        // Clean up global constant-related maps
        self.global_constants.deinit(self.allocator);
        self.global_constant_asts.deinit(self.allocator);
        self.constants_in_progress.deinit(self.allocator);
        self.converted_runtime_constants.deinit(self.allocator);
    }

    // ------------------------------------------------------------------------
    // Scope Label Tracking (for duplicate block identifier detection)
    // ------------------------------------------------------------------------

    /// Push a new scope level for tracking block labels
    fn pushLabelScope(self: *AstToIr) !void {
        var new_scope = std.StringHashMapUnmanaged(void){};
        try self.scope_labels.append(self.allocator, new_scope);
        _ = &new_scope; // Avoid unused variable warning
    }

    /// Pop the current scope level
    fn popLabelScope(self: *AstToIr) void {
        if (self.scope_labels.items.len > 0) {
            var scope = self.scope_labels.pop().?;
            scope.deinit(self.allocator);
        }
    }

    /// Check if a label is already used at the current scope level.
    /// If not, register it. If duplicate, report error and return error.
    fn checkAndRegisterLabel(self: *AstToIr, label: ?[]const u8) !void {
        const lbl = label orelse return;
        if (self.scope_labels.items.len == 0) return;

        var current_scope = &self.scope_labels.items[self.scope_labels.items.len - 1];
        if (current_scope.get(lbl) != null) {
            // Duplicate label at same scope level
            self.reportError(.E036, lbl);
            return error.SemanticError;
        }
        try current_scope.put(self.allocator, lbl, {});
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

        // Check conformance for external types (registered before convert() was called)
        for (self.pending_conformance_checks.items) |type_decl| {
            try self.checkInterfaceConformance(type_decl.*);
        }
        self.pending_conformance_checks.clearRetainingCapacity();

        // Register declarations
        for (program.enums) |enum_decl| {
            try self.registerEnum(enum_decl);
        }
        for (program.types) |type_decl| try self.registerType(type_decl);
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

        // Register methods from enums
        for (program.enums) |enum_decl| {
            for (enum_decl.methods) |method| {
                try self.registerMethod(enum_decl.name, method);
            }
        }

        // Check interface conformance for all types
        for (program.types) |type_decl| {
            try self.checkInterfaceConformance(type_decl);
        }

        // Evaluate global constants before converting functions
        // This must happen after enums are registered so we can resolve enum member references
        try self.evaluateGlobalConstants(program.global_constants);

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

        // Convert methods from enums
        for (program.enums) |enum_decl| {
            for (enum_decl.methods) |method| {
                try self.convertMethod(enum_decl.name, method);
            }
        }

        // Transfer ownership of module
        var module = self.module;
        self.module = ir.Module.init(self.allocator);
        module.source_file = self.source_file;
        return module;
    }

    // ------------------------------------------------------------------------
    // Global Constant Evaluation
    // ------------------------------------------------------------------------

    /// Evaluate all global constants, handling forward references and detecting circular dependencies
    fn evaluateGlobalConstants(self: *AstToIr, constants: []const ast.GlobalConstant) ConvertError!void {
        // First, register all constant ASTs for forward reference resolution
        for (constants) |constant| {
            try self.global_constant_asts.put(self.allocator, constant.name, constant);
        }

        // Then evaluate each constant (will recursively evaluate dependencies)
        for (constants) |constant| {
            if (!self.global_constants.contains(constant.name)) {
                _ = try self.evaluateConstant(constant.name);
            }
        }
    }

    /// Evaluate a single constant by name, handling forward references
    fn evaluateConstant(self: *AstToIr, name: []const u8) ConvertError!ConstantValue {
        // Check if already evaluated
        if (self.global_constants.get(name)) |value| {
            return value;
        }

        // Check for circular dependency
        if (self.constants_in_progress.contains(name)) {
            // Build list of constants in the cycle for error message
            var cycle_names: std.ArrayListUnmanaged(u8) = .empty;
            defer cycle_names.deinit(self.allocator);
            var iter = self.constants_in_progress.iterator();
            var first = true;
            while (iter.next()) |entry| {
                if (!first) {
                    cycle_names.appendSlice(self.allocator, ", ") catch {};
                }
                first = false;
                cycle_names.appendSlice(self.allocator, entry.key_ptr.*) catch {};
            }
            const msg = std.fmt.allocPrint(self.allocator, "Circular dependency detected among global constants: {s}", .{cycle_names.items}) catch {
                self.reportError(.E005, name);
                return error.SemanticError;
            };
            self.last_error = .{
                .code = .E005,
                .message = msg,
                .location = .{
                    .file = self.source_file,
                    .line = @intCast(self.current_line),
                    .column = @intCast(self.current_column),
                },
            };
            return error.SemanticError;
        }

        // Get the constant AST
        const constant = self.global_constant_asts.get(name) orelse {
            self.reportError(.E005, name);
            return error.UndefinedVariable;
        };

        // Mark as in progress
        try self.constants_in_progress.put(self.allocator, name, {});
        defer _ = self.constants_in_progress.remove(name);

        // Track location for error reporting
        self.current_line = constant.line;
        self.current_column = constant.column;

        // Evaluate the expression
        const value = try self.evaluateConstantExpr(constant.value);

        // Store the result
        try self.global_constants.put(self.allocator, name, value);

        return value;
    }

    /// Evaluate a constant expression at compile time
    fn evaluateConstantExpr(self: *AstToIr, expr: ast.Expression) ConvertError!ConstantValue {
        return switch (expr) {
            .integer => |v| .{ .int = v },
            .float_lit => |v| .{ .float = v },
            .bool_lit => |v| .{ .bool = v },
            .string_literal => |s| .{ .string = s },
            .identifier => |name| {
                // Could be a reference to another constant
                if (self.global_constant_asts.contains(name)) {
                    return self.evaluateConstant(name);
                }
                // Unknown identifier in constant expression
                self.reportError(.E005, name);
                return error.UndefinedVariable;
            },
            .unary => |un| {
                const operand = try self.evaluateConstantExpr(un.operand.*);
                switch (un.op) {
                    .negate => {
                        if (operand == .int) {
                            return .{ .int = -operand.int };
                        } else if (operand == .float) {
                            return .{ .float = -operand.float };
                        }
                        self.reportError(.E003, "operand");
                        return error.SemanticError;
                    },
                    .not => {
                        if (operand == .bool) {
                            return .{ .bool = !operand.bool };
                        }
                        self.reportError(.E003, "operand");
                        return error.SemanticError;
                    },
                }
            },
            .binary => |bin| {
                const left = try self.evaluateConstantExpr(bin.left.*);
                const right = try self.evaluateConstantExpr(bin.right.*);

                // Handle integer operations
                if (left == .int and right == .int) {
                    const l = left.int;
                    const r = right.int;
                    return .{ .int = switch (bin.op) {
                        .add => l + r,
                        .sub => l - r,
                        .mul => l * r,
                        .div => @divTrunc(l, r),
                        .mod => @mod(l, r),
                        .band => l & r,
                        .bitor => l | r,
                        .bxor => l ^ r,
                        .shl => l << @intCast(r),
                        .shr => l >> @intCast(r),
                    } };
                }

                // Handle float operations
                if (left == .float and right == .float) {
                    const l = left.float;
                    const r = right.float;
                    return .{ .float = switch (bin.op) {
                        .add => l + r,
                        .sub => l - r,
                        .mul => l * r,
                        .div => l / r,
                        .mod, .band, .bitor, .bxor, .shl, .shr => {
                            self.reportError(.E003, "operand");
                            return error.SemanticError;
                        },
                    } };
                }

                // Mixed int/float - promote to float
                if ((left == .int and right == .float) or (left == .float and right == .int)) {
                    const l: f64 = if (left == .int) @floatFromInt(left.int) else left.float;
                    const r: f64 = if (right == .int) @floatFromInt(right.int) else right.float;
                    return .{ .float = switch (bin.op) {
                        .add => l + r,
                        .sub => l - r,
                        .mul => l * r,
                        .div => l / r,
                        .mod, .band, .bitor, .bxor, .shl, .shr => {
                            self.reportError(.E003, "operand");
                            return error.SemanticError;
                        },
                    } };
                }

                self.reportError(.E003, "operand");
                return error.SemanticError;
            },
            .compare => |cmp| {
                const left = try self.evaluateConstantExpr(cmp.left.*);
                const right = try self.evaluateConstantExpr(cmp.right.*);

                // Handle integer comparisons
                if (left == .int and right == .int) {
                    const l = left.int;
                    const r = right.int;
                    return .{ .bool = switch (cmp.op) {
                        .eq => l == r,
                        .ne => l != r,
                        .lt => l < r,
                        .le => l <= r,
                        .gt => l > r,
                        .ge => l >= r,
                    } };
                }

                // Handle float comparisons
                if (left == .float and right == .float) {
                    const l = left.float;
                    const r = right.float;
                    return .{ .bool = switch (cmp.op) {
                        .eq => l == r,
                        .ne => l != r,
                        .lt => l < r,
                        .le => l <= r,
                        .gt => l > r,
                        .ge => l >= r,
                    } };
                }

                // Handle bool comparisons
                if (left == .bool and right == .bool) {
                    const l = left.bool;
                    const r = right.bool;
                    return .{ .bool = switch (cmp.op) {
                        .eq => l == r,
                        .ne => l != r,
                        else => {
                            self.reportError(.E003, "operand");
                            return error.SemanticError;
                        },
                    } };
                }

                self.reportError(.E003, "operand");
                return error.SemanticError;
            },
            .logical => |log| {
                const left = try self.evaluateConstantExpr(log.left.*);
                const right = try self.evaluateConstantExpr(log.right.*);

                if (left == .bool and right == .bool) {
                    return .{ .bool = switch (log.op) {
                        .@"and" => left.bool and right.bool,
                        .@"or" => left.bool or right.bool,
                    } };
                }

                self.reportError(.E003, "operand");
                return error.SemanticError;
            },
            .array_literal => |_| {
                // Array literals need special handling for iteration to work
                // They should be wrapped in Array type, so defer to runtime
                return .{ .runtime_expr = expr };
            },
            .field_access => |fa| {
                // Handle enum member access: EnumName.memberName
                if (fa.base.* == .identifier) {
                    const enum_name = fa.base.identifier;
                    if (self.enum_decl_map.get(enum_name)) |enum_decl| {
                        // Find the member
                        var member_value: i64 = 0;
                        for (enum_decl.members) |member| {
                            if (std.mem.eql(u8, member.name, fa.field_name)) {
                                return .{ .enum_member = .{
                                    .enum_name = enum_name,
                                    .member_name = fa.field_name,
                                    .value = member_value,
                                } };
                            }
                            member_value += 1;
                        }
                    }
                }
                self.reportError(.E003, "field access");
                return error.SemanticError;
            },
            .map_literal => |_| {
                // Map literals are initialized at runtime, not compile time
                return .{ .runtime_expr = expr };
            },
            .cast => |_| {
                // Casts are evaluated at runtime
                return .{ .runtime_expr = expr };
            },
            else => {
                // Unsupported expression type in constant context
                self.reportError(.E003, "expression");
                return error.SemanticError;
            },
        };
    }

    /// Convert a compile-time constant value to IR
    fn convertConstantValue(self: *AstToIr, constant: ConstantValue) ConvertError!TypedValue {
        return switch (constant) {
            .int => |v| .{
                .value = try self.func().emitConstI64(v),
                .ty = .{ .primitive = "int" },
            },
            .float => |v| .{
                .value = try self.func().emitConstF64(v),
                .ty = .{ .primitive = "float" },
            },
            .bool => |v| .{
                .value = try self.func().emitConstI64(if (v) 1 else 0),
                .ty = .{ .primitive = "bool" },
            },
            .string => |s| self.convertStringLiteral(s),
            .array => |elements| {
                // Convert array constant elements to IR values directly
                // instead of going through convertArrayLiteral which expects
                // expressions to still be valid
                if (elements.len == 0) {
                    const managed_ptr = try self.emitEmptyManagedArray();
                    return .{
                        .value = managed_ptr,
                        .ty = .{ .array_type = .{ .element_type = .i64, .size = 0, .storage = .heap } },
                    };
                }

                // Determine element type from first element
                const first_elem = elements[0];
                const elem_type: ir.Type = switch (first_elem) {
                    .int => .i64,
                    .float => .f64,
                    .bool => .i64,
                    else => .i64,
                };

                // Allocate buffer for elements on heap
                const elem_count = try self.func().emitConstI64(@intCast(elements.len));
                const elem_size: i64 = 8;
                const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
                const buffer = try self.func().emitHeapAlloc(buffer_size, "array buffer");

                // Store elements into buffer
                for (elements, 0..) |elem, i| {
                    const value = switch (elem) {
                        .int => |v| try self.func().emitConstI64(v),
                        .float => |v| try self.func().emitConstF64(v),
                        .bool => |v| try self.func().emitConstI64(if (v) 1 else 0),
                        else => {
                            self.reportError(.E003, "array element");
                            return error.SemanticError;
                        },
                    };
                    const idx_val = try self.func().emitConstI64(@intCast(i));
                    const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
                    try self.func().emitStore(elem_ptr, value);
                }

                // Create managed array
                const managed_ptr = try self.emitManagedArray(buffer, elem_count, elem_count);

                return .{
                    .value = managed_ptr,
                    .ty = .{ .array_type = .{ .element_type = elem_type, .size = elements.len, .storage = .heap } },
                };
            },
            .enum_member => |em| .{
                .value = try self.func().emitConstI64(em.value),
                .ty = .{ .enum_type = em.enum_name },
            },
            .runtime_expr => |expr| {
                // For runtime-initialized constants, convert the expression
                // Array literals need to be wrapped in Array struct for iteration to work
                if (expr == .array_literal) {
                    return self.convertArrayConstant(expr.array_literal);
                }
                return self.convertExpression(expr);
            },
        };
    }

    /// Convert an array literal constant to an Array struct (for iteration support)
    fn convertArrayConstant(self: *AstToIr, arr_lit: ast.ArrayLiteralExpr) ConvertError!TypedValue {
        // First convert the array literal to a __ManagedArray
        const arr_typed = try self.convertArrayLiteral(arr_lit);

        // Determine element type name from the array type
        const elem_type_name: []const u8 = switch (arr_typed.ty) {
            .array_type => |a| a.element_type.toMaxonName(),
            else => "int",
        };

        // Get or create monomorphized Array type: Array$int, Array$float, etc.
        var type_args = [_][]const u8{elem_type_name};
        const array_type_name = try self.getOrCreateMonomorphizedType("Array", &type_args);

        // Wrap in Array struct by calling Array$init(__ManagedArray)
        const array_init_name = "Array$init";
        const array_func_info = self.func_map.get(array_init_name) orelse {
            self.reportInternalError("Array$init not found for array constant");
            return error.UnknownFunction;
        };

        if (!array_func_info.ir_generated) {
            try self.ensureMethodGenerated(array_init_name);
        }

        // Get Array type info for size
        const array_type_info = self.type_map.get("Array") orelse {
            self.reportError(.E006, "Array");
            return error.UnknownType;
        };

        const array_size = array_type_info.struct_type.size;
        const array_ptr = try self.func().emitAllocaSized(array_size);

        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = array_ptr;
        args[1] = arr_typed.value;
        _ = try self.func().emitCall(array_init_name, args, array_func_info.return_type);

        return .{
            .value = array_ptr,
            .ty = .{ .struct_type = array_type_name },
        };
    }

    // ------------------------------------------------------------------------
    // Type Lookup Helpers
    // ------------------------------------------------------------------------

    fn lookupIrType(self: *AstToIr, name: []const u8) !ir.Type {
        const type_info = self.type_map.get(name) orelse {
            self.reportError(.E006, name);
            return error.UnknownType;
        };
        // Check visibility
        try self.checkTypeVisibility(name, type_info);
        return type_info.irType();
    }

    fn lookupStructInfo(self: *AstToIr, type_name: []const u8) !StructTypeInfo {
        // First check if type exists
        if (self.type_map.get(type_name)) |type_info| {
            // Check visibility
            try self.checkTypeVisibility(type_name, type_info);
            return switch (type_info) {
                .struct_type => |s| s,
                .primitive, .enum_type => {
                    self.reportError(.E006, type_name);
                    return error.UnknownType;
                },
            };
        }

        // Type not found - check if it's a monomorphized generic type that needs to be created
        // Format is BaseType$Arg1$Arg2...
        if (std.mem.indexOf(u8, type_name, "$")) |first_dollar| {
            const base_type = type_name[0..first_dollar];
            // Parse type arguments from remaining string
            var args_list = std.ArrayListUnmanaged([]const u8){};
            defer args_list.deinit(self.allocator);

            var rest = type_name[first_dollar + 1 ..];
            while (rest.len > 0) {
                if (std.mem.indexOf(u8, rest, "$")) |next_dollar| {
                    try args_list.append(self.allocator, rest[0..next_dollar]);
                    rest = rest[next_dollar + 1 ..];
                } else {
                    try args_list.append(self.allocator, rest);
                    break;
                }
            }

            // Try to create the monomorphized type
            _ = self.getOrCreateMonomorphizedType(base_type, args_list.items) catch {
                self.reportError(.E006, type_name);
                return error.UnknownType;
            };

            // Now look it up again
            if (self.type_map.get(type_name)) |type_info| {
                return switch (type_info) {
                    .struct_type => |s| s,
                    .primitive, .enum_type => {
                        self.reportError(.E006, type_name);
                        return error.UnknownType;
                    },
                };
            }
        }

        self.reportError(.E006, type_name);
        return error.UnknownType;
    }

    fn lookupField(self: *AstToIr, struct_info: StructTypeInfo, field_name: []const u8) !FieldInfo {
        for (struct_info.fields) |f| {
            if (std.mem.eql(u8, f.name, field_name)) return f;
        }
        self.reportError(.E007, field_name);
        return error.UnknownField;
    }

    /// Check if a type is visible from the current source file
    fn checkTypeVisibility(self: *AstToIr, type_name: []const u8, type_info: TypeInfo) !void {
        switch (type_info) {
            .struct_type => |s| {
                if (!s.is_export) {
                    // Private type - check if we're in the same file
                    if (s.source_file) |def_file| {
                        if (self.source_file) |current_file| {
                            if (!std.mem.eql(u8, def_file, current_file)) {
                                debug.astToIr("Type visibility error: '{s}' defined in '{s}' accessed from '{s}'", .{ type_name, def_file, current_file });
                                self.reportError(.E006, type_name);
                                return error.UnknownType;
                            }
                        } else {
                            // No current source file (shouldn't happen)
                            debug.astToIr("Type visibility error: '{s}' - no current source file", .{type_name});
                            self.reportError(.E006, type_name);
                            return error.UnknownType;
                        }
                    }
                }
            },
            .enum_type => {
                // Enums used in struct fields are implementation details
                // Don't enforce visibility for them - they're only accessible through the struct API
                // If user code explicitly references the enum name, that will fail to compile
                // because the enum won't be in scope
            },
            .primitive => {}, // Primitives are always visible
        }
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
            .error_union => |eu| {
                // Error union size = 8 (tag) + max(success size, error size)
                return 8 + self.getTypeSize(eu.success_type.*);
            },
            .function_type => {
                // Function pointers are always 8 bytes
                return 8;
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

    /// Get the ValueType for the wrapped value inside an optional.
    /// Handles struct, enum, and primitive types correctly.
    fn getOptionalWrappedValueType(opt_info: OptionalInfo) ValueType {
        if (opt_info.wrapped_struct_type) |struct_name| {
            return .{ .struct_type = struct_name };
        } else if (opt_info.wrapped_enum_type) |enum_name| {
            return .{ .enum_type = enum_name };
        } else {
            return .{ .primitive = opt_info.wrapped.toMaxonName() };
        }
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

        const field_result = try self.buildFieldInfos(type_decl.fields);

        try self.type_map.put(self.allocator, type_decl.name, .{
            .struct_type = .{
                .name = type_decl.name,
                .fields = field_result.fields,
                .size = field_result.size,
                .decl_line = type_decl.block.start_line,
                .decl_column = type_decl.block.start_column,
                .source_file = self.source_file,
                .is_export = type_decl.is_export,
            },
        });

        // Now that the new entry is in the map, free the old fields if this was a re-registration
        if (old_fields) |of| {
            self.allocator.free(of);
        }

        debug.astToIr("Registered type '{s}' with size {d}", .{ type_decl.name, field_result.size });
    }

    /// Register an external type (from stdlib or other modules)
    pub fn registerExternalType(self: *AstToIr, ext_type: ExternalTypeInfo) !void {
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
        // Also queue for conformance check after interfaces are registered
        if (ext_type.type_decl) |type_decl| {
            try self.type_decl_map.put(self.allocator, ext_type.name, type_decl.*);
            try self.pending_conformance_checks.append(self.allocator, type_decl);
        }

        debug.astToIr("Registered external type '{s}' with size {d}", .{ ext_type.name, ext_type.size });
    }

    fn registerEnum(self: *AstToIr, enum_decl: ast.EnumDecl) !void {
        var members: std.StringHashMapUnmanaged(i64) = .{};
        errdefer members.deinit(self.allocator);

        var case_info: std.StringHashMapUnmanaged(types.EnumCaseInfo) = .{};
        errdefer case_info.deinit(self.allocator);

        // Determine backing type
        const backing_type: types.EnumTypeInfo.BackingType = if (enum_decl.backing_type) |bt|
            (if (std.mem.eql(u8, bt, "string")) .string else .int)
        else
            .int;

        // Check if this enum conforms to Error interface
        var is_error = false;
        for (enum_decl.conformances) |conformance| {
            if (std.mem.eql(u8, conformance.interface_name, "Error")) {
                is_error = true;
                break;
            }
        }

        // For string-backed enums, store the string values
        var string_values: std.AutoHashMapUnmanaged(i64, []const u8) = .{};
        errdefer string_values.deinit(self.allocator);

        var has_associated_values = false;
        var max_payload_size: i32 = 0;

        // Track seen raw values for duplicate detection
        var seen_raw_values: std.AutoHashMapUnmanaged(i64, []const u8) = .{};
        defer seen_raw_values.deinit(self.allocator);

        var next_value: i64 = 0;
        for (enum_decl.members) |member| {
            // Check for duplicate case names
            if (members.contains(member.name)) {
                self.reportErrorAt(.E030, member.name, member.line, member.column);
                return error.SemanticError;
            }

            if (member.value) |value_expr| {
                // Explicit value - evaluate constant expression
                if (value_expr.* == .integer) {
                    next_value = value_expr.integer;

                    // Check for duplicate raw values
                    if (seen_raw_values.get(next_value)) |_| {
                        const msg = try std.fmt.allocPrint(self.allocator, "{d}", .{next_value});
                        try self.module.trackString(msg);
                        self.reportErrorAt(.E031, msg, member.line, member.column);
                        return error.SemanticError;
                    }
                } else if (value_expr.* == .string_literal and backing_type == .string) {
                    // Store the string backing value for this ordinal
                    try string_values.put(self.allocator, next_value, value_expr.string_literal);
                } else if (value_expr.* == .string_literal and backing_type == .int) {
                    // Type mismatch: string value for int-backed enum
                    self.reportErrorAt(.E032, "expected int, got string", member.line, member.column);
                    return error.SemanticError;
                } else if (value_expr.* == .integer and backing_type == .string) {
                    // Type mismatch: int value for string-backed enum
                    self.reportErrorAt(.E032, "expected string, got int", member.line, member.column);
                    return error.SemanticError;
                }
            }

            // Track this raw value
            try seen_raw_values.put(self.allocator, next_value, member.name);
            try members.put(self.allocator, member.name, next_value);

            // Build associated value info for this case
            var assoc_values = std.ArrayListUnmanaged(types.AssociatedValueInfo){};
            var payload_size: i32 = 0;

            for (member.associated_values) |av| {
                const type_name = switch (av.type_expr) {
                    .simple => |name| name,
                    else => "ptr", // Complex types are pointers
                };
                const av_ir_type = types.nameToIrType(type_name);
                const av_size: i32 = switch (av_ir_type) {
                    .i64, .f64, .ptr => 8,
                    .i32 => 4,
                    .i8 => 1,
                    .void => 0,
                };
                payload_size += av_size;
                try assoc_values.append(self.allocator, .{
                    .name = av.name,
                    .type_name = type_name,
                    .ir_type = av_ir_type,
                });
            }

            if (assoc_values.items.len > 0) {
                has_associated_values = true;
                if (payload_size > max_payload_size) {
                    max_payload_size = payload_size;
                }
            }

            try case_info.put(self.allocator, member.name, .{
                .tag = next_value,
                .associated_values = try assoc_values.toOwnedSlice(self.allocator),
            });

            next_value += 1;
        }

        // Store enum declaration for conformance lookup
        try self.enum_decl_map.put(self.allocator, enum_decl.name, enum_decl);

        try self.type_map.put(self.allocator, enum_decl.name, .{
            .enum_type = .{
                .name = enum_decl.name,
                .members = members,
                .case_info = case_info,
                .backing_type = backing_type,
                .string_values = string_values,
                .is_error = is_error,
                .has_associated_values = has_associated_values,
                .max_payload_size = max_payload_size,
                .has_explicit_backing_type = enum_decl.backing_type != null,
                .decl_line = enum_decl.block.start_line,
                .decl_column = enum_decl.block.start_column,
                .source_file = self.source_file,
                .is_export = enum_decl.is_export,
            },
        });
        debug.astToIr("Registered enum '{s}' with {d} members (backing: {s}, is_error: {}, has_assoc: {})", .{ enum_decl.name, enum_decl.members.len, @tagName(backing_type), is_error, has_associated_values });
    }

    pub fn registerInterface(self: *AstToIr, iface: ast.InterfaceDecl) !void {
        try self.interface_map.put(self.allocator, iface.name, .{
            .name = iface.name,
            .methods = iface.methods,
            .associated_types = iface.generic_params,
            .extends = iface.extends,
            .decl_line = iface.block.start_line,
            .decl_column = iface.block.start_column,
        });
        debug.astToIr("Registered interface '{s}' with {d} methods, {d} associated types, extends {d} interfaces", .{ iface.name, iface.methods.len, iface.generic_params.len, iface.extends.len });
    }

    /// Collect all methods from an interface including methods from transitively extended interfaces.
    /// Returns a list of (interface_name, method) pairs to track which interface requires each method.
    fn collectAllInterfaceMethods(self: *AstToIr, interface_name: []const u8, visited: *std.StringHashMapUnmanaged(void)) !std.ArrayListUnmanaged(types.InterfaceMethodInfo) {
        var all_methods: std.ArrayListUnmanaged(types.InterfaceMethodInfo) = .empty;

        // Avoid infinite loops from circular extends (shouldn't happen but be safe)
        if (visited.contains(interface_name)) {
            return all_methods;
        }
        try visited.put(self.allocator, interface_name, {});

        const iface = self.interface_map.get(interface_name) orelse {
            // Unknown interface - skip
            debug.astToIr("Skipping unknown interface '{s}' in transitive resolution", .{interface_name});
            return all_methods;
        };

        // Add methods from extended interfaces first (base methods before derived)
        for (iface.extends) |extended_name| {
            var extended_methods = try self.collectAllInterfaceMethods(extended_name, visited);
            defer extended_methods.deinit(self.allocator);
            try all_methods.appendSlice(self.allocator, extended_methods.items);
        }

        // Add methods from this interface
        for (iface.methods) |method| {
            try all_methods.append(self.allocator, .{ .interface_name = interface_name, .method = method });
        }

        return all_methods;
    }

    fn checkInterfaceConformance(self: *AstToIr, type_decl: ast.TypeDecl) !void {
        for (type_decl.conformances) |conformance| {
            // Reject struct types trying to conform to Error interface
            // Error can only be implemented by enums
            if (std.mem.eql(u8, conformance.interface_name, "Error")) {
                const msg = std.fmt.allocPrint(self.allocator, "Type '{s}' cannot conform to Error - only enums can implement Error", .{
                    type_decl.name,
                }) catch {
                    self.reportError(.E023, type_decl.name);
                    return error.SemanticError;
                };
                self.last_error = .{
                    .code = .E023,
                    .message = msg,
                    .location = .{
                        .file = self.source_file,
                        .line = 1,
                        .column = 1,
                    },
                    .message_allocated = true,
                };
                return error.SemanticError;
            }

            const iface = self.interface_map.get(conformance.interface_name) orelse {
                // Unknown interface - only allow in stdlib code where interfaces may not be loaded yet
                if (intrinsics.isStdlibFile(self)) {
                    debug.astToIr("Skipping unknown interface '{s}' in stdlib", .{conformance.interface_name});
                    continue;
                }
                // Report error for non-stdlib code trying to implement unknown interface
                const msg = std.fmt.allocPrint(self.allocator, "Unknown interface '{s}' - interface may not be exported or does not exist", .{
                    conformance.interface_name,
                }) catch {
                    self.reportError(.E051, conformance.interface_name);
                    return error.SemanticError;
                };
                self.last_error = .{
                    .code = .E051,
                    .message = msg,
                    .location = .{
                        .file = self.source_file,
                        .line = 1,
                        .column = 1,
                    },
                    .message_allocated = true,
                };
                return error.SemanticError;
            };

            // Build a map of associated type name -> bound type
            var type_bindings = std.StringHashMapUnmanaged([]const u8){};
            defer type_bindings.deinit(self.allocator);

            // Check that all required associated types are bound
            for (iface.associated_types, 0..) |assoc_type, i| {
                if (i < conformance.type_args.len) {
                    // Normalize "with" syntax to "of" syntax for type comparison
                    const normalized = self.normalizeConformanceTypeArg(conformance.type_args[i]);
                    try type_bindings.put(self.allocator, assoc_type, normalized);
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
                        .message_allocated = true,
                    };
                    return error.SemanticError;
                }
            }

            // Collect all methods including those from extended interfaces (transitive)
            var visited = std.StringHashMapUnmanaged(void){};
            defer visited.deinit(self.allocator);
            var all_interface_methods = try self.collectAllInterfaceMethods(conformance.interface_name, &visited);
            defer all_interface_methods.deinit(self.allocator);

            // Check that all required interface methods are implemented
            var missing_methods = std.ArrayListUnmanaged([]const u8){};
            defer missing_methods.deinit(self.allocator);

            for (all_interface_methods.items) |method_info| {
                const iface_method = method_info.method;
                const source_interface = method_info.interface_name;

                // Skip methods with default implementations (they're optional to override)
                if (iface_method.has_default_impl) continue;

                // Look for matching method in type - must have qualified name "Interface.method"
                var found_method: ?ast.MethodDecl = null;
                for (type_decl.methods) |type_method| {
                    // Method must have qualified_name matching "InterfaceName.methodName"
                    if (type_method.qualified_name) |qualified| {
                        if (std.mem.indexOf(u8, qualified, ".")) |dot_pos| {
                            const iface_name = qualified[0..dot_pos];
                            const method_name = qualified[dot_pos + 1 ..];
                            if (std.mem.eql(u8, iface_name, source_interface) and
                                std.mem.eql(u8, method_name, iface_method.name))
                            {
                                found_method = type_method;
                                break;
                            }
                        }
                    }
                }

                if (found_method) |type_method| {
                    // Check return type match (with associated type substitution)
                    if (iface_method.return_type) |iface_ret| {
                        if (type_method.return_type) |impl_ret| {
                            const expected = self.resolveAssociatedTypeWithSelf(iface_ret, type_bindings, type_decl.name);
                            const actual = self.resolveAssociatedTypeWithSelf(impl_ret, type_bindings, type_decl.name);
                            if (!std.mem.eql(u8, expected, actual)) {
                                const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' has return type '{s}' but interface '{s}' requires '{s}'", .{
                                    type_decl.name,
                                    iface_method.name,
                                    actual,
                                    source_interface,
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
                                    .message_allocated = true,
                                };
                                return error.SemanticError;
                            }
                        } else {
                            // Interface expects a return type but implementation has none
                            const expected = self.resolveAssociatedTypeWithSelf(iface_ret, type_bindings, type_decl.name);
                            const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' has no return type but interface '{s}' requires '{s}'", .{
                                type_decl.name,
                                iface_method.name,
                                source_interface,
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
                                .message_allocated = true,
                            };
                            return error.SemanticError;
                        }
                    } else if (type_method.return_type) |impl_ret| {
                        // Interface expects no return type but implementation has one
                        const actual = self.resolveAssociatedTypeWithSelf(impl_ret, type_bindings, type_decl.name);
                        const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' has return type '{s}' but interface '{s}' requires no return type", .{
                            type_decl.name,
                            iface_method.name,
                            actual,
                            source_interface,
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
                            .message_allocated = true,
                        };
                        return error.SemanticError;
                    }

                    // Check parameter types match (with associated type substitution)
                    if (iface_method.params.len == type_method.params.len) {
                        for (iface_method.params, 0..) |iface_param, i| {
                            const expected = self.resolveAssociatedTypeWithSelf(iface_param.type_expr, type_bindings, type_decl.name);
                            const actual = self.resolveAssociatedTypeWithSelf(type_method.params[i].type_expr, type_bindings, type_decl.name);
                            if (!std.mem.eql(u8, expected, actual)) {
                                const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' parameter {d} has type '{s}' but interface '{s}' requires '{s}'", .{
                                    type_decl.name,
                                    iface_method.name,
                                    i + 1,
                                    actual,
                                    source_interface,
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
                                    .message_allocated = true,
                                };
                                return error.SemanticError;
                            }
                        }
                    }

                    // Check throws_type conformance
                    if (iface_method.throws_type) |iface_throws| {
                        if (type_method.throws_type) |impl_throws| {
                            // Interface specifies Error: implementation must throw a type that conforms to Error
                            if (std.mem.eql(u8, iface_throws, "Error")) {
                                // Check if impl_throws conforms to Error
                                if (!self.typeConformsTo(impl_throws, "Error")) {
                                    const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' throws '{s}' which does not conform to Error", .{
                                        type_decl.name,
                                        iface_method.name,
                                        impl_throws,
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
                                        .message_allocated = true,
                                    };
                                    return error.SemanticError;
                                }
                            } else {
                                // Interface specifies a specific error type: implementation must match exactly
                                if (!std.mem.eql(u8, iface_throws, impl_throws)) {
                                    const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' throws '{s}' but interface '{s}' requires throws '{s}'", .{
                                        type_decl.name,
                                        iface_method.name,
                                        impl_throws,
                                        source_interface,
                                        iface_throws,
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
                                        .message_allocated = true,
                                    };
                                    return error.SemanticError;
                                }
                            }
                        } else {
                            // Interface method throws but implementation doesn't
                            const msg = std.fmt.allocPrint(self.allocator, "Method '{s}.{s}' must throw '{s}' as required by interface '{s}'", .{
                                type_decl.name,
                                iface_method.name,
                                iface_throws,
                                source_interface,
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
                                .message_allocated = true,
                            };
                            return error.SemanticError;
                        }
                    } else if (type_method.throws_type) |impl_throws| {
                        // Implementation throws but interface doesn't require it
                        // This is actually OK - a method can throw even if interface doesn't require it
                        // However, for strict conformance we might want to disallow this
                        // For now, allow it (implementation can be more restrictive)
                        _ = impl_throws;
                    }
                } else {
                    // Missing method - collect for aggregate error, noting which interface requires it
                    const resolved_ret = if (iface_method.return_type) |rt|
                        self.resolveAssociatedTypeWithSelf(rt, type_bindings, type_decl.name)
                    else
                        "void";
                    const method_sig = if (std.mem.eql(u8, source_interface, conformance.interface_name))
                        std.fmt.allocPrint(self.allocator, "{s}() returns {s}", .{
                            iface_method.name,
                            resolved_ret,
                        }) catch continue
                    else
                        std.fmt.allocPrint(self.allocator, "{s}() returns {s} (from {s})", .{
                            iface_method.name,
                            resolved_ret,
                            source_interface,
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
                    .message_allocated = true,
                };
                return error.SemanticError;
            }

            // Check that methods with qualified names (e.g., Collection.map) actually exist in the interface
            // Skip this check for marker interfaces (interfaces with no methods like Builtin, Error)
            if (iface.methods.len > 0) {
                for (type_decl.methods) |type_method| {
                    if (type_method.qualified_name) |qualified| {
                        // Parse "Interface.method" format
                        if (std.mem.indexOf(u8, qualified, ".")) |dot_pos| {
                            const iface_name = qualified[0..dot_pos];
                            const method_name = qualified[dot_pos + 1 ..];

                            // Only validate for the current interface being checked
                            if (std.mem.eql(u8, iface_name, conformance.interface_name)) {
                                // Check if this method exists in the interface
                                var method_exists = false;
                                for (iface.methods) |iface_method| {
                                    if (std.mem.eql(u8, iface_method.name, method_name)) {
                                        method_exists = true;
                                        break;
                                    }
                                }

                                if (!method_exists) {
                                    const msg = std.fmt.allocPrint(self.allocator, "Method '{s}' is not defined in interface '{s}'", .{
                                        method_name,
                                        iface_name,
                                    }) catch {
                                        self.reportError(.E015, method_name);
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
                                        .message_allocated = true,
                                    };
                                    return error.SemanticError;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// Normalize a conformance type arg string from "with" syntax to "of" syntax
    /// e.g., "Pair with (Key, Value)" -> "Pair of Key Value"
    fn normalizeConformanceTypeArg(self: *AstToIr, type_arg: []const u8) []const u8 {
        // Find " with " in the string
        if (std.mem.indexOf(u8, type_arg, " with ")) |with_pos| {
            var result: std.ArrayListUnmanaged(u8) = .empty;
            // Add base type
            result.appendSlice(self.allocator, type_arg[0..with_pos]) catch return type_arg;
            result.appendSlice(self.allocator, " of ") catch return type_arg;

            // Parse the rest after " with "
            var rest = type_arg[with_pos + 6 ..]; // skip " with "

            // Remove parentheses and convert commas to spaces
            if (rest.len > 0 and rest[0] == '(') {
                rest = rest[1..];
            }
            if (rest.len > 0 and rest[rest.len - 1] == ')') {
                rest = rest[0 .. rest.len - 1];
            }

            // Replace ", " with " "
            var i: usize = 0;
            while (i < rest.len) {
                if (i + 1 < rest.len and rest[i] == ',' and rest[i + 1] == ' ') {
                    result.append(self.allocator, ' ') catch return type_arg;
                    i += 2;
                } else {
                    result.append(self.allocator, rest[i]) catch return type_arg;
                    i += 1;
                }
            }

            const owned = result.toOwnedSlice(self.allocator) catch return type_arg;
            self.module.trackString(owned) catch {};
            return owned;
        }
        return type_arg;
    }

    /// Resolve a type expression by substituting associated types and Self with their bound types
    fn resolveAssociatedTypeWithSelf(self: *AstToIr, type_expr: ast.TypeExpr, bindings: std.StringHashMapUnmanaged([]const u8), self_type: []const u8) []const u8 {
        return switch (type_expr) {
            .simple => |name| {
                // Handle Self specially
                if (std.mem.eql(u8, name, "Self")) return self_type;
                // Check if this is an associated type that should be substituted
                return bindings.get(name) orelse name;
            },
            .generic => |g| {
                if (std.mem.eql(u8, g.base_type, "Self")) return self_type;
                // Check if it's an associated type
                if (bindings.get(g.base_type)) |bound| return bound;
                // Build the full generic type string: "Base of Param1 Param2..."
                var result: std.ArrayListUnmanaged(u8) = .empty;
                result.appendSlice(self.allocator, g.base_type) catch return g.base_type;
                if (g.type_args.len > 0) {
                    result.appendSlice(self.allocator, " of ") catch return g.base_type;
                    for (g.type_args, 0..) |param, i| {
                        if (i > 0) result.append(self.allocator, ' ') catch return g.base_type;
                        // Resolve each parameter (may be associated type like Key, Value)
                        const resolved = bindings.get(param) orelse param;
                        result.appendSlice(self.allocator, resolved) catch return g.base_type;
                    }
                }
                const owned = result.toOwnedSlice(self.allocator) catch return g.base_type;
                self.module.trackString(owned) catch {};
                return owned;
            },
            .optional => |wrapped| switch (wrapped.*) {
                .simple => |name| if (std.mem.eql(u8, name, "Self")) self_type else bindings.get(name) orelse name,
                .generic => |g| {
                    if (std.mem.eql(u8, g.base_type, "Self")) return self_type;
                    // Check if it's an associated type
                    if (bindings.get(g.base_type)) |bound| return bound;
                    // Build the full generic type string for optional wrapped type
                    var result: std.ArrayListUnmanaged(u8) = .empty;
                    result.appendSlice(self.allocator, g.base_type) catch return g.base_type;
                    if (g.type_args.len > 0) {
                        result.appendSlice(self.allocator, " of ") catch return g.base_type;
                        for (g.type_args, 0..) |param, i| {
                            if (i > 0) result.append(self.allocator, ' ') catch return g.base_type;
                            const resolved = bindings.get(param) orelse param;
                            result.appendSlice(self.allocator, resolved) catch return g.base_type;
                        }
                    }
                    const owned = result.toOwnedSlice(self.allocator) catch return g.base_type;
                    self.module.trackString(owned) catch {};
                    return owned;
                },
                .optional => "?",
                .error_union => "error_union",
                .function_type => "(fn)",
            },
            .error_union => |eu| bindings.get(eu.error_type) orelse eu.error_type,
            .function_type => "(fn)",
        };
    }

    /// Check if a type conforms to a specific interface by name.
    /// This includes transitive conformance through interface inheritance.
    fn typeConformsTo(self: *AstToIr, type_name: []const u8, interface_name: []const u8) bool {
        // Collect all interfaces the type directly declares conformance to
        var direct_conformances: [32][]const u8 = undefined;
        var num_conformances: usize = 0;

        // Check struct types
        if (self.type_decl_map.get(type_name)) |type_decl| {
            for (type_decl.conformances) |conformance| {
                if (num_conformances < 32) {
                    direct_conformances[num_conformances] = conformance.interface_name;
                    num_conformances += 1;
                }
            }
        }

        // Check enum types
        if (self.enum_decl_map.get(type_name)) |enum_decl| {
            for (enum_decl.conformances) |conformance| {
                if (num_conformances < 32) {
                    direct_conformances[num_conformances] = conformance.interface_name;
                    num_conformances += 1;
                }
            }
        }

        // Check each direct conformance
        for (direct_conformances[0..num_conformances]) |conf_name| {
            if (self.interfaceConformsTo(conf_name, interface_name)) {
                return true;
            }
        }

        return false;
    }

    /// Check if interface_name conforms to target_interface (directly or transitively).
    fn interfaceConformsTo(self: *AstToIr, interface_name: []const u8, target_interface: []const u8) bool {
        // Direct match
        if (std.mem.eql(u8, interface_name, target_interface)) {
            return true;
        }

        // Check if interface_name extends target_interface (transitively)
        const iface = self.interface_map.get(interface_name) orelse return false;
        for (iface.extends) |extended_name| {
            if (self.interfaceConformsTo(extended_name, target_interface)) {
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
                    // Use full monomorphized name for generic types (e.g., "Array$int" not just "Array")
                    if (type_info.isStruct()) ret_type_name = getStructNameFromTypeExpr(rt);
                    break :blk type_info.irType();
                },
                .optional => |wrapped| {
                    // Optional return types are returned as pointers
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    ret_value_type = .{
                        .optional_type = .{
                            .wrapped = wrapped_value_type.toPrimitiveType(),
                            .wrapped_struct_type = if (wrapped_value_type == .struct_type) wrapped_value_type.struct_type else null,
                            .wrapped_enum_type = if (wrapped_value_type == .enum_type) wrapped_value_type.enum_type else null,
                        },
                    };
                    break :blk .ptr;
                },
                .error_union => {
                    // Error union return types are returned as pointers
                    ret_value_type = try self.typeExprToValueType(rt);
                    break :blk .ptr;
                },
                .function_type => {
                    // Function types are returned as pointers
                    ret_value_type = try self.typeExprToValueType(rt);
                    break :blk .ptr;
                },
            }
        } else .void;

        var param_types = try self.allocator.alloc(ParamType, decl.params.len);
        errdefer self.allocator.free(param_types);
        for (decl.params, 0..) |param, i| {
            param_types[i] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
                .name = param.name,
                .default_value = param.default_value,
            };
        }

        // Free old param_types if overwriting an existing entry (e.g., from external registration)
        if (self.func_map.get(decl.name)) |old_info| {
            if (old_info.param_types.len > 0) {
                self.allocator.free(old_info.param_types);
            }
        }

        // If function throws, the effective return type is an error union
        var effective_ret_type = ret_type;
        var effective_ret_value_type = ret_value_type;
        if (decl.throws_type != null) {
            // Throwing functions return via sret (error union)
            effective_ret_type = .ptr;
            // Create error union type info
            const success_ir_type = ret_type;
            const success_struct_type = ret_type_name;
            effective_ret_value_type = .{
                .error_union_type = .{
                    .success_type = success_ir_type,
                    .success_struct_type = success_struct_type,
                    .error_enum_type = decl.throws_type.?,
                },
            };
        }

        try self.func_map.put(self.allocator, decl.name, .{
            .return_type = effective_ret_type,
            .return_type_name = ret_type_name,
            .return_value_type = effective_ret_value_type,
            .param_types = param_types,
            .decl_line = decl.block.start_line,
            .decl_column = decl.block.start_column,
        });

        debug.astToIr("Registered function '{s}' returning {s}", .{ decl.name, ret_type.toIrName() });
    }

    /// Extract base type name from simple or generic type expressions
    fn typeExprBaseTypeName(type_expr: ast.TypeExpr) ?[]const u8 {
        return switch (type_expr) {
            .simple => |name| name,
            .generic => |gen| gen.base_type,
            .optional, .error_union, .function_type => null,
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
                .error_union => "error_union",
                .function_type => "(fn)",
            },
            .error_union => |eu| eu.error_type,
            .function_type => "(fn)",
        };
    }

    /// Result from building field infos
    const FieldInfoResult = struct {
        fields: []FieldInfo,
        size: i32,
    };

    /// Build FieldInfo array from type declaration fields
    /// Shared by registerType and getOrCreateMonomorphizedType
    fn buildFieldInfos(self: *AstToIr, type_decl_fields: []const ast.FieldDecl) !FieldInfoResult {
        var fields = try self.allocator.alloc(FieldInfo, type_decl_fields.len);
        errdefer self.allocator.free(fields);
        var offset: i32 = 0;

        for (type_decl_fields, 0..) |field, i| {
            const value_type = try self.typeExprToValueType(field.type_expr);
            const field_size: i32 = try self.getValueTypeSize(value_type);
            fields[i] = .{
                .name = field.name,
                .offset = offset,
                .size = field_size,
                .value_type = value_type,
                .is_mutable = field.is_mutable,
                .is_export = field.is_export,
            };
            offset += field_size;
        }

        return .{ .fields = fields, .size = offset };
    }

    /// Get the size in bytes for a value type
    fn getValueTypeSize(self: *AstToIr, value_type: ValueType) !i32 {
        return switch (value_type) {
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
            .primitive, .enum_type, .array_type => 8,
            .optional_type => |opt_info| self.getOptionalSize(opt_info),
            .error_union_type => 8,
            .function_type => 8,
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
                // Check visibility
                try self.checkTypeVisibility(resolved, type_info);
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
                    .wrapped_enum_type = if (wrapped_value_type == .enum_type) wrapped_value_type.enum_type else null,
                } };
            },
            .function_type => |ft| {
                // Convert function type expression to ValueType
                const param_types = try self.allocator.alloc(ValueType, ft.param_types.len);
                errdefer self.allocator.free(param_types);
                for (ft.param_types, 0..) |pt, i| {
                    param_types[i] = try self.typeExprToValueType(pt);
                }
                // Track for cleanup in deinit
                try self.function_type_allocs.append(self.allocator, .{ .param_types = param_types });

                var return_type: ?*const ValueType = null;
                var return_ir_type: ir.Type = .void;
                if (ft.return_type) |rt| {
                    const ret_vt = try self.typeExprToValueType(rt.*);
                    const ret_ptr = try self.allocator.create(ValueType);
                    errdefer self.allocator.destroy(ret_ptr);
                    ret_ptr.* = ret_vt;
                    return_type = ret_ptr;
                    return_ir_type = ret_vt.toPrimitiveType();
                    // Track for cleanup in deinit
                    try self.function_type_allocs.append(self.allocator, .{ .return_type = ret_ptr });
                }

                return .{ .function_type = .{
                    .param_types = param_types,
                    .return_type = return_type,
                    .return_ir_type = return_ir_type,
                } };
            },
            .error_union => |eu| {
                // Convert error union type: T or E where E conforms to Error
                const success_value_type = try self.typeExprToValueType(eu.success_type.*);
                const resolved_error = self.resolveTypeName(eu.error_type);
                return .{ .error_union_type = .{
                    .success_type = success_value_type.toPrimitiveType(),
                    .success_struct_type = if (success_value_type == .struct_type) success_value_type.struct_type else null,
                    .error_enum_type = resolved_error,
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
        const field_result = self.buildFieldInfos(type_decl.fields) catch |e| {
            self.in_stdlib_method = saved_in_stdlib;
            return e;
        };

        const struct_info = StructTypeInfo{
            .name = mono_name,
            .fields = field_result.fields,
            .size = field_result.size,
            .decl_line = type_decl.block.start_line,
            .decl_column = type_decl.block.start_column,
        };

        try self.type_map.put(self.allocator, mono_name, .{ .struct_type = struct_info });
        try self.type_decl_map.put(self.allocator, mono_name, type_decl);

        // Create generic bindings for lazy method generation
        var bindings = try self.allocator.alloc(PendingMethod.GenericBinding, type_decl.generic_params.len);
        for (type_decl.generic_params, type_args, 0..) |param, arg, i| {
            bindings[i] = .{ .param = param, .concrete = arg };
        }

        // Register method signatures only (lazy generation - IR generated on-demand)
        // Track if any method stored the bindings (they share the same slice)
        var bindings_stored = false;
        for (type_decl.methods) |*method| {
            if (try self.registerMethodSignatureOnly(mono_name, method, bindings)) {
                bindings_stored = true;
            }
        }

        // If no method stored the bindings (all were already registered), free them
        if (!bindings_stored) {
            self.allocator.free(bindings);
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

    /// Build FuncInfo from method declaration (shared by registerMethod and registerMethodSignatureOnly).
    fn buildMethodFuncInfo(
        self: *AstToIr,
        type_name: []const u8,
        method: anytype, // ast.MethodDecl or *const ast.MethodDecl
        ir_generated: bool,
    ) !FuncInfo {
        // Get method fields (works for both value and pointer)
        const m = if (@TypeOf(method) == *const ast.MethodDecl) method.* else method;

        // Determine return type
        var ret_type_name: ?[]const u8 = null;
        var ret_value_type: ?ValueType = null;
        const ret_type: ir.Type = if (m.return_type) |rt| blk: {
            switch (rt) {
                .simple => {
                    const base_name = typeExprBaseTypeName(rt).?;
                    const resolved = self.resolveTypeName(base_name);
                    const type_info = self.type_map.get(resolved) orelse {
                        self.reportError(.E006, resolved);
                        return error.UnknownType;
                    };
                    if (type_info.isStruct()) ret_type_name = resolved;
                    break :blk type_info.irType();
                },
                .generic => |gen| {
                    // For generic types like "Array of String", build monomorphized name
                    const resolved_args = try self.allocator.alloc([]const u8, gen.type_args.len);
                    defer self.allocator.free(resolved_args);
                    for (gen.type_args, 0..) |arg, i| {
                        resolved_args[i] = self.resolveTypeName(arg);
                    }
                    const mono_name = try self.getOrCreateMonomorphizedType(gen.base_type, resolved_args);
                    ret_type_name = mono_name;
                    break :blk .ptr; // Generic struct types are returned as pointers
                },
                .optional => |wrapped| {
                    const wrapped_value_type = try self.typeExprToValueType(wrapped.*);
                    const wrapped_struct_name: ?[]const u8 = if (wrapped_value_type == .struct_type)
                        wrapped_value_type.struct_type
                    else
                        null;
                    const wrapped_enum_name: ?[]const u8 = if (wrapped_value_type == .enum_type)
                        wrapped_value_type.enum_type
                    else
                        null;
                    ret_value_type = .{
                        .optional_type = .{
                            .wrapped = wrapped_value_type.toPrimitiveType(),
                            .wrapped_struct_type = wrapped_struct_name,
                            .wrapped_enum_type = wrapped_enum_name,
                        },
                    };
                    break :blk .ptr;
                },
                .error_union => {
                    // Error union return types are returned as pointers
                    ret_value_type = try self.typeExprToValueType(rt);
                    break :blk .ptr;
                },
                .function_type => {
                    ret_value_type = try self.typeExprToValueType(rt);
                    break :blk .ptr;
                },
            }
        } else .void;

        // Count params: +1 for implicit self if not static
        const extra_params: usize = if (m.is_static) 0 else 1;
        var param_types = try self.allocator.alloc(ParamType, m.params.len + extra_params);

        // First param is self (pointer to type instance) for instance methods
        if (!m.is_static) {
            param_types[0] = .{ .ty = .{ .struct_type = type_name }, .name = "self" };
        }

        // Register explicit params
        for (m.params, 0..) |param, i| {
            param_types[i + extra_params] = .{
                .ty = try self.typeExprToValueType(param.type_expr),
                .name = param.name,
                .default_value = param.default_value,
            };
        }

        // If method throws, the effective return type is an error union
        var effective_ret_type = ret_type;
        var effective_ret_value_type = ret_value_type;
        if (m.throws_type) |error_type| {
            // Throwing methods return via sret (error union)
            effective_ret_type = .ptr;
            // Create error union type info
            effective_ret_value_type = .{
                .error_union_type = .{
                    .success_type = ret_type,
                    .success_struct_type = ret_type_name,
                    .error_enum_type = error_type,
                },
            };
        }

        return FuncInfo{
            .return_type = effective_ret_type,
            .return_type_name = ret_type_name,
            .return_value_type = effective_ret_value_type,
            .param_types = param_types,
            .ir_generated = ir_generated,
        };
    }

    /// Register a method as a function with mangled name: TypeName$methodName
    /// For interface methods (qualified_name set), also registers TypeName$Interface.methodName
    fn registerMethod(self: *AstToIr, type_name: []const u8, method: ast.MethodDecl) !void {
        // Save current type context for Self resolution
        const saved_type = self.current_type_name;
        self.current_type_name = type_name;
        defer self.current_type_name = saved_type;

        // If method has a qualified name (Interface.methodName), verify the type declares conformance
        if (method.qualified_name) |qualified| {
            if (std.mem.indexOf(u8, qualified, ".")) |dot_pos| {
                const interface_name = qualified[0..dot_pos];
                // Check if type conforms to this interface (directly or transitively via extends)
                if (!self.typeConformsTo(type_name, interface_name)) {
                    const msg = std.fmt.allocPrint(self.allocator, "Type '{s}' implements '{s}.{s}' but does not declare conformance to '{s}'", .{
                        type_name,
                        interface_name,
                        method.name,
                        interface_name,
                    }) catch {
                        self.reportError(.E015, method.name);
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
                        .message_allocated = true,
                    };
                    return error.SemanticError;
                }
            }
        }

        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Check if method is already registered (avoid double allocation)
        if (self.func_map.contains(mangled_name)) {
            // Still need to check if interface qualified name needs registering
            if (method.qualified_name) |qualified| {
                const qualified_mangled = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, qualified });
                try self.module.trackString(qualified_mangled);
                if (!self.func_map.contains(qualified_mangled)) {
                    const func_info = self.func_map.get(mangled_name).?;
                    try self.func_map.put(self.allocator, qualified_mangled, func_info);
                    debug.astToIr("Registered interface method '{s}' as '{s}' (added qualified name)", .{ method.name, qualified_mangled });
                }
            }
            return;
        }

        const func_info = try self.buildMethodFuncInfo(type_name, method, true);

        // Free old param_types if overwriting an existing entry (e.g., from external registration)
        if (self.func_map.get(mangled_name)) |old_info| {
            if (old_info.param_types.len > 0) {
                self.allocator.free(old_info.param_types);
            }
        }

        try self.func_map.put(self.allocator, mangled_name, func_info);
        debug.astToIr("Registered method '{s}' as '{s}' returning {s}", .{ method.name, mangled_name, func_info.return_type.toIrName() });
    }

    /// Register method signature only (for lazy generation of monomorphized types).
    /// Stores the method in pending_methods for on-demand IR generation.
    /// Returns true if bindings were stored (method was newly registered), false if already registered.
    fn registerMethodSignatureOnly(
        self: *AstToIr,
        type_name: []const u8,
        method: *const ast.MethodDecl,
        bindings: []const PendingMethod.GenericBinding,
    ) !bool {
        // Save current type context for Self resolution
        const saved_type = self.current_type_name;
        self.current_type_name = type_name;
        defer self.current_type_name = saved_type;

        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Check if method is already registered
        if (self.func_map.contains(mangled_name)) {
            return false;
        }

        const func_info = try self.buildMethodFuncInfo(type_name, method, false);

        try self.func_map.put(self.allocator, mangled_name, func_info);

        // Queue for lazy generation
        try self.pending_methods.put(self.allocator, mangled_name, .{
            .type_name = type_name,
            .method = method,
            .generic_bindings = bindings,
        });
        debug.astToIr("Registered lazy method '{s}' as '{s}' returning {s}", .{ method.name, mangled_name, func_info.return_type.toIrName() });
        return true;
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
        converted_runtime_constants: std.StringHashMapUnmanaged(TypedValue),
        in_stdlib_method: bool,
        loop_end_block: ?u32,
        loop_cond_block: ?u32,
        temporary_strings: std.ArrayListUnmanaged(ir.Value),
        current_func_throws_type: ?[]const u8,
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
            .converted_runtime_constants = self.converted_runtime_constants,
            .in_stdlib_method = self.in_stdlib_method,
            .loop_end_block = self.loop_end_block,
            .loop_cond_block = self.loop_cond_block,
            .temporary_strings = self.temporary_strings,
            .current_func_throws_type = self.current_func_throws_type,
        };

        // Clear var_map, converted_runtime_constants, and temporary_strings for new method
        self.var_map = .{};
        self.converted_runtime_constants = .{};
        self.temporary_strings = .{};

        return saved;
    }

    /// Restore conversion context after generating a method
    fn restoreConversionContext(self: *AstToIr, saved: SavedContext) void {
        // Free the temporary var_map, converted_runtime_constants, and temporary_strings used for method conversion
        self.var_map.deinit(self.allocator);
        self.converted_runtime_constants.deinit(self.allocator);
        self.temporary_strings.deinit(self.allocator);

        // Restore all saved state
        self.current_func = if (saved.func_idx) |idx| &self.module.functions.items[idx] else null;
        self.current_func_name = saved.func_name;
        self.sret_ptr = saved.sret_ptr;
        self.sret_size = saved.sret_size;
        self.sret_optional_info = saved.sret_optional_info;
        self.self_ptr = saved.self_ptr;
        self.var_map = saved.var_map;
        self.converted_runtime_constants = saved.converted_runtime_constants;
        self.in_stdlib_method = saved.in_stdlib_method;
        self.loop_end_block = saved.loop_end_block;
        self.loop_cond_block = saved.loop_cond_block;
        self.temporary_strings = saved.temporary_strings;
        self.current_func_throws_type = saved.current_func_throws_type;
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

        debug.astToIr("Lazily generated method '{s}'", .{mangled_name});
    }

    // ------------------------------------------------------------------------
    // Function/Method Conversion Helpers
    // ------------------------------------------------------------------------

    /// Result of analyzing return type for sret handling
    const SretInfo = struct {
        uses_sret: bool,
        struct_size: i32,
        optional_info: ?OptionalInfo,
        ir_type: ir.Type,
    };

    /// Analyze return type to determine if sret is needed and get IR type
    /// resolve_names: if true, resolves type names through generic params (for methods)
    fn analyzeReturnType(self: *AstToIr, return_type: ?ast.TypeExpr, resolve_names: bool) !SretInfo {
        if (return_type == null) {
            return .{ .uses_sret = false, .struct_size = 0, .optional_info = null, .ir_type = .void };
        }

        const rt = return_type.?;
        var uses_sret = false;
        var sret_struct_size: i32 = 0;
        var sret_opt_info: ?OptionalInfo = null;

        switch (rt) {
            .simple => |name| {
                const resolved = if (resolve_names) self.resolveTypeName(name) else name;
                if (self.type_map.get(resolved)) |type_info| {
                    if (type_info == .struct_type) {
                        uses_sret = true;
                        sret_struct_size = type_info.struct_type.size;
                    }
                }
            },
            .generic => |gen| {
                // For generic types like "Array of int", build the monomorphized name
                var resolved_args: [8][]const u8 = undefined;
                for (gen.type_args, 0..) |arg, i| {
                    if (i >= 8) break;
                    resolved_args[i] = if (resolve_names) self.resolveTypeName(arg) else arg;
                }
                const mono_name = try self.getOrCreateMonomorphizedType(gen.base_type, resolved_args[0..gen.type_args.len]);
                if (self.type_map.get(mono_name)) |type_info| {
                    if (type_info == .struct_type) {
                        uses_sret = true;
                        sret_struct_size = type_info.struct_type.size;
                    }
                }
            },
            .optional => |wrapped| {
                uses_sret = true;
                sret_struct_size = 8 + self.getTypeSize(wrapped.*);
                const wrapped_struct_name = getStructNameFromTypeExpr(wrapped.*);
                sret_opt_info = .{
                    .wrapped = getIrTypeFromTypeExpr(wrapped.*),
                    .wrapped_struct_type = wrapped_struct_name,
                };
            },
            .error_union => |eu| {
                // Error unions use sret like optionals
                uses_sret = true;
                sret_struct_size = 8 + self.getTypeSize(eu.success_type.*);
            },
            .function_type => {
                // Function types are returned as pointers, no sret needed
            },
        }

        const ir_type: ir.Type = switch (rt) {
            .simple, .generic => blk: {
                const base_name = typeExprBaseTypeName(rt).?;
                const resolved = if (resolve_names) self.resolveTypeName(base_name) else base_name;
                break :blk try self.lookupIrType(resolved);
            },
            .optional, .error_union, .function_type => .ptr,
        };

        return .{
            .uses_sret = uses_sret,
            .struct_size = sret_struct_size,
            .optional_info = sret_opt_info,
            .ir_type = ir_type,
        };
    }

    /// Setup common function state (sret, entry block, var map)
    fn setupFunctionState(self: *AstToIr, ir_func: *ir.Function, func_name: []const u8, sret_info: SretInfo) !i32 {
        self.current_func = ir_func;
        self.current_func_name = func_name;
        self.var_map.clearRetainingCapacity();
        self.converted_runtime_constants.clearRetainingCapacity();
        _ = try ir_func.addBlock("entry");

        // Reset sret state
        self.sret_ptr = null;
        self.sret_size = 0;
        self.sret_optional_info = null;

        // If returning struct, first parameter is sret pointer
        var param_offset: i32 = 0;
        if (sret_info.uses_sret) {
            self.sret_ptr = try ir_func.emitParam(0, .ptr);
            self.sret_size = sret_info.struct_size;
            self.sret_optional_info = sret_info.optional_info;
            param_offset = 1;
        }

        return param_offset;
    }

    /// Convert function body and add implicit return for void functions
    fn convertBodyAndFinalize(self: *AstToIr, body: []const ast.Statement, ret_type: ir.Type) !void {
        // Push a new label scope for the function body
        try self.pushLabelScope();
        errdefer self.popLabelScope();

        for (body) |stmt| {
            try self.convertStatement(stmt);
        }

        // Pop the label scope
        self.popLabelScope();

        // For void functions, add implicit return if needed
        if (ret_type == .void) {
            const block = self.func().currentBlock() orelse return;
            const needs_implicit_ret = block.instructions.items.len == 0 or
                block.instructions.items[block.instructions.items.len - 1].op != .ret;
            if (needs_implicit_ret) {
                try self.func().emitRet(null);
            }
        }
    }

    /// Check for unused variables and report errors
    fn checkUnusedVariables(self: *AstToIr) !void {
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            if (!entry.value_ptr.used and !std.mem.eql(u8, entry.key_ptr.*, "_")) {
                debug.astToIr("error: unused variable '{s}'\n", .{entry.key_ptr.*});
                self.reportError(.E014, entry.key_ptr.*);
                return error.UnusedVariable;
            }
        }
    }

    // ------------------------------------------------------------------------
    // Return Path Analysis
    // ------------------------------------------------------------------------

    /// Check if all execution paths through a statement list end with a return
    fn allPathsReturn(body: []const ast.Statement) bool {
        if (body.len == 0) return false;

        // Check if the last statement guarantees a return
        const last_stmt = body[body.len - 1];
        return statementReturns(last_stmt);
    }

    /// Check if a statement guarantees a return on all paths
    fn statementReturns(stmt: ast.Statement) bool {
        switch (stmt.kind) {
            .@"return" => return true,
            .throw_stmt => return true, // throw also exits the function
            .if_stmt => |if_s| {
                // Both branches must return, and there must be an else branch
                if (!hasElse(if_s.children)) return false;

                // Check then branch
                if (!allPathsReturn(getPrimaryBody(if_s.children))) return false;

                // Check else branch
                if (getElseBody(if_s.children)) |else_body| {
                    if (!allPathsReturn(else_body)) return false;
                }

                // Check else-if chain
                if (getElseIf(if_s.children)) |else_if| {
                    if (!statementReturns(.{ .kind = .{ .if_stmt = else_if }, .line = 0, .column = 0 })) return false;
                }

                return true;
            },
            .match_stmt => |match_s| {
                // All cases must return
                var case_count: usize = 0;
                for (match_s.children) |child| {
                    if (child.role == .match_case) {
                        case_count += 1;
                        // Match case body is a single statement in child.statements
                        if (child.statements.len > 0) {
                            if (!statementReturns(child.statements[0])) return false;
                        } else {
                            return false;
                        }
                    }
                }
                // If there's a default case, it must also return
                if (getDefaultCase(match_s.children)) |default_stmts| {
                    if (default_stmts.len > 0) {
                        return statementReturns(default_stmts[0]);
                    }
                    return false;
                }
                // No default means we assume exhaustiveness (validated elsewhere)
                // Return true if all explicit cases return
                return case_count > 0;
            },
            .do_catch_stmt => |do_catch| {
                // The do block and all catch blocks must return
                if (!allPathsReturn(getPrimaryBody(do_catch.children))) return false;
                for (do_catch.children) |child| {
                    if (child.role == .catch_handler) {
                        if (!allPathsReturn(child.statements)) return false;
                    }
                }
                return true;
            },
            // Other statements don't guarantee a return
            else => return false,
        }
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

        // Track if this method throws (for throw statement validation)
        self.current_func_throws_type = method.throws_type;
        defer self.current_func_throws_type = null;

        // Generate mangled name using short method name (matches registerMethod)
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method.name });
        try self.module.trackString(mangled_name);

        // Analyze return type for sret handling (resolve_names=true for methods)
        var sret_info = try self.analyzeReturnType(method.return_type, true);

        // If method throws, we need to use sret for the error union
        if (method.throws_type) |error_type| {
            // Get the error type size
            const error_size = if (self.type_map.get(error_type)) |type_info|
                if (type_info == .struct_type) type_info.struct_type.size else 8
            else
                8;

            // Get the success type size (use 8 for primitives if not already using sret)
            const success_size = if (sret_info.uses_sret) sret_info.struct_size else 8;

            // Error union layout: 8 bytes tag + max(success_size, error_size)
            const payload_size = @max(success_size, error_size);
            sret_info.uses_sret = true;
            sret_info.struct_size = 8 + payload_size;
            sret_info.optional_info = null; // We don't use optional_info for error unions
        }

        const ir_func = try self.module.addFunctionWithExport(mangled_name, sret_info.ir_type, method.is_export);

        self.self_ptr = null;
        var param_offset = try self.setupFunctionState(ir_func, mangled_name, sret_info);

        // Register implicit self parameter for instance methods
        if (!method.is_static) {
            const self_val = try ir_func.emitParam(param_offset, .ptr);
            try ir_func.setValueName(self_val, "self");
            self.self_ptr = self_val;

            // Register self as a variable for explicit self.field access
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

        // Convert body and add implicit return for void methods
        try self.convertBodyAndFinalize(method.body, sret_info.ir_type);

        // Skip unused variable check for 'self' - it's always implicitly used
        if (self.var_map.getPtr("self")) |self_info| {
            self_info.used = true;
        }

        try self.checkUnusedVariables();

        // Clear method context
        self.self_ptr = null;
    }

    // ------------------------------------------------------------------------
    // Function Conversion
    // ------------------------------------------------------------------------

    fn convertFunction(self: *AstToIr, decl: ast.FunctionDecl) !void {
        // Check that all paths return for non-void functions
        if (decl.return_type != null) {
            if (!allPathsReturn(decl.body)) {
                self.reportError(.E037, decl.name);
                return error.MissingReturn;
            }
        }

        // Track if this function throws (for throw statement validation)
        self.current_func_throws_type = decl.throws_type;
        defer self.current_func_throws_type = null;

        // Analyze return type for sret handling (resolve_names=false for functions)
        var sret_info = try self.analyzeReturnType(decl.return_type, false);

        // If function throws, we need to use sret for the error union
        if (decl.throws_type) |error_type| {
            // Get the error type size
            const error_size = if (self.type_map.get(error_type)) |type_info|
                if (type_info == .struct_type) type_info.struct_type.size else 8
            else
                8;

            // Get the success type size (use 8 for primitives if not already using sret)
            const success_size = if (sret_info.uses_sret) sret_info.struct_size else 8;

            // Error union layout: 8 bytes tag + max(success_size, error_size)
            const payload_size = @max(success_size, error_size);
            sret_info.uses_sret = true;
            sret_info.struct_size = 8 + payload_size;
            sret_info.optional_info = null; // We don't use optional_info for error unions
        }

        const ir_func = try self.module.addFunctionWithExport(decl.name, sret_info.ir_type, decl.is_export);
        const param_offset = try self.setupFunctionState(ir_func, decl.name, sret_info);

        // Check if this is main with args parameter
        const is_main_with_args = std.mem.eql(u8, decl.name, "main") and
            decl.params.len == 1 and
            self.isArgsArrayOfString(decl.params[0].type_expr);

        if (is_main_with_args) {
            // For main(args array of String), call __get_command_args() and bind result
            try self.registerMainArgsParameter(decl.params[0]);
        } else {
            // Register parameters normally (offset by 1 if using sret)
            for (decl.params, 0..) |param, i| {
                try self.registerParameter(param, @as(i32, @intCast(i)) + param_offset);
            }
        }

        // Convert body and add implicit return for void functions
        try self.convertBodyAndFinalize(decl.body, sret_info.ir_type);

        try self.checkUnusedVariables();
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
            .array_type, .optional_type, .error_union_type => {
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
            .function_type => {
                // Function pointers are passed as pointers - store in a stack slot
                const param_val = try self.func().emitParam(idx, .ptr);
                try self.func().setValueName(param_val, param.name);
                const ptr = try self.func().emitAlloca(.ptr);
                try self.func().setValueName(ptr, param.name);
                try self.func().emitStore(ptr, param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    ptr,
                    value_type,
                    true,
                    true, // uses_slot for function pointers
                ));
            },
        }
    }

    /// Check if a type expression is "array of String" (the type for main's args parameter)
    fn isArgsArrayOfString(self: *AstToIr, type_expr: ast.TypeExpr) bool {
        _ = self;
        switch (type_expr) {
            .generic => |gen| {
                // Check for "Array of String" or "array of String"
                if (!std.mem.eql(u8, gen.base_type, "Array") and !std.mem.eql(u8, gen.base_type, "array")) {
                    return false;
                }
                // Must have exactly one type argument (the element type)
                if (gen.type_args.len != 1) return false;
                // Element type must be "String" or "string"
                return std.mem.eql(u8, gen.type_args[0], "String") or std.mem.eql(u8, gen.type_args[0], "string");
            },
            else => return false,
        }
    }

    /// Register the args parameter for main(args array of String)
    /// Generates IR to get command line arguments via Windows APIs
    fn registerMainArgsParameter(self: *AstToIr, param: ast.ParamDecl) !void {
        // Generate IR to get the array pointer (uses extern_call to Windows APIs)
        const array_ptr = try intrinsics.emitGetCommandArgs(self);

        // Store the pointer in a stack slot for the args variable
        const slot_ptr = try self.func().emitAlloca(.ptr);
        try self.func().setValueName(slot_ptr, param.name);
        try self.func().emitStore(slot_ptr, array_ptr);

        // Register the variable with Array$String type info
        const value_type = ValueType{ .struct_type = "Array$String" };
        try self.var_map.put(self.allocator, param.name, VarInfo.init(
            slot_ptr,
            value_type,
            true, // is_initialized
            true, // uses_slot
        ));
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
                _ = try self.convertMethodCallExpr(mcall);
                // Clean up any temporary strings created during this call
                try self.cleanupTemporaryStrings();
            },
            .if_stmt => |if_s| try self.convertIfStmt(if_s),
            .while_stmt => |while_s| try self.convertWhileStmt(while_s),
            .for_stmt => |for_s| try self.convertForStmt(for_s),
            .break_stmt => |brk| try self.convertBreakStmt(brk),
            .continue_stmt => |cont| try self.convertContinueStmt(cont),
            .else_unwrap_decl => |unwrap| try self.convertElseUnwrapDecl(unwrap),
            .guard_let_decl => |guard| try self.convertGuardLetDecl(guard),
            // Error handling
            .throw_stmt => |throw_s| try self.convertThrowStmt(throw_s),
            .do_catch_stmt => |dc| try self.convertDoCatchStmt(dc),
            // Match statement
            .match_stmt => |match_s| try self.convertMatchStmt(match_s),
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
                .optional, .error_union, .function_type => null,
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
                            .optional, .error_union, .function_type => unreachable,
                        };
                        try self.convertInitableFromArrayLiteralSimple(decl, resolved_type_name);
                        return;
                    }
                }

                // Check for InitableFromDictionaryLiteral transformation
                // Syntax: var m Map from K to V = ["a": 1, "b": 2] (generic) or var m StringIntMap = [...] (simple)
                if (self.typeConformsTo(t_name, "InitableFromDictionaryLiteral")) {
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
                            .optional, .error_union, .function_type => unreachable,
                        };
                        try self.convertInitableFromDictionaryLiteralSimple(decl, resolved_type_name);
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

        // Heap arrays, optionals, and function types use a slot to hold the pointer
        const uses_slot = (init_typed.ty == .array_type and init_typed.ty.array_type.storage == .heap) or
            init_typed.ty == .optional_type or
            init_typed.ty == .function_type;

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
            // Check for returning nil from non-optional function (before any other handling)
            if (expr == .nil_lit and self.sret_optional_info == null) {
                // Get the return type name for the error message
                const ret_type_name = if (self.func().return_type == .i64)
                    "int"
                else if (self.func().return_type == .f64)
                    "float"
                else
                    "non-optional";
                self.reportError(.E041, ret_type_name);
                return error.TypeMismatch;
            }

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
                    } else if (self.current_func_throws_type != null) {
                        // Returning struct init from throwing function - wrap in success result
                        // Write tag = 0 (success) first
                        const zero = try self.func().emitConstI64(0);
                        try self.func().emitStore(sret, zero);
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
                // (non-optional case already handled above)
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

                // If returning from a throwing function, wrap in success result
                if (self.current_func_throws_type != null and self.sret_optional_info == null) {
                    // Write tag = 0 (success) to sret
                    const zero = try self.func().emitConstI64(0);
                    try self.func().emitStore(sret, zero);
                    // Write value at offset 8
                    const value_ptr = try self.func().emitGetFieldPtr(sret, 8);
                    // For struct types, memcpy the contents; for primitives, store the value
                    if (typed_val.ty == .struct_type) {
                        const struct_name = typed_val.ty.struct_type;
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
                    // Mark returned variable as moved so cleanup doesn't free its heap resources
                    if (expr == .identifier) {
                        if (self.var_map.getPtr(expr.identifier)) |var_info| {
                            // Only mark as moved for types with heap resources (Arrays, Strings)
                            if (var_info.ty == .struct_type) {
                                const struct_name = var_info.ty.struct_type;
                                if (std.mem.startsWith(u8, struct_name, "Array$") or
                                    std.mem.eql(u8, struct_name, "Array"))
                                {
                                    var_info.markMoved("return", self.current_line);
                                    if (self.track_memory) {
                                        try self.func().emitTrackMove(expr.identifier);
                                    }
                                }
                            } else if (var_info.ty == .array_type) {
                                var_info.markMoved("return", self.current_line);
                                if (self.track_memory) {
                                    try self.func().emitTrackMove(expr.identifier);
                                }
                            }
                        }
                    }
                    try self.freeHeapAllocations();
                    try self.func().emitRet(sret);
                    return;
                }

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
                        try self.func().emitMemcpy(sret, typed_val.value, struct_helpers.MANAGED_STRING_SIZE);
                        // Set _iterPos to 0 at offset 24
                        const iter_pos_ptr = try self.func().emitGetFieldPtr(sret, 24);
                        try self.func().emitStore(iter_pos_ptr, try self.func().emitConstI64(0));
                    } else {
                        try self.func().emitMemcpy(sret, typed_val.value, self.sret_size);
                    }
                }
                // Mark returned variable as moved so cleanup doesn't free its heap resources
                if (expr == .identifier) {
                    if (self.var_map.getPtr(expr.identifier)) |var_info| {
                        // Only mark as moved for types with heap resources (Arrays, Strings, Maps)
                        if (var_info.ty == .struct_type) {
                            const struct_name = var_info.ty.struct_type;
                            if (std.mem.startsWith(u8, struct_name, "Array$") or
                                std.mem.eql(u8, struct_name, "Array") or
                                std.mem.startsWith(u8, struct_name, "Map$") or
                                std.mem.eql(u8, struct_name, "Map"))
                            {
                                var_info.markMoved("return", self.current_line);
                                if (self.track_memory) {
                                    try self.func().emitTrackMove(expr.identifier);
                                }
                            }
                        } else if (var_info.ty == .array_type) {
                            // Also track moves for internal array types (heap arrays without type annotation)
                            var_info.markMoved("return", self.current_line);
                            if (self.track_memory) {
                                try self.func().emitTrackMove(expr.identifier);
                            }
                        }
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

    /// Free heap memory for a single variable if it owns heap-allocated data.
    /// Handles heap arrays, stdlib Array types, Strings (with COW decref), and cstrings.
    /// Set skip_if_parameter to true when cleaning up scope (parameters are caller-owned).
    /// var_name is used for memory tracking (can be "<expr>" for unnamed temporaries).
    fn freeHeapVar(self: *AstToIr, var_info: VarInfo, skip_if_parameter: bool, var_name: []const u8) !void {
        if (var_info.state == .moved) {
            debug.astToIr("freeHeapVar: skipped (moved)", .{});
            return;
        }

        if (var_info.ty == .array_type) {
            const arr_info = var_info.ty.array_type;
            if (arr_info.storage == .heap) {
                // var_info.ptr points to a stack slot holding a ptr to __ManagedArray
                // __ManagedArray layout: [buffer_ptr, len, capacity] at offsets [0, 8, 16]
                const managed_ptr = try self.func().emitLoad(var_info.ptr.?, .ptr);

                // For arrays of strings, we need to decref each string element before freeing the buffer
                if (arr_info.element_struct_type) |elem_type| {
                    if (std.mem.eql(u8, elem_type, "String")) {
                        try self.emitArrayStringElementsCleanup(managed_ptr);
                    }
                }

                const buffer_ptr = try self.func().emitLoad(managed_ptr, .ptr);
                try self.func().emitHeapFree(buffer_ptr, "array cleanup");
            }
        }

        if (var_info.ty == .struct_type) {
            const struct_name = var_info.ty.struct_type;
            debug.astToIr("freeHeapVar: struct {s}", .{struct_name});

            // Handle stdlib Array types (Array$int, etc.)
            if (std.mem.startsWith(u8, struct_name, "Array$")) {
                if (skip_if_parameter and var_info.is_parameter) return;

                // For Array$String, we need to decref each string element before freeing the buffer
                if (std.mem.eql(u8, struct_name, "Array$String")) {
                    try self.emitArrayStringElementsCleanup(var_info.ptr.?);
                }

                // Buffer pointer is at offset 0 within the inlined __ManagedArray
                const buf_ptr = try self.func().emitLoad(var_info.ptr.?, .ptr);
                try self.func().emitHeapFree(buf_ptr, "array cleanup");
            }

            // Handle String type with COW semantics
            if (self.isStringType(var_info.ty)) {
                if (skip_if_parameter and var_info.is_parameter) return;
                try self.emitStringDecref(var_info.ptr.?, var_name);
            }

            // Handle cstring type cleanup
            if (std.mem.eql(u8, struct_name, "cstring")) {
                if (skip_if_parameter and var_info.is_parameter) return;
                try self.emitCstringCleanup(var_info.ptr.?);
            }

            // Handle stdlib Map types (Map$K$V)
            // Map layout: keys(Array 32b) + values(Array 32b) + states(Array 32b) + count(8) + capacity(8) + iterIndex(8)
            // Each Array layout: buffer_ptr(8) + len(8) + cap(8) + iterIndex(8)
            if (std.mem.startsWith(u8, struct_name, "Map$")) {
                debug.astToIr("freeHeapVar: emitting Map cleanup for {s}", .{struct_name});
                if (skip_if_parameter and var_info.is_parameter) return;
                try self.emitMapCleanup(var_info.ptr.?, struct_name);
            }
        }
    }

    /// Emit cleanup code for Map types, freeing the three internal array buffers.
    /// For Map$String$V, also decrefs each string key element.
    fn emitMapCleanup(self: *AstToIr, map_ptr: ir.Value, struct_name: []const u8) !void {
        // Map layout:
        //   offset 0:  keys array (32 bytes) - Array$KeyType
        //   offset 32: values array (32 bytes) - Array$ValueType
        //   offset 64: states array (32 bytes) - Array$int
        //   offset 96: count (8 bytes)
        //   offset 104: capacity (8 bytes)
        // Each Array: buffer_ptr(8) + len(8) + cap(8) + iterIndex(8)

        // Parse key type from Map$KeyType$ValueType
        const prefix_len = "Map$".len;
        const key_value_part = struct_name[prefix_len..];
        // Find the second $ to separate key and value types
        var dollar_pos: ?usize = null;
        for (key_value_part, 0..) |c, i| {
            if (c == '$') {
                dollar_pos = i;
                break;
            }
        }
        const key_type = if (dollar_pos) |pos| key_value_part[0..pos] else key_value_part;
        const has_string_keys = std.mem.eql(u8, key_type, "String");

        // Get pointer to keys array at offset 0
        const keys_ptr = map_ptr;
        // If keys are strings, cleanup occupied slots only (check states array)
        if (has_string_keys) {
            try self.emitMapStringKeysCleanup(map_ptr);
        }
        // Free the keys buffer (buffer_ptr is at offset 0 of the array struct)
        const keys_buf_ptr = try self.func().emitLoad(keys_ptr, .ptr);
        try self.func().emitHeapFree(keys_buf_ptr, "map keys cleanup");

        // Get pointer to values array at offset 32 and free its buffer
        const values_ptr = try self.func().emitGetFieldPtr(map_ptr, 32);
        const values_buf_ptr = try self.func().emitLoad(values_ptr, .ptr);
        try self.func().emitHeapFree(values_buf_ptr, "map values cleanup");

        // Get pointer to states array at offset 64 and free its buffer
        const states_ptr = try self.func().emitGetFieldPtr(map_ptr, 64);
        const states_buf_ptr = try self.func().emitLoad(states_ptr, .ptr);
        try self.func().emitHeapFree(states_buf_ptr, "map states cleanup");
    }

    /// Emit cleanup code for Map string keys, checking the states array for occupied slots.
    /// Only decrefs string buffers for slots where states[i] == 1 (OCCUPIED).
    fn emitMapStringKeysCleanup(self: *AstToIr, map_ptr: ir.Value) !void {
        // Map layout:
        //   offset 0:  keys array (32 bytes) - buffer_ptr at offset 0
        //   offset 64: states array (32 bytes) - buffer_ptr at offset 64
        //   offset 104: capacity (8 bytes)

        // Load capacity to know how many slots to iterate
        const cap_ptr = try self.func().emitGetFieldPtr(map_ptr, 104);
        const capacity = try self.func().emitLoad(cap_ptr, .i64);

        // Skip cleanup if capacity is 0
        const zero = try self.func().emitConstI64(0);
        const is_empty = try self.func().emitBinaryOp(.icmp_eq, capacity, zero, .i64);

        // Allocate loop counter on stack
        const counter_ptr = try self.func().emitAlloca(.i64);
        try self.func().emitStore(counter_ptr, zero);

        // Create blocks - all created upfront to ensure stable indices
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("map_str_cleanup_cond");
        const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("map_str_cleanup_body");
        const occupied_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("map_str_cleanup_occupied");
        const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("map_str_cleanup_decref");
        const heap_free_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("map_str_heap_free");
        const next_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("map_str_cleanup_next");
        const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("map_str_cleanup_end");

        // Branch from entry: if empty, skip to end; otherwise go to condition
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_empty }, .{ .block_ref = end_block_idx }, .{ .block_ref = cond_block_idx } },
        });

        // Defer blocks (6 blocks)
        var deferred = try DeferredBlocks.init(self.allocator, 6);
        defer deferred.deinit();
        deferred.deferBlocks(self, 6);

        // Condition block: check if i < capacity
        const cond_cap_ptr = try self.func().emitGetFieldPtr(map_ptr, 104);
        const cond_cap = try self.func().emitLoad(cond_cap_ptr, .i64);
        const cond_i = try self.func().emitLoad(counter_ptr, .i64);
        const done = try self.func().emitBinaryOp(.icmp_ge, cond_i, cond_cap, .i64);

        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = done }, .{ .block_ref = end_block_idx }, .{ .block_ref = body_block_idx } },
        });

        // Restore body block (deferred index 5)
        try deferred.restore(self, 5);

        // Body block: check if states[i] == 1 (OCCUPIED)
        const body_i = try self.func().emitLoad(counter_ptr, .i64);

        // Get states buffer pointer (at offset 64 of map, then offset 0 of Array)
        const states_array_ptr = try self.func().emitGetFieldPtr(map_ptr, 64);
        const states_buf_ptr = try self.func().emitLoad(states_array_ptr, .ptr);

        // Load states[i] - each state is 8 bytes (int)
        const eight = try self.func().emitConstI64(8);
        const state_offset = try self.func().emitBinaryOp(.mul, body_i, eight, .i64);
        const state_ptr = try self.func().emitBinaryOp(.add, states_buf_ptr, state_offset, .ptr);
        const state = try self.func().emitLoad(state_ptr, .i64);

        // Check if state == 1 (OCCUPIED)
        const one = try self.func().emitConstI64(1);
        const is_occupied = try self.func().emitBinaryOp(.icmp_eq, state, one, .i64);

        // If occupied, go to occupied block; otherwise skip to next
        try self.func().blocks.items[body_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_occupied }, .{ .block_ref = occupied_block_idx }, .{ .block_ref = next_block_idx } },
        });

        // Restore occupied block (deferred index 4)
        try deferred.restore(self, 4);

        // Occupied block: get keys[i] and check if it's heap-allocated
        const occ_i = try self.func().emitLoad(counter_ptr, .i64);

        // Get keys buffer pointer (at offset 0 of map)
        const keys_buf_ptr = try self.func().emitLoad(map_ptr, .ptr);

        // Calculate element pointer: buf_ptr + i * 32 (String size)
        const elem_size = try self.func().emitConstI64(32);
        const offset = try self.func().emitBinaryOp(.mul, occ_i, elem_size, .i64);
        const elem_ptr = try self.func().emitBinaryOp(.add, keys_buf_ptr, offset, .ptr);

        // Check if heap mode: cap_flags & 3 == 1
        // cap_flags is at offset 12 in the String
        const cap_flags_ptr = try self.func().emitGetFieldPtr(elem_ptr, 12);
        const cap_flags = try self.func().emitLoad(cap_flags_ptr, .i32);
        const three = try self.func().emitConstI32(3);
        const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
        const one_i32 = try self.func().emitConstI32(1);
        const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

        // If heap, go to decref; otherwise skip to next
        try self.func().blocks.items[occupied_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_heap }, .{ .block_ref = decref_block_idx }, .{ .block_ref = next_block_idx } },
        });

        // Restore decref block (deferred index 3)
        try deferred.restore(self, 3);

        // Decref block: decrement refcount and check if zero
        const decref_i = try self.func().emitLoad(counter_ptr, .i64);
        const decref_keys_buf_ptr = try self.func().emitLoad(map_ptr, .ptr);
        const decref_offset = try self.func().emitBinaryOp(.mul, decref_i, elem_size, .i64);
        const decref_elem_ptr = try self.func().emitBinaryOp(.add, decref_keys_buf_ptr, decref_offset, .ptr);

        // Refcount is at offset 16 in the String
        const ref_ptr = try self.func().emitGetFieldPtr(decref_elem_ptr, 16);
        const old_ref = try self.func().emitLoad(ref_ptr, .i32);
        const one_ref = try self.func().emitConstI32(1);
        const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one_ref, .i32);
        try self.func().emitStoreI32(ref_ptr, new_ref);

        // Emit tracking call for decref
        if (self.track_memory) {
            try self.func().emitTrackDecref("<map key>", new_ref);
        }

        // Check if refcount is now zero
        const zero_ref = try self.func().emitConstI32(0);
        const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero_ref, .i32);

        // Branch: if zero, go to heap_free; otherwise go to next
        try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_zero }, .{ .block_ref = heap_free_block_idx }, .{ .block_ref = next_block_idx } },
        });

        // Restore heap_free block (deferred index 2)
        try deferred.restore(self, 2);

        // Heap free block: recompute elem_ptr (can't use value from previous block)
        const heap_i = try self.func().emitLoad(counter_ptr, .i64);
        const heap_keys_buf_ptr = try self.func().emitLoad(map_ptr, .ptr);
        const heap_elem_size = try self.func().emitConstI64(32);
        const heap_offset = try self.func().emitBinaryOp(.mul, heap_i, heap_elem_size, .i64);
        const heap_elem_ptr = try self.func().emitBinaryOp(.add, heap_keys_buf_ptr, heap_offset, .ptr);

        // Free the buffer and jump to next
        const str_buf_ptr = try self.func().emitLoad(heap_elem_ptr, .ptr);
        try self.func().emitHeapFree(str_buf_ptr, "map string key cleanup");
        try self.func().emitBr(next_block_idx);

        // Restore next block (deferred index 1)
        try deferred.restore(self, 1);

        // Next block: increment counter and branch to cond
        const next_i = try self.func().emitLoad(counter_ptr, .i64);
        const next_one = try self.func().emitConstI64(1);
        const next_val = try self.func().emitBinaryOp(.add, next_i, next_one, .i64);
        try self.func().emitStore(counter_ptr, next_val);
        try self.func().emitBr(cond_block_idx);

        // Restore end block
        try deferred.restore(self, 0);
    }

    fn freeHeapVars(self: *AstToIr, exclude_vars: ?*std.StringHashMapUnmanaged(OwnershipState)) !void {
        // Borrow checking: first clear borrows from slice variables that are going out of scope
        self.clearSliceBorrows(exclude_vars);

        // Borrow checking: verify no borrowed strings go out of scope
        try self.checkNoOutstandingBorrows();

        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            debug.astToIr("freeHeapVars: checking '{s}'", .{entry.key_ptr.*});
            if (exclude_vars) |excluded| {
                if (excluded.contains(entry.key_ptr.*)) continue;
            }
            try self.freeHeapVar(entry.value_ptr.*, true, entry.key_ptr.*);
        }
    }

    fn freeHeapAllocations(self: *AstToIr) !void {
        try self.freeHeapVars(null);
        // Clean up any remaining temporary strings (e.g., method call arguments)
        try self.cleanupTemporaryStrings();
        // Also clean up runtime-initialized constants that have heap resources (Maps, Arrays, etc.)
        try self.freeRuntimeConstants();
    }

    /// Free heap resources for runtime-initialized constants (Maps, Arrays, Strings)
    fn freeRuntimeConstants(self: *AstToIr) !void {
        var iter = self.converted_runtime_constants.iterator();
        while (iter.next()) |entry| {
            const typed_value = entry.value_ptr.*;
            // Create a temporary VarInfo to reuse freeHeapVar logic
            const var_info = VarInfo.init(
                typed_value.value,
                typed_value.ty,
                false, // is_mutable
                false, // uses_slot
            );
            try self.freeHeapVar(var_info, false, "<const>");
        }
    }

    fn freeLoopScopedHeapVars(self: *AstToIr, pre_loop_vars: *std.StringHashMapUnmanaged(OwnershipState)) !void {
        try self.freeHeapVars(pre_loop_vars);
    }

    /// Remove loop-scoped variables from var_map (variables not in pre_loop_vars).
    fn removeLoopScopedVars(self: *AstToIr, pre_loop_vars: *std.StringHashMapUnmanaged(OwnershipState)) !void {
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

            // Free old heap memory AFTER evaluating RHS
            try self.freeHeapVar(var_info.*, false, assign.target);

            const is_reference = var_info.ty == .struct_type or var_info.ty == .array_type or var_info.ty == .optional_type;
            if (is_reference and !var_info.uses_slot) {
                // For struct reassignment, we need to copy the data to the original stack location
                // Just updating var_info.ptr would cause loops to use stale data
                if (var_info.ty == .struct_type) {
                    const struct_name = var_info.ty.struct_type;
                    const struct_info_result = try self.lookupStructInfo(struct_name);
                    const size = struct_info_result.size;
                    try self.func().emitMemcpy(var_info.ptr.?, value_typed.value, size);
                    // Handle String refcount - the new value's refcount is already correct from expression evaluation
                    // but we need to incref if copying from another variable
                    if (assign.value == .identifier or assign.value == .field_access) {
                        if (std.mem.eql(u8, struct_name, "String") or std.mem.eql(u8, struct_name, "__ManagedString")) {
                            try self.emitStringIncref(var_info.ptr.?, assign.target);
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
                try self.func().emitStore(var_info.ptr.?, value_typed.value);
            }
            if (is_reference) {
                var_info.ty = value_typed.ty;
            }

            // COW: If assigning a String from another variable/field, incref the new value
            if (self.isStringType(value_typed.ty) and (assign.value == .identifier or assign.value == .field_access)) {
                try self.emitStringIncref(var_info.ptr.?, assign.target);
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
        const struct_info = self.lookupStructInfo(type_name) catch return true;
        const field_info = self.lookupField(struct_info, field_name) catch return true;
        return field_info.is_mutable;
    }

    fn convertFieldAssign(self: *AstToIr, assign: ast.FieldAssign) ConvertError!void {
        // Check if base is an immutable variable (let struct)
        if (assign.base.* == .identifier) {
            const var_name = assign.base.identifier;
            if (self.var_map.get(var_name)) |var_info| {
                if (!var_info.is_mutable) {
                    self.reportError(.E009, var_name);
                    return error.ImmutableAssign;
                }
            }
        }

        const base = try self.convertExpression(assign.base.*);
        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type, .error_union_type, .function_type => {
                std.debug.print("[AST->IR] convertFieldAssign: expected struct type for field '{s}'\n", .{assign.field_name});
                self.reportError(.E006, assign.field_name);
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try self.lookupField(struct_info, assign.field_name);

        // Check field visibility: unexported fields can only be accessed within the type's methods
        const is_inside_type = self.current_type_name != null and std.mem.eql(u8, self.current_type_name.?, type_name);
        if (!field_info.is_export and !is_inside_type) {
            const msg = std.fmt.allocPrint(self.allocator, "{s}' outside of type '{s}", .{ assign.field_name, type_name }) catch {
                self.reportError(.E050, assign.field_name);
                return error.SemanticError;
            };
            self.reportError(.E050, msg);
            return error.SemanticError;
        }

        // Check if field is mutable
        if (!field_info.is_mutable) {
            self.reportError(.E009, assign.field_name);
            return error.ImmutableAssign;
        }

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
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(if_stmt.block.identifier);

        // Convert condition expression
        const cond_typed = try self.convertExpression(if_stmt.condition);

        // Clean up any temporary strings created during condition evaluation
        // (e.g., lookup keys in `if let v = m.get("key")`)
        try self.cleanupTemporaryStrings();

        // For if-let, we need to check the optional's tag
        const is_if_let = if_stmt.binding_name != null;
        const condition_value = if (is_if_let)
            try self.emitOptionalHasValue(cond_typed.value)
        else
            cond_typed.value;

        // Determine what blocks we need
        const has_else_block = hasElse(if_stmt.children);

        // Save entry block index - we'll emit br_cond later with correct target indices
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

        // Create then block
        const then_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock(if (is_if_let) "if_let_then" else "then");

        // Create else block (if needed) - actual index determined after then body
        if (has_else_block or is_if_let) {
            _ = try self.func().addBlock(if (is_if_let) "if_let_else" else "else");
        }

        // Create end block - actual index determined after all bodies
        _ = try self.func().addBlock(if (is_if_let) "if_let_end" else "end");

        // DON'T emit br_cond yet - nested ifs in body will shift block indices
        // We'll emit it after all blocks are restored with correct indices

        // Defer blocks: end always, else conditionally
        const deferred_count: usize = if (has_else_block or is_if_let) 2 else 1;
        var deferred = try DeferredBlocks.init(self.allocator, deferred_count);
        defer deferred.deinit();
        deferred.deferBlocks(self, deferred_count);

        // Now current block is "then" block
        // For if-let, bind the unwrapped value before executing body
        if (if_stmt.binding_name) |binding_name| {
            // Validate that the expression is an optional type
            if (cond_typed.ty != .optional_type) {
                // Get a readable name for the expression being checked
                const expr_name = if (if_stmt.condition == .identifier)
                    if_stmt.condition.identifier
                else
                    "expression";
                const type_name = cond_typed.ty.getTypeName() orelse @tagName(cond_typed.ty);
                const msg = std.fmt.allocPrint(self.allocator, "'{s}' has type '{s}', not optional", .{ expr_name, type_name }) catch expr_name;
                self.reportError(.E039, msg);
                return error.TypeMismatch;
            }
            const opt_info = cond_typed.ty.optional_type;
            const wrapped_type = opt_info.wrapped;
            const value_ptr = try self.func().emitGetFieldPtr(cond_typed.value, 8);
            const var_value_type = getOptionalWrappedValueType(opt_info);

            if (opt_info.wrapped_struct_type != null) {
                // For struct types, the struct data is stored inline after the tag (at offset 8).
                // value_ptr already points to this inline struct data.
                // Use initParam to mark as borrowed - we don't own this memory, the container does.
                // This prevents cleanup from freeing/decref'ing memory we don't own (e.g., map keys).
                try self.func().setValueName(value_ptr, binding_name);
                try self.var_map.put(self.allocator, binding_name, VarInfo.initParam(value_ptr, var_value_type, true, false));
            } else {
                // For primitive/enum types, load the value and store in a new slot
                const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);
                const binding_ptr = try self.func().emitAlloca(wrapped_type);
                try self.func().setValueName(binding_ptr, binding_name);
                try self.func().emitStore(binding_ptr, unwrapped_value);
                try self.var_map.put(self.allocator, binding_name, VarInfo.init(binding_ptr, var_value_type, true, false));
            }
        }

        // Convert then body statements (push new scope for nested labels)
        try self.pushLabelScope();
        for (getPrimaryBody(if_stmt.children)) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

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
        if (has_else_block or is_if_let) {
            actual_else_block_idx = @intCast(self.func().blocks.items.len);
            // else_block is at index 1 (second popped)
            try deferred.restore(self, 1);

            // Check for else-if chain (not allowed for if-let)
            if (getElseIf(if_stmt.children)) |else_if| {
                try self.convertIfStmt(else_if);
            } else if (getElseBody(if_stmt.children)) |else_body| {
                // Push new scope for else body labels
                try self.pushLabelScope();
                for (else_body) |stmt| {
                    try self.convertStatement(stmt);
                }
                self.popLabelScope();
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
        // end_block is at index 0 (first popped)
        try deferred.restore(self, 0);

        // NOW emit br_cond with correct block indices (after nested ifs shifted things)
        const branch_target_if_false = if (has_else_block or is_if_let) actual_else_block_idx else actual_end_block_idx;
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
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(while_stmt.block.identifier);

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

        // Clean up any temporary strings created during condition evaluation
        try self.cleanupTemporaryStrings();

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
        if (while_stmt.block.identifier) |label| {
            try self.labeled_loops.append(self.allocator, .{
                .label = label,
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

        // Convert body statements (body block is current, with new label scope)
        try self.pushLabelScope();
        for (getPrimaryBody(while_stmt.children)) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

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
        try self.removeLoopScopedVars(&pre_loop_vars);

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
        if (while_stmt.block.identifier != null) {
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
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(for_stmt.block.identifier);

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

        // Check if optional result has a value (continue loop) or is nil (exit loop)
        const has_value = try self.emitOptionalHasValue(next_result.value);

        // Create body block
        const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forbody");

        // Save loop context
        const saved_end_block = self.loop_end_block;
        const saved_cond_block = self.loop_cond_block;
        self.loop_cond_block = cond_block_idx;
        self.loop_end_block = 0xFFFFFFFF; // sentinel for patching

        // Register labeled loop for break/continue with labels
        if (for_stmt.block.identifier) |label| {
            try self.labeled_loops.append(self.allocator, .{
                .label = label,
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
            .primitive, .struct_type, .array_type, .enum_type, .error_union_type, .function_type => OptionalInfo{ .wrapped = .i64 },
        };
        const value_offset = try self.func().emitConstI64(8);
        const value_ptr = try self.func().emitBinaryOp(.add, next_result.value, value_offset, .ptr);

        // Register the loop variable
        // For struct types, use the pointer directly (struct data is inline in the optional)
        // For primitive/enum types, load the value and store in a new slot
        const var_slot: ir.Value = if (opt_info.wrapped_struct_type != null) blk: {
            // For struct types, the payload is the struct data inline
            break :blk value_ptr;
        } else blk: {
            const element_value = try self.func().emitLoad(value_ptr, opt_info.wrapped);
            const slot = try self.func().emitAlloca(opt_info.wrapped);
            try self.func().emitStore(slot, element_value);
            break :blk slot;
        };

        const var_type = getOptionalWrappedValueType(opt_info);
        try self.var_map.put(self.allocator, for_stmt.var_name, VarInfo.init(var_slot, var_type, false, false));

        // Convert body statements (with new label scope)
        try self.pushLabelScope();
        for (getPrimaryBody(for_stmt.children)) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

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
        try self.removeLoopScopedVars(&pre_loop_vars);

        // Now create continuation block
        const cont_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forcont");

        // Emit conditional branch in condition block: if has_value, go to body; else fall through to cont
        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = has_value }, .{ .block_ref = body_block_idx }, .{ .block_ref = cont_block_idx } },
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
        if (for_stmt.block.identifier != null) {
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

    /// Validate a match statement for semantic errors
    /// Checks: fallthrough with return, exhaustiveness, duplicate patterns, type mismatch, default position
    fn validateMatchStmt(self: *AstToIr, match_stmt: ast.MatchStmt) ConvertError!void {
        const saved_line = self.current_line;
        const saved_column = self.current_column;
        defer {
            self.current_line = saved_line;
            self.current_column = saved_column;
        }

        const has_default = hasDefaultCase(match_stmt.children);

        // Check 1: default case must be last (if present, there should be no cases after it)
        // The parser already puts default_case in a separate field, so if default_case is set
        // and there are cases, the default is after all cases - which is correct.
        // We need to check if there's a "default" in the cases array (which would be an error)
        // Actually, looking at the AST, default_case is separate, so we need to check if
        // all patterns in cases are not "default" patterns.
        // The spec says "default must be last" - this is enforced by the parser structure.
        // However, we should verify the parser doesn't allow default patterns in the cases list.
        // For now, this validation passes since the AST separates default_case.

        // Check 2: fallthrough cannot be combined with return
        for (match_stmt.children) |child| {
            if (child.role != .match_case) continue;
            if (child.has_fallthrough) {
                // Check if the body is a return statement
                const body = getChildBody(child);
                if (body.kind == .@"return") {
                    self.current_line = body.line;
                    self.current_column = body.column;
                    self.reportError(.E025, "cannot combine 'fallthrough' with 'return'");
                    return error.SemanticError;
                }
            }
        }

        // Get scrutinee type for type checking and exhaustiveness
        const scrutinee_typed = try self.convertExpressionForTypeCheck(match_stmt.scrutinee);
        const scrutinee_ty = scrutinee_typed.ty;

        // Check 3: pattern type must match scrutinee type
        for (match_stmt.children) |child| {
            if (child.role != .match_case) continue;
            for (child.match_patterns) |pattern| {
                const pattern_typed = try self.convertPatternForTypeCheck(pattern, scrutinee_ty);
                if (!self.typesAreCompatibleForMatch(scrutinee_ty, pattern_typed.ty)) {
                    // Set location to the pattern (approximate - use body line)
                    const body = getChildBody(child);
                    self.current_line = body.line;
                    self.current_column = body.column;
                    const pattern_type_name = pattern_typed.ty.getTypeName() orelse "unknown";
                    const scrutinee_type_name = scrutinee_ty.getTypeName() orelse "unknown";
                    const msg = std.fmt.allocPrint(self.allocator, "pattern type '{s}' does not match scrutinee type '{s}'", .{ pattern_type_name, scrutinee_type_name }) catch {
                        self.reportError(.E028, "pattern type mismatch");
                        return error.SemanticError;
                    };
                    self.last_error = .{
                        .code = .E028,
                        .message = msg,
                        .location = .{
                            .file = self.source_file,
                            .line = @intCast(self.current_line),
                            .column = @intCast(self.current_column),
                        },
                        .message_allocated = true,
                    };
                    return error.SemanticError;
                }
            }
        }

        // Check 4: duplicate patterns
        var seen_patterns = std.StringHashMapUnmanaged(void){};
        defer {
            // Free all allocated pattern keys stored in the map
            var key_iter = seen_patterns.keyIterator();
            while (key_iter.next()) |key| {
                self.allocator.free(key.*);
            }
            seen_patterns.deinit(self.allocator);
        }

        for (match_stmt.children) |child| {
            if (child.role != .match_case) continue;
            for (child.match_patterns) |pattern| {
                const body = getChildBody(child);
                const pattern_key = self.patternToString(pattern) catch continue;

                if (seen_patterns.contains(pattern_key)) {
                    self.current_line = body.line;
                    self.current_column = body.column;
                    self.reportError(.E027, pattern_key);
                    self.allocator.free(pattern_key);
                    return error.SemanticError;
                }
                // Store the key directly (no dupe needed since we don't free it here)
                seen_patterns.put(self.allocator, pattern_key, {}) catch {
                    self.allocator.free(pattern_key);
                    continue;
                };
            }
        }

        // Check 5: enum exhaustiveness (only if no default case)
        if (!has_default) {
            if (scrutinee_ty == .enum_type) {
                const enum_name = scrutinee_ty.enum_type;
                if (self.type_map.get(enum_name)) |type_info| {
                    if (type_info == .enum_type) {
                        const enum_info = type_info.enum_type;
                        var missing_cases = std.ArrayListUnmanaged([]const u8){};
                        defer missing_cases.deinit(self.allocator);

                        // Check each enum member is covered
                        var member_iter = enum_info.members.keyIterator();
                        while (member_iter.next()) |member_name| {
                            var found = false;
                            for (match_stmt.children) |child| {
                                if (child.role != .match_case) continue;
                                for (child.match_patterns) |pattern| {
                                    if (self.patternMatchesEnumMember(pattern, enum_name, member_name.*)) {
                                        found = true;
                                        break;
                                    }
                                }
                                if (found) break;
                            }
                            if (!found) {
                                missing_cases.append(self.allocator, member_name.*) catch continue;
                            }
                        }

                        if (missing_cases.items.len > 0) {
                            // Build missing cases string
                            var missing_str = std.ArrayListUnmanaged(u8){};
                            defer missing_str.deinit(self.allocator);
                            for (missing_cases.items, 0..) |case, i| {
                                if (i > 0) {
                                    missing_str.appendSlice(self.allocator, ", ") catch continue;
                                }
                                missing_str.appendSlice(self.allocator, case) catch continue;
                            }

                            const msg = std.fmt.allocPrint(self.allocator, "match on enum '{s}' is not exhaustive, missing: {s}", .{ enum_name, missing_str.items }) catch {
                                self.reportError(.E026, enum_name);
                                return error.SemanticError;
                            };
                            self.last_error = .{
                                .code = .E026,
                                .message = msg,
                                .location = .{
                                    .file = self.source_file,
                                    .line = @intCast(self.current_line),
                                    .column = @intCast(self.current_column),
                                },
                                .message_allocated = true,
                            };
                            return error.SemanticError;
                        }
                    }
                }
            }
        }
    }

    /// Convert expression just to get type info, without emitting IR
    fn convertExpressionForTypeCheck(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        // For type checking, we can use convertExpression but we need to be careful
        // not to emit actual IR. For now, use the regular conversion.
        return try self.convertExpression(expr);
    }

    /// Convert pattern expression for type checking in the context of a scrutinee type.
    fn convertPatternForTypeCheck(self: *AstToIr, pattern: ast.Expression, scrutinee_ty: ValueType) ConvertError!TypedValue {
        return try self.convertPatternExpression(pattern, scrutinee_ty);
    }

    /// Check if two types are compatible for match pattern matching
    fn typesAreCompatibleForMatch(self: *AstToIr, scrutinee_ty: ValueType, pattern_ty: ValueType) bool {
        _ = self;
        // Same type is always compatible
        if (std.meta.eql(scrutinee_ty, pattern_ty)) return true;

        // Check by type category
        return switch (scrutinee_ty) {
            .primitive => |s_name| switch (pattern_ty) {
                .primitive => |p_name| std.mem.eql(u8, s_name, p_name),
                else => false,
            },
            .enum_type => |s_name| switch (pattern_ty) {
                .enum_type => |p_name| std.mem.eql(u8, s_name, p_name),
                else => false,
            },
            .struct_type => |s_name| switch (pattern_ty) {
                .struct_type => |p_name| std.mem.eql(u8, s_name, p_name),
                else => false,
            },
            else => false,
        };
    }

    /// Convert a pattern expression to a string key for duplicate detection
    fn patternToString(self: *AstToIr, pattern: ast.Expression) ![]const u8 {
        return switch (pattern) {
            .integer => |val| std.fmt.allocPrint(self.allocator, "{d}", .{val}),
            .string_literal => |s| std.fmt.allocPrint(self.allocator, "\"{s}\"", .{s}),
            .bool_lit => |b| std.fmt.allocPrint(self.allocator, "{}", .{b}),
            .field_access => |fa| blk: {
                // Enum member access like Color.red
                const base_name = switch (fa.base.*) {
                    .identifier => |id| id,
                    else => return error.InvalidPattern,
                };
                break :blk std.fmt.allocPrint(self.allocator, "{s}.{s}", .{ base_name, fa.field_name });
            },
            .identifier => |id| self.allocator.dupe(u8, id),
            else => error.InvalidPattern,
        };
    }

    /// Set up local variables for pattern bindings (e.g., `value(n)` extracts `n` from enum associated value)
    fn setupPatternBindings(self: *AstToIr, match_case: ast.MatchCase, scrutinee_value: ir.Value, scrutinee_ty: ValueType) ConvertError!void {
        const enum_name = scrutinee_ty.enum_type;
        const type_info = self.type_map.get(enum_name) orelse return;
        if (type_info != .enum_type) return;
        const enum_info = type_info.enum_type;

        // For each pattern with bindings, set up local variables
        for (match_case.pattern_bindings) |maybe_binding| {
            const binding = maybe_binding orelse continue;

            // Get the case info for this pattern
            const case_info = enum_info.case_info.get(binding.case_name) orelse {
                // Unknown case - report error
                self.reportError(.E034, binding.case_name);
                return error.SemanticError;
            };

            // Validate binding count matches associated values count
            if (binding.bindings.len != case_info.associated_values.len) {
                self.reportError(.E035, binding.case_name);
                return error.SemanticError;
            }

            if (case_info.associated_values.len == 0) continue;

            // Check if this enum has associated values (stored as pointer)
            if (!enum_info.has_associated_values) continue;

            // Extract each associated value and bind it to the local variable
            var offset: i32 = 8; // Start after tag
            for (case_info.associated_values, 0..) |av, ai| {
                const binding_name = binding.bindings[ai];

                // Load the associated value from scrutinee
                const payload_ptr = try self.func().emitGetFieldPtr(scrutinee_value, offset);
                const value = try self.func().emitLoad(payload_ptr, av.ir_type);

                // Create local variable for the binding
                const var_ptr = try self.func().emitAlloca(av.ir_type);
                try self.func().emitStore(var_ptr, value);

                // Determine the ValueType for this binding
                const binding_ty: ValueType = if (std.mem.eql(u8, av.type_name, "int"))
                    .{ .primitive = "int" }
                else if (std.mem.eql(u8, av.type_name, "float"))
                    .{ .primitive = "float" }
                else if (std.mem.eql(u8, av.type_name, "bool"))
                    .{ .primitive = "bool" }
                else
                    .{ .primitive = av.type_name };

                // Register in var_map
                try self.var_map.put(self.allocator, binding_name, types.VarInfo.init(var_ptr, binding_ty, false, false));

                offset += switch (av.ir_type) {
                    .i64, .f64, .ptr => 8,
                    .i32 => 4,
                    .i8 => 1,
                    .void => 0,
                };
            }
        }
    }

    /// Set up pattern bindings for match expressions (similar to setupPatternBindings but for MatchExprCase)
    fn setupPatternBindingsExpr(self: *AstToIr, match_case: ast.MatchExprCase, scrutinee_value: ir.Value, scrutinee_ty: ValueType) ConvertError!void {
        const enum_name = scrutinee_ty.enum_type;
        const type_info = self.type_map.get(enum_name) orelse return;
        if (type_info != .enum_type) return;
        const enum_info = type_info.enum_type;

        // For each pattern with bindings, set up local variables
        for (match_case.pattern_bindings) |maybe_binding| {
            const binding = maybe_binding orelse continue;

            // Get the case info for this pattern
            const case_info = enum_info.case_info.get(binding.case_name) orelse {
                // Unknown case - report error
                self.reportError(.E034, binding.case_name);
                return error.SemanticError;
            };

            // Validate binding count matches associated values count
            if (binding.bindings.len != case_info.associated_values.len) {
                self.reportError(.E035, binding.case_name);
                return error.SemanticError;
            }

            if (case_info.associated_values.len == 0) continue;

            // Check if this enum has associated values (stored as pointer)
            if (!enum_info.has_associated_values) continue;

            // Extract each associated value and bind it to the local variable
            var offset: i32 = 8; // Start after tag
            for (case_info.associated_values, 0..) |av, ai| {
                const binding_name = binding.bindings[ai];

                // Load the associated value from scrutinee
                const payload_ptr = try self.func().emitGetFieldPtr(scrutinee_value, offset);
                const value = try self.func().emitLoad(payload_ptr, av.ir_type);

                // Create local variable for the binding
                const var_ptr = try self.func().emitAlloca(av.ir_type);
                try self.func().emitStore(var_ptr, value);

                // Determine the ValueType for this binding
                const binding_ty: ValueType = if (std.mem.eql(u8, av.type_name, "int"))
                    .{ .primitive = "int" }
                else if (std.mem.eql(u8, av.type_name, "float"))
                    .{ .primitive = "float" }
                else if (std.mem.eql(u8, av.type_name, "bool"))
                    .{ .primitive = "bool" }
                else
                    .{ .primitive = av.type_name };

                // Register in var_map
                try self.var_map.put(self.allocator, binding_name, types.VarInfo.init(var_ptr, binding_ty, false, false));

                offset += switch (av.ir_type) {
                    .i64, .f64, .ptr => 8,
                    .i32 => 4,
                    .i8 => 1,
                    .void => 0,
                };
            }
        }
    }

    /// Set up pattern bindings from a ChildBlock (unified child block structure)
    fn setupPatternBindingsFromChild(self: *AstToIr, child: ast.ChildBlock, scrutinee_value: ir.Value, scrutinee_ty: ValueType) ConvertError!void {
        const enum_name = scrutinee_ty.enum_type;
        const type_info = self.type_map.get(enum_name) orelse return;
        if (type_info != .enum_type) return;
        const enum_info = type_info.enum_type;

        // For each pattern with bindings, set up local variables
        for (child.pattern_bindings) |maybe_binding| {
            const binding = maybe_binding orelse continue;

            // Get the case info for this pattern
            const case_info = enum_info.case_info.get(binding.case_name) orelse {
                // Unknown case - report error
                self.reportError(.E034, binding.case_name);
                return error.SemanticError;
            };

            // Validate binding count matches associated values count
            if (binding.bindings.len != case_info.associated_values.len) {
                self.reportError(.E035, binding.case_name);
                return error.SemanticError;
            }

            if (case_info.associated_values.len == 0) continue;

            // Check if this enum has associated values (stored as pointer)
            if (!enum_info.has_associated_values) continue;

            // Extract each associated value and bind it to the local variable
            var offset: i32 = 8; // Start after tag
            for (case_info.associated_values, 0..) |av, ai| {
                const binding_name = binding.bindings[ai];

                // Load the associated value from scrutinee
                const payload_ptr = try self.func().emitGetFieldPtr(scrutinee_value, offset);
                const value = try self.func().emitLoad(payload_ptr, av.ir_type);

                // Create local variable for the binding
                const var_ptr = try self.func().emitAlloca(av.ir_type);
                try self.func().emitStore(var_ptr, value);

                // Determine the ValueType for this binding
                const binding_ty: ValueType = if (std.mem.eql(u8, av.type_name, "int"))
                    .{ .primitive = "int" }
                else if (std.mem.eql(u8, av.type_name, "float"))
                    .{ .primitive = "float" }
                else if (std.mem.eql(u8, av.type_name, "bool"))
                    .{ .primitive = "bool" }
                else
                    .{ .primitive = av.type_name };

                // Register in var_map
                try self.var_map.put(self.allocator, binding_name, types.VarInfo.init(var_ptr, binding_ty, false, false));

                offset += switch (av.ir_type) {
                    .i64, .f64, .ptr => 8,
                    .i32 => 4,
                    .i8 => 1,
                    .void => 0,
                };
            }
        }
    }

    /// Check if a pattern matches an enum member
    fn patternMatchesEnumMember(self: *AstToIr, pattern: ast.Expression, enum_name: []const u8, member_name: []const u8) bool {
        _ = self;
        switch (pattern) {
            .field_access => |fa| {
                // Check if pattern is EnumName.member_name
                const base_name = switch (fa.base.*) {
                    .identifier => |id| id,
                    else => return false,
                };
                return std.mem.eql(u8, base_name, enum_name) and std.mem.eql(u8, fa.field_name, member_name);
            },
            .identifier => |id| {
                // Bare identifier like `empty` or `value` matches the same member name
                return std.mem.eql(u8, id, member_name);
            },
            else => return false,
        }
    }

    /// Convert a match statement to IR
    /// Creates a chain of conditional checks for each case, with pattern matching
    /// Uses DeferredBlocks pattern with interleaved block creation for correct indexing
    fn convertMatchStmt(self: *AstToIr, match_stmt: ast.MatchStmt) ConvertError!void {
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(match_stmt.block.identifier);

        // Perform semantic validations before converting
        try self.validateMatchStmt(match_stmt);

        const scrutinee_typed = try self.convertExpression(match_stmt.scrutinee);
        const scrutinee_value = scrutinee_typed.value;

        const num_cases = countMatchCases(match_stmt.children);
        const has_default = hasDefaultCase(match_stmt.children);

        if (num_cases == 0) {
            if (getDefaultCase(match_stmt.children)) |default| {
                for (default) |stmt| try self.convertStatement(stmt);
            }
            return;
        }

        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

        // Allocate index arrays
        var check_indices = try self.allocator.alloc(u32, num_cases);
        defer self.allocator.free(check_indices);
        var body_indices = try self.allocator.alloc(u32, num_cases);
        defer self.allocator.free(body_indices);

        // Create first check block (stays current)
        check_indices[0] = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("match_check");

        // Deferred blocks: body[0..n-1] + check[1..n-1] + (default?) + merge
        const num_deferred = num_cases + (num_cases - 1) + @as(usize, if (has_default) 1 else 0) + 1;
        var deferred = try DeferredBlocks.init(self.allocator, num_deferred);
        defer deferred.deinit();

        // Create blocks interleaved: body[0], check[1], body[1], check[2], ...
        for (0..num_cases) |i| {
            body_indices[i] = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("match_body");

            if (i + 1 < num_cases) {
                check_indices[i + 1] = @intCast(self.func().blocks.items.len);
                _ = try self.func().addBlock("match_check");
            }
        }

        // Create default block if present
        if (has_default) {
            _ = try self.func().addBlock("match_default");
        }

        // Create merge block
        _ = try self.func().addBlock("match_merge");

        // Defer all blocks except the first check
        deferred.deferBlocks(self, num_deferred);

        // Branch from entry to first check
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = check_indices[0] }, .none, .none },
            .result = null,
        });

        // Track exit blocks for deferred branch emission
        var body_exit_indices = try self.allocator.alloc(?u32, num_cases);
        defer self.allocator.free(body_exit_indices);
        for (body_exit_indices) |*p| p.* = null;

        // Track actual body block indices for fallthrough support
        var actual_body_indices = try self.allocator.alloc(u32, num_cases);
        defer self.allocator.free(actual_body_indices);

        // Track has_fallthrough for each case
        var case_has_fallthrough = try self.allocator.alloc(bool, num_cases);
        defer self.allocator.free(case_has_fallthrough);

        // Deferred block indices with interleaved creation:
        // Created: body[0], check[1], body[1], check[2], ..., body[n-1], [default], merge
        // Reversed: merge, [default], body[n-1], check[n-1], ..., body[1], check[1], body[0]
        // body[i]: num_deferred - 1 - (2 * i)
        // check[i] for i >= 1: num_deferred - 1 - (2 * i - 1) = num_deferred - 2 * i

        var case_idx: usize = 0;
        for (match_stmt.children) |child| {
            if (child.role != .match_case) continue;
            const i = case_idx;
            case_idx += 1;

            // Emit pattern comparisons in check block
            var cmp_result: ir.Value = undefined;
            for (child.match_patterns, 0..) |pattern, pi| {
                const pattern_typed = try self.convertPatternExpression(pattern, scrutinee_typed.ty);
                const this_cmp = try self.emitPatternCompare(scrutinee_value, scrutinee_typed.ty, pattern_typed);
                cmp_result = if (pi == 0) this_cmp else try self.func().emitBinaryOp(.bitor, cmp_result, this_cmp, .i64);
            }

            // Restore body[i] to get its actual index
            const body_deferred_idx = num_deferred - 1 - (2 * i);
            try deferred.restore(self, body_deferred_idx);
            const actual_body_idx: u32 = @intCast(self.func().blocks.items.len - 1);
            actual_body_indices[i] = actual_body_idx;

            // Pop the body block temporarily so we can continue in check block
            const body_block = self.func().blocks.pop().?;

            // Emit conditional branch with placeholder for next_target (will be patched)
            // Use 0xFFFFFFFF as sentinel for "needs patching"
            try self.func().currentBlock().?.instructions.append(self.allocator, .{
                .op = .br_cond,
                .operands = .{ .{ .value = cmp_result }, .{ .block_ref = actual_body_idx }, .{ .block_ref = 0xFFFFFFFF } },
                .result = null,
            });
            const check_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

            // Re-push the body block to continue with body code
            try self.func().blocks.append(self.allocator, body_block);

            // Set up pattern bindings for associated value extraction
            if (scrutinee_typed.ty == .enum_type) {
                try self.setupPatternBindingsFromChild(child, scrutinee_value, scrutinee_typed.ty);
            }

            // Execute body statement
            try self.convertStatement(getChildBody(child));

            // Track has_fallthrough for this case
            case_has_fallthrough[i] = child.has_fallthrough;

            // Track exit block for deferred branch
            if (self.func().currentBlock()) |block| {
                const len = block.instructions.items.len;
                if (len == 0 or (block.instructions.items[len - 1].op != .ret and
                    block.instructions.items[len - 1].op != .br and
                    block.instructions.items[len - 1].op != .br_cond))
                {
                    body_exit_indices[i] = @intCast(self.func().blocks.items.len - 1);
                }
            }

            // Now restore next check block and patch the branch target
            if (i + 1 < num_cases) {
                const check_deferred_idx = num_deferred - 1 - (2 * i + 1);
                try deferred.restore(self, check_deferred_idx);
                const actual_next_check: u32 = @intCast(self.func().blocks.items.len - 1);

                // Patch the branch instruction in the check block
                var check_block = &self.func().blocks.items[check_block_idx];
                const last_instr_idx = check_block.instructions.items.len - 1;
                check_block.instructions.items[last_instr_idx].operands[2] = .{ .block_ref = actual_next_check };
            }
            // If last case, sentinel will be patched after default/merge is restored

            // Store check block index for patching later
            check_indices[i] = check_block_idx;
        }

        // Handle default case
        var default_exit_idx: ?u32 = null;
        var actual_default_idx: u32 = 0;
        if (getDefaultCase(match_stmt.children)) |default| {
            try deferred.restore(self, 1); // default is at index 1
            actual_default_idx = @intCast(self.func().blocks.items.len - 1);

            for (default) |stmt| try self.convertStatement(stmt);

            if (self.func().currentBlock()) |block| {
                const len = block.instructions.items.len;
                if (len == 0 or (block.instructions.items[len - 1].op != .ret and
                    block.instructions.items[len - 1].op != .br and
                    block.instructions.items[len - 1].op != .br_cond))
                {
                    default_exit_idx = @intCast(self.func().blocks.items.len - 1);
                }
            }
        }

        // Restore merge block (at index 0)
        try deferred.restore(self, 0);
        const actual_merge_idx: u32 = @intCast(self.func().blocks.items.len - 1);

        // Patch sentinel values in the last check block's branch
        // The last case uses sentinel 0xFFFFFFFF - branch to default if present, otherwise merge
        if (num_cases > 0) {
            const last_check_idx = check_indices[num_cases - 1];
            const last_check_block = &self.func().blocks.items[last_check_idx];
            const last_instr_idx = last_check_block.instructions.items.len - 1;
            var last_instr = &last_check_block.instructions.items[last_instr_idx];
            if (last_instr.operands[2] == .block_ref) {
                if (last_instr.operands[2].block_ref == 0xFFFFFFFF) {
                    // Branch to default if it exists, otherwise to merge
                    last_instr.operands[2] = .{ .block_ref = if (has_default) actual_default_idx else actual_merge_idx };
                } else if (last_instr.operands[2].block_ref == 0xFFFFFFFE) {
                    last_instr.operands[2] = .{ .block_ref = actual_merge_idx };
                }
            }
        }

        // Emit deferred branches from body blocks
        for (body_exit_indices, 0..) |maybe_idx, i| {
            if (maybe_idx) |idx| {
                // For fallthrough, branch to the next body block
                const target = if (case_has_fallthrough[i]) blk: {
                    if (i + 1 < num_cases) {
                        break :blk actual_body_indices[i + 1];
                    }
                    if (has_default) break :blk actual_default_idx;
                    break :blk actual_merge_idx;
                } else actual_merge_idx;

                try self.func().blocks.items[idx].instructions.append(self.allocator, .{
                    .op = .br,
                    .operands = .{ .{ .block_ref = target }, .none, .none },
                    .result = null,
                });
            }
        }

        if (default_exit_idx) |idx| {
            try self.func().blocks.items[idx].instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = actual_merge_idx }, .none, .none },
                .result = null,
            });
        }
    }

    /// Convert a pattern expression in the context of a match scrutinee type.
    /// If the scrutinee is an enum and the pattern is a bare identifier, resolve it as an enum case.
    fn convertPatternExpression(self: *AstToIr, pattern: ast.Expression, scrutinee_ty: ValueType) ConvertError!TypedValue {
        // If scrutinee is an enum and pattern is a bare identifier, try to resolve as enum case
        if (scrutinee_ty == .enum_type) {
            const enum_name = scrutinee_ty.enum_type;
            if (pattern == .identifier) {
                const case_name = pattern.identifier;
                // Look up enum type
                if (self.type_map.get(enum_name)) |type_info| {
                    if (type_info == .enum_type) {
                        // Try to find this case in the enum
                        if (type_info.enum_type.members.get(case_name)) |member_value| {
                            return .{
                                .value = try self.func().emitConstI64(member_value),
                                .ty = .{ .enum_type = enum_name },
                            };
                        } else {
                            // Unknown case - report error
                            self.reportError(.E034, case_name);
                            return error.SemanticError;
                        }
                    }
                }
            }
        }
        // Fall back to normal expression conversion
        return self.convertExpression(pattern);
    }

    /// Emit a comparison between scrutinee and pattern, returning a boolean value
    fn emitPatternCompare(self: *AstToIr, scrutinee: ir.Value, scrutinee_ty: ValueType, pattern: TypedValue) ConvertError!ir.Value {
        // Determine comparison type based on scrutinee type
        return switch (scrutinee_ty) {
            .primitive => |name| {
                if (std.mem.eql(u8, name, "float")) {
                    return try self.func().emitBinaryOp(.fcmp_eq, scrutinee, pattern.value, .f64);
                } else {
                    return try self.func().emitBinaryOp(.icmp_eq, scrutinee, pattern.value, .i64);
                }
            },
            .enum_type => |enum_name| {
                // For enums with associated values, scrutinee is a pointer - extract the tag first
                const type_info = self.type_map.get(enum_name) orelse {
                    return try self.func().emitBinaryOp(.icmp_eq, scrutinee, pattern.value, .i64);
                };
                if (type_info == .enum_type and type_info.enum_type.has_associated_values) {
                    // Load tag from offset 0 of the scrutinee pointer
                    const tag = try self.func().emitLoad(scrutinee, .i64);
                    return try self.func().emitBinaryOp(.icmp_eq, tag, pattern.value, .i64);
                }
                // Simple enum - scrutinee is the tag directly
                return try self.func().emitBinaryOp(.icmp_eq, scrutinee, pattern.value, .i64);
            },
            .struct_type => |name| {
                // For struct types that implement Equatable, use the equals method
                if (self.typeConformsTo(name, "Equatable")) {
                    const equals_result = try self.emitMethodCallWithIrArgs(name, "equals", &.{pattern.value}, scrutinee);
                    return equals_result.value;
                }
                return error.TypeMismatch;
            },
            else => error.TypeMismatch,
        };
    }

    /// Convert a match expression to IR
    /// Similar to match statement but returns a value from each case
    fn convertMatchExpr(self: *AstToIr, match_expr: ast.MatchExpr) ConvertError!TypedValue {
        // 1. Evaluate scrutinee once in current block
        const scrutinee_typed = try self.convertExpression(match_expr.scrutinee.*);
        const scrutinee_value = scrutinee_typed.value;

        const num_cases = match_expr.cases.len;
        const has_default = match_expr.default_expr != null;

        if (num_cases == 0 and !has_default) {
            // Empty match expression with no default - error
            self.reportError(.E003, "empty match expression requires default case");
            return error.SemanticError;
        }

        // Allocate result storage
        const result_ptr = try self.func().emitAlloca(.i64);

        if (num_cases == 0) {
            // Only default case - just evaluate it
            const default_typed = try self.convertExpression(match_expr.default_expr.?.*);
            try self.func().emitStore(result_ptr, default_typed.value);
            return .{ .value = try self.func().emitLoad(result_ptr, .i64), .ty = default_typed.ty };
        }

        // Record entry block
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

        // Create all blocks in the order we want to restore them:
        // check[0], body[0], check[1], body[1], ..., [default], merge
        // This way we can restore them in sequence

        var check_indices = try self.allocator.alloc(u32, num_cases);
        defer self.allocator.free(check_indices);
        var body_indices = try self.allocator.alloc(u32, num_cases);
        defer self.allocator.free(body_indices);

        // Create first check block (stays current)
        check_indices[0] = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("match_expr_check");

        // Count how many blocks we need to defer
        // body[0..n-1] + check[1..n-1] + (default?) + merge
        const num_deferred = num_cases + (num_cases - 1) + @as(usize, if (has_default) 1 else 0) + 1;
        var deferred = try DeferredBlocks.init(self.allocator, num_deferred);
        defer deferred.deinit();

        // Create body and remaining check blocks interleaved: body[0], check[1], body[1], check[2], ...
        for (0..num_cases) |i| {
            body_indices[i] = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("match_expr_body");

            if (i + 1 < num_cases) {
                check_indices[i + 1] = @intCast(self.func().blocks.items.len);
                _ = try self.func().addBlock("match_expr_check");
            }
        }

        // Create default block if present
        var default_block_idx: u32 = 0;
        if (has_default) {
            default_block_idx = @intCast(self.func().blocks.items.len);
            _ = try self.func().addBlock("match_expr_default");
        }

        // Create merge block
        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("match_expr_merge");

        // Defer all blocks except first check
        deferred.deferBlocks(self, num_deferred);

        // Branch from entry to first check
        try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = check_indices[0] }, .none, .none },
            .result = null,
        });

        // Track result type from first case
        var result_ty: ?ValueType = null;

        // Track which deferred block to restore next
        // Deferred order (reversed creation): merge, [default], check[n-1], body[n-1], check[n-2], body[n-2], ..., body[0]
        // We want to restore in order: body[0], check[1], body[1], check[2], ..., [default], merge

        // Process each case
        for (match_expr.cases, 0..) |match_case, i| {
            // Current block is check[i]
            // Emit pattern comparisons
            var cmp_result: ir.Value = undefined;
            for (match_case.patterns, 0..) |pattern, pi| {
                const pattern_typed = try self.convertPatternExpression(pattern, scrutinee_typed.ty);
                const this_cmp = try self.emitPatternCompare(scrutinee_value, scrutinee_typed.ty, pattern_typed);

                if (pi == 0) {
                    cmp_result = this_cmp;
                } else {
                    cmp_result = try self.func().emitBinaryOp(.bitor, cmp_result, this_cmp, .i64);
                }
            }

            // Determine next target if no match
            const next_target = if (i + 1 < num_cases)
                check_indices[i + 1]
            else if (has_default)
                default_block_idx
            else
                merge_block_idx;

            // Emit conditional branch
            try self.func().currentBlock().?.instructions.append(self.allocator, .{
                .op = .br_cond,
                .operands = .{ .{ .value = cmp_result }, .{ .block_ref = body_indices[i] }, .{ .block_ref = next_target } },
                .result = null,
            });

            // Restore body[i] - it's at deferred index (num_deferred - 1 - 2*i) for interleaved creation
            // Actually with interleaved: body[0], check[1], body[1], check[2], ...
            // Reversed: merge, [default], check[n-1], body[n-1], ..., check[1], body[0]
            // body[i] is at: num_deferred - 1 - (2*i) for i < n-1, last body is at num_deferred - 1 - 2*(n-1) + 1 if default else 0
            // This is getting complex. Let me just compute directly:
            // Created order: body[0], check[1], body[1], check[2], ..., body[n-1], [default], merge
            // Deferred (reversed): merge, [default], body[n-1], check[n-1], ..., body[1], check[1], body[0]
            const body_deferred_idx = num_deferred - 1 - (2 * i);
            try deferred.restore(self, body_deferred_idx);

            // Set up pattern bindings for match expressions
            if (scrutinee_typed.ty == .enum_type) {
                try self.setupPatternBindingsExpr(match_case, scrutinee_value, scrutinee_typed.ty);
            }

            // Evaluate result expression and store
            const case_result = try self.convertExpression(match_case.result);
            try self.func().emitStore(result_ptr, case_result.value);

            // Track result type
            if (result_ty == null) {
                result_ty = case_result.ty;
            }

            // Branch to merge
            try self.func().currentBlock().?.instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
                .result = null,
            });

            // Restore next check block if there is one
            if (i + 1 < num_cases) {
                // check[i+1] is at deferred index (num_deferred - 1 - (2*i + 1))
                const check_deferred_idx = num_deferred - 1 - (2 * i + 1);
                try deferred.restore(self, check_deferred_idx);
            }
        }

        // Handle default case if present
        if (match_expr.default_expr) |default| {
            // Default is at deferred index 1 (second from end)
            try deferred.restore(self, 1);

            const default_result = try self.convertExpression(default.*);
            try self.func().emitStore(result_ptr, default_result.value);

            if (result_ty == null) {
                result_ty = default_result.ty;
            }

            try self.func().currentBlock().?.instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
                .result = null,
            });
        }

        // Restore merge block (at deferred index 0)
        try deferred.restore(self, 0);

        // Load final result
        const final_value = try self.func().emitLoad(result_ptr, .i64);
        return .{ .value = final_value, .ty = result_ty orelse .{ .primitive = "int" } };
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

    fn convertThrowStmt(self: *AstToIr, throw_stmt: ast.ThrowStmt) ConvertError!void {
        // Verify we're in a throwing function
        const throws_type = self.current_func_throws_type orelse {
            self.reportError(.E001, "throw statement in non-throwing function");
            return error.SemanticError;
        };

        // Evaluate the error expression
        const error_typed = try self.convertExpression(throw_stmt.error_expr);

        // Verify the error type is an enum (errors must be enums)
        const error_type_name = switch (error_typed.ty) {
            .enum_type => |name| name,
            .struct_type => |name| {
                // Struct errors are no longer allowed
                self.reportError(.E023, name);
                return error.SemanticError;
            },
            else => {
                self.reportError(.E001, "throw requires an error enum type");
                return error.SemanticError;
            },
        };

        // Check that thrown type matches declared throws type
        if (!std.mem.eql(u8, error_type_name, throws_type)) {
            self.reportError(.E001, "thrown error type does not match function's throws declaration");
            return error.SemanticError;
        }

        // For throwing functions, we use sret to return the error union
        // Layout: [tag: 8 bytes][enum ordinal: 8 bytes]
        // tag = 0: success, tag = 1: error
        if (self.sret_ptr) |sret| {
            // Write tag = 1 (error)
            const one = try self.func().emitConstI64(1);
            try self.func().emitStore(sret, one);

            // Write enum ordinal at offset 8
            const error_ptr = try self.func().emitGetFieldPtr(sret, 8);
            try self.func().emitStore(error_ptr, error_typed.value);

            // Return via sret
            try self.freeHeapAllocations();
            try self.func().emitRet(sret);
        } else {
            // This shouldn't happen for throwing functions, but handle gracefully
            self.reportError(.E001, "throwing function missing sret pointer");
            return error.SemanticError;
        }
    }

    fn convertDoCatchStmt(self: *AstToIr, do_catch: ast.DoCatchStmt) ConvertError!void {
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(do_catch.block.identifier);

        // Save the outer do-catch context (for nested do-catch blocks)
        const outer_error_buffer = self.do_catch_error_buffer;
        const outer_catch_block = self.do_catch_catch_block;
        const outer_end_block = self.do_catch_end_block;
        const outer_error_type = self.do_catch_error_type;
        defer {
            self.do_catch_error_buffer = outer_error_buffer;
            self.do_catch_catch_block = outer_catch_block;
            self.do_catch_end_block = outer_end_block;
            self.do_catch_error_type = outer_error_type;
        }

        // Allocate buffer for storing the caught error (error union: tag + payload)
        // We use a generous size to hold any error type
        const error_buffer = try self.func().emitAllocaSized(64);
        self.do_catch_error_buffer = error_buffer;

        // Use sentinel value to indicate we're in a do-catch context
        // The actual block index will be set after do body processing
        // This is necessary because block indices shift as the do body creates its own blocks
        self.do_catch_catch_block = 0xFFFFFFFF; // Sentinel meaning "to be determined"
        self.do_catch_end_block = 0xFFFFFFFF;
        self.do_catch_error_type = null; // Will be set by try expressions

        // Process the do body - any `try` statements will record their error branches
        // These branches will be patched after we know the actual catch_dispatch index
        var try_error_branches = std.ArrayListUnmanaged(TryBranchInfo){};
        defer try_error_branches.deinit(self.allocator);
        const outer_try_branches = self.pending_try_branches;
        self.pending_try_branches = &try_error_branches;
        defer {
            self.pending_try_branches = outer_try_branches;
        }

        // Save var_map state before do body to restore for catch blocks
        // (catch blocks should not see variables declared in the do body)
        var pre_do_vars = std.StringHashMapUnmanaged(VarInfo){};
        defer pre_do_vars.deinit(self.allocator);
        var var_iter = self.var_map.iterator();
        while (var_iter.next()) |entry| {
            try pre_do_vars.put(self.allocator, entry.key_ptr.*, entry.value_ptr.*);
        }

        // Convert do body (with new label scope)
        try self.pushLabelScope();
        for (getPrimaryBody(do_catch.children)) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

        // Create catch_dispatch block - now we know the actual index
        const catch_dispatch_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("catch_dispatch");
        self.do_catch_catch_block = catch_dispatch_idx;

        // Create do_end block (final index calculated later after catch blocks are added)
        _ = try self.func().addBlock("do_end");

        // Pop both blocks for later restoration
        const do_end_block = self.func().blocks.pop().?;
        const catch_dispatch_block = self.func().blocks.pop().?;

        // Patch all try error branches to point to the actual catch_dispatch_idx
        for (try_error_branches.items) |branch_info| {
            const block = &self.func().blocks.items[branch_info.block_idx];
            block.instructions.items[branch_info.instr_idx].operands[0] = .{ .block_ref = catch_dispatch_idx };
        }

        // Track the instruction index for the do body's branch to do_end (we'll patch it later)
        const do_body_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const do_body_branch_instr_idx: u32 = @intCast(self.func().currentBlock().?.instructions.items.len);

        // Add branch from end of do body to do_end block (placeholder, will be patched)
        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = 0xFFFFFFFF }, .none, .none }, // Placeholder
            .result = null,
        });

        // Restore catch_dispatch block
        try self.func().blocks.append(self.allocator, catch_dispatch_block);

        // Create blocks for each catch handler
        // Note: catches are required by the parser, so we always have at least one
        const catch_count = countCatchHandlers(do_catch.children);
        var catch_blocks = try std.ArrayList(u32).initCapacity(self.allocator, catch_count);
        defer catch_blocks.deinit(self.allocator);

        for (0..catch_count) |i| {
            const catch_block_idx: u32 = @intCast(self.func().blocks.items.len);
            const catch_name = std.fmt.allocPrint(self.allocator, "catch_{d}", .{i}) catch "catch";
            // Track allocated name for cleanup when function is destroyed
            try self.func().allocated_names.append(self.allocator, catch_name);
            _ = try self.func().addBlock(catch_name);
            try catch_blocks.append(self.allocator, catch_block_idx);
        }

        // Pop all the catch blocks for later restoration
        var deferred_catch_blocks = try std.ArrayList(ir.BasicBlock).initCapacity(self.allocator, catch_blocks.items.len);
        defer deferred_catch_blocks.deinit(self.allocator);

        for (0..catch_blocks.items.len) |_| {
            try deferred_catch_blocks.append(self.allocator, self.func().blocks.pop().?);
        }

        // Catch dispatch jumps to first catch block
        // (More sophisticated: check error type tag against each catch's expected type)
        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = catch_blocks.items[0] }, .none, .none },
            .result = null,
        });

        // Track catch block branch instructions that need patching to point to do_end
        var catch_branch_infos = try std.ArrayList(struct { block_idx: u32, instr_idx: u32 }).initCapacity(self.allocator, catch_count);
        defer catch_branch_infos.deinit(self.allocator);

        // Restore and process each catch block
        var catch_idx: usize = 0;
        for (do_catch.children) |child| {
            if (child.role != .catch_handler) continue;

            // Restore this catch block as current
            const idx = deferred_catch_blocks.items.len - 1 - catch_idx;
            try self.func().blocks.append(self.allocator, deferred_catch_blocks.items[idx]);

            // Remove do-body-scoped variables from var_map before processing catch block
            // (catch blocks should not see/cleanup variables declared in the do body)
            var to_remove = std.ArrayListUnmanaged([]const u8){};
            defer to_remove.deinit(self.allocator);
            var remove_iter = self.var_map.keyIterator();
            while (remove_iter.next()) |key| {
                if (!pre_do_vars.contains(key.*)) {
                    try to_remove.append(self.allocator, key.*);
                }
            }
            for (to_remove.items) |key| {
                _ = self.var_map.remove(key);
            }

            // Get error value pointer (at offset 8 in error_buffer)
            const error_value_ptr = try self.func().emitGetFieldPtr(error_buffer, 8);

            // Bind the error to a variable (errors are now enums)
            const binding_name = child.catch_binding orelse "error";
            const error_ty: ValueType = if (child.catch_error_type) |et|
                .{ .enum_type = et }
            else
                .{ .enum_type = "Error" };
            try self.var_map.put(self.allocator, binding_name, VarInfo.init(error_value_ptr, error_ty, false, false));
            defer _ = self.var_map.remove(binding_name);

            // Process catch body (with new label scope)
            try self.pushLabelScope();
            for (child.statements) |stmt| {
                try self.convertStatement(stmt);
            }
            self.popLabelScope();

            // Track where we'll add the branch to do_end
            const current_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
            const branch_instr_idx: u32 = @intCast(self.func().currentBlock().?.instructions.items.len);
            try catch_branch_infos.append(self.allocator, .{ .block_idx = current_block_idx, .instr_idx = branch_instr_idx });

            // Jump to end block after catch body (placeholder, will be patched)
            try self.func().currentBlock().?.instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = 0xFFFFFFFF }, .none, .none }, // Placeholder
                .result = null,
            });

            catch_idx += 1;
        }

        // Now we know the actual do_end index - it's the next block to be added
        const do_end_idx: u32 = @intCast(self.func().blocks.items.len);
        self.do_catch_end_block = do_end_idx;

        // Patch the do body's branch to point to the correct do_end index
        self.func().blocks.items[do_body_block_idx].instructions.items[do_body_branch_instr_idx].operands[0] = .{ .block_ref = do_end_idx };

        // Patch all catch block branches to point to the correct do_end index
        for (catch_branch_infos.items) |info| {
            self.func().blocks.items[info.block_idx].instructions.items[info.instr_idx].operands[0] = .{ .block_ref = do_end_idx };
        }

        // Restore end block
        try self.func().blocks.append(self.allocator, do_end_block);
    }

    fn convertElseUnwrapDecl(self: *AstToIr, unwrap: ast.ElseUnwrapDecl) ConvertError!void {
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(unwrap.block.identifier);

        // Convert the optional expression
        const opt_typed = try self.convertExpression(unwrap.optional_expr.*);

        // Clean up any temporary strings created during expression evaluation
        // (e.g., lookup keys in `var x = m.get("key") else ...`)
        try self.cleanupTemporaryStrings();

        // Validate that the expression is an optional type
        if (opt_typed.ty != .optional_type) {
            // Get a readable name for the expression being unwrapped
            const expr_name = if (unwrap.optional_expr.* == .identifier)
                unwrap.optional_expr.identifier
            else
                "expression";
            const type_name = opt_typed.ty.getTypeName() orelse @tagName(opt_typed.ty);
            const msg = std.fmt.allocPrint(self.allocator, "'{s}' has type '{s}', not optional", .{ expr_name, type_name }) catch expr_name;
            self.reportError(.E040, msg);
            return error.TypeMismatch;
        }

        // Check if optional has a value
        const has_value = try self.emitOptionalHasValue(opt_typed.value);

        // Get the wrapped type info
        const opt_info = opt_typed.ty.optional_type;
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
        const var_value_type = getOptionalWrappedValueType(opt_info);
        const is_struct = opt_info.wrapped_struct_type != null;
        try self.var_map.put(self.allocator, unwrap.var_name, VarInfo.init(var_ptr, var_value_type, true, is_struct));

        // Validate that the else body contains an assignment to var_name
        const default_body = getDefaultValueBody(unwrap.children);
        if (!elseBlockAssignsTo(default_body, unwrap.var_name)) {
            self.reportError(.E044, unwrap.var_name);
            return error.SemanticError;
        }

        // Create 2-way branch: has_value -> unwrap, else -> execute default body
        var branch = try BranchBuilder.init(self, has_value, "else_unwrap_some", "else_unwrap_nil", "else_unwrap_end");
        defer branch.deinit();

        // Then block ("some"): unwrap and store value
        const value_ptr = try self.func().emitGetFieldPtr(opt_typed.value, 8);
        if (opt_info.wrapped_struct_type) |struct_name| {
            // For struct types, copy the struct data from the optional payload to var_ptr
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

        // Switch to else block
        try branch.switchToElse(true);

        // Else block ("nil"): execute default body (which should assign to var_name)
        // (with new label scope for the else body)
        try self.pushLabelScope();
        for (default_body) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

        // Branch to end only if the block doesn't already have a terminator
        const needs_branch = if (self.func().currentBlock()) |block|
            block.instructions.items.len == 0 or block.instructions.items[block.instructions.items.len - 1].op != .ret
        else
            true;

        // Switch to merge block
        try branch.switchToMerge(needs_branch);
    }

    /// Check if a statement list contains an assignment to the given variable name
    fn elseBlockAssignsTo(stmts: []const ast.Statement, var_name: []const u8) bool {
        for (stmts) |stmt| {
            switch (stmt.kind) {
                .assign => |assign| {
                    if (std.mem.eql(u8, assign.target, var_name)) {
                        return true;
                    }
                },
                else => {},
            }
        }
        return false;
    }

    /// Guard-let: `let x = opt or 'label' ... end 'label'`
    /// If optional is nil, execute body (must exit). If has value, bind x to unwrapped.
    fn convertGuardLetDecl(self: *AstToIr, guard: ast.GuardLetDecl) ConvertError!void {
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(guard.block.identifier);

        // Convert the optional expression
        const opt_typed = try self.convertExpression(guard.optional_expr.*);

        // Clean up any temporary strings created during expression evaluation
        // (e.g., lookup keys in `guard let v = m.get("key")`)
        try self.cleanupTemporaryStrings();

        // Get the wrapped type info - must be an optional type
        const opt_info = switch (opt_typed.ty) {
            .optional_type => |info| info,
            .primitive, .struct_type, .array_type, .enum_type, .error_union_type, .function_type => {
                self.reportError(.E017, "guard-let requires optional type");
                return error.TypeMismatch;
            },
        };
        const wrapped_type = opt_info.wrapped;

        // Check if optional has a value
        const has_value = try self.emitOptionalHasValue(opt_typed.value);

        // Create a stack slot for the unwrapped variable
        const var_ptr = if (opt_info.wrapped_struct_type) |struct_name| blk: {
            const struct_size = if (self.type_map.get(struct_name)) |type_info| s: {
                if (type_info == .struct_type) {
                    break :s type_info.struct_type.size;
                }
                break :s 8;
            } else 8;
            break :blk try self.func().emitAllocaSized(struct_size);
        } else try self.func().emitAlloca(wrapped_type);
        try self.func().setValueName(var_ptr, guard.var_name);

        // Create 2-way branch: has_value -> unwrap and continue, nil -> execute body (must exit)
        var branch = try BranchBuilder.init(self, has_value, "guard_some", "guard_nil", "guard_end");
        defer branch.deinit();

        // Then block ("some"): unwrap and store value, then jump to merge
        const value_ptr = try self.func().emitGetFieldPtr(opt_typed.value, 8);
        if (opt_info.wrapped_struct_type) |struct_name| {
            const struct_size = if (self.type_map.get(struct_name)) |type_info| blk: {
                if (type_info == .struct_type) {
                    break :blk type_info.struct_type.size;
                }
                break :blk 8;
            } else 8;
            try self.emitStructCopy(var_ptr, value_ptr, struct_size, struct_name);
        } else {
            const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);
            try self.func().emitStore(var_ptr, unwrapped_value);
        }

        // Switch to else block (nil case)
        try branch.switchToElse(true);

        // Else block ("nil"): execute body (must contain exit statement)
        try self.pushLabelScope();
        for (getNilHandler(guard.children)) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

        // The nil body MUST exit (return/break/continue), so no branch to merge is needed
        // If it does not exit, codegen will produce invalid code, but that is a semantic error
        // that should be caught by a separate validation pass
        const nil_block_terminates = if (self.func().currentBlock()) |block|
            block.instructions.items.len > 0 and block.instructions.items[block.instructions.items.len - 1].op == .ret
        else
            false;

        // Switch to merge block
        try branch.switchToMerge(!nil_block_terminates);

        // Add the variable to var_map for use after the guard-let
        if (opt_info.wrapped_struct_type) |struct_name| {
            try self.var_map.put(self.allocator, guard.var_name, VarInfo.init(
                var_ptr,
                .{ .struct_type = struct_name },
                false, // immutable (let binding)
                true,
            ));
        } else {
            try self.var_map.put(self.allocator, guard.var_name, VarInfo.init(
                var_ptr,
                .{ .primitive = wrapped_type.toMaxonName() },
                false, // immutable (let binding)
                false,
            ));
        }
    }

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
            .set_from => |sf| self.convertSetFrom(sf),
            .index => |idx| self.convertIndex(idx),
            .array_type => |arr| self.convertArrayType(arr),
            .method_call => |mcall| self.convertMethodCallExpr(mcall),
            .nil_coalesce => |nc| self.convertNilCoalesce(nc),
            .cast => |c| self.convertCast(c),
            .interpolated_string => |interp| self.convertInterpolatedString(interp),
            .closure => |clos| self.convertClosure(clos),
            .try_expr => |try_e| self.convertTryExpr(try_e),
            .match_expr => |me| self.convertMatchExpr(me),
            .enum_case => |ec| self.convertEnumCase(ec),
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

        // Check if it's a global constant
        if (self.global_constants.get(name)) |constant| {
            // For runtime-initialized constants, check if we've already converted it in this function
            if (constant == .runtime_expr) {
                if (self.converted_runtime_constants.get(name)) |cached| {
                    return cached;
                }
                // Convert and cache
                const result = try self.convertConstantValue(constant);
                try self.converted_runtime_constants.put(self.allocator, name, result);
                return result;
            }
            return self.convertConstantValue(constant);
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

        // Check if it's a function name - return function pointer with typed function_type
        if (self.func_map.get(name)) |func_info| {
            const func_ptr = try self.func().emitFuncAddr(name);

            // Build function type from func_info
            const param_types = try self.allocator.alloc(ValueType, func_info.param_types.len);
            for (func_info.param_types, 0..) |pt, i| {
                param_types[i] = pt.ty;
            }

            // Build return type
            var return_type: ?*const ValueType = null;
            if (func_info.return_value_type) |ret_vt| {
                const ret_ptr = try self.allocator.create(ValueType);
                ret_ptr.* = ret_vt;
                return_type = ret_ptr;
            } else if (func_info.return_type_name) |ret_name| {
                const ret_ptr = try self.allocator.create(ValueType);
                if (self.type_map.get(ret_name)) |type_info| {
                    if (type_info == .struct_type) {
                        ret_ptr.* = .{ .struct_type = ret_name };
                    } else if (type_info == .enum_type) {
                        ret_ptr.* = .{ .enum_type = ret_name };
                    } else {
                        ret_ptr.* = .{ .primitive = ret_name };
                    }
                } else {
                    ret_ptr.* = .{ .primitive = ret_name };
                }
                return_type = ret_ptr;
            } else if (func_info.return_type != .void) {
                const ret_ptr = try self.allocator.create(ValueType);
                ret_ptr.* = .{ .primitive = func_info.return_type.toMaxonName() };
                return_type = ret_ptr;
            }

            return .{
                .value = func_ptr,
                .ty = .{ .function_type = .{
                    .param_types = param_types,
                    .return_type = return_type,
                    .return_ir_type = func_info.return_type,
                } },
            };
        }

        // Fall back to original identifier behavior (will produce error for undefined)
        return self.convertIdentifier(name);
    }

    /// Check if an optional has a value (tag == 1).
    /// Returns an IR value representing the boolean result.
    fn emitOptionalHasValue(self: *AstToIr, opt_ptr: ir.Value) !ir.Value {
        const tag = try self.func().emitLoad(opt_ptr, .i64);
        const one = try self.func().emitConstI64(1);
        return try self.func().emitBinaryOp(.icmp_eq, tag, one, .i64);
    }

    /// Check if an optional is nil (tag == 0).
    /// Returns an IR value representing the boolean result.
    fn emitOptionalIsNil(self: *AstToIr, opt_ptr: ir.Value) !ir.Value {
        const tag = try self.func().emitLoad(opt_ptr, .i64);
        const zero = try self.func().emitConstI64(0);
        return try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64);
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

        // Call String$init to initialize the destination pointer (Builtin.init is also registered as init)
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
            .error_union_type => {
                self.reportInternalError("cannot convert error union to string for interpolation");
                return error.UnknownType;
            },
            .function_type => {
                self.reportInternalError("cannot convert function to string for interpolation");
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
        // Build the method name: TypeName$toString
        const method_name = try std.fmt.allocPrint(self.allocator, "{s}$toString", .{type_name});
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
        const new_buffer = try self.func().emitHeapAlloc(buffer_size, "string concat");

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
        const buffer = try self.func().emitHeapAlloc(buffer_size, "int.toString");

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
    /// For now, keep calling the runtime function since the inline version is complex
    fn emitFloatToStringCall(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        // Allocate buffer (32 bytes should be enough for most floats)
        const buffer_size = try self.func().emitConstI64(32);
        const buffer = try self.func().emitHeapAlloc(buffer_size, "float.toString");

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
        const buffer = try self.func().emitHeapAlloc(buffer_size, "bool.toString");

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

    /// Emit a panic call with the given error type name
    /// Prints "panic: unhandled {error_type}\n" and exits with code 1
    fn emitPanic(self: *AstToIr, error_type: []const u8) ConvertError!void {
        // Build the panic message: "panic: unhandled {error_type}\n"
        var msg_buf: [128]u8 = undefined;
        const msg_slice = std.fmt.bufPrint(&msg_buf, "panic: unhandled {s}\n", .{error_type}) catch "panic: unhandled error\n";

        // Dupe the message so it persists (the buffer is on the stack)
        const msg = try self.allocator.dupe(u8, msg_slice);

        // Emit string constant for the message (stored in data section)
        const msg_ptr = try self.func().emitStringConstant(msg);
        const msg_len = try self.func().emitConstI64(@intCast(msg.len));

        // Call __panic(msg_ptr, msg_len)
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = msg_ptr;
        args[1] = msg_len;
        _ = try self.func().emitCall("__panic", args, .void);
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
            if (info.uses_slot) try self.func().emitLoad(info.ptr.?, .ptr) else info.ptr.?
        else
            try self.func().emitLoad(info.ptr.?, info.ty.toPrimitiveType());

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
        const target_type_name = cast.target_type;

        // Check for string literal cast to InitableFromStringLiteral type
        if (cast.expr.* == .string_literal) {
            if (self.typeConformsTo(target_type_name, "InitableFromStringLiteral")) {
                return self.convertStringLiteralCast(cast.expr.string_literal, target_type_name);
            }
        }

        // Check for character literal cast to InitableFromCharLiteral type
        if (cast.expr.* == .char_literal) {
            if (self.typeConformsTo(target_type_name, "InitableFromCharLiteral")) {
                return self.convertCharLiteralCast(cast.expr.char_literal, target_type_name);
            }
        }

        const source = try self.convertExpression(cast.expr.*);
        const source_type = source.ty.toPrimitiveType();

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
            .return_type = body_result.ty.toPrimitiveType(),
            .return_type_name = body_result.ty.getTypeName(),
            .return_value_type = body_result.ty,
            .param_types = func_param_types,
        });

        // Free the IR types array since it's no longer needed
        self.allocator.free(param_ir_types);

        // Build the function type for the closure
        const closure_param_types = try self.allocator.alloc(ValueType, clos.params.len);
        for (clos.params, 0..) |param, i| {
            closure_param_types[i] = .{ .primitive = param.type_name };
        }

        // Build return type pointer
        const ret_ptr = try self.allocator.create(ValueType);
        ret_ptr.* = body_result.ty;

        // Return the function pointer - emit func.addr to get address of the closure function
        const func_ptr = try self.func().emitFuncAddr(anon_name);
        return .{
            .value = func_ptr,
            .ty = .{ .function_type = .{
                .param_types = closure_param_types,
                .return_type = ret_ptr,
                .return_ir_type = body_result.ty.toPrimitiveType(),
            } },
        };
    }

    fn convertBinary(self: *AstToIr, bin: ast.BinaryExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(bin.left.*);
        const right = try self.convertExpression(bin.right.*);

        // Check for optional types used without unwrapping
        if (left.ty == .optional_type) {
            const expr_name = if (bin.left.* == .identifier) bin.left.identifier else "expression";
            self.reportError(.E038, expr_name);
            return error.TypeMismatch;
        }
        if (right.ty == .optional_type) {
            const expr_name = if (bin.right.* == .identifier) bin.right.identifier else "expression";
            self.reportError(.E038, expr_name);
            return error.TypeMismatch;
        }

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
            const is_nil = try self.emitOptionalIsNil(left.value);

            const result = switch (cmp.op) {
                .eq => is_nil,
                .ne => blk: {
                    const zero = try self.func().emitConstI64(0);
                    break :blk try self.func().emitBinaryOp(.icmp_eq, is_nil, zero, .i64);
                },
                .lt, .le, .gt, .ge => {
                    // < > <= >= don't make sense for optional/nil comparison
                    self.reportError(.E003, "comparison operators < > <= >= not valid for optional/nil");
                    return error.TypeMismatch;
                },
            };
            return .{ .value = result, .ty = .{ .primitive = "bool" } };
        } else if (left_is_optional) {
            // Left is optional, right is a value - unwrap left and compare
            // Get pointer to the value at offset 8 of the optional
            const value_ptr = try self.func().emitGetFieldPtr(left.value, 8);

            // Check if the wrapped type is a struct that implements Equatable
            if (left.ty.optional_type.wrapped_struct_type) |struct_name| {
                if (self.typeConformsTo(struct_name, "Equatable")) {
                    if (cmp.op == .eq or cmp.op == .ne) {
                        // Call equals method: unwrapped_left.equals(right)
                        const equals_result = try self.emitMethodCallWithIrArgs(struct_name, "equals", &.{right.value}, value_ptr);

                        if (cmp.op == .eq) {
                            return .{ .value = equals_result.value, .ty = .{ .primitive = "bool" } };
                        } else {
                            const zero = try self.func().emitConstI64(0);
                            const negated = try self.func().emitBinaryOp(.icmp_eq, equals_result.value, zero, .i64);
                            return .{ .value = negated, .ty = .{ .primitive = "bool" } };
                        }
                    }
                }
            }

            // Fall back to primitive comparison
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
            // Get pointer to the value at offset 8 of the optional
            const value_ptr = try self.func().emitGetFieldPtr(right.value, 8);

            // Check if the wrapped type is a struct that implements Equatable
            if (right.ty.optional_type.wrapped_struct_type) |struct_name| {
                if (self.typeConformsTo(struct_name, "Equatable")) {
                    if (cmp.op == .eq or cmp.op == .ne) {
                        // Call equals method: left.equals(unwrapped_right)
                        const equals_result = try self.emitMethodCallWithIrArgs(struct_name, "equals", &.{value_ptr}, left.value);

                        if (cmp.op == .eq) {
                            return .{ .value = equals_result.value, .ty = .{ .primitive = "bool" } };
                        } else {
                            const zero = try self.func().emitConstI64(0);
                            const negated = try self.func().emitBinaryOp(.icmp_eq, equals_result.value, zero, .i64);
                            return .{ .value = negated, .ty = .{ .primitive = "bool" } };
                        }
                    }
                }
            }

            // Fall back to primitive comparison
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

        // Type checking for comparison operands - disallow int/float mixing
        const left_type_name = if (left.ty == .primitive) left.ty.primitive else null;
        const right_type_name = if (right.ty == .primitive) right.ty.primitive else null;

        if (left_type_name != null and right_type_name != null) {
            const left_is_int = std.mem.eql(u8, left_type_name.?, "int");
            const left_is_float = std.mem.eql(u8, left_type_name.?, "float");
            const left_is_byte = std.mem.eql(u8, left_type_name.?, "byte");
            const right_is_int = std.mem.eql(u8, right_type_name.?, "int");
            const right_is_float = std.mem.eql(u8, right_type_name.?, "float");
            const right_is_byte = std.mem.eql(u8, right_type_name.?, "byte");

            // Check for int/float mismatch (either direction)
            if ((left_is_int and right_is_float) or (left_is_float and right_is_int)) {
                self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_type_name.?, right_type_name.? }) catch "type mismatch in comparison");
                return error.TypeMismatch;
            }

            // Check for byte vs int literal - allow if literal is in range 0-255
            if (left_is_byte and right_is_int) {
                // Right is int - check if it's a literal in range
                if (cmp.right.* == .integer) {
                    const lit_value = cmp.right.integer;
                    if (lit_value < 0 or lit_value > 255) {
                        self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_type_name.?, right_type_name.? }) catch "type mismatch in comparison");
                        return error.TypeMismatch;
                    }
                    // Literal is in range - allow the comparison
                } else {
                    // Right is an int variable, not a literal - error
                    self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_type_name.?, right_type_name.? }) catch "type mismatch in comparison");
                    return error.TypeMismatch;
                }
            } else if (right_is_byte and left_is_int) {
                // Left is int - check if it's a literal in range
                if (cmp.left.* == .integer) {
                    const lit_value = cmp.left.integer;
                    if (lit_value < 0 or lit_value > 255) {
                        self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_type_name.?, right_type_name.? }) catch "type mismatch in comparison");
                        return error.TypeMismatch;
                    }
                    // Literal is in range - allow the comparison
                } else {
                    // Left is an int variable, not a literal - error
                    self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_type_name.?, right_type_name.? }) catch "type mismatch in comparison");
                    return error.TypeMismatch;
                }
            }
        }

        const left_prim = left.ty.toPrimitiveType();
        const right_prim = right.ty.toPrimitiveType();

        // If either operand is float, use float comparison (only reached for float vs float)
        if (left_prim == .f64 or right_prim == .f64) {
            // Both must be float at this point (int/float mismatch already caught above)
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
                // If left is an optional type, treat as nil coalescing
                if (left.ty == .optional_type) {
                    const opt_info = left.ty.optional_type;
                    const wrapped_type = opt_info.wrapped;

                    // Allocate result storage
                    const result_ptr = try self.func().emitAlloca(wrapped_type);

                    // Check if optional has a value (tag != 0)
                    const has_value = try self.emitOptionalHasValue(left.value);

                    // Create 2-way branch: has_value -> extract, else -> use default
                    var branch = try BranchBuilder.init(self, has_value, "or_has_value", "or_default", "or_merge");
                    defer branch.deinit();

                    // Then block: extract unwrapped value
                    const value_ptr = try self.func().emitGetFieldPtr(left.value, 8);
                    const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);
                    try self.func().emitStore(result_ptr, unwrapped_value);

                    // Switch to else block
                    try branch.switchToElse(true);

                    // Else block: use right value as default
                    try self.func().emitStore(result_ptr, right.value);

                    // Switch to merge block
                    try branch.switchToMerge(true);

                    // Load result
                    const final_value = try self.func().emitLoad(result_ptr, wrapped_type);

                    // Return the unwrapped type
                    const type_name = wrapped_type.toMaxonName();
                    return .{ .value = final_value, .ty = .{ .primitive = type_name } };
                }

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
            .primitive, .struct_type, .array_type, .enum_type, .error_union_type, .function_type => {
                // Left operand is not an optional type - this is an error
                self.reportError(.E017, "left operand of ?? must be optional type");
                return error.TypeMismatch;
            },
        };

        const wrapped_type = opt_info.wrapped;

        // Allocate result storage in entry block BEFORE branching
        const result_ptr = try self.func().emitAlloca(wrapped_type);

        // Check if optional has a value
        const has_value = try self.emitOptionalHasValue(opt_typed.value);

        // Create 2-way branch: has_value -> extract, else -> use default
        var branch = try BranchBuilder.init(self, has_value, "coalesce_has_value", "coalesce_default", "coalesce_merge");
        defer branch.deinit();

        // Then block: extract the unwrapped value
        const value_ptr = try self.func().emitGetFieldPtr(opt_typed.value, 8);
        const unwrapped_value = try self.func().emitLoad(value_ptr, wrapped_type);
        try self.func().emitStore(result_ptr, unwrapped_value);

        // Switch to else block
        try branch.switchToElse(true);

        // Else block: evaluate default expression (short-circuit evaluation)
        const default_typed = try self.convertExpression(nc.default.*);
        try self.func().emitStore(result_ptr, default_typed.value);

        // Switch to merge block
        try branch.switchToMerge(true);

        // Load result
        const result = try self.func().emitLoad(result_ptr, wrapped_type);
        return .{ .value = result, .ty = default_typed.ty };
    }

    /// Convert try expression: `try expr` (propagate)
    fn convertTryExpr(self: *AstToIr, try_e: ast.TryExpr) ConvertError!TypedValue {
        // The inner expression must be a call to a throwing function
        // For now, we only support function calls
        const inner_expr = try_e.expr.*;

        // Evaluate the inner expression (this calls the throwing function)
        const result_typed = try self.convertExpression(inner_expr);

        // The result should be an error union (from a throwing function via sret)
        // Check the tag at offset 0: 0 = success, 1 = error

        // Load the tag
        const tag = try self.func().emitLoad(result_typed.value, .i64);
        const zero = try self.func().emitConstI64(0);
        const is_success = try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64);

        // try - propagate error to caller or do-catch block
        // Check if we're in a do-catch block
        const in_do_catch = self.do_catch_catch_block != null;

        // Verify we're in a valid context for try
        if (!in_do_catch and self.current_func_throws_type == null) {
            self.reportError(.E001, "try requires enclosing function to throw or do-catch block");
            return error.SemanticError;
        }

        // Create blocks for success and error cases
        const success_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("try_success");
        const error_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("try_propagate");
        const merge_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("try_merge");

        // Branch based on tag
        const entry_block = &self.func().blocks.items[success_block_idx - 1];
        try entry_block.instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_success }, .{ .block_ref = success_block_idx }, .{ .block_ref = error_block_idx } },
        });

        // Defer merge and error blocks
        var deferred = try DeferredBlocks.init(self.allocator, 2);
        defer deferred.deinit();
        deferred.deferBlocks(self, 2);

        // Get success type info from error union
        const success_type: types.ValueType = if (result_typed.ty == .error_union_type) blk: {
            const eu_info = result_typed.ty.error_union_type;
            if (eu_info.success_struct_type) |struct_name| {
                break :blk .{ .struct_type = struct_name };
            } else {
                // Primitive success type - infer from IR type
                const prim_name: []const u8 = switch (eu_info.success_type) {
                    .i64, .i32 => "int",
                    .i8 => "byte",
                    .f64 => "float",
                    .ptr => "ptr",
                    .void => "void",
                };
                break :blk .{ .primitive = prim_name };
            }
        } else .{ .primitive = "int" };

        // Success block: extract value and store for later use
        const value_ptr = try self.func().emitGetFieldPtr(result_typed.value, 8);
        const is_struct_type = success_type == .struct_type;

        // For struct types, we return the pointer directly (data is embedded in error union)
        // For primitives, we load the value and store in a result alloca
        const result_ptr = if (is_struct_type)
            try self.func().emitAlloca(.ptr)
        else
            try self.func().emitAlloca(.i64);

        if (is_struct_type) {
            // Store pointer to embedded struct data
            try self.func().emitStore(result_ptr, value_ptr);
        } else {
            // Load primitive value
            const success_value = try self.func().emitLoad(value_ptr, .i64);
            try self.func().emitStore(result_ptr, success_value);
        }

        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
            .result = null,
        });

        // Error block: propagate error
        try deferred.restore(self, 1);

        if (in_do_catch) {
            // In do-catch: copy error to do-catch buffer and jump to catch dispatch
            const catch_block = self.do_catch_catch_block.?;
            if (self.do_catch_error_buffer) |err_buf| {
                // Copy the error union to the do-catch buffer
                try self.func().emitMemcpy(err_buf, result_typed.value, 64);
            }

            // Track the error type for panic messages
            if (self.do_catch_error_type == null) {
                if (result_typed.ty == .error_union_type) {
                    self.do_catch_error_type = result_typed.ty.error_union_type.error_enum_type;
                }
            }

            // Get current block index and instruction count before adding the branch
            const current_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
            const current_block = self.func().currentBlock().?;
            const instr_idx = current_block.instructions.items.len;

            try current_block.instructions.append(self.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = catch_block }, .none, .none },
                .result = null,
            });

            // If catch_block is sentinel (0xFFFFFFFF), record this branch for later patching
            if (catch_block == 0xFFFFFFFF) {
                if (self.pending_try_branches) |branches| {
                    try branches.append(self.allocator, .{
                        .block_idx = current_block_idx,
                        .instr_idx = instr_idx,
                    });
                }
            }
        } else {
            // Not in do-catch: propagate to function's sret and return
            if (self.sret_ptr) |sret| {
                // Copy the entire error union (tag + payload)
                try self.func().emitMemcpy(sret, result_typed.value, self.sret_size);
                try self.func().emitRet(sret);
            } else {
                self.reportError(.E001, "try requires sret for error propagation");
                return error.SemanticError;
            }
        }

        // Merge block: load the success value
        try deferred.restore(self, 0);
        const final_value = if (is_struct_type)
            try self.func().emitLoad(result_ptr, .ptr)
        else
            try self.func().emitLoad(result_ptr, .i64);

        return .{
            .value = final_value,
            .ty = success_type,
        };
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

        // Build a set of field names provided in the struct init
        var provided_fields = std.StringHashMap(void).init(self.allocator);
        defer provided_fields.deinit();

        // Initialize fields from struct init expression
        for (sinit.fields) |field_init| {
            try provided_fields.put(field_init.name, {});
            const field_info = try self.lookupField(struct_info, field_init.name);
            const field_ptr = try self.func().emitGetFieldPtr(dest_ptr, field_info.offset);
            const field_val = try self.convertExpression(field_init.value.*);

            // Track ownership transfer for array/struct fields
            try self.trackFieldOwnershipTransfer(field_init.value.*, type_name);

            if (field_info.isStruct()) {
                // Struct fields are embedded inline - copy the data
                try self.emitStructCopy(field_ptr, field_val.value, field_info.size, field_info.structName());
            } else if (field_info.value_type == .optional_type) {
                // Optional field - check if we need to wrap the value
                const opt_info = field_info.value_type.optional_type;
                if (field_val.ty == .optional_type) {
                    // Value is already optional - copy it directly (16 bytes for primitives)
                    try self.func().emitMemcpy(field_ptr, field_val.value, self.getOptionalSize(opt_info));
                } else {
                    // Value is not optional - wrap it as "some"
                    // Store tag = 1 at field_ptr
                    const one = try self.func().emitConstI64(1);
                    try self.func().emitStore(field_ptr, one);
                    // Store value at offset 8
                    const value_ptr = try self.func().emitGetFieldPtr(field_ptr, 8);
                    try self.func().emitStore(value_ptr, field_val.value);
                }
            } else {
                try self.func().emitStore(field_ptr, field_val.value);
            }
        }

        // Apply default values for fields not provided in struct init
        if (self.type_decl_map.get(type_name)) |type_decl| {
            for (type_decl.fields) |field_decl| {
                // Skip if this field was already provided
                if (provided_fields.contains(field_decl.name)) continue;

                // Apply default value if present
                if (field_decl.default_value) |default_expr| {
                    const field_info = try self.lookupField(struct_info, field_decl.name);
                    const field_ptr = try self.func().emitGetFieldPtr(dest_ptr, field_info.offset);
                    const default_val = try self.convertExpression(default_expr.*);

                    if (field_info.isStruct()) {
                        try self.emitStructCopy(field_ptr, default_val.value, field_info.size, field_info.structName());
                    } else {
                        try self.func().emitStore(field_ptr, default_val.value);
                    }
                }
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
        if (self.track_memory) {
            try self.func().emitTrackMove(var_name);
        }
    }

    fn convertFieldAccess(self: *AstToIr, faccess: ast.FieldAccessExpr) ConvertError!TypedValue {
        // Check for enum member access (e.g., Colors.Green)
        if (faccess.base.* == .identifier) {
            if (self.type_map.get(faccess.base.identifier)) |type_info| {
                if (type_info == .enum_type) {
                    const member_value = type_info.enum_type.members.get(faccess.field_name) orelse {
                        self.reportError(.E034, faccess.field_name);
                        return error.SemanticError;
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

        // Handle .rawValue for enum types
        if (base.ty == .enum_type) {
            const enum_name = base.ty.enum_type;
            if (std.mem.eql(u8, faccess.field_name, "rawValue")) {
                // Get enum type info
                const type_info = self.type_map.get(enum_name) orelse {
                    self.reportError(.E006, enum_name);
                    return error.UnknownType;
                };
                if (type_info != .enum_type) {
                    self.reportError(.E006, enum_name);
                    return error.UnknownType;
                }
                const enum_info = type_info.enum_type;

                // Check if enum has an explicit backing type
                if (!enum_info.has_explicit_backing_type) {
                    self.reportError(.E033, enum_name);
                    return error.SemanticError;
                }

                // For int-backed enums, the enum value IS the raw value
                if (enum_info.backing_type == .int) {
                    return .{ .value = base.value, .ty = .{ .primitive = "int" } };
                } else {
                    // For string-backed enums, look up in string_values table
                    // For now, return the ordinal as int
                    return .{ .value = base.value, .ty = .{ .primitive = "int" } };
                }
            }
            // Enum values don't have other fields
            std.debug.print("[AST->IR] convertFieldAccess: expected struct type for field '{s}'\n", .{faccess.field_name});
            self.reportError(.E006, faccess.field_name);
            return error.UnknownType;
        }

        const type_name = switch (base.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type, .error_union_type, .function_type => {
                std.debug.print("[AST->IR] convertFieldAccess: expected struct type for field '{s}'\n", .{faccess.field_name});
                self.reportError(.E006, faccess.field_name);
                return error.UnknownType;
            },
        };

        const struct_info = try self.lookupStructInfo(type_name);
        const field_info = try self.lookupField(struct_info, faccess.field_name);

        // Check field visibility: unexported fields can only be accessed within the type's methods
        const is_inside_type = self.current_type_name != null and std.mem.eql(u8, self.current_type_name.?, type_name);
        if (!field_info.is_export and !is_inside_type) {
            const msg = std.fmt.allocPrint(self.allocator, "{s}' outside of type '{s}", .{ faccess.field_name, type_name }) catch {
                self.reportError(.E050, faccess.field_name);
                return error.SemanticError;
            };
            self.reportError(.E050, msg);
            return error.SemanticError;
        }

        const field_ptr = try self.func().emitGetFieldPtr(base.value, field_info.offset);

        // Struct fields and optional fields are embedded (return ptr directly); others are loaded
        const value = if (field_info.value_type == .struct_type or field_info.value_type == .optional_type)
            field_ptr
        else
            try self.func().emitLoad(field_ptr, field_info.value_type.toPrimitiveType());

        return .{ .value = value, .ty = field_info.value_type };
    }

    /// Convert enum case construction with associated values (e.g., Result.success(42))
    fn convertEnumCase(self: *AstToIr, ec: ast.EnumCaseExpr) ConvertError!TypedValue {
        // Look up enum type
        const type_info = self.type_map.get(ec.enum_name) orelse {
            self.reportError(.E006, ec.enum_name);
            return error.UnknownType;
        };

        if (type_info != .enum_type) {
            self.reportError(.E006, ec.enum_name);
            return error.UnknownType;
        }

        const enum_info = type_info.enum_type;

        // Get case info
        const case_info = enum_info.case_info.get(ec.case_name) orelse {
            self.reportError(.E034, ec.case_name);
            return error.SemanticError;
        };

        // Validate argument count
        if (ec.args.len != case_info.associated_values.len) {
            self.reportError(.E011, ec.case_name);
            return error.WrongArgumentCount;
        }

        if (!enum_info.has_associated_values) {
            // Simple enum without associated values - just return the tag
            return .{
                .value = try self.func().emitConstI64(case_info.tag),
                .ty = .{ .enum_type = ec.enum_name },
            };
        }

        // For enums with associated values, allocate storage for tag + payload
        // Layout: [tag: i64][payload...]
        const total_size = 8 + enum_info.max_payload_size; // tag is i64
        const enum_ptr = try self.func().emitAlloca(.ptr);

        // Allocate memory for enum storage
        const alloc_val = try self.func().emitConstI64(total_size);
        const mem_ptr = try self.func().emitHeapAlloc(alloc_val, "enum storage");
        try self.func().emitStore(enum_ptr, mem_ptr);

        // Store tag
        const tag_val = try self.func().emitConstI64(case_info.tag);
        try self.func().emitStore(mem_ptr, tag_val);

        // Store associated values
        var offset: i32 = 8; // Start after tag
        for (case_info.associated_values, 0..) |av, i| {
            const arg_typed = try self.convertExpression(ec.args[i]);

            // Type check: verify argument type matches expected associated value type
            const expected_type = av.type_name;
            const actual_type = arg_typed.ty.getTypeName();
            if (actual_type) |actual| {
                if (!std.mem.eql(u8, actual, expected_type)) {
                    // Handle string compatibility
                    const is_string_match = (std.mem.eql(u8, expected_type, "String") and std.mem.eql(u8, actual, "__ManagedString")) or
                        (std.mem.eql(u8, expected_type, "__ManagedString") and std.mem.eql(u8, actual, "String"));
                    if (!is_string_match) {
                        self.reportError(.E022, av.name);
                        return error.TypeMismatch;
                    }
                }
            }

            const payload_ptr = try self.func().emitGetFieldPtr(mem_ptr, offset);
            try self.func().emitStore(payload_ptr, arg_typed.value);
            offset += switch (av.ir_type) {
                .i64, .f64, .ptr => 8,
                .i32 => 4,
                .i8 => 1,
                .void => 0,
            };
        }

        // Return the pointer to the enum storage
        const loaded_ptr = try self.func().emitLoad(enum_ptr, .ptr);
        return .{
            .value = loaded_ptr,
            .ty = .{ .enum_type = ec.enum_name },
        };
    }

    /// Apply implicit int→float promotion if needed
    fn applyIntToFloatPromotion(self: *AstToIr, arg_value: ir.Value, arg_type: ValueType, param_type: ValueType) !ir.Value {
        const arg_prim = arg_type.toPrimitiveType();
        const param_expects_float = switch (param_type) {
            .primitive => |name| std.mem.eql(u8, name, "float"),
            else => false,
        };
        if (param_expects_float and arg_prim == .i64) {
            return self.func().emitUnaryOp(.sitofp, arg_value, .f64) catch return error.OutOfMemory;
        }
        return arg_value;
    }

    /// Resolved argument: either a provided expression or a default value
    const ResolvedArg = union(enum) {
        expr: ast.Expression,
        default: *const ast.Expression,
    };

    /// Resolve named arguments and default values to build final positional argument list.
    /// Returns a slice of ResolvedArg for each parameter.
    /// Validates:
    /// - No positional args after named args
    /// - Named args reference valid parameter names
    /// - All required parameters are provided
    /// - No duplicate named args
    fn resolveCallArguments(
        self: *AstToIr,
        func_name: []const u8,
        positional_args: []const ast.Expression,
        named_args: []const ast.NamedArg,
        param_types: []const ParamType,
        skip_self: bool, // true for method calls where first param is implicit self
    ) ConvertError![]const ResolvedArg {
        const param_offset: usize = if (skip_self) 1 else 0;
        const explicit_params = param_types[param_offset..];

        // Count required parameters (those without default values)
        var required_count: usize = 0;
        for (explicit_params) |param| {
            if (param.default_value == null) {
                required_count += 1;
            }
        }

        // Validate: positional args can only fill required params (not those with defaults)
        if (positional_args.len > required_count) {
            const msg = std.fmt.allocPrint(self.allocator, "Too many positional arguments\n  Function '{s}' has {d} required parameter{s}", .{
                func_name,
                required_count,
                if (required_count == 1) "" else "s",
            }) catch {
                self.reportError(.E046, func_name);
                return error.WrongArgumentCount;
            };
            self.last_error = .{
                .code = .E046,
                .message = msg,
                .location = .{
                    .file = self.source_file,
                    .line = @intCast(self.current_line),
                    .column = @intCast(self.current_column),
                },
                .message_allocated = true,
            };
            return error.WrongArgumentCount;
        }

        // Allocate result array
        var resolved = try self.allocator.alloc(ResolvedArg, explicit_params.len);
        @memset(resolved, .{ .default = undefined });

        // Track which parameters have been filled
        var filled = try self.allocator.alloc(bool, explicit_params.len);
        defer self.allocator.free(filled);
        @memset(filled, false);

        // Fill positional arguments
        for (positional_args, 0..) |arg, i| {
            resolved[i] = .{ .expr = arg };
            filled[i] = true;
        }

        // Fill named arguments
        for (named_args) |named| {
            // Find the parameter by name
            var found_idx: ?usize = null;
            for (explicit_params, 0..) |param, i| {
                if (std.mem.eql(u8, param.name, named.name)) {
                    found_idx = i;
                    break;
                }
            }

            if (found_idx == null) {
                self.reportError(.E045, named.name);
                return error.UnknownParameter;
            }

            const idx = found_idx.?;
            if (filled[idx]) {
                self.reportError(.E047, named.name);
                return error.DuplicateArgument;
            }

            resolved[idx] = .{ .expr = named.value.* };
            filled[idx] = true;
        }

        // Fill defaults and check for missing required args
        for (explicit_params, 0..) |param, i| {
            if (!filled[i]) {
                if (param.default_value) |default| {
                    resolved[i] = .{ .default = default };
                } else {
                    const msg = std.fmt.allocPrint(self.allocator, "Missing required argument '{s}' in call to '{s}'", .{
                        param.name,
                        func_name,
                    }) catch {
                        self.reportError(.E049, param.name);
                        return error.MissingArgument;
                    };
                    self.last_error = .{
                        .code = .E049,
                        .message = msg,
                        .location = .{
                            .file = self.source_file,
                            .line = @intCast(self.current_line),
                            .column = @intCast(self.current_column),
                        },
                        .message_allocated = true,
                    };
                    return error.MissingArgument;
                }
            }
        }

        return resolved;
    }

    fn convertCall(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
        // Handle built-in functions
        if (intrinsics.convertBuiltin(self, call)) |result| {
            return result;
        } else |builtin_err| switch (builtin_err) {
            error.NotABuiltin => {},
            else => return builtin_err,
        }

        // Check if calling through a function variable (indirect call)
        if (self.var_map.get(call.func_name)) |var_info| {
            if (var_info.ty == .function_type) {
                return self.convertIndirectCall(call, var_info);
            }
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
            // Report undefined function error immediately with location
            self.reportError(.E024, call.func_name);
            return error.UnknownFunction;
        };

        // Resolve named arguments and default values to positional arguments
        const resolved_args = try self.resolveCallArguments(
            call.func_name,
            call.args,
            call.named_args,
            func_info.param_types,
            false, // no implicit self for regular functions
        );
        defer self.allocator.free(resolved_args);

        // Check if return type is optional or error union (needs sret)
        const returns_optional = if (func_info.return_value_type) |vt|
            vt == .optional_type
        else
            false;
        const returns_error_union = if (func_info.return_value_type) |vt|
            vt == .error_union_type
        else
            false;

        // Check if callee returns a struct, optional, or error union (needs sret)
        const returns_struct = func_info.return_type_name != null or returns_optional or returns_error_union;
        var sret_buffer: ?ir.Value = null;

        // Allocate args: +1 if using sret for hidden first parameter
        const num_args = resolved_args.len + @as(usize, if (returns_struct) 1 else 0);
        const args = try self.func().allocator.alloc(ir.Value, num_args);

        // If returning struct, optional, or error union, allocate buffer in caller and pass as first arg
        if (returns_struct) {
            if (returns_optional) {
                // Optional size = 8 (tag) + wrapped_value_size
                const opt_info = func_info.return_value_type.?.optional_type;
                sret_buffer = try self.func().emitAllocaSized(self.getOptionalSize(opt_info));
            } else if (returns_error_union) {
                // Error union size = 8 (tag) + max(success_size, error_size)
                const eu_info = func_info.return_value_type.?.error_union_type;
                const success_size: i32 = if (eu_info.success_struct_type) |sn|
                    if (self.type_map.get(sn)) |ti| if (ti == .struct_type) ti.struct_type.size else 8 else 8
                else
                    8;
                // Error enums are always 8 bytes (i64 ordinal)
                const error_size: i32 = 8;
                sret_buffer = try self.func().emitAllocaSized(8 + @max(success_size, error_size));
            } else {
                const struct_name = func_info.return_type_name.?;
                const struct_info = try self.lookupStructInfo(struct_name);
                sret_buffer = try self.func().emitAllocaSized(struct_info.size);
            }
            args[0] = sret_buffer.?;
        }

        const arg_offset: usize = if (returns_struct) 1 else 0;
        for (resolved_args, 0..) |resolved_arg, i| {
            // Get expression from either provided arg or default value
            const arg_expr: ast.Expression = switch (resolved_arg) {
                .expr => |e| e,
                .default => |d| d.*,
            };
            const arg = try self.convertExpression(arg_expr);
            var arg_value = arg.value;
            var arg_ty = arg.ty;

            // Implicit optional unwrap: if arg is optional but param expects the wrapped type
            if (i < func_info.param_types.len) {
                const param_ty = func_info.param_types[i].ty;
                if (arg_ty == .optional_type and param_ty != .optional_type) {
                    // Check if param type matches the wrapped type
                    const opt_info = arg_ty.optional_type;
                    const param_matches = blk: {
                        if (param_ty == .struct_type) {
                            if (opt_info.wrapped_struct_type) |wrapped_name| {
                                break :blk std.mem.eql(u8, param_ty.struct_type, wrapped_name);
                            }
                        }
                        break :blk param_ty == .primitive and
                            std.mem.eql(u8, param_ty.primitive, opt_info.wrapped.toMaxonName());
                    };
                    if (param_matches) {
                        // Unwrap optional: get pointer to value (offset 8 from optional start)
                        arg_value = try self.func().emitGetFieldPtr(arg_value, 8);
                        arg_ty = param_ty;
                    }
                }
            }

            // Implicit optional wrap: if arg is non-optional but param expects optional
            if (i < func_info.param_types.len) {
                const param_ty = func_info.param_types[i].ty;
                if (arg_ty != .optional_type and param_ty == .optional_type) {
                    const opt_info = param_ty.optional_type;
                    // Check if arg type matches the optional's wrapped type
                    const type_matches = blk: {
                        if (arg_ty == .struct_type) {
                            if (opt_info.wrapped_struct_type) |wrapped_name| {
                                break :blk std.mem.eql(u8, arg_ty.struct_type, wrapped_name);
                            }
                        }
                        if (arg_ty == .primitive) {
                            break :blk std.mem.eql(u8, arg_ty.primitive, opt_info.wrapped.toMaxonName());
                        }
                        break :blk false;
                    };
                    if (type_matches) {
                        // Allocate optional struct buffer
                        const opt_size = self.getOptionalSize(opt_info);
                        const opt_buffer = try self.func().emitAllocaSized(opt_size);
                        // Store tag = 1 (has value)
                        const one = try self.func().emitConstI64(1);
                        try self.func().emitStore(opt_buffer, one);
                        // Store value at offset 8
                        const value_ptr = try self.func().emitGetFieldPtr(opt_buffer, 8);
                        if (arg_ty == .struct_type) {
                            // For struct types, need to memcpy
                            if (opt_info.wrapped_struct_type) |struct_name| {
                                if (self.type_map.get(struct_name)) |type_info| {
                                    if (type_info == .struct_type) {
                                        try self.func().emitMemcpy(value_ptr, arg_value, type_info.struct_type.size);
                                    } else {
                                        try self.func().emitStore(value_ptr, arg_value);
                                    }
                                } else {
                                    try self.func().emitStore(value_ptr, arg_value);
                                }
                            } else {
                                try self.func().emitStore(value_ptr, arg_value);
                            }
                        } else {
                            try self.func().emitStore(value_ptr, arg_value);
                        }
                        arg_value = opt_buffer;
                        arg_ty = param_ty;
                    }
                }
            }

            // Implicit int→float promotion: if parameter expects float but arg is int
            if (i < func_info.param_types.len) {
                arg_value = try self.applyIntToFloatPromotion(arg_value, arg_ty, func_info.param_types[i].ty);
            }

            args[i + arg_offset] = arg_value;
            // Only check ownership for explicitly provided args
            if (resolved_arg == .expr) {
                try self.checkOwnershipTransfer(call.func_name, arg_expr, i);
            }
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
        // If the function returns an error union with sret, return the sret buffer
        if (returns_error_union) {
            return .{ .value = sret_buffer.?, .ty = func_info.return_value_type.? };
        }
        // If the function returns an array, use the full array type info
        if (func_info.return_value_type) |vtype| {
            return .{ .value = result orelse 0, .ty = vtype };
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = func_info.return_type.toMaxonName() } };
    }

    /// Convert an indirect call through a function variable
    fn convertIndirectCall(self: *AstToIr, call: ast.CallExpr, var_info: VarInfo) ConvertError!TypedValue {
        const func_type_info = var_info.ty.function_type;

        // Load the function pointer from the variable
        const func_ptr = if (var_info.uses_slot)
            try self.func().emitLoad(var_info.ptr.?, .ptr)
        else
            var_info.ptr.?;

        // Mark variable as used
        if (self.var_map.getPtr(call.func_name)) |info| {
            info.used = true;
        }

        // Check argument count
        if (call.args.len != func_type_info.param_types.len) {
            const msg = std.fmt.allocPrint(self.allocator, "expected {d} arguments, got {d}", .{ func_type_info.param_types.len, call.args.len }) catch "wrong number of arguments";
            self.reportError(.E008, msg);
            return error.WrongArgumentCount;
        }

        // Check if return type is a struct that needs sret
        const returns_struct = if (func_type_info.return_type) |ret_vt| blk: {
            break :blk ret_vt.* == .struct_type or ret_vt.* == .optional_type;
        } else false;

        var sret_buffer: ?ir.Value = null;
        const num_args = call.args.len + @as(usize, if (returns_struct) 1 else 0);
        const args = try self.func().allocator.alloc(ir.Value, num_args);

        // If returning struct, allocate buffer in caller and pass as first arg
        if (returns_struct) {
            if (func_type_info.return_type) |ret_vt| {
                if (ret_vt.* == .optional_type) {
                    const opt_info = ret_vt.optional_type;
                    sret_buffer = try self.func().emitAllocaSized(self.getOptionalSize(opt_info));
                } else if (ret_vt.* == .struct_type) {
                    const struct_name = ret_vt.struct_type;
                    const struct_info = try self.lookupStructInfo(struct_name);
                    sret_buffer = try self.func().emitAllocaSized(struct_info.size);
                }
            }
            if (sret_buffer) |buf| {
                args[0] = buf;
            }
        }

        const arg_offset: usize = if (returns_struct) 1 else 0;
        for (call.args, 0..) |arg_expr, i| {
            const arg = try self.convertExpression(arg_expr);
            var arg_value = arg.value;

            // Implicit int→float promotion
            if (i < func_type_info.param_types.len) {
                arg_value = try self.applyIntToFloatPromotion(arg_value, arg.ty, func_type_info.param_types[i]);
            }

            args[i + arg_offset] = arg_value;
        }

        const result = try self.func().emitCallIndirect(func_ptr, args, func_type_info.return_ir_type);

        // Return the appropriate type
        if (func_type_info.return_type) |ret_vt| {
            if (ret_vt.* == .struct_type or ret_vt.* == .optional_type) {
                return .{ .value = sret_buffer.?, .ty = ret_vt.* };
            }
            return .{ .value = result orelse 0, .ty = ret_vt.* };
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = "void" } };
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
        if (self.track_memory) {
            try self.func().emitTrackMove(var_name);
        }
    }

    /// Index into a __ManagedArray: managed[i]
    /// Returns the element as an optional (bounds-checked)
    /// Uses current_type_name or var_name context to determine element type
    fn convertManagedArrayIndex(self: *AstToIr, managed_ptr: ir.Value, index_expr: ast.Expression, var_name: ?[]const u8) ConvertError!TypedValue {
        const idx_typed = try self.convertExpression(index_expr);

        // Extract element type from context
        // 1. First try current_type_name (e.g., "Array$String" -> "String")
        // 2. If that fails and we're in a Map context with a known var name, use the appropriate type param
        const elem_type_name: ?[]const u8 = blk: {
            // Try Array$* pattern first
            if (self.current_type_name) |type_name| {
                if (std.mem.startsWith(u8, type_name, "Array$")) {
                    break :blk type_name[6..]; // Skip "Array$" prefix
                }
            }
            // Check for Map$K$V context with known var names
            if (var_name) |vn| {
                if (self.current_type_name) |type_name| {
                    if (std.mem.startsWith(u8, type_name, "Map$")) {
                        // In Map$Key$Value, "keys" param has Key elements, "values" param has Value elements
                        // Use generic_params to resolve to the actual types
                        if (std.mem.eql(u8, vn, "keys")) {
                            break :blk self.generic_params.get("Key");
                        } else if (std.mem.eql(u8, vn, "values")) {
                            break :blk self.generic_params.get("Value");
                        }
                    }
                }
            }
            break :blk null;
        };

        // Determine element size and whether it's a struct
        const ElemInfo = struct { size: i32, is_struct: bool, name: ?[]const u8 };
        const elem_info: ElemInfo = if (elem_type_name) |tn| blk: {
            if (self.type_map.get(tn)) |type_info| {
                if (type_info == .struct_type) {
                    break :blk ElemInfo{ .size = type_info.struct_type.size, .is_struct = true, .name = tn };
                }
            }
            break :blk ElemInfo{ .size = 8, .is_struct = false, .name = tn };
        } else ElemInfo{ .size = 8, .is_struct = false, .name = null };

        // Load buffer pointer (offset 0) and length (offset 8)
        const buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
        const len_ptr = try self.func().emitGetFieldPtr(managed_ptr, 8);
        const len = try self.func().emitLoad(len_ptr, .i64);

        // Calculate optional size: 8 (tag) + element size
        const opt_size: i32 = 8 + elem_info.size;
        const opt_ptr = try self.func().emitAllocaSized(opt_size);

        // Bounds check: index >= 0 AND index < len
        const zero = try self.func().emitConstI64(0);
        const is_non_negative = try self.func().emitBinaryOp(.icmp_ge, idx_typed.value, zero, .i64);
        const is_less_than_len = try self.func().emitBinaryOp(.icmp_lt, idx_typed.value, len, .i64);
        const in_bounds = try self.func().emitBinaryOp(.mul, is_non_negative, is_less_than_len, .i64);

        // Create 2-way branch: in_bounds -> get element, else -> return nil
        var branch = try BranchBuilder.init(self, in_bounds, "managed_index_in_bounds", "managed_index_out_of_bounds", "managed_index_merge");
        defer branch.deinit();

        // Then block: create some(element)
        const one_val = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one_val); // tag = 1

        // Calculate element pointer: buf_ptr + index * elem_size
        const elem_size_val = try self.func().emitConstI64(elem_info.size);
        const offset = try self.func().emitBinaryOp(.mul, idx_typed.value, elem_size_val, .i64);
        const elem_ptr = try self.func().emitBinaryOp(.add, buf_ptr, offset, .ptr);

        const value_slot = try self.func().emitGetFieldPtr(opt_ptr, 8);

        if (elem_info.is_struct) {
            // For struct elements, copy the struct data into the optional
            // Note: we use plain memcpy here, not emitStructCopy, because:
            // 1. emitStructCopy creates branch blocks for refcount handling that would
            //    interfere with the BranchBuilder's block management
            // 2. For read-only access (indexing), we're borrowing the data, not taking ownership
            // If the caller wants ownership, they will copy and incref separately.
            try self.func().emitMemcpy(value_slot, elem_ptr, elem_info.size);
        } else {
            // For primitive elements, load and store the value
            const val = try self.func().emitLoad(elem_ptr, .i64);
            try self.func().emitStore(value_slot, val);
        }

        // Switch to else block
        try branch.switchToElse(true);

        // Else block: create nil
        const zero_val = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero_val); // tag = 0

        // Switch to merge block
        try branch.switchToMerge(true);

        // Return the optional with proper type info
        return .{
            .value = opt_ptr,
            .ty = .{ .optional_type = .{
                .wrapped = if (elem_info.is_struct) .ptr else .i64,
                .wrapped_struct_type = if (elem_info.is_struct) elem_info.name else null,
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

        // The set method returns void (mutates in place)
        // Args: self_ptr, index, value
        var args = try self.allocator.alloc(ir.Value, 3);
        args[0] = base_typed.value;
        args[1] = idx_typed.value;
        args[2] = val_typed.value;

        _ = try self.func().emitCall(mangled_name, args, func_info.return_type);
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
    /// Array is special-cased: receives __ManagedArray directly.
    /// Other types receive an Array (following Swift's ExpressibleByArrayLiteral pattern).
    fn convertInitableFromArrayLiteralImpl(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const arr_lit = decl.value.array_literal;
        const elements = arr_lit.elements;

        debug.astToIr("InitableFromArrayLiteral: {s} with {d} elements", .{ type_name, elements.len });

        const managed_ptr = if (elements.len == 0)
            try self.emitEmptyManagedArray()
        else blk: {
            // Allocate buffer for elements (on heap for ownership)
            const elem_count = try self.func().emitConstI64(@intCast(elements.len));
            const elem_size: i64 = 8; // All elements are 8 bytes (i64, f64, or pointer)
            const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
            const buffer = try self.func().emitHeapAlloc(buffer_size, "set buffer");

            // Store elements into buffer
            for (elements, 0..) |elem, i| {
                const typed = try self.convertExpression(elem);
                const idx_val = try self.func().emitConstI64(@intCast(i));
                const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(elem_ptr, typed.value);
            }

            break :blk try self.emitManagedArray(buffer, elem_count, elem_count);
        };

        // Check if this is the Array type (special-cased: receives __ManagedArray directly)
        // Other types receive an Array (we create one first, then pass it to their init)
        // Note: type_name may be monomorphized like "Array$int", so check for prefix
        const is_array_type = std.mem.eql(u8, type_name, "Array") or std.mem.startsWith(u8, type_name, "Array$");

        // For non-Array types, first create an Array from the __ManagedArray
        var init_arg: ir.Value = undefined;
        if (is_array_type) {
            init_arg = managed_ptr;
        } else {
            // Create an Array by calling Array$init(__ManagedArray)
            const array_init_name = "Array$init";
            const array_func_info = self.func_map.get(array_init_name) orelse {
                self.reportInternalError("Array$init not found for InitableFromArrayLiteral");
                return error.UnknownFunction;
            };

            if (!array_func_info.ir_generated) {
                try self.ensureMethodGenerated(array_init_name);
            }

            // Get Array type info for size
            const array_type_info = self.type_map.get("Array") orelse {
                self.reportError(.E006, "Array");
                return error.UnknownType;
            };

            const array_size = array_type_info.struct_type.size;
            const array_ptr = try self.func().emitAllocaSized(array_size);

            var array_args = try self.func().allocator.alloc(ir.Value, 2);
            array_args[0] = array_ptr;
            array_args[1] = managed_ptr;
            _ = try self.func().emitCall(array_init_name, array_args, array_func_info.return_type);

            init_arg = array_ptr;
        }

        // Call Type$init - the static init method
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

            // Build args: [sret_ptr, init_arg]
            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = result_ptr;
            args[1] = init_arg;

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
            args[0] = init_arg;
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

    /// Convert InitableFromDictionaryLiteral with type annotation: var m Map from K to V = ["a": 1, "b": 2]
    /// Map is special-cased: receives __ManagedArrays directly.
    /// Other types receive two Arrays (following Swift's ExpressibleByDictionaryLiteral pattern).
    fn convertInitableFromDictionaryLiteralSimple(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const map_lit = decl.value.map_literal;
        const entries = map_lit.entries;

        debug.astToIr("InitableFromDictionaryLiteral: {s} with {d} entries", .{ type_name, entries.len });

        var keys_managed_ptr: ir.Value = undefined;
        var values_managed_ptr: ir.Value = undefined;

        if (entries.len == 0) {
            keys_managed_ptr = try self.emitEmptyManagedArray();
            values_managed_ptr = try self.emitEmptyManagedArray();
        } else {
            // Allocate buffers for keys and values on heap
            const elem_count = try self.func().emitConstI64(@intCast(entries.len));
            const elem_size: i64 = 8; // All elements are 8 bytes
            const buffer_size = try self.func().emitConstI64(@intCast(entries.len * @as(usize, @intCast(elem_size))));
            const keys_buffer = try self.func().emitHeapAlloc(buffer_size, "map buffer");
            const values_buffer = try self.func().emitHeapAlloc(buffer_size, "map buffer");

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

            keys_managed_ptr = try self.emitManagedArray(keys_buffer, elem_count, elem_count);
            values_managed_ptr = try self.emitManagedArray(values_buffer, elem_count, elem_count);
        }

        // Check if this is the Map type (special-cased: receives __ManagedArrays directly)
        // Other types receive Arrays (we create them first, then pass to their init)
        // Note: type_name may be monomorphized like "Map$int$int", so check for prefix
        const is_map_type = std.mem.eql(u8, type_name, "Map") or std.mem.startsWith(u8, type_name, "Map$");

        // For non-Map types, first create Arrays from the __ManagedArrays
        var keys_arg: ir.Value = undefined;
        var values_arg: ir.Value = undefined;
        if (is_map_type) {
            keys_arg = keys_managed_ptr;
            values_arg = values_managed_ptr;
        } else {
            // Create Arrays by calling Array$init(__ManagedArray)
            const array_init_name = "Array$init";
            const array_func_info = self.func_map.get(array_init_name) orelse {
                self.reportInternalError("Array$init not found for InitableFromDictionaryLiteral");
                return error.UnknownFunction;
            };

            if (!array_func_info.ir_generated) {
                try self.ensureMethodGenerated(array_init_name);
            }

            // Get Array type info for size
            const array_type_info = self.type_map.get("Array") orelse {
                self.reportError(.E006, "Array");
                return error.UnknownType;
            };

            const array_size = array_type_info.struct_type.size;

            // Create keys Array
            const keys_array_ptr = try self.func().emitAllocaSized(array_size);
            var keys_array_args = try self.func().allocator.alloc(ir.Value, 2);
            keys_array_args[0] = keys_array_ptr;
            keys_array_args[1] = keys_managed_ptr;
            _ = try self.func().emitCall(array_init_name, keys_array_args, array_func_info.return_type);

            // Create values Array
            const values_array_ptr = try self.func().emitAllocaSized(array_size);
            var values_array_args = try self.func().allocator.alloc(ir.Value, 2);
            values_array_args[0] = values_array_ptr;
            values_array_args[1] = values_managed_ptr;
            _ = try self.func().emitCall(array_init_name, values_array_args, array_func_info.return_type);

            keys_arg = keys_array_ptr;
            values_arg = values_array_ptr;
        }

        // Call Type$init(keys, values) - the static init method from InitableFromDictionaryLiteral interface
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        // Look up the function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for InitableFromDictionaryLiteral", .{type_name}) catch "missing init method";
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

            // Build args: [sret_ptr, keys_arg, values_arg]
            var args = try self.func().allocator.alloc(ir.Value, 3);
            args[0] = result_ptr;
            args[1] = keys_arg;
            args[2] = values_arg;

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
            // Non-struct return type (unlikely for InitableFromDictionaryLiteral)
            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = keys_arg;
            args[1] = values_arg;
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

    /// Common implementation for initable types (String, Character, custom types).
    /// String is special-cased: receives __ManagedString directly.
    /// Other types receive a String (following Swift's ExpressibleByStringLiteral pattern).
    fn convertInitableFromBytes(self: *AstToIr, bytes: []const u8, type_name: []const u8, decl_name: []const u8) !void {
        debug.astToIr("InitableFromBytes: {s} with {d} bytes", .{ type_name, bytes.len });

        const managed_ptr = try self.emitManagedStringFromBytes(bytes);

        // String type is special-cased: it receives __ManagedString directly
        // Other types receive a String (we create one first, then pass it to their init)
        const is_string_type = std.mem.eql(u8, type_name, "String");

        // For non-String types, first create a String from the __ManagedString
        var init_arg: ir.Value = undefined;
        if (is_string_type) {
            init_arg = managed_ptr;
        } else {
            // Create a String by calling String$init(__ManagedString) (Builtin.init is also registered as init)
            const string_init_name = "String$init";
            const string_func_info = self.func_map.get(string_init_name) orelse {
                self.reportInternalError("String$init not found for InitableFromStringLiteral");
                return error.UnknownFunction;
            };

            if (!string_func_info.ir_generated) {
                try self.ensureMethodGenerated(string_init_name);
            }

            // Get String type info for size
            const string_type_info = self.type_map.get("String") orelse {
                self.reportError(.E006, "String");
                return error.UnknownType;
            };

            const string_size = string_type_info.struct_type.size;
            const string_ptr = try self.func().emitAllocaSized(string_size);

            var string_args = try self.func().allocator.alloc(ir.Value, 2);
            string_args[0] = string_ptr;
            string_args[1] = managed_ptr;
            _ = try self.func().emitCall(string_init_name, string_args, string_func_info.return_type);

            init_arg = string_ptr;
        }

        // Call Type$init - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for Initable", .{type_name}) catch "missing init method";
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
            const struct_size = type_info.struct_type.size;
            const result_ptr = try self.func().emitAllocaSized(struct_size);

            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = result_ptr;
            args[1] = init_arg;

            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

            try self.func().setValueName(result_ptr, decl_name);
            try self.var_map.put(self.allocator, decl_name, VarInfo.init(
                result_ptr,
                .{ .struct_type = type_name },
                self.current_decl_is_mutable,
                false,
            ));
        } else {
            var args = try self.func().allocator.alloc(ir.Value, 1);
            args[0] = init_arg;
            const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
            const result_ptr = try self.func().emitAlloca(func_info.return_type);
            try self.func().emitStore(result_ptr, result orelse 0);
            try self.func().setValueName(result_ptr, decl_name);
            try self.var_map.put(self.allocator, decl_name, VarInfo.init(
                result_ptr,
                .{ .primitive = func_info.return_type.toMaxonName() },
                self.current_decl_is_mutable,
                false,
            ));
        }
    }

    /// Convert InitableFromStringLiteral: var s string = "hello"
    fn convertInitableFromStringLiteral(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const processed = try self.processEscapeSequences(decl.value.string_literal);
        defer self.allocator.free(processed);
        try self.convertInitableFromBytes(processed, type_name, decl.name);
    }

    /// Convert InitableFromCharLiteral: var c character = 'a'
    /// Character is special-cased: receives __ManagedString directly.
    /// Other types receive a Character (following Swift's ExpressibleByStringLiteral pattern).
    fn convertInitableFromCharLiteral(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const processed = try self.processEscapeSequences(decl.value.char_literal);
        defer self.allocator.free(processed);

        const managed_ptr = try self.emitManagedStringFromBytes(processed);

        // Character type is special-cased: it receives __ManagedString directly
        // Other types receive a Character (we create one first, then pass it to their init)
        const is_character_type = std.mem.eql(u8, type_name, "Character");

        var init_arg: ir.Value = undefined;
        if (is_character_type) {
            init_arg = managed_ptr;
        } else {
            // Create a Character by calling Character$init(__ManagedString)
            const char_init_name = "Character$init";
            const char_func_info = self.func_map.get(char_init_name) orelse {
                self.reportInternalError("Character$init not found for InitableFromCharLiteral");
                return error.UnknownFunction;
            };

            if (!char_func_info.ir_generated) {
                try self.ensureMethodGenerated(char_init_name);
            }

            // Get Character type info for size
            const char_type_info = self.type_map.get("Character") orelse {
                self.reportError(.E006, "Character");
                return error.UnknownType;
            };

            const char_size = char_type_info.struct_type.size;
            const char_ptr = try self.func().emitAllocaSized(char_size);

            var char_args = try self.func().allocator.alloc(ir.Value, 2);
            char_args[0] = char_ptr;
            char_args[1] = managed_ptr;
            _ = try self.func().emitCall(char_init_name, char_args, char_func_info.return_type);

            init_arg = char_ptr;
        }

        // Call Type$init - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for InitableFromCharLiteral", .{type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        const struct_size = type_info.struct_type.size;
        const result_ptr = try self.func().emitAllocaSized(struct_size);

        var args = try self.func().allocator.alloc(ir.Value, 2);
        args[0] = result_ptr;
        args[1] = init_arg;

        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        try self.func().setValueName(result_ptr, decl.name);
        try self.var_map.put(self.allocator, decl.name, VarInfo.init(
            result_ptr,
            .{ .struct_type = type_name },
            self.current_decl_is_mutable,
            false,
        ));
    }

    /// Convert string literal cast: "hello" as MyType
    /// Returns a TypedValue for the result of the cast expression
    fn convertStringLiteralCast(self: *AstToIr, str_literal: []const u8, type_name: []const u8) ConvertError!TypedValue {
        const processed = try self.processEscapeSequences(str_literal);
        defer self.allocator.free(processed);

        const managed_ptr = try self.emitManagedStringFromBytes(processed);

        // Create a String by calling String$init(__ManagedString) (Builtin.init is also registered as init)
        const string_init_name = "String$init";
        const string_func_info = self.func_map.get(string_init_name) orelse {
            self.reportInternalError("String$init not found for cast");
            return error.UnknownFunction;
        };

        if (!string_func_info.ir_generated) {
            try self.ensureMethodGenerated(string_init_name);
        }

        // Get String type info for size
        const string_type_info = self.type_map.get("String") orelse {
            self.reportError(.E006, "String");
            return error.UnknownType;
        };

        const string_size = string_type_info.struct_type.size;
        const string_ptr = try self.func().emitAllocaSized(string_size);

        var string_args = try self.func().allocator.alloc(ir.Value, 2);
        string_args[0] = string_ptr;
        string_args[1] = managed_ptr;
        _ = try self.func().emitCall(string_init_name, string_args, string_func_info.return_type);

        // Call Type$init(String) - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for InitableFromStringLiteral", .{type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        const struct_size = type_info.struct_type.size;
        const result_ptr = try self.func().emitAllocaSized(struct_size);

        var args = try self.func().allocator.alloc(ir.Value, 2);
        args[0] = result_ptr;
        args[1] = string_ptr;
        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        return .{
            .value = result_ptr,
            .ty = .{ .struct_type = type_name },
        };
    }

    /// Convert character literal cast: 'A' as MyType
    /// Returns a TypedValue for the result of the cast expression
    fn convertCharLiteralCast(self: *AstToIr, char_literal: []const u8, type_name: []const u8) ConvertError!TypedValue {
        const processed = try self.processEscapeSequences(char_literal);
        defer self.allocator.free(processed);

        const managed_ptr = try self.emitManagedStringFromBytes(processed);

        // Create a Character by calling Character$init(__ManagedString)
        const char_init_name = "Character$init";
        const char_func_info = self.func_map.get(char_init_name) orelse {
            self.reportInternalError("Character$init not found for cast");
            return error.UnknownFunction;
        };

        if (!char_func_info.ir_generated) {
            try self.ensureMethodGenerated(char_init_name);
        }

        // Get Character type info for size
        const char_type_info = self.type_map.get("Character") orelse {
            self.reportError(.E006, "Character");
            return error.UnknownType;
        };

        const char_size = char_type_info.struct_type.size;
        const char_ptr = try self.func().emitAllocaSized(char_size);

        var char_args = try self.func().allocator.alloc(ir.Value, 2);
        char_args[0] = char_ptr;
        char_args[1] = managed_ptr;
        _ = try self.func().emitCall(char_init_name, char_args, char_func_info.return_type);

        // Call Type$init(Character) - the static init method
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method for InitableFromCharLiteral", .{type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        const struct_size = type_info.struct_type.size;
        const result_ptr = try self.func().emitAllocaSized(struct_size);

        var args = try self.func().allocator.alloc(ir.Value, 2);
        args[0] = result_ptr;
        args[1] = char_ptr;
        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        return .{
            .value = result_ptr,
            .ty = .{ .struct_type = type_name },
        };
    }

    fn convertArrayLiteral(self: *AstToIr, arr_lit: ast.ArrayLiteralExpr) ConvertError!TypedValue {
        const elements = arr_lit.elements;

        if (elements.len == 0) {
            const managed_ptr = try self.emitEmptyManagedArray();
            return .{
                .value = managed_ptr,
                .ty = .{ .array_type = .{ .element_type = .i64, .size = 0, .storage = .heap } },
            };
        }

        const first_typed = try self.convertExpression(elements[0]);
        const elem_type = first_typed.ty.toPrimitiveType();
        const elem_struct_type: ?[]const u8 = switch (first_typed.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type, .error_union_type, .function_type => null,
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
        const buffer = try self.func().emitHeapAlloc(buffer_size, "set buffer");

        // Store elements into buffer
        for (elements, 0..) |elem, i| {
            const typed = if (i == 0) first_typed else try self.convertExpression(elem);
            const idx_val = try self.func().emitConstI64(@intCast(i));
            const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
            if (elem_struct_type) |struct_name| {
                // For structs, copy the struct data
                // Check if this is a temporary String that we're moving (not copying)
                // In that case, we just memcpy without incref since ownership transfers
                const is_temporary_string = std.mem.eql(u8, struct_name, "String") and
                    self.isInTemporaries(typed.value);
                if (is_temporary_string) {
                    // Move semantics: just copy data, don't incref (ownership transfers to array)
                    try self.func().emitMemcpy(elem_ptr, typed.value, @intCast(elem_size));
                    self.removeFromTemporaries(typed.value);
                } else {
                    // Copy semantics: copy and incref
                    try self.emitStructCopy(elem_ptr, typed.value, @intCast(elem_size), struct_name);
                }
            } else {
                // For primitives, store the value directly
                try self.func().emitStore(elem_ptr, typed.value);
            }
        }

        // Initialize __ManagedArray with buffer, length, and capacity
        const managed_ptr = try self.emitManagedArray(buffer, elem_count, elem_count);

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

        // Calculate element sizes based on actual types
        const key_elem_size: i64 = try self.getValueTypeSize(first_key_typed.ty);
        const value_elem_size: i64 = try self.getValueTypeSize(first_value_typed.ty);

        // Allocate buffers for keys and values on heap
        const elem_count = try self.func().emitConstI64(@intCast(entries.len));
        const keys_buffer_size = try self.func().emitConstI64(@intCast(entries.len * @as(usize, @intCast(key_elem_size))));
        const values_buffer_size = try self.func().emitConstI64(@intCast(entries.len * @as(usize, @intCast(value_elem_size))));
        const keys_buffer = try self.func().emitHeapAlloc(keys_buffer_size, "map buffer");
        const values_buffer = try self.func().emitHeapAlloc(values_buffer_size, "map buffer");

        // Store entries into buffers
        for (entries, 0..) |entry, i| {
            const key_typed = if (i == 0) first_key_typed else try self.convertExpression(entry.key.*);
            const value_typed = if (i == 0) first_value_typed else try self.convertExpression(entry.value.*);
            const idx_val = try self.func().emitConstI64(@intCast(i));

            const key_ptr = try self.func().emitGetElemPtr(keys_buffer, idx_val, @intCast(key_elem_size));
            // For structs, memcpy the full value; for primitives, store directly
            if (key_elem_size > 8) {
                try self.func().emitMemcpy(key_ptr, key_typed.value, @intCast(key_elem_size));
            } else {
                try self.func().emitStore(key_ptr, key_typed.value);
            }

            // Ownership transfer: string keys are now owned by the map
            // Remove from temporaries so they don't get cleaned up at scope end
            if (std.mem.eql(u8, key_type_name, "String")) {
                self.removeFromTemporaries(key_typed.value);
            }

            const value_ptr = try self.func().emitGetElemPtr(values_buffer, idx_val, @intCast(value_elem_size));
            if (value_elem_size > 8) {
                try self.func().emitMemcpy(value_ptr, value_typed.value, @intCast(value_elem_size));
            } else {
                try self.func().emitStore(value_ptr, value_typed.value);
            }
        }

        // Create __ManagedArrays for keys and values
        const keys_managed_ptr = try self.emitManagedArray(keys_buffer, elem_count, elem_count);
        const values_managed_ptr = try self.emitManagedArray(values_buffer, elem_count, elem_count);

        // Build init function name: Map$K$V$init (static init from InitableFromDictionaryLiteral interface)
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{map_type_name});
        try self.module.trackString(init_func_name);

        // Look up function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "Map type '{s}' missing init method for InitableFromDictionaryLiteral", .{map_type_name}) catch "missing init method";
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

        // NOTE: We do NOT decref the temporary string keys here.
        // Map$init calls __managed_array_set_at which increfs each key before copying
        // into the Map's internal array. The Map now owns those references.
        // The temp buffer strings have had their refcounts incremented, so when we
        // later decref them (e.g., at scope end), they'll properly decrement.
        // The original string literals (tmp_alloca.3, tmp_alloca.5) will be cleaned
        // up by freeHeapVars at function return.

        // Free the temporary buffers used to pass keys and values to init
        // The Map$init function copies the data into its internal hash table,
        // so we need to clean up the temporary managed arrays
        debug.astToIr("Map literal: emitting heap.free for temp buffers in block {d}", .{self.func().blocks.items.len - 1});
        try self.func().emitHeapFree(keys_buffer, "map literal keys cleanup");
        try self.func().emitHeapFree(values_buffer, "map literal values cleanup");

        return .{
            .value = result_ptr,
            .ty = .{ .struct_type = map_type_name },
        };
    }

    /// Convert Set from [...] expression
    /// Parses: Set from [1, 2, 3] -> creates Set with elements
    fn convertSetFrom(self: *AstToIr, sf: ast.SetFromExpr) ConvertError!TypedValue {
        // The elements expression must be an array literal
        const arr_lit = sf.elements.array_literal;
        const elements = arr_lit.elements;

        debug.astToIr("Set from: type={s}, {d} elements", .{ sf.type_name, elements.len });

        // Handle empty set - still need to create it but can't infer element type
        // For now, we'll use 'int' as default for empty sets (similar to empty arrays)
        var elem_type_name: []const u8 = "int";

        // Create __ManagedArray for elements
        var managed_ptr: ir.Value = undefined;

        if (elements.len == 0) {
            managed_ptr = try self.emitEmptyManagedArray();
        } else {
            // Infer element type from first element
            const first_typed = try self.convertExpression(elements[0]);
            elem_type_name = first_typed.ty.getTypeName() orelse {
                debug.astToIr("error: set element type must be a named type", .{});
                self.reportError(.E006, "set element type must be a named type");
                return error.UnknownType;
            };

            // Allocate buffer for elements (on heap for ownership)
            const elem_count = try self.func().emitConstI64(@intCast(elements.len));
            const elem_size: i64 = 8; // All elements are 8 bytes (i64, f64, or pointer)
            const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
            const buffer = try self.func().emitHeapAlloc(buffer_size, "array buffer");

            // Store elements into buffer
            for (elements, 0..) |elem, i| {
                const typed = if (i == 0) first_typed else try self.convertExpression(elem);
                const idx_val = try self.func().emitConstI64(@intCast(i));
                const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(elem_ptr, typed.value);
            }

            managed_ptr = try self.emitManagedArray(buffer, elem_count, elem_count);
        }

        // Build the monomorphized Set type name: Set$ElementType
        var type_args = [_][]const u8{elem_type_name};
        const set_type_name = try self.getOrCreateMonomorphizedType(sf.type_name, &type_args);

        debug.astToIr("Set type name: {s}", .{set_type_name});

        // First create an Array from the __ManagedArray (Set's init takes Array, not __ManagedArray)
        const array_init_name = "Array$init";
        const array_func_info = self.func_map.get(array_init_name) orelse {
            self.reportInternalError("Array$init not found for Set from");
            return error.UnknownFunction;
        };

        if (!array_func_info.ir_generated) {
            try self.ensureMethodGenerated(array_init_name);
        }

        // Get Array type info for size
        const array_type_info = self.type_map.get("Array") orelse {
            self.reportError(.E006, "Array");
            return error.UnknownType;
        };

        const array_size = array_type_info.struct_type.size;
        const array_ptr = try self.func().emitAllocaSized(array_size);

        var array_args = try self.allocator.alloc(ir.Value, 2);
        array_args[0] = array_ptr;
        array_args[1] = managed_ptr;
        _ = try self.func().emitCall(array_init_name, array_args, array_func_info.return_type);

        // Build init function name: Set$Element$init (static init from InitableFromArrayLiteral interface)
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{set_type_name});
        try self.module.trackString(init_func_name);

        // Look up function and type info
        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "Set type '{s}' missing init method for InitableFromArrayLiteral", .{set_type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        // Trigger lazy generation if needed
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info = self.type_map.get(set_type_name) orelse {
            self.reportError(.E006, set_type_name);
            return error.UnknownType;
        };

        // Set$init returns a struct, so use sret calling convention
        const struct_size = type_info.struct_type.size;
        const result_ptr = try self.func().emitAllocaSized(struct_size);

        // Build args: [sret_ptr, array_ptr]
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = result_ptr;
        args[1] = array_ptr;

        // Call init with sret
        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        return .{
            .value = result_ptr,
            .ty = .{ .struct_type = set_type_name },
        };
    }

    fn convertIndex(self: *AstToIr, idx: ast.IndexExpr) ConvertError!TypedValue {
        const base_typed = try self.convertExpression(idx.base.*);

        // Handle __ManagedArray indexing (stdlib only)
        if (base_typed.ty == .struct_type) {
            if (std.mem.eql(u8, base_typed.ty.struct_type, "__ManagedArray")) {
                // Extract variable name if base is an identifier
                const var_name: ?[]const u8 = if (idx.base.* == .identifier) idx.base.identifier else null;
                return self.convertManagedArrayIndex(base_typed.value, idx.index.*, var_name);
            }
            // Handle stdlib Array types (Array$int, etc.) - call .get() method
            if (std.mem.startsWith(u8, base_typed.ty.struct_type, "Array$")) {
                return self.convertStdlibArrayIndex(base_typed, idx.index.*);
            }
        }

        const arr_info = switch (base_typed.ty) {
            .array_type => |a| a,
            .primitive, .struct_type, .enum_type, .optional_type, .error_union_type, .function_type => {
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
                .set_from,
                .index,
                .array_type,
                .method_call,
                .nil_coalesce,
                .cast,
                .interpolated_string,
                .closure,
                .try_expr,
                .match_expr,
                .enum_case,
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
            break :blk try self.func().emitLoad(size_var_entry.ptr.?, .i64);
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

        // Create 2-way branch: in_bounds -> get element, else -> return nil
        var branch = try BranchBuilder.init(self, in_bounds, "index_in_bounds", "index_out_of_bounds", "index_merge");
        defer branch.deinit();

        // Then block: create some(element)
        const one_val = try self.func().emitConstI64(1);
        try self.func().emitStore(opt_ptr, one_val); // tag = 1

        const elem_ptr = try self.func().emitGetElemPtr(buffer_ptr, idx_typed.value, elem_size);

        const value_slot = try self.func().emitGetFieldPtr(opt_ptr, 8);
        if (arr_info.element_struct_type) |struct_name| {
            // For structs, copy the struct data inline
            // Use plain memcpy without incref - for read-only access (indexing), we're borrowing
            // the data, not taking ownership. If the caller wants ownership (e.g., assigning to
            // a variable), they will copy and incref separately.
            const struct_size: i32 = if (self.type_map.get(struct_name)) |type_info| blk: {
                if (type_info == .struct_type) {
                    break :blk type_info.struct_type.size;
                }
                break :blk 8;
            } else 8;
            try self.func().emitMemcpy(value_slot, elem_ptr, struct_size);
        } else {
            // For primitives, load the value and store it
            const val = try self.func().emitLoad(elem_ptr, arr_info.element_type);
            try self.func().emitStore(value_slot, val);
        }

        // Switch to else block
        try branch.switchToElse(true);

        // Else block: create nil
        const zero_val = try self.func().emitConstI64(0);
        try self.func().emitStore(opt_ptr, zero_val); // tag = 0

        // Switch to merge block
        try branch.switchToMerge(true);

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
        // Calculate actual element size from type
        const elem_size_val: i64 = if (self.type_map.get(resolved_elem)) |ti|
            if (ti == .struct_type) ti.struct_type.size else 8
        else
            8;
        const elem_size = try self.func().emitConstI64(elem_size_val);
        const buffer_size = try self.func().emitBinaryOp(.mul, capacity_typed.value, elem_size, .i64);
        const buffer = try self.func().emitHeapAlloc(buffer_size, "array buffer");
        // Zero-initialize the buffer so hash tables (Map, Set) have correct initial state
        try self.func().emitMemsetDynamic(buffer, 0, buffer_size);
        const managed_ptr = try self.emitManagedArray(buffer, capacity_typed.value, capacity_typed.value);

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

    /// Argument source for method calls - either AST expressions or pre-converted IR values
    const MethodCallArgs = union(enum) {
        expressions: []const ast.Expression,
        ir_values: []const ir.Value,

        fn len(self: MethodCallArgs) usize {
            return switch (self) {
                .expressions => |e| e.len,
                .ir_values => |v| v.len,
            };
        }
    };

    /// Common implementation for emitting method calls (static or instance).
    fn emitMethodCallImpl(
        self: *AstToIr,
        type_name: []const u8,
        method_name: []const u8,
        call_args: MethodCallArgs,
        self_value: ?ir.Value,
    ) ConvertError!TypedValue {
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method_name });
        try self.module.trackString(mangled_name);

        // If method not found, try to create the type on-demand (for generic types like Array$String)
        var func_info_opt = self.func_map.get(mangled_name);
        if (func_info_opt == null and std.mem.indexOf(u8, type_name, "$") != null) {
            // Type name has $, might be a monomorphized type that needs to be created
            _ = self.lookupStructInfo(type_name) catch {};
            func_info_opt = self.func_map.get(mangled_name);
        }
        const func_info = func_info_opt orelse {
            debug.astToIr("error: unknown method '{s}' on type '{s}'", .{ method_name, type_name });
            self.reportError(.E003, mangled_name);
            return error.SemanticError;
        };

        // Validate argument count
        {
            const is_instance_method = self_value != null;
            const expected_args = if (is_instance_method) func_info.param_types.len - 1 else func_info.param_types.len;
            if (call_args.len() != expected_args) {
                const msg = std.fmt.allocPrint(self.allocator, "Wrong number of arguments: expected {d}, got {d}", .{
                    expected_args,
                    call_args.len(),
                }) catch {
                    self.reportError(.E011, method_name);
                    return error.WrongArgumentCount;
                };
                self.last_error = .{
                    .code = .E011,
                    .message = msg,
                    .location = .{
                        .file = self.source_file,
                        .line = @intCast(self.current_line),
                        .column = @intCast(self.current_column),
                    },
                    .message_allocated = true,
                };
                return error.WrongArgumentCount;
            }
        }

        // Trigger lazy generation if method IR hasn't been generated yet
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(mangled_name);
        }

        // Check if return type is optional (needs sret for 16-byte struct)
        const returns_optional = if (func_info.return_value_type) |vt|
            vt == .optional_type
        else
            false;
        // Check if return type is error union (needs sret for error union wrapper)
        const returns_error_union = if (func_info.return_value_type) |vt|
            vt == .error_union_type
        else
            false;

        // Determine argument layout
        const returns_struct = func_info.return_type_name != null or returns_optional or returns_error_union;
        const has_self = self_value != null;
        const sret_offset: usize = if (returns_struct) 1 else 0;
        const self_offset: usize = if (has_self) 1 else 0;
        const num_args = call_args.len() + sret_offset + self_offset;

        const args = try self.allocator.alloc(ir.Value, num_args);
        var arg_idx: usize = 0;

        // Sret buffer as first arg if returning struct, optional, or error union
        var sret_buffer: ?ir.Value = null;
        if (returns_struct) {
            if (returns_optional) {
                const opt_info = func_info.return_value_type.?.optional_type;
                sret_buffer = try self.func().emitAllocaSized(self.getOptionalSize(opt_info));
            } else if (returns_error_union) {
                // Error union size = 8 (tag) + max(success_size, error_size)
                const eu_info = func_info.return_value_type.?.error_union_type;
                const success_size: i32 = if (eu_info.success_struct_type) |sn|
                    if (self.type_map.get(sn)) |ti| if (ti == .struct_type) ti.struct_type.size else 8 else 8
                else
                    8;
                // Error enums are always 8 bytes (i64 ordinal)
                const error_size: i32 = 8;
                sret_buffer = try self.func().emitAllocaSized(8 + @max(success_size, error_size));
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

        // Explicit arguments - either convert expressions or use pre-converted values
        switch (call_args) {
            .expressions => |exprs| {
                for (exprs, 0..) |arg_expr, i| {
                    const arg = try self.convertExpression(arg_expr);
                    var arg_value = arg.value;
                    var arg_ty = arg.ty;

                    const param_index = i + self_offset; // Account for self parameter

                    // Implicit optional wrap: if arg is non-optional but param expects optional
                    if (param_index < func_info.param_types.len) {
                        const param_ty = func_info.param_types[param_index].ty;
                        if (arg_ty != .optional_type and param_ty == .optional_type) {
                            const opt_info = param_ty.optional_type;
                            // Check if arg type matches the optional's wrapped type
                            const type_matches = blk: {
                                if (arg_ty == .struct_type) {
                                    if (opt_info.wrapped_struct_type) |wrapped_name| {
                                        break :blk std.mem.eql(u8, arg_ty.struct_type, wrapped_name);
                                    }
                                }
                                if (arg_ty == .primitive) {
                                    break :blk std.mem.eql(u8, arg_ty.primitive, opt_info.wrapped.toMaxonName());
                                }
                                break :blk false;
                            };
                            if (type_matches) {
                                // Allocate optional struct buffer
                                const opt_size = self.getOptionalSize(opt_info);
                                const opt_buffer = try self.func().emitAllocaSized(opt_size);
                                // Store tag = 1 (has value)
                                const one = try self.func().emitConstI64(1);
                                try self.func().emitStore(opt_buffer, one);
                                // Store value at offset 8
                                const value_ptr = try self.func().emitGetFieldPtr(opt_buffer, 8);
                                if (arg_ty == .struct_type) {
                                    // For struct types, need to memcpy
                                    if (opt_info.wrapped_struct_type) |struct_name| {
                                        if (self.type_map.get(struct_name)) |type_info| {
                                            if (type_info == .struct_type) {
                                                try self.func().emitMemcpy(value_ptr, arg_value, type_info.struct_type.size);
                                            } else {
                                                try self.func().emitStore(value_ptr, arg_value);
                                            }
                                        } else {
                                            try self.func().emitStore(value_ptr, arg_value);
                                        }
                                    } else {
                                        try self.func().emitStore(value_ptr, arg_value);
                                    }
                                } else {
                                    try self.func().emitStore(value_ptr, arg_value);
                                }
                                arg_value = opt_buffer;
                                arg_ty = param_ty;
                            }
                        }
                    }

                    // Implicit int→float promotion: if parameter expects float but arg is int
                    if (param_index < func_info.param_types.len) {
                        arg_value = try self.applyIntToFloatPromotion(arg_value, arg_ty, func_info.param_types[param_index].ty);
                    }

                    // Ownership transfer: when a String temporary is passed to Array.push,
                    // remove it from temporaries so it doesn't get cleaned up (array takes ownership)
                    if (arg_ty == .struct_type and std.mem.eql(u8, arg_ty.struct_type, "String")) {
                        // Check if this is an Array$X$push call where the String is the element being pushed
                        if (std.mem.startsWith(u8, mangled_name, "Array$") and std.mem.endsWith(u8, mangled_name, "$push")) {
                            self.removeFromTemporaries(arg.value);
                        }
                    }

                    args[arg_idx] = arg_value;
                    arg_idx += 1;
                }
            },
            .ir_values => |vals| {
                for (vals) |arg_val| {
                    args[arg_idx] = arg_val;
                    arg_idx += 1;
                }
            },
        }

        const result = try self.func().emitCall(mangled_name, args, func_info.return_type);

        // Return appropriate value
        if (sret_buffer) |buf| {
            if (returns_optional) {
                return .{ .value = buf, .ty = func_info.return_value_type.? };
            }
            if (returns_error_union) {
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

    /// Emit a method call (static or instance) with AST expression arguments.
    fn emitMethodCall(self: *AstToIr, type_name: []const u8, method_name: []const u8, arg_exprs: []const ast.Expression, self_value: ?ir.Value) ConvertError!TypedValue {
        return self.emitMethodCallImpl(type_name, method_name, .{ .expressions = arg_exprs }, self_value);
    }

    /// Emit a method call with pre-converted IR values as arguments.
    /// Used when we already have typed values (e.g., in operator overloading for Equatable).
    fn emitMethodCallWithIrArgs(self: *AstToIr, type_name: []const u8, method_name: []const u8, arg_values: []const ir.Value, self_value: ?ir.Value) ConvertError!TypedValue {
        return self.emitMethodCallImpl(type_name, method_name, .{ .ir_values = arg_values }, self_value);
    }

    /// Emit a method call with support for named arguments and default values.
    fn emitMethodCallWithNamedArgs(
        self: *AstToIr,
        type_name: []const u8,
        method_name: []const u8,
        positional_args: []const ast.Expression,
        named_args: []const ast.NamedArg,
        self_value: ?ir.Value,
    ) ConvertError!TypedValue {
        // If no named args and no need for default value resolution, use simple path
        if (named_args.len == 0) {
            return self.emitMethodCall(type_name, method_name, positional_args, self_value);
        }

        // Look up method info to get parameter info
        const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}${s}", .{ type_name, method_name });
        try self.module.trackString(mangled_name);

        const func_info = self.func_map.get(mangled_name) orelse {
            debug.astToIr("error: unknown method '{s}' on type '{s}'", .{ method_name, type_name });
            self.reportError(.E003, mangled_name);
            return error.SemanticError;
        };

        // Resolve named arguments
        const has_self = self_value != null;
        const resolved_args = try self.resolveCallArguments(
            mangled_name,
            positional_args,
            named_args,
            func_info.param_types,
            has_self, // skip self parameter for instance methods
        );
        defer self.allocator.free(resolved_args);

        // Convert resolved args to a slice of expressions
        var final_exprs = try self.allocator.alloc(ast.Expression, resolved_args.len);
        defer self.allocator.free(final_exprs);

        for (resolved_args, 0..) |resolved, i| {
            final_exprs[i] = switch (resolved) {
                .expr => |e| e,
                .default => |d| d.*,
            };
        }

        return self.emitMethodCall(type_name, method_name, final_exprs, self_value);
    }

    /// Call a method on an already-converted TypedValue (used for for-in loop desugaring)
    fn convertMethodCallOnTyped(self: *AstToIr, base_typed: TypedValue, method_name: []const u8, arg_exprs: []const ast.Expression) ConvertError!TypedValue {
        // Get the type name from the TypedValue
        const type_name = switch (base_typed.ty) {
            .struct_type => |name| name,
            .primitive, .array_type, .enum_type, .optional_type, .error_union_type, .function_type => {
                debug.astToIr("error: method call on non-struct type", .{});
                self.reportError(.E003, method_name);
                return error.SemanticError;
            },
        };

        return self.emitMethodCall(type_name, method_name, arg_exprs, base_typed.value);
    }

    fn convertMethodCallExpr(self: *AstToIr, mcall: ast.MethodCallExpr) ConvertError!TypedValue {
        // Check if base is a type name (static method call: TypeName.method() or enum case construction)
        if (mcall.base.* == .identifier) {
            const base_name = mcall.base.identifier;
            if (self.type_map.get(base_name)) |type_info| {
                // Check if this is an enum case construction (e.g., Container.value(42))
                if (type_info == .enum_type) {
                    const enum_info = type_info.enum_type;
                    // Check if method_name is an enum case with associated values
                    if (enum_info.case_info.get(mcall.method_name)) |case_info| {
                        if (case_info.associated_values.len > 0) {
                            // This is enum case construction
                            return self.convertEnumCase(.{
                                .enum_name = base_name,
                                .case_name = mcall.method_name,
                                .args = mcall.args,
                            });
                        }
                    }
                }
                // Regular static method call
                return self.emitMethodCallWithNamedArgs(base_name, mcall.method_name, mcall.args, mcall.named_args, null);
            }
        }

        // Convert base and get its type (instance method call)
        const base_typed = try self.convertExpression(mcall.base.*);

        // Check if this is a struct type with methods
        if (base_typed.ty == .struct_type) {
            return self.emitMethodCallWithNamedArgs(base_typed.ty.struct_type, mcall.method_name, mcall.args, mcall.named_args, base_typed.value);
        }

        // Check if this is a primitive type with hash/equals methods
        if (base_typed.ty == .primitive) {
            return self.emitPrimitiveMethodCall(base_typed, mcall.method_name, mcall.args);
        }

        // Check if this is an enum type with methods
        if (base_typed.ty == .enum_type) {
            const enum_name = base_typed.ty.enum_type;
            return self.emitMethodCallWithNamedArgs(enum_name, mcall.method_name, mcall.args, mcall.named_args, base_typed.value);
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
            return self.emitMethodCallWithNamedArgs(array_type_name, mcall.method_name, mcall.args, mcall.named_args, base_typed.value);
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

            // Create 2-way branch: is_zero -> return 0, else -> bitcast
            var branch = try BranchBuilder.init(self, is_zero, "float_hash_zero", "float_hash_nonzero", "float_hash_end");
            defer branch.deinit();

            // Then block (zero case): store 0
            const zero_i64 = try self.func().emitConstI64(0);
            try self.func().emitStore(result_ptr, zero_i64);

            // Switch to else block
            try branch.switchToElse(true);

            // Else block (non-zero case): bitcast float to i64
            const bitcast_value = try self.func().emitUnaryOp(.bitcast_f64_to_i64, value, .i64);
            try self.func().emitStore(result_ptr, bitcast_value);

            // Switch to merge block
            try branch.switchToMerge(true);

            // Load result
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

pub fn convert(program: ast.Program, allocator: std.mem.Allocator, mutation_analyzer: ?*const semantic_analysis.MutationAnalyzer) !ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    defer converter.deinit();
    return try converter.convert(program);
}

pub fn convertWithFile(program: ast.Program, allocator: std.mem.Allocator, mutation_analyzer: ?*const semantic_analysis.MutationAnalyzer, source_file: ?[]const u8, out_error: *?err.CompileError) ConvertError!ir.Module {
    return convertWithExternals(program, allocator, mutation_analyzer, source_file, null, null, null, .{}, out_error);
}

/// Options for AST-to-IR conversion
pub const ConvertOptions = struct {
    track_memory: bool = false,
};

/// Convert AST to IR with external function signatures from other modules
pub fn convertWithExternals(
    program: ast.Program,
    allocator: std.mem.Allocator,
    mutation_analyzer: ?*const semantic_analysis.MutationAnalyzer,
    source_file: ?[]const u8,
    external_funcs: ?[]const ExternalFuncSignature,
    external_types: ?[]const ExternalTypeInfo,
    external_interfaces: ?[]const ExternalInterfaceInfo,
    external_enums: ?[]const *const ast.EnumDecl,
    options: ConvertOptions,
    out_error: *?err.CompileError,
) ConvertError!ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    converter.source_file = source_file;
    converter.track_memory = options.track_memory;
    defer converter.deinit();

    // Register external interfaces before types (needed for conformance checking)
    if (external_interfaces) |ext_ifaces| {
        for (ext_ifaces) |ext_iface| {
            try converter.registerInterface(ext_iface.interface_decl.*);
        }
    }

    // Register external enums before types (types may reference enums in their fields)
    if (external_enums) |ext_enums| {
        for (ext_enums) |ext_enum| {
            // Skip if already registered (avoid duplicates from multiple source files)
            if (converter.type_map.contains(ext_enum.name)) continue;

            try converter.registerEnum(ext_enum.*);
            debug.astToIr("Registered external enum '{s}'", .{ext_enum.name});
        }
    }

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
                .error_union => "ptr", // Error unions stored as pointers
                .function_type => "ptr", // Function pointers
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

/// Extract enum declarations from a parsed program (for cross-module compilation)
/// Includes all enums (public and private) so they can be registered with proper visibility
pub fn extractEnumDecls(program: ast.Program, allocator: std.mem.Allocator) ![]const *const ast.EnumDecl {
    var enum_decls = std.ArrayListUnmanaged(*const ast.EnumDecl){};
    errdefer enum_decls.deinit(allocator);

    for (program.enums) |*enum_decl| {
        try enum_decls.append(allocator, enum_decl);
    }

    return enum_decls.toOwnedSlice(allocator);
}

/// Extract interface info from a parsed program (for cross-module compilation)
pub fn extractInterfaceInfo(program: ast.Program, allocator: std.mem.Allocator) ![]ExternalInterfaceInfo {
    var iface_infos = std.ArrayListUnmanaged(ExternalInterfaceInfo){};
    errdefer iface_infos.deinit(allocator);

    for (program.interfaces) |*iface_decl| {
        // Only include exported interfaces
        if (iface_decl.is_export) {
            try iface_infos.append(allocator, .{
                .interface_decl = iface_decl,
            });
        }
    }

    return iface_infos.toOwnedSlice(allocator);
}

/// Extract function signatures from a parsed program (for cross-module compilation)
/// This includes methods from type declarations
pub fn extractFunctionSignaturesFromAst(program: ast.Program, allocator: std.mem.Allocator) ![]ExternalFuncSignature {
    var signatures = std.ArrayListUnmanaged(ExternalFuncSignature){};
    errdefer {
        // Free allocated names, param_types, and return_type_name in case of error
        for (signatures.items) |sig| {
            allocator.free(sig.name);
            if (sig.param_types.len > 0) {
                allocator.free(sig.param_types);
            }
            if (sig.return_type_name) |rtn| {
                allocator.free(rtn);
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

        // Always dupe return_type_name so we can uniformly free it during cleanup
        const return_type_name: ?[]const u8 = if (func.return_type) |rt| blk: {
            const name = getStructNameFromTypeExpr(rt);
            if (name) |n| {
                break :blk try allocator.dupe(u8, n);
            }
            break :blk null;
        } else null;

        const return_value_type: ?ValueType = if (func.return_type) |rt|
            getValueTypeFromTypeExpr(rt)
        else
            null;

        // Extract parameter types (including names and default values for named args)
        const param_types = try allocator.alloc(ParamType, func.params.len);
        for (func.params, 0..) |param, i| {
            param_types[i] = .{
                .ty = getValueTypeFromTypeExprForParam(param.type_expr),
                .name = param.name,
                .default_value = param.default_value,
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
            .doc_comment = func.doc_comment,
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
            // Always dupe return_type_name so we can uniformly free it during cleanup
            var return_type_name: ?[]const u8 = if (method.return_type) |rt| blk: {
                const name = getStructNameFromTypeExpr(rt);
                if (name) |n| {
                    break :blk try allocator.dupe(u8, n);
                }
                break :blk null;
            } else null;

            // Handle "Self" return type - replace with actual type name
            if (return_type_name) |name| {
                if (std.mem.eql(u8, name, "Self")) {
                    // Free the allocated "Self" string and replace with type name
                    allocator.free(name);
                    return_type_name = try allocator.dupe(u8, type_decl.name);
                }
            }

            // Calculate return_value_type, accounting for throws
            var return_value_type: ?ValueType = if (method.return_type) |rt|
                getValueTypeFromTypeExpr(rt)
            else
                null;

            // If method throws, wrap return type in error_union_type
            if (method.throws_type) |error_type| {
                const success_ir_type: ir.Type = if (method.return_type) |rt| getIrTypeFromTypeExpr(rt) else .void;
                return_value_type = .{
                    .error_union_type = .{
                        .success_type = success_ir_type,
                        .success_struct_type = return_type_name,
                        .error_enum_type = error_type,
                    },
                };
            }

            // Extract parameter types for methods (including names and default values for named args)
            // For instance methods, include the implicit self parameter to match buildMethodFuncInfo
            const extra_params: usize = if (method.is_static) 0 else 1;
            const param_types = try allocator.alloc(ParamType, method.params.len + extra_params);
            if (!method.is_static) {
                param_types[0] = .{ .ty = .{ .struct_type = type_decl.name }, .name = "self" };
            }
            for (method.params, 0..) |param, i| {
                param_types[i + extra_params] = .{
                    .ty = getValueTypeFromTypeExprForParam(param.type_expr),
                    .name = param.name,
                    .default_value = param.default_value,
                };
            }

            // For throwing methods, update return_type to ptr (sret)
            const effective_return_type: ir.Type = if (method.throws_type != null) .ptr else return_type;

            // Methods inherit export status from their containing type
            try signatures.append(allocator, .{
                .name = mangled_name,
                .return_type = effective_return_type,
                .return_type_name = return_type_name,
                .return_value_type = return_value_type,
                .is_exported = type_decl.is_export or method.is_export,
                .param_types = param_types,
                .doc_comment = method.doc_comment,
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
        .error_union => .ptr,
        .function_type => .ptr,
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
        .error_union => |eu| {
            const success_ir_type = getIrTypeFromTypeExpr(eu.success_type.*);
            const success_struct_name = getStructNameFromTypeExpr(eu.success_type.*);
            return .{
                .error_union_type = .{
                    .success_type = success_ir_type,
                    .success_struct_type = success_struct_name,
                    .error_enum_type = eu.error_type,
                },
            };
        },
        .simple, .generic, .function_type => null,
    };
}

/// Helper: get struct name from TypeExpr if it's a struct type
/// Get the struct name from a type expression.
/// For generic types like "Array of String", returns the monomorphized name "Array$String".
/// Note: This uses a global buffer, so callers must copy the result if needed.
var mono_name_buffer: [256]u8 = undefined;
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
        .generic => |gen| blk: {
            // Build monomorphized name: BaseType$Arg1$Arg2...
            var fbs = std.io.fixedBufferStream(&mono_name_buffer);
            const writer = fbs.writer();
            writer.writeAll(gen.base_type) catch break :blk gen.base_type;
            for (gen.type_args) |arg| {
                writer.writeByte('$') catch break :blk gen.base_type;
                writer.writeAll(arg) catch break :blk gen.base_type;
            }
            break :blk fbs.getWritten();
        },
        .optional, .error_union, .function_type => null,
    };
}

/// Helper: get ValueType from TypeExpr for function parameters
fn getValueTypeFromTypeExprForParam(te: ast.TypeExpr) ValueType {
    return switch (te) {
        .simple => |name| blk: {
            // Known primitives are primitive types
            if (std.mem.eql(u8, name, "int") or
                std.mem.eql(u8, name, "float") or
                std.mem.eql(u8, name, "bool") or
                std.mem.eql(u8, name, "byte") or
                std.mem.eql(u8, name, "ptr") or
                std.mem.eql(u8, name, "void") or
                std.mem.eql(u8, name, "string") or
                std.mem.eql(u8, name, "character"))
            {
                break :blk .{ .primitive = name };
            }
            // All other simple types (including String) are struct types
            break :blk .{ .struct_type = name };
        },
        .generic => |gen| .{ .struct_type = gen.base_type },
        .optional => |wrapped| .{
            .optional_type = .{
                .wrapped = getIrTypeFromTypeExpr(wrapped.*),
                .wrapped_struct_type = getStructNameFromTypeExpr(wrapped.*),
            },
        },
        .error_union => |eu| .{
            .error_union_type = .{
                .success_type = getIrTypeFromTypeExpr(eu.success_type.*),
                .success_struct_type = getStructNameFromTypeExpr(eu.success_type.*),
                .error_enum_type = eu.error_type,
            },
        },
        .function_type => .{ .primitive = "ptr" }, // Function types are represented as pointers in simple contexts
    };
}
