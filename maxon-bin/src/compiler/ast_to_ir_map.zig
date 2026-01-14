/// Map IR emission helpers.
/// Contains layout constants and cleanup operations for Map types.
const std = @import("std");
const ir = @import("ir.zig");
const ast = @import("ast.zig");
const debug = @import("debug.zig");
const string = @import("ast_to_ir_string.zig");
const array = @import("ast_to_ir_array.zig");
const ManagedArray = string.ManagedArray;
const String = string.String;
const ast_to_ir = @import("4-ast_to_ir.zig");
const AstToIr = ast_to_ir.AstToIr;
const DeferredBlocks = ast_to_ir.DeferredBlocks;

// ============================================================================
// Map Layout (144 bytes)
// ============================================================================
/// Map$K$V struct layout (144 bytes total)
/// Layout: keys(40) + values(40) + states(40) + count(8) + capacity(8) + iter_index(8)
///
/// Fields:
/// - keys: Array$K (40 bytes) - array of keys
/// - values: Array$V (40 bytes) - array of values
/// - states: Array$int (40 bytes) - array of bucket states (0=empty, 1=occupied, 2=deleted)
/// - count: i64 (8 bytes) - number of key-value pairs
/// - capacity: i64 (8 bytes) - capacity of the hash table
/// - iter_index: i64 (8 bytes) - current iteration index
pub const Map = struct {
    const SIZE: i32 = 144;
    const KEYS_OFFSET: i32 = 0;
    const VALUES_OFFSET: i32 = 40;
    const STATES_OFFSET: i32 = 80;
    const COUNT_OFFSET: i32 = 120;
    const CAPACITY_OFFSET: i32 = 128;
    const ITER_INDEX_OFFSET: i32 = 136;

    comptime {
        if (VALUES_OFFSET != KEYS_OFFSET + 40) {
            @compileError("Map layout mismatch: VALUES_OFFSET != KEYS_OFFSET + 40");
        }
        if (STATES_OFFSET != VALUES_OFFSET + 40) {
            @compileError("Map layout mismatch: STATES_OFFSET != VALUES_OFFSET + 40");
        }
        if (COUNT_OFFSET != STATES_OFFSET + 40) {
            @compileError("Map layout mismatch: COUNT_OFFSET != STATES_OFFSET + 40");
        }
        if (CAPACITY_OFFSET != COUNT_OFFSET + 8) {
            @compileError("Map layout mismatch: CAPACITY_OFFSET != COUNT_OFFSET + 8");
        }
        if (ITER_INDEX_OFFSET != CAPACITY_OFFSET + 8) {
            @compileError("Map layout mismatch: ITER_INDEX_OFFSET != CAPACITY_OFFSET + 8");
        }
        if (ITER_INDEX_OFFSET + 8 != SIZE) {
            @compileError("Map layout mismatch: ITER_INDEX_OFFSET + 8 != SIZE");
        }
    }

    /// Returns the size of the Map struct in bytes.
    pub fn size() i32 {
        return SIZE;
    }

    /// Returns a field pointer to the keys array at KEYS_OFFSET.
    pub fn getKeysPtr(func: *ir.Function, map_ptr: ir.Value) !ir.FieldPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(map_ptr), KEYS_OFFSET);
    }

    /// Returns a field pointer to the values array at VALUES_OFFSET.
    pub fn getValuesPtr(func: *ir.Function, map_ptr: ir.Value) !ir.FieldPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(map_ptr), VALUES_OFFSET);
    }

    /// Returns a field pointer to the states array at STATES_OFFSET.
    pub fn getStatesPtr(func: *ir.Function, map_ptr: ir.Value) !ir.FieldPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(map_ptr), STATES_OFFSET);
    }

    /// Returns a field pointer to the count field at COUNT_OFFSET.
    pub fn getCountPtr(func: *ir.Function, map_ptr: ir.Value) !ir.FieldPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(map_ptr), COUNT_OFFSET);
    }

    /// Returns a field pointer to the capacity field at CAPACITY_OFFSET.
    pub fn getCapacityPtr(func: *ir.Function, map_ptr: ir.Value) !ir.FieldPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(map_ptr), CAPACITY_OFFSET);
    }

    /// Returns a field pointer to the iter_index field at ITER_INDEX_OFFSET.
    pub fn getIterIndexPtr(func: *ir.Function, map_ptr: ir.Value) !ir.FieldPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(map_ptr), ITER_INDEX_OFFSET);
    }

    /// Loads the capacity field as i64.
    pub fn loadCapacity(func: *ir.Function, map_ptr: ir.Value) !ir.Value {
        const cap_ptr = try getCapacityPtr(func, map_ptr);
        return func.emitLoad(cap_ptr.raw(), .i64);
    }

    /// Loads the count field as i64.
    pub fn loadCount(func: *ir.Function, map_ptr: ir.Value) !ir.Value {
        const count_ptr = try getCountPtr(func, map_ptr);
        return func.emitLoad(count_ptr.raw(), .i64);
    }
};

