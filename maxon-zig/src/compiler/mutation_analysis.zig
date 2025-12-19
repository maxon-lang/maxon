const std = @import("std");
const ast = @import("ast.zig");

/// Analyzes which function parameters are mutated (assigned to) within their function body.
/// This is used for ownership/borrow checking: if a function mutates a parameter,
/// passing a value to that parameter transfers ownership (move semantics).
pub const MutationAnalyzer = struct {
    allocator: std.mem.Allocator,
    /// Map: function name -> bitset of mutated parameter indices
    function_mutations: std.StringHashMapUnmanaged(MutatedParams),

    const MutatedParams = struct {
        bits: u64, // Supports up to 64 parameters
    };

    pub fn init(allocator: std.mem.Allocator) MutationAnalyzer {
        return .{
            .allocator = allocator,
            .function_mutations = .{},
        };
    }

    pub fn deinit(self: *MutationAnalyzer) void {
        self.function_mutations.deinit(self.allocator);
    }

    /// Analyze all functions in the program to determine parameter mutations
    pub fn analyze(self: *MutationAnalyzer, program: ast.Program) !void {
        for (program.functions) |func| {
            try self.analyzeFunction(func);
        }
    }

    /// Analyze a single function for parameter mutations
    fn analyzeFunction(self: *MutationAnalyzer, func: ast.FunctionDecl) !void {
        var mutated: MutatedParams = .{ .bits = 0 };

        // Build map of parameter names to indices
        var param_indices: std.StringHashMapUnmanaged(usize) = .{};
        defer param_indices.deinit(self.allocator);

        for (func.params, 0..) |param, idx| {
            try param_indices.put(self.allocator, param.name, idx);
        }

        // Scan all statements for assignments to parameters
        for (func.body) |stmt| {
            self.checkStatementForMutation(stmt, &param_indices, &mutated);
        }

        try self.function_mutations.put(self.allocator, func.name, mutated);
    }

    fn checkStatementForMutation(
        self: *MutationAnalyzer,
        stmt: ast.Statement,
        param_indices: *std.StringHashMapUnmanaged(usize),
        mutated: *MutatedParams,
    ) void {
        switch (stmt) {
            .assign => |assign| {
                // Direct assignment to a variable - check if it's a parameter
                if (param_indices.get(assign.target)) |idx| {
                    if (idx < 64) {
                        mutated.bits |= @as(u64, 1) << @intCast(idx);
                    }
                }
            },
            .index_assign => |idx_assign| {
                // Array index assignment - check if base is a parameter
                self.checkExpressionForParamMutation(idx_assign.base.*, param_indices, mutated);
            },
            .var_decl, .let_decl => {
                // Variable declarations don't mutate existing parameters
                // (they may shadow them, but that's a different concern)
            },
            .@"return" => {
                // Return statements don't mutate parameters
            },
            .call => {
                // Standalone call statements don't directly mutate parameters
                // (mutations happen inside the called function)
            },
            .field_assign => |assign| {
                // Field assignment - check if base is a parameter (e.g., d.value = 100)
                self.checkExpressionForParamMutation(assign.base.*, param_indices, mutated);
            },
        }
    }

    fn checkExpressionForParamMutation(
        self: *MutationAnalyzer,
        expr: ast.Expression,
        param_indices: *std.StringHashMapUnmanaged(usize),
        mutated: *MutatedParams,
    ) void {
        switch (expr) {
            .identifier => |name| {
                // Direct reference to a parameter being mutated
                if (param_indices.get(name)) |idx| {
                    if (idx < 64) {
                        mutated.bits |= @as(u64, 1) << @intCast(idx);
                    }
                }
            },
            .field_access => |fa| {
                // Field access mutation also mutates the base struct
                // Recursively check if the ultimate base is a parameter
                self.checkExpressionForParamMutation(fa.base.*, param_indices, mutated);
            },
            .index => |idx| {
                // Index access mutation also mutates the base array
                self.checkExpressionForParamMutation(idx.base.*, param_indices, mutated);
            },
            // integer, float_lit, binary, call, struct_init, array_literal, sized_array:
            // These cannot be mutation targets (only identifier, field_access, index can)
            .integer, .float_lit, .binary, .call, .struct_init, .array_literal, .sized_array => {},
        }
    }

    /// Query if a function mutates a specific parameter
    pub fn doesMutateParam(self: *const MutationAnalyzer, func_name: []const u8, param_idx: usize) bool {
        if (self.function_mutations.get(func_name)) |mutated| {
            if (param_idx < 64) {
                return (mutated.bits & (@as(u64, 1) << @intCast(param_idx))) != 0;
            }
        }
        // Unknown function or param index out of range - assume no mutation (borrow)
        return false;
    }
};
