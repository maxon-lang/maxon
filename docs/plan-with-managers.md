# Plan: Rebuild AST-to-IR Stage with OwnershipManager and MemoryManager

## Overview

Rebuild the AST to IR stage of the Maxon compiler from scratch with two dedicated managers:
1. **OwnershipManager** - Tracks ownership state, move/borrow semantics, reference counting
2. **MemoryManager** - Tracks allocations, emits heap operations, detects double-free

Both managers will support optional runtime debug printing when `--track-memory` is enabled.

## File Structure

```
maxon-bin/src/compiler/
  4-ast_to_ir.zig              # Main entry point + exports
  ast_to_ir_types.zig          # Type definitions (External* types, ValueType, etc.)
  ast_to_ir_type_mapper.zig    # TypeMapper (Maxon types → IR types)
  ast_to_ir_method_resolver.zig # MethodResolver (method lookup + generics)
  ast_to_ir_errors.zig         # ErrorCollector (error handling infrastructure)
  ast_to_ir_blocks.zig         # BlockManager (control flow tracking)
  ast_to_ir_resources.zig      # ResourceManager (unified ownership + memory)
  ast_to_ir_cleanup.zig        # Cleanup code generation helpers
  ast_to_ir_intrinsics.zig     # Intrinsic function handling
  ast_to_ir_cstring.zig        # C-string conversions
  ast_to_ir_error_union.zig    # Error union handling
```

## Phase 1: Type Definitions (ast_to_ir_types.zig)

Define core types needed by the compiler:

```zig
// External type metadata for cross-module compilation
pub const ExternalTypeInfo = struct { ... };
pub const ExternalFuncSignature = struct { ... };
pub const ExternalInterfaceInfo = struct { ... };
pub const ExternalExtensionInfo = struct { ... };
pub const ExternalEnumInfo = struct { ... };
pub const ExternalTypeAliasInfo = struct { ... };
pub const ExternalValueType = struct { ... };
pub const ExternalParamType = struct { ... };

// Internal type representations
pub const ValueType = union(enum) { ... };
pub const FieldInfo = struct { ... };
pub const ParamType = struct { ... };
```

## Phase 2: ResourceManager (ast_to_ir_resources.zig)

**Unified ownership + memory management** - A single manager handles the entire variable lifecycle.

Ownership and memory are fundamentally intertwined:
- When ownership moves, cleanup responsibility moves
- When a scope ends, ownership determines what gets cleaned up
- Reference counting is an ownership concern that affects memory

```zig
pub const OwnershipState = enum {
    owned,      // This scope owns the value and will clean it up
    moved,      // Ownership transferred elsewhere
    borrowed,   // Temporarily accessed, original owner cleans up
};

pub const VariableInfo = struct {
    name: []const u8,
    ptr: ir.Value,              // IR pointer to storage
    value_type: ValueType,      // Type info (determines cleanup strategy)
    is_mutable: bool,           // let vs var
    state: OwnershipState,
    moved_at: ?SourceLocation,  // For error messages
};

pub const ResourceManager = struct {
    allocator: std.mem.Allocator,
    func: *ir.Function,
    track_memory: bool,
    mutation_analyzer: *const MutationAnalyzer,
    error_info: *?compile_error.CompileError,

    // Scope stack
    scopes: std.ArrayListUnmanaged(Scope),

    const Scope = struct {
        variables: std.StringHashMapUnmanaged(VariableInfo),
    };

    // === Scope Lifecycle ===
    pub fn beginScope() !void;
    pub fn endScope() !void;  // Auto-cleans up owned variables

    // === Variable Declaration ===
    // Allocates storage and registers with owned state
    pub fn declareVariable(name: []const u8, value_type: ValueType, is_mutable: bool) !ir.Value;

    // === Variable Access (compile-time checks) ===
    pub fn useVariable(name: []const u8, loc: SourceLocation) !ir.Value;  // E008 if moved
    pub fn getVariablePtr(name: []const u8) ?ir.Value;  // No ownership check

    // === Ownership Transfer ===
    // Called when passing to a function - checks mutation analysis
    pub fn passToFunction(name: []const u8, func_name: []const u8, param_idx: usize, loc: SourceLocation) !void;

    // Called on reassignment - restores ownership
    pub fn reassign(name: []const u8) !void;

    // === Control Flow ===
    pub fn snapshot() !StateSnapshot;
    pub fn restore(snapshot: StateSnapshot) !void;
    pub fn merge(then_snapshot: StateSnapshot, else_snapshot: StateSnapshot) !void;

    // === Deferred Ownership for Struct Literals ===
    // Allows a variable to be used multiple times in the same struct literal
    // Moves are deferred until endStructLiteral is called
    pub fn beginStructLiteral() void;
    pub fn endStructLiteral() !void;  // Applies all pending moves
    pub fn hasPendingMove(name: []const u8) bool;  // Check if move is pending

};
```

### Resource Tracking with Origin-Based IDs

When `--track-memory` is enabled, every resource gets a unique ID that encodes its origin (function + variable name). This makes it easy to trace a resource through its entire lifecycle.

```zig
/// Resource ID encodes origin: "function_name:variable_name"
/// For nested resources: "function_name:parent.field"
pub const ResourceId = []const u8;  // e.g., "main:arr", "process:data.buffer"

pub const ResourceManager = struct {
    // ... existing fields ...

    current_function: []const u8,  // For building resource IDs

    // === Tracking API (emits runtime debug output) ===

    // Builds ID from current function + variable name
    fn makeResourceId(name: []const u8) ResourceId;
    // e.g., in function "main" with var "arr" → "main:arr"
    // For nested: "main:container.data"

    // Called when a resource is allocated
    fn trackAlloc(id: ResourceId, type_name: []const u8, size: ir.Value) !void;
    // Output: "ALLOC main:arr (IntArray) 32 bytes"

    // Called when heap buffer is reallocated
    fn trackRealloc(id: ResourceId, old_size: ir.Value, new_size: ir.Value) !void;
    // Output: "REALLOC main:arr 32 -> 64 bytes"

    // Called when ownership transfers
    fn trackMove(id: ResourceId, to_context: []const u8) !void;
    // Output: "MOVE main:arr -> mutateArray:param"

    // Called when a reference is created
    fn trackIncref(id: ResourceId, new_count: ir.Value) !void;
    // Output: "INCREF main:arr rc=2"

    // Called when a reference is released
    fn trackDecref(id: ResourceId, new_count: ir.Value) !void;
    // Output: "DECREF main:arr rc=1"

    // Called when cleanup begins
    fn trackCleanup(id: ResourceId) !void;
    // Output: "CLEANUP main:arr"

    // Called when memory is freed
    fn trackFree(id: ResourceId, size: ir.Value) !void;
    // Output: "FREE main:arr 32 bytes"
};
```

**Example trace output for `--track-memory`:**
```
ALLOC main:arr (IntArray) 32 bytes
INCREF main:arr rc=1
REALLOC main:arr 32 -> 64 bytes
MOVE main:arr -> mutateArray:data
CLEANUP mutateArray:data
DECREF mutateArray:data rc=0
FREE mutateArray:data 64 bytes

=== MEMORY STATS ===
Allocated: 64 bytes
Freed:     64 bytes
Leaked:    0 bytes
```