// ============================================================================
// Map Cleanup Helpers
// ============================================================================

/// Emit cleanup code for Map types, freeing the three internal array buffers.
/// For Map$String$V, also decrefs each string key element.
/// For Map$K$String, also decrefs each string value element.
pub fn emitMapCleanup(self: *AstToIr, map_ptr: ir.Value, struct_name: []const u8) !void {
    // Map layout:
    //   offset 0:  keys array (32 bytes) - Array$KeyType
    //   offset 32: values array (32 bytes) - Array$ValueType
    //   offset 64: states array (32 bytes) - Array$int
    //   offset 96: count (8 bytes)
    //   offset 104: capacity (8 bytes)
    // Each Array: buffer_ptr(8) + len(8) + cap(8) + iterIndex(8)

    // Parse key and value types from Map$KeyType$ValueType
    const prefix_len = "Map$".len;
    const key_value_part = struct_name[prefix_len..];
    // Find the $ to separate key and value types
    var dollar_pos: ?usize = null;
    for (key_value_part, 0..) |c, i| {
        if (c == '$') {
            dollar_pos = i;
            break;
        }
    }
    const key_type = if (dollar_pos) |pos| key_value_part[0..pos] else key_value_part;
    const value_type = if (dollar_pos) |pos| key_value_part[pos + 1 ..] else "";

    // Check if key/value types have COW semantics (has_managed_buffer)
    const key_has_managed_buffer = if (self.type_map.get(key_type)) |ti|
        ti == .struct_type and ti.struct_type.has_managed_buffer
    else
        false;
    const value_has_managed_buffer = if (self.type_map.get(value_type)) |ti|
        ti == .struct_type and ti.struct_type.has_managed_buffer
    else
        false;

    // Get pointer to keys array at offset 0 (Array$K which has __ManagedArray as first field)
    const keys_ptr = map_ptr;
    // If keys have COW semantics, cleanup occupied slots only (check states array)
    if (key_has_managed_buffer) {
        try emitMapManagedKeysCleanup(self, map_ptr);
    }
    // Cleanup the keys array using refcounted decref
    try array.emitManagedArrayDecref(self, ir.toManagedArrayPtr(keys_ptr), "m.keys", "map keys cleanup");

    // Get pointer to values array at offset 32 (Array$V which has __ManagedArray as first field)
    const values_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.VALUES_OFFSET);
    // If values have COW semantics, cleanup occupied slots only (check states array)
    if (value_has_managed_buffer) {
        try emitMapManagedValuesCleanup(self, map_ptr);
    }
    // Cleanup the values array using refcounted decref
    try array.emitManagedArrayDecref(self, ir.toManagedArrayPtr(values_ptr.raw()), "m.values", "map values cleanup");

    // Get pointer to states array at offset 64 (Array$SlotState which has __ManagedArray as first field)
    const states_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.STATES_OFFSET);
    // Cleanup the states array using refcounted decref
    try array.emitManagedArrayDecref(self, ir.toManagedArrayPtr(states_ptr.raw()), "m.states", "map states cleanup");
}

