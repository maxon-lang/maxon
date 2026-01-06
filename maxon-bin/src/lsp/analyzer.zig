const std = @import("std");
const compiler = @import("../compiler/0-compiler.zig");
const ast_to_ir = @import("../compiler/4-ast_to_ir.zig");
const ast = @import("../compiler/ast.zig");
const intrinsics_registry = @import("../compiler/intrinsics_registry.zig");
const types = @import("types.zig");

fn log(comptime fmt: []const u8, args: anytype) void {
    std.debug.print("[LSP:Analyzer] " ++ fmt ++ "\n", args);
}

pub const Document = struct {
    uri: []const u8,
    content: []const u8,
    version: i64,

    pub fn deinit(self: *Document, allocator: std.mem.Allocator) void {
        allocator.free(self.uri);
        allocator.free(self.content);
    }
};

pub const Analyzer = struct {
    allocator: std.mem.Allocator,
    documents: std.StringHashMapUnmanaged(Document) = .{},
    semantic_cache: std.StringHashMapUnmanaged(ast_to_ir.SemanticInfo) = .{},

    pub fn init(allocator: std.mem.Allocator) Analyzer {
        return .{ .allocator = allocator };
    }

    pub fn deinit(self: *Analyzer) void {
        var doc_it = self.documents.iterator();
        while (doc_it.next()) |entry| {
            var doc = entry.value_ptr.*;
            doc.deinit(self.allocator);
        }
        self.documents.deinit(self.allocator);

        var cache_it = self.semantic_cache.iterator();
        while (cache_it.next()) |entry| {
            var info = entry.value_ptr.*;
            info.deinit();
        }
        self.semantic_cache.deinit(self.allocator);
    }

    pub fn openDocument(self: *Analyzer, uri: []const u8, content: []const u8, version: i64) !void {
        const uri_copy = try self.allocator.dupe(u8, uri);
        errdefer self.allocator.free(uri_copy);

        const content_copy = try self.allocator.dupe(u8, content);
        errdefer self.allocator.free(content_copy);

        const doc = Document{ .uri = uri_copy, .content = content_copy, .version = version };
        try self.documents.put(self.allocator, uri_copy, doc);

        // Eagerly analyze to populate cache
        _ = self.getSemanticInfo(uri_copy) catch |err| {
            log("Semantic analysis failed: {}", .{err});
        };
    }

    pub fn updateDocument(self: *Analyzer, uri: []const u8, content: []const u8, version: i64) !void {
        if (self.documents.getPtr(uri)) |doc| {
            // Store old content in case analysis fails
            const old_content = doc.content;
            doc.content = try self.allocator.dupe(u8, content);
            doc.version = version;

            // Try to re-analyze - if successful, invalidate old cache
            const new_info = compiler.analyzeForLSP(doc.content, null, self.allocator) catch |err| {
                log("Semantic analysis failed: {} (keeping previous cache)", .{err});
                // Keep old cache, just free old content
                self.allocator.free(old_content);
                return;
            };

            // Analysis succeeded - replace old cache with new
            if (self.semantic_cache.fetchRemove(uri)) |kv| {
                var old_info = kv.value;
                old_info.deinit();
            }
            self.semantic_cache.put(self.allocator, uri, new_info) catch {
                // If we fail to cache the new info, clean it up
                var info_to_free = new_info;
                info_to_free.deinit();
            };
            self.allocator.free(old_content);
        }
    }

    pub fn closeDocument(self: *Analyzer, uri: []const u8) void {
        // Remove from semantic cache
        if (self.semantic_cache.fetchRemove(uri)) |kv| {
            var info = kv.value;
            info.deinit();
        }

        if (self.documents.fetchRemove(uri)) |kv| {
            var doc = kv.value;
            doc.deinit(self.allocator);
        }
    }

    fn getSemanticInfo(self: *Analyzer, uri: []const u8) !*ast_to_ir.SemanticInfo {
        if (self.semantic_cache.getPtr(uri)) |cached| {
            return cached;
        }

        const doc = self.documents.get(uri) orelse return error.DocumentNotFound;
        log("Running semantic analysis for {s}...", .{uri});

        var info = compiler.analyzeForLSP(doc.content, null, self.allocator) catch |err| {
            log("Semantic analysis error: {}", .{err});
            return err;
        };
        errdefer info.deinit();

        try self.semantic_cache.put(self.allocator, uri, info);
        log("Semantic analysis complete: {d} variables", .{info.variables.len});
        return self.semantic_cache.getPtr(uri).?;
    }

    pub fn inferTypeAtPosition(self: *Analyzer, doc_uri: []const u8, _: u32, _: u32, var_name: []const u8) ?[]const u8 {
        const info = self.getSemanticInfo(doc_uri) catch return null;

        // Find the variable by name
        if (info.findVariable(var_name)) |v| {
            return v.ty.getTypeName() orelse v.ty.toDisplayName();
        }
        return null;
    }

    pub fn findType(self: *Analyzer, doc_uri: []const u8, type_name: []const u8) ?ast_to_ir.TypeInfo {
        const info = self.getSemanticInfo(doc_uri) catch return null;
        return info.getType(type_name);
    }

    pub fn getCompletionsForVariable(self: *Analyzer, doc_uri: []const u8, var_name: []const u8) ![]types.CompletionItem {
        const info = self.getSemanticInfo(doc_uri) catch return &.{};
        const v = info.findVariable(var_name) orelse return &.{};
        return self.getCompletionsForType(info, v.ty);
    }

    fn getCompletionsForType(self: *Analyzer, info: *ast_to_ir.SemanticInfo, ty: ast_to_ir.ValueType) ![]types.CompletionItem {
        var items: std.ArrayListUnmanaged(types.CompletionItem) = .empty;

        // Get type name for method lookup
        // For arrays, build monomorphized name like "Array$int"
        var type_name_buf: [256]u8 = undefined;
        const type_name: []const u8 = switch (ty) {
            .primitive => |name| name,
            .struct_type => |name| name,
            .enum_type => |name| name,
            .array_type => |arr| blk: {
                // Build monomorphized array type name
                // Use Maxon type names (int, float) not IR type names (i64, f64)
                const elem_name = arr.element_struct_type orelse switch (arr.element_type) {
                    .i64 => "int",
                    .i32 => "int",
                    .i8 => "byte",
                    .f64 => "float",
                    .ptr => "ptr",
                    .void => "void",
                };
                break :blk std.fmt.bufPrint(&type_name_buf, "Array${s}", .{elem_name}) catch "Array";
            },
            .optional_type => return &.{}, // Optionals don't have methods
            .error_union_type => return &.{},
            .function_type => return &.{},
        };

        // Get fields for struct types
        if (info.getType(type_name)) |type_info| {
            switch (type_info) {
                .struct_type => |st| {
                    for (st.fields) |field| {
                        // Skip internal fields (starting with _ or __)
                        if (!std.mem.startsWith(u8, field.name, "__") and !std.mem.startsWith(u8, field.name, "_")) {
                            try items.append(self.allocator, .{
                                .label = field.name,
                                .kind = .field,
                                .detail = field.value_type.toDisplayName(),
                            });
                        }
                    }
                },
                else => {},
            }
        }

        // Look up methods from func_map (methods are registered as "Type$method")
        var prefix_buf: [256]u8 = undefined;
        const prefix = std.fmt.bufPrint(&prefix_buf, "{s}$", .{type_name}) catch return items.toOwnedSlice(self.allocator);

        var func_iter = info.functions.iterator();
        while (func_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            if (std.mem.startsWith(u8, name, prefix)) {
                const method_name = name[prefix.len..];
                try items.append(self.allocator, .{
                    .label = method_name,
                    .kind = .method,
                    .detail = formatFuncSignature(entry.value_ptr.*),
                });
            }
        }

        // Return empty literal if no items found, otherwise return allocated slice
        if (items.items.len == 0) {
            items.deinit(self.allocator);
            return &.{};
        }
        return items.toOwnedSlice(self.allocator);
    }

    /// Get hover information for the symbol at the given position
    /// Returns allocated markdown string that caller must free, or null if no hover info
    pub fn getHoverInfo(self: *Analyzer, uri: []const u8, line: u32, character: u32) ?[]const u8 {
        const doc = self.documents.get(uri) orelse return null;
        const word = getWordAtPosition(doc.content, line, character) orelse return null;
        const info = self.getSemanticInfo(uri) catch return null;

        // 1. Check for intrinsics from registry (includes public builtins like sqrt, trunc)
        if (intrinsics_registry.findIntrinsic(word)) |intr| {
            return self.formatRegistryIntrinsicHover(intr);
        }

        // 2. Check for variables (local vars and parameters)
        if (info.findVariable(word)) |v| {
            return self.formatVariableHover(v, info, line);
        }

        // 3. Check for function calls
        if (info.functions.get(word)) |func_info| {
            return self.formatFunctionHover(word, func_info);
        }

        // 4. Check for type references
        if (info.types.get(word)) |type_info| {
            return self.formatTypeHover(word, type_info);
        }

        return null;
    }

    fn formatVariableHover(self: *Analyzer, v: ast_to_ir.SemanticVarInfo, info: *ast_to_ir.SemanticInfo, hover_line: u32) ?[]const u8 {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        // For immutable variables (let), show value if on declaration line
        if (!v.is_mutable and v.decl_line > 0 and v.decl_line - 1 == hover_line) {
            if (info.program) |program| {
                if (findLetDeclExpression(program, v.name, v.decl_line)) |expr| {
                    writer.writeAll("```maxon\nlet ") catch return null;
                    writer.writeAll(v.name) catch return null;
                    writer.writeAll(" = ") catch return null;
                    formatExpression(writer, expr) catch return null;
                    writer.writeAll("\n```") catch return null;
                    return buffer.toOwnedSlice(self.allocator) catch null;
                }
            }
        }

        // Show type info
        const type_name = v.ty.toDisplayName();
        const mutability = if (v.is_mutable) "var" else "let";
        const kind = if (v.is_parameter) "(parameter)" else "(local)";

        writer.print("```maxon\n{s} {s}: {s}\n```\n{s}", .{ mutability, v.name, type_name, kind }) catch return null;

        return buffer.toOwnedSlice(self.allocator) catch null;
    }

    fn formatFunctionHover(self: *Analyzer, name: []const u8, func: ast_to_ir.FuncInfo) ?[]const u8 {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        writer.writeAll("```maxon\nfunction ") catch return null;
        writer.writeAll(name) catch return null;
        writer.writeByte('(') catch return null;

        for (func.param_types, 0..) |param, i| {
            if (i > 0) writer.writeAll(", ") catch return null;
            writer.writeAll(param.name) catch return null;
            writer.writeByte(' ') catch return null;
            writer.writeAll(param.ty.toDisplayName()) catch return null;
        }

        writer.writeByte(')') catch return null;

        // Add return type if not void
        if (func.return_type_name) |rt| {
            writer.writeAll(" returns ") catch return null;
            writer.writeAll(rt) catch return null;
        } else if (func.return_type != .void) {
            writer.writeAll(" returns ") catch return null;
            writer.writeAll(func.return_type.toMaxonName()) catch return null;
        }

        writer.writeAll("\n```") catch return null;
        return buffer.toOwnedSlice(self.allocator) catch null;
    }

    fn formatIntrinsicHover(self: *Analyzer, name: []const u8, func: ast_to_ir.FuncInfo) ?[]const u8 {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        writer.writeAll("```maxon\nintrinsic ") catch return null;
        writer.writeAll(name) catch return null;
        writer.writeByte('(') catch return null;

        for (func.param_types, 0..) |param, i| {
            if (i > 0) writer.writeAll(", ") catch return null;
            writer.writeAll(param.ty.toDisplayName()) catch return null;
        }

        writer.writeAll(")\n```") catch return null;
        return buffer.toOwnedSlice(self.allocator) catch null;
    }

    fn formatRegistryIntrinsicHover(self: *Analyzer, intr: *const intrinsics_registry.Intrinsic) ?[]const u8 {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        // Format signature - use "intrinsic" for stdlib-only, "function" for public
        const keyword = if (intr.visibility == .stdlib_only) "intrinsic" else "function";
        writer.writeAll("```maxon\n") catch return null;
        writer.writeAll(keyword) catch return null;
        writer.writeByte(' ') catch return null;
        writer.writeAll(intr.name) catch return null;
        writer.writeByte('(') catch return null;

        for (intr.params, 0..) |param, i| {
            if (i > 0) writer.writeAll(", ") catch return null;
            writer.writeAll(param.name) catch return null;
            writer.writeByte(' ') catch return null;
            writer.writeAll(param.type_name) catch return null;
        }

        writer.writeAll(") returns ") catch return null;
        writer.writeAll(intr.return_type_name) catch return null;
        writer.writeAll("\n```") catch return null;

        // Add help text if available
        if (intr.help_text.len > 0) {
            writer.writeAll("\n\n") catch return null;
            writer.writeAll(intr.help_text) catch return null;
        }

        return buffer.toOwnedSlice(self.allocator) catch null;
    }

    fn formatTypeHover(self: *Analyzer, name: []const u8, type_info: ast_to_ir.TypeInfo) ?[]const u8 {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        switch (type_info) {
            .struct_type => |st| {
                writer.print("```maxon\ntype {s}\n```\n\nStruct with {d} fields", .{ name, st.fields.len }) catch return null;
            },
            .enum_type => |et| {
                writer.print("```maxon\nenum {s}\n```\n\nEnum with {d} cases", .{ name, et.members.count() }) catch return null;
            },
            .primitive => {
                writer.print("```maxon\n{s}\n```\n\nPrimitive type", .{name}) catch return null;
            },
        }

        return buffer.toOwnedSlice(self.allocator) catch null;
    }

    /// Result of a definition lookup
    pub const DefinitionLocation = struct {
        uri: []const u8,
        line: u32,
        character: u32,
    };

    /// Find the definition of the symbol at the given position
    pub fn findDefinition(self: *Analyzer, uri: []const u8, line: u32, character: u32) ?DefinitionLocation {
        const doc = self.documents.get(uri) orelse return null;

        // Get the identifier at position
        const word = getWordAtPosition(doc.content, line, character) orelse return null;

        // Get semantic info for AST-based lookups
        const info = self.getSemanticInfo(uri) catch return null;

        // 1. Check if it's a variable reference
        if (info.findVariable(word)) |v| {
            // Return definition location (convert from 1-based to 0-based)
            return .{
                .uri = uri,
                .line = if (v.decl_line > 0) v.decl_line - 1 else 0,
                .character = if (v.decl_column > 0) v.decl_column - 1 else 0,
            };
        }

        // 2. Check if it's a function call - use AST-based lookup
        if (info.functions.get(word)) |func_info| {
            if (func_info.decl_line > 0) {
                return .{
                    .uri = uri,
                    .line = func_info.decl_line - 1,
                    .character = if (func_info.decl_column > 0) func_info.decl_column - 1 else 0,
                };
            }
        }

        // 3. Check if it's a type reference - use AST-based lookup
        if (info.types.get(word)) |type_info| {
            switch (type_info) {
                .struct_type => |st| {
                    if (st.decl_line > 0) {
                        return .{
                            .uri = uri,
                            .line = st.decl_line - 1,
                            .character = if (st.decl_column > 0) st.decl_column - 1 else 0,
                        };
                    }
                },
                else => {},
            }
        }

        return null;
    }

    pub fn getMemberCompletions(self: *Analyzer, doc_uri: []const u8, type_name: []const u8) ![]types.CompletionItem {
        const info = self.getSemanticInfo(doc_uri) catch return &.{};

        const type_info = info.getType(type_name) orelse {
            log("Type not found: {s}", .{type_name});
            return &.{};
        };

        var items: std.ArrayListUnmanaged(types.CompletionItem) = .empty;

        switch (type_info) {
            .struct_type => |st| {
                for (st.fields) |field| {
                    // Skip internal fields (starting with _ or __)
                    if (!std.mem.startsWith(u8, field.name, "__") and !std.mem.startsWith(u8, field.name, "_")) {
                        try items.append(self.allocator, .{
                            .label = field.name,
                            .kind = .field,
                            .detail = field.value_type.toDisplayName(),
                        });
                    }
                }
            },
            else => {},
        }

        // Look up methods from func_map (methods are registered as "Type$method")
        var prefix_buf: [256]u8 = undefined;
        const prefix = std.fmt.bufPrint(&prefix_buf, "{s}$", .{type_name}) catch return items.toOwnedSlice(self.allocator);

        var func_iter = info.functions.iterator();
        while (func_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            if (std.mem.startsWith(u8, name, prefix)) {
                const method_name = name[prefix.len..];
                try items.append(self.allocator, .{
                    .label = method_name,
                    .kind = .method,
                    .detail = formatFuncSignature(entry.value_ptr.*),
                });
            }
        }

        // Return empty literal if no items found, otherwise return allocated slice
        if (items.items.len == 0) {
            items.deinit(self.allocator);
            return &.{};
        }
        return items.toOwnedSlice(self.allocator);
    }

    /// Get all document symbols (functions, types, etc.) for outline view
    pub fn getDocumentSymbols(self: *Analyzer, uri: []const u8) ![]types.SymbolInformation {
        const doc = self.documents.get(uri) orelse return &.{};
        const info = self.getSemanticInfo(uri) catch return &.{};

        var symbols: std.ArrayListUnmanaged(types.SymbolInformation) = .empty;
        errdefer symbols.deinit(self.allocator);

        // Add functions
        var func_iter = info.functions.iterator();
        while (func_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            const func_info = entry.value_ptr.*;

            // Skip methods (contain $) and intrinsics (start with __)
            if (std.mem.indexOf(u8, name, "$") != null) continue;
            if (std.mem.startsWith(u8, name, "__")) continue;

            // Only include if it has valid location
            if (func_info.decl_line > 0) {
                try symbols.append(self.allocator, .{
                    .name = name,
                    .kind = .function,
                    .location = .{
                        .uri = uri,
                        .range = .{
                            .start = .{
                                .line = func_info.decl_line - 1,
                                .character = if (func_info.decl_column > 0) func_info.decl_column - 1 else 0,
                            },
                            .end = .{
                                .line = func_info.decl_line - 1,
                                .character = if (func_info.decl_column > 0) func_info.decl_column - 1 else 0,
                            },
                        },
                    },
                });
            }
        }

        // Add types (structs)
        var type_iter = info.types.iterator();
        while (type_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            const type_info = entry.value_ptr.*;

            // Skip internal types and monomorphized types (contain $)
            if (std.mem.indexOf(u8, name, "$") != null) continue;
            if (std.mem.startsWith(u8, name, "__")) continue;

            switch (type_info) {
                .struct_type => |st| {
                    if (st.decl_line > 0) {
                        try symbols.append(self.allocator, .{
                            .name = name,
                            .kind = .@"struct",
                            .location = .{
                                .uri = uri,
                                .range = .{
                                    .start = .{
                                        .line = st.decl_line - 1,
                                        .character = if (st.decl_column > 0) st.decl_column - 1 else 0,
                                    },
                                    .end = .{
                                        .line = st.decl_line - 1,
                                        .character = if (st.decl_column > 0) st.decl_column - 1 else 0,
                                    },
                                },
                            },
                        });
                    }
                },
                .enum_type => |et| {
                    if (et.decl_line > 0) {
                        try symbols.append(self.allocator, .{
                            .name = name,
                            .kind = .@"enum",
                            .location = .{
                                .uri = uri,
                                .range = .{
                                    .start = .{
                                        .line = et.decl_line - 1,
                                        .character = if (et.decl_column > 0) et.decl_column - 1 else 0,
                                    },
                                    .end = .{
                                        .line = et.decl_line - 1,
                                        .character = if (et.decl_column > 0) et.decl_column - 1 else 0,
                                    },
                                },
                            },
                        });
                    }
                },
                else => {},
            }
        }

        // Add interfaces
        var intf_iter = info.interfaces.iterator();
        while (intf_iter.next()) |entry| {
            const name = entry.key_ptr.*;
            const intf_info = entry.value_ptr.*;

            if (intf_info.decl_line > 0) {
                try symbols.append(self.allocator, .{
                    .name = name,
                    .kind = .interface,
                    .location = .{
                        .uri = uri,
                        .range = .{
                            .start = .{
                                .line = intf_info.decl_line - 1,
                                .character = if (intf_info.decl_column > 0) intf_info.decl_column - 1 else 0,
                            },
                            .end = .{
                                .line = intf_info.decl_line - 1,
                                .character = if (intf_info.decl_column > 0) intf_info.decl_column - 1 else 0,
                            },
                        },
                    },
                });
            }
        }

        // Use a text-based fallback if no symbols from semantic info
        if (symbols.items.len == 0) {
            // Search for functions and types in document
            try self.findSymbolsInDocument(doc.content, uri, &symbols);
        }

        if (symbols.items.len == 0) {
            return &.{};
        }
        return symbols.toOwnedSlice(self.allocator);
    }

    fn findSymbolsInDocument(self: *Analyzer, content: []const u8, uri: []const u8, symbols: *std.ArrayListUnmanaged(types.SymbolInformation)) !void {
        var lines = std.mem.splitScalar(u8, content, '\n');
        var line_num: u32 = 0;

        while (lines.next()) |line_content| : (line_num += 1) {
            const trimmed = std.mem.trimLeft(u8, line_content, " \t");

            // Check for function declarations
            if (std.mem.startsWith(u8, trimmed, "function ") or std.mem.startsWith(u8, trimmed, "export function ")) {
                const func_start = if (std.mem.startsWith(u8, trimmed, "export function "))
                    trimmed["export function ".len..]
                else
                    trimmed["function ".len..];

                const paren_pos = std.mem.indexOf(u8, func_start, "(");
                const func_name = if (paren_pos) |p| func_start[0..p] else func_start;
                const func_name_trimmed = std.mem.trim(u8, func_name, " \t");

                if (func_name_trimmed.len > 0) {
                    const keyword_pos = std.mem.indexOf(u8, line_content, "function ") orelse 0;
                    try symbols.append(self.allocator, .{
                        .name = func_name_trimmed,
                        .kind = .function,
                        .location = .{
                            .uri = uri,
                            .range = .{
                                .start = .{ .line = line_num, .character = @intCast(keyword_pos + "function ".len) },
                                .end = .{ .line = line_num, .character = @intCast(keyword_pos + "function ".len + func_name_trimmed.len) },
                            },
                        },
                    });
                }
            }
            // Check for type declarations
            else if (std.mem.startsWith(u8, trimmed, "type ") or std.mem.startsWith(u8, trimmed, "export type ")) {
                const type_start = if (std.mem.startsWith(u8, trimmed, "export type "))
                    trimmed["export type ".len..]
                else
                    trimmed["type ".len..];

                var end: usize = 0;
                while (end < type_start.len and (std.ascii.isAlphanumeric(type_start[end]) or type_start[end] == '_')) {
                    end += 1;
                }

                if (end > 0) {
                    const type_name = type_start[0..end];
                    const keyword_pos = if (std.mem.indexOf(u8, line_content, "export type ")) |pos|
                        pos + "export type ".len
                    else if (std.mem.indexOf(u8, line_content, "type ")) |pos|
                        pos + "type ".len
                    else
                        0;

                    try symbols.append(self.allocator, .{
                        .name = type_name,
                        .kind = .@"struct",
                        .location = .{
                            .uri = uri,
                            .range = .{
                                .start = .{ .line = line_num, .character = @intCast(keyword_pos) },
                                .end = .{ .line = line_num, .character = @intCast(keyword_pos + type_name.len) },
                            },
                        },
                    });
                }
            }
        }
    }

    /// Get a document by URI
    pub fn getDocument(self: *Analyzer, uri: []const u8) ?Document {
        return self.documents.get(uri);
    }

    /// Format a document and return the formatted text
    pub fn formatDocument(self: *Analyzer, uri: []const u8, tab_size: u32, insert_spaces: bool) ?[]const u8 {
        const doc = self.documents.get(uri) orelse return null;
        const info = self.semantic_cache.get(uri);
        return formatContent(self.allocator, doc.content, tab_size, insert_spaces, info) catch null;
    }

    /// Get folding ranges for a document
    pub fn getFoldingRanges(self: *Analyzer, uri: []const u8) ![]types.FoldingRange {
        _ = self.documents.get(uri) orelse return &.{};

        // Use AST-based folding if available
        if (self.semantic_cache.get(uri)) |info| {
            if (info.program) |program| {
                return collectFoldingRanges(self.allocator, program);
            }
        }

        return &.{};
    }

    /// Get linked editing ranges for a position in a document
    pub fn getLinkedEditingRanges(self: *Analyzer, uri: []const u8, line: u32, character: u32) ?types.LinkedEditingRanges {
        const doc = self.documents.get(uri) orelse return null;
        const info = self.semantic_cache.get(uri);
        return findLinkedEditingRanges(self.allocator, doc.content, line, character, info) catch null;
    }
};