**Nested resource IDs:**
```
ALLOC main:container (Container) 48 bytes
ALLOC main:container.data (IntArray) 32 bytes
MOVE main:container.data -> process:arr
CLEANUP main:container          // skips .data (was moved)
FREE main:container 48 bytes
CLEANUP process:arr
FREE process:arr 32 bytes
```

**ID rules:**
- ID encodes origin: `function:variable` or `function:parent.field`
- When moved, the ID updates to reflect new owner: `main:arr` → `process:param`
- Nested resources include parent path: `main:container.data.buffer`
```

**Key behaviors:**
- `declareVariable` → allocates storage, sets state to `owned`
- `useVariable` → returns error E008 if state is `moved`
- `passToFunction` → checks MutationAnalyzer; determines move vs borrow based on destination mutability
- `reassign` → restores state to `owned`
- `endScope` → for each variable still `owned`, emits cleanup code

### Move vs Borrow Rules

The decision is based on **where the parameter value ends up**, not just direct mutation:

**Borrow** (caller keeps ownership):
- Parameter is only read (not stored anywhere)
- Parameter is assigned to an **immutable** (`let`) field or variable
- Safe to share because destination can't mutate it

**Move** (ownership transfers):
- Parameter is directly mutated (assigned to, fields modified)
- Parameter is assigned to a **mutable** (`var`) field or variable
- New owner might mutate, so original must give up ownership

```maxon
type Token
    let text String    // immutable field
end 'Token'

type Wrapper
    var data String    // mutable field
end 'Wrapper'

function tokenize(s String) returns Token
    return {text: s}   // s → let field = BORROW
end 'tokenize'

function wrap(s String) returns Wrapper
    return {data: s}   // s → var field = MOVE
end 'wrap'

function main() returns int
    var text = "hello"
    var t1 = tokenize(text)  // borrows - text still usable
    var t2 = tokenize(text)  // can borrow again
    var w = wrap(text)       // moves - text no longer usable
    return 0
end 'main'
```

The MutationAnalyzer tracks:
1. Direct parameter mutation (assignment, field modification)
2. Assignment to `var` fields in struct literals
3. Assignment to `var` local variables
4. Passing to other functions that move

### Error Detection and Validation

The ResourceManager validates all operations and panics on invalid states. These are compiler bugs (not user errors) - they indicate the AST-to-IR logic is wrong.

```zig
pub const ResourceError = error{
    // === Ownership Errors (compiler bugs) ===
    UseAfterMove,           // Trying to use a resource that was already moved
    MoveFromImmutable,      // Trying to move from a let binding
    DoubleFree,             // Trying to cleanup/free an already-freed resource
    FreeUnowned,            // Trying to free a resource we don't own
    MoveUnowned,            // Trying to move a resource we don't own
    BorrowAfterMove,        // Trying to borrow something that was moved

    // === Memory Errors (compiler bugs) ===
    ReallocFreed,           // Trying to realloc a freed buffer
    ReallocUnowned,         // Trying to realloc a buffer we don't own

    // === Scope Errors (compiler bugs) ===
    VariableNotFound,       // Variable doesn't exist in any scope
    VariableAlreadyDeclared,// Declaring same variable twice in same scope
    ScopeUnderflow,         // endScope called without matching beginScope
    LeakedResources,        // Resources still owned when scope ends unexpectedly

    // === Nesting Errors (compiler bugs) ===
    ParentNotFound,         // Trying to access field of non-existent parent
    ChildAlreadyMoved,      // Parent cleanup found child in invalid state
    OrphanedChild,          // Child exists but parent doesn't

    // === Tracking Errors (compiler bugs) ===
    DuplicateResourceId,    // Same resource ID used twice
    UnknownResourceId,      // Tracking operation on unknown ID
    InconsistentRefCount,   // Refcount went negative or overflowed
};
```

**Validation in each operation:**

```zig
pub fn useVariable(name: []const u8, loc: SourceLocation) !ir.Value {
    const node = self.findResource(name) orelse return error.VariableNotFound;
    if (node.state == .moved) {
        // Compile-time error E008
        self.reportError(E008, "use after move: '{s}'", .{name}, loc);
        return error.UseAfterMove;
    }
    if (node.state == .freed) {
        @panic("COMPILER BUG: useVariable on freed resource");
    }
    return node.ptr;
}

pub fn passToFunction(name: []const u8, func_name: []const u8, param_idx: usize, loc: SourceLocation) !void {
    const node = self.findResource(name) orelse return error.VariableNotFound;

    // Check state
    if (node.state == .moved) return error.UseAfterMove;
    if (node.state == .freed) @panic("COMPILER BUG: passing freed resource to function");

    // Check if function mutates this param
    if (self.mutation_analyzer.isMutated(func_name, param_idx)) {
        // Move semantics
        if (!node.is_mutable) {
            self.reportError(E010, "cannot move from immutable variable: '{s}'", .{name}, loc);
            return error.MoveFromImmutable;
        }
        node.state = .moved;
        node.moved_at = loc;
        self.trackMove(node.id, func_name ++ ":" ++ param_name);
    }
    // else: borrow semantics, no state change
}

pub fn endScope() !void {
    if (self.scopes.items.len == 0) {
        @panic("COMPILER BUG: endScope with no active scope");
    }

    const scope = self.scopes.pop();
    for (scope.variables.values()) |*node| {
        switch (node.state) {
            .owned => {
                // Normal: emit cleanup
                try self.cleanupResource(node);
            },
            .moved => {
                // Normal: skip cleanup, new owner handles it
            },
            .borrowed => {
                @panic("COMPILER BUG: borrowed resource at scope end");
            },
            .freed => {
                @panic("COMPILER BUG: freed resource still in scope");
            },
        }
    }
}

fn cleanupResource(node: *ResourceNode) !void {
    if (node.state == .freed) {
        @panic("COMPILER BUG: double free of resource " ++ node.id);
    }
    if (node.state == .moved) {
        @panic("COMPILER BUG: cleanup of moved resource " ++ node.id);
    }
    if (node.state != .owned) {
        @panic("COMPILER BUG: cleanup of unowned resource " ++ node.id);
    }

    // Cleanup children first (depth-first)
    for (node.children.items) |*child| {
        if (child.state == .owned) {
            try self.cleanupResource(child);
        } else if (child.state == .freed) {
            @panic("COMPILER BUG: child already freed before parent cleanup");
        }
        // moved children are skipped (new owner cleans them)
    }

    // Emit cleanup IR
    try self.emitCleanup(node);
    node.state = .freed;
    self.trackFree(node.id, node.size);
}
```

**Refcount validation:**
```zig
fn trackIncref(node: *ResourceNode) !void {
    node.refcount += 1;
    if (node.refcount < 1) {
        @panic("COMPILER BUG: refcount overflow");
    }
    self.emitTrackIncref(node.id, node.refcount);
}