/// Emit cleanup code for Map keys with COW semantics, checking the states array for occupied slots.
/// Only decrefs buffers for slots where states[i] != 0 (not EMPTY).
/// Note: Currently uses String.size() since String is the only COW-semantics type.
fn emitMapManagedKeysCleanup(self: *AstToIr, map_ptr: ir.Value) !void {
    // Map layout:
    //   offset 0:  keys array (32 bytes) - buffer_ptr at offset 0
    //   offset 64: states array (32 bytes) - buffer_ptr at offset 64
    //   offset 104: capacity (8 bytes)

    // Load capacity to know how many slots to iterate
    const cap_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.CAPACITY_OFFSET);
    const capacity = try self.func().emitLoad(cap_ptr.raw(), .i64);

    // Skip cleanup if capacity is 0
    const zero = try self.func().emitConstI64(0);
    const is_empty = try self.func().emitBinaryOp(.icmp_eq, capacity, zero, .i64);

    // Allocate loop counter on stack
    const counter_ptr = try self.func().emitAlloca(.i64);
    try self.func().emitStore(counter_ptr.raw(), zero);

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
    const cond_cap_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.CAPACITY_OFFSET);
    const cond_cap = try self.func().emitLoad(cond_cap_ptr.raw(), .i64);
    const cond_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const done = try self.func().emitBinaryOp(.icmp_ge, cond_i, cond_cap, .i64);

    try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = done }, .{ .block_ref = end_block_idx }, .{ .block_ref = body_block_idx } },
    });

    // Restore body block (deferred index 5)
    try deferred.restore(self, 5);

    // Body block: check if states[i] == 1 (OCCUPIED)
    const body_i = try self.func().emitLoad(counter_ptr.raw(), .i64);

    // Get states buffer pointer (at offset 64 of map, then offset 0 of Array)
    const states_array_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.STATES_OFFSET);
    const states_buf_ptr = try self.func().emitLoad(states_array_ptr.raw(), .ptr);

    // Load states[i] - each state is 8 bytes (int)
    const eight = try self.func().emitConstI64(8);
    const state_offset = try self.func().emitBinaryOp(.mul, body_i, eight, .i64);
    const state_ptr = try self.func().emitBinaryOp(.add, states_buf_ptr, state_offset, .ptr);
    const state = try self.func().emitLoad(state_ptr, .i64);

    // Check if state != 0 (EMPTY) - we need to cleanup both OCCUPIED (1) and DELETED (2) slots
    // DELETED slots may still hold string keys that haven't been cleaned up when remove() was called
    const is_not_empty = try self.func().emitBinaryOp(.icmp_ne, state, zero, .i64);

    // If not empty, go to occupied block; otherwise skip to next
    try self.func().blocks.items[body_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_not_empty }, .{ .block_ref = occupied_block_idx }, .{ .block_ref = next_block_idx } },
    });

    // Restore occupied block (deferred index 4)
    try deferred.restore(self, 4);

    // Occupied block: get keys[i] and check if it's heap-allocated
    const occ_i = try self.func().emitLoad(counter_ptr.raw(), .i64);

    // Get keys buffer pointer (at offset 0 of map)
    const keys_buf_ptr = try self.func().emitLoad(map_ptr, .ptr);

    // Calculate element pointer: buf_ptr + i * 40 (String size)
    const elem_size = try self.func().emitConstI64(String.size());
    const offset = try self.func().emitBinaryOp(.mul, occ_i, elem_size, .i64);
    const elem_ptr = try self.func().emitBinaryOp(.add, keys_buf_ptr, offset, .ptr);

    // Check if heap mode: cap_flags & 3 == 1
    // cap_flags is at offset 24 in the String
    const cap_flags_ptr = try ManagedArray.getFlagsPtr(self.func(), ir.toManagedArrayPtr(elem_ptr));
    const cap_flags = try self.func().emitLoad(cap_flags_ptr.raw(), .i32);
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

    // Decref block: decrement refcount in buffer header and check if zero
    const decref_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const decref_keys_buf_ptr = try self.func().emitLoad(map_ptr, .ptr);
    const decref_offset = try self.func().emitBinaryOp(.mul, decref_i, elem_size, .i64);
    const decref_elem_ptr = try self.func().emitBinaryOp(.add, decref_keys_buf_ptr, decref_offset, .ptr);

    // Buffer header refcount: load buf_ptr from String, subtract 8 to get header
    const str_buf_ptr_decref = try self.func().emitLoad(decref_elem_ptr, .ptr);
    const eight_decref = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, str_buf_ptr_decref, eight_decref, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one_ref = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one_ref, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    // Emit tracking call for decref
    if (self.track_memory) {
        try self.func().emitTrackDecref("<map key>", new_ref);
    }

    // Check if refcount is now zero
    const zero_ref = try self.func().emitConstI64(0);
    const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero_ref, .i64);

    // Branch: if zero, go to heap_free; otherwise go to next
    try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_zero }, .{ .block_ref = heap_free_block_idx }, .{ .block_ref = next_block_idx } },
    });

    // Restore heap_free block (deferred index 2)
    try deferred.restore(self, 2);

    // Heap free block: recompute elem_ptr and free buffer at header (buf_ptr - 8)
    const heap_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const heap_keys_buf_ptr = try self.func().emitLoad(map_ptr, .ptr);
    const heap_elem_size = try self.func().emitConstI64(String.size());
    const heap_offset = try self.func().emitBinaryOp(.mul, heap_i, heap_elem_size, .i64);
    const heap_elem_ptr = try self.func().emitBinaryOp(.add, heap_keys_buf_ptr, heap_offset, .ptr);

    // Free the buffer including header and jump to next
    const str_buf_ptr = try self.func().emitLoad(heap_elem_ptr, .ptr);
    const eight_free = try self.func().emitConstI64(8);
    const free_header_ptr = try self.func().emitBinaryOp(.sub, str_buf_ptr, eight_free, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(free_header_ptr), "map string key cleanup");
    try self.func().emitBr(next_block_idx);

    // Restore next block (deferred index 1)
    try deferred.restore(self, 1);

    // Next block: increment counter and branch to cond
    const next_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const next_one = try self.func().emitConstI64(1);
    const next_val = try self.func().emitBinaryOp(.add, next_i, next_one, .i64);
    try self.func().emitStore(counter_ptr.raw(), next_val);
    try self.func().emitBr(cond_block_idx);

    // Restore end block
    try deferred.restore(self, 0);
}

