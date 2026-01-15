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

// Import type-specific modules (layouts + helpers)
const string_helpers = @import("ast_to_ir_string.zig");
const cstring_helpers = @import("ast_to_ir_cstring.zig");
const array_helpers = @import("ast_to_ir_array.zig");
const error_union_helpers = @import("ast_to_ir_error_union.zig");
const map_helpers = @import("ast_to_ir_map.zig");
const cleanup_helpers = @import("ast_to_ir_cleanup.zig");

// Re-export layouts for other modules
pub const ManagedArray = array_helpers.ManagedArray;
pub const String = string_helpers.String;
pub const CString = cstring_helpers.CString;
pub const Array = array_helpers.Array;
pub const ErrorUnion = error_union_helpers.ErrorUnion;
pub const Map = map_helpers.Map;
pub const TypedValue = types.TypedValue;
pub const ValueType = types.ValueType;
pub const freeValueTypeAllocations = types.freeValueTypeAllocations;
pub const FieldInfo = types.FieldInfo;
pub const StructTypeInfo = types.StructTypeInfo;
pub const EnumTypeInfo = types.EnumTypeInfo;
pub const TypeInfo = types.TypeInfo;
pub const FuncInfo = types.FuncInfo;
pub const ExternalFuncSignature = types.ExternalFuncSignature;
pub const ExternalTypeInfo = types.ExternalTypeInfo;
pub const ExternalInterfaceInfo = types.ExternalInterfaceInfo;
pub const ExternalEnumInfo = types.ExternalEnumInfo;
pub const ParamType = types.ParamType;
pub const OwnershipState = types.OwnershipState;
pub const VarInfo = types.VarInfo;
pub const ConvertError = types.ConvertError;
pub const InterfaceInfo = types.InterfaceInfo;
pub const PendingMethod = types.PendingMethod;
pub const SemanticInfo = types.SemanticInfo;
pub const SemanticVarInfo = types.SemanticVarInfo;