fn trackDecref(node: *ResourceNode) !void {
    if (node.refcount == 0) {
        @panic("COMPILER BUG: decref on zero refcount");
    }
    node.refcount -= 1;
    self.emitTrackDecref(node.id, node.refcount);
}
```

### Nested Resources

Resources can be nested (struct containing array, array of structs, struct containing struct with array, etc.). The ResourceManager must handle these correctly:

```zig
pub const ResourceNode = struct {
    id: ResourceId,             // Origin-based ID: "func:var" or "func:parent.field"
    name: []const u8,           // Just the variable/field name
    ptr: ir.Value,
    value_type: ValueType,
    is_mutable: bool,
    state: OwnershipState,
    moved_at: ?SourceLocation,
    alloc_loc: SourceLocation,

    // Nested resources (children)
    children: std.ArrayListUnmanaged(ResourceNode),
    parent: ?*ResourceNode,

    // Updates ID when ownership transfers (e.g., "main:arr" -> "process:param")
    fn transferTo(self: *ResourceNode, new_func: []const u8, new_name: []const u8) void;
};
```

**Nested resource rules:**

1. **Cleanup propagates down** - When a struct is cleaned up, all its fields that need cleanup are cleaned up first (depth-first, children before parent)

2. **Move propagates down** - When a struct is moved, all nested resources are also moved (the new owner is responsible for all of them)

3. **Field access creates child tracking** - Accessing `container.data` where `data` is an array creates a child resource node linked to the parent

4. **Parent cleanup skips moved children** - If a field was individually moved out, parent cleanup skips that field

5. **Partial moves are allowed** - You can move individual fields and still use other fields:
   ```maxon
   var c = Container{data: arr, name: "test"}
   takeArray(c.data)  // moves c.data only
   print(c.name)      // OK - c.name is still owned
   // cleanup of c will clean c.name but skip c.data
   ```

**Example:**
```maxon
type Container
    var data IntArray
    var name String
end 'Container'

var c = Container{...}  // c owns data and name
c.data.push(42)         // accessing c.data - tracked as child of c
takeArray(c.data)       // if takeArray mutates, c.data is moved
// c.name still owned by c
// cleanup of c will clean name, but skip data (already moved)
```

**ResourceManager additions for nesting:**
```zig
pub const ResourceManager = struct {
    // ... existing fields ...

    // === Nested Resource Access ===
    // Returns or creates a child resource for field access
    pub fn accessField(parent_name: []const u8, field_name: []const u8, field_type: ValueType) !*ResourceNode;

    // Returns or creates a child resource for array element
    pub fn accessElement(array_name: []const u8, element_type: ValueType) !*ResourceNode;

    // === Internal ===
    // Recursively cleanup a resource and its children (depth-first)
    fn cleanupResource(node: *ResourceNode) !void;

    // Recursively mark a resource and children as moved
    fn moveResource(node: *ResourceNode, loc: SourceLocation) !void;
};
```

## Phase 3: BlockManager (ast_to_ir_blocks.zig)

Control flow creates complexity for ownership tracking. The BlockManager handles:
- Tracking state changes per block
- Merging divergent branches
- Detecting invalid loop moves
- Coordinating cleanup placement

### The Problem

1. **Conditional moves** - A variable might be moved in one branch but not another
2. **Loop moves** - A variable moved in a loop body is invalid on the next iteration
3. **Early returns** - A return in a branch affects what's reachable afterward
4. **Nested blocks** - If inside if inside while, etc.
5. **Cleanup placement** - Where to emit cleanup code when blocks have multiple exit points

### Block Types

```zig
pub const BlockKind = enum {
    function,     // Function body - cleanup on return
    if_then,      // If-then branch
    if_else,      // If-else branch
    while_loop,   // While loop body - special move rules
    match_arm,    // Match case arm
    plain,        // Plain scope (defer, etc.)
};
```

### BlockManager API

```zig
pub const BlockState = struct {
    kind: BlockKind,
    entry_snapshot: OwnershipSnapshot,  // Variable states when entering block
    terminates: bool,                    // Does this block always return/break/continue?
    moves: std.StringHashMapUnmanaged(SourceLocation),  // Variables moved in this block
    reassigns: std.StringHashMapUnmanaged(void),        // Variables reassigned in this block
    parent_block: ?*BlockState,          // For nested block access
};

pub const BlockManager = struct {
    allocator: std.mem.Allocator,
    blocks: std.ArrayListUnmanaged(BlockState),

    // === Block Lifecycle ===
    pub fn enterBlock(kind: BlockKind, current_ownership: OwnershipSnapshot) !void;
    pub fn exitBlock() !BlockState;  // Returns state for merge analysis
    pub fn currentBlock() ?*BlockState;

    // === Termination Tracking ===
    pub fn markTerminates() void;  // Called on return/break/continue
    pub fn currentBlockTerminates() bool;

    // === Move/Reassign Recording ===
    pub fn recordMove(name: []const u8, loc: SourceLocation) !void;
    pub fn recordReassign(name: []const u8) !void;
    pub fn wasMovedInCurrentBlock(name: []const u8) bool;
    pub fn wasReassignedInCurrentBlock(name: []const u8) bool;

    // === Branch Merging ===
    pub fn mergeBranches(then_state: BlockState, else_state: ?BlockState) !MergeResult;

    // === Loop Safety ===
    pub fn checkLoopSafety(name: []const u8, use_loc: SourceLocation) !void;

    // === Cleanup Coordination ===
    pub fn needsCleanupBeforeExit() bool;
    pub fn getVariablesNeedingCleanup() []const []const u8;
};

pub const OwnershipSnapshot = struct {
    // Maps variable name -> ownership state at snapshot time
    states: std.StringHashMapUnmanaged(OwnershipState),

    pub fn clone(allocator: std.mem.Allocator) !OwnershipSnapshot;
    pub fn diff(other: OwnershipSnapshot) StateDiff;
};

pub const MergeResult = struct {
    // Variables that are definitely moved (moved in all non-terminating branches)
    definitely_moved: std.ArrayListUnmanaged([]const u8),
    // Variables that might be moved (moved in some branches, not others)
    // These are errors in Maxon - we don't allow "maybe moved" state
    conflicting: std.ArrayListUnmanaged(MoveConflict),
};

pub const MoveConflict = struct {
    name: []const u8,
    moved_in_branch: BlockKind,
    moved_at: SourceLocation,
    // Which branch has it as owned vs moved
};
```

### Branch Merge Rules

The merge logic determines the final ownership state after an if/else:

```zig
fn mergeBranches(then_state: BlockState, else_state: ?BlockState) !MergeResult {
    var result = MergeResult.init(self.allocator);

    // Case 1: No else branch
    if (else_state == null) {
        if (then_state.terminates) {
            // Then branch returns - after if-then, we're in the "else" path
            // Variables moved in then are NOT moved afterward
            return result;
        } else {
            // Then branch falls through
            // Any move in then affects the continuation
            for (then_state.moves.keys()) |name| {
                try result.definitely_moved.append(name);
            }
            return result;
        }
    }

    const else_st = else_state.?;

    // Case 2: Both branches terminate (return)
    if (then_state.terminates and else_st.terminates) {
        // Code after if-else is unreachable
        // No merging needed
        return result;
    }

    // Case 3: Only then terminates
    if (then_state.terminates) {
        // Continuation only reachable from else
        for (else_st.moves.keys()) |name| {
            try result.definitely_moved.append(name);
        }
        return result;
    }

    // Case 4: Only else terminates
    if (else_st.terminates) {
        // Continuation only reachable from then
        for (then_state.moves.keys()) |name| {
            try result.definitely_moved.append(name);
        }
        return result;
    }

    // Case 5: Neither terminates - both fall through
    // Variables must be in same state in both branches
    for (then_state.moves.keys()) |name| {
        if (else_st.moves.contains(name)) {
            // Moved in both - definitely moved
            try result.definitely_moved.append(name);
        } else {
            // Moved in then only - CONFLICT
            try result.conflicting.append(.{
                .name = name,
                .moved_in_branch = .if_then,
                .moved_at = then_state.moves.get(name).?,
            });
        }
    }

    // Check for moves only in else
    for (else_st.moves.keys()) |name| {
        if (!then_state.moves.contains(name)) {
            try result.conflicting.append(.{
                .name = name,
                .moved_in_branch = .if_else,
                .moved_at = else_st.moves.get(name).?,
            });
        }
    }

    return result;
}
```

### Loop Safety

Loops require special handling because the body might execute multiple times:

```zig
fn checkLoopSafety(name: []const u8, use_loc: SourceLocation) !void {
    const block = self.currentBlock() orelse return;

    // Only applies to while_loop blocks
    if (block.kind != .while_loop) return;

    // Check if this variable was moved earlier in the SAME loop iteration
    if (block.moves.get(name)) |move_loc| {
        // Using after move in same iteration - already caught by normal use-after-move
        return;
    }

    // The tricky case: move happens AFTER this use in the loop body
    // We detect this by doing a forward analysis pass
    // For now, we rely on the "move in loop must be preceded by reassign" rule
}

