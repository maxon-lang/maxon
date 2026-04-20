using System.Linq;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  // ManagedList data layout: [head:i64 @ 0, tail:i64 @ 8, count:i64 @ 16, cursor:i64 @ 24]
  private const int ManagedListHeadOffset = 0;
  private const int ManagedListTailOffset = 8;
  private const int ManagedListCountOffset = 16;
  private const int ManagedListCursorOffset = 24;
  private const int ManagedListDataSize = 32;

  // ManagedListNode data layout: [next:i64 @ 0, prev:i64 @ 8, managedList:i64 @ 16, value:i64 @ 24]
  private const int NodeNextOffset = 0;
  private const int NodePrevOffset = 8;
  private const int NodeManagedListOffset = 16;
  private const int NodeValueOffset = 24;
  private const int ManagedListNodeDataSize = 32;

  /// Whether a managed list element's valueKind represents a heap-allocated type (struct/string/etc.)
  /// rather than a primitive (int/float/bool/byte).
  /// After monomorphization, valueKind may be a typealias name (e.g., "Integer") for a
  /// ranged primitive — resolve through typeDefs to check the underlying type.
  private static bool IsManagedListHeapValueKind(string valueKind, Dictionary<string, IrType>? typeDefs = null) {
    // Check both source-level names and IR-level names (post-monomorphization)
    if (valueKind is "int" or "float" or "bool" or "byte" or "void"
        or "i64" or "f64" or "i1" or "i8" or "u32") return false;
    // Check if this is a ranged primitive typealias (e.g., "Integer" = int(i64.min to i64.max))
    if (typeDefs != null && typeDefs.TryGetValue(valueKind, out var resolvedType)
        && resolvedType is IrRangedPrimitiveType) return false;
    return true;
  }

  /// <summary>
  /// Lowers MaxonManagedListCreateOp: allocates a 32-byte managed list data block, zeroes all fields.
  /// </summary>
  private static void LowerManagedListCreate(
    MaxonManagedListCreateOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    string? inlineTarget = null) {

    // Use the concrete alias name (e.g., "TokenManagedList") so the destructor knows whether elements are managed
    var managedListTypeName = op.Result.TypeName is "__ManagedList" ? "__ManagedList" : op.Result.TypeName;
    var managedListPtr = EmitAlloc(block, ManagedListDataSize, managedListTypeName, scopeName: _currentFuncName);

    // Zero-initialize head, tail, count, cursor
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, managedListPtr, ManagedListHeadOffset, IrType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, managedListPtr, ManagedListTailOffset, IrType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, managedListPtr, ManagedListCountOffset, IrType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, managedListPtr, ManagedListCursorOffset, IrType.I64));

    var tempName = inlineTarget
      ?? temps.CreateTemp("managedList", op.Result.Id, op.Result.TypeName, OwnershipFlags.None);
    EmitStore(block, managedListPtr, tempName, varTypes);
    valueMap[op.Result] = new StdHeapPtr(managedListPtr.Id, op.Result.TypeName, tempName);
  }

  /// <summary>
  /// Lowers MaxonManagedListInsertValueOp: allocates a refcounted node,
  /// stores the value, and calls the appropriate runtime insert function.
  /// </summary>
  private static void LowerManagedListInsertValue(
    MaxonManagedListInsertValueOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;

    // Allocate node as independent refcounted allocation
    var nodePtr = EmitAlloc(block, ManagedListNodeDataSize, "__ManagedListNode", scopeName: _currentFuncName);

    // Zero-initialize link fields (runtime insert will set them)
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeNextOffset, IrType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodePrevOffset, IrType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeManagedListOffset, IrType.I64));

    // Store value at offset 24
    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs) && valueMap.TryGetValue(op.Value, out var valSv) && valSv is StdHeapPtr valHp) {
      // Struct/heap value: store pointer and incref — node holds a reference
      var valueHeapPtr = (StdI64)EmitLoad(block, valHp.VarName!, varTypes);
      block.AddOp(new StdStoreIndirectOp(valueHeapPtr, nodePtr, NodeValueOffset, IrType.I64));
      EmitIncrefValue(block, valueHeapPtr, scopeName: _currentFuncName);
    } else {
      // Primitive value: store directly
      var valueStd = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(valueStd, nodePtr, NodeValueOffset, IrType.I64));
    }

    // Save nodePtr before runtime call (which may clobber registers)
    var nodeTempName = temps.CreateTemp("managed_list_node", op.Result.Id, op.Result.TypeName, OwnershipFlags.None);
    EmitStore(block, nodePtr, nodeTempName, varTypes);

    // Reload managedListPtr for runtime call
    var managedListPtrReload = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var nodePtrReload = (StdI64)EmitLoad(block, nodeTempName, varTypes);

    var rtName = op.AtHead ? "maxon_managed_list_insert_first" : "maxon_managed_list_insert_last";
    block.AddOp(new StdCallRuntimeOp(rtName, [managedListPtrReload, nodePtrReload], null));

    valueMap[op.Result] = new StdHeapPtr(nodePtr.Id, op.Result.TypeName, nodeTempName);
  }

  /// <summary>
  /// Lowers MaxonManagedListInsertRelativeValueOp: allocates a node, stores value,
  /// and calls insert_after or insert_before relative to a target node.
  /// </summary>
  private static void LowerManagedListInsertRelativeValue(
    MaxonManagedListInsertRelativeValueOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;

    // Validate target node belongs to this list before allocating anything.
    {
      var listPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);
      var targetVarNameCheck = ((StdHeapPtr)valueMap[op.Target]).VarName!;
      var targetPtrCheck = (StdI64)EmitLoad(block, targetVarNameCheck, varTypes);
      EmitNodeInListOrPanic(block, targetPtrCheck, listPtr);
    }

    // Allocate node as independent refcounted allocation
    var nodePtr = EmitAlloc(block, ManagedListNodeDataSize, "__ManagedListNode", scopeName: _currentFuncName);

    // Zero-initialize link fields
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeNextOffset, IrType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodePrevOffset, IrType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeManagedListOffset, IrType.I64));

    // Store value at offset 24
    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs) && valueMap.TryGetValue(op.Value, out var valSv) && valSv is StdHeapPtr valHp) {
      // Struct/heap value: store pointer and incref — node holds a reference
      var valueHeapPtr = (StdI64)EmitLoad(block, valHp.VarName!, varTypes);
      block.AddOp(new StdStoreIndirectOp(valueHeapPtr, nodePtr, NodeValueOffset, IrType.I64));
      EmitIncrefValue(block, valueHeapPtr, scopeName: _currentFuncName);
    } else {
      var valueStd = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(valueStd, nodePtr, NodeValueOffset, IrType.I64));
    }

    // Save nodePtr before runtime calls
    var nodeTempName = temps.CreateTemp("managed_list_node", op.Result.Id, op.Result.TypeName, OwnershipFlags.None);
    EmitStore(block, nodePtr, nodeTempName, varTypes);

    // Reload pointers for runtime call
    var managedListPtrReload = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var targetVarName = ((StdHeapPtr)valueMap[op.Target]).VarName!;
    var targetPtr = (StdI64)EmitLoad(block, targetVarName, varTypes);
    var nodePtrReload = (StdI64)EmitLoad(block, nodeTempName, varTypes);

    var rtName = op.After ? "maxon_managed_list_insert_after" : "maxon_managed_list_insert_before";
    block.AddOp(new StdCallRuntimeOp(rtName, [managedListPtrReload, targetPtr, nodePtrReload], null));

    valueMap[op.Result] = new StdHeapPtr(nodePtr.Id, op.Result.TypeName, nodeTempName);
  }

  /// <summary>
  /// Lowers MaxonManagedListReinsertOp: moves an existing node into the new managed list
  /// and calls the appropriate runtime insert function.
  /// </summary>
  private static void LowerManagedListReinsert(
    MaxonManagedListReinsertOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var nodeVarName = ((StdHeapPtr)valueMap[op.Node]).VarName!;

    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);

    var rtName = op.AtHead ? "maxon_managed_list_insert_first" : "maxon_managed_list_insert_last";
    block.AddOp(new StdCallRuntimeOp(rtName, [managedListPtr, nodePtr], null));
  }

  /// <summary>
  /// Lowers MaxonManagedListReinsertRelativeOp: inserts node relative to target.
  /// </summary>
  private static void LowerManagedListReinsertRelative(
    MaxonManagedListReinsertRelativeOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var nodeVarName = ((StdHeapPtr)valueMap[op.Node]).VarName!;
    var targetVarName = ((StdHeapPtr)valueMap[op.Target]).VarName!;

    // Validate: target must belong to this list. Node may be detached or attached;
    // the runtime call unlinks first and then re-links at the target position.
    {
      var listPtrCheck = (StdI64)EmitLoad(block, managedListVarName, varTypes);
      var targetPtrCheck = (StdI64)EmitLoad(block, targetVarName, varTypes);
      EmitNodeInListOrPanic(block, targetPtrCheck, listPtrCheck);
    }

    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var targetPtr = (StdI64)EmitLoad(block, targetVarName, varTypes);

    var rtName = op.After ? "maxon_managed_list_insert_after" : "maxon_managed_list_insert_before";
    block.AddOp(new StdCallRuntimeOp(rtName, [managedListPtr, targetPtr, nodePtr], null));
  }

  /// <summary>
  /// Lowers MaxonManagedListDetachOp: unlinks node from managed list and moves it to the current scope.
  /// </summary>
  private static void LowerManagedListDetach(
    MaxonManagedListDetachOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var nodeVarName = ((StdHeapPtr)valueMap[op.Node]).VarName!;

    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    // Validate: node must belong to this list.
    EmitNodeInListOrPanic(block, nodePtr, managedListPtr);

    // Unlink node from managed list's linked list and release the managed list's counted reference.
    // The node survives because the local variable still holds a reference.
    block.AddOp(new StdCallRuntimeOp("maxon_managed_list_unlink", [managedListPtr, nodePtr], null));
    var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    EmitDecrefValueIfNonnull(block, nodePtrReload, scopeName: _currentFuncName);
  }

  /// <summary>
  /// Lowers MaxonManagedListRemoveOp: unlinks node, extracts value, decrefs node, returns value.
  /// </summary>
  private static void LowerManagedListRemove(
    MaxonManagedListRemoveOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var nodeVarName = ((StdHeapPtr)valueMap[op.Node]).VarName!;

    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    // Validate: node must belong to this list.
    EmitNodeInListOrPanic(block, nodePtr, managedListPtr);

    // Unlink node from managed list and release the managed list's counted reference
    block.AddOp(new StdCallRuntimeOp("maxon_managed_list_unlink", [managedListPtr, nodePtr], null));
    var nodeDecrefPtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    EmitDecrefValueIfNonnull(block, nodeDecrefPtr, scopeName: _currentFuncName);

    // Load value from node before freeing it
    var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var valueLoad = new StdLoadIndirectOp(nodePtrReload, NodeValueOffset, IrType.I64);
    block.AddOp(valueLoad);
    var extractedValue = (StdI64)valueLoad.Result;

    // Release the node's reference to the value (the ManagedListNode destructor is empty,
    // so we must explicitly decref managed values before freeing the node)
    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs)) {
      EmitDecrefValueIfNonnull(block, extractedValue, scopeName: _currentFuncName);
    }

    // Save the extracted value
    var valueTempName = temps.CreateTemp("managed_list_nav", op.Result.Id, op.ValueKind, OwnershipFlags.None);
    EmitStore(block, extractedValue, valueTempName, varTypes);

    // Decref the node — local variable releases its reference
    var nodePtrForFree = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    EmitDecrefValueIfNonnull(block, nodePtrForFree, scopeName: _currentFuncName);

    // Zero the node variable so scope-end cleanup skips it (node memory is already freed)
    var zeroNode = new StdConstI64Op(0);
    block.AddOp(zeroNode);
    EmitStore(block, zeroNode.Result, nodeVarName, varTypes);

    // Register the extracted value
    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs)) {
      valueMap[op.Result] = new StdHeapPtr(extractedValue.Id, op.ValueKind, valueTempName);
    } else {
      // Primitive value: only register in valueMap as a plain value.
      // Registering as StdHeapPtr would cause downstream assign ops
      // to treat the primitive as a heap pointer and emit incref.
      var valReload = EmitLoad(block, valueTempName, varTypes);
      valueMap[op.Result] = valReload;
    }
  }

  /// <summary>
  /// Lowers MaxonManagedListCountOp: loads the count field from the managed list data.
  /// </summary>
  private static void LowerManagedListCount(
    MaxonManagedListCountOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var countLoad = new StdLoadIndirectOp(managedListPtr, ManagedListCountOffset, IrType.I64);
    block.AddOp(countLoad);
    valueMap[op.Result] = countLoad.Result;
  }

  /// <summary>
  /// Lowers MaxonManagedListNodeValueOp: loads the value stored in a managed list node.
  /// For struct types, returns a borrowed reference (node owns the value).
  /// </summary>
  private static void LowerManagedListNodeValue(
    MaxonManagedListNodeValueOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps) {

    var nodeVarName = ((StdHeapPtr)valueMap[op.Node]).VarName!;
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var valueLoad = new StdLoadIndirectOp(nodePtr, NodeValueOffset, IrType.I64);
    block.AddOp(valueLoad);

    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs)) {
      // Heap value: return a borrowed pointer. The assignment incref (from
      // MaxonAssignOp) provides the caller's reference.
      var tempName = temps.CreateTemp("managed_list_val", op.Result.Id, op.ValueKind, OwnershipFlags.Borrowed);
      EmitStore(block, (StdI64)valueLoad.Result, tempName, varTypes);
      valueMap[op.Result] = new StdHeapPtr(valueLoad.Result.Id, op.ValueKind, tempName);
    } else {
      // Primitive value
      valueMap[op.Result] = valueLoad.Result;
    }
  }

  /// <summary>
  /// Lowers MaxonManagedListNodeSetValueOp: replaces the value in a managed list node.
  /// For struct types, detaches the old value to current scope and decrefs it,
  /// then stores the new value in the node.
  /// </summary>
  private static void LowerManagedListNodeSetValue(
    MaxonManagedListNodeSetValueOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs) {

    var nodeVarName = ((StdHeapPtr)valueMap[op.Node]).VarName!;
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs)) {
      // Load old value pointer from node
      var oldValueLoad = new StdLoadIndirectOp(nodePtr, NodeValueOffset, IrType.I64);
      block.AddOp(oldValueLoad);
      var oldValue = (StdI64)oldValueLoad.Result;

      // Decref old value — node no longer holds a reference (may be null)
      EmitDecrefValueIfNonnull(block, oldValue, scopeName: _currentFuncName);

      // Store new value and incref — node now holds a reference
      var valueSrcName = ((StdHeapPtr)valueMap[op.Value]).VarName!;
      var newValuePtr = (StdI64)EmitLoad(block, valueSrcName, varTypes);
      var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);
      block.AddOp(new StdStoreIndirectOp(newValuePtr, nodePtrReload, NodeValueOffset, IrType.I64));
      EmitIncrefValue(block, newValuePtr, scopeName: _currentFuncName);
    } else {
      // Primitive: just store new value
      var newValue = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(newValue, nodePtr, NodeValueOffset, IrType.I64));
    }
  }

  /// <summary>
  /// Lowers MaxonManagedListClearOp: calls runtime to unlink and free all nodes.
  /// Uses maxon_managed_list_clear_managed for heap-value managed lists (decrefs node values),
  /// and maxon_managed_list_clear for primitive managed lists (no decref needed).
  /// </summary>
  private static void LowerManagedListClear(
    MaxonManagedListClearOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var clearFunc = IsManagedListHeapValueKind(op.ValueKind, typeDefs) ? "maxon_managed_list_clear_managed" : "maxon_managed_list_clear";
    block.AddOp(new StdCallRuntimeOp(clearFunc, [managedListPtr], null));

    // Reset cursor to null after clearing
    var managedListPtrReload = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, managedListPtrReload, ManagedListCursorOffset, IrType.I64));
  }

  /// <summary>
  /// Lowers MaxonManagedListCursorResetOp: sets the cursor field to 0 (null).
  /// </summary>
  private static void LowerManagedListCursorReset(
    MaxonManagedListCursorResetOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, managedListPtr, ManagedListCursorOffset, IrType.I64));
  }

  /// <summary>
  /// Lowers MaxonManagedListCursorValueOp: loads the value from the node currently pointed
  /// to by the managed list's cursor field. The cursor must be non-null.
  /// </summary>
  private static void LowerManagedListCursorValue(
    MaxonManagedListCursorValueOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);

    // Load cursor (node pointer) from managed list
    var cursorLoad = new StdLoadIndirectOp(managedListPtr, ManagedListCursorOffset, IrType.I64);
    block.AddOp(cursorLoad);
    var cursorPtr = (StdI64)cursorLoad.Result;

    // Panic if cursor is null (cursorStart() not called, or cursorAdvance past end).
    // Stdlib iterators set the cursor before calling cursorValue; this catches misuse.
    {
      var zero = new StdConstI64Op(0);
      block.AddOp(zero);
      var isNull = new StdCmpI64Op("eq", cursorPtr, zero.Result);
      block.AddOp(isNull);
      var oneConst = new StdConstI64Op(1);
      block.AddOp(oneConst);
      var asI64 = new StdSelectI64Op(isNull.Result, oneConst.Result, zero.Result);
      block.AddOp(asI64);
      EmitBoundsCheck(block, asI64.Result, oneConst.Result, "__mm_panic_list_empty");
    }

    // Load value from the cursor node
    var valueLoad = new StdLoadIndirectOp(cursorPtr, NodeValueOffset, IrType.I64);
    block.AddOp(valueLoad);

    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs)) {
      var tempName = temps.CreateTemp("managed_list_cursor_val", op.Result.Id, op.ValueKind, OwnershipFlags.Borrowed);
      EmitStore(block, (StdI64)valueLoad.Result, tempName, varTypes);
      valueMap[op.Result] = new StdHeapPtr(valueLoad.Result.Id, op.ValueKind, tempName);
    } else {
      valueMap[op.Result] = valueLoad.Result;
    }
  }

  /// <summary>
  /// Intercepts synthetic managed list navigation calls (__managed_list_head, __managed_list_tail,
  /// __managed_list_node_next, __managed_list_node_prev, __managed_list_cursor_start, __managed_list_cursor_advance)
  /// during lowering.
  /// These load a pointer from the managed list/node and set an error flag if null.
  /// Returns true if the callee was handled.
  /// </summary>
  private static bool TryLowerManagedListNavigation(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    bool isTryCall,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {

    // Handle cursor operations that read from/write to the managed list's cursor field
    if (callee == "__managed_list_cursor_start") {
      return LowerManagedListCursorStart(args, isTryCall, block, valueMap, varTypes,
        errorFlagValue);
    }
    if (callee == "__managed_list_cursor_advance") {
      return LowerManagedListCursorAdvance(args, isTryCall, block, valueMap, varTypes,
        errorFlagValue);
    }

    int fieldOffset;
    bool isNodeNav; // true for node_next/node_prev, false for managed list head/tail

    switch (callee) {
      case "__managed_list_head":
        fieldOffset = ManagedListHeadOffset;
        isNodeNav = false;
        break;
      case "__managed_list_tail":
        fieldOffset = ManagedListTailOffset;
        isNodeNav = false;
        break;
      case "__managed_list_node_next":
        fieldOffset = NodeNextOffset;
        isNodeNav = true;
        break;
      case "__managed_list_node_prev":
        fieldOffset = NodePrevOffset;
        isNodeNav = true;
        break;
      default:
        return false;
    }

    // Load the pointer from the appropriate struct
    var srcVarName = ((StdHeapPtr)valueMap[args[0]]).VarName!;
    var srcPtr = (StdI64)EmitLoad(block, srcVarName, varTypes);
    var ptrLoad = new StdLoadIndirectOp(srcPtr, fieldOffset, IrType.I64);
    block.AddOp(ptrLoad);
    var loadedPtr = (StdI64)ptrLoad.Result;

    // ManagedListError.empty (ordinal 0) -> flag 1, ManagedListError.endOfManagedList (ordinal 1) -> flag 2.
    var errorOrdinal = isNodeNav ? 2 : 1;
    EmitNullCheckErrorFlag(block, loadedPtr, errorOrdinal, isTryCall, valueMap, varTypes, errorFlagValue);

    // Register result as a ManagedListNode (even if null — the error path handles that)
    if (result != null) {
      var managedListNodeType = result is MaxonStruct ms ? ms.TypeName : "__ManagedListNode";
      var tempName = temps.CreateTemp("managed_list_nav", result.Id, managedListNodeType, OwnershipFlags.Orphan | OwnershipFlags.OwnsRef);
      EmitStore(block, loadedPtr, tempName, varTypes);
      // Incref the ManagedListNode so it survives until scope exit decref.
      // Null-guarded: on the error path the node pointer is null.
      var nodeForIncref = (StdI64)EmitLoad(block, tempName, varTypes);
      EmitIncrefValueIfNonnull(block, nodeForIncref, scopeName: _currentFuncName);
      valueMap[result] = new StdHeapPtr(IrContext.Current.NextId(), managedListNodeType, tempName);
    }

    return true;
  }

  /// <summary>
  /// Lowers __managed_list_cursor_start: sets cursor to managed list head and sets error flag.
  /// No refcounting — cursor is a borrowed pointer; the managed list owns the nodes.
  /// </summary>
  private static bool LowerManagedListCursorStart(
    List<MaxonValue> args,
    bool isTryCall,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {

    var managedListVarName = ((StdHeapPtr)valueMap[args[0]]).VarName!;
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);

    // Load head pointer
    var headLoad = new StdLoadIndirectOp(managedListPtr, ManagedListHeadOffset, IrType.I64);
    block.AddOp(headLoad);
    var headPtr = (StdI64)headLoad.Result;

    // Store head as cursor
    var managedListPtrReload = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    block.AddOp(new StdStoreIndirectOp(headPtr, managedListPtrReload, ManagedListCursorOffset, IrType.I64));

    EmitNullCheckErrorFlag(block, headPtr, 1, isTryCall, valueMap, varTypes, errorFlagValue);

    return true;
  }

  /// <summary>
  /// Lowers __managed_list_cursor_advance: loads cursor->next, stores it as the new cursor.
  /// Sets error flag if the new cursor is null (at end of managed list).
  /// No refcounting — cursor is a borrowed pointer; the managed list owns the nodes.
  /// </summary>
  private static bool LowerManagedListCursorAdvance(
    List<MaxonValue> args,
    bool isTryCall,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {

    var managedListVarName = ((StdHeapPtr)valueMap[args[0]]).VarName!;
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);

    // Load current cursor
    var cursorLoad = new StdLoadIndirectOp(managedListPtr, ManagedListCursorOffset, IrType.I64);
    block.AddOp(cursorLoad);
    var cursorPtr = (StdI64)cursorLoad.Result;

    // Load cursor->next
    var nextLoad = new StdLoadIndirectOp(cursorPtr, NodeNextOffset, IrType.I64);
    block.AddOp(nextLoad);
    var nextPtr = (StdI64)nextLoad.Result;

    // Store next as new cursor
    var managedListPtrReload = (StdI64)EmitLoad(block, managedListVarName, varTypes);
    block.AddOp(new StdStoreIndirectOp(nextPtr, managedListPtrReload, ManagedListCursorOffset, IrType.I64));

    EmitNullCheckErrorFlag(block, nextPtr, 2, isTryCall, valueMap, varTypes, errorFlagValue);

    return true;
  }

  /// <summary>
  /// Emits null-check + error flag for managed list operations.
  /// Sets __error_flag to errorOrdinal if ptr is null, 0 otherwise.
  /// </summary>
  private static void EmitNullCheckErrorFlag(
    IrBlock<StandardOp> block,
    StdI64 ptr,
    int errorOrdinal,
    bool isTryCall,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {

    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var isNull = new StdCmpI64Op("eq", ptr, zeroConst.Result);
    block.AddOp(isNull);
    var errorConst = new StdConstI64Op(errorOrdinal);
    block.AddOp(errorConst);
    var successConst = new StdConstI64Op(0);
    block.AddOp(successConst);
    var selectFlag = new StdSelectI64Op(isNull.Result, errorConst.Result, successConst.Result);
    block.AddOp(selectFlag);
    EmitStore(block, selectFlag.Result, "__error_flag", varTypes);

    if (isTryCall && errorFlagValue != null) {
      valueMap[errorFlagValue] = selectFlag.Result;
    }
  }

  /// <summary>
  /// Panic if the node's back-pointer does not match the given managed list.
  /// Violation indicates the caller passed a node that belongs to a different
  /// list (or no list). Stdlib wrappers are expected to have verified membership
  /// before reaching this point; the panic catches misuse of the raw builtin.
  /// </summary>
  private static void EmitNodeInListOrPanic(
    IrBlock<StandardOp> block,
    StdI64 nodePtr,
    StdI64 managedListPtr) {
    // Load node.managedList and check equality with managedListPtr.
    var nodeListLoad = new StdLoadIndirectOp(nodePtr, NodeManagedListOffset, IrType.I64);
    block.AddOp(nodeListLoad);
    // EmitBoundsCheck panics when index >= limit (unsigned). Using it as a generic
    // "X == Y required" check is awkward — instead, compute inequality and branch to panic.
    // Simpler: reuse maxon_bounds_check with an inverted comparison by passing:
    //   index = (nodeList != managedListPtr) ? 1 : 0
    //   limit = 1
    // so index >= limit iff nodeList != managedListPtr → panic.
    var isMismatch = new StdCmpI64Op("ne", (StdI64)nodeListLoad.Result, managedListPtr);
    block.AddOp(isMismatch);
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var asI64 = new StdSelectI64Op(isMismatch.Result, oneConst.Result, zeroConst.Result);
    block.AddOp(asI64);
    EmitBoundsCheck(block, asI64.Result, oneConst.Result, "__mm_panic_list_node_not_in_list");
  }

  /// <summary>
  /// Lowers MaxonManagedListHeadPtrOp: loads the head pointer as a raw i64.
  /// No refcounting — this is a borrowed raw pointer for iterator use.
  /// </summary>
  private static void LowerManagedListHeadPtr(
    MaxonManagedListHeadPtrOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps) {

    var managedListVarName = ((StdHeapPtr)valueMap[op.ManagedList]).VarName!;
    var managedListPtr = (StdI64)EmitLoad(block, managedListVarName, varTypes);

    var headLoad = new StdLoadIndirectOp(managedListPtr, ManagedListHeadOffset, IrType.I64);
    block.AddOp(headLoad);

    var tempName = temps.CreateTemp("managed_list_head_ptr", op.Result.Id, "int", OwnershipFlags.None);
    EmitStore(block, headLoad.Result, tempName, varTypes);
    valueMap[op.Result] = headLoad.Result;
  }

  /// <summary>
  /// Lowers MaxonManagedListNodePtrNextOp: loads cursor->next as a raw i64.
  /// No refcounting — borrowed raw pointer for iterator use.
  /// </summary>
  private static void LowerManagedListNodePtrNext(
    MaxonManagedListNodePtrNextOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps) {

    var cursorVal = valueMap[op.CursorPtr];
    // If the cursor is stored in a var, load it
    StdI64 cursorPtr;
    if (cursorVal is StdHeapPtr hp && hp.VarName != null)
      cursorPtr = (StdI64)EmitLoad(block, hp.VarName, varTypes);
    else
      cursorPtr = (StdI64)cursorVal;

    var nextLoad = new StdLoadIndirectOp(cursorPtr, NodeNextOffset, IrType.I64);
    block.AddOp(nextLoad);

    var tempName = temps.CreateTemp("managed_list_node_ptr_next", op.Result.Id, "int", OwnershipFlags.None);
    EmitStore(block, nextLoad.Result, tempName, varTypes);
    valueMap[op.Result] = nextLoad.Result;
  }

  /// <summary>
  /// Lowers MaxonManagedListNodePtrValueOp: loads the value from a node given its raw pointer.
  /// No refcounting on the node — the managed list owns nodes.
  /// </summary>
  private static void LowerManagedListNodePtrValue(
    MaxonManagedListNodePtrValueOp op,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps) {

    var cursorVal = valueMap[op.CursorPtr];
    StdI64 cursorPtr;
    if (cursorVal is StdHeapPtr hp && hp.VarName != null)
      cursorPtr = (StdI64)EmitLoad(block, hp.VarName, varTypes);
    else
      cursorPtr = (StdI64)cursorVal;

    var valueLoad = new StdLoadIndirectOp(cursorPtr, NodeValueOffset, IrType.I64);
    block.AddOp(valueLoad);

    if (IsManagedListHeapValueKind(op.ValueKind, typeDefs)) {
      var tempName = temps.CreateTemp("managed_list_node_ptr_val", op.Result.Id, op.ValueKind, OwnershipFlags.Borrowed);
      EmitStore(block, (StdI64)valueLoad.Result, tempName, varTypes);
      valueMap[op.Result] = new StdHeapPtr(valueLoad.Result.Id, op.ValueKind, tempName);
    } else {
      valueMap[op.Result] = valueLoad.Result;
    }
  }
}