// ============================================================================
// Helper Functions
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
    pub fn init(allocator: std.mem.Allocator, max_blocks: usize) !DeferredBlocks {
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
    pub fn deferBlocks(self: *DeferredBlocks, self_ir: *AstToIr, n: usize) void {
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
    pub fn idx(self: DeferredBlocks, n: usize) u32 {
        return self.indices[n];
    }

    /// Restore the i-th deferred block (0 = last popped = first to restore).
    /// This appends it back to the function's block list, making it the current block.
    pub fn restore(self: *DeferredBlocks, self_ir: *AstToIr, n: usize) !void {
        try self_ir.func().blocks.append(self_ir.allocator, self.blocks[n]);
    }

    /// Clean up allocated memory.
    pub fn deinit(self: *DeferredBlocks) void {
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
    type_source_files: std.StringHashMapUnmanaged([]const u8) = .{}, // Source file for each type (for error reporting)
    current_decl_is_mutable: bool,
    mutation_analyzer: ?*const semantic_analysis.MutationAnalyzer,
    current_line: usize,
    current_column: usize,
    // For struct returns: pointer passed by caller for return value
    sret_ptr: ?ir.Value,
    sret_size: i32,
    sret_type_name: ?[]const u8 = null,
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
    // Error handling: true when inside a try expression (to validate throwing calls are wrapped)
    in_try_context: bool = false,
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

    /// Clear any last error, freeing allocated message if needed
    fn clearLastError(self: *AstToIr) void {
        if (self.last_error) |le| {
            if (le.message_allocated) {
                self.allocator.free(le.message);
            }
            self.last_error = null;
        }
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
                    // Free associated_values slices from case_info entries
                    var case_iter = e.case_info.iterator();
                    while (case_iter.next()) |case_entry| {
                        if (case_entry.value_ptr.associated_values.len > 0) {
                            self.allocator.free(case_entry.value_ptr.associated_values);
                        }
                    }
                    e.case_info.deinit(self.allocator);
                    // Free backing values map if present
                    switch (e.backing) {
                        .float => |*m| m.deinit(self.allocator),
                        .string => |*m| m.deinit(self.allocator),
                        .character => |*m| m.deinit(self.allocator),
                        .none, .int => {},
                    }
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

        // Type source files map owns duplicated path strings
        var tsf_iter = self.type_source_files.iterator();
        while (tsf_iter.next()) |entry| {
            self.allocator.free(entry.value_ptr.*);
        }
        self.type_source_files.deinit(self.allocator);

        // Generic params map doesn't own its data (references AST strings)
        self.generic_params.deinit(self.allocator);

        // Clean up labeled loops stack
        self.labeled_loops.deinit(self.allocator);

        // Clean up scope labels stack
        for (self.scope_labels.items) |*scope_set| {
            scope_set.deinit(self.allocator);
        }
        self.scope_labels.deinit(self.allocator);

        // Clean up pending_methods: generic_bindings (shared) and source_file (owned)
        var freed_bindings = std.AutoHashMapUnmanaged([*]const PendingMethod.GenericBinding, void){};
        defer freed_bindings.deinit(self.allocator);
        var pending_iter = self.pending_methods.iterator();
        while (pending_iter.next()) |entry| {
            // Free generic bindings (may be shared across methods of same type)
            if (entry.value_ptr.generic_bindings.len > 0) {
                const ptr = entry.value_ptr.generic_bindings.ptr;
                if (freed_bindings.get(ptr) == null) {
                    freed_bindings.put(self.allocator, ptr, {}) catch {};
                    self.allocator.free(entry.value_ptr.generic_bindings);
                }
            }
            // Free owned source_file string
            if (entry.value_ptr.source_file) |sf| {
                self.allocator.free(sf);
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

    /// Register compiler-internal types needed before processing external types.
    /// This includes primitives (int, float, etc.) and internal types (__ManagedArray, cstring).
    /// Safe to call multiple times - skips already registered types.
    fn registerBuiltinTypes(self: *AstToIr) !void {
        // Register primitive types from the single source of truth
        for (types.primitive_types) |prim| {
            if (!self.type_map.contains(prim.maxon_name)) {
                try self.type_map.put(self.allocator, prim.maxon_name, .{ .primitive = prim.ir_type });
            }
        }

        // Register __ManagedArray compiler-internal type (32 bytes - unified for both arrays and strings)
        // Layout: ptr buffer (8) + i64 len (8) + i64 capacity (8) + i32 flags (4) + i32 parent_off (4)
        // Mode detection via flags & 0x3:
        //   0 = SSO (future - inline storage)
        //   1 = Heap-refcounted (refcount at buffer-8)
        //   2 = Slice (borrowed view into parent)
        if (!self.type_map.contains("__ManagedArray")) {
            const managed_array_fields = try ManagedArray.createFieldDefs(self.allocator);
            self.type_map.put(self.allocator, "__ManagedArray", .{
                .struct_type = .{ .name = "__ManagedArray", .fields = managed_array_fields, .size = ManagedArray.size(), .needs_cleanup = true, .has_managed_buffer = true, .is_internal_type = true },
            }) catch {
                self.allocator.free(managed_array_fields);
                return error.OutOfMemory;
            };
        }

        // Register cstring compiler-internal type (24 bytes)
        // Layout: data(8) + length(8) + managed(8)
        // For non-slice strings: data points to String's buffer, managed points to __ManagedArray (incref'd)
        // For slice strings: data points to newly allocated null-terminated copy, managed = null
        if (!self.type_map.contains("cstring")) {
            const cstring_fields = try CString.createFieldDefs(self.allocator);
            self.type_map.put(self.allocator, "cstring", .{
                .struct_type = .{ .name = "cstring", .fields = cstring_fields, .size = CString.size(), .needs_cleanup = true, .is_cstring = true },
            }) catch {
                self.allocator.free(cstring_fields);
                return error.OutOfMemory;
            };
        }
    }

    pub fn convert(self: *AstToIr, program: ast.Program) !ir.Module {
        // Register builtin types first (primitives and internal types)
        try self.registerBuiltinTypes();

        // Register interfaces first (needed for conformance checking)
        for (program.interfaces) |iface| try self.registerInterface(iface);

        // Check conformance for external types (registered before convert() was called)
        for (self.pending_conformance_checks.items) |type_decl| {
            try self.checkInterfaceConformance(type_decl.*);
        }
        self.pending_conformance_checks.clearRetainingCapacity();

        // Register declarations
        for (program.enums) |enum_decl| {
            // Skip if already registered (e.g., from external_enums in multi-file compilation)
            if (self.type_map.contains(enum_decl.name)) continue;
            try self.registerEnum(enum_decl);
        }
        for (program.types) |type_decl| try self.registerType(type_decl);
        for (program.functions) |fn_decl| try self.registerFunction(fn_decl);

        // Register methods from types
        for (program.types) |type_decl| {
            for (type_decl.methods) |method| {
                try self.registerMethod(type_decl.name, method);
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
        // Skip generic types - their methods are converted lazily when monomorphized
        for (program.types) |type_decl| {
            if (type_decl.generic_params.len > 0) continue;
            for (type_decl.methods) |method| {
                try self.convertMethod(type_decl.name, method);
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
                .ty = .{ .primitive = .int },
            },
            .float => |v| .{
                .value = try self.func().emitConstF64(v),
                .ty = .{ .primitive = .float },
            },
            .bool => |v| .{
                .value = try self.func().emitConstI64(if (v) 1 else 0),
                .ty = .{ .primitive = .bool },
            },
            .string => |s| self.convertStringLiteral(s),
            .array => |elements| {
                // Convert array constant elements to IR values directly
                // instead of going through convertArrayLiteral which expects
                // expressions to still be valid

                // Determine element type from first element (or default to int)
                const elem_prim: types.Primitive = if (elements.len == 0)
                    .int
                else switch (elements[0]) {
                    .int => .int,
                    .float => .float,
                    .bool => .bool,
                    else => .int,
                };

                // Get the Array type name and info
                const arr_type_name = try std.fmt.allocPrint(self.allocator, "Array${s}", .{elem_prim.toMaxonName()});
                try self.module.trackString(arr_type_name);

                // Get or create the monomorphized Array type
                var type_args = [_][]const u8{elem_prim.toMaxonName()};
                _ = try self.getOrCreateMonomorphizedType("Array", &type_args);

                const arr_type_info = self.type_map.get(arr_type_name) orelse {
                    self.reportInternalError("failed to get Array type info");
                    return error.SemanticError;
                };
                const arr_struct_info = &arr_type_info.struct_type;

                // Allocate the full Array struct (includes iterIndex field)
                const array_ptr = try self.func().emitAllocaSized(arr_struct_info.size);

                if (elements.len == 0) {
                    // Initialize with empty managed array at the correct offset
                    const managed_ptr = try string_helpers.getManagedArrayPtr(self, array_ptr.raw(), arr_struct_info.managed_buffer_offset);
                    try array_helpers.ManagedArray.initEmpty(self.func(), managed_ptr);
                } else {
                    // Allocate refcounted buffer for elements
                    const elem_count = try self.func().emitConstI64(@intCast(elements.len));
                    const elem_size: i64 = 8;
                    const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
                    const buffer = try array_helpers.emitAllocRefcountedBuffer(self, buffer_size, "array buffer");

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
                        try self.func().emitStore(elem_ptr.raw(), value);
                    }

                    // Initialize __ManagedArray at the correct offset within the Array struct
                    const managed_ptr = try string_helpers.getManagedArrayPtr(self, array_ptr.raw(), arr_struct_info.managed_buffer_offset);
                    const heap_flags = try self.func().emitConstI32(array_helpers.MODE_HEAP);
                    try array_helpers.ManagedArray.init(self.func(), managed_ptr, buffer, elem_count, elem_count, heap_flags);
                }

                // Initialize iterIndex field to 0
                const zero_i64 = try self.func().emitConstI64(0);
                for (arr_struct_info.fields) |field| {
                    if (std.mem.eql(u8, field.name, "iterIndex")) {
                        try struct_helpers.storeI64Field(self.func(), array_ptr.asStruct(), field.offset, zero_i64);
                        break;
                    }
                }

                return .{
                    .value = array_ptr.raw(),
                    .ty = .{ .struct_type = arr_struct_info },
                };
            },
            .enum_member => |em| .{
                .value = try self.func().emitConstI64(em.value),
                .ty = try self.typeNameToValueType(em.enum_name),
            },
            .runtime_expr => |expr| {
                // For runtime-initialized constants, convert the expression
                // convertArrayLiteral already returns a full Array struct (not __ManagedArray)
                return self.convertExpression(expr);
            },
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
                // Check if it's a primitive type
                if (types.isPrimitiveTypeName(name)) {
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

    /// Register an external type (from stdlib or other modules).
    /// Computes field sizes using type_map, so dependent types must be registered first.
    pub fn registerExternalType(self: *AstToIr, ext_type: ExternalTypeInfo) !void {
        // Skip if already registered (avoid duplicate allocations)
        if (self.type_map.contains(ext_type.name)) return;

        const type_decl = ext_type.type_decl;

        // Set up generic type parameters (e.g., "Element" for Array uses Element)
        for (type_decl.generic_params) |param| {
            try self.generic_params.put(self.allocator, param, "int");
        }
        defer {
            for (type_decl.generic_params) |param| {
                _ = self.generic_params.remove(param);
            }
        }

        // Allow access to internal types (like __ManagedArray) when registering external types
        // since these come from stdlib and may have internal fields
        const saved_in_stdlib = self.in_stdlib_method;
        self.in_stdlib_method = true;
        defer self.in_stdlib_method = saved_in_stdlib;

        // Build field infos - this uses type_map to look up field type sizes
        const field_result = try self.buildFieldInfos(type_decl.fields);

        // Compute needs_cleanup: true if any field's type needs cleanup
        const needs_cleanup = blk: {
            for (field_result.fields) |field| {
                if (field.value_type == .struct_type) {
                    if (field.value_type.struct_type.needs_cleanup) {
                        break :blk true;
                    }
                }
            }
            break :blk false;
        };

        // Check for __ManagedArray field (for COW semantics)
        const managed_offset = types.findManagedArrayField(field_result.fields);

        try self.type_map.put(self.allocator, ext_type.name, .{
            .struct_type = .{
                .name = ext_type.name,
                .fields = field_result.fields,
                .size = field_result.size,
                .is_export = ext_type.is_exported,
                .needs_cleanup = needs_cleanup,
                .has_managed_buffer = managed_offset != null,
                .managed_buffer_offset = managed_offset orelse 0,
            },
        });

        // Store type declaration for conformance checks and monomorphization
        try self.type_decl_map.put(self.allocator, ext_type.name, type_decl.*);
        try self.pending_conformance_checks.append(self.allocator, type_decl);

        // Store source file for error reporting (duplicate the path string to own it)
        if (ext_type.source_path) |sp| {
            const owned_path = try self.allocator.dupe(u8, sp);
            try self.type_source_files.put(self.allocator, ext_type.name, owned_path);
        }

        debug.astToIr("Registered external type '{s}' with size {d}", .{ ext_type.name, field_result.size });
    }

    fn registerEnum(self: *AstToIr, enum_decl: ast.EnumDecl) !void {
        var members: std.StringHashMapUnmanaged(i64) = .{};
        errdefer members.deinit(self.allocator);

        var case_info: std.StringHashMapUnmanaged(types.EnumCaseInfo) = .{};
        errdefer case_info.deinit(self.allocator);

        // Check if this enum conforms to Error interface
        var is_error = false;
        for (enum_decl.conformances) |conformance| {
            if (std.mem.eql(u8, conformance.interface_name, "Error")) {
                is_error = true;
                break;
            }
        }

        // Backing values - we'll initialize the appropriate variant on first value
        var backing: types.BackingValues = .none;

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

            // Handle implicit string-backed enums (name is a string literal)
            if (member.name_is_string_literal) {
                switch (backing) {
                    .none => backing = .{ .string = .{} },
                    .string => {},
                    else => {
                        self.reportErrorAt(.E032, "expected String, got other type", member.line, member.column);
                        return error.SemanticError;
                    },
                }
                try backing.string.put(self.allocator, next_value, member.name);
            }

            // Handle implicit character-backed enums (name is a char literal)
            if (member.name_is_char_literal) {
                switch (backing) {
                    .none => backing = .{ .character = .{} },
                    .character => {},
                    else => {
                        self.reportErrorAt(.E032, "expected character, got other type", member.line, member.column);
                        return error.SemanticError;
                    },
                }
                try backing.character.put(self.allocator, next_value, member.name);
            }

            if (member.value) |value_expr| {
                // Explicit value - infer backing type from expression
                switch (value_expr.*) {
                    .integer => |v| {
                        switch (backing) {
                            .none => backing = .int,
                            .int => {},
                            else => {
                                const msg = try std.fmt.allocPrint(self.allocator, "expected {s}, got int", .{backing.displayName()});
                                try self.module.trackString(msg);
                                self.reportErrorAt(.E032, msg, member.line, member.column);
                                return error.SemanticError;
                            },
                        }
                        next_value = v;
                        // Check for duplicate raw values
                        if (seen_raw_values.get(next_value)) |_| {
                            const msg = try std.fmt.allocPrint(self.allocator, "{d}", .{next_value});
                            try self.module.trackString(msg);
                            self.reportErrorAt(.E031, msg, member.line, member.column);
                            return error.SemanticError;
                        }
                    },
                    .float_lit => |v| {
                        switch (backing) {
                            .none => backing = .{ .float = .{} },
                            .float => {},
                            else => {
                                const msg = try std.fmt.allocPrint(self.allocator, "expected {s}, got float", .{backing.displayName()});
                                try self.module.trackString(msg);
                                self.reportErrorAt(.E032, msg, member.line, member.column);
                                return error.SemanticError;
                            },
                        }
                        try backing.float.put(self.allocator, next_value, v);
                    },
                    .string_literal => |v| {
                        switch (backing) {
                            .none => backing = .{ .string = .{} },
                            .string => {},
                            else => {
                                const msg = try std.fmt.allocPrint(self.allocator, "expected {s}, got String", .{backing.displayName()});
                                try self.module.trackString(msg);
                                self.reportErrorAt(.E032, msg, member.line, member.column);
                                return error.SemanticError;
                            },
                        }
                        try backing.string.put(self.allocator, next_value, v);
                    },
                    .char_literal => |v| {
                        switch (backing) {
                            .none => backing = .{ .character = .{} },
                            .character => {},
                            else => {
                                const msg = try std.fmt.allocPrint(self.allocator, "expected {s}, got character", .{backing.displayName()});
                                try self.module.trackString(msg);
                                self.reportErrorAt(.E032, msg, member.line, member.column);
                                return error.SemanticError;
                            },
                        }
                        try backing.character.put(self.allocator, next_value, v);
                    },
                    else => {},
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
                const av_size: i32 = av_ir_type.sizeInBytes();
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
                .backing = backing,
                .is_error = is_error,
                .has_associated_values = has_associated_values,
                .max_payload_size = max_payload_size,
                .decl_line = enum_decl.block.start_line,
                .decl_column = enum_decl.block.start_column,
                .source_file = self.source_file,
                .is_export = enum_decl.is_export,
            },
        });
        debug.astToIr("Registered enum '{s}' with {d} members (backing: {s}, is_error: {}, has_assoc: {})", .{ enum_decl.name, enum_decl.members.len, @tagName(backing), is_error, has_associated_values });
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
        var ret_primitive_type: ?types.Primitive = null; // Track primitive type (byte, int, etc.) for error unions
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
                    if (type_info.isStruct()) {
                        ret_type_name = getStructNameFromTypeExpr(self.allocator, rt, &self.allocated_type_strings);
                    } else if (type_info == .primitive) {
                        ret_primitive_type = types.Primitive.fromString(base_name);
                    }
                    break :blk type_info.irType();
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
                    .success_primitive_type = ret_primitive_type,
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
            .error_union, .function_type => null,
        };
    }

    /// Convert type expression to string representation for error messages
    fn typeExprToString(type_expr: ast.TypeExpr) []const u8 {
        return switch (type_expr) {
            .simple => |name| name,
            .generic => |gen| gen.base_type,
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
    pub fn getValueTypeSize(self: *AstToIr, value_type: ValueType) !i32 {
        _ = self;
        return switch (value_type) {
            .struct_type => |info| info.size, // includes arrays (Array$T is 40 bytes)
            .primitive, .enum_type, .error_union_type, .function_type => 8,
        };
    }

    /// Convert a resolved type name to a ValueType with pointer to type_map entry
    pub fn typeNameToValueType(self: *AstToIr, type_name: []const u8) !ValueType {
        // Handle primitives first - they may not be in type_map
        if (types.Primitive.fromString(type_name)) |prim| {
            return .{ .primitive = prim };
        }
        const type_info_ptr = self.type_map.getPtr(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };
        return switch (type_info_ptr.*) {
            .struct_type => |*s| .{ .struct_type = s },
            .enum_type => |*e| .{ .enum_type = e },
            .primitive => |ir_ty| .{ .primitive = types.Primitive.fromIrType(ir_ty) },
        };
    }

    /// Non-erroring version of typeNameToValueType - returns null for unknown types
    fn typeNameToValueTypeOpt(self: *AstToIr, type_name: []const u8) ?ValueType {
        // Handle primitives first - they may not be in type_map
        if (types.Primitive.fromString(type_name)) |prim| {
            return .{ .primitive = prim };
        }
        const type_info_ptr = self.type_map.getPtr(type_name) orelse return null;
        return switch (type_info_ptr.*) {
            .struct_type => |*s| .{ .struct_type = s },
            .enum_type => |*e| .{ .enum_type = e },
            .primitive => |ir_ty| .{ .primitive = types.Primitive.fromIrType(ir_ty) },
        };
    }

    /// Convert an ExternalValueType (string-based) to ValueType (pointer-based)
    fn convertExternalValueType(self: *AstToIr, evt: types.ExternalValueType) ?ValueType {
        return switch (evt) {
            .primitive => |p| .{ .primitive = p },
            .struct_type => |name| blk: {
                // First try normal lookup
                if (self.typeNameToValueTypeOpt(name)) |vt| {
                    break :blk vt;
                }
                // If not found and it's an Array type, trigger monomorphization
                if (std.mem.startsWith(u8, name, "Array$")) {
                    const element_name = name["Array$".len..];
                    var type_args = [_][]const u8{element_name};
                    const mono_name = self.getOrCreateMonomorphizedType("Array", &type_args) catch {
                        debug.astToIr("convertExternalValueType: failed to monomorphize {s}", .{name});
                        break :blk null;
                    };
                    if (self.type_map.getPtr(mono_name)) |entry| {
                        if (entry.* == .struct_type) {
                            break :blk .{ .struct_type = &entry.struct_type };
                        }
                    }
                }
                break :blk null;
            },
            .enum_type => |name| self.typeNameToValueTypeOpt(name),
            .error_union_type => |eu| .{ .error_union_type = eu },
            .function_type_marker => .{ .primitive = .ptr },
        };
    }

    /// Convert a slice of ExternalParamTypes to ParamTypes
    fn convertExternalParamTypes(self: *AstToIr, ext_params: []const types.ExternalParamType) ![]ParamType {
        const result = try self.allocator.alloc(ParamType, ext_params.len);
        errdefer self.allocator.free(result);

        for (ext_params, 0..) |ep, i| {
            result[i] = .{
                .ty = self.convertExternalValueType(ep.ty) orelse .{ .primitive = .ptr },
                .name = ep.name,
                .display_name = ep.display_name,
                .default_value = ep.default_value,
            };
        }
        return result;
    }

    /// Get or create an array type in the type_map and return a pointer to its StructTypeInfo.
    /// Arrays are represented as struct types with a fixed size of 40 bytes.
    /// Takes the element type name directly (e.g., "int", "byte", "Point").
    pub fn getOrCreateArrayType(self: *AstToIr, elem_name: []const u8) !*const types.StructTypeInfo {
        // Check if it already exists by name
        const array_name = try std.fmt.allocPrint(self.allocator, "Array${s}", .{elem_name});

        if (self.type_map.getPtr(array_name)) |entry| {
            self.allocator.free(array_name);
            return &entry.struct_type;
        }
        self.allocator.free(array_name);

        // Create via monomorphization to register methods properly
        var type_args = [_][]const u8{elem_name};
        const mono_name = try self.getOrCreateMonomorphizedType("Array", &type_args);

        return &self.type_map.getPtr(mono_name).?.struct_type;
    }

    /// Get an array type by its string name (e.g., "Array$byte") if it exists.
    /// Used when external function signatures reference array types.
    /// Returns null if the type doesn't exist yet (will be created lazily during monomorphization).
    fn getOrCreateArrayTypeByName(self: *AstToIr, array_type_name: []const u8) ?*const types.StructTypeInfo {
        debug.astToIr("getOrCreateArrayTypeByName: '{s}'", .{array_type_name});

        // Check if it already exists
        if (self.type_map.getPtr(array_type_name)) |entry| {
            if (entry.* == .struct_type) {
                debug.astToIr("  -> found existing", .{});
                return &entry.struct_type;
            }
            debug.astToIr("  -> exists but not struct_type", .{});
            return null;
        }

        // Type doesn't exist yet - will be created via getOrCreateMonomorphizedType
        // when actually needed (during method calls or variable access)
        debug.astToIr("  -> not found, will be created lazily", .{});
        return null;
    }

    fn typeExprToValueType(self: *AstToIr, type_expr: ast.TypeExpr) !ValueType {
        switch (type_expr) {
            .simple => {
                const base_name = typeExprBaseTypeName(type_expr).?;
                const resolved = self.resolveTypeName(base_name);
                // Check for internal type access
                try intrinsics.checkInternalTypeAccess(self, resolved);
                const type_info_ptr = self.type_map.getPtr(resolved) orelse {
                    self.reportError(.E006, resolved);
                    return error.UnknownType;
                };
                // Check visibility
                try self.checkTypeVisibility(resolved, type_info_ptr.*);
                return switch (type_info_ptr.*) {
                    .struct_type => |*s| .{ .struct_type = s },
                    .enum_type => |*e| .{ .enum_type = e },
                    .primitive => if (types.Primitive.fromString(resolved)) |prim|
                        .{ .primitive = prim }
                    else {
                        self.reportError(.E006, resolved);
                        return error.UnknownType;
                    },
                };
            },
            .generic => |gen| {
                // For generic types like "Array of int", monomorphize them
                // Resolve type arguments first (e.g., "Element" -> "int" when inside a generic type)
                const resolved_args = try self.allocator.alloc([]const u8, gen.type_args.len);
                defer self.allocator.free(resolved_args);
                for (gen.type_args, 0..) |arg, i| {
                    resolved_args[i] = self.resolveTypeName(arg);
                }
                const mono_name = try self.getOrCreateMonomorphizedType(gen.base_type, resolved_args);
                return try self.typeNameToValueType(mono_name);
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
                    return_ir_type = ret_vt.toIrType();
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
                    .success_type = success_value_type.toIrType(),
                    .success_primitive_type = if (success_value_type == .primitive) success_value_type.primitive else null,
                    .success_struct_type = if (success_value_type == .struct_type) success_value_type.struct_type.name else null,
                    .success_enum_type = if (success_value_type == .enum_type) success_value_type.enum_type.name else null,
                    .error_enum_type = resolved_error,
                } };
            },
        }
    }

    /// Resolve type name, handling 'Self' substitution and generic type parameters
    pub fn resolveTypeName(self: *AstToIr, type_name: []const u8) []const u8 {
        if (std.mem.eql(u8, type_name, "Self")) {
            const resolved = self.current_type_name orelse type_name;

            return resolved;
        }
        // Check if this is a generic type parameter (e.g., "Element")
        if (self.generic_params.get(type_name)) |resolved| {
            return resolved;
        }
        return type_name;
    }

    /// Get or create a monomorphized version of a generic type
    /// e.g., "Container" + ["int"] -> "Container$int"
    pub fn getOrCreateMonomorphizedType(self: *AstToIr, base_type: []const u8, type_args: []const []const u8) ConvertError![]const u8 {
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

        // IMPORTANT: Pre-allocate space in type_map BEFORE building field infos.
        // buildFieldInfos returns FieldInfo with pointers into type_map.
        // If type_map resizes after that, those pointers become invalid.
        // Ensure we have capacity for this new type plus any nested monomorphizations.
        try self.type_map.ensureUnusedCapacity(self.allocator, 16);

        // Register the monomorphized type
        const field_result = self.buildFieldInfos(type_decl.fields) catch |e| {
            self.in_stdlib_method = saved_in_stdlib;
            return e;
        };

        // Compute needs_cleanup: true if any field's type needs cleanup
        const needs_cleanup = blk: {
            for (field_result.fields) |field| {
                if (field.value_type == .struct_type) {
                    if (field.value_type.struct_type.needs_cleanup) {
                        break :blk true;
                    }
                }
            }
            break :blk false;
        };

        // Determine element_type_name for collection types
        // This is used during cleanup to properly decref elements
        const is_array = std.mem.eql(u8, base_type, "Array");
        const element_type_name: ?[]const u8 = if (is_array and type_args.len > 0)
            type_args[0]
        else
            null;

        // Check if element type has has_managed_buffer (for COW cleanup of elements)
        const element_has_managed_buffer = if (element_type_name) |eln|
            if (self.type_map.get(eln)) |elem_type_info|
                elem_type_info == .struct_type and elem_type_info.struct_type.has_managed_buffer
            else
                false
        else
            false;

        // Find __ManagedArray field offset
        const managed_offset = types.findManagedArrayField(field_result.fields);

        const struct_info = StructTypeInfo{
            .name = mono_name,
            .fields = field_result.fields,
            .size = field_result.size,
            .decl_line = type_decl.block.start_line,
            .decl_column = type_decl.block.start_column,
            .needs_cleanup = needs_cleanup,
            .element_type_name = element_type_name,
            .has_managed_buffer = managed_offset != null,
            .managed_buffer_offset = managed_offset orelse 0,
            .element_has_managed_buffer = element_has_managed_buffer,
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
        debug.astToIr("Monomorphizing type '{s}' with {d} methods", .{ mono_name, type_decl.methods.len });
        for (type_decl.methods) |*method| {
            debug.astToIr("  Registering method '{s}'", .{method.name});
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

    /// Get the size of a struct type, triggering monomorphization if needed.
    /// For monomorphized types like "Array$String" or "Map$Key$Value", this will
    /// ensure the type is registered before looking up its size.
    /// Returns the struct size, or null if the type cannot be resolved.
    pub fn getStructSizeWithMonomorphization(self: *AstToIr, struct_name: []const u8) ?i32 {
        // First try direct lookup
        if (self.type_map.get(struct_name)) |type_info| {
            if (type_info == .struct_type) {
                return type_info.struct_type.size;
            }
        }

        // Not found - try to trigger monomorphization for generic types
        if (std.mem.startsWith(u8, struct_name, "Array$")) {
            const elem_type = struct_name["Array$".len..];
            _ = self.getOrCreateMonomorphizedType("Array", &[_][]const u8{elem_type}) catch return null;
        } else if (std.mem.startsWith(u8, struct_name, "Map$")) {
            // Parse Map$Key$Value
            const rest = struct_name["Map$".len..];
            if (std.mem.indexOf(u8, rest, "$")) |sep| {
                const key_type = rest[0..sep];
                const val_type = rest[sep + 1 ..];
                _ = self.getOrCreateMonomorphizedType("Map", &[_][]const u8{ key_type, val_type }) catch return null;
            } else {
                return null;
            }
        } else {
            // Unknown monomorphized type pattern
            return null;
        }

        // Try lookup again after monomorphization
        if (self.type_map.get(struct_name)) |type_info| {
            if (type_info == .struct_type) {
                return type_info.struct_type.size;
            }
        }

        return null;
    }

    /// Get the size of a struct type for error union success types.
    /// This handles the common pattern of extracting success_struct_type from an ErrorUnionInfo
    /// and computing the appropriate allocation size.
    /// Returns 8 for primitives/enums, the actual struct size for structs, or null if struct size is unknown.
    pub fn getErrorUnionSuccessSize(self: *AstToIr, eu_info: types.ErrorUnionInfo) ?i32 {
        if (eu_info.success_struct_type) |struct_name| {
            return self.getStructSizeWithMonomorphization(struct_name);
        }
        // Primitives and enums are 8 bytes
        return 8;
    }

    /// Build FuncInfo from method declaration (by value).
    fn buildMethodFuncInfo(
        self: *AstToIr,
        type_name: []const u8,
        method: ast.MethodDecl,
        ir_generated: bool,
    ) !FuncInfo {
        return self.buildMethodFuncInfoImpl(type_name, method, ir_generated);
    }

    /// Build FuncInfo from method declaration (by pointer).
    fn buildMethodFuncInfoFromPtr(
        self: *AstToIr,
        type_name: []const u8,
        method: *const ast.MethodDecl,
        ir_generated: bool,
    ) !FuncInfo {
        return self.buildMethodFuncInfoImpl(type_name, method.*, ir_generated);
    }

    /// Internal implementation of buildMethodFuncInfo.
    fn buildMethodFuncInfoImpl(
        self: *AstToIr,
        type_name: []const u8,
        method: ast.MethodDecl,
        ir_generated: bool,
    ) !FuncInfo {
        const m = method;

        // Determine return type
        var ret_type_name: ?[]const u8 = null;
        var ret_primitive_type: ?types.Primitive = null; // Track primitive type (byte, int, etc.) for error unions
        var ret_enum_name: ?[]const u8 = null; // Track enum type name for error unions
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
                    if (type_info.isStruct()) {
                        ret_type_name = resolved;
                    } else if (type_info == .enum_type) {
                        ret_enum_name = resolved;
                    } else if (type_info == .primitive) {
                        ret_primitive_type = types.Primitive.fromString(resolved);
                    }
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
            param_types[0] = .{ .ty = try self.typeNameToValueType(type_name), .name = "self" };
        }

        // Register explicit params
        for (m.params, 0..) |param, i| {
            const param_vt = try self.typeExprToValueType(param.type_expr);

            param_types[i + extra_params] = .{
                .ty = param_vt,
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
                    .success_primitive_type = ret_primitive_type,
                    .success_struct_type = ret_type_name,
                    .success_enum_type = ret_enum_name,
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

        const func_info = try self.buildMethodFuncInfoFromPtr(type_name, method, false);

        try self.func_map.put(self.allocator, mangled_name, func_info);

        // Get source file for the base type (for error reporting)
        // type_name might be monomorphized like "Map$String$int", so extract base type "Map"
        const base_type = if (std.mem.indexOf(u8, type_name, "$")) |idx|
            type_name[0..idx]
        else
            type_name;
        const method_source_file = self.type_source_files.get(base_type) orelse self.source_file;

        // Queue for lazy generation (duplicate source_file to own it since source can be freed)
        const owned_source_file = if (method_source_file) |sf|
            try self.allocator.dupe(u8, sf)
        else
            null;
        errdefer if (owned_source_file) |sf| self.allocator.free(sf);

        try self.pending_methods.put(self.allocator, mangled_name, .{
            .type_name = type_name,
            .method = method,
            .generic_bindings = bindings,
            .source_file = owned_source_file,
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
        sret_type_name: ?[]const u8,
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
            .sret_type_name = self.sret_type_name,
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
        self.sret_type_name = saved.sret_type_name;
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
    pub fn ensureMethodGenerated(self: *AstToIr, mangled_name: []const u8) ConvertError!void {
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

        // Save caller's location for error reporting (user code that triggered this method generation)
        const caller_source_file = self.source_file;
        const caller_line = self.current_line;
        const caller_column = self.current_column;

        // Generate the method IR (source_file stays as caller's file for error reporting)

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

        // Propagate error if method generation failed, with caller's location
        if (gen_err) |e| {
            // Update error location to point to user code that triggered the method generation
            if (self.last_error) |*le| {
                le.location.file = caller_source_file;
                le.location.line = @intCast(caller_line);
                le.location.column = @intCast(caller_column);
            }
            return e;
        }

        // Mark as generated in func_map
        if (self.func_map.getPtr(mangled_name)) |info_ptr| {
            info_ptr.ir_generated = true;
        }

        // Remove from pending and free owned data
        if (pending.source_file) |sf| {
            self.allocator.free(sf);
        }
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
        ir_type: ir.Type,
        type_name: ?[]const u8 = null,
    };

    /// Analyze return type to determine if sret is needed and get IR type
    /// resolve_names: if true, resolves type names through generic params (for methods)
    fn analyzeReturnType(self: *AstToIr, return_type: ?ast.TypeExpr, resolve_names: bool) !SretInfo {
        if (return_type == null) {
            return .{ .uses_sret = false, .struct_size = 0, .ir_type = .void };
        }

        const rt = return_type.?;
        var uses_sret = false;
        var sret_struct_size: i32 = 0;

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
            .error_union => |eu| {
                // Error unions use sret (16-byte wrapper: tag + value)
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
            .error_union, .function_type => .ptr,
        };

        // Get the resolved type name for sret type checking
        const type_name: ?[]const u8 = switch (rt) {
            .simple => |name| if (resolve_names) self.resolveTypeName(name) else name,
            .generic => |gen| blk: {
                var resolved_args: [8][]const u8 = undefined;
                for (gen.type_args, 0..) |arg, i| {
                    if (i >= 8) break;
                    resolved_args[i] = if (resolve_names) self.resolveTypeName(arg) else arg;
                }
                break :blk self.getOrCreateMonomorphizedType(gen.base_type, resolved_args[0..gen.type_args.len]) catch null;
            },
            .error_union, .function_type => null,
        };

        return .{
            .uses_sret = uses_sret,
            .struct_size = sret_struct_size,
            .ir_type = ir_type,
            .type_name = type_name,
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
        self.sret_type_name = null;

        // If returning struct, first parameter is sret pointer
        var param_offset: i32 = 0;
        if (sret_info.uses_sret) {
            self.sret_ptr = try ir_func.emitParam(0, .ptr);
            self.sret_size = sret_info.struct_size;
            self.sret_type_name = sret_info.type_name;
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
        } else if (self.current_func_throws_type != null and self.sret_ptr != null) {
            // Void throwing function - ret_type is .ptr for sret, but original return was void
            // Need to set success tag (0) and return via sret when falling through without throwing
            const block = self.func().currentBlock() orelse return;
            const needs_implicit_ret = block.instructions.items.len == 0 or
                block.instructions.items[block.instructions.items.len - 1].op != .ret;
            if (needs_implicit_ret) {
                const sret = self.sret_ptr.?;
                // Write tag = 0 (success) to sret
                const zero = try self.func().emitConstI64(0);
                try self.func().emitStore(sret, zero);
                try cleanup_helpers.freeHeapAllocations(self);
                try self.func().emitRet(sret);
            }
        }
    }

    /// Check for unused variables and report errors
    fn checkUnusedVariables(self: *AstToIr) !void {
        var iter = self.var_map.iterator();
        while (iter.next()) |entry| {
            // Skip variables starting with underscore (intentionally unused)
            if (!entry.value_ptr.used and !std.mem.startsWith(u8, entry.key_ptr.*, "_")) {
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
            sret_info.ir_type = .ptr; // sret uses pointer return
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
                try self.typeNameToValueType(type_name),
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

        // main cannot throw - errors must be handled within main
        if (std.mem.eql(u8, decl.name, "main") and decl.throws_type != null) {
            self.reportErrorAt(.E054, "main", decl.block.start_line, decl.block.start_column);
            return error.SemanticError;
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
            sret_info.ir_type = .ptr; // sret uses pointer return
        }

        const ir_func = try self.module.addFunctionWithExport(decl.name, sret_info.ir_type, decl.is_export);
        const param_offset = try self.setupFunctionState(ir_func, decl.name, sret_info);

        // Register parameters (offset by 1 if using sret)
        for (decl.params, 0..) |param, i| {
            try self.registerParameter(param, @as(i32, @intCast(i)) + param_offset);
        }

        // Convert body and add implicit return for void functions
        try self.convertBodyAndFinalize(decl.body, sret_info.ir_type);

        try self.checkUnusedVariables();
    }

    fn registerParameter(self: *AstToIr, param: ast.ParamDecl, idx: i32) !void {
        const value_type = try self.typeExprToValueType(param.type_expr);

        switch (value_type) {
            .struct_type => |struct_info| {
                const param_val = try self.func().emitParam(idx, .ptr);
                try self.func().setValueName(param_val, param.name);

                // For stdlib Array parameters, check if mutation transfers ownership
                var owns_memory = false;
                if (std.mem.startsWith(u8, struct_info.name, "Array$")) {
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
                    .{ .struct_type = struct_info },
                    true,
                    false,
                );
                if (owns_memory) {
                    var_info.is_parameter = false; // We now own it, should free on return
                }
                try self.var_map.put(self.allocator, param.name, var_info);
            },
            .error_union_type => {
                // Reference types are passed as pointers - store in a stack slot
                const param_val = try self.func().emitParam(idx, .ptr);
                const slot_ptr = try self.func().emitAlloca(.ptr);
                try self.func().setValueName(slot_ptr.raw(), param.name);
                try self.func().emitStore(slot_ptr.raw(), param_val);

                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    slot_ptr.raw(),
                    value_type,
                    true,
                    true,
                ));
            },
            .primitive, .enum_type => {
                const ir_type = value_type.toIrType();
                const param_val = try self.func().emitParam(idx, ir_type);
                try self.func().setValueName(param_val, param.name);
                const ptr = try self.func().emitAlloca(ir_type);
                try self.func().setValueName(ptr.raw(), param.name);
                try self.func().emitStore(ptr.raw(), param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.init(
                    ptr.raw(),
                    value_type,
                    true,
                    false,
                ));
                if (std.mem.eql(u8, param.name, "value")) {}
            },
            .function_type => {
                // Function pointers are passed as pointers - store in a stack slot
                const param_val = try self.func().emitParam(idx, .ptr);
                try self.func().setValueName(param_val, param.name);
                const ptr = try self.func().emitAlloca(.ptr);
                try self.func().setValueName(ptr.raw(), param.name);
                try self.func().emitStore(ptr.raw(), param_val);
                try self.var_map.put(self.allocator, param.name, VarInfo.initParam(
                    ptr.raw(),
                    value_type,
                    true,
                    true, // is_heap_allocated for function pointers
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
                // Clean up any temporary strings from the initializer expression
                // (e.g., string arguments to function calls in the initializer)
                try cleanup_helpers.cleanupTemporaryStrings(self);
            },
            .var_decl => |decl| {
                self.current_decl_is_mutable = true;
                try self.convertVarDecl(decl);
                // Clean up any temporary strings from the initializer expression
                try cleanup_helpers.cleanupTemporaryStrings(self);
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
                try cleanup_helpers.cleanupTemporaryStrings(self);
            },
            .method_call => |mcall| {
                _ = try self.convertMethodCallExpr(mcall);
                // Clean up any temporary strings created during this call
                try cleanup_helpers.cleanupTemporaryStrings(self);
            },
            .if_stmt => |if_s| try self.convertIfStmt(if_s),
            .while_stmt => |while_s| try self.convertWhileStmt(while_s),
            .for_stmt => |for_s| try self.convertForStmt(for_s),
            .break_stmt => |brk| try self.convertBreakStmt(brk),
            .continue_stmt => |cont| try self.convertContinueStmt(cont),
            // Error handling
            .throw_stmt => |throw_s| try self.convertThrowStmt(throw_s),
            .try_stmt => |try_s| {
                // try statement for void-returning throwing functions
                _ = try self.convertTryExpr(try_s);
            },
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
                .error_union, .function_type => null,
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
                            .error_union, .function_type => unreachable,
                        };
                        try array_helpers.convertInitableFromArrayLiteralSimple(self, decl, resolved_type_name);
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
                            .error_union, .function_type => unreachable,
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

        // Function types need alloca for the function pointer
        const is_function_type = init_typed.ty == .function_type;

        // Primitives/enums need alloca for the value
        const needs_alloca = init_typed.ty == .primitive or init_typed.ty == .enum_type or is_function_type;

        // Struct types from field accesses need to be copied to a new local allocation
        // to avoid aliasing issues (e.g., var oldElements = elements before overwriting elements)
        const needs_struct_copy = init_typed.ty == .struct_type and
            (decl.value == .identifier or decl.value == .field_access);

        const ptr = if (needs_struct_copy) blk: {
            // Allocate new memory for the struct and copy data
            const struct_info = init_typed.ty.struct_type;
            const size = struct_info.size;
            const p = try self.func().emitAllocaSized(size);
            try self.func().setValueName(p.raw(), decl.name);
            try string_helpers.emitStructCopy(self, p.asStruct(), ir.toStructPtr(init_typed.value), size, struct_info.name);
            break :blk p.raw();
        } else if (needs_alloca) blk: {
            const alloca_type = if (is_function_type) .ptr else init_typed.ty.toIrType();
            const p = try self.func().emitAlloca(alloca_type);
            try self.func().setValueName(p.raw(), decl.name);
            try self.func().emitStore(p.raw(), init_typed.value);
            break :blk p.raw();
        } else blk: {
            try self.func().setValueName(init_typed.value, decl.name);
            break :blk init_typed.value;
        };

        // Function types use heap allocation (pointer indirection), everything else is stack-allocated
        try self.var_map.put(self.allocator, decl.name, VarInfo.init(
            ptr,
            init_typed.ty,
            self.current_decl_is_mutable,
            is_function_type,
        ));

        // If this was a temporary String, the variable now owns it - remove from temporaries
        if (string_helpers.isStringType(init_typed.ty)) {
            cleanup_helpers.removeFromTemporaries(self, init_typed.value);
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
            cleanup_helpers.markStringBorrowed(self, source_name);
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
                    if (self.current_func_throws_type != null) {
                        // Returning struct init from throwing function - wrap in success result
                        // Write tag = 0 (success) first
                        const zero = try self.func().emitConstI64(0);
                        try self.func().emitStore(sret, zero);
                        // Initialize struct at offset 8 (after the tag)
                        const value_ptr = try ErrorUnion.getValuePtr(self.func(), sret);
                        try self.initStructInto(expr.struct_init, value_ptr.raw());
                    } else {
                        // Initialize struct directly into sret buffer (no intermediate copy)
                        try self.initStructInto(expr.struct_init, sret);
                    }
                    try cleanup_helpers.freeHeapAllocations(self);
                    try self.func().emitRet(sret);
                    return;
                }

                // Returning an existing struct variable or error union - copy to sret buffer
                const typed_val = try self.convertExpression(expr);

                // If returning from a throwing function, wrap in success result
                if (self.current_func_throws_type != null) {
                    // Write tag = 0 (success) to sret
                    const zero = try self.func().emitConstI64(0);
                    try self.func().emitStore(sret, zero);
                    // Write value at offset 8
                    const value_ptr = try ErrorUnion.getValuePtr(self.func(), sret);
                    // For struct types, memcpy the contents; for primitives, store the value
                    if (typed_val.ty == .struct_type) {
                        const struct_info = typed_val.ty.struct_type;
                        try self.func().emitMemcpy(value_ptr.asRawPtr(), ir.toRawPtr(typed_val.value), struct_info.size);
                        // For types with COW semantics that are NOT from a variable (e.g., from array element access),
                        // we need to incref since we're copying from shared data
                        if (struct_info.has_managed_buffer and expr != .identifier) {
                            try string_helpers.emitStringIncref(self, ir.toStringPtr(value_ptr.raw()), "<array element return>");
                        }
                    } else {
                        try self.func().emitStore(value_ptr.raw(), typed_val.value);
                    }
                    // Mark returned variable as moved so cleanup doesn't free its heap resources
                    if (expr == .identifier) {
                        if (self.var_map.getPtr(expr.identifier)) |var_info| {
                            // Mark as moved for any type with heap resources (uses needs_cleanup flag)
                            if (var_info.ty == .struct_type and var_info.ty.struct_type.needs_cleanup) {
                                var_info.markMoved("return", self.current_line);
                                if (self.track_memory) {
                                    try self.func().emitTrackMove(expr.identifier);
                                }
                            }
                        }
                    }
                    try cleanup_helpers.freeHeapAllocations(self);
                    try self.func().emitRet(sret);
                    return;
                }

                // Check return type matches expected type
                if (self.sret_type_name) |expected| {
                    if (typed_val.ty.getTypeName()) |actual| {
                        if (!std.mem.eql(u8, actual, expected)) {
                            self.reportError(.E022, actual);
                            return error.TypeMismatch;
                        }
                    }
                }

                try self.func().emitMemcpy(ir.toRawPtr(sret), ir.toRawPtr(typed_val.value), self.sret_size);
                // Mark returned variable as moved so cleanup doesn't free its heap resources
                if (expr == .identifier) {
                    if (self.var_map.getPtr(expr.identifier)) |var_info| {
                        // Mark as moved for any type with heap resources (uses needs_cleanup flag)
                        if (var_info.ty == .struct_type and var_info.ty.struct_type.needs_cleanup) {
                            var_info.markMoved("return", self.current_line);
                            if (self.track_memory) {
                                try self.func().emitTrackMove(expr.identifier);
                            }
                        }
                    }
                }
                // If returning a String (including from interpolation), remove from temporaries
                // since ownership transfers to caller via sret
                if (string_helpers.isStringType(typed_val.ty)) {
                    cleanup_helpers.removeFromTemporaries(self, typed_val.value);
                }
                try cleanup_helpers.freeHeapAllocations(self);
                try self.func().emitRet(sret);
                return;
            }

            const typed_val = try self.convertExpression(expr);

            // Convert float to int if needed
            if (self.func().return_type == .i64 and typed_val.ty.toIrType() == .f64) {
                ret_value = try self.func().emitUnaryOp(.fptosi, typed_val.value, .i64);
            } else {
                ret_value = typed_val.value;
            }
        }

        // Free heap allocations before return
        try cleanup_helpers.freeHeapAllocations(self);

        try self.func().emitRet(ret_value);
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
            try cleanup_helpers.checkStringNotBorrowed(self, assign.target);

            var_info.used = true;

            // IMPORTANT: Evaluate RHS BEFORE freeing old value, since RHS may reference the old value
            // (e.g., result = result.concat(b) needs result's buffer while evaluating RHS)
            const value_typed = try self.convertExpression(assign.value);

            // Free old heap memory AFTER evaluating RHS
            try cleanup_helpers.freeHeapVar(self, var_info.*, false, assign.target);

            const is_reference = var_info.ty == .struct_type;
            if (is_reference and !var_info.is_heap_allocated) {
                // For struct reassignment, we need to copy the data to the original stack location
                // Just updating var_info.ptr would cause loops to use stale data
                if (var_info.ty == .struct_type) {
                    const struct_name = var_info.ty.struct_type.name;
                    const struct_info_result = try self.lookupStructInfo(struct_name);
                    const size = struct_info_result.size;
                    try self.func().emitMemcpy(ir.toRawPtr(var_info.ptr.?), ir.toRawPtr(value_typed.value), size);
                    // Handle String refcount - the new value's refcount is already correct from expression evaluation
                    // but we need to incref if copying from another variable
                    if (assign.value == .identifier or assign.value == .field_access) {
                        if (self.areTypesEquivalent(struct_name, "String")) {
                            try string_helpers.emitStringIncref(self, ir.toStringPtr(var_info.ptr.?), assign.target);
                        }
                    }
                    // For String assignment from a temporary (literal/interpolation/etc),
                    // remove the source from temporaries since ownership was transferred to the variable
                    if (string_helpers.isStringType(var_info.ty)) {
                        cleanup_helpers.removeFromTemporaries(self, value_typed.value);
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
            if (string_helpers.isStringType(value_typed.ty) and (assign.value == .identifier or assign.value == .field_access)) {
                try string_helpers.emitStringIncref(self, ir.toStringPtr(var_info.ptr.?), assign.target);
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
                            const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(self_val), field.offset);

                            // Track ownership transfer: source variable is moved to field
                            try self.trackFieldOwnershipTransfer(assign.value, type_name, field.is_mutable);

                            // Check if field is a struct type (including Arrays, which are now embedded structs)
                            const is_embedded_struct = field.value_type == .struct_type;
                            if (is_embedded_struct) {
                                // Struct fields are embedded inline - copy the full data
                                try string_helpers.emitStructCopy(self, field_ptr, ir.toStructPtr(value_typed.value), field.size, field.structName());
                            } else {
                                try self.func().emitStore(field_ptr.raw(), value_typed.value);
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
            .struct_type => |struct_info| struct_info.name,
            .primitive, .enum_type, .error_union_type, .function_type => {
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

        const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(base.value), field_info.offset);
        const val_typed = try self.convertExpression(assign.value);

        // Check if field is a struct type (including Arrays, which are now embedded structs)
        const is_embedded_struct = field_info.value_type == .struct_type;
        if (is_embedded_struct) {
            // Struct fields are embedded inline - copy the full data
            try string_helpers.emitStructCopy(self, field_ptr, ir.toStructPtr(val_typed.value), field_info.size, field_info.structName());
        } else {
            try self.func().emitStore(field_ptr.raw(), val_typed.value);
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
            if (std.mem.startsWith(u8, base_typed.ty.struct_type.name, "Array$")) {
                try array_helpers.convertStdlibArrayIndexAssign(self, base_typed, assign.index.*, assign.value);
                return;
            }
        }

        // Generic indexing for non-Array struct types (unlikely but handle gracefully)
        const idx_typed = try self.convertExpression(assign.index.*);
        const val_typed = try self.convertExpression(assign.value);

        // Default fallback: simple 8-byte element store
        const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(base_typed.value), idx_typed.value, 8);
        try self.func().emitStore(elem_ptr.raw(), val_typed.value);
    }

    fn convertIfStmt(self: *AstToIr, if_stmt: ast.IfStmt) ConvertError!void {
        // Check for duplicate block identifier at this scope level
        try self.checkAndRegisterLabel(if_stmt.block.identifier);

        // Check for if-try form
        if (if_stmt.if_try) |if_try| {
            return self.convertIfTryStmt(if_stmt, if_try);
        }

        // Convert condition expression
        const cond_typed = try self.convertExpression(if_stmt.condition);

        // Clean up any temporary strings created during condition evaluation
        try cleanup_helpers.cleanupTemporaryStrings(self);

        const condition_value = cond_typed.value;

        // Determine what blocks we need
        const has_else_block = hasElse(if_stmt.children);

        // Save entry block index - we'll emit br_cond later with correct target indices
        const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

        // Create then block
        const then_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("then");

        // Create else block (if needed) - actual index determined after then body
        if (has_else_block) {
            _ = try self.func().addBlock("else");
        }

        // Create end block - actual index determined after all bodies
        _ = try self.func().addBlock("end");

        // DON'T emit br_cond yet - nested ifs in body will shift block indices
        // We'll emit it after all blocks are restored with correct indices

        // Defer blocks: end always, else conditionally
        const deferred_count: usize = if (has_else_block) 2 else 1;
        var deferred = try DeferredBlocks.init(self.allocator, deferred_count);
        defer deferred.deinit();
        deferred.deferBlocks(self, deferred_count);

        // Now current block is "then" block

        // Save ownership states before if body - if the body terminates (returns/breaks),
        // we need to restore states since code after the if is only reachable when condition is false
        var saved_states: std.StringHashMapUnmanaged(types.OwnershipState) = .empty;
        defer saved_states.deinit(self.allocator);
        {
            var it = self.var_map.iterator();
            while (it.next()) |entry| {
                try saved_states.put(self.allocator, entry.key_ptr.*, entry.value_ptr.state);
            }
        }

        // Convert then body statements (push new scope for nested labels)
        try self.pushLabelScope();
        for (getPrimaryBody(if_stmt.children)) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

        // Track the block that needs a branch to end (we'll add it after restoring end block)
        var then_exit_block_idx: ?u32 = null;
        const then_terminates = if (self.func().currentBlock()) |block|
            block.instructions.items.len > 0 and block.instructions.items[block.instructions.items.len - 1].op == .ret
        else
            false;
        if (!then_terminates) {
            then_exit_block_idx = @intCast(self.func().blocks.items.len - 1);
        } else {
            // If then block terminates, restore ownership states since code after if is only
            // reachable when the if condition is false (so moves in then block didn't happen)
            var sit = saved_states.iterator();
            while (sit.next()) |entry| {
                if (self.var_map.getPtr(entry.key_ptr.*)) |var_info| {
                    var_info.state = entry.value_ptr.*;
                    if (entry.value_ptr.* == .owned) {
                        var_info.moved_to = null;
                        var_info.moved_line = 0;
                    }
                }
            }
        }

        // Restore and generate else block if needed
        var else_exit_block_idx: ?u32 = null;
        var actual_else_block_idx: u32 = undefined;
        if (has_else_block) {
            actual_else_block_idx = @intCast(self.func().blocks.items.len);
            // else_block is at index 1 (second popped)
            try deferred.restore(self, 1);

            // Check for else-if chain
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
        const branch_target_if_false = if (has_else_block) actual_else_block_idx else actual_end_block_idx;
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

    /// Convert if-try statement: `if try expr` or `if let x = try expr`
    /// Uses TryOtherwiseContext to handle proper reference counting for String types
    fn convertIfTryStmt(self: *AstToIr, if_stmt: ast.IfStmt, if_try: *const ast.IfTryCondition) ConvertError!void {
        // Mark that we're in a try context so throwing calls are allowed
        const was_in_try = self.in_try_context;
        self.in_try_context = true;
        defer self.in_try_context = was_in_try;

        // Evaluate the try expression (this calls the throwing function)
        const result_typed = try self.convertExpression(if_try.try_expr.*);

        // Validate that the expression is a throwing function (returns error union)
        if (result_typed.ty != .error_union_type) {
            self.reportErrorAt(.E055, "expression does not throw", if_stmt.block.start_line, if_stmt.block.start_column);
            return error.SemanticError;
        }

        // Check the tag at offset 0: 0 = success, 1 = error
        const tag = try self.func().emitLoad(result_typed.value, .i64);
        const zero = try self.func().emitConstI64(0);
        const is_success = try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64);

        // Use TryOtherwiseContext for proper handling (same as `let x = try ... otherwise`)
        // This handles String incref correctly.
        // Pass branch_to_merge=false so we stay in success block to run the if-body
        var ctx = try TryOtherwiseContext.initWithOptions(self, result_typed, is_success, "if_try_else", false);
        defer ctx.deinit();

        // Now we're in the success block - TryOtherwiseContext copied the value
        // to ctx.result_ptr with proper incref for Strings, but didn't branch yet

        // Determine what blocks we need
        const has_else_block = hasElse(if_stmt.children);

        // Save ownership states before if body
        var saved_states: std.StringHashMapUnmanaged(types.OwnershipState) = .empty;
        defer saved_states.deinit(self.allocator);
        {
            var it = self.var_map.iterator();
            while (it.next()) |entry| {
                try saved_states.put(self.allocator, entry.key_ptr.*, entry.value_ptr.state);
            }
        }

        // If binding form, register the variable pointing to ctx.result_ptr
        if (if_try.binding_name) |binding_name| {
            try self.var_map.put(self.allocator, binding_name, VarInfo.init(ctx.result_ptr, ctx.success_type, false, false));
        }

        // Convert then body statements
        try self.pushLabelScope();
        for (getPrimaryBody(if_stmt.children)) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

        // Check if then block terminates (returns) BEFORE emitting cleanup
        const then_terminates = if (self.func().currentBlock()) |block|
            block.instructions.items.len > 0 and block.instructions.items[block.instructions.items.len - 1].op == .ret
        else
            false;

        // Remove binding from scope if it was added
        // For String bindings, emit decref before removing from scope
        // But skip if block terminates (return cleanup already handles it)
        if (if_try.binding_name) |binding_name| {
            if (!then_terminates) {
                if (self.var_map.get(binding_name)) |var_info| {
                    if (var_info.ptr) |ptr| {
                        if (string_helpers.isStringType(var_info.ty)) {
                            try string_helpers.emitStringDecref(self, ir.toStringPtr(ptr), binding_name);
                        }
                    }
                }
            }
            _ = self.var_map.remove(binding_name);
        }

        if (then_terminates) {
            // If then block terminates, restore ownership states
            var sit = saved_states.iterator();
            while (sit.next()) |entry| {
                if (self.var_map.getPtr(entry.key_ptr.*)) |var_info| {
                    var_info.state = entry.value_ptr.*;
                    if (entry.value_ptr.* == .owned) {
                        var_info.moved_to = null;
                        var_info.moved_line = 0;
                    }
                }
            }
        }

        // Branch from then block to merge block (skip else)
        if (!then_terminates) {
            try ctx.emitBranchToMerge(self);
        }

        // Switch to error block for else handling
        // (with branch_to_merge=false, we're still in success block)
        try ctx.restoreBlock(self, 1);

        if (has_else_block) {
            // Check for error binding in else clause
            const error_binding = getElseErrorBinding(if_stmt.children);
            if (error_binding) |err_name| {
                // Extract error value from error union and bind it
                const err_ptr = try ErrorUnion.getValuePtr(self.func(), result_typed.value);
                const err_value = try self.func().emitLoad(err_ptr.raw(), .i64);

                const err_var_ptr = try self.func().emitAlloca(.i64);
                try self.func().emitStore(err_var_ptr.raw(), err_value);
                try self.func().setValueName(err_var_ptr.raw(), err_name);

                const err_type: types.ValueType = if (result_typed.ty == .error_union_type)
                    self.typeNameToValueType(result_typed.ty.error_union_type.error_enum_type) catch .{ .primitive = .int }
                else
                    .{ .primitive = .int };

                try self.var_map.put(self.allocator, err_name, VarInfo.init(err_var_ptr.raw(), err_type, false, false));
            }

            // Convert else body
            if (getElseBody(if_stmt.children)) |else_body| {
                try self.pushLabelScope();
                for (else_body) |stmt| {
                    try self.convertStatement(stmt);
                }
                self.popLabelScope();
            }

            // Remove error binding from scope if it was added
            if (error_binding) |err_name| {
                _ = self.var_map.remove(err_name);
            }
        }

        // Branch from else block to merge if it doesn't terminate
        const else_terminates = if (self.func().currentBlock()) |block|
            block.instructions.items.len > 0 and block.instructions.items[block.instructions.items.len - 1].op == .ret
        else
            false;
        if (!else_terminates) {
            try ctx.emitBranchToMerge(self);
        }

        // Restore to merge block
        try ctx.restoreBlock(self, 0);
    }

    /// Get error binding from else clause if present (for if-try)
    fn getElseErrorBinding(children: []const ast.ChildBlock) ?[]const u8 {
        for (children) |child| {
            if (child.role == .else_clause) {
                return child.error_binding;
            }
        }
        return null;
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
        try cleanup_helpers.cleanupTemporaryStrings(self);

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
        try cleanup_helpers.freeLoopScopedHeapVars(self, &pre_loop_vars);

        // Emit unconditional branch back to condition block (if not already terminated)
        if (self.func().currentBlock()) |block| {
            const len = block.instructions.items.len;
            if (len == 0 or (block.instructions.items[len - 1].op != .ret and block.instructions.items[len - 1].op != .br and block.instructions.items[len - 1].op != .br_cond)) {
                try self.func().emitBr(cond_block_idx);
            }
        }

        // Remove loop-scoped variables from var_map
        try cleanup_helpers.removeLoopScopedVars(self, &pre_loop_vars);

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
    ///     var __next_result = try collection.next() otherwise break
    ///     var item = __next_result
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
            .struct_type => |struct_info| blk: {
                // Allocate space for the struct on the stack
                const slot = try self.func().emitAllocaSized(@intCast(struct_info.size));
                // Copy the struct data into our slot
                try string_helpers.emitStructCopy(self, slot.asStruct(), ir.toStructPtr(iterable_typed.value), @intCast(struct_info.size), struct_info.name);
                break :blk slot.raw();
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
        // Set in_try_context because the for loop handles the error (breaks on IterationError)
        const was_in_try = self.in_try_context;
        self.in_try_context = true;
        const next_result = try self.convertMethodCallOnTyped(iterator_for_call, "next", &.{});
        self.in_try_context = was_in_try;

        // Check if error union result is success (continue loop) or error (exit loop)
        // Error union layout: offset 0 = tag (0 = success, 1 = error), offset 8 = payload
        const tag = try self.func().emitLoad(next_result.value, .i64);
        const zero = try self.func().emitConstI64(0);
        const is_success = try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64);

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

        // Extract the value from the error union result (offset 8)
        // Error union layout: offset 0 = tag (0 = success, 1 = error), offset 8 = payload
        const value_offset = try self.func().emitConstI64(8);
        const value_ptr = try self.func().emitBinaryOp(.add, next_result.value, value_offset, .ptr);

        // Get element type from error union
        const element_info: struct { wrapped: ir.Type, wrapped_struct_type: ?[]const u8, wrapped_enum_type: ?[]const u8 } = switch (next_result.ty) {
            .error_union_type => |eu_info| .{
                .wrapped = eu_info.success_type,
                .wrapped_struct_type = eu_info.success_struct_type,
                .wrapped_enum_type = eu_info.success_enum_type,
            },
            .primitive, .struct_type, .enum_type, .function_type => .{
                .wrapped = .i64,
                .wrapped_struct_type = null,
                .wrapped_enum_type = null,
            },
        };

        // Register the loop variable
        // For struct types, use the pointer directly (struct data is inline in the error union)
        // For primitive/enum types, load the value and store in a new slot
        const var_slot: ir.Value = if (element_info.wrapped_struct_type != null) blk: {
            // For struct types, the payload is the struct data inline
            break :blk value_ptr;
        } else blk: {
            const element_value = try self.func().emitLoad(value_ptr, element_info.wrapped);
            const slot = try self.func().emitAlloca(element_info.wrapped);
            try self.func().emitStore(slot.raw(), element_value);
            break :blk slot.raw();
        };

        // Determine the var type from the error union element info
        const var_type: types.ValueType = if (element_info.wrapped_struct_type) |wrapped_struct_name|
            try self.typeNameToValueType(wrapped_struct_name)
        else if (element_info.wrapped_enum_type) |wrapped_enum_name|
            try self.typeNameToValueType(wrapped_enum_name)
        else
            .{ .primitive = types.Primitive.fromIrType(element_info.wrapped) };
        try self.var_map.put(self.allocator, for_stmt.var_name, VarInfo.initStackAllocated(var_slot, var_type, false));

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
        try cleanup_helpers.freeLoopScopedHeapVars(self, &pre_loop_vars);

        // Emit unconditional branch back to condition block
        if (self.func().currentBlock()) |block| {
            const len = block.instructions.items.len;
            if (len == 0 or (block.instructions.items[len - 1].op != .ret and block.instructions.items[len - 1].op != .br and block.instructions.items[len - 1].op != .br_cond)) {
                try self.func().emitBr(cond_block_idx);
            }
        }

        // Remove loop-scoped variables from var_map
        try cleanup_helpers.removeLoopScopedVars(self, &pre_loop_vars);

        // Now create continuation block
        const cont_block_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("forcont");

        // Emit conditional branch in condition block: if is_success, go to body; else fall through to cont
        try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_success }, .{ .block_ref = body_block_idx }, .{ .block_ref = cont_block_idx } },
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
                const enum_name = scrutinee_ty.enum_type.name;
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
            .primitive => |s_prim| switch (pattern_ty) {
                .primitive => |p_prim| s_prim == p_prim,
                else => false,
            },
            .enum_type => |s_info| switch (pattern_ty) {
                .enum_type => |p_info| std.mem.eql(u8, s_info.name, p_info.name),
                else => false,
            },
            .struct_type => |s_info| switch (pattern_ty) {
                .struct_type => |p_info| std.mem.eql(u8, s_info.name, p_info.name),
                else => false,
            },
            else => false,
        };
    }

    /// Check if two type names are equivalent (handles type aliases and internal types).
    /// String and __ManagedString are equivalent because String's first field is __ManagedString,
    /// allowing binary-compatible pass-by-value. This is checked via the type_map.
    fn areTypesEquivalent(self: *AstToIr, name1: []const u8, name2: []const u8) bool {
        if (std.mem.eql(u8, name1, name2)) return true;

        // Check if one type contains the other as its first field with offset 0
        // This handles String <-> __ManagedString equivalence
        if (self.type_map.get(name1)) |type_info1| {
            if (type_info1 == .struct_type) {
                const fields = type_info1.struct_type.fields;
                if (fields.len > 0 and fields[0].offset == 0) {
                    if (fields[0].value_type == .primitive) {
                        if (std.mem.eql(u8, fields[0].value_type.primitive.toMaxonName(), name2)) return true;
                    } else if (fields[0].value_type == .struct_type) {
                        if (std.mem.eql(u8, fields[0].value_type.struct_type.name, name2)) return true;
                    }
                }
            }
        }
        if (self.type_map.get(name2)) |type_info2| {
            if (type_info2 == .struct_type) {
                const fields = type_info2.struct_type.fields;
                if (fields.len > 0 and fields[0].offset == 0) {
                    if (fields[0].value_type == .primitive) {
                        if (std.mem.eql(u8, fields[0].value_type.primitive.toMaxonName(), name1)) return true;
                    } else if (fields[0].value_type == .struct_type) {
                        if (std.mem.eql(u8, fields[0].value_type.struct_type.name, name1)) return true;
                    }
                }
            }
        }

        return false;
    }

    /// Check if a type implements the Builtin interface (receives __ManagedArray directly).
    /// This replaces hardcoded checks for String, Array, Character, Map.
    pub fn isBuiltinType(self: *AstToIr, type_name: []const u8) bool {
        // Handle monomorphized names like "Array$int", "Map$String$int"
        const base_name = if (std.mem.indexOf(u8, type_name, "$")) |dollar_pos|
            type_name[0..dollar_pos]
        else
            type_name;
        return self.typeConformsTo(base_name, "Builtin");
    }

    /// Check if an argument type is compatible with a parameter type.
    /// Also handles int→float implicit compatibility for backward compatibility.
    fn checkTypeCompatibility(self: *AstToIr, arg_ty: ValueType, param_ty: ValueType) bool {
        // Handle generic type parameters - if param_ty is a struct_type that's actually
        // a generic parameter name, resolve it to the actual type
        var resolved_param_ty = param_ty;
        if (param_ty == .struct_type) {
            if (self.generic_params.get(param_ty.struct_type.name)) |resolved_name| {
                // Look up the resolved name in the type_map to determine its actual type
                if (self.type_map.getPtr(resolved_name)) |type_info_ptr| {
                    switch (type_info_ptr.*) {
                        .primitive => resolved_param_ty = if (types.Primitive.fromString(resolved_name)) |prim|
                            .{ .primitive = prim }
                        else
                            .{ .struct_type = &type_info_ptr.struct_type },
                        .enum_type => |*e| resolved_param_ty = .{ .enum_type = e },
                        .struct_type => |*s| resolved_param_ty = .{ .struct_type = s },
                    }
                } else {
                    // Not in type_map - assume it's a struct type (e.g., monomorphized generics)
                    // This case shouldn't happen often with pointer-based types
                    return false;
                }
            }
        }

        return switch (arg_ty) {
            .primitive => |arg_prim| switch (resolved_param_ty) {
                .primitive => |param_prim| {
                    if (arg_prim == param_prim) return true;
                    // int→float promotion: int args are compatible with float params
                    if (arg_prim.isIntegral() and param_prim.isFloatingPoint()) return true;
                    return false;
                },
                // cstring can be primitive or struct_type depending on context
                .struct_type => |param_info| self.areTypesEquivalent(arg_prim.toMaxonName(), param_info.name),
                else => false,
            },
            .struct_type => |arg_info| switch (resolved_param_ty) {
                .struct_type => |param_info| self.areTypesEquivalent(arg_info.name, param_info.name),
                // cstring can be primitive or struct_type depending on context
                .primitive => |param_prim| self.areTypesEquivalent(arg_info.name, param_prim.toMaxonName()),
                else => false,
            },
            .enum_type => |arg_info| switch (resolved_param_ty) {
                .enum_type => |param_info| self.areTypesEquivalent(arg_info.name, param_info.name),
                // Handle case where param_ty was marked as struct_type but is actually an enum
                // (this happens with external function signatures extracted from AST before type info is available)
                .struct_type => |param_info| blk: {
                    // Check if param_name is actually an enum type
                    if (self.type_map.get(param_info.name)) |ti| {
                        if (ti == .enum_type) {
                            break :blk self.areTypesEquivalent(arg_info.name, param_info.name);
                        }
                    }
                    break :blk false;
                },
                else => false,
            },
            .error_union_type => switch (resolved_param_ty) {
                .error_union_type => true,
                else => false,
            },
            .function_type => switch (resolved_param_ty) {
                .function_type => true,
                else => false,
            },
        };
    }

    /// Check if a type conversion from source to target is possible.
    /// Used by both implicit conversions and explicit 'as' casts.
    fn canConvert(self: *AstToIr, source_ty: ValueType, target_ty: ValueType) bool {
        if (self.checkTypeCompatibility(source_ty, target_ty)) return true;

        // Numeric conversions: int<->float, int<->byte, float->byte
        if (source_ty == .primitive and target_ty == .primitive) {
            if (source_ty.primitive.isNumeric() and target_ty.primitive.isNumeric()) return true;
        }

        // Stringable -> String (custom types only, NOT primitives)
        if (target_ty == .struct_type and std.mem.eql(u8, target_ty.struct_type.name, "String")) {
            if (source_ty == .struct_type and self.typeConformsTo(source_ty.struct_type.name, "Stringable")) return true;
        }

        return false;
    }

    /// Perform type conversion, returns converted value or original if no conversion needed.
    /// Used by BOTH implicit conversions AND explicit 'as' operator.
    fn convertType(self: *AstToIr, value: ir.Value, source_ty: ValueType, target_ty: ValueType) ConvertError!TypedValue {
        const src_name = source_ty.getTypeName();
        const tgt_name = target_ty.getTypeName();

        // Check for actual type equivalence (same type, no conversion needed)
        if (src_name != null and tgt_name != null) {
            if (std.mem.eql(u8, src_name.?, tgt_name.?)) {
                return .{ .value = value, .ty = target_ty };
            }
        }

        // Get type info for numeric conversions
        const src_info = if (src_name) |n| types.getPrimitiveTypeInfo(n) else null;
        const tgt_info = if (tgt_name) |n| types.getPrimitiveTypeInfo(n) else null;

        // int -> float
        if (src_info != null and tgt_info != null and src_info.?.is_integral and tgt_info.?.is_floating_point) {
            const result = try self.func().emitUnaryOp(.sitofp, value, .f64);
            return .{ .value = result, .ty = .{ .primitive = .float } };
        }

        // float -> int
        if (src_info != null and tgt_info != null and src_info.?.is_floating_point and tgt_info.?.is_integral) {
            if (tgt_name != null and !std.mem.eql(u8, tgt_name.?, types.BYTE)) {
                const result = try self.func().emitUnaryOp(.fptosi, value, .i64);
                return .{ .value = result, .ty = .{ .primitive = .int } };
            }
        }

        // int -> byte (truncate)
        if (src_name != null and tgt_name != null and
            std.mem.eql(u8, src_name.?, types.INT) and std.mem.eql(u8, tgt_name.?, types.BYTE))
        {
            const mask = try self.func().emitConstI64(0xFF);
            const result = try self.func().emitBinaryOp(.band, value, mask, .i64);
            return .{ .value = result, .ty = .{ .primitive = .byte } };
        }

        // float -> byte (truncate to int, then to byte)
        if (src_info != null and tgt_name != null and
            src_info.?.is_floating_point and std.mem.eql(u8, tgt_name.?, types.BYTE))
        {
            const int_val = try self.func().emitUnaryOp(.fptosi, value, .i64);
            const mask = try self.func().emitConstI64(0xFF);
            const result = try self.func().emitBinaryOp(.band, int_val, mask, .i64);
            return .{ .value = result, .ty = .{ .primitive = .byte } };
        }

        // byte -> int (identity, already i64)
        if (src_name != null and tgt_name != null and
            std.mem.eql(u8, src_name.?, types.BYTE) and std.mem.eql(u8, tgt_name.?, types.INT))
        {
            return .{ .value = value, .ty = .{ .primitive = .int } };
        }

        // Stringable -> String (custom types only)
        if (target_ty == .struct_type and std.mem.eql(u8, target_ty.struct_type.name, "String")) {
            if (source_ty == .struct_type and self.typeConformsTo(source_ty.struct_type.name, "Stringable")) {
                const str_val = try self.callStringableToString(source_ty.struct_type.name, value, null);
                return .{ .value = str_val, .ty = target_ty };
            }
        }

        // Check if types are compatible without conversion
        if (self.checkTypeCompatibility(source_ty, target_ty)) {
            return .{ .value = value, .ty = target_ty };
        }

        // No conversion possible
        return error.TypeMismatch;
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
                const payload_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(scrutinee_value), offset);
                const value = try self.func().emitLoad(payload_ptr.raw(), av.ir_type);

                // Create local variable for the binding
                const var_ptr = try self.func().emitAlloca(av.ir_type);
                try self.func().emitStore(var_ptr.raw(), value);

                // Register in var_map with primitive ValueType
                const binding_ty: ValueType = if (types.Primitive.fromString(av.type_name)) |prim|
                    .{ .primitive = prim }
                else
                    .{ .struct_type = av.type_name };
                try self.var_map.put(self.allocator, binding_name, types.VarInfo.initStackAllocated(var_ptr.raw(), binding_ty, false));

                offset += av.ir_type.sizeInBytes();
            }
        }
    }

    /// Set up pattern bindings for match expressions (similar to setupPatternBindings but for MatchExprCase)
    fn setupPatternBindingsExpr(self: *AstToIr, match_case: ast.MatchExprCase, scrutinee_value: ir.Value, scrutinee_ty: ValueType) ConvertError!void {
        const enum_info = scrutinee_ty.enum_type;

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
                const payload_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(scrutinee_value), offset);
                const value = try self.func().emitLoad(payload_ptr.raw(), av.ir_type);

                // Create local variable for the binding
                const var_ptr = try self.func().emitAlloca(av.ir_type);
                try self.func().emitStore(var_ptr.raw(), value);

                // Register in var_map with primitive ValueType
                const binding_ty: ValueType = if (types.Primitive.fromString(av.type_name)) |prim|
                    .{ .primitive = prim }
                else
                    try self.typeNameToValueType(av.type_name);
                try self.var_map.put(self.allocator, binding_name, types.VarInfo.initStackAllocated(var_ptr.raw(), binding_ty, false));

                offset += av.ir_type.sizeInBytes();
            }
        }
    }

    /// Set up pattern bindings from a ChildBlock (unified child block structure)
    fn setupPatternBindingsFromChild(self: *AstToIr, child: ast.ChildBlock, scrutinee_value: ir.Value, scrutinee_ty: ValueType) ConvertError!void {
        const enum_info = scrutinee_ty.enum_type;

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
                const payload_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(scrutinee_value), offset);
                const value = try self.func().emitLoad(payload_ptr.raw(), av.ir_type);

                // Create local variable for the binding
                const var_ptr = try self.func().emitAlloca(av.ir_type);
                try self.func().emitStore(var_ptr.raw(), value);

                // Register in var_map with primitive ValueType
                const binding_ty: ValueType = try self.typeNameToValueType(av.type_name);
                try self.var_map.put(self.allocator, binding_name, types.VarInfo.initStackAllocated(var_ptr.raw(), binding_ty, false));

                offset += av.ir_type.sizeInBytes();
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
            const enum_info = scrutinee_ty.enum_type;
            if (pattern == .identifier) {
                const case_name = pattern.identifier;
                // Try to find this case in the enum
                if (enum_info.members.get(case_name)) |member_value| {
                    return .{
                        .value = try self.func().emitConstI64(member_value),
                        .ty = scrutinee_ty,
                    };
                } else {
                    // Unknown case - report error
                    self.reportError(.E034, case_name);
                    return error.SemanticError;
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
            .primitive => |prim| {
                if (prim.isFloatingPoint()) {
                    return try self.func().emitBinaryOp(.fcmp_eq, scrutinee, pattern.value, .f64);
                } else {
                    return try self.func().emitBinaryOp(.icmp_eq, scrutinee, pattern.value, .i64);
                }
            },
            .enum_type => |enum_info| {
                // For enums with associated values, scrutinee is a pointer - extract the tag first
                if (enum_info.has_associated_values) {
                    // Load tag from offset 0 of the scrutinee pointer
                    const tag = try self.func().emitLoad(scrutinee, .i64);
                    return try self.func().emitBinaryOp(.icmp_eq, tag, pattern.value, .i64);
                }
                // Simple enum - scrutinee is the tag directly
                return try self.func().emitBinaryOp(.icmp_eq, scrutinee, pattern.value, .i64);
            },
            .struct_type => |struct_info| {
                // For struct types that implement Equatable, use the equals method
                if (self.typeConformsTo(struct_info.name, "Equatable")) {
                    const equals_result = try self.emitMethodCallWithIrArgs(struct_info.name, "equals", &.{pattern.value}, scrutinee);
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
        const result_ptr_raw = try self.func().emitAlloca(.i64);
        const result_ptr = result_ptr_raw.raw();

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
        return .{ .value = final_value, .ty = result_ty orelse .{ .primitive = .int } };
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
            .enum_type => |enum_info| enum_info.name,
            .struct_type => |struct_info| {
                // Struct errors are no longer allowed
                self.reportError(.E023, struct_info.name);
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
            const error_ptr = try ErrorUnion.getValuePtr(self.func(), sret);
            try self.func().emitStore(error_ptr.raw(), error_typed.value);

            // Return via sret
            try cleanup_helpers.freeHeapAllocations(self);
            try self.func().emitRet(sret);
        } else {
            // This shouldn't happen for throwing functions, but handle gracefully
            self.reportError(.E001, "throwing function missing sret pointer");
            return error.SemanticError;
        }
    }

    // Expression Conversion
    // ------------------------------------------------------------------------

    pub fn convertExpression(self: *AstToIr, expr: ast.Expression) ConvertError!TypedValue {
        return switch (expr) {
            .integer => |v| .{ .value = try self.func().emitConstI64(v), .ty = .{ .primitive = .int } },
            .float_lit => |v| .{ .value = try self.func().emitConstF64(v), .ty = .{ .primitive = .float } },
            .bool_lit => |v| .{ .value = try self.func().emitConstI64(if (v) 1 else 0), .ty = .{ .primitive = .bool } },
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
            .array_literal => |arr| array_helpers.convertArrayLiteral(self, arr),
            .map_literal => |map| map_helpers.convertMapLiteral(self, map),
            .init_from_array => |ifa| array_helpers.convertInitFromArray(self, ifa),
            .index => |idx| self.convertIndex(idx),
            .array_type => |arr| array_helpers.convertArrayType(self, arr),
            .method_call => |mcall| self.convertMethodCallExpr(mcall),
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
        return .{ .value = self_val, .ty = try self.typeNameToValueType(type_name) };
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
                            const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(self_val), field.offset);

                            // Mark self as used
                            if (self.var_map.getPtr("self")) |info| {
                                info.used = true;
                            }

                            // Struct fields are embedded; others need to be loaded
                            const value = if (field.value_type == .struct_type)
                                field_ptr.raw()
                            else
                                try self.func().emitLoad(field_ptr.raw(), field.value_type.toIrType());

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
                if (self.type_map.getPtr(ret_name)) |type_info_ptr| {
                    switch (type_info_ptr.*) {
                        .struct_type => |*s| ret_ptr.* = .{ .struct_type = s },
                        .enum_type => |*e| ret_ptr.* = .{ .enum_type = e },
                        .primitive => ret_ptr.* = if (types.Primitive.fromString(ret_name)) |prim|
                            .{ .primitive = prim }
                        else
                            .{ .struct_type = &type_info_ptr.struct_type },
                    }
                } else if (types.Primitive.fromString(ret_name)) |prim| {
                    ret_ptr.* = .{ .primitive = prim };
                } else {
                    self.reportError(.E006, ret_name);
                    return error.UnknownType;
                }
                return_type = ret_ptr;
            } else if (func_info.return_type != .void) {
                const ret_ptr = try self.allocator.create(ValueType);
                ret_ptr.* = .{ .primitive = types.Primitive.fromIrType(func_info.return_type) };
                return_type = ret_ptr;
            }

            return .{
                .value = func_ptr.raw(),
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

    /// Convert string literal expression to a string type
    /// This creates a String struct with the literal data
    fn convertStringLiteral(self: *AstToIr, str_bytes: []const u8) ConvertError!TypedValue {
        // Process escape sequences
        const processed = try self.processEscapeSequences(str_bytes);
        defer self.allocator.free(processed);

        // Check if "String" type exists and conforms to InitableFromStringLiteral
        const type_name = "String";

        // If the String type exists, create a managed string and call init
        if (self.type_map.getPtr(type_name)) |type_info_ptr| {
            if (type_info_ptr.* == .struct_type) {
                const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
                try self.module.trackString(init_func_name);

                if (self.func_map.get(init_func_name)) |func_info| {
                    // Trigger lazy generation if needed
                    if (!func_info.ir_generated) {
                        try self.ensureMethodGenerated(init_func_name);
                    }

                    const managed_ptr = try string_helpers.emitManagedArrayFromStaticBytes(self, processed);

                    // Allocate result struct and call init
                    const struct_size = type_info_ptr.struct_type.size;
                    const result_ptr = try self.func().emitAllocaSized(struct_size);

                    var args = try self.func().allocator.alloc(ir.Value, 2);
                    args[0] = result_ptr.raw();
                    args[1] = managed_ptr.raw();

                    _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

                    // Track this as a temporary String that may need cleanup
                    try self.temporary_strings.append(self.allocator, result_ptr.raw());

                    return .{ .value = result_ptr.raw(), .ty = .{ .struct_type = &type_info_ptr.struct_type } };
                }
            }
        }

        // Fallback: store string bytes as a pointer to constant data
        // This is used when String type is not available
        const str_ptr = try self.func().emitStringConstant(processed);
        return .{ .value = str_ptr.raw(), .ty = try self.typeNameToValueType("String") };
    }

    /// Emit a string literal directly into a pre-allocated String pointer.
    /// Unlike convertStringLiteral, this does NOT add to temporary_strings tracking.
    /// Used for control-flow constructs where only one path executes at runtime.
    fn emitStringLiteralIntoPtr(self: *AstToIr, dest_ptr: ir.Value, str_bytes: []const u8) ConvertError!void {
        // Process escape sequences
        const processed = try self.processEscapeSequences(str_bytes);
        defer self.allocator.free(processed);

        // Create managed string from static bytes
        const managed_ptr = try string_helpers.emitManagedArrayFromStaticBytes(self, processed);

        // Call String$init to initialize the destination pointer (Builtin.init is also registered as init)
        const init_func_name = "String$init";
        if (self.func_map.get(init_func_name)) |func_info| {
            // Trigger lazy generation if needed
            if (!func_info.ir_generated) {
                try self.ensureMethodGenerated(init_func_name);
            }

            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = dest_ptr;
            args[1] = managed_ptr.raw();
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
            return .{ .value = string_parts.items[0], .ty = try self.typeNameToValueType("String") };
        }

        // Concatenate all parts: result = concat(concat(a, b), c)...
        var result = string_parts.items[0];
        for (string_parts.items[1..]) |next_val| {
            result = try self.emitStringConcatCall(result, next_val);
        }

        return .{ .value = result, .ty = try self.typeNameToValueType("String") };
    }

    /// Convert a TypedValue to a String pointer
    /// Handles different types: Stringable struct types, primitives (int, float, bool)
    fn convertToString(self: *AstToIr, typed_val: TypedValue, format_spec: ?[]const u8) ConvertError!ir.Value {
        switch (typed_val.ty) {
            .struct_type => |struct_info| {
                // String passthrough optimization - already a String, just return it
                if (string_helpers.isStringType(typed_val.ty)) {
                    return typed_val.value;
                }
                // All other struct types must implement Stringable for string interpolation
                if (self.typeConformsTo(struct_info.name, "Stringable")) {
                    return self.callStringableToString(struct_info.name, typed_val.value, format_spec);
                }
                const msg = std.fmt.allocPrint(self.allocator, "cannot convert type '{s}' to string for interpolation", .{struct_info.name}) catch "cannot convert type to string";
                self.reportInternalError(msg);
                return error.UnknownType;
            },
            .primitive => |prim| {
                // Check bool first since it needs special "true"/"false" output
                if (prim == .bool) {
                    return self.convertBoolToString(typed_val.value);
                }
                if (prim.isFloatingPoint()) {
                    return self.convertFloatToString(typed_val.value);
                } else if (prim.isIntegral()) {
                    return self.convertIntToString(typed_val.value);
                }
                const msg = std.fmt.allocPrint(self.allocator, "cannot convert primitive type '{s}' to string for interpolation", .{prim.toMaxonName()}) catch "cannot convert primitive to string";
                self.reportInternalError(msg);
                return error.UnknownType;
            },
            .enum_type => |enum_info| {
                // Check if string-backed enum
                if (enum_info.backing == .string) {
                    // String-backed enum: generate switch on ordinal to select raw string value
                    return self.convertStringEnumToString(typed_val.value, enum_info.*);
                }
                // Int-backed or simple enum: convert ordinal to string
                return self.convertIntToString(typed_val.value);
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
    /// The enum value is now a pointer to constant string data, so we create a String from it
    fn convertStringEnumToString(self: *AstToIr, enum_value: ir.Value, enum_info: types.EnumTypeInfo) ConvertError!ir.Value {
        _ = enum_info;

        // The enum value is a pointer to null-terminated string data in the data section
        // Create a String in slice mode from this pointer

        // Get string length using inline strlen
        const str_len = try self.func().emitCstrLen(enum_value);

        // Create a ManagedArray with flags=0 (static data, no cleanup needed)
        const managed_ptr = try ManagedArray.alloca(self.func());
        const zero_i32 = try self.func().emitConstI32(0);
        try ManagedArray.init(self.func(), managed_ptr, ir.toRawPtr(enum_value), str_len, str_len, zero_i32);

        // Wrap in a String struct
        const string_ptr = try string_helpers.emitStringFromManaged(self, managed_ptr);
        return string_ptr.raw();
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

        // Allocate space for the return value
        const result_ptr = try string_helpers.emitStringAlloca(self);

        // Prepare the format argument as a String (empty string if no format spec)
        const format_str = if (format_spec) |spec|
            try self.convertStringLiteral(spec)
        else
            try self.convertStringLiteral("");

        // Call toString(self, format) -> result_ptr contains the String
        var args = try self.func().allocator.alloc(ir.Value, 3);
        args[0] = result_ptr.raw(); // Return slot
        args[1] = value_ptr; // self
        args[2] = format_str.value; // format argument (String)

        _ = try self.func().emitCall(method_name, args, func_info.return_type);

        return result_ptr.raw();
    }

    /// Convert a Character to a String
    /// Character layout: { _managed: __ManagedArray } (32 bytes)
    /// String layout: { _managed: __ManagedArray, _iterPos: int } (40 bytes)
    fn convertCharacterToString(self: *AstToIr, char_ptr: ir.Value) ConvertError!ir.Value {
        // Allocate a String struct
        const string_ptr = try string_helpers.emitStringAlloca(self);

        // Copy the _managed field from Character to String (32 bytes at offset 0)
        const char_managed = try self.func().emitGetFieldPtr(ir.toStructPtr(char_ptr), 0);
        const string_managed = try self.func().emitGetFieldPtr(ir.toStructPtr(string_ptr.raw()), 0);
        try string_helpers.emitStructCopy(self, string_managed, char_managed, 32, "__ManagedArray");

        // Set _iterPos to 0 (at offset 32)
        try String.initIterPos(self.func(), string_ptr.raw());

        return string_ptr.raw();
    }

    /// Emit a call to concatenate two String values
    fn emitStringConcatCall(self: *AstToIr, a: ir.Value, b: ir.Value) ConvertError!ir.Value {
        // Get the _managed field from both String structs (offset 0)
        const a_managed = try self.func().emitGetFieldPtr(ir.toStructPtr(a), 0);
        const b_managed = try self.func().emitGetFieldPtr(ir.toStructPtr(b), 0);

        // Get lengths from ManagedArray (already i64)
        const a_len = try ManagedArray.loadLen(self.func(), ir.toManagedArrayPtr(a_managed.raw()));
        const b_len = try ManagedArray.loadLen(self.func(), ir.toManagedArrayPtr(b_managed.raw()));
        const total_len = try self.func().emitBinaryOp(.add, a_len, b_len, .i64);

        // Allocate new buffer with header: total_len + 1 for null terminator
        const one = try self.func().emitConstI64(1);
        const data_size = try self.func().emitBinaryOp(.add, total_len, one, .i64);
        const new_buffer = try array_helpers.emitAllocRefcountedBuffer(self, data_size, "string concat");

        // Copy first string
        const a_buf_ptr = try self.func().emitGetFieldPtr(a_managed, 0);
        const a_buf = try self.func().emitLoad(a_buf_ptr.raw(), .ptr);
        try self.func().emitMemcpyDynamic(new_buffer, ir.toRawPtr(a_buf), a_len);

        // Copy second string at offset a_len
        const b_buf_ptr = try self.func().emitGetFieldPtr(b_managed, 0);
        const b_buf = try self.func().emitLoad(b_buf_ptr.raw(), .ptr);
        const offset_ptr = try self.func().emitGetElemPtr(new_buffer, a_len, 1);
        try self.func().emitMemcpyDynamic(offset_ptr.asRawPtr(), ir.toRawPtr(b_buf), b_len);

        // Null terminate
        const null_offset = try self.func().emitGetElemPtr(new_buffer, total_len, 1);
        try self.func().emitStoreI8(null_offset.raw(), try self.func().emitConstI8(0));

        const result_managed = try string_helpers.emitManagedArrayFromBuffer(self, new_buffer, total_len);
        const string_ptr = try string_helpers.emitStringFromManaged(self, result_managed);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr.raw());
        return string_ptr.raw();
    }

    /// Emit code to convert an int to a String
    fn emitIntToStringCall(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        // Allocate buffer with header (22 bytes max: sign + 20 digits + null)
        const buffer_size = try self.func().emitConstI64(22);
        const buffer = try array_helpers.emitAllocRefcountedBuffer(self, buffer_size, "int.toString");

        // Call __runtime_int_to_string(buffer, value) -> returns length
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = buffer.raw();
        args[1] = value;
        const len = (try self.func().emitCall("__runtime_int_to_string", args, .i64)).?;

        const managed_ptr = try string_helpers.emitManagedArrayFromBuffer(self, buffer, len);
        const string_ptr = try string_helpers.emitStringFromManaged(self, managed_ptr);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr.raw());
        return string_ptr.raw();
    }

    /// Emit code to convert a float to a String
    /// For now, keep calling the runtime function since the inline version is complex
    fn emitFloatToStringCall(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        // Allocate buffer with header (32 bytes should be enough for most floats)
        const buffer_size = try self.func().emitConstI64(32);
        const buffer = try array_helpers.emitAllocRefcountedBuffer(self, buffer_size, "float.toString");

        // Call __runtime_float_to_string(buffer, value) -> returns length
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = buffer.raw();
        args[1] = value;
        const len = (try self.func().emitCall("__runtime_float_to_string", args, .i64)).?;

        const managed_ptr = try string_helpers.emitManagedArrayFromBuffer(self, buffer, len);
        const string_ptr = try string_helpers.emitStringFromManaged(self, managed_ptr);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr.raw());
        return string_ptr.raw();
    }

    /// Emit code to convert a bool to a String
    fn emitBoolToStringCall(self: *AstToIr, value: ir.Value) ConvertError!ir.Value {
        // Create a buffer with header (6 bytes max: "false\0")
        const buffer_size = try self.func().emitConstI64(6);
        const buffer = try array_helpers.emitAllocRefcountedBuffer(self, buffer_size, "bool.toString");

        // Call __runtime_bool_to_string(buffer, value) -> returns length
        var args = try self.allocator.alloc(ir.Value, 2);
        args[0] = buffer.raw();
        args[1] = value;
        const len = (try self.func().emitCall("__runtime_bool_to_string", args, .i64)).?;

        const managed_ptr = try string_helpers.emitManagedArrayFromBuffer(self, buffer, len);
        const string_ptr = try string_helpers.emitStringFromManaged(self, managed_ptr);
        // Track as temporary for cleanup after expression evaluation
        try self.temporary_strings.append(self.allocator, string_ptr.raw());
        return string_ptr.raw();
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
        if (self.type_map.getPtr(type_name)) |type_info_ptr| {
            if (type_info_ptr.* == .struct_type) {
                const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
                try self.module.trackString(init_func_name);

                if (self.func_map.get(init_func_name)) |func_info| {
                    // Trigger lazy generation if needed
                    if (!func_info.ir_generated) {
                        try self.ensureMethodGenerated(init_func_name);
                    }

                    const managed_ptr = try string_helpers.emitManagedArrayFromBytes(self, processed_bytes);

                    // Allocate result struct and call init
                    const struct_size = type_info_ptr.struct_type.size;
                    const result_ptr = try self.func().emitAllocaSized(struct_size);

                    var args = try self.func().allocator.alloc(ir.Value, 2);
                    args[0] = result_ptr.raw();
                    args[1] = managed_ptr.raw();

                    _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

                    return .{ .value = result_ptr.raw(), .ty = .{ .struct_type = &type_info_ptr.struct_type } };
                }
            }
        }

        // Fallback: store char bytes as constant data pointer
        const char_ptr = try self.func().emitStringConstant(processed_bytes);
        return .{ .value = char_ptr.raw(), .ty = try self.typeNameToValueType("Character") };
    }

    fn convertIdentifier(self: *AstToIr, name: []const u8) ConvertError!TypedValue {
        const info = self.var_map.getPtr(name) orelse {
            self.reportError(.E005, name);
            return error.UndefinedVariable;
        };
        if (std.mem.eql(u8, name, "value")) {}

        if (info.state == .moved) {
            debug.astToIr("variable '{s}' was moved\n", .{name});
            self.reportError(.E008, name);
            return error.UseAfterMove;
        }

        info.used = true;

        // Reference types may use heap allocation (pointer indirection); value types always load
        const value = if (info.ty == .struct_type)
            if (info.is_heap_allocated) try self.func().emitLoad(info.ptr.?, .ptr) else info.ptr.?
        else
            try self.func().emitLoad(info.ptr.?, info.ty.toIrType());

        return .{ .value = value, .ty = info.ty };
    }

    fn convertUnary(self: *AstToIr, un: ast.UnaryExpr) ConvertError!TypedValue {
        const operand = try self.convertExpression(un.operand.*);
        const is_float = operand.ty.toIrType() == .f64;

        switch (un.op) {
            .negate => {
                // Negate: -x
                const zero = if (is_float) try self.func().emitConstF64(0.0) else try self.func().emitConstI64(0);
                const op: ir.Instruction.Op = if (is_float) .fsub else .sub;
                const ty: ir.Type = if (is_float) .f64 else .i64;
                const result = try self.func().emitBinaryOp(op, zero, operand.value, ty);
                // Preserve the operand's type for negate
                return .{ .value = result, .ty = if (operand.ty == .primitive)
                    .{ .primitive = operand.ty.primitive }
                else
                    .{ .primitive = types.Primitive.fromIrType(ty) } };
            },
            .not => {
                // Logical not: x == 0
                const zero = try self.func().emitConstI64(0);
                const result = try self.func().emitBinaryOp(.icmp_eq, operand.value, zero, .i64);
                return .{ .value = result, .ty = .{ .primitive = .bool } };
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

        // Determine target type
        if (!types.isPrimitiveTypeName(target_type_name)) {
            // Unknown cast target type
            debug.astToIr("Unknown cast target type: {s}\n", .{target_type_name});
            self.reportError(.E006, target_type_name);
            return error.TypeMismatch;
        }
        const target_ty: ValueType = if (types.Primitive.fromString(target_type_name)) |prim|
            .{ .primitive = prim }
        else
            try self.typeNameToValueType(target_type_name);

        // Use unified conversion system
        return self.convertType(source.value, source.ty, target_ty);
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
            func_param_types[i] = .{ .ty = if (types.Primitive.fromString(param.type_name)) |prim|
                .{ .primitive = prim }
            else
                try self.typeNameToValueType(param.type_name) };
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
            try func_ir.emitStore(param_ptr.raw(), param_val);
            try self.var_map.put(self.allocator, param.name, VarInfo.initParam(
                param_ptr.raw(),
                if (types.Primitive.fromString(param.type_name)) |prim|
                    .{ .primitive = prim }
                else
                    try self.typeNameToValueType(param.type_name),
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
            .return_type = body_result.ty.toIrType(),
            .return_type_name = body_result.ty.getTypeName(),
            .return_value_type = body_result.ty,
            .param_types = func_param_types,
        });

        // Free the IR types array since it's no longer needed
        self.allocator.free(param_ir_types);

        // Build the function type for the closure
        const closure_param_types = try self.allocator.alloc(ValueType, clos.params.len);
        for (clos.params, 0..) |param, i| {
            closure_param_types[i] = if (types.Primitive.fromString(param.type_name)) |prim|
                .{ .primitive = prim }
            else
                try self.typeNameToValueType(param.type_name);
        }

        // Build return type pointer
        const ret_ptr = try self.allocator.create(ValueType);
        ret_ptr.* = body_result.ty;

        // Return the function pointer - emit func.addr to get address of the closure function
        const func_ptr = try self.func().emitFuncAddr(anon_name);
        return .{
            .value = func_ptr.raw(),
            .ty = .{ .function_type = .{
                .param_types = closure_param_types,
                .return_type = ret_ptr,
                .return_ir_type = body_result.ty.toIrType(),
            } },
        };
    }

    fn convertBinary(self: *AstToIr, bin: ast.BinaryExpr) ConvertError!TypedValue {
        const left = try self.convertExpression(bin.left.*);
        const right = try self.convertExpression(bin.right.*);

        const left_prim = left.ty.toIrType();
        const right_prim = right.ty.toIrType();

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

        // Determine the result type based on IR type
        return .{ .value = result, .ty = .{ .primitive = types.Primitive.fromIrType(result_ty) } };
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

        // Check if left operand is a struct type that implements Equatable
        // If so, use the equals() method for == and != operators
        if (left.ty == .struct_type) {
            const type_name = left.ty.struct_type.name;
            if (self.typeConformsTo(type_name, "Equatable")) {
                // Only handle == and != for Equatable
                if (cmp.op == .eq or cmp.op == .ne) {
                    // Call the equals() method: left.equals(right)
                    const equals_result = try self.emitMethodCallWithIrArgs(type_name, "equals", &.{right.value}, left.value);

                    // For ==, return the result directly; for !=, negate it
                    if (cmp.op == .eq) {
                        return .{ .value = equals_result.value, .ty = .{ .primitive = .bool } };
                    } else {
                        // != is the negation of equals
                        const zero = try self.func().emitConstI64(0);
                        const negated = try self.func().emitBinaryOp(.icmp_eq, equals_result.value, zero, .i64);
                        return .{ .value = negated, .ty = .{ .primitive = .bool } };
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
                    return .{ .value = result, .ty = .{ .primitive = .bool } };
                }
            }
        }

        // Type checking for comparison operands - disallow int/float mixing
        const left_prim = if (left.ty == .primitive) left.ty.primitive else null;
        const right_prim = if (right.ty == .primitive) right.ty.primitive else null;

        if (left_prim != null and right_prim != null) {
            const left_is_int = left_prim.? == .int;
            const left_is_float = left_prim.? == .float;
            const left_is_byte = left_prim.? == .byte;
            const right_is_int = right_prim.? == .int;
            const right_is_float = right_prim.? == .float;
            const right_is_byte = right_prim.? == .byte;

            // Check for int/float mismatch (either direction)
            if ((left_is_int and right_is_float) or (left_is_float and right_is_int)) {
                self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_prim.?.toMaxonName(), right_prim.?.toMaxonName() }) catch "type mismatch in comparison");
                return error.TypeMismatch;
            }

            // Check for byte vs int literal - allow if literal is in range 0-255
            if (left_is_byte and right_is_int) {
                // Right is int - check if it's a literal in range
                if (cmp.right.* == .integer) {
                    const lit_value = cmp.right.integer;
                    if (lit_value < 0 or lit_value > 255) {
                        self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_prim.?.toMaxonName(), right_prim.?.toMaxonName() }) catch "type mismatch in comparison");
                        return error.TypeMismatch;
                    }
                    // Literal is in range - allow the comparison
                } else {
                    // Right is an int variable, not a literal - error
                    self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_prim.?.toMaxonName(), right_prim.?.toMaxonName() }) catch "type mismatch in comparison");
                    return error.TypeMismatch;
                }
            } else if (right_is_byte and left_is_int) {
                // Left is int - check if it's a literal in range
                if (cmp.left.* == .integer) {
                    const lit_value = cmp.left.integer;
                    if (lit_value < 0 or lit_value > 255) {
                        self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_prim.?.toMaxonName(), right_prim.?.toMaxonName() }) catch "type mismatch in comparison");
                        return error.TypeMismatch;
                    }
                    // Literal is in range - allow the comparison
                } else {
                    // Left is an int variable, not a literal - error
                    self.reportError(.E022, std.fmt.allocPrint(self.allocator, "cannot compare {s} with {s}", .{ left_prim.?.toMaxonName(), right_prim.?.toMaxonName() }) catch "type mismatch in comparison");
                    return error.TypeMismatch;
                }
            }
        }

        const left_ir_prim = left.ty.toIrType();
        const right_ir_prim = right.ty.toIrType();

        // If either operand is float, use float comparison (only reached for float vs float)
        if (left_ir_prim == .f64 or right_ir_prim == .f64) {
            // Both must be float at this point (int/float mismatch already caught above)
            const left_val = if (left_ir_prim == .i64)
                try self.func().emitUnaryOp(.sitofp, left.value, .f64)
            else
                left.value;
            const right_val = if (right_ir_prim == .i64)
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
            return .{ .value = result, .ty = .{ .primitive = .bool } };
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
        return .{ .value = result, .ty = .{ .primitive = .bool } };
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
                return .{ .value = result, .ty = .{ .primitive = .bool } };
            },
            .@"or" => {
                // Logical 'or' - at least one value must be non-zero (truthy)
                // We implement this as: (left != 0) | (right != 0)
                // Using bitwise OR: if either is 1, result is 1
                const zero = try self.func().emitConstI64(0);
                const left_bool = try self.func().emitBinaryOp(.icmp_ne, left.value, zero, .i64);
                const right_bool = try self.func().emitBinaryOp(.icmp_ne, right.value, zero, .i64);
                const result = try self.func().emitBinaryOp(.bitor, left_bool, right_bool, .i64);
                return .{ .value = result, .ty = .{ .primitive = .bool } };
            },
        }
    }

    /// Convert try expression: `try expr` or `try expr otherwise ...`
    fn convertTryExpr(self: *AstToIr, try_e: ast.TryExpr) ConvertError!TypedValue {
        // The inner expression must be a call to a throwing function
        // For now, we only support function calls
        const inner_expr = try_e.expr.*;

        // Mark that we're in a try context so throwing calls are allowed
        const was_in_try = self.in_try_context;
        self.in_try_context = true;
        defer self.in_try_context = was_in_try;

        // Evaluate the inner expression (this calls the throwing function)
        const result_typed = try self.convertExpression(inner_expr);

        // Validate that the expression is a throwing function (returns error union)
        if (result_typed.ty != .error_union_type) {
            self.reportError(.E055, "expression does not throw");
            return error.SemanticError;
        }

        // The result should be an error union (from a throwing function via sret)
        // Check the tag at offset 0: 0 = success, 1 = error

        // Load the tag
        const tag = try self.func().emitLoad(result_typed.value, .i64);
        const zero = try self.func().emitConstI64(0);
        const is_success = try self.func().emitBinaryOp(.icmp_eq, tag, zero, .i64);

        // Check if we have an otherwise clause
        if (try_e.otherwise) |otherwise| {
            // Handle different otherwise modes
            return switch (otherwise.mode) {
                .ignore => self.convertTryOtherwiseIgnore(result_typed, is_success),
                .default_expr => self.convertTryOtherwiseDefault(result_typed, is_success, otherwise.default_expr.?.*),
                .block, .block_with_err => self.convertTryOtherwiseBlock(result_typed, is_success, otherwise),
            };
        }

        // No otherwise clause - propagate error to caller
        // Verify we're in a valid context for try without otherwise
        if (self.current_func_throws_type == null) {
            self.reportError(.E022, "try without otherwise requires enclosing function to throw");
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
                break :blk self.typeNameToValueType(struct_name) catch .{ .primitive = .int };
            } else if (eu_info.success_enum_type) |enum_name| {
                break :blk self.typeNameToValueType(enum_name) catch .{ .primitive = .int };
            } else if (eu_info.success_primitive_type) |prim| {
                break :blk .{ .primitive = prim };
            } else {
                // Fallback - infer from IR type
                break :blk .{ .primitive = types.Primitive.fromIrType(eu_info.success_type) };
            }
        } else .{ .primitive = .int };

        // Success block: extract value and store for later use
        const value_ptr = try ErrorUnion.getValuePtr(self.func(), result_typed.value);
        const is_struct_type = success_type == .struct_type;

        // For struct types, we return the pointer directly (data is embedded in error union)
        // For primitives, we load the value and store in a result alloca
        const result_ptr = if (is_struct_type)
            (try self.func().emitAlloca(.ptr)).raw()
        else
            (try self.func().emitAlloca(.i64)).raw();

        if (is_struct_type) {
            // Store pointer to embedded struct data
            try self.func().emitStore(result_ptr, value_ptr.raw());
        } else {
            // Load primitive value
            const success_value = try self.func().emitLoad(value_ptr.raw(), .i64);
            try self.func().emitStore(result_ptr, success_value);
        }

        try self.func().currentBlock().?.instructions.append(self.allocator, .{
            .op = .br,
            .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
            .result = null,
        });

        // Error block: propagate error to function's sret and return
        try deferred.restore(self, 1);

        if (self.sret_ptr) |sret| {
            // Copy the entire error union (tag + payload)
            try self.func().emitMemcpy(ir.toRawPtr(sret), ir.toRawPtr(result_typed.value), self.sret_size);
            try self.func().emitRet(sret);
        } else {
            self.reportError(.E001, "try requires sret for error propagation");
            return error.SemanticError;
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

    /// Common context for try-otherwise handling, encapsulating shared setup and state
    const TryOtherwiseContext = struct {
        success_type: types.ValueType,
        is_struct_type: bool,
        struct_size: i32,
        result_ptr: ir.Value,
        merge_block_idx: u32, // Only valid when branch_to_merge=true in init
        deferred: DeferredBlocks,
        restores_done: u32 = 0, // Track how many restore() calls have been made
        // Fields for deferred branch emission (when branch_to_merge=false)
        entry_block_idx: u32 = 0,
        success_block_idx: u32 = 0,
        is_success_condition: ir.Value = 0,
        needs_initial_branch: bool = false,
        // Field for deferred success-to-merge branch
        needs_success_to_merge_branch: bool = false,

        /// Initialize context for try-otherwise handling.
        /// If branch_to_merge is true (default for otherwise), branches to merge after extracting success value.
        /// If false (for if-try), stays in success block to allow additional code before branching.
        fn init(self_ir: *AstToIr, result_typed: TypedValue, is_success: ir.Value, error_block_name: []const u8) ConvertError!TryOtherwiseContext {
            return initWithOptions(self_ir, result_typed, is_success, error_block_name, true);
        }

        fn initWithOptions(self_ir: *AstToIr, result_typed: TypedValue, is_success: ir.Value, error_block_name: []const u8, branch_to_merge: bool) ConvertError!TryOtherwiseContext {
            // Extract success type from error union
            const success_type: types.ValueType = if (result_typed.ty == .error_union_type) blk: {
                const eu_info = result_typed.ty.error_union_type;
                if (eu_info.success_struct_type) |struct_name| {
                    break :blk self_ir.typeNameToValueType(struct_name) catch .{ .primitive = .int };
                } else if (eu_info.success_enum_type) |enum_name| {
                    break :blk self_ir.typeNameToValueType(enum_name) catch .{ .primitive = .int };
                } else if (eu_info.success_primitive_type) |prim| {
                    break :blk .{ .primitive = prim };
                } else {
                    break :blk .{ .primitive = types.Primitive.fromIrType(eu_info.success_type) };
                }
            } else .{ .primitive = .int };

            const is_struct_type = success_type == .struct_type;

            // Use helper to get struct size with automatic monomorphization
            const struct_size: i32 = if (is_struct_type) blk: {
                break :blk self_ir.getStructSizeWithMonomorphization(success_type.struct_type.name) orelse {
                    self_ir.reportInternalError("unknown struct type size in try-otherwise");
                    return error.SemanticError;
                };
            } else 0;

            // Allocate result buffer BEFORE creating new blocks (in current/entry block)
            // so it's visible to both success and error paths
            const result_ptr = if (is_struct_type)
                (try self_ir.func().emitAllocaSized(struct_size)).raw()
            else
                (try self_ir.func().emitAlloca(.i64)).raw();

            // Create blocks
            const success_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
            _ = try self_ir.func().addBlock("try_success");
            const error_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
            _ = try self_ir.func().addBlock(error_block_name);
            const merge_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
            _ = try self_ir.func().addBlock("try_merge");

            const entry_block_idx = success_block_idx - 1;

            // For branch_to_merge=true, emit branch now with static indices (they won't change)
            // For branch_to_merge=false (if-try), defer branch emission until restoreBlock is called
            // because nested if-try may create blocks that shift the indices
            if (branch_to_merge) {
                const entry_block = &self_ir.func().blocks.items[entry_block_idx];
                try entry_block.instructions.append(self_ir.allocator, .{
                    .op = .br_cond,
                    .operands = .{ .{ .value = is_success }, .{ .block_ref = success_block_idx }, .{ .block_ref = error_block_idx } },
                });
            }

            // Defer merge and error blocks
            var deferred = try DeferredBlocks.init(self_ir.allocator, 2);
            deferred.deferBlocks(self_ir, 2);

            // Emit success block: extract value from error union
            const value_ptr = try ErrorUnion.getValuePtr(self_ir.func(), result_typed.value);

            if (is_struct_type) {
                try self_ir.func().emitMemcpy(ir.toRawPtr(result_ptr), value_ptr.asRawPtr(), struct_size);
                // Note: We do NOT incref here because we're moving ownership from the
                // error union buffer to result_ptr. The error union is a stack allocation
                // that gets abandoned - there's no explicit cleanup of its String value.
                // By not increfing, the refcount stays correct for the single remaining reference.
            } else {
                const success_value = try self_ir.func().emitLoad(value_ptr.raw(), .i64);
                try self_ir.func().emitStore(result_ptr, success_value);
            }

            // For try-otherwise, we need to branch to merge but we can't emit it yet
            // because the error block code may add more blocks (like String cleanup).
            // We defer the branch emission to finalize() when we know the actual merge index.
            // For if-try, stay in success block to run the if-body first
            var restores_done: u32 = 0;
            var needs_success_to_merge_branch = false;
            if (branch_to_merge) {
                // Don't emit the branch here - defer it to finalize()
                needs_success_to_merge_branch = true;

                // Switch to error block
                try deferred.restore(self_ir, 1);
                restores_done = 1; // Track that error block has been restored
            }

            return .{
                .success_type = success_type,
                .is_struct_type = is_struct_type,
                .struct_size = struct_size,
                .result_ptr = result_ptr,
                .merge_block_idx = merge_block_idx,
                .deferred = deferred,
                .entry_block_idx = entry_block_idx,
                .success_block_idx = success_block_idx,
                .is_success_condition = is_success,
                .needs_initial_branch = !branch_to_merge,
                .restores_done = restores_done,
                .needs_success_to_merge_branch = needs_success_to_merge_branch,
            };
        }

        fn deinit(self: *TryOtherwiseContext) void {
            self.deferred.deinit();
        }

        /// Restore deferred block and track the restore count.
        /// For the first restore (error block), also emits the deferred initial branch if needed.
        fn restoreBlock(self: *TryOtherwiseContext, self_ir: *AstToIr, n: usize) !void {
            // When restoring error block (n=1), emit the deferred initial branch with correct indices
            if (n == 1 and self.needs_initial_branch) {
                // Now we know where error block will be: current block count
                const error_block_idx: u32 = @intCast(self_ir.func().blocks.items.len);
                const entry_block = &self_ir.func().blocks.items[self.entry_block_idx];
                try entry_block.instructions.append(self_ir.allocator, .{
                    .op = .br_cond,
                    .operands = .{ .{ .value = self.is_success_condition }, .{ .block_ref = self.success_block_idx }, .{ .block_ref = error_block_idx } },
                });
                self.needs_initial_branch = false;
            }
            try self.deferred.restore(self_ir, n);
            self.restores_done += 1;
        }

        fn emitBranchToMerge(self: *const TryOtherwiseContext, self_ir: *AstToIr) !void {
            // Compute merge block index dynamically.
            // Merge block (deferred[0]) is restored last (after error block which is deferred[1]).
            // The index depends on how many deferred blocks are still pending:
            // - If 0 restores done (error not yet restored): merge will be at current_len + 1
            // - If 1 restore done (error restored): merge will be at current_len
            const pending_restores = 2 - self.restores_done;
            const merge_idx: u32 = @intCast(self_ir.func().blocks.items.len + pending_restores - 1);
            try self_ir.func().currentBlock().?.instructions.append(self_ir.allocator, .{
                .op = .br,
                .operands = .{ .{ .block_ref = merge_idx }, .none, .none },
                .result = null,
            });
        }

        fn finalize(self: *TryOtherwiseContext, self_ir: *AstToIr) ConvertError!TypedValue {
            // If we deferred the success-to-merge branch, emit it now with the correct merge index.
            // The merge block will be at the current length (after we restore it).
            if (self.needs_success_to_merge_branch) {
                const merge_idx: u32 = @intCast(self_ir.func().blocks.items.len);
                const success_block = &self_ir.func().blocks.items[self.success_block_idx];
                try success_block.instructions.append(self_ir.allocator, .{
                    .op = .br,
                    .operands = .{ .{ .block_ref = merge_idx }, .none, .none },
                    .result = null,
                });
            }

            try self.deferred.restore(self_ir, 0);
            const final_value = if (self.is_struct_type)
                self.result_ptr
            else
                try self_ir.func().emitLoad(self.result_ptr, .i64);

            return .{ .value = final_value, .ty = self.success_type };
        }
    };

    /// Handle `try expr otherwise ignore` - discard error, return default zero value
    fn convertTryOtherwiseIgnore(self: *AstToIr, result_typed: TypedValue, is_success: ir.Value) ConvertError!TypedValue {
        var ctx = try TryOtherwiseContext.init(self, result_typed, is_success, "try_ignore");
        defer ctx.deinit();

        // Error block: zero-initialize result buffer
        if (ctx.is_struct_type) {
            try self.func().emitMemset(ir.toRawPtr(ctx.result_ptr), 0, ctx.struct_size);
        } else {
            const default_val = try self.func().emitConstI64(0);
            try self.func().emitStore(ctx.result_ptr, default_val);
        }

        try ctx.emitBranchToMerge(self);
        return ctx.finalize(self);
    }

    /// Handle `try expr otherwise defaultExpr` - on error, evaluate and return default expression
    fn convertTryOtherwiseDefault(self: *AstToIr, result_typed: TypedValue, is_success: ir.Value, default_expr: ast.Expression) ConvertError!TypedValue {
        var ctx = try TryOtherwiseContext.init(self, result_typed, is_success, "try_default");
        defer ctx.deinit();

        // Error block: evaluate default expression and copy to result
        var default_typed = try self.convertExpression(default_expr);

        // Type check: use unified canConvert for compatibility
        const types_compatible = self.checkTypeCompatibility(default_typed.ty, ctx.success_type) or
            self.canConvert(default_typed.ty, ctx.success_type);
        if (!types_compatible) {
            const expected_name = ctx.success_type.getTypeName() orelse "unknown";
            const actual_name = default_typed.ty.getTypeName() orelse "unknown";
            const msg = std.fmt.allocPrint(self.allocator, "otherwise type '{s}' does not match expected type '{s}'", .{ actual_name, expected_name }) catch "type mismatch in otherwise";
            self.reportError(.E022, msg);
            return error.SemanticError;
        }

        // Apply implicit conversion if needed
        if (!self.checkTypeCompatibility(default_typed.ty, ctx.success_type)) {
            default_typed = try self.convertType(default_typed.value, default_typed.ty, ctx.success_type);
        }

        if (ctx.is_struct_type) {
            try self.func().emitMemcpy(ir.toRawPtr(ctx.result_ptr), ir.toRawPtr(default_typed.value), ctx.struct_size);
            // For String defaults that are NOT temporaries (e.g., variables, parameters),
            // we need to incref since we're creating a new reference to the same buffer.
            // If the default is a variable/parameter, we're borrowing its data.
            if (string_helpers.isStringType(ctx.success_type)) {
                if (default_expr == .identifier) {
                    // Default is a variable/parameter - need to incref the copy
                    try string_helpers.emitStringIncref(self, ir.toStringPtr(ctx.result_ptr), "<array index String>");
                } else {
                    // Default is a temporary - transfer ownership, remove from temporaries
                    cleanup_helpers.removeFromTemporaries(self, default_typed.value);
                }
            }
        } else {
            try self.func().emitStore(ctx.result_ptr, default_typed.value);
        }

        try ctx.emitBranchToMerge(self);
        return ctx.finalize(self);
    }

    /// Handle `try expr otherwise 'label' ... end 'label'` or `try expr otherwise (err) 'label' ... end 'label'`
    fn convertTryOtherwiseBlock(self: *AstToIr, result_typed: TypedValue, is_success: ir.Value, otherwise: *const ast.OtherwiseClause) ConvertError!TypedValue {
        var ctx = try TryOtherwiseContext.init(self, result_typed, is_success, "try_otherwise_block");
        defer ctx.deinit();

        // If block_with_err mode, bind the error value
        if (otherwise.mode == .block_with_err) {
            if (otherwise.error_binding) |err_name| {
                const err_ptr = try ErrorUnion.getValuePtr(self.func(), result_typed.value);
                const err_value = try self.func().emitLoad(err_ptr.raw(), .i64);

                const err_var_ptr = try self.func().emitAlloca(.i64);
                try self.func().emitStore(err_var_ptr.raw(), err_value);
                try self.func().setValueName(err_var_ptr.raw(), err_name);

                const err_type: types.ValueType = if (result_typed.ty == .error_union_type)
                    self.typeNameToValueType(result_typed.ty.error_union_type.error_enum_type) catch .{ .primitive = .int }
                else
                    .{ .primitive = .int };

                try self.var_map.put(self.allocator, err_name, VarInfo.init(err_var_ptr.raw(), err_type, false, false));
            }
        }

        // Execute the otherwise block body
        try self.pushLabelScope();
        for (otherwise.body) |stmt| {
            try self.convertStatement(stmt);
        }
        self.popLabelScope();

        // Branch to merge if no terminator
        if (self.func().currentBlock()) |current_block| {
            const has_terminator = current_block.instructions.items.len > 0 and blk: {
                const last_op = current_block.instructions.items[current_block.instructions.items.len - 1].op;
                break :blk last_op == .ret or last_op == .br or last_op == .br_cond;
            };
            if (!has_terminator) {
                try ctx.emitBranchToMerge(self);
            }
        }

        return ctx.finalize(self);
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
        try self.initStructIntoResolved(sinit, struct_ptr.raw(), type_name);
        return .{ .value = struct_ptr.raw(), .ty = try self.typeNameToValueType(type_name) };
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
        try self.func().emitMemset(ir.toRawPtr(dest_ptr), 0, struct_info.size);

        // Build a set of field names provided in the struct init
        var provided_fields = std.StringHashMap(void).init(self.allocator);
        defer provided_fields.deinit();

        // Collect pending ownership transfers - defer until all field expressions are evaluated
        // so that a variable can be used multiple times within the same struct literal
        const PendingTransfer = struct { expr: ast.Expression, dest_is_mutable: bool };
        var pending_transfers: [64]PendingTransfer = undefined;
        var pending_count: usize = 0;

        // Initialize fields from struct init expression
        for (sinit.fields) |field_init| {
            try provided_fields.put(field_init.name, {});
            const field_info = try self.lookupField(struct_info, field_init.name);
            const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(dest_ptr), field_info.offset);
            const field_val = try self.convertExpression(field_init.value.*);

            // Collect ownership transfer to apply after all fields are evaluated
            if (pending_count < pending_transfers.len) {
                pending_transfers[pending_count] = .{ .expr = field_init.value.*, .dest_is_mutable = field_info.is_mutable };
                pending_count += 1;
            }

            // Check if field is a struct type (including Arrays, which are now embedded structs)
            const is_embedded_struct = field_info.value_type == .struct_type;
            if (is_embedded_struct) {
                // Struct fields are embedded inline - copy the data
                // Use move semantics (no incref) when:
                // - Source is a temporary (will be removed from cleanup)
                // - Source is a variable (will be marked as moved by trackFieldOwnershipTransfer)
                // Use copy semantics (with incref) only for non-variable expressions that persist
                const is_temp = cleanup_helpers.isInTemporaries(self, field_val.value);
                const is_variable = field_init.value.* == .identifier;
                const needs_move = is_temp or is_variable;
                if (needs_move) {
                    // Move: ownership transfers, no incref needed
                    try string_helpers.emitStructMove(self, field_ptr, ir.toStructPtr(field_val.value), field_info.size);
                    if (is_temp) {
                        cleanup_helpers.removeFromTemporaries(self, field_val.value);
                    }
                } else {
                    // Copy: source will remain alive, incref needed
                    try string_helpers.emitStructCopy(self, field_ptr, ir.toStructPtr(field_val.value), field_info.size, field_info.structName());
                }
            } else {
                try self.func().emitStore(field_ptr.raw(), field_val.value);
            }
        }

        // Apply deferred ownership transfers now that all field expressions are evaluated
        for (pending_transfers[0..pending_count]) |transfer| {
            try self.trackFieldOwnershipTransfer(transfer.expr, type_name, transfer.dest_is_mutable);
        }

        // Apply default values for fields not provided in struct init
        if (self.type_decl_map.get(type_name)) |type_decl| {
            for (type_decl.fields) |field_decl| {
                // Skip if this field was already provided
                if (provided_fields.contains(field_decl.name)) continue;

                // Apply default value if present
                if (field_decl.default_value) |default_expr| {
                    const field_info = try self.lookupField(struct_info, field_decl.name);
                    const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(dest_ptr), field_info.offset);
                    const default_val = try self.convertExpression(default_expr.*);

                    // Check if field is a struct type (including Arrays, which are now embedded structs)
                    const is_embedded_struct = field_info.value_type == .struct_type;
                    if (is_embedded_struct) {
                        try string_helpers.emitStructCopy(self, field_ptr, ir.toStructPtr(default_val.value), field_info.size, field_info.structName());
                    } else {
                        try self.func().emitStore(field_ptr.raw(), default_val.value);
                    }
                }
            }
        }
    }

    /// Track ownership when a variable is moved into a struct field
    fn trackFieldOwnershipTransfer(self: *AstToIr, expr: ast.Expression, target_type: []const u8, dest_is_mutable: bool) ConvertError!void {
        if (expr != .identifier) return;

        const var_name = expr.identifier;
        const var_info = self.var_map.getPtr(var_name) orelse return;

        // Reference types are moved, not copied
        const is_reference = var_info.ty == .struct_type;
        if (!is_reference) return;

        // Only error if moving immutable value into mutable field
        if (!var_info.is_mutable and dest_is_mutable) {
            debug.astToIr("cannot move immutable variable '{s}' into mutable field\n", .{var_name});
            self.reportError(.E010, var_name);
            return error.ImmutableMove;
        }
        var_info.markMoved(target_type, self.current_line);
        if (self.track_memory) {
            try self.func().emitTrackMove(var_name);
        }
    }

    /// Emit a String or Character struct from a static data pointer using strlen
    fn emitStringOrCharFromPtr(self: *AstToIr, data_ptr: ir.Value, is_character: bool) ConvertError!TypedValue {
        // Get string length using inline strlen
        const str_len = try self.func().emitCstrLen(data_ptr);

        const managed_ptr = try ManagedArray.alloca(self.func());
        const zero_i32 = try self.func().emitConstI32(0);
        try ManagedArray.init(self.func(), managed_ptr, ir.toRawPtr(data_ptr), str_len, str_len, zero_i32);

        // Both Character and String use the same layout (40 bytes with _iterPos)
        const struct_ptr = try string_helpers.emitStringFromManaged(self, managed_ptr);
        if (is_character) {
            return .{ .value = struct_ptr.raw(), .ty = try self.typeNameToValueType(types.CHARACTER) };
        } else {
            return .{ .value = struct_ptr.raw(), .ty = try self.typeNameToValueType(types.STRING) };
        }
    }

    fn convertFieldAccess(self: *AstToIr, faccess: ast.FieldAccessExpr) ConvertError!TypedValue {
        // Check for enum member access (e.g., Colors.Green)
        if (faccess.base.* == .identifier) {
            if (self.type_map.getPtr(faccess.base.identifier)) |type_info_ptr| {
                if (type_info_ptr.* == .enum_type) {
                    const enum_info = &type_info_ptr.enum_type;
                    const member_value = enum_info.members.get(faccess.field_name) orelse {
                        self.reportError(.E034, faccess.field_name);
                        return error.SemanticError;
                    };
                    const enum_ty = types.ValueType{ .enum_type = enum_info };

                    switch (enum_info.backing) {
                        .float => |float_map| {
                            const float_val = float_map.get(member_value) orelse
                                return .{ .value = try self.func().emitConstI64(member_value), .ty = enum_ty };
                            const f64_val = try self.func().emitConstF64(float_val);
                            return .{ .value = try self.func().emitUnaryOp(.bitcast_f64_to_i64, f64_val, .i64), .ty = enum_ty };
                        },
                        .string => |string_map| {
                            const string_val = string_map.get(member_value) orelse
                                return .{ .value = try self.func().emitConstI64(member_value), .ty = enum_ty };
                            // Allocate null-terminated copy for static data section
                            const null_term_val = try self.allocator.alloc(u8, string_val.len + 1);
                            @memcpy(null_term_val[0..string_val.len], string_val);
                            null_term_val[string_val.len] = 0;
                            try self.module.trackString(null_term_val);
                            return .{ .value = (try self.func().emitStringConstant(null_term_val)).raw(), .ty = enum_ty };
                        },
                        .character => |char_map| {
                            const char_val = char_map.get(member_value) orelse
                                return .{ .value = try self.func().emitConstI64(member_value), .ty = enum_ty };
                            // Allocate null-terminated copy for static data section
                            const null_term_val = try self.allocator.alloc(u8, char_val.len + 1);
                            @memcpy(null_term_val[0..char_val.len], char_val);
                            null_term_val[char_val.len] = 0;
                            try self.module.trackString(null_term_val);
                            return .{ .value = (try self.func().emitStringConstant(null_term_val)).raw(), .ty = enum_ty };
                        },
                        .none, .int => {},
                    }
                    return .{ .value = try self.func().emitConstI64(member_value), .ty = enum_ty };
                }
            }
        }

        const base = try self.convertExpression(faccess.base.*);

        // Handle .rawValue for enum types
        if (base.ty == .enum_type) {
            const enum_info = base.ty.enum_type;
            if (std.mem.eql(u8, faccess.field_name, "rawValue")) {
                return switch (enum_info.backing) {
                    .none, .int => .{ .value = base.value, .ty = .{ .primitive = .int } },
                    .float => .{ .value = try self.func().emitUnaryOp(.bitcast_i64_to_f64, base.value, .f64), .ty = .{ .primitive = .float } },
                    .string => try self.emitStringOrCharFromPtr(base.value, false),
                    .character => try self.emitStringOrCharFromPtr(base.value, true),
                };
            }
            std.debug.print("[AST->IR] convertFieldAccess: expected struct type for field '{s}'\n", .{faccess.field_name});
            self.reportError(.E006, faccess.field_name);
            return error.UnknownType;
        }

        const type_name = switch (base.ty) {
            .struct_type => |struct_info| struct_info.name,
            .primitive, .enum_type, .error_union_type, .function_type => {
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

        const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(base.value), field_info.offset);

        // Struct fields are embedded (return ptr directly); others are loaded
        const value = if (field_info.value_type == .struct_type)
            field_ptr.raw()
        else
            try self.func().emitLoad(field_ptr.raw(), field_info.value_type.toIrType());

        return .{ .value = value, .ty = field_info.value_type };
    }

    /// Convert enum case construction with associated values (e.g., Result.success(42))
    fn convertEnumCase(self: *AstToIr, ec: ast.EnumCaseExpr) ConvertError!TypedValue {
        // Look up enum type
        const type_info_ptr = self.type_map.getPtr(ec.enum_name) orelse {
            self.reportError(.E006, ec.enum_name);
            return error.UnknownType;
        };

        if (type_info_ptr.* != .enum_type) {
            self.reportError(.E006, ec.enum_name);
            return error.UnknownType;
        }

        const enum_info = &type_info_ptr.enum_type;
        const enum_ty = types.ValueType{ .enum_type = enum_info };

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
            // Simple enum without associated values
            // For float-backed enums, store the float value as bitcast i64
            if (enum_info.backing == .float) {
                const float_val = enum_info.backing.float.get(case_info.tag) orelse {
                    // Fallback to tag if no float value
                    return .{
                        .value = try self.func().emitConstI64(case_info.tag),
                        .ty = enum_ty,
                    };
                };
                // Emit the float constant and bitcast to i64 for storage
                const f64_val = try self.func().emitConstF64(float_val);
                const i64_val = try self.func().emitUnaryOp(.bitcast_f64_to_i64, f64_val, .i64);
                return .{
                    .value = i64_val,
                    .ty = enum_ty,
                };
            }
            // For other enums, just return the tag
            return .{
                .value = try self.func().emitConstI64(case_info.tag),
                .ty = enum_ty,
            };
        }

        // Copy the associated_values data before calling convertExpression.
        // convertExpression can trigger type_map modifications (e.g., monomorphization),
        // which could cause reallocation and invalidate pointers into type_map.
        const associated_values = try self.allocator.dupe(types.AssociatedValueInfo, case_info.associated_values);
        defer self.allocator.free(associated_values);
        const tag = case_info.tag;
        const max_payload_size = enum_info.max_payload_size;

        // For enums with associated values, allocate storage for tag + payload
        // Layout: [tag: i64][payload...]
        const total_size = 8 + max_payload_size; // tag is i64
        const enum_ptr = try self.func().emitAlloca(.ptr);

        // Allocate memory for enum storage
        const alloc_val = try self.func().emitConstI64(total_size);
        const mem_ptr = try self.func().emitHeapAlloc(alloc_val, "enum storage");
        try self.func().emitStore(enum_ptr.raw(), mem_ptr.raw());

        // Store tag
        const tag_val = try self.func().emitConstI64(tag);
        try self.func().emitStore(mem_ptr.raw(), tag_val);

        // Store associated values
        var offset: i32 = 8; // Start after tag
        for (associated_values, 0..) |av, i| {
            const arg_typed = try self.convertExpression(ec.args[i]);

            // Type check: verify argument type matches expected associated value type
            const expected_type = av.type_name;
            const actual_type = arg_typed.ty.getTypeName();
            if (actual_type) |actual| {
                if (!self.areTypesEquivalent(actual, expected_type)) {
                    self.reportError(.E022, av.name);
                    return error.TypeMismatch;
                }
            }

            const payload_ptr = try self.func().emitGetFieldPtr(mem_ptr.asStruct(), offset);
            try self.func().emitStore(payload_ptr.raw(), arg_typed.value);
            offset += av.ir_type.sizeInBytes();
        }

        // Return the pointer to the enum storage
        const loaded_ptr = try self.func().emitLoad(enum_ptr.raw(), .ptr);
        return .{
            .value = loaded_ptr,
            .ty = enum_ty,
        };
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

        // Require named arguments for non-intrinsic function calls
        // First argument can be positional, but subsequent arguments must be named
        // Intrinsics (starting with __) and builtin functions are exempt
        const is_intrinsic = func_name.len >= 2 and func_name[0] == '_' and func_name[1] == '_';
        const is_builtin = std.mem.eql(u8, func_name, "print");
        if (!is_intrinsic and !is_builtin and positional_args.len > 1) {
            const msg = std.fmt.allocPrint(self.allocator, "Second and subsequent arguments must be named. Use 'name: value' syntax", .{}) catch {
                self.reportError(.E052, func_name);
                return error.WrongArgumentCount;
            };
            self.last_error = .{
                .code = .E052,
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

        // Count required parameters (those without default values)
        var required_count: usize = 0;
        for (explicit_params) |param| {
            if (param.default_value == null) {
                required_count += 1;
            }
        }

        // Allocate result array
        var resolved = try self.allocator.alloc(ResolvedArg, explicit_params.len);
        @memset(resolved, .{ .default = undefined });

        // Track which parameters have been filled
        var filled = try self.allocator.alloc(bool, explicit_params.len);
        defer self.allocator.free(filled);
        @memset(filled, false);

        // Fill positional arguments (only used by intrinsics)
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

        // If inside a method body, check if calling a method of the current type
        // This handles both instance methods (with self_ptr) and static methods (without self_ptr)
        // Only do this if we have an active function (not during registration)
        if (self.current_type_name != null and self.current_func != null) {
            const type_name = self.current_type_name.?;
            // Build mangled name on stack to avoid allocation
            var mangled_buf: [256]u8 = undefined;
            if (std.fmt.bufPrint(&mangled_buf, "{s}${s}", .{ type_name, call.func_name })) |mangled_name| {
                if (self.func_map.contains(mangled_name)) {
                    // It's a method of the current type - call with self if available (instance method)
                    // or with null (static method)
                    if (self.self_ptr) |self_val| {
                        debug.astToIr("Implicit instance method call: {s} (self_ptr=%{d})", .{ mangled_name, self_val });
                        return self.emitMethodCallWithNamedArgs(type_name, call.func_name, call.args, call.named_args, self_val);
                    } else {
                        debug.astToIr("Implicit static method call: {s}", .{mangled_name});
                        return self.emitMethodCallWithNamedArgs(type_name, call.func_name, call.args, call.named_args, null);
                    }
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

        // Check if return type is error union (needs sret)
        const returns_error_union = if (func_info.return_value_type) |vt|
            vt == .error_union_type
        else
            false;

        // Validate that throwing functions are called within try context
        if (returns_error_union and !self.in_try_context) {
            self.reportError(.E057, call.func_name);
            return error.SemanticError;
        }

        // Check if callee returns a struct or error union (needs sret)
        // Note: return_type_name may be set for enums too (from AST extraction),
        // so check type_map to verify it's actually a struct before assuming sret
        const returns_actual_struct = if (func_info.return_type_name) |name|
            if (self.type_map.get(name)) |ti| ti == .struct_type else false
        else
            false;
        const returns_struct = returns_actual_struct or returns_error_union;
        var sret_buffer: ?ir.Value = null;

        // Allocate args: +1 if using sret for hidden first parameter
        const num_args = resolved_args.len + @as(usize, if (returns_struct) 1 else 0);
        const args = try self.func().allocator.alloc(ir.Value, num_args);

        // If returning struct or error union, allocate buffer in caller and pass as first arg
        if (returns_struct) {
            if (returns_error_union) {
                // Error union size = 8 (tag) + max(success_size, error_size)
                const eu_info = func_info.return_value_type.?.error_union_type;
                const success_size = self.getErrorUnionSuccessSize(eu_info) orelse {
                    self.reportInternalError("unknown error union success type size");
                    return error.SemanticError;
                };
                // Error enums are always 8 bytes (i64 ordinal)
                const error_size: i32 = 8;
                sret_buffer = (try self.func().emitAllocaSized(8 + @max(success_size, error_size))).raw();
            } else {
                const struct_name = func_info.return_type_name.?;
                const struct_info = try self.lookupStructInfo(struct_name);
                sret_buffer = (try self.func().emitAllocaSized(struct_info.size)).raw();
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

            // Type validation and implicit conversions
            if (i < func_info.param_types.len) {
                const param_ty = func_info.param_types[i].ty;
                // Try implicit conversion if conversion is possible
                if (self.canConvert(arg_ty, param_ty)) {
                    const converted = try self.convertType(arg_value, arg_ty, param_ty);
                    arg_value = converted.value;
                    arg_ty = converted.ty;
                }
                if (!self.checkTypeCompatibility(arg_ty, param_ty)) {
                    const expected_name = func_info.param_types[i].display_name orelse
                        param_ty.getTypeName() orelse "unknown";
                    const actual_name = arg_ty.getTypeName() orelse "unknown";
                    const param_name = func_info.param_types[i].name;

                    const msg = std.fmt.allocPrint(
                        self.allocator,
                        "argument type mismatch for '{s}': expected '{s}', got '{s}'",
                        .{ param_name, expected_name, actual_name },
                    ) catch "type mismatch";

                    self.last_error = .{
                        .code = .E022,
                        .message = msg,
                        .location = .{
                            .file = self.source_file,
                            .line = @intCast(self.current_line),
                            .column = @intCast(self.current_column),
                        },
                        .message_allocated = true,
                    };
                    return error.TypeMismatch;
                }
            }

            args[i + arg_offset] = arg_value;
            // Only check ownership for explicitly provided args
            if (resolved_arg == .expr) {
                try self.checkOwnershipTransfer(call.func_name, arg_expr, i);
            }
        }

        const result = try self.func().emitCall(call.func_name, args, func_info.return_type);

        // If sret buffer was used (for actual structs or error unions), return it
        if (sret_buffer) |buf| {
            if (returns_error_union) {
                return .{ .value = buf, .ty = func_info.return_value_type.? };
            }
            // Must be a struct
            return .{ .value = buf, .ty = try self.typeNameToValueType(func_info.return_type_name.?) };
        }
        // If the function returns an array, use the full array type info
        if (func_info.return_value_type) |vtype| {
            return .{ .value = result orelse 0, .ty = vtype };
        }
        // Check if return_type_name is an enum (not a struct that would have used sret)
        if (func_info.return_type_name) |name| {
            if (self.type_map.getPtr(name)) |ti_ptr| {
                if (ti_ptr.* == .enum_type) {
                    return .{ .value = result orelse 0, .ty = .{ .enum_type = &ti_ptr.enum_type } };
                }
            }
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = types.Primitive.fromIrType(func_info.return_type) } };
    }

    /// Convert an indirect call through a function variable
    fn convertIndirectCall(self: *AstToIr, call: ast.CallExpr, var_info: VarInfo) ConvertError!TypedValue {
        const func_type_info = var_info.ty.function_type;

        // Load the function pointer from the variable
        const func_ptr = if (var_info.is_heap_allocated)
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
            break :blk ret_vt.* == .struct_type;
        } else false;

        var sret_buffer: ?ir.Value = null;
        const num_args = call.args.len + @as(usize, if (returns_struct) 1 else 0);
        const args = try self.func().allocator.alloc(ir.Value, num_args);

        // If returning struct, allocate buffer in caller and pass as first arg
        if (returns_struct) {
            if (func_type_info.return_type) |ret_vt| {
                if (ret_vt.* == .struct_type) {
                    const struct_info = ret_vt.struct_type;
                    sret_buffer = (try self.func().emitAllocaSized(struct_info.size)).raw();
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

            // Implicit type conversions
            if (i < func_type_info.param_types.len) {
                const param_ty = func_type_info.param_types[i];
                if (self.canConvert(arg.ty, param_ty)) {
                    const converted = try self.convertType(arg_value, arg.ty, param_ty);
                    arg_value = converted.value;
                }
            }

            args[i + arg_offset] = arg_value;
        }

        const result = try self.func().emitCallIndirect(ir.FuncPtr{ .val = func_ptr }, args, func_type_info.return_ir_type);

        // Return the appropriate type
        if (func_type_info.return_type) |ret_vt| {
            if (ret_vt.* == .struct_type) {
                return .{ .value = sret_buffer.?, .ty = ret_vt.* };
            }
            return .{ .value = result orelse 0, .ty = ret_vt.* };
        }
        return .{ .value = result orelse 0, .ty = .{ .primitive = .void } };
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
            keys_managed_ptr = (try array_helpers.emitEmptyManagedArray(
                self,
            )).raw();
            values_managed_ptr = (try array_helpers.emitEmptyManagedArray(
                self,
            )).raw();
        } else {
            // Allocate refcounted buffers for keys and values
            const elem_count = try self.func().emitConstI64(@intCast(entries.len));
            const elem_size: i64 = 8; // All elements are 8 bytes
            const buffer_size = try self.func().emitConstI64(@intCast(entries.len * @as(usize, @intCast(elem_size))));
            const keys_buffer = try array_helpers.emitAllocRefcountedBuffer(self, buffer_size, "map buffer");
            const values_buffer = try array_helpers.emitAllocRefcountedBuffer(self, buffer_size, "map buffer");

            // Store entries into buffers
            for (entries, 0..) |entry, i| {
                const key_typed = try self.convertExpression(entry.key.*);
                const value_typed = try self.convertExpression(entry.value.*);
                const idx_val = try self.func().emitConstI64(@intCast(i));

                const key_ptr = try self.func().emitGetElemPtr(keys_buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(key_ptr.raw(), key_typed.value);

                const value_ptr = try self.func().emitGetElemPtr(values_buffer, idx_val, @intCast(elem_size));
                try self.func().emitStore(value_ptr.raw(), value_typed.value);
            }

            keys_managed_ptr = (try array_helpers.emitManagedArray(self, keys_buffer, elem_count, elem_count)).raw();
            values_managed_ptr = (try array_helpers.emitManagedArray(self, values_buffer, elem_count, elem_count)).raw();
        }

        // Builtin types receive __ManagedArrays directly
        // Other types receive Arrays (we create them first, then pass to their init)
        const is_builtin_type = self.isBuiltinType(type_name);

        // For non-Builtin types, first create Arrays from the __ManagedArrays
        var keys_arg: ir.Value = undefined;
        var values_arg: ir.Value = undefined;
        if (is_builtin_type) {
            keys_arg = keys_managed_ptr;
            values_arg = values_managed_ptr;
        } else {
            // Extract key and value types from the monomorphized type name (e.g., "CustomMap$String$int")
            // Format: TypeName$KeyType$ValueType
            var key_type_name: []const u8 = "int";
            var value_type_name: []const u8 = "int";
            if (std.mem.indexOf(u8, type_name, "$")) |first_dollar| {
                const after_first = type_name[first_dollar + 1 ..];
                if (std.mem.indexOf(u8, after_first, "$")) |second_dollar| {
                    key_type_name = after_first[0..second_dollar];
                    value_type_name = after_first[second_dollar + 1 ..];
                } else {
                    // Only one type arg - use same for both (shouldn't happen for dict literal)
                    key_type_name = after_first;
                    value_type_name = after_first;
                }
            }

            // Create Array$KeyType for keys
            var key_type_args = [_][]const u8{key_type_name};
            const keys_array_type_name = try self.getOrCreateMonomorphizedType("Array", &key_type_args);
            const keys_array_init_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{keys_array_type_name});
            try self.module.trackString(keys_array_init_name);
            const keys_array_func_info = self.func_map.get(keys_array_init_name) orelse {
                self.reportInternalError("Array init not found for InitableFromDictionaryLiteral keys");
                return error.UnknownFunction;
            };

            if (!keys_array_func_info.ir_generated) {
                try self.ensureMethodGenerated(keys_array_init_name);
            }

            const keys_array_type_info = self.type_map.get(keys_array_type_name) orelse {
                self.reportError(.E006, keys_array_type_name);
                return error.UnknownType;
            };

            // Create Array$ValueType for values
            var value_type_args = [_][]const u8{value_type_name};
            const values_array_type_name = try self.getOrCreateMonomorphizedType("Array", &value_type_args);
            const values_array_init_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{values_array_type_name});
            try self.module.trackString(values_array_init_name);
            const values_array_func_info = self.func_map.get(values_array_init_name) orelse {
                self.reportInternalError("Array init not found for InitableFromDictionaryLiteral values");
                return error.UnknownFunction;
            };

            if (!values_array_func_info.ir_generated) {
                try self.ensureMethodGenerated(values_array_init_name);
            }

            const values_array_type_info = self.type_map.get(values_array_type_name) orelse {
                self.reportError(.E006, values_array_type_name);
                return error.UnknownType;
            };

            // Create keys Array
            const keys_array_size = keys_array_type_info.struct_type.size;
            const keys_array_ptr = try self.func().emitAllocaSized(keys_array_size);
            var keys_array_args = try self.func().allocator.alloc(ir.Value, 2);
            keys_array_args[0] = keys_array_ptr.raw();
            keys_array_args[1] = keys_managed_ptr;
            _ = try self.func().emitCall(keys_array_init_name, keys_array_args, keys_array_func_info.return_type);

            // Create values Array
            const values_array_size = values_array_type_info.struct_type.size;
            const values_array_ptr = try self.func().emitAllocaSized(values_array_size);
            var values_array_args = try self.func().allocator.alloc(ir.Value, 2);
            values_array_args[0] = values_array_ptr.raw();
            values_array_args[1] = values_managed_ptr;
            _ = try self.func().emitCall(values_array_init_name, values_array_args, values_array_func_info.return_type);

            keys_arg = keys_array_ptr.raw();
            values_arg = values_array_ptr.raw();
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
            args[0] = result_ptr.raw();
            args[1] = keys_arg;
            args[2] = values_arg;

            // Call init with sret
            _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

            // Store result in var_map
            try self.func().setValueName(result_ptr.raw(), decl.name);
            const var_type: ValueType = try self.typeNameToValueType(type_name);
            try self.var_map.put(self.allocator, decl.name, VarInfo.initStackAllocated(
                result_ptr.raw(),
                var_type,
                self.current_decl_is_mutable,
            ));
        } else {
            // Non-struct return type (unlikely for InitableFromDictionaryLiteral)
            var args = try self.func().allocator.alloc(ir.Value, 2);
            args[0] = keys_arg;
            args[1] = values_arg;
            const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
            const result_ptr = try self.func().emitAlloca(func_info.return_type);
            try self.func().emitStore(result_ptr.raw(), result orelse 0);
            try self.func().setValueName(result_ptr.raw(), decl.name);
            const var_type: ValueType = .{ .primitive = types.Primitive.fromIrType(func_info.return_type) };
            try self.var_map.put(self.allocator, decl.name, VarInfo.initStackAllocated(
                result_ptr.raw(),
                var_type,
                self.current_decl_is_mutable,
            ));
        }
    }

    /// The wrapper type to use for literal initialization
    const LiteralWrapperType = enum {
        string,
        character,
    };

    /// Emit a wrapper type (String or Character) from a __ManagedArray.
    /// Returns the pointer to the allocated wrapper struct.
    fn emitWrapperFromManaged(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr, wrapper: LiteralWrapperType) ConvertError!ir.Value {
        const wrapper_name: []const u8 = switch (wrapper) {
            .string => "String",
            .character => "Character",
        };
        const init_name: []const u8 = switch (wrapper) {
            .string => "String$init",
            .character => "Character$init",
        };

        const func_info = self.func_map.get(init_name) orelse {
            self.reportInternalError(switch (wrapper) {
                .string => "String$init not found",
                .character => "Character$init not found",
            });
            return error.UnknownFunction;
        };

        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_name);
        }

        const type_info = self.type_map.get(wrapper_name) orelse {
            self.reportError(.E006, wrapper_name);
            return error.UnknownType;
        };

        const wrapper_ptr = try self.func().emitAllocaSized(type_info.struct_type.size);
        var args = try self.func().allocator.alloc(ir.Value, 2);
        args[0] = wrapper_ptr.raw();
        args[1] = managed_ptr.raw();
        _ = try self.func().emitCall(init_name, args, func_info.return_type);

        return wrapper_ptr.raw();
    }

    /// Call Type$init with an argument and return the result pointer.
    /// Returns the result pointer and the type info for use in TypedValue.
    fn emitTypeInit(self: *AstToIr, type_name: []const u8, init_arg: ir.Value) ConvertError!struct { ptr: ir.Value, type_info: *const types.StructTypeInfo } {
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
        try self.module.trackString(init_func_name);

        const func_info = self.func_map.get(init_func_name) orelse {
            const msg = std.fmt.allocPrint(self.allocator, "type '{s}' missing init method", .{type_name}) catch "missing init method";
            self.reportInternalError(msg);
            return error.UnknownFunction;
        };

        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        const type_info_ptr = self.type_map.getPtr(type_name) orelse {
            self.reportError(.E006, type_name);
            return error.UnknownType;
        };

        const struct_size = type_info_ptr.struct_type.size;
        const result_ptr = try self.func().emitAllocaSized(struct_size);

        var args = try self.func().allocator.alloc(ir.Value, 2);
        args[0] = result_ptr.raw();
        args[1] = init_arg;
        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        return .{ .ptr = result_ptr.raw(), .type_info = &type_info_ptr.struct_type };
    }

    /// Common implementation for literal initialization (string or character literals).
    /// For builtin types, passes __ManagedArray directly.
    /// For other types, creates the wrapper type first then calls Type$init.
    fn convertLiteralInit(
        self: *AstToIr,
        bytes: []const u8,
        type_name: []const u8,
        decl_name: []const u8,
        wrapper: LiteralWrapperType,
    ) !void {
        const managed_ptr = try string_helpers.emitManagedArrayFromBytes(self, bytes);

        // Builtin types receive __ManagedArray directly
        const init_arg = if (self.isBuiltinType(type_name))
            managed_ptr.raw()
        else
            try self.emitWrapperFromManaged(managed_ptr, wrapper);

        const result = try self.emitTypeInit(type_name, init_arg);

        try self.func().setValueName(result.ptr, decl_name);
        try self.var_map.put(self.allocator, decl_name, VarInfo.initStackAllocated(
            result.ptr,
            .{ .struct_type = result.type_info },
            self.current_decl_is_mutable,
        ));
    }

    /// Common implementation for literal casts ("hello" as MyType or 'A' as MyType).
    /// Always creates the wrapper type first (casts are never to builtin types directly).
    fn convertLiteralCast(
        self: *AstToIr,
        literal: []const u8,
        type_name: []const u8,
        wrapper: LiteralWrapperType,
    ) ConvertError!TypedValue {
        const processed = try self.processEscapeSequences(literal);
        defer self.allocator.free(processed);

        const managed_ptr = try string_helpers.emitManagedArrayFromBytes(self, processed);
        const wrapper_ptr = try self.emitWrapperFromManaged(managed_ptr, wrapper);
        const result = try self.emitTypeInit(type_name, wrapper_ptr);

        return .{
            .value = result.ptr,
            .ty = .{ .struct_type = result.type_info },
        };
    }

    /// Convert InitableFromStringLiteral: var s String = "hello"
    fn convertInitableFromStringLiteral(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const processed = try self.processEscapeSequences(decl.value.string_literal);
        defer self.allocator.free(processed);
        try self.convertLiteralInit(processed, type_name, decl.name, .string);
    }

    /// Convert InitableFromCharLiteral: var c Character = 'a'
    fn convertInitableFromCharLiteral(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
        const processed = try self.processEscapeSequences(decl.value.char_literal);
        defer self.allocator.free(processed);
        try self.convertLiteralInit(processed, type_name, decl.name, .character);
    }

    /// Convert string literal cast: "hello" as MyType
    fn convertStringLiteralCast(self: *AstToIr, str_literal: []const u8, type_name: []const u8) ConvertError!TypedValue {
        return self.convertLiteralCast(str_literal, type_name, .string);
    }

    /// Convert character literal cast: 'A' as MyType
    fn convertCharLiteralCast(self: *AstToIr, char_literal: []const u8, type_name: []const u8) ConvertError!TypedValue {
        return self.convertLiteralCast(char_literal, type_name, .character);
    }

    fn convertIndex(self: *AstToIr, idx: ast.IndexExpr) ConvertError!TypedValue {
        const base_typed = try self.convertExpression(idx.base.*);

        // Handle __ManagedArray indexing (stdlib only)
        if (base_typed.ty == .struct_type) {
            if (std.mem.eql(u8, base_typed.ty.struct_type.name, "__ManagedArray")) {
                // Extract variable name if base is an identifier
                const var_name: ?[]const u8 = if (idx.base.* == .identifier) idx.base.identifier else null;
                return array_helpers.convertManagedArrayIndex(self, base_typed.value, idx.index.*, var_name);
            }
            // Handle stdlib Array types (Array$int, etc.) - call .get() method
            if (std.mem.startsWith(u8, base_typed.ty.struct_type.name, "Array$")) {
                return array_helpers.convertStdlibArrayIndex(self, base_typed, idx.index.*);
            }
        }

        // Only struct types with Array$ prefix or __ManagedArray support indexing
        std.debug.print("[AST->IR] convertIndexExpr: expected array type\n", .{});
        self.reportError(.E006, "expected array type for indexing");
        return error.UnknownType;
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

        // Check if return type is error union (needs sret for error union wrapper)
        const returns_error_union = if (func_info.return_value_type) |vt|
            vt == .error_union_type
        else
            false;

        // Validate that throwing methods are called within try context
        if (returns_error_union and !self.in_try_context) {
            self.reportError(.E057, method_name);
            return error.SemanticError;
        }

        // Determine argument layout
        // Note: return_type_name may be set for enums too (from AST extraction),
        // so check type_map to verify it's actually a struct before assuming sret
        const returns_actual_struct = if (func_info.return_type_name) |name|
            if (self.type_map.get(name)) |ti| ti == .struct_type else false
        else
            false;
        const returns_struct = returns_actual_struct or returns_error_union;
        const has_self = self_value != null;
        const sret_offset: usize = if (returns_struct) 1 else 0;
        const self_offset: usize = if (has_self) 1 else 0;
        const num_args = call_args.len() + sret_offset + self_offset;

        const args = try self.allocator.alloc(ir.Value, num_args);
        var arg_idx: usize = 0;

        // Sret buffer as first arg if returning struct or error union
        var sret_buffer: ?ir.Value = null;
        if (returns_struct) {
            if (returns_error_union) {
                // Error union size = 8 (tag) + max(success_size, error_size)
                const eu_info = func_info.return_value_type.?.error_union_type;
                const success_size = self.getErrorUnionSuccessSize(eu_info) orelse {
                    self.reportInternalError("unknown error union success type size");
                    return error.SemanticError;
                };
                // Error enums are always 8 bytes (i64 ordinal)
                const error_size: i32 = 8;
                sret_buffer = (try self.func().emitAllocaSized(8 + @max(success_size, error_size))).raw();
            } else {
                const struct_info = try self.lookupStructInfo(func_info.return_type_name.?);
                sret_buffer = (try self.func().emitAllocaSized(struct_info.size)).raw();
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

                    // Type validation and implicit conversions
                    if (param_index < func_info.param_types.len) {
                        const param_ty = func_info.param_types[param_index].ty;
                        // Try implicit conversion if conversion is possible
                        if (self.canConvert(arg_ty, param_ty)) {
                            const converted = try self.convertType(arg_value, arg_ty, param_ty);
                            arg_value = converted.value;
                            arg_ty = converted.ty;
                        }
                        if (!self.checkTypeCompatibility(arg_ty, param_ty)) {
                            const expected_name = func_info.param_types[param_index].display_name orelse
                                param_ty.getTypeName() orelse "unknown";
                            const actual_name = arg_ty.getTypeName() orelse "unknown";
                            const param_name = func_info.param_types[param_index].name;

                            const msg = std.fmt.allocPrint(
                                self.allocator,
                                "argument type mismatch for '{s}': expected '{s}', got '{s}'",
                                .{ param_name, expected_name, actual_name },
                            ) catch "type mismatch";

                            self.last_error = .{
                                .code = .E022,
                                .message = msg,
                                .location = .{
                                    .file = self.source_file,
                                    .line = @intCast(self.current_line),
                                    .column = @intCast(self.current_column),
                                },
                                .message_allocated = true,
                            };
                            return error.TypeMismatch;
                        }
                    }

                    // NOTE: String temporaries passed to Array.push are NOT removed from temporaries here.
                    // The intrinsic __managed_array_set_at calls emitStringIncref on the stored value,
                    // bumping refcount to 2. The temp cleanup decrefs (to 1), and array cleanup decrefs (to 0, freed).
                    // This ensures proper refcounting with the buffer header scheme.

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
            if (returns_error_union) {
                return .{ .value = buf, .ty = func_info.return_value_type.? };
            }
            return .{ .value = buf, .ty = try self.typeNameToValueType(func_info.return_type_name.?) };
        }
        const ret_ty: ValueType = if (func_info.return_value_type) |vt|
            vt
        else if (func_info.return_type_name) |name| blk: {
            // Check if it's actually a struct or an enum
            if (self.type_map.getPtr(name)) |ti_ptr| {
                break :blk if (ti_ptr.* == .struct_type)
                    .{ .struct_type = &ti_ptr.struct_type }
                else if (ti_ptr.* == .enum_type)
                    .{ .enum_type = &ti_ptr.enum_type }
                else
                    .{ .primitive = types.Primitive.fromIrType(func_info.return_type) };
            }
            break :blk self.typeNameToValueType(name) catch .{ .primitive = types.Primitive.fromIrType(func_info.return_type) };
        } else .{ .primitive = types.Primitive.fromIrType(func_info.return_type) };

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
            .struct_type => |struct_info| struct_info.name,
            .primitive, .enum_type, .error_union_type, .function_type => {
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
            if (std.mem.eql(u8, mcall.method_name, "insert")) {}
            return self.emitMethodCallWithNamedArgs(base_typed.ty.struct_type.name, mcall.method_name, mcall.args, mcall.named_args, base_typed.value);
        }

        // Check if this is a primitive type with hash/equals methods
        if (base_typed.ty == .primitive) {
            return self.emitPrimitiveMethodCall(base_typed, mcall.method_name, mcall.args);
        }

        // Check if this is an enum type with methods
        if (base_typed.ty == .enum_type) {
            const enum_name = base_typed.ty.enum_type.name;
            return self.emitMethodCallWithNamedArgs(enum_name, mcall.method_name, mcall.args, mcall.named_args, base_typed.value);
        }

        debug.astToIr("error: method call on non-struct type", .{});
        self.reportError(.E003, mcall.method_name);
        return error.SemanticError;
    }

    /// Emit a method call on a primitive type (hash, equals)
    fn emitPrimitiveMethodCall(self: *AstToIr, base_typed: TypedValue, method_name: []const u8, arg_exprs: []const ast.Expression) ConvertError!TypedValue {
        const prim = base_typed.ty.primitive;

        if (std.mem.eql(u8, method_name, "hash")) {
            return self.emitPrimitiveHash(base_typed.value, prim);
        } else if (std.mem.eql(u8, method_name, "equals")) {
            if (arg_exprs.len != 1) {
                self.reportError(.E011, "equals() requires exactly 1 argument");
                return error.WrongArgumentCount;
            }
            const other = try self.convertExpression(arg_exprs[0]);
            return self.emitPrimitiveEquals(base_typed.value, other.value, prim);
        }

        debug.astToIr("error: unknown method '{s}' on primitive type '{s}'", .{ method_name, prim.toMaxonName() });
        self.reportError(.E003, method_name);
        return error.SemanticError;
    }

    /// Emit hash() for primitive types
    fn emitPrimitiveHash(self: *AstToIr, value: ir.Value, prim: types.Primitive) ConvertError!TypedValue {
        if (prim.isFloatingPoint()) {
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
            try self.func().emitStore(result_ptr.raw(), zero_i64);

            // Switch to else block
            try branch.switchToElse(true);

            // Else block (non-zero case): bitcast float to i64
            const bitcast_value = try self.func().emitUnaryOp(.bitcast_f64_to_i64, value, .i64);
            try self.func().emitStore(result_ptr.raw(), bitcast_value);

            // Switch to merge block
            try branch.switchToMerge(true);

            // Load result
            const result = try self.func().emitLoad(result_ptr.raw(), .i64);

            return .{ .value = result, .ty = .{ .primitive = .int } };
        } else if (prim.isIntegral()) {
            // int/bool/byte.hash() returns the value directly
            return .{ .value = value, .ty = .{ .primitive = .int } };
        }

        debug.astToIr("error: hash not supported for type '{s}'", .{prim.toMaxonName()});
        self.reportError(.E003, prim.toMaxonName());
        return error.SemanticError;
    }

    /// Emit equals() for primitive types
    fn emitPrimitiveEquals(self: *AstToIr, left: ir.Value, right: ir.Value, prim: types.Primitive) ConvertError!TypedValue {
        if (prim.isFloatingPoint()) {
            // Float comparison (follows IEEE semantics: NaN != NaN)
            const result = try self.func().emitBinaryOp(.fcmp_eq, left, right, .i64);
            return .{ .value = result, .ty = .{ .primitive = .bool } };
        } else if (prim.isIntegral()) {
            // Integer comparison
            const result = try self.func().emitBinaryOp(.icmp_eq, left, right, .i64);
            return .{ .value = result, .ty = .{ .primitive = .bool } };
        }

        debug.astToIr("error: equals not supported for type '{s}'", .{prim.toMaxonName()});
        self.reportError(.E003, prim.toMaxonName());
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

/// Copy an error, duplicating the file path so it outlives the converter
fn copyErrorWithOwnedPath(allocator: std.mem.Allocator, error_in: err.CompileError) err.CompileError {
    var error_out = error_in;
    // Duplicate the file path so it survives after converter is deinitialized
    if (error_in.location.file) |file| {
        error_out.location.file = allocator.dupe(u8, file) catch null;
        error_out.location.file_allocated = true;
    }
    return error_out;
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
    external_enums: ?[]const ExternalEnumInfo,
    options: ConvertOptions,
    out_error: *?err.CompileError,
) ConvertError!ir.Module {
    var converter = AstToIr.init(allocator, mutation_analyzer);
    converter.source_file = source_file;
    converter.track_memory = options.track_memory;
    defer converter.deinit();

    // Pre-allocate type_map capacity to prevent resizing during conversion.
    // ValueType stores pointers into type_map (struct_type, enum_type), so resizing
    // would invalidate those pointers causing use-after-free bugs.
    // Estimate: primitives(~20) + builtins(~10) + external types/enums + monomorphized types
    const estimated_types = 50 + (external_types orelse &[_]ExternalTypeInfo{}).len +
        (external_enums orelse &[_]ExternalEnumInfo{}).len + 500; // 500 extra for monomorphized types
    try converter.type_map.ensureUnusedCapacity(allocator, @intCast(estimated_types));

    // Register builtin types first (primitives, __ManagedArray, cstring, etc.)
    // External types may depend on these (e.g., Array has a __ManagedArray field)
    try converter.registerBuiltinTypes();

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
            if (converter.type_map.contains(ext_enum.enum_decl.name)) continue;

            // Temporarily set source_file to the enum's source for error reporting
            const original_source_file = converter.source_file;
            converter.source_file = ext_enum.source_path;

            converter.registerEnum(ext_enum.enum_decl.*) catch |e| {
                if (converter.last_error) |le| {
                    out_error.* = copyErrorWithOwnedPath(allocator, le);
                }
                converter.source_file = original_source_file;
                return e;
            };

            // Restore original source file
            converter.source_file = original_source_file;
            debug.astToIr("Registered external enum '{s}'", .{ext_enum.enum_decl.name});
        }
    }

    // Register external types before conversion.
    // Use multiple passes because types can depend on each other.
    if (external_types) |ext_types| {
        var registered_count: usize = 0;
        var pass: usize = 0;
        const max_passes = ext_types.len + 1;

        while (registered_count < ext_types.len and pass < max_passes) : (pass += 1) {
            const prev_count = registered_count;
            for (ext_types) |ext_type| {
                // Skip if already registered
                if (converter.type_map.contains(ext_type.name)) continue;

                // Try to register - may fail if dependencies aren't ready
                converter.registerExternalType(ext_type) catch {
                    // Clear any error from this attempt to avoid memory leaks
                    converter.clearLastError();
                    continue; // Try again next pass
                };
                registered_count += 1;
            }
            // If no progress was made, we have unresolvable dependencies
            if (registered_count == prev_count) break;
        }

        // Check if all types were registered
        if (registered_count < ext_types.len) {
            // Find the first unregistered type for error reporting
            for (ext_types) |ext_type| {
                if (!converter.type_map.contains(ext_type.name)) {
                    debug.astToIr("Failed to register external type '{s}' after {d} passes", .{ ext_type.name, pass });
                    return error.UnknownType;
                }
            }
        }
    }

    // Register external function signatures before conversion
    if (external_funcs) |funcs| {
        for (funcs) |ext_func| {
            // Skip if already registered (avoid duplicates from multiple source files)
            if (converter.func_map.contains(ext_func.name)) continue;

            // Convert ExternalParamTypes to ParamTypes by resolving type names to pointers
            const param_types = try converter.convertExternalParamTypes(ext_func.param_types);

            // Convert ExternalValueType to ValueType for return type
            const return_value_type: ?ValueType = if (ext_func.return_value_type) |evt|
                converter.convertExternalValueType(evt)
            else
                null;

            try converter.func_map.put(allocator, ext_func.name, .{
                .return_type = ext_func.return_type,
                .return_type_name = ext_func.return_type_name,
                .return_value_type = return_value_type,
                .param_types = param_types,
            });
            debug.astToIr("Registered external function '{s}' returning {s}", .{ ext_func.name, ext_func.return_type.toIrName() });
        }
    }

    const result = converter.convert(program) catch |e| {
        if (converter.last_error) |le| {
            out_error.* = copyErrorWithOwnedPath(allocator, le);
        }
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

/// Extract type info from a parsed program (for cross-module compilation).
/// Only stores type declarations - sizes are computed during registration
/// when all type information is available.
pub fn extractTypeInfo(program: ast.Program, allocator: std.mem.Allocator) ![]ExternalTypeInfo {
    var type_infos = std.ArrayListUnmanaged(ExternalTypeInfo){};
    errdefer type_infos.deinit(allocator);

    for (program.types) |*type_decl| {
        try type_infos.append(allocator, .{
            .name = type_decl.name,
            .type_decl = type_decl,
            .is_exported = type_decl.is_export,
        });
    }

    return type_infos.toOwnedSlice(allocator);
}

/// Extract enum declarations from a parsed program (for cross-module compilation)
/// Includes all enums (public and private) so they can be registered with proper visibility
pub fn extractEnumDecls(program: ast.Program, allocator: std.mem.Allocator, source_path: ?[]const u8) ![]const ExternalEnumInfo {
    var enum_infos = std.ArrayListUnmanaged(ExternalEnumInfo){};
    errdefer enum_infos.deinit(allocator);

    for (program.enums) |*enum_decl| {
        try enum_infos.append(allocator, .{
            .enum_decl = enum_decl,
            .source_path = source_path,
        });
    }

    return enum_infos.toOwnedSlice(allocator);
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
        // Free allocated names, param_types (including struct_type strings), and return_type_name in case of error
        for (signatures.items) |sig| {
            allocator.free(sig.name);
            if (sig.param_types.len > 0) {
                freeExternalParamTypes(allocator, sig.param_types);
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
            const name = getStructNameFromTypeExpr(allocator, rt, null);
            if (name) |n| {
                // For generic types, getStructNameFromTypeExpr already allocates
                // For simple types, we need to dupe
                if (rt == .generic) {
                    break :blk n;
                }
                break :blk try allocator.dupe(u8, n);
            }
            break :blk null;
        } else null;

        const return_value_type: ?types.ExternalValueType = if (func.return_type) |rt|
            getExternalValueTypeForReturn(allocator, rt)
        else
            null;

        // Extract parameter types (including names and default values for named args)
        const param_types = try allocator.alloc(types.ExternalParamType, func.params.len);
        for (func.params, 0..) |param, i| {
            const vt = getExternalValueTypeFromTypeExpr(allocator, param.type_expr);
            param_types[i] = .{
                .ty = vt,
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
            errdefer allocator.free(mangled_name);

            const return_type: ir.Type = if (method.return_type) |rt|
                getIrTypeFromTypeExpr(rt)
            else
                .void;

            // For methods returning the containing type (like init() -> Self)
            // Always dupe return_type_name so we can uniformly free it during cleanup
            var return_type_name: ?[]const u8 = if (method.return_type) |rt| blk: {
                const name = getStructNameFromTypeExpr(allocator, rt, null);
                if (name) |n| {
                    // For generic types, getStructNameFromTypeExpr already allocates
                    // For simple types, we need to dupe
                    if (rt == .generic) {
                        break :blk n;
                    }
                    break :blk try allocator.dupe(u8, n);
                }
                break :blk null;
            } else null;

            // Track primitive return type (byte, int, etc.) for error unions
            const return_primitive_type: ?types.Primitive = if (method.return_type) |rt| blk: {
                switch (rt) {
                    .simple => |name| break :blk types.Primitive.fromString(name),
                    else => break :blk null,
                }
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
            var return_value_type: ?types.ExternalValueType = if (method.return_type) |rt|
                getExternalValueTypeForReturn(allocator, rt)
            else
                null;

            // If method throws, wrap return type in error_union_type
            if (method.throws_type) |error_type| {
                const success_ir_type: ir.Type = if (method.return_type) |rt| getIrTypeFromTypeExpr(rt) else .void;
                return_value_type = .{
                    .error_union_type = .{
                        .success_type = success_ir_type,
                        .success_primitive_type = return_primitive_type,
                        .success_struct_type = return_type_name,
                        .error_enum_type = error_type,
                    },
                };
            }

            // Extract parameter types for methods (including names and default values for named args)
            // For instance methods, include the implicit self parameter to match buildMethodFuncInfo
            const extra_params: usize = if (method.is_static) 0 else 1;
            const param_types = try allocator.alloc(types.ExternalParamType, method.params.len + extra_params);
            if (!method.is_static) {
                // Allocate a copy of type_decl.name for consistent ownership
                const self_type_name = try allocator.dupe(u8, type_decl.name);
                param_types[0] = .{ .ty = .{ .struct_type = self_type_name }, .name = "self" };
            }
            for (method.params, 0..) |param, i| {
                const vt = getExternalValueTypeFromTypeExpr(allocator, param.type_expr);
                param_types[i + extra_params] = .{
                    .ty = vt,
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
            // Use centralized primitive type lookup
            return types.nameToIrType(name);
        },
        .generic => .ptr,
        .error_union => .ptr,
        .function_type => .ptr,
    };
}

/// Helper: get ExternalValueType from TypeExpr (for error union/array types)
/// Returns null for simple types that don't need special tracking.
fn getExternalValueTypeForReturn(allocator: std.mem.Allocator, te: ast.TypeExpr) ?types.ExternalValueType {
    return switch (te) {
        .error_union => |eu| {
            const success_ir_type = getIrTypeFromTypeExpr(eu.success_type.*);
            const success_struct_name = getStructNameFromTypeExpr(allocator, eu.success_type.*, null);
            // Get primitive type if success type is a primitive (e.g., .byte)
            const success_primitive: ?types.Primitive = switch (eu.success_type.*) {
                .simple => |name| types.Primitive.fromString(name),
                else => null,
            };
            return .{
                .error_union_type = .{
                    .success_type = success_ir_type,
                    .success_primitive_type = success_primitive,
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
/// Returns a pointer to static string data for simple types, or null for primitives.
/// For generic types, allocates a new string. If alloc_tracker is provided, the string is
/// added to the list for later cleanup; otherwise the caller must free it.
fn getStructNameFromTypeExpr(
    allocator: std.mem.Allocator,
    te: ast.TypeExpr,
    alloc_tracker: ?*std.ArrayListUnmanaged([]const u8),
) ?[]const u8 {
    return switch (te) {
        .simple => |name| {
            // Known primitives don't have struct names
            if (types.isPrimitiveTypeName(name)) {
                return null;
            }
            return name;
        },
        .generic => |gen| blk: {
            // Build monomorphized name: BaseType$Arg1$Arg2...
            // Calculate required size
            var size: usize = gen.base_type.len;
            for (gen.type_args) |arg| {
                size += 1 + arg.len; // '$' + arg
            }
            const buf = allocator.alloc(u8, size) catch break :blk null;
            var pos: usize = 0;
            @memcpy(buf[pos..][0..gen.base_type.len], gen.base_type);
            pos += gen.base_type.len;
            for (gen.type_args) |arg| {
                buf[pos] = '$';
                pos += 1;
                @memcpy(buf[pos..][0..arg.len], arg);
                pos += arg.len;
            }
            // Track allocation for cleanup if tracker provided
            if (alloc_tracker) |tracker| {
                tracker.append(allocator, buf) catch {};
            }
            break :blk buf;
        },
        .error_union, .function_type => null,
    };
}

/// Helper: get ValueType from TypeExpr for function parameters
/// Convert TypeExpr to ExternalValueType (uses strings, not pointers)
/// OWNERSHIP: Allocates copies of all type names for uniform ownership.
/// CLEANUP: Caller MUST free using freeExternalParamTypes()
fn getExternalValueTypeFromTypeExpr(allocator: std.mem.Allocator, te: ast.TypeExpr) types.ExternalValueType {
    return switch (te) {
        .simple => |name| blk: {
            // Known primitives are primitive types
            if (types.Primitive.fromString(name)) |p| {
                break :blk .{ .primitive = p };
            }
            // All other simple types (including String) are struct types
            // Allocate a copy for consistent ownership
            const name_copy = allocator.dupe(u8, name) catch break :blk .{ .struct_type = name };
            break :blk .{ .struct_type = name_copy };
        },
        .generic => |gen| blk: {
            // Build monomorphized name: BaseType$Arg1$Arg2...
            const name = getStructNameFromTypeExpr(allocator, .{ .generic = gen }, null) orelse break :blk .{ .struct_type = gen.base_type };
            // Array types use struct_type with "Array$" prefix
            break :blk .{ .struct_type = name };
        },
        .error_union => |eu| blk: {
            // Get primitive type if success type is a primitive (e.g., .byte)
            const success_primitive: ?types.Primitive = switch (eu.success_type.*) {
                .simple => |name| types.Primitive.fromString(name),
                else => null,
            };
            break :blk .{
                .error_union_type = .{
                    .success_type = getIrTypeFromTypeExpr(eu.success_type.*),
                    .success_primitive_type = success_primitive,
                    .success_struct_type = getStructNameFromTypeExpr(allocator, eu.success_type.*, null),
                    .error_enum_type = eu.error_type,
                },
            };
        },
        .function_type => .function_type_marker, // Function types are represented as pointers
    };
}

/// Free a slice of ExternalParamTypes, including any allocated type name strings.
/// This properly handles the allocations made by getExternalValueTypeFromTypeExpr.
pub fn freeExternalParamTypes(allocator: std.mem.Allocator, param_types: []const types.ExternalParamType) void {
    for (param_types) |pt| {
        // Free allocated type name strings
        switch (pt.ty) {
            .struct_type => |name| allocator.free(name),
            .enum_type => |name| allocator.free(name),
            .primitive, .error_union_type, .function_type_marker => {},
        }
    }
    allocator.free(param_types);
}

/// Free a slice of ParamTypes (with pointer-based ValueTypes).
/// ParamTypes point into type_map, so we don't free the type info pointers.
pub fn freeParamTypes(allocator: std.mem.Allocator, param_types: []const ParamType) void {
    // ValueType variants now use pointers into type_map, not allocated strings
    // So we just free the slice itself
    allocator.free(param_types);
}

/// Shallow-copy a slice of ParamTypes.
/// Since ParamTypes now use pointers into type_map, no deep-copying of strings is needed.
/// The caller must free the result slice with allocator.free().
pub fn dupeParamTypes(allocator: std.mem.Allocator, param_types: []const ParamType) ![]ParamType {
    return try allocator.dupe(ParamType, param_types);
}