fn validateLoopBody(block: BlockState) !void {
    // After processing loop body, check that any moved variable was also reassigned
    for (block.moves.keys()) |name| {
        if (!block.reassigns.contains(name)) {
            // ERROR: variable moved in loop without reassignment
            // This would cause use-after-move on iteration 2
            return error.MoveInLoopWithoutReassign;
        }
    }
}
```

### Example: If-Else Move Handling

```maxon
function test(flag bool) returns int
    var x = SomeType{}

    if flag 'branch'
        consume(x)  // moves x
        return 0
    end 'branch'

    // x is still owned here because the if branch returned
    return x.value
end 'test'
```

Block trace:
1. Enter function block, declare `x` as owned
2. Enter if_then block, snapshot: `{x: owned}`
3. `consume(x)` - record move in current block, `x` now moved
4. `return 0` - mark block as terminates
5. Exit if_then block → `{terminates: true, moves: {x}}`
6. No else branch, then terminates → merge result: nothing definitely moved
7. After if: `x` is still owned (per entry snapshot)
8. `return x.value` - valid use

### Example: If-Else Conflict (COMPILE ERROR)

When a variable is moved in one branch but not another, and neither branch terminates (returns),
this is a **compile error** (not a warning, not conservatively moved). This is safer and matches Rust's behavior.

```maxon
function test(flag bool) returns int
    var x = SomeType{}

    if flag 'branch'
        consume(x)  // moves x
    end 'branch' else 'other'
        // x not moved
    end 'other'

    return x.value  // ERROR: x might be moved
end 'test'
```

Block trace:
1. Enter function block, declare `x` as owned
2. Enter if_then block, `consume(x)` moves x
3. Exit if_then → `{terminates: false, moves: {x}}`
4. Restore to entry snapshot, enter if_else block
5. Exit if_else → `{terminates: false, moves: {}}`
6. Merge branches: `x` moved in then but not else → **COMPILE ERROR**
7. Report error E008: "variable 'x' moved in 'if_then' branch but not 'if_else' branch"

### Example: Loop Move Error

```maxon
function test() returns int
    var x = SomeType{}
    var i = 0

    while i < 3 'loop'
        consume(x)  // ERROR: moves x, but loop iterates
        i = i + 1
    end 'loop'

    return 0
end 'test'
```

Block trace:
1. Enter while_loop block
2. `consume(x)` - record move
3. Exit while_loop, validate: `x` moved but not reassigned
4. ERROR: "variable 'x' moved in loop without reassignment"

### Example: Loop Move with Reassignment (Valid)

```maxon
function test() returns int
    var x = SomeType{}
    var i = 0

    while i < 3 'loop'
        consume(x)      // moves x
        x = SomeType{}  // reassigns x - restores ownership
        i = i + 1
    end 'loop'

    return 0
end 'test'
```

Block trace:
1. Enter while_loop block
2. `consume(x)` - record move
3. `x = SomeType{}` - record reassign
4. Exit while_loop, validate: `x` moved AND reassigned → OK

### Integration with ResourceManager

```zig
pub const ResourceManager = struct {
    // ... existing fields ...
    blocks: BlockManager,

    pub fn beginBlock(kind: BlockKind) !void {
        const snapshot = try self.captureOwnershipSnapshot();
        try self.blocks.enterBlock(kind, snapshot);
    }

    pub fn endBlock() !void {
        const block_state = try self.blocks.exitBlock();

        // For loops, validate move safety
        if (block_state.kind == .while_loop) {
            try self.blocks.validateLoopBody(block_state);
        }
    }

    pub fn markMoved(name: []const u8, loc: SourceLocation) !void {
        // Update variable state
        const node = try self.findVariable(name);
        node.state = .moved;
        node.moved_at = loc;

        // Record in block for merge analysis
        try self.blocks.recordMove(name, loc);
    }

    pub fn reassign(name: []const u8) !void {
        // Restore ownership
        const node = try self.findVariable(name);
        node.state = .owned;
        node.moved_at = null;

        // Record reassignment for loop validation
        try self.blocks.recordReassign(name);
    }

    pub fn finishIfElse(then_block: BlockState, else_block: ?BlockState) !void {
        const merge = try self.blocks.mergeBranches(then_block, else_block);

        // Report conflicts as errors
        for (merge.conflicting.items) |conflict| {
            self.reportError(
                .E008,
                "variable '{s}' moved in {s} branch but not the other",
                .{conflict.name, @tagName(conflict.moved_in_branch)},
                conflict.moved_at,
            );
        }

        // Apply definitely moved
        for (merge.definitely_moved.items) |name| {
            const node = try self.findVariable(name);
            node.state = .moved;
        }
    }
};
```

### Cleanup Placement

The BlockManager also helps determine where cleanup code should go:

1. **Normal scope exit**: Cleanup at end of block
2. **Early return**: Cleanup before the return instruction
3. **Break/continue**: Cleanup before the jump (for variables declared inside the loop)

For break/continue, we emit cleanup for locally declared variables before jumping.
This matches normal scope exit behavior and ensures variables in nested scopes are properly cleaned up.

```zig
pub fn emitCleanupForEarlyExit(resources: *ResourceManager, target_scope_depth: usize) !void {
    // Cleanup all owned variables from current scope down to target_scope_depth
    // For break/continue, target is the loop's parent scope
    // For return, target is scope 0 (function exit)

    var depth = resources.scopes.items.len;
    while (depth > target_scope_depth) {
        depth -= 1;
        const scope = resources.scopes.items[depth];

        for (scope.variables.values()) |*node| {
            if (node.state == .owned and node.value_type.needsCleanup()) {
                try resources.emitCleanup(node);
            }
        }
    }
}
```

Example:
```maxon
while i < 10 'loop'
    var temp = allocateSomething()  // needs cleanup
    if condition 'check'
        break  // must cleanup temp before jumping out
    end 'check'