/// Emit cleanup code for Map values with COW semantics, checking the states array for occupied slots.
/// Only decrefs buffers for slots where states[i] == 1 (OCCUPIED).
/// Note: Currently uses String.size() since String is the only COW-semantics type.
fn emitMapManagedValuesCleanup(self: *AstToIr, map_ptr: ir.Value) !void {
    // Map layout:
    //   offset 32: values array (32 bytes) - buffer_ptr at offset 0 of Array
    //   offset 64: states array (32 bytes) - buffer_ptr at offset 0 of Array
    //   offset 104: capacity (8 bytes)

    // Load capacity to know how many slots to iterate
    const cap_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.CAPACITY_OFFSET);
    const capacity = try self.func().emitLoad(cap_ptr.raw(), .i64);

    // Skip cleanup if capacity is 0
    const zero = try self.func().emitConstI64(0);
    const is_empty = try self.func().emitBinaryOp(.icmp_eq, capacity, zero, .i64);

    // Allocate loop counter on stack
    const counter_ptr = try self.func().emitAlloca(.i64);
    try self.func().emitStore(counter_ptr.raw(), zero);

    // Create blocks - all created upfront to ensure stable indices
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("map_val_cleanup_cond");
    const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("map_val_cleanup_body");
    const occupied_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("map_val_cleanup_occupied");
    const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("map_val_cleanup_decref");
    const heap_free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("map_val_heap_free");
    const next_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("map_val_cleanup_next");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("map_val_cleanup_end");

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
    const cond_cap_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.CAPACITY_OFFSET);
    const cond_cap = try self.func().emitLoad(cond_cap_ptr.raw(), .i64);
    const cond_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const done = try self.func().emitBinaryOp(.icmp_ge, cond_i, cond_cap, .i64);

    try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = done }, .{ .block_ref = end_block_idx }, .{ .block_ref = body_block_idx } },
    });

    // Restore body block (deferred index 5)
    try deferred.restore(self, 5);

    // Body block: check if states[i] != 0 (EMPTY) - cleanup both OCCUPIED (1) and DELETED (2) slots
    const body_i = try self.func().emitLoad(counter_ptr.raw(), .i64);

    // Get states buffer pointer (at offset 64 of map, then offset 0 of Array)
    const states_array_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.STATES_OFFSET);
    const states_buf_ptr = try self.func().emitLoad(states_array_ptr.raw(), .ptr);

    // Load states[i] - each state is 8 bytes (int)
    const eight = try self.func().emitConstI64(8);
    const state_offset = try self.func().emitBinaryOp(.mul, body_i, eight, .i64);
    const state_ptr = try self.func().emitBinaryOp(.add, states_buf_ptr, state_offset, .ptr);
    const state = try self.func().emitLoad(state_ptr, .i64);

    // Check if state != 0 (EMPTY) - we need to cleanup both OCCUPIED (1) and DELETED (2) slots
    // DELETED slots may still hold string values that haven't been cleaned up when remove() was called
    const is_not_empty = try self.func().emitBinaryOp(.icmp_ne, state, zero, .i64);

    // If not empty, go to occupied block; otherwise skip to next
    try self.func().blocks.items[body_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_not_empty }, .{ .block_ref = occupied_block_idx }, .{ .block_ref = next_block_idx } },
    });

    // Restore occupied block (deferred index 4)
    try deferred.restore(self, 4);

    // Occupied block: get values[i] and check if it's heap-allocated
    const occ_i = try self.func().emitLoad(counter_ptr.raw(), .i64);

    // Get values buffer pointer (at offset 32 of map, then offset 0 of Array)
    const values_array_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.VALUES_OFFSET);
    const values_buf_ptr = try self.func().emitLoad(values_array_ptr.raw(), .ptr);

    // Calculate element pointer: buf_ptr + i * 40 (String size)
    const elem_size = try self.func().emitConstI64(String.size());
    const offset = try self.func().emitBinaryOp(.mul, occ_i, elem_size, .i64);
    const elem_ptr = try self.func().emitBinaryOp(.add, values_buf_ptr, offset, .ptr);

    // Check if heap mode: cap_flags & 3 == 1
    // cap_flags is at offset 24 in the String
    const cap_flags_ptr = try ManagedArray.getFlagsPtr(self.func(), ir.toManagedArrayPtr(elem_ptr));
    const cap_flags = try self.func().emitLoad(cap_flags_ptr.raw(), .i32);
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

    // Decref block: decrement refcount in buffer header and check if zero
    const decref_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const decref_values_array_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.VALUES_OFFSET);
    const decref_values_buf_ptr = try self.func().emitLoad(decref_values_array_ptr.raw(), .ptr);
    const decref_offset = try self.func().emitBinaryOp(.mul, decref_i, elem_size, .i64);
    const decref_elem_ptr = try self.func().emitBinaryOp(.add, decref_values_buf_ptr, decref_offset, .ptr);

    // Buffer header refcount: load buf_ptr from String, subtract 8 to get header
    const str_buf_ptr_decref = try self.func().emitLoad(decref_elem_ptr, .ptr);
    const eight_decref = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, str_buf_ptr_decref, eight_decref, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one_ref = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one_ref, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    // Emit tracking call for decref
    if (self.track_memory) {
        try self.func().emitTrackDecref("<map value>", new_ref);
    }

    // Check if refcount is now zero
    const zero_ref = try self.func().emitConstI64(0);
    const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero_ref, .i64);

    // Branch: if zero, go to heap_free; otherwise go to next
    try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_zero }, .{ .block_ref = heap_free_block_idx }, .{ .block_ref = next_block_idx } },
    });

    // Restore heap_free block (deferred index 2)
    try deferred.restore(self, 2);

    // Heap free block: recompute elem_ptr and free buffer at header (buf_ptr - 8)
    const heap_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const heap_values_array_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(map_ptr), Map.VALUES_OFFSET);
    const heap_values_buf_ptr = try self.func().emitLoad(heap_values_array_ptr.raw(), .ptr);
    const heap_elem_size = try self.func().emitConstI64(String.size());
    const heap_offset = try self.func().emitBinaryOp(.mul, heap_i, heap_elem_size, .i64);
    const heap_elem_ptr = try self.func().emitBinaryOp(.add, heap_values_buf_ptr, heap_offset, .ptr);

    // Free the buffer including header and jump to next
    const str_buf_ptr = try self.func().emitLoad(heap_elem_ptr, .ptr);
    const eight_free = try self.func().emitConstI64(8);
    const free_header_ptr = try self.func().emitBinaryOp(.sub, str_buf_ptr, eight_free, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(free_header_ptr), "map string value cleanup");
    try self.func().emitBr(next_block_idx);

    // Restore next block (deferred index 1)
    try deferred.restore(self, 1);

    // Next block: increment counter and branch to cond
    const next_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const next_one = try self.func().emitConstI64(1);
    const next_val = try self.func().emitBinaryOp(.add, next_i, next_one, .i64);
    try self.func().emitStore(counter_ptr.raw(), next_val);
    try self.func().emitBr(cond_block_idx);

    // Restore end block
    try deferred.restore(self, 0);
}
/// Convert a map literal expression to IR.
/// Creates a Map instance by allocating temporary buffers for keys and values,
/// then calling the Map$init method with InitableFromDictionaryLiteral interface.
pub fn convertMapLiteral(self: *AstToIr, map_lit: ast.MapLiteralExpr) ast_to_ir.ConvertError!ast_to_ir.TypedValue {
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
            try self.func().emitMemcpy(key_ptr.asRawPtr(), ir.toRawPtr(key_typed.value), @intCast(key_elem_size));
        } else {
            try self.func().emitStore(key_ptr.raw(), key_typed.value);
        }

        // NOTE: Do NOT remove string keys from temporaries.
        // Map$init COPIES keys with incref, so it creates its own references.
        // The original temporary strings should be cleaned up normally at scope end.
        // The map's copies will survive because they have their own refcounts.

        const value_ptr = try self.func().emitGetElemPtr(values_buffer, idx_val, @intCast(value_elem_size));
        if (value_elem_size > 8) {
            try self.func().emitMemcpy(value_ptr.asRawPtr(), ir.toRawPtr(value_typed.value), @intCast(value_elem_size));
        } else {
            try self.func().emitStore(value_ptr.raw(), value_typed.value);
        }

        // NOTE: Do NOT remove string values from temporaries.
        // Map$init COPIES values with incref, so it creates its own references.
        // The original temporary strings should be cleaned up normally at scope end.
    }

    // Create __ManagedArrays for keys and values
    const keys_managed_ptr = try array.emitManagedArray(self, keys_buffer, elem_count, elem_count);
    const values_managed_ptr = try array.emitManagedArray(self, values_buffer, elem_count, elem_count);

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
    args[0] = result_ptr.raw();
    args[1] = keys_managed_ptr.raw();
    args[2] = values_managed_ptr.raw();

    // Call init with sret
    _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

    // Map$init copies all keys/values with incref into its internal storage.
    // The original temporary strings remain tracked and will be cleaned up
    // normally at scope end via cleanupTemporaryStrings().

    // Free the temporary buffers used to pass keys and values to init
    // The Map$init function copies the data into its internal hash table,
    // so we need to clean up the temporary managed arrays
    debug.astToIr("Map literal: emitting heap.free for temp buffers in block {d}", .{self.func().blocks.items.len - 1});
    try self.func().emitHeapFree(keys_buffer, "map literal keys cleanup");
    try self.func().emitHeapFree(values_buffer, "map literal values cleanup");

    return .{
        .value = result_ptr.raw(),
        .ty = try self.typeNameToValueType(map_type_name),
    };
}
