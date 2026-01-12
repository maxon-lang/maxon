const std = @import("std");
const compiler = @import("../compiler/0-compiler.zig");
const ast_to_ir = @import("../compiler/4-ast_to_ir.zig");
const ast = @import("../compiler/ast.zig");
const types = @import("types.zig");
const lexer = @import("../compiler/1-lexer.zig");

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
        _ = self.getSemanticInfo(uri_copy) catch {};
    }

    pub fn updateDocument(self: *Analyzer, uri: []const u8, content: []const u8, version: i64) !void {
        // Get entry to access the owned key for cache operations
        const entry = self.documents.getEntry(uri) orelse return;
        const owned_uri = entry.key_ptr.*;
        const doc = entry.value_ptr;

        // Store old content in case analysis fails
        const old_content = doc.content;
        doc.content = try self.allocator.dupe(u8, content);
        doc.version = version;

        // Try to re-analyze - if successful, invalidate old cache
        const new_info = compiler.analyzeForLSP(doc.content, null, self.allocator) catch {
            // Keep old cache, just free old content
            self.allocator.free(old_content);
            return;
        };

        // Analysis succeeded - replace old cache with new
        // Use owned_uri since uri may be from parsed JSON that gets freed
        if (self.semantic_cache.fetchRemove(owned_uri)) |kv| {
            var old_info = kv.value;
            old_info.deinit();
        }
        self.semantic_cache.put(self.allocator, owned_uri, new_info) catch {
            // If we fail to cache the new info, clean it up
            var info_to_free = new_info;
            info_to_free.deinit();
        };
        self.allocator.free(old_content);
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

        // Get the document - we need to use doc.uri as the cache key since it's
        // owned by the documents map. The passed-in uri may be from parsed JSON
        // that gets freed after the message handler returns.
        const doc_entry = self.documents.getEntry(uri) orelse return error.DocumentNotFound;
        const owned_uri = doc_entry.key_ptr.*;

        var info = compiler.analyzeForLSP(doc_entry.value_ptr.content, null, self.allocator) catch |err| {
            return err;
        };
        errdefer info.deinit();

        try self.semantic_cache.put(self.allocator, owned_uri, info);
        return self.semantic_cache.getPtr(owned_uri).?;
    }

    pub fn inferTypeAtPosition(self: *Analyzer, doc_uri: []const u8, _: u32, _: u32, var_name: []const u8) ?[]const u8 {
        const info = self.getSemanticInfo(doc_uri) catch return null;

        // Find the variable by name
        if (info.findVariable(var_name)) |v| {
            return v.display_name orelse v.ty.getTypeName();
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
                                .detail = field.display_name orelse field.value_type.getTypeName(),
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
        const doc = self.documents.get(uri) orelse {
            return null;
        };

        // Check if the position is inside a comment - if so, return null
        if (isPositionInComment(doc.content, line, character)) {
            return null;
        }

        const word = getWordAtPosition(doc.content, line, character) orelse {
            return null;
        };

        // 1. Check for keywords - doesn't require semantic analysis
        if (getKeywordHover(word)) |hover_text| {
            // Format as: ```maxon\nkeyword\n```\n\nDescription
            var buffer: std.ArrayListUnmanaged(u8) = .empty;
            const writer = buffer.writer(self.allocator);
            writer.writeAll("```maxon\n") catch return null;
            writer.writeAll(word) catch return null;
            writer.writeAll("\n```\n\n") catch return null;
            writer.writeAll(hover_text) catch return null;
            return buffer.toOwnedSlice(self.allocator) catch null;
        }

        // Get semantic info for remaining checks
        const info = self.getSemanticInfo(uri) catch return null;

        // 2. Check for variables (local vars and parameters)
        if (info.findVariable(word)) |v| {
            return self.formatVariableHover(v, info, line);
        }

        // 3. Check for function calls
        if (info.functions.get(word)) |func_info| {
            return self.formatFunctionHover(word, func_info);
        }

        // 3b. Check for method calls - look up mangled name based on receiver type
        if (self.findMethodCallHover(doc.content, info, line, character, word)) |hover| {
            return hover;
        }

        // 4. Check for type references (structs, enums)
        if (info.types.get(word)) |type_info| {
            return self.formatTypeHover(word, type_info);
        }

        // 5. Check for interface references
        if (info.interfaces.get(word)) |intf_info| {
            return self.formatInterfaceHover(word, intf_info);
        }

        // 6. Check for struct field declarations (when cursor is on a field in a type definition)
        if (self.getStructFieldAtPosition(info, line, word)) |field_info| {
            return self.formatFieldHover(field_info);
        }

        return null;
    }

    /// Find method call hover by detecting receiver.method pattern and looking up mangled name
    fn findMethodCallHover(self: *Analyzer, content: []const u8, info: *ast_to_ir.SemanticInfo, line: u32, _: u32, method_name: []const u8) ?[]const u8 {
        // Get the line content
        var lines = std.mem.splitScalar(u8, content, '\n');
        var current_line: u32 = 0;
        while (lines.next()) |line_content| : (current_line += 1) {
            if (current_line == line) {
                // Find the start of the method name
                const method_start = std.mem.indexOf(u8, line_content, method_name) orelse return null;

                // Check if there's a '.' before the method name
                if (method_start == 0) return null;
                var dot_pos: usize = method_start - 1;

                // Skip whitespace before the method name
                while (dot_pos > 0 and line_content[dot_pos] == ' ') {
                    dot_pos -= 1;
                }

                if (line_content[dot_pos] != '.') return null;

                // Extract the receiver identifier (scan backwards from the dot)
                if (dot_pos == 0) return null;
                const receiver_end = dot_pos;
                var receiver_start = dot_pos - 1;
                while (receiver_start > 0) {
                    const c = line_content[receiver_start - 1];
                    if (!std.ascii.isAlphanumeric(c) and c != '_') break;
                    receiver_start -= 1;
                }

                const receiver_name = line_content[receiver_start..receiver_end];
                if (receiver_name.len == 0) return null;

                // Look up the receiver's type
                const receiver_var = info.findVariable(receiver_name) orelse {
                    return null;
                };

                // Get the type name for method lookup
                const type_name = self.getTypeNameForMethodLookup(receiver_var.ty) orelse {
                    return null;
                };

                // Construct mangled method name and look it up
                var mangled_buf: [512]u8 = undefined;
                const mangled_name = std.fmt.bufPrint(&mangled_buf, "{s}${s}", .{ type_name, method_name }) catch return null;

                if (info.functions.get(mangled_name)) |func_info| {
                    return self.formatFunctionHover(method_name, func_info);
                }

                return null;
            }
        }
        return null;
    }

    /// Get the type name suitable for method lookup (handles arrays, structs, etc.)
    fn getTypeNameForMethodLookup(self: *Analyzer, ty: ast_to_ir.ValueType) ?[]const u8 {
        _ = self;
        return switch (ty) {
            .primitive => |name| name,
            .struct_type => |name| name,
            .enum_type => |name| name,
            .array_type => "Array", // Array methods are on the generic Array type
            .error_union_type, .function_type => null,
        };
    }

    fn formatVariableHover(self: *Analyzer, v: ast_to_ir.SemanticVarInfo, info: *ast_to_ir.SemanticInfo, hover_line: u32) ?[]const u8 {
        _ = hover_line;
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        // For immutable variables (let), show value
        if (!v.is_mutable and v.decl_line > 0) {
            if (info.program) |program| {
                if (findLetDeclExpression(program, v.name, v.decl_line)) |expr| {
                    writer.writeAll("```maxon\nlet ") catch return null;
                    writer.writeAll(v.name) catch return null;
                    writer.writeAll(": ") catch return null;
                    const type_name = v.display_name orelse v.ty.getTypeName() orelse "unknown";
                    writer.writeAll(type_name) catch return null;
                    writer.writeAll(" = ") catch return null;
                    formatExpression(writer, expr) catch return null;
                    writer.writeAll("\n```") catch return null;
                    return buffer.toOwnedSlice(self.allocator) catch null;
                }
            }
        }

        // Show type info - prefer display_name from AST, fall back to type name
        const type_name = v.display_name orelse v.ty.getTypeName() orelse "unknown";
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
            writer.writeAll(param.display_name orelse param.ty.getTypeName() orelse "unknown") catch return null;
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

        // Add doc comment if present
        if (func.doc_comment) |doc| {
            writer.writeAll("\n\n") catch return null;
            writer.writeAll(doc) catch return null;
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

    fn formatInterfaceHover(self: *Analyzer, name: []const u8, intf_info: ast_to_ir.InterfaceInfo) ?[]const u8 {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        writer.print("```maxon\ninterface {s}\n```\n\nInterface with {d} methods", .{ name, intf_info.methods.len }) catch return null;

        return buffer.toOwnedSlice(self.allocator) catch null;
    }

    fn getStructFieldAtPosition(self: *Analyzer, info: *ast_to_ir.SemanticInfo, line: u32, field_name: []const u8) ?ast_to_ir.FieldInfo {
        _ = self;
        // Convert 0-based line to 1-based for AST
        const ast_line = line + 1;

        // Search through all struct types to find if field_name is declared on this line
        var type_iter = info.types.iterator();
        while (type_iter.next()) |entry| {
            switch (entry.value_ptr.*) {
                .struct_type => |st| {
                    // Check if the line is within the struct definition
                    if (ast_line > st.decl_line) {
                        for (st.fields) |field| {
                            if (std.mem.eql(u8, field.name, field_name)) {
                                return field;
                            }
                        }
                    }
                },
                else => {},
            }
        }
        return null;
    }

    fn formatFieldHover(self: *Analyzer, field: ast_to_ir.FieldInfo) ?[]const u8 {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        const writer = buffer.writer(self.allocator);

        const mutability = if (field.is_mutable) "var" else "let";
        const type_name = field.display_name orelse field.value_type.getTypeName() orelse "unknown";
        writer.print("```maxon\n{s} {s}: {s}\n```\n\n(field)", .{ mutability, field.name, type_name }) catch return null;

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
                .enum_type => |et| {
                    if (et.decl_line > 0) {
                        return .{
                            .uri = uri,
                            .line = et.decl_line - 1,
                            .character = if (et.decl_column > 0) et.decl_column - 1 else 0,
                        };
                    }
                },
                else => {},
            }
        }

        // 4. Check if it's an interface reference
        if (info.interfaces.get(word)) |intf_info| {
            if (intf_info.decl_line > 0) {
                return .{
                    .uri = uri,
                    .line = intf_info.decl_line - 1,
                    .character = if (intf_info.decl_column > 0) intf_info.decl_column - 1 else 0,
                };
            }
        }

        // 5. Check if it's a struct field access (e.g., p.x where p is a struct)
        // Look at the context to see if there's a dot before the word
        if (self.getFieldAccessContext(doc.content, line, character, word)) |ctx| {
            // ctx.type_name is the type being accessed, word is the field name
            if (info.types.get(ctx.type_name)) |type_info| {
                switch (type_info) {
                    .struct_type => |st| {
                        // Find the field in the struct
                        for (st.fields) |field| {
                            if (std.mem.eql(u8, field.name, word)) {
                                // Return field definition - field is on line after struct declaration
                                // We don't have exact field lines, so approximate based on field index
                                return .{
                                    .uri = uri,
                                    .line = st.decl_line, // Field is on line after struct decl
                                    .character = 4, // Typical field indentation
                                };
                            }
                        }
                    },
                    else => {},
                }
            }
        }

        return null;
    }

    /// Context for field access (var.field)
    const FieldAccessContext = struct {
        type_name: []const u8,
    };

    /// Check if position is accessing a field (e.g., p.x) and return the type being accessed
    fn getFieldAccessContext(self: *Analyzer, content: []const u8, line: u32, character: u32, field_name: []const u8) ?FieldAccessContext {
        _ = field_name;
        // Find the line
        var lines = std.mem.splitScalar(u8, content, '\n');
        var current_line: u32 = 0;
        while (lines.next()) |line_content| : (current_line += 1) {
            if (current_line == line) {
                // Look for pattern: identifier.word at position
                // Walk backwards from character to find the dot
                if (character > 0 and character <= line_content.len) {
                    var i = character - 1;
                    // Skip the word we're on
                    while (i > 0 and (std.ascii.isAlphanumeric(line_content[i]) or line_content[i] == '_')) {
                        i -= 1;
                    }
                    // Check if there's a dot
                    if (i < line_content.len and line_content[i] == '.') {
                        i -= 1;
                        // Now find the identifier before the dot
                        const end_pos = i + 1;
                        while (i > 0 and (std.ascii.isAlphanumeric(line_content[i - 1]) or line_content[i - 1] == '_')) {
                            i -= 1;
                        }
                        const var_name = line_content[i..end_pos];
                        if (var_name.len > 0) {
                            // Get the type of this variable from semantic info
                            const uri = self.getUriFromContent(content);
                            if (uri) |u| {
                                const info = self.getSemanticInfo(u) catch return null;
                                if (info.findVariable(var_name)) |v| {
                                    if (v.ty.getTypeName()) |type_name| {
                                        return .{ .type_name = type_name };
                                    }
                                }
                            }
                        }
                    }
                }
                break;
            }
        }
        return null;
    }

    /// Get the URI for a given content string (reverse lookup from documents)
    fn getUriFromContent(self: *Analyzer, content: []const u8) ?[]const u8 {
        var iter = self.documents.iterator();
        while (iter.next()) |entry| {
            if (entry.value_ptr.content.ptr == content.ptr) {
                return entry.key_ptr.*;
            }
        }
        return null;
    }

    pub fn getMemberCompletions(self: *Analyzer, doc_uri: []const u8, type_name: []const u8) ![]types.CompletionItem {
        const info = self.getSemanticInfo(doc_uri) catch return &.{};

        const type_info = info.getType(type_name) orelse return &.{};

        var items: std.ArrayListUnmanaged(types.CompletionItem) = .empty;

        switch (type_info) {
            .struct_type => |st| {
                for (st.fields) |field| {
                    // Skip internal fields (starting with _ or __)
                    if (!std.mem.startsWith(u8, field.name, "__") and !std.mem.startsWith(u8, field.name, "_")) {
                        try items.append(self.allocator, .{
                            .label = field.name,
                            .kind = .field,
                            .detail = field.display_name orelse field.value_type.getTypeName(),
                        });
                    }
                }
            },
            .enum_type => |et| {
                // Add enum members as completions
                var member_iter = et.members.iterator();
                while (member_iter.next()) |entry| {
                    try items.append(self.allocator, .{
                        .label = entry.key_ptr.*,
                        .kind = .enum_member,
                        .detail = type_name,
                    });
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

    /// Get rename edits for a symbol at the given position
    pub fn getRenameEdits(self: *Analyzer, uri: []const u8, line: u32, character: u32, new_name: []const u8) ?[]types.TextEdit {
        const doc = self.documents.get(uri) orelse return null;
        const word = getWordAtPosition(doc.content, line, character) orelse return null;

        // Find all occurrences of this word and create edits
        var edits: std.ArrayListUnmanaged(types.TextEdit) = .empty;
        errdefer edits.deinit(self.allocator);

        var current_line: u32 = 0;
        var lines = std.mem.splitScalar(u8, doc.content, '\n');

        while (lines.next()) |line_content| : (current_line += 1) {
            var col: u32 = 0;
            while (col < line_content.len) {
                // Look for word at this position
                if (findWordAt(line_content, col, word)) |range| {
                    edits.append(self.allocator, .{
                        .range = .{
                            .start = .{ .line = current_line, .character = range.start },
                            .end = .{ .line = current_line, .character = range.end },
                        },
                        .newText = new_name,
                    }) catch return null;
                    col = range.end;
                } else {
                    col += 1;
                }
            }
        }

        if (edits.items.len == 0) {
            edits.deinit(self.allocator);
            return null;
        }
        return edits.toOwnedSlice(self.allocator) catch null;
    }

    /// Get code actions for a given line (e.g., quick fixes for diagnostics)
    /// Returns a simple code action without edit (client must implement the command)
    pub fn getCodeActions(self: *Analyzer, uri: []const u8, line: u32) ?[]types.CodeAction {
        const info = self.getSemanticInfo(uri) catch return null;

        var actions: std.ArrayListUnmanaged(types.CodeAction) = .empty;
        errdefer actions.deinit(self.allocator);

        // Check for unused variable warnings on this line
        for (info.variables) |v| {
            // Convert 1-based to 0-based line
            if (v.decl_line > 0 and v.decl_line - 1 == line) {
                // Check if variable name doesn't start with underscore
                const name = v.name;
                if (!std.mem.startsWith(u8, name, "_")) {
                    // Create a simple code action (without edit to avoid allocation issues)
                    actions.append(self.allocator, .{
                        .title = "Prefix unused variable with _",
                        .kind = "quickfix",
                    }) catch continue;
                }
            }
        }

        if (actions.items.len == 0) {
            actions.deinit(self.allocator);
            return null;
        }
        return actions.toOwnedSlice(self.allocator) catch null;
    }

    /// Get semantic tokens for a document
    pub fn getSemanticTokens(self: *Analyzer, uri: []const u8) ?[]u32 {
        const doc = self.documents.get(uri) orelse return null;

        var tokens: std.ArrayListUnmanaged(u32) = .empty;
        errdefer tokens.deinit(self.allocator);

        var prev_line: u32 = 0;
        var prev_char: u32 = 0;
        var current_line: u32 = 0;

        var lines = std.mem.splitScalar(u8, doc.content, '\n');
        while (lines.next()) |line_content| : (current_line += 1) {
            var col: u32 = 0;
            while (col < line_content.len) {
                // Skip whitespace
                if (std.ascii.isWhitespace(line_content[col])) {
                    col += 1;
                    continue;
                }

                // Check for comments
                if (col + 1 < line_content.len and line_content[col] == '/' and line_content[col + 1] == '/') {
                    // Line comment - rest of line
                    const start = col;
                    const len = @as(u32, @intCast(line_content.len - col));
                    addSemanticToken(&tokens, self.allocator, current_line, start, len, 6, 0, &prev_line, &prev_char) catch break;
                    break;
                }

                // Check for strings
                if (line_content[col] == '"' or line_content[col] == '\'') {
                    const quote = line_content[col];
                    const start = col;
                    col += 1;
                    while (col < line_content.len and line_content[col] != quote) {
                        if (line_content[col] == '\\' and col + 1 < line_content.len) {
                            col += 2;
                        } else {
                            col += 1;
                        }
                    }
                    if (col < line_content.len) col += 1; // closing quote
                    const len = col - start;
                    addSemanticToken(&tokens, self.allocator, current_line, start, len, 4, 0, &prev_line, &prev_char) catch break;
                    continue;
                }

                // Check for numbers
                if (std.ascii.isDigit(line_content[col])) {
                    const start = col;
                    while (col < line_content.len and (std.ascii.isDigit(line_content[col]) or line_content[col] == '.' or line_content[col] == '_')) {
                        col += 1;
                    }
                    const len = col - start;
                    addSemanticToken(&tokens, self.allocator, current_line, start, len, 5, 0, &prev_line, &prev_char) catch break;
                    continue;
                }

                // Check for identifiers and keywords
                if (std.ascii.isAlphabetic(line_content[col]) or line_content[col] == '_') {
                    const start = col;
                    while (col < line_content.len and (std.ascii.isAlphanumeric(line_content[col]) or line_content[col] == '_')) {
                        col += 1;
                    }
                    const word = line_content[start..col];
                    const len = @as(u32, @intCast(word.len));

                    // Determine token type based on keyword category or context
                    const token_type: u32 = blk: {
                        // Check if it's a keyword and get its category
                        inline for (lexer.Lexer.keyword_map) |entry| {
                            const keyword_text = entry[0];
                            const category = entry[2];
                            if (std.mem.eql(u8, word, keyword_text)) {
                                // Map category to token type
                                break :blk switch (category) {
                                    .control => 0, // keyword
                                    .other => 9, // modifier (for function, type, enum, etc.)
                                    .logical => 7, // operator
                                    .constant => 0, // keyword (true, false, nil)
                                    .type_keyword => 3, // type
                                };
                            }
                        }
                        // Not a keyword - determine by context
                        if (std.ascii.isUpper(word[0])) break :blk 3; // type
                        if (col < line_content.len and line_content[col] == '(') break :blk 1; // function
                        break :blk 2; // variable
                    };

                    addSemanticToken(&tokens, self.allocator, current_line, start, len, token_type, 0, &prev_line, &prev_char) catch break;
                    continue;
                }

                col += 1;
            }
        }

        if (tokens.items.len == 0) {
            tokens.deinit(self.allocator);
            return null;
        }
        return tokens.toOwnedSlice(self.allocator) catch null;
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

    // Recurse into child blocks
    // Note: We only check child blocks, not getBlockInfo(), because for statements
    // like while/for/if, the block info has the same line range as the primary
    // child block. Checking both would result in double-counting the indentation.
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
            if (character >= line_content.len) {
                return null;
            }

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
        .closure, .try_expr, .match_expr, .map_literal, .init_from_array, .array_type, .interpolated_string => {
            try writer.writeAll("...");
        },
    }
}

// ============================================================================
// Comment Detection
// ============================================================================

/// Check if a position is inside a comment (line or block comment)
fn isPositionInComment(content: []const u8, line: u32, character: u32) bool {
    // Get absolute position from line and character
    var abs_pos: usize = 0;
    var lines = std.mem.splitScalar(u8, content, '\n');
    var current_line: u32 = 0;

    while (lines.next()) |line_content| : (current_line += 1) {
        if (current_line == line) {
            abs_pos += @min(character, line_content.len);
            break;
        }
        abs_pos += line_content.len + 1; // +1 for newline
    }

    // Scan through content tracking comment state
    var i: usize = 0;
    var in_block_comment = false;

    while (i < content.len) {
        if (in_block_comment) {
            // Look for end of block comment
            if (i + 1 < content.len and content[i] == '*' and content[i + 1] == '/') {
                // Position inside comment if before the closing */
                if (abs_pos < i + 2) {
                    return true;
                }
                in_block_comment = false;
                i += 2;
                continue;
            }
        } else {
            // Check for line comment
            if (i + 1 < content.len and content[i] == '/' and content[i + 1] == '/') {
                // Find end of line
                var end_of_line = i;
                while (end_of_line < content.len and content[end_of_line] != '\n') {
                    end_of_line += 1;
                }
                if (abs_pos >= i and abs_pos < end_of_line) {
                    return true;
                }
                i = end_of_line;
                continue;
            }
            // Check for block comment start
            if (i + 1 < content.len and content[i] == '/' and content[i + 1] == '*') {
                in_block_comment = true;
                // If position is at start of block comment, it's in comment
                if (abs_pos >= i) {
                    // But we need to find end first
                    var j = i + 2;
                    while (j + 1 < content.len) {
                        if (content[j] == '*' and content[j + 1] == '/') {
                            // Block comment ends at j+2
                            if (abs_pos < j + 2) {
                                return true;
                            }
                            break;
                        }
                        j += 1;
                    }
                    // If no closing found, position after start is in comment
                    if (j + 1 >= content.len and abs_pos >= i) {
                        return true;
                    }
                }
                i += 2;
                continue;
            }
        }
        i += 1;
    }

    // If still in block comment at end of content
    if (in_block_comment and abs_pos >= i) {
        return true;
    }

    return false;
}

// ============================================================================
// Keyword Hover
// ============================================================================

/// Get hover text for a keyword
fn getKeywordHover(word: []const u8) ?[]const u8 {
    inline for (lexer.Lexer.keyword_map) |entry| {
        const keyword_text = entry[0];
        const help_text = entry[3];
        if (std.mem.eql(u8, word, keyword_text)) {
            return help_text;
        }
    }
    return null;
}

// ============================================================================
// Word Finding
// ============================================================================

const WordRange = struct {
    start: u32,
    end: u32,
};

/// Find a specific word at or after the given column position
/// Returns the range if found at exactly that position
fn findWordAt(line: []const u8, col: u32, word: []const u8) ?WordRange {
    if (col >= line.len) return null;

    // Check if we're at the start of an identifier
    const c = line[col];
    if (!std.ascii.isAlphabetic(c) and c != '_') return null;

    // Find the full word at this position
    const start = col;
    var end = col;

    // Extend to end of word
    while (end < line.len and (std.ascii.isAlphanumeric(line[end]) or line[end] == '_')) {
        end += 1;
    }

    // Check if it matches
    const found_word = line[start..end];
    if (std.mem.eql(u8, found_word, word)) {
        return .{ .start = start, .end = @intCast(end) };
    }
    return null;
}

/// Check if a word is a Maxon keyword
fn isKeyword(word: []const u8) bool {
    inline for (lexer.Lexer.keyword_map) |entry| {
        const keyword_text = entry[0];
        const category = entry[2];
        // Exclude type keywords - they're handled separately
        if (category != .type_keyword) {
            if (std.mem.eql(u8, word, keyword_text)) return true;
        }
    }
    return false;
}

fn isTypeKeyword(word: []const u8) bool {
    inline for (lexer.Lexer.keyword_map) |entry| {
        const keyword_text = entry[0];
        const category = entry[2];
        if (category == .type_keyword) {
            if (std.mem.eql(u8, word, keyword_text)) return true;
        }
    }
    return false;
}

/// Add a semantic token to the tokens array (LSP delta encoding)
fn addSemanticToken(
    tokens: *std.ArrayListUnmanaged(u32),
    allocator: std.mem.Allocator,
    line: u32,
    char: u32,
    length: u32,
    token_type: u32,
    modifiers: u32,
    prev_line: *u32,
    prev_char: *u32,
) !void {
    const delta_line = line - prev_line.*;
    const delta_char = if (delta_line == 0) char - prev_char.* else char;

    try tokens.append(allocator, delta_line);
    try tokens.append(allocator, delta_char);
    try tokens.append(allocator, length);
    try tokens.append(allocator, token_type);
    try tokens.append(allocator, modifiers);

    prev_line.* = line;
    prev_char.* = char;
}

test "analyzer hover shows doc comment for function" {
    const allocator = std.testing.allocator;
    var analyzer = Analyzer.init(allocator);
    defer analyzer.deinit();

    const source =
        \\/// Multiplies two numbers together
        \\function multiply(x int, y int) returns int
        \\    return x * y
        \\end 'multiply'
        \\
        \\function main() returns int
        \\    return multiply(3, 4)
        \\end 'main'
    ;

    try analyzer.openDocument("file:///test.maxon", source, 1);

    // Verify doc_comment is stored in FuncInfo
    const info = try analyzer.getSemanticInfo("file:///test.maxon");
    const func_info = info.functions.get("multiply").?;
    try std.testing.expect(func_info.doc_comment != null);
    try std.testing.expectEqualStrings("Multiplies two numbers together", func_info.doc_comment.?);

    // Verify hover returns doc comment
    const hover = analyzer.getHoverInfo("file:///test.maxon", 6, 11);
    defer if (hover) |h| allocator.free(h);

    try std.testing.expect(hover != null);
    const content = hover.?;
    try std.testing.expect(std.mem.indexOf(u8, content, "function") != null);
    try std.testing.expect(std.mem.indexOf(u8, content, "multiply") != null);
    try std.testing.expect(std.mem.indexOf(u8, content, "Multiplies two numbers together") != null);
}

test "analyzer hover shows method with doc comment" {
    const allocator = std.testing.allocator;
    var analyzer = Analyzer.init(allocator);
    defer analyzer.deinit();

    const source =
        \\type Counter
        \\    var count int
        \\
        \\    /// Increments the counter by one
        \\    method increment()
        \\        self.count = self.count + 1
        \\    end 'increment'
        \\end 'Counter'
        \\
        \\function main() returns int
        \\    var c = Counter{count: 0}
        \\    c.increment()
        \\    return c.count
        \\end 'main'
    ;

    try analyzer.openDocument("file:///test.maxon", source, 1);

    // line 11 is "    c.increment()" (0-indexed)
    // "increment" starts at column 6
    const hover = analyzer.getHoverInfo("file:///test.maxon", 11, 6);
    defer if (hover) |h| allocator.free(h);

    try std.testing.expect(hover != null);
    const content = hover.?;
    try std.testing.expect(std.mem.indexOf(u8, content, "increment") != null);
    try std.testing.expect(std.mem.indexOf(u8, content, "Increments the counter by one") != null);
}

test "analyzer hover shows stdlib Array.push with doc comment" {
    const allocator = std.testing.allocator;
    var analyzer = Analyzer.init(allocator);
    defer analyzer.deinit();

    const source =
        \\function main() returns int
        \\    var arr = [1, 2, 3]
        \\    arr.push(4)
        \\    return 0
        \\end 'main'
    ;

    try analyzer.openDocument("file:///test.maxon", source, 1);

    // line 2 is "    arr.push(4)" (0-indexed)
    // "push" starts at column 8
    const hover = analyzer.getHoverInfo("file:///test.maxon", 2, 8);
    defer if (hover) |h| allocator.free(h);

    try std.testing.expect(hover != null);
    const content = hover.?;
    try std.testing.expect(std.mem.indexOf(u8, content, "push") != null);
    // This should show the doc comment from stdlib Array.maxon
    const has_doc = std.mem.indexOf(u8, content, "Append element to end") != null;
    if (!has_doc) {
        std.debug.print("\n[FAIL] Expected doc comment 'Append element to end' but got:\n{s}\n", .{content});
    }
    try std.testing.expect(has_doc);
}