end 'loop'
```

The break emits cleanup for `temp` before the jump instruction.

### Match Statement Merging

Match arms are treated like if-else branches. All non-terminating arms must agree on ownership state.

```maxon
match getChoice() 'select'
    1 then consume(x)
    2 then doSomething()  // doesn't move x
    default then consume(x)
end 'select'
// ERROR: arm 2 doesn't move x but arms 1 and default do
```

This is a compile error, just like if-else conflicts.

```zig
pub fn mergeMatchArms(arms: []const BlockState) !MergeResult {
    // Find all non-terminating arms
    var non_terminating = std.ArrayListUnmanaged(BlockState).init(self.allocator);
    for (arms) |arm| {
        if (!arm.terminates) {
            try non_terminating.append(arm);
        }
    }

    if (non_terminating.items.len == 0) {
        // All arms terminate - no merge needed
        return MergeResult.empty();
    }

    // All non-terminating arms must agree on which variables are moved
    const first = non_terminating.items[0];
    var result = MergeResult.init(self.allocator);

    for (first.moves.keys()) |name| {
        var all_move = true;
        for (non_terminating.items[1..]) |arm| {
            if (!arm.moves.contains(name)) {
                all_move = false;
                try result.conflicting.append(.{
                    .name = name,
                    .moved_in_branch = first.kind,
                    .moved_at = first.moves.get(name).?,
                });
                break;
            }
        }
        if (all_move) {
            try result.definitely_moved.append(name);
        }
    }

    // Check for variables moved only in other arms
    for (non_terminating.items[1..]) |arm| {
        for (arm.moves.keys()) |name| {
            if (!first.moves.contains(name)) {
                try result.conflicting.append(.{
                    .name = name,
                    .moved_in_branch = arm.kind,
                    .moved_at = arm.moves.get(name).?,
                });
            }
        }
    }

    return result;
}
```

### Deferred Struct Literal Ownership

When constructing a struct literal, a variable can be used multiple times before the move happens:

```maxon
var s = "hello"
var w = Wrapper{data: s, len: s.byteLength()}  // s used twice
// After struct literal: s is moved (if data is var field)
```

The ResourceManager defers moves during struct literal construction:

```zig
pub fn beginStructLiteral() void {
    self.in_struct_literal_depth += 1;
}

pub fn endStructLiteral() !void {
    self.in_struct_literal_depth -= 1;
    if (self.in_struct_literal_depth == 0) {
        // Apply all pending moves
        for (self.pending_moves.items) |pending| {
            try self.applyMove(pending.name, pending.loc);
        }
        self.pending_moves.clearRetainingCapacity();
    }
}

pub fn markMoved(name: []const u8, loc: SourceLocation) !void {
    if (self.in_struct_literal_depth > 0) {
        // Defer the move
        try self.pending_moves.append(.{ .name = name, .loc = loc });
    } else {
        try self.applyMove(name, loc);
    }
}

pub fn useVariable(name: []const u8, loc: SourceLocation) !ir.Value {
    // Check actual state, not pending
    const node = self.findVariable(name) orelse return error.VariableNotFound;
    if (node.state == .moved) {
        return error.UseAfterMove;
    }
    // Pending moves don't block use during same struct literal
    return node.ptr;
}
```
```

### Additional Edge Cases

#### Return Value Ownership
When a function returns a value, ownership transfers to the caller:
- New values created in the function → caller owns them
- Returning a parameter → parameter is moved into return value

```maxon
function passThrough(x SomeType) returns SomeType
    return x  // Moves x into return value, caller receives ownership
end 'passThrough'
```

#### Self-Assignment
Self-assignment (`x = x`) is a no-op for ownership purposes:
```zig
pub fn reassign(name: []const u8, source_name: ?[]const u8) !void {
    if (source_name != null and std.mem.eql(u8, name, source_name.?)) {
        // Self-assignment - no ownership change
        return;
    }
    // Normal reassignment logic...
}
```

#### Multiple Returns in Same Function
Each return point must handle cleanup for variables still owned at that point:
```maxon
function test(flag bool) returns SomeType
    var x = SomeType{}
    var y = OtherType{}  // needs cleanup
    if flag 'check'
        return x  // cleanup y, then return x
    end 'check'
    y.mutate()
    return x  // cleanup y, then return x
end 'test'
```

Both return paths emit cleanup for `y` before returning.

#### Compound Expressions and Evaluation Order
Expressions are evaluated left-to-right. A move in one part affects subsequent parts:
```maxon
var result = foo(a) + bar(a)  // OK if neither moves a
var result = foo(a) + mutate(a)  // OK - a borrowed first, then moved
var result = mutate(a) + foo(a)  // ERROR - a moved, then used
```

The converter processes arguments in order, so moves are detected correctly.

#### Error Paths (try/otherwise)
The `otherwise` clause is executed if the try fails. Both paths must be analyzed:
```maxon
var x = SomeType{}
var result = try riskyOp(x) otherwise {
    // x was passed to riskyOp - was it moved?
    // If riskyOp moves its param, x is moved regardless of success/failure
    return 0
}
```

For ownership, we analyze the function signature:
- If `riskyOp` borrows `x` → `x` still owned in both paths
- If `riskyOp` moves `x` → `x` moved in both paths (move happens before success/failure is known)

## Phase 4: Type System and Resolution

### Type Mapping (Maxon → IR)

Maxon types must be mapped to IR types for code generation:

```zig
pub const TypeMapper = struct {
    allocator: std.mem.Allocator,
    type_cache: std.StringHashMapUnmanaged(ir.Type),  // Cache computed IR types

    /// Map Maxon ValueType to IR Type
    pub fn mapType(self: *TypeMapper, value_type: ValueType) !ir.Type {
        return switch (value_type) {
            // Primitives
            .int_type => .i64,
            .float_type => .f64,
            .bool_type => .i1,
            .byte_type => .i8,
            .character_type => .i32,  // Unicode codepoint
            .void_type => .void,

            // Pointers
            .ptr_type => .ptr,
            .raw_ptr => .ptr,

            // Compound types
            .struct_type => |info| self.getOrCreateStructType(info),
            .array_type => |info| self.getOrCreateArrayType(info),
            .enum_type => |info| self.getOrCreateEnumType(info),
            .error_union => |info| self.getOrCreateErrorUnionType(info),
        };
    }

    /// Get struct type, creating if needed
    fn getOrCreateStructType(self: *TypeMapper, info: StructTypeInfo) !ir.Type {
        // Check cache first
        if (self.type_cache.get(info.name)) |cached| {
            return cached;
        }

        // Create struct type with fields
        var field_types = std.ArrayListUnmanaged(ir.Type){};
        for (info.fields) |field| {
            try field_types.append(self.allocator, try self.mapType(field.value_type));
        }

        const struct_type = ir.Type{
            .struct_type = .{
                .name = info.name,
                .field_types = field_types.items,
                .field_offsets = try self.computeOffsets(field_types.items),
                .size = try self.computeSize(field_types.items),
            },
        };

        try self.type_cache.put(self.allocator, info.name, struct_type);
        return struct_type;
    }
};
```

### Generic Instantiation

Generics are monomorphized (specialized) by the semantic analyzer. AST-to-IR receives:
- Concrete type names like `Map<string,int>` (not `Map from K to V`)
- Generic parameter bindings in the converter context