// ============================================================================
// Formatting Implementation
// ============================================================================

/// Format document content with proper indentation
fn formatContent(allocator: std.mem.Allocator, content: []const u8, tab_size: u32, insert_spaces: bool, info: ?ast_to_ir.SemanticInfo) ![]const u8 {
    var result: std.ArrayListUnmanaged(u8) = .empty;
    errdefer result.deinit(allocator);

    // Create indent string based on options
    var indent_buf: [16]u8 = undefined;
    const indent_str: []const u8 = if (insert_spaces) blk: {
        const size = @min(tab_size, indent_buf.len);
        @memset(indent_buf[0..size], ' ');
        break :blk indent_buf[0..size];
    } else "\t";

    // Collect all lines first
    var all_lines: std.ArrayListUnmanaged([]const u8) = .empty;
    defer all_lines.deinit(allocator);

    var lines = std.mem.splitScalar(u8, content, '\n');
    while (lines.next()) |line| {
        try all_lines.append(allocator, line);
    }

    // Get program from semantic info if available
    const program = if (info) |semantic_info| semantic_info.program else null;
    if (program == null) return try allocator.dupe(u8, content);

    var first_line = true;

    for (all_lines.items, 0..) |line, line_idx| {
        const trimmed = std.mem.trim(u8, line, " \t\r");
        // Line numbers are 1-based in AST
        const ast_line: u32 = @intCast(line_idx + 1);

        // Add newline before all lines except the first
        if (!first_line) {
            try result.append(allocator, '\n');
        }
        first_line = false;

        // Empty lines - just leave empty (preserves blank lines)
        if (trimmed.len == 0) {
            continue;
        }

        // Calculate indentation depth directly from AST
        const depth = calculateIndentDepth(program.?, ast_line);

        // Add indentation
        var i: u32 = 0;
        while (i < depth) : (i += 1) {
            try result.appendSlice(allocator, indent_str);
        }

        // Add the trimmed content
        try result.appendSlice(allocator, trimmed);
    }

    return result.toOwnedSlice(allocator);
}