```zig
pub const GenericContext = struct {
    /// Maps generic param name → concrete type
    /// e.g., "KeyType" → "string", "ValueType" → "int"
    bindings: std.StringHashMapUnmanaged(ValueType),

    /// Resolve a type name, substituting generic params if needed
    pub fn resolveType(self: *GenericContext, type_name: []const u8) ValueType {
        // Check if it's a generic parameter
        if (self.bindings.get(type_name)) |concrete| {
            return concrete;
        }
        // Otherwise return as-is
        return .{ .named_type = type_name };
    }
};

pub const AstToIrConverter = struct {
    // ... existing fields ...
    generic_context: GenericContext,

    /// Called when entering a generic type's method
    pub fn enterGenericContext(self: *AstToIrConverter, type_decl: *ast.TypeDecl, type_args: []const ValueType) !void {
        // Bind generic params to concrete types
        for (type_decl.generic_params, type_args) |param, arg| {
            try self.generic_context.bindings.put(self.allocator, param, arg);
        }
    }

    pub fn exitGenericContext(self: *AstToIrConverter, type_decl: *ast.TypeDecl) void {
        for (type_decl.generic_params) |param| {
            _ = self.generic_context.bindings.remove(param);
        }
    }
};
```

### Method Resolution

Method calls must resolve to the correct implementation, considering:
1. Type methods (defined in type declaration)
2. Extension methods (defined in `extend` blocks)
3. Generic type methods (with type parameter substitution)
4. Primitive type methods (compiler intrinsics)

```zig
pub const MethodResolver = struct {
    type_map: *TypeMap,
    extension_map: *ExtensionMap,
    generic_context: *GenericContext,

    pub const ResolvedMethod = struct {
        kind: enum { type_method, extension_method, intrinsic },
        func_name: []const u8,     // Mangled name for IR
        receiver_type: ValueType,   // Actual type (after generic substitution)
        param_types: []const ValueType,
        return_type: ValueType,
    };

    /// Resolve a method call on a value of given type
    pub fn resolve(
        self: *MethodResolver,
        receiver_type: ValueType,
        method_name: []const u8,
    ) !?ResolvedMethod {
        // 1. Check for primitive intrinsics
        if (self.tryResolveIntrinsic(receiver_type, method_name)) |intrinsic| {
            return intrinsic;
        }

        // 2. Resolve generic parameters
        const concrete_type = self.generic_context.resolveType(receiver_type);

        // 3. Check type methods
        if (self.type_map.getMethod(concrete_type, method_name)) |method| {
            return .{
                .kind = .type_method,
                .func_name = method.mangled_name,
                .receiver_type = concrete_type,
                .param_types = method.param_types,
                .return_type = method.return_type,
            };
        }

        // 4. Check extension methods
        if (self.extension_map.getMethod(concrete_type, method_name)) |method| {
            return .{
                .kind = .extension_method,
                .func_name = method.mangled_name,
                .receiver_type = concrete_type,
                .param_types = method.param_types,
                .return_type = method.return_type,
            };
        }

        return null;  // Method not found
    }

    /// Check for compiler intrinsics on primitive types
    fn tryResolveIntrinsic(self: *MethodResolver, receiver_type: ValueType, method: []const u8) ?ResolvedMethod {
        _ = self;
        return switch (receiver_type) {
            .int_type => switch (method) {
                "hash" => .{ .kind = .intrinsic, ... },
                "equals" => .{ .kind = .intrinsic, ... },
                else => null,
            },
            .byte_type => switch (method) {
                "hash" => .{ .kind = .intrinsic, ... },
                "equals" => .{ .kind = .intrinsic, ... },
                else => null,
            },
            else => null,
        };
    }
};
```

### Error Handling Infrastructure

Compile errors are collected during conversion, allowing multiple errors to be reported:

```zig
pub const ErrorCollector = struct {
    allocator: std.mem.Allocator,
    errors: std.ArrayListUnmanaged(compile_error.CompileError),
    warnings: std.ArrayListUnmanaged(compile_error.CompileError),
    has_fatal: bool,

    pub fn init(allocator: std.mem.Allocator) ErrorCollector {
        return .{
            .allocator = allocator,
            .errors = .{},
            .warnings = .{},
            .has_fatal = false,
        };
    }

    /// Report a compile error (continues compilation to find more errors)
    pub fn reportError(
        self: *ErrorCollector,
        code: compile_error.ErrorCode,
        comptime fmt: []const u8,
        args: anytype,
        loc: SourceLocation,
    ) void {
        const message = std.fmt.allocPrint(self.allocator, fmt, args) catch return;
        self.errors.append(self.allocator, .{
            .code = code,
            .message = message,
            .message_allocated = true,
            .location = loc,
        }) catch return;
    }

    /// Report a warning (doesn't stop compilation)
    pub fn reportWarning(
        self: *ErrorCollector,
        comptime fmt: []const u8,
        args: anytype,
        loc: SourceLocation,
    ) void {
        const message = std.fmt.allocPrint(self.allocator, fmt, args) catch return;
        self.warnings.append(self.allocator, .{
            .code = .W001,
            .message = message,
            .message_allocated = true,
            .location = loc,
        }) catch return;
    }

    /// Report a fatal error (stops compilation)
    pub fn fatal(
        self: *ErrorCollector,
        code: compile_error.ErrorCode,
        comptime fmt: []const u8,
        args: anytype,
        loc: SourceLocation,
    ) error{CompileError} {
        self.reportError(code, fmt, args, loc);
        self.has_fatal = true;
        return error.CompileError;
    }

    /// Check if compilation should continue
    pub fn canContinue(self: *ErrorCollector) bool {
        return !self.has_fatal;
    }

    /// Get all errors for reporting
    pub fn getErrors(self: *ErrorCollector) []const compile_error.CompileError {
        return self.errors.items;
    }
};
```

**Error Recovery Strategy:**
- **Recoverable errors** (E008, E010): Report and continue to find more errors
- **Fatal errors**: Stop immediately (e.g., internal compiler errors)
- **Warnings**: Always continue

```zig
pub const AstToIrConverter = struct {
    // ... existing fields ...
    errors: ErrorCollector,

    fn convertStatement(self: *AstToIrConverter, stmt: *ast.Statement) !void {
        // ... conversion logic ...
    } catch |err| switch (err) {
        error.UseAfterMove, error.MoveFromImmutable => {
            // These are already reported - continue with other statements
            if (!self.errors.canContinue()) return err;
        },
        else => return err,
    };
};
```

## Phase 5: Helper Modules

### ast_to_ir_cleanup.zig
- Generate cleanup code for types with `needs_cleanup`
- Handle struct destructors, array cleanup
- Emit `track_cleanup` and `track_decref` operations

### ast_to_ir_intrinsics.zig
- Handle built-in intrinsic functions
- Array operations, string operations, etc.

### ast_to_ir_cstring.zig
- Convert Maxon strings to null-terminated C strings
- Used for extern function calls

### ast_to_ir_error_union.zig
- Handle error union types
- Generate try/throw/otherwise code

## Phase 5: Main Converter (4-ast_to_ir.zig)

### Required Public API

```zig
// Extraction functions
pub fn extractTypeInfo(program, allocator) ![]ExternalTypeInfo;
pub fn extractFunctionSignaturesFromAst(program, allocator) ![]ExternalFuncSignature;
pub fn extractInterfaceInfo(program, allocator) ![]ExternalInterfaceInfo;
pub fn extractExtensionInfo(program, allocator) ![]ExternalExtensionInfo;
pub fn extractEnumDecls(program, allocator, source_file) ![]ExternalEnumInfo;
pub fn extractTypeAliases(program, allocator, source_file) ![]ExternalTypeAliasInfo;

// Main conversion
pub fn convertWithExternals(
    program, allocator, mutation_analyzer, source_file,
    external_funcs, external_types, external_interfaces,
    external_extensions, external_enums, external_type_aliases,
    all_type_aliases, options, ir_error
) !ir.Module;

// Memory cleanup helpers
pub fn freeExternalParamTypes(allocator, param_types) void;
pub fn freeParamTypes(allocator, param_types) void;
pub fn freeValueTypeAllocations(allocator, value_type) void;

// Re-export types
pub const ExternalTypeInfo = types.ExternalTypeInfo;
pub const ExternalFuncSignature = types.ExternalFuncSignature;
// ... etc
```

### AstToIrConverter Structure

```zig
const AstToIrConverter = struct {
    allocator: std.mem.Allocator,
    ir_module: ir.Module,
    func: *ir.Function,

    // Unified resource management (ownership + memory)
    resources: ResourceManager,

    // Context
    type_map: TypeMap,
    source_file: ?[]const u8,
};
```

## Integration Points

### Variable Declaration
```zig
fn convertVarDecl(decl) !void {
    const ptr = try self.resources.declareVariable(decl.name, value_type, is_mutable);
    // ... generate init code to store value at ptr
}
```

### Variable Use
```zig
fn convertIdentifier(name, loc) !ir.Value {
    return try self.resources.useVariable(name, loc);  // E008 if moved
}
```

### Function Call Argument
```zig
fn convertCallArg(arg_name, func_name, param_idx, loc) !void {
    // ResourceManager checks mutation analysis internally
    // - If mutates: checks E010 (immutable), marks as moved
    // - If not: marks as borrowed (no state change)
    try self.resources.passToFunction(arg_name, func_name, param_idx, loc);
}
```

### Assignment (restores ownership)
```zig
fn convertAssign(target, value) !void {
    // ... generate value
    try self.resources.reassign(target);  // Restores to owned
}
```

### Control Flow (if/else, match)
```zig
fn convertIfStmt(if_stmt) !void {
    const before = try self.resources.snapshot();
    // convert then branch
    const after_then = try self.resources.snapshot();
    try self.resources.restore(before);
    // convert else branch
    const after_else = try self.resources.snapshot();
    try self.resources.merge(after_then, after_else);
}
```

### Scope (cleanup is automatic)
```zig
fn convertBlock(stmts) !void {
    try self.resources.beginScope();
    defer self.resources.endScope();  // Auto-cleans owned variables

    for (stmts) |stmt| {
        try self.convertStatement(stmt);
    }
}
```

## Implementation Order

1. **ast_to_ir_types.zig** - Type definitions (no dependencies)
2. **ast_to_ir_errors.zig** - ErrorCollector (depends on types)
3. **ast_to_ir_type_mapper.zig** - TypeMapper (depends on types, ir.zig)
4. **ast_to_ir_method_resolver.zig** - MethodResolver (depends on types, type_mapper)
5. **ast_to_ir_blocks.zig** - BlockManager (depends on types)
6. **ast_to_ir_resources.zig** - ResourceManager (depends on types, blocks, errors, ir.zig)
7. **ast_to_ir_cleanup.zig** - Cleanup helpers (depends on types, resources)
8. **ast_to_ir_intrinsics.zig** - Intrinsics (depends on types, resources, method_resolver)
9. **ast_to_ir_cstring.zig** - C-strings (depends on resources)
10. **ast_to_ir_error_union.zig** - Error unions (depends on types, resources)
11. **4-ast_to_ir.zig** - Main converter (depends on all above)

## Critical Files

- `maxon-bin/src/compiler/4-ast_to_ir.zig` - Main file (currently empty)
- `maxon-bin/src/compiler/ir.zig` - IR types (reference)
- `maxon-bin/src/compiler/3-semantic_analysis.zig` - MutationAnalyzer (import)
- `maxon-bin/src/compiler/ast.zig` - AST types (input)
- `maxon-bin/specs/ownership.md` - Ownership spec (reference)

## Nested Resource Test Matrix

All combinations of nested resources must be tested. The nesting hierarchy:
- **Primitives**: int, float, bool, byte, character
- **Structs**: can contain primitives, other structs, arrays, strings
- **Arrays**: can contain primitives, structs, other arrays, strings
- **Strings**: special struct with managed memory buffer (like arrays but for UTF-8 text)

### Test Categories

#### 1. Struct containing primitives
```maxon
type Point
    var x int
    var y int
end 'Point'
```
- Declare, use, cleanup
- Move entire struct
- Move struct, verify primitives not double-cleaned

#### 2. Struct containing struct
```maxon
type Inner
    var value int
end 'Inner'

type Outer
    var inner Inner
end 'Outer'
```
- Cleanup outer → cleanup inner first
- Move outer → inner also moved
- Move inner field only → outer cleanup skips inner

#### 3. Struct containing array
```maxon
type Container
    var data IntArray
end 'Container'
```
- Cleanup container → cleanup array first (free buffer)
- Move container → array buffer also moved
- Move container.data only → container cleanup skips data

#### 4. Struct containing struct containing array
```maxon
type Level1
    var buffer IntArray
end 'Level1'

type Level2
    var level1 Level1
end 'Level2'
```
- 3-level cleanup: array buffer → Level1 → Level2
- Move Level2 → entire tree moves
- Move level2.level1 → Level2 cleanup skips level1
- Move level2.level1.buffer → both Level2 and Level1 skip buffer

#### 5. Array of primitives
```maxon
typealias IntArray = Array with int
var arr = [1, 2, 3]
```
- Declare, use, cleanup (frees buffer)
- Move array → new owner frees buffer
- Resize array → realloc tracking

#### 6. Array of structs
```maxon
type Item
    var id int
    var name String
end 'Item'

typealias ItemArray = Array with Item
var items = ItemArray{}
```
- Cleanup array → cleanup each struct element first
- Move array → all elements move with it
- Access element → creates child tracking for that element

#### 7. Array of arrays (nested arrays)
```maxon
typealias IntArray = Array with int
typealias Matrix = Array with IntArray

var matrix = Matrix{}
matrix.push([1, 2, 3])
matrix.push([4, 5, 6])
```
- Cleanup outer → cleanup each inner array first (free all buffers)
- Move outer → all inner arrays move
- Move single row → outer cleanup skips that row

#### 8. Array of structs containing arrays
```maxon
type Row
    var cells IntArray
end 'Row'

typealias Table = Array with Row
var table = Table{}
```
- 3-level cleanup: cell arrays → Row structs → Table array
- Move table → everything moves
- Move table[0].cells → table[0] and table skip that cells array

#### 9. Struct containing array of structs
```maxon
type Inner
    var value int
end 'Inner'

typealias InnerArray = Array with Inner

type Outer
    var items InnerArray
end 'Outer'
```
- Cleanup: Inner elements → InnerArray → Outer
- Move outer → array and all elements move
- Move outer.items → outer cleanup skips items

#### 10. String (special managed struct)