// ============================================================================
// Linked Editing Range Implementation
// ============================================================================

/// Find linked editing ranges at a position
fn findLinkedEditingRanges(allocator: std.mem.Allocator, content: []const u8, line: u32, character: u32, info: ?ast_to_ir.SemanticInfo) !?types.LinkedEditingRanges {
    // Get all lines
    var lines_list: std.ArrayListUnmanaged([]const u8) = .empty;
    defer lines_list.deinit(allocator);

    var lines_iter = std.mem.splitScalar(u8, content, '\n');
    while (lines_iter.next()) |l| {
        try lines_list.append(allocator, l);
    }

    if (line >= lines_list.items.len) return null;

    const current_line = lines_list.items[line];

    // Get the word at the cursor position
    const word = getWordAtPositionInLine(current_line, character) orelse return null;

    // Determine context: are we in a function name, block label, or method name?
    var result_ranges: std.ArrayListUnmanaged(types.Range) = .empty;
    errdefer result_ranges.deinit(allocator);

    const trimmed_current = std.mem.trim(u8, current_line, " \t\r");

    // Case 1: Check if we're on a function declaration line
    if (std.mem.startsWith(u8, trimmed_current, "function ") or std.mem.startsWith(u8, trimmed_current, "export function ")) {
        // Extract function name
        const func_start_idx = std.mem.indexOf(u8, trimmed_current, "function ").? + "function ".len;
        const func_name_end = std.mem.indexOfAny(u8, trimmed_current[func_start_idx..], "(") orelse (trimmed_current.len - func_start_idx);
        const func_name = std.mem.trim(u8, trimmed_current[func_start_idx..][0..func_name_end], " \t");

        if (std.mem.eql(u8, word, func_name)) {
            // Find function name position in original line
            const name_start = std.mem.indexOf(u8, current_line, func_name).?;
            try result_ranges.append(allocator, .{
                .start = .{ .line = line, .character = @intCast(name_start) },
                .end = .{ .line = line, .character = @intCast(name_start + func_name.len) },
            });

            // Find the matching end label using AST
            if (info) |semantic_info| {
                if (semantic_info.program) |program| {
                    // AST uses 1-based lines
                    if (findFunctionEndLine(program, line + 1, func_name)) |end_line| {
                        const end_line_0 = end_line - 1;
                        if (end_line_0 < lines_list.items.len) {
                            const end_line_content = lines_list.items[end_line_0];
                            if (std.mem.indexOf(u8, end_line_content, func_name)) |end_name_pos| {
                                try result_ranges.append(allocator, .{
                                    .start = .{ .line = end_line_0, .character = @intCast(end_name_pos) },
                                    .end = .{ .line = end_line_0, .character = @intCast(end_name_pos + func_name.len) },
                                });
                            }
                        }
                    }
                }
            }
        }
    }

    // Case 2: Check if we're in a block label (like 'loop' at end of for/if/etc.)
    if (result_ranges.items.len == 0) {
        // Check if cursor is in a label (inside quotes at end of line)
        if (findLabelAtPosition(current_line, character)) |label_info| {
            try result_ranges.append(allocator, .{
                .start = .{ .line = line, .character = @intCast(label_info.start) },
                .end = .{ .line = line, .character = @intCast(label_info.end) },
            });

            // Find the matching start/end label using AST
            if (info) |semantic_info| {
                if (semantic_info.program) |program| {
                    try findMatchingLabelFromAST(allocator, lines_list.items, line, label_info.label, program, &result_ranges);
                }
            }
        }
    }

    // Case 3: Check if we're in a method implementation (Interface.method)
    if (result_ranges.items.len == 0) {
        if (std.mem.indexOf(u8, trimmed_current, "function ")) |func_idx| {
            const after_func = trimmed_current[func_idx + "function ".len ..];
            if (std.mem.indexOf(u8, after_func, ".")) |dot_idx| {
                const method_start = dot_idx + 1;
                const method_end = std.mem.indexOfAny(u8, after_func[method_start..], "(") orelse (after_func.len - method_start);
                const method_name = after_func[method_start..][0..method_end];

                if (std.mem.eql(u8, word, method_name)) {
                    // Find method name position in trimmed then convert to original
                    const pos_in_trimmed = func_idx + "function ".len + method_start;
                    const pos_in_line = findPositionInOriginal(current_line, trimmed_current, pos_in_trimmed);

                    try result_ranges.append(allocator, .{
                        .start = .{ .line = line, .character = @intCast(pos_in_line) },
                        .end = .{ .line = line, .character = @intCast(pos_in_line + method_name.len) },
                    });

                    // Find matching end label using AST
                    if (info) |semantic_info| {
                        if (semantic_info.program) |program| {
                            // AST uses 1-based lines
                            if (findFunctionEndLine(program, line + 1, method_name)) |end_line| {
                                const end_line_0 = end_line - 1;
                                if (end_line_0 < lines_list.items.len) {
                                    const end_line_content = lines_list.items[end_line_0];
                                    if (std.mem.indexOf(u8, end_line_content, method_name)) |end_name_pos| {
                                        try result_ranges.append(allocator, .{
                                            .start = .{ .line = end_line_0, .character = @intCast(end_name_pos) },
                                            .end = .{ .line = end_line_0, .character = @intCast(end_name_pos + method_name.len) },
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    if (result_ranges.items.len < 2) {
        result_ranges.deinit(allocator);
        return null;
    }

    return types.LinkedEditingRanges{
        .ranges = try result_ranges.toOwnedSlice(allocator),
    };
}

fn getWordAtPositionInLine(line: []const u8, character: u32) ?[]const u8 {
    if (character >= line.len) return null;

    var start: usize = character;
    while (start > 0) {
        const c = line[start - 1];
        if (!std.ascii.isAlphanumeric(c) and c != '_') break;
        start -= 1;
    }

    var end: usize = character;
    while (end < line.len) {
        const c = line[end];
        if (!std.ascii.isAlphanumeric(c) and c != '_') break;
        end += 1;
    }

    if (start < end) {
        return line[start..end];
    }
    return null;
}

const LabelInfo = struct {
    label: []const u8,
    start: usize,
    end: usize,
};

fn findLabelAtPosition(line: []const u8, character: u32) ?LabelInfo {
    // Look for 'label' pattern - find quote before and after cursor
    var idx: usize = 0;
    while (idx < line.len) {
        if (line[idx] == '\'') {
            const label_start = idx + 1;
            // Find closing quote
            var end_idx = label_start;
            while (end_idx < line.len and line[end_idx] != '\'') {
                end_idx += 1;
            }
            if (end_idx < line.len and end_idx > label_start) {
                // Check if cursor is within this label
                if (character >= label_start and character <= end_idx) {
                    return .{
                        .label = line[label_start..end_idx],
                        .start = label_start,
                        .end = end_idx,
                    };
                }
            }
            idx = end_idx + 1;
        } else {
            idx += 1;
        }
    }
    return null;
}

/// Find matching label using the AST (with end_line information)
fn findMatchingLabelFromAST(allocator: std.mem.Allocator, lines: []const []const u8, current_line: u32, label: []const u8, program: ast.Program, ranges: *std.ArrayListUnmanaged(types.Range)) !void {
    // AST line numbers are 1-based, current_line is 0-based
    const ast_line = current_line + 1;

    // Check if current line is an "end" line - search for block start
    const trimmed_current = std.mem.trim(u8, lines[current_line], " \t\r");

    if (std.mem.startsWith(u8, trimmed_current, "end ")) {
        // We're on an end line - find the block that ends on this line
        if (findBlockStartLineForEnd(program, ast_line, label)) |start_line| {
            // Add the start label position (convert to 0-based)
            const start_line_0 = start_line - 1;
            if (start_line_0 < lines.len) {
                const orig_line = lines[start_line_0];
                if (std.mem.indexOf(u8, orig_line, "'")) |quote_pos| {
                    try ranges.append(allocator, .{
                        .start = .{ .line = start_line_0, .character = @intCast(quote_pos + 1) },
                        .end = .{ .line = start_line_0, .character = @intCast(quote_pos + 1 + label.len) },
                    });
                }
            }
        }
    } else {
        // We're on a block start - find the end line from AST
        if (findBlockEndLineForStart(program, ast_line, label)) |end_line| {
            // Add the end label position (convert to 0-based)
            const end_line_0 = end_line - 1;
            if (end_line_0 < lines.len) {
                const orig_line = lines[end_line_0];
                if (std.mem.indexOf(u8, orig_line, "'")) |quote_pos| {
                    try ranges.append(allocator, .{
                        .start = .{ .line = end_line_0, .character = @intCast(quote_pos + 1) },
                        .end = .{ .line = end_line_0, .character = @intCast(quote_pos + 1 + label.len) },
                    });
                }
            }
        }
    }
}

/// Collect folding ranges directly from AST (converts to 0-based LSP lines)
fn collectFoldingRanges(allocator: std.mem.Allocator, program: ast.Program) ![]types.FoldingRange {
    var ranges: std.ArrayListUnmanaged(types.FoldingRange) = .empty;
    errdefer ranges.deinit(allocator);

    // Collect from types
    for (program.types) |type_decl| {
        if (type_decl.block.end_line > 0) {
            try ranges.append(allocator, .{
                .startLine = type_decl.block.start_line - 1,
                .endLine = type_decl.block.end_line - 1,
            });
        }
        for (type_decl.methods) |method| {
            try collectFoldingFromMethod(allocator, method, &ranges);
        }
    }

    // Collect from enums
    for (program.enums) |enum_decl| {
        if (enum_decl.block.end_line > 0) {
            try ranges.append(allocator, .{
                .startLine = enum_decl.block.start_line - 1,
                .endLine = enum_decl.block.end_line - 1,
            });
        }
        for (enum_decl.methods) |method| {
            try collectFoldingFromMethod(allocator, method, &ranges);
        }
    }

    // Collect from interfaces
    for (program.interfaces) |interface_decl| {
        if (interface_decl.block.end_line > 0) {
            try ranges.append(allocator, .{
                .startLine = interface_decl.block.start_line - 1,
                .endLine = interface_decl.block.end_line - 1,
            });
        }
    }

    // Collect from functions
    for (program.functions) |func| {
        if (func.block.end_line > 0) {
            try ranges.append(allocator, .{
                .startLine = func.block.start_line - 1,
                .endLine = func.block.end_line - 1,
            });
        }
        try collectFoldingFromStatements(allocator, func.body, &ranges);
    }

    return ranges.toOwnedSlice(allocator);
}

fn collectFoldingFromMethod(allocator: std.mem.Allocator, method: ast.MethodDecl, ranges: *std.ArrayListUnmanaged(types.FoldingRange)) !void {
    if (method.block.end_line > 0) {
        try ranges.append(allocator, .{
            .startLine = method.block.start_line - 1,
            .endLine = method.block.end_line - 1,
        });
    }
    try collectFoldingFromStatements(allocator, method.body, ranges);
}

fn collectFoldingFromStatements(allocator: std.mem.Allocator, statements: []const ast.Statement, ranges: *std.ArrayListUnmanaged(types.FoldingRange)) std.mem.Allocator.Error!void {
    for (statements) |stmt| {
        try collectFoldingFromStatement(allocator, stmt, ranges);
    }
}

fn collectFoldingFromStatement(allocator: std.mem.Allocator, stmt: ast.Statement, ranges: *std.ArrayListUnmanaged(types.FoldingRange)) std.mem.Allocator.Error!void {
    // Add folding range for this statement if it has a block
    if (stmt.kind.getBlockInfo()) |info| {
        try ranges.append(allocator, .{
            .startLine = info.start_line - 1,
            .endLine = info.end_line - 1,
        });
    }

    // Recurse into child blocks
    for (stmt.kind.getChildBlocks()) |child| {
        // Add folding range for secondary child blocks (else clauses, catch handlers, etc.)
        // but not for primary blocks (their range is already covered by the statement's block info)
        if (child.role != .primary and child.info.end_line > 0) {
            try ranges.append(allocator, .{
                .startLine = child.info.start_line - 1,
                .endLine = child.info.end_line - 1,
            });
        }
        // Recurse into child statements
        try collectFoldingFromStatements(allocator, child.statements, ranges);
    }
}

/// Calculate indentation depth for a line by counting containing blocks (1-based line)
fn calculateIndentDepth(program: ast.Program, line: u32) u32 {
    var depth: u32 = 0;

    // Check types
    for (program.types) |type_decl| {
        if (type_decl.block.start_line < line and line < type_decl.block.end_line) {
            depth += 1;
        }
        for (type_decl.methods) |method| {
            depth += calculateDepthFromMethod(method, line);
        }
    }

    // Check enums
    for (program.enums) |enum_decl| {
        if (enum_decl.block.start_line < line and line < enum_decl.block.end_line) {
            depth += 1;
        }
        for (enum_decl.methods) |method| {
            depth += calculateDepthFromMethod(method, line);
        }
    }

    // Check interfaces
    for (program.interfaces) |interface_decl| {
        if (interface_decl.block.start_line < line and line < interface_decl.block.end_line) {
            depth += 1;
        }
    }

    // Check functions
    for (program.functions) |func| {
        if (func.block.start_line < line and line < func.block.end_line) {
            depth += 1;
        }
        depth += calculateDepthFromStatements(func.body, line);
    }

    return depth;
}

fn calculateDepthFromMethod(method: ast.MethodDecl, line: u32) u32 {
    var depth: u32 = 0;
    if (method.block.start_line < line and line < method.block.end_line) {
        depth += 1;
    }
    depth += calculateDepthFromStatements(method.body, line);
    return depth;
}

fn calculateDepthFromStatements(statements: []const ast.Statement, line: u32) u32 {
    var depth: u32 = 0;
    for (statements) |stmt| {
        depth += calculateDepthFromStatement(stmt, line);
    }
    return depth;
}

fn calculateDepthFromStatement(stmt: ast.Statement, line: u32) u32 {
    var depth: u32 = 0;

    // Check if line is inside this statement's block
    if (stmt.kind.getBlockInfo()) |info| {
        if (info.start_line < line and line < info.end_line) {
            depth += 1;
        }
    }

    // Recurse into child blocks
    for (stmt.kind.getChildBlocks()) |child| {
        // Check if line is inside this child block
        if (child.info.start_line < line and line < child.info.end_line) {
            depth += 1;
        }
        // Recurse into child statements
        depth += calculateDepthFromStatements(child.statements, line);
    }

    return depth;
}

/// Find the end line of a function or method by name and start line
fn findFunctionEndLine(program: ast.Program, start_line: u32, name: []const u8) ?u32 {
    // Search in functions
    for (program.functions) |func| {
        if (func.block.start_line == start_line and std.mem.eql(u8, func.name, name)) {
            return func.block.end_line;
        }
    }
    // Search in type methods
    for (program.types) |type_decl| {
        for (type_decl.methods) |method| {
            if (method.block.start_line == start_line and std.mem.eql(u8, method.name, name)) {
                return method.block.end_line;
            }
        }
    }
    // Search in enum methods
    for (program.enums) |enum_decl| {
        for (enum_decl.methods) |method| {
            if (method.block.start_line == start_line and std.mem.eql(u8, method.name, name)) {
                return method.block.end_line;
            }
        }
    }
    return null;
}

/// Find the start line of a block that ends on the given line with the given label
fn findBlockStartLineForEnd(program: ast.Program, end_line: u32, label: []const u8) ?u32 {
    // Search in functions
    for (program.functions) |func| {
        if (findBlockStartInStatements(func.body, end_line, label)) |line| return line;
    }
    // Search in type methods
    for (program.types) |type_decl| {
        for (type_decl.methods) |method| {
            if (findBlockStartInStatements(method.body, end_line, label)) |line| return line;
        }
    }
    // Search in enum methods
    for (program.enums) |enum_decl| {
        for (enum_decl.methods) |method| {
            if (findBlockStartInStatements(method.body, end_line, label)) |line| return line;
        }
    }
    return null;
}

/// Find the end line of a block that starts on the given line with the given label
fn findBlockEndLineForStart(program: ast.Program, start_line: u32, label: []const u8) ?u32 {
    // Search in functions
    for (program.functions) |func| {
        if (findBlockEndInStatements(func.body, start_line, label)) |line| return line;
    }
    // Search in type methods
    for (program.types) |type_decl| {
        for (type_decl.methods) |method| {
            if (findBlockEndInStatements(method.body, start_line, label)) |line| return line;
        }
    }
    // Search in enum methods
    for (program.enums) |enum_decl| {
        for (enum_decl.methods) |method| {
            if (findBlockEndInStatements(method.body, start_line, label)) |line| return line;
        }
    }
    return null;
}

fn findBlockStartInStatements(statements: []const ast.Statement, end_line: u32, label: []const u8) ?u32 {
    for (statements) |stmt| {
        if (findBlockStartInStatement(stmt, end_line, label)) |line| return line;
    }
    return null;
}

fn findBlockStartInStatement(stmt: ast.Statement, end_line: u32, label: []const u8) ?u32 {
    // Check if this statement's block ends on the target line with matching label
    if (stmt.kind.getBlockInfo()) |info| {
        if (info.end_line == end_line) {
            if (info.identifier) |id| {
                if (std.mem.eql(u8, id, label)) return stmt.line;
            } else if (label.len == 0) {
                return stmt.line;
            }
        }
    }

    // Recurse into child blocks
    for (stmt.kind.getChildBlocks()) |child| {
        if (findBlockStartInStatements(child.statements, end_line, label)) |line| return line;
    }
    return null;
}

fn findBlockEndInStatements(statements: []const ast.Statement, start_line: u32, label: []const u8) ?u32 {
    for (statements) |stmt| {
        if (findBlockEndInStatement(stmt, start_line, label)) |line| return line;
    }
    return null;
}

fn findBlockEndInStatement(stmt: ast.Statement, start_line: u32, label: []const u8) ?u32 {
    // Check if this statement's block starts on the target line with matching label
    if (stmt.kind.getBlockInfo()) |info| {
        if (stmt.line == start_line) {
            if (info.identifier) |id| {
                if (std.mem.eql(u8, id, label)) return info.end_line;
            } else if (label.len == 0) {
                return info.end_line;
            }
        }
    }

    // Recurse into child blocks
    for (stmt.kind.getChildBlocks()) |child| {
        if (findBlockEndInStatements(child.statements, start_line, label)) |line| return line;
    }
    return null;
}

fn findPositionInOriginal(original: []const u8, trimmed: []const u8, pos_in_trimmed: usize) usize {
    // Find where the trimmed content starts in original
    const trim_start = std.mem.indexOf(u8, original, trimmed) orelse 0;
    return trim_start + pos_in_trimmed;
}

fn formatFuncSignature(func: ast_to_ir.FuncInfo) []const u8 {
    if (func.return_type_name) |rt| {
        return rt;
    }
    return func.return_type.toMaxonName();
}

/// Get the identifier word at the given position in content
fn getWordAtPosition(content: []const u8, line: u32, character: u32) ?[]const u8 {
    // Find the specified line
    var lines = std.mem.splitScalar(u8, content, '\n');
    var current_line: u32 = 0;

    while (lines.next()) |line_content| : (current_line += 1) {
        if (current_line == line) {
            // Found the line, now extract the word at character position
            if (character >= line_content.len) return null;

            // Find the start of the identifier (scan backwards)
            var start: usize = character;
            while (start > 0) {
                const c = line_content[start - 1];
                if (!std.ascii.isAlphanumeric(c) and c != '_') break;
                start -= 1;
            }

            // Find the end of the identifier (scan forwards)
            var end: usize = character;
            while (end < line_content.len) {
                const c = line_content[end];
                if (!std.ascii.isAlphanumeric(c) and c != '_') break;
                end += 1;
            }

            if (start < end) {
                return line_content[start..end];
            }
            return null;
        }
    }

    return null;
}

/// Find the let declaration expression for a variable by name and line number
fn findLetDeclExpression(program: ast.Program, var_name: []const u8, decl_line: u32) ?ast.Expression {
    // Search in functions
    for (program.functions) |func| {
        if (findLetInStatements(func.body, var_name, decl_line)) |expr| {
            return expr;
        }
    }

    // Search in type methods
    for (program.types) |type_decl| {
        for (type_decl.methods) |method| {
            if (findLetInStatements(method.body, var_name, decl_line)) |expr| {
                return expr;
            }
        }
    }

    // Search in enum methods
    for (program.enums) |enum_decl| {
        for (enum_decl.methods) |method| {
            if (findLetInStatements(method.body, var_name, decl_line)) |expr| {
                return expr;
            }
        }
    }

    return null;
}

fn findLetInStatements(stmts: []const ast.Statement, var_name: []const u8, decl_line: u32) ?ast.Expression {
    for (stmts) |stmt| {
        // Check for let declaration
        if (stmt.kind == .let_decl) {
            const decl = stmt.kind.let_decl;
            if (stmt.line == decl_line and std.mem.eql(u8, decl.name, var_name)) {
                return decl.value;
            }
        }

        // Recurse into child blocks
        for (stmt.kind.getChildBlocks()) |child| {
            if (findLetInStatements(child.statements, var_name, decl_line)) |expr| return expr;
        }
    }
    return null;
}

fn binaryOpToSymbol(op: ast.BinaryOp) []const u8 {
    return switch (op) {
        .add => "+",
        .sub => "-",
        .mul => "*",
        .div => "/",
        .mod => "%",
        .band => "&",
        .bitor => "|",
        .bxor => "^",
        .shl => "<<",
        .shr => ">>",
    };
}

fn compareOpToSymbol(op: ast.CompareOp) []const u8 {
    return switch (op) {
        .eq => "==",
        .ne => "!=",
        .lt => "<",
        .le => "<=",
        .gt => ">",
        .ge => ">=",
    };
}

/// Format an AST expression to a string for display
fn formatExpression(writer: anytype, expr: ast.Expression) !void {
    switch (expr) {
        .integer => |i| try writer.print("{d}", .{i}),
        .float_lit => |f| try writer.print("{d}", .{f}),
        .bool_lit => |b| try writer.print("{}", .{b}),
        .nil_lit => try writer.writeAll("nil"),
        .string_literal => |s| try writer.print("\"{s}\"", .{s}),
        .char_literal => |c| try writer.print("'{s}'", .{c}),
        .identifier => |id| try writer.writeAll(id),
        .self_expr => try writer.writeAll("self"),
        .unary => |un| {
            switch (un.op) {
                .negate => try writer.writeByte('-'),
                .not => try writer.writeAll("not "),
            }
            try formatExpression(writer, un.operand.*);
        },
        .binary => |bin| {
            try formatExpression(writer, bin.left.*);
            try writer.print(" {s} ", .{binaryOpToSymbol(bin.op)});
            try formatExpression(writer, bin.right.*);
        },
        .compare => |cmp| {
            try formatExpression(writer, cmp.left.*);
            try writer.print(" {s} ", .{compareOpToSymbol(cmp.op)});
            try formatExpression(writer, cmp.right.*);
        },
        .logical => |logical| {
            try formatExpression(writer, logical.left.*);
            try writer.print(" {s} ", .{if (logical.op == .@"and") "and" else "or"});
            try formatExpression(writer, logical.right.*);
        },
        .call => |call| {
            try writer.writeAll(call.func_name);
            try writer.writeByte('(');
            for (call.args, 0..) |arg, i| {
                if (i > 0) try writer.writeAll(", ");
                try formatExpression(writer, arg);
            }
            try writer.writeByte(')');
        },
        .method_call => |mcall| {
            try formatExpression(writer, mcall.base.*);
            try writer.writeByte('.');
            try writer.writeAll(mcall.method_name);
            try writer.writeByte('(');
            for (mcall.args, 0..) |arg, i| {
                if (i > 0) try writer.writeAll(", ");
                try formatExpression(writer, arg);
            }
            try writer.writeByte(')');
        },
        .field_access => |fa| {
            try formatExpression(writer, fa.base.*);
            try writer.writeByte('.');
            try writer.writeAll(fa.field_name);
        },
        .index => |idx| {
            try formatExpression(writer, idx.base.*);
            try writer.writeByte('[');
            try formatExpression(writer, idx.index.*);
            try writer.writeByte(']');
        },
        .array_literal => |arr| {
            try writer.writeByte('[');
            for (arr.elements, 0..) |elem, i| {
                if (i > 0) try writer.writeAll(", ");
                try formatExpression(writer, elem);
            }
            try writer.writeByte(']');
        },
        .struct_init => |sinit| {
            try writer.writeAll(sinit.type_name);
            try writer.writeAll(" { ");
            for (sinit.fields, 0..) |field, i| {
                if (i > 0) try writer.writeAll(", ");
                try writer.writeAll(field.name);
                try writer.writeAll(": ");
                try formatExpression(writer, field.value.*);
            }
            try writer.writeAll(" }");
        },
        .nil_coalesce => |nc| {
            try formatExpression(writer, nc.optional.*);
            try writer.writeAll(" ?? ");
            try formatExpression(writer, nc.default.*);
        },
        .cast => |c| {
            try formatExpression(writer, c.expr.*);
            try writer.writeAll(" as ");
            try writer.writeAll(c.target_type);
        },
        .enum_case => |ec| {
            try writer.writeByte('.');
            try writer.writeAll(ec.case_name);
            if (ec.args.len > 0) {
                try writer.writeByte('(');
                for (ec.args, 0..) |arg, i| {
                    if (i > 0) try writer.writeAll(", ");
                    try formatExpression(writer, arg);
                }
                try writer.writeByte(')');
            }
        },
        // For complex expressions, just show a placeholder
        .closure, .try_expr, .match_expr, .map_literal, .set_from, .array_type, .interpolated_string => {
            try writer.writeAll("...");
        },
    }
}