Strings are structs with a managed memory buffer (like arrays). Special behaviors:
- String literals create owned strings with heap-allocated buffers
- Concatenation (`+`) borrows both operands and creates a new string
- Methods like `.byteLength()` borrow (don't move)
- Passing to functions that mutate moves ownership

```maxon
var s = "hello"
var s2 = s + " world"  // borrows s, creates new string s2
print(s)               // borrows s
mutateString(s)        // moves s if mutateString mutates its param
```
- String literal → owned, needs cleanup (frees buffer)
- String concatenation → new string allocated, operands borrowed
- Move string → new owner frees buffer
- String in struct field
- Array of strings

#### 11. Struct containing string
```maxon
type Person
    var name String
    var age int
end 'Person'

var p = Person{name: "Alice", age: 30}
```
- Cleanup person → cleanup name string first
- Move person → name string also moved
- Move p.name only → person cleanup skips name

#### 12. Array of strings
```maxon
typealias StringArray = Array with String
var names = ["Alice", "Bob", "Charlie"]
```
- Cleanup array → cleanup each string first (free all string buffers)
- Move array → all strings move
- Move single string → array cleanup skips that string

#### 13. Struct containing string and array
```maxon
type Document
    var title String
    var pages IntArray
end 'Document'
```
- Cleanup: pages array → title string → Document
- Move document → both title and pages move
- Move just title → document cleanup skips title, cleans pages
- Move just pages → document cleanup skips pages, cleans title

#### 14. Deeply nested (4+ levels)
```maxon
type Level4
    var value int
end 'Level4'

typealias L4Array = Array with Level4

type Level3
    var items L4Array
end 'Level3'

typealias L3Array = Array with Level3

type Level2
    var nested L3Array
end 'Level2'

type Level1
    var level2 Level2
end 'Level1'
```
- Verify depth-first cleanup order
- Move at any level → all descendants move
- Partial moves at different levels

### Error Detection Tests

#### Use-after-move errors (E008)
```maxon
// Struct field moved, then parent used
var c = Container{data: [...]}
takeArray(c.data)  // moves c.data
print(c.data.len)  // E008: use after move: 'c.data'

// Array element moved, then accessed
var items = [Item{...}, Item{...}]
takeItem(items[0])  // moves items[0]
print(items[0].id)  // E008: use after move: 'items[0]'
```

#### Immutable move errors (E010)
```maxon
// Immutable struct with mutable field
let c = Container{data: [...]}
mutateArray(c.data)  // E010: cannot move from immutable variable: 'c'

// Immutable array element
let items = [Item{...}]
mutateItem(items[0])  // E010
```

#### Double-free detection (panic)
```maxon
// These should trigger compiler panics during AST-to-IR:
// - Cleanup already-cleaned resource
// - Free already-freed buffer
// - Child freed before parent cleanup
```

### Tracking Output Tests

Verify `--track-memory` output for nested resources:

```
// For: var outer = Outer{inner: Inner{value: 42}}
ALLOC main:outer (Outer) 16 bytes
ALLOC main:outer.inner (Inner) 8 bytes

// Cleanup in correct order:
CLEANUP main:outer.inner
FREE main:outer.inner 8 bytes
CLEANUP main:outer
FREE main:outer 16 bytes
```

```
// For: var table = Table{} with rows containing cells
ALLOC main:table (Table) 32 bytes
ALLOC main:table[0] (Row) 32 bytes
ALLOC main:table[0].cells (IntArray) 32 bytes
ALLOC main:table[1] (Row) 32 bytes
ALLOC main:table[1].cells (IntArray) 32 bytes

// Move single cell array:
MOVE main:table[0].cells -> process:arr

// Cleanup (skips moved cell array):
CLEANUP main:table[1].cells
FREE main:table[1].cells 32 bytes
CLEANUP main:table[1]
FREE main:table[1] 32 bytes
// main:table[0].cells skipped (was moved)
CLEANUP main:table[0]
FREE main:table[0] 32 bytes
CLEANUP main:table
FREE main:table 32 bytes
```

### ResourceManager Unit Tests

The ResourceManager should have unit tests for:

1. **Scope operations**: beginScope, endScope, nested scopes
2. **Variable lifecycle**: declare → use → cleanup
3. **Move semantics**: move → verify state → verify new owner
4. **Nested access**: accessField, accessElement
5. **Cleanup order**: verify depth-first traversal
6. **Error detection**: all ResourceError cases
7. **Tracking**: verify correct IDs and output format
8. **Control flow**: snapshot, restore, merge for if/else/match
9. **Refcounting**: incref, decref, zero-check

### BlockManager Unit Tests

The BlockManager should have unit tests for:

1. **Block lifecycle**: enterBlock, exitBlock, nested blocks
2. **Termination tracking**: markTerminates, currentBlockTerminates
3. **Move recording**: recordMove, wasMovedInCurrentBlock
4. **Reassign recording**: recordReassign, wasReassignedInCurrentBlock
5. **Branch merge - both terminate**: no conflicts
6. **Branch merge - then terminates**: else state applies
7. **Branch merge - else terminates**: then state applies
8. **Branch merge - neither terminates, same moves**: definitely moved
9. **Branch merge - neither terminates, different moves**: conflict error
10. **Loop validation - move without reassign**: error
11. **Loop validation - move with reassign**: OK
12. **Nested blocks**: if inside while, while inside if
13. **Match arms**: multiple mutually exclusive branches
14. **Match merge - all arms move**: definitely moved
15. **Match merge - some arms move, some don't**: conflict error
16. **Match merge - all arms terminate**: no merge needed

### Edge Case Tests

1. **Deferred struct literal**: variable used twice in same struct, move after
2. **Deferred struct literal nested**: struct literal inside struct literal
3. **Partial struct move**: move one field, use another
4. **Partial struct cleanup**: cleanup skips moved field
5. **Self-assignment**: `x = x` is no-op
6. **Multiple returns**: cleanup emitted before each return
7. **Compound expression order**: left-to-right move detection
8. **Try/otherwise with move**: move happens before success/failure
9. **Return value ownership**: caller owns returned value
10. **Return parameter**: parameter moved into return value

### TypeMapper Unit Tests

1. **Primitive types**: int→i64, float→f64, bool→i1, byte→i8, character→i32
2. **Struct types**: create with correct field types and offsets
3. **Nested structs**: struct containing struct
4. **Array types**: array of primitives, array of structs
5. **Type caching**: same type returns cached result

### MethodResolver Unit Tests

1. **Type method lookup**: find method defined in type declaration
2. **Extension method lookup**: find method defined in extend block
3. **Primitive intrinsics**: int.hash, int.equals, byte.hash, etc.
4. **Generic method resolution**: resolve `key.hash()` when key is `KeyType` bound to `string`
5. **Method not found**: returns null

### ErrorCollector Unit Tests

1. **Error collection**: multiple errors can be reported
2. **Warning collection**: warnings don't stop compilation
3. **Fatal error**: stops compilation
4. **canContinue**: returns false after fatal
5. **Error formatting**: messages formatted correctly with location

## Verification

Run spec tests to verify the implementation:
```bash
cd maxon-bin && zig build spec-test
```

Key test categories:
- Ownership tests from `specs/ownership.md`
- Memory tracking output verification
- Double-free detection
- Use-after-move detection (E008)
- Immutable move detection (E010)
