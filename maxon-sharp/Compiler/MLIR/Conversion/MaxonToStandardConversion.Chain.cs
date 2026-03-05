using System.Linq;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
  // Chain data layout: [head:i64 @ 0, tail:i64 @ 8, count:i64 @ 16, cursor:i64 @ 24]
  private const int ChainHeadOffset = 0;
  private const int ChainTailOffset = 8;
  private const int ChainCountOffset = 16;
  private const int ChainCursorOffset = 24;
  private const int ChainDataSize = 32;

  // ChainNode data layout: [next:i64 @ 0, prev:i64 @ 8, chain:i64 @ 16, value:i64 @ 24]
  private const int NodeNextOffset = 0;
  private const int NodePrevOffset = 8;
  private const int NodeChainOffset = 16;
  private const int NodeValueOffset = 24;
  private const int ChainNodeDataSize = 32;

  /// Whether a chain element's valueKind represents a heap-allocated type (struct/string/etc.)
  /// rather than a primitive (int/float/bool/byte).
  /// After monomorphization, valueKind may be a typealias name (e.g., "Integer") for a
  /// ranged primitive — resolve through typeDefs to check the underlying type.
  private static bool IsChainHeapValueKind(string valueKind, Dictionary<string, MlirType>? typeDefs = null) {
    // Check both source-level names and MLIR-level names (post-monomorphization)
    if (valueKind is "int" or "float" or "bool" or "byte" or "void"
        or "i64" or "f64" or "i1" or "i8" or "u32") return false;
    // Check if this is a ranged primitive typealias (e.g., "Integer" = int(i64.min to i64.max))
    if (typeDefs != null && typeDefs.TryGetValue(valueKind, out var resolvedType)
        && resolvedType is MlirRangedPrimitiveType) return false;
    return true;
  }

  /// <summary>
  /// Lowers MaxonChainCreateOp: allocates a 24-byte chain data block, zeroes all fields.
  /// </summary>
  private static void LowerChainCreate(
    MaxonChainCreateOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    VarRegistry temps) {

    var chainPtr = EmitAlloc(block, ChainDataSize, "__Chain", scopeName: _currentFuncName);

    // Zero-initialize head, tail, count, cursor
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainHeadOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainTailOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainCountOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainCursorOffset, MlirType.I64));

    var tempName = temps.CreateTemp("chain", op.Result.Id, op.Result.TypeName, OwnershipFlags.None);
    EmitStore(block, chainPtr, tempName, varTypes);
    structVarNames[op.Result.Id] = tempName;
    structValueTypes[op.Result.Id] = op.Result.TypeName;
  }

  /// <summary>
  /// Lowers MaxonChainInsertValueOp: allocates a node as child of the chain,
  /// stores the value, and calls the appropriate runtime insert function.
  /// </summary>
  private static void LowerChainInsertValue(
    MaxonChainInsertValueOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps) {

    var chainVarName = structVarNames[op.Chain.Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);

    // Allocate node as child of chain
    var nodePtr = EmitAllocIn(block, ChainNodeDataSize, chainPtr, "__ChainNode");

    // Zero-initialize link fields (runtime insert will set them)
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeNextOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodePrevOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeChainOffset, MlirType.I64));

    // Store value at offset 24
    if (IsChainHeapValueKind(op.ValueKind, typeDefs) && structVarNames.TryGetValue(op.Value.Id, out var valueSrcName)) {
      // Struct/heap value: store pointer and incref — node holds a reference
      var valueHeapPtr = (StdI64)EmitLoad(block, valueSrcName, varTypes);
      block.AddOp(new StdStoreIndirectOp(valueHeapPtr, nodePtr, NodeValueOffset, MlirType.I64));
      EmitIncrefValue(block, valueHeapPtr, scopeName: _currentFuncName);
    } else {
      // Primitive value: store directly
      var valueStd = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(valueStd, nodePtr, NodeValueOffset, MlirType.I64));
    }

    // Save nodePtr before runtime call (which may clobber registers)
    var nodeTempName = temps.CreateTemp("chain_node", op.Result.Id, op.Result.TypeName, OwnershipFlags.None);
    EmitStore(block, nodePtr, nodeTempName, varTypes);

    // Reload chainPtr for runtime call
    var chainPtrReload = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var nodePtrReload = (StdI64)EmitLoad(block, nodeTempName, varTypes);

    var rtName = op.AtHead ? "maxon_chain_insert_first" : "maxon_chain_insert_last";
    block.AddOp(new StdCallRuntimeOp(rtName, [chainPtrReload, nodePtrReload], null));

    structVarNames[op.Result.Id] = nodeTempName;
    structValueTypes[op.Result.Id] = op.Result.TypeName;
  }

  /// <summary>
  /// Lowers MaxonChainInsertRelativeValueOp: allocates a node, stores value,
  /// and calls insert_after or insert_before relative to a target node.
  /// </summary>
  private static void LowerChainInsertRelativeValue(
    MaxonChainInsertRelativeValueOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps) {

    var chainVarName = structVarNames[op.Chain.Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);

    // Allocate node as child of chain
    var nodePtr = EmitAllocIn(block, ChainNodeDataSize, chainPtr, "__ChainNode");

    // Zero-initialize link fields
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeNextOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodePrevOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, nodePtr, NodeChainOffset, MlirType.I64));

    // Store value at offset 24
    if (IsChainHeapValueKind(op.ValueKind, typeDefs) && structVarNames.TryGetValue(op.Value.Id, out var valueSrcName)) {
      // Struct/heap value: store pointer and incref — node holds a reference
      var valueHeapPtr = (StdI64)EmitLoad(block, valueSrcName, varTypes);
      block.AddOp(new StdStoreIndirectOp(valueHeapPtr, nodePtr, NodeValueOffset, MlirType.I64));
      EmitIncrefValue(block, valueHeapPtr, scopeName: _currentFuncName);
    } else {
      var valueStd = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(valueStd, nodePtr, NodeValueOffset, MlirType.I64));
    }

    // Save nodePtr before runtime calls
    var nodeTempName = temps.CreateTemp("chain_node", op.Result.Id, op.Result.TypeName, OwnershipFlags.None);
    EmitStore(block, nodePtr, nodeTempName, varTypes);

    // Reload pointers for runtime call
    var chainPtrReload = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var targetVarName = structVarNames[op.Target.Id];
    var targetPtr = (StdI64)EmitLoad(block, targetVarName, varTypes);
    var nodePtrReload = (StdI64)EmitLoad(block, nodeTempName, varTypes);

    var rtName = op.After ? "maxon_chain_insert_after" : "maxon_chain_insert_before";
    block.AddOp(new StdCallRuntimeOp(rtName, [chainPtrReload, targetPtr, nodePtrReload], null));

    structVarNames[op.Result.Id] = nodeTempName;
    structValueTypes[op.Result.Id] = op.Result.TypeName;
  }

  /// <summary>
  /// Lowers MaxonChainReinsertOp: reparents an existing node under the new chain
  /// and calls the appropriate runtime insert function.
  /// </summary>
  private static void LowerChainReinsert(
    MaxonChainReinsertOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var chainVarName = structVarNames[op.Chain.Id];
    var nodeVarName = structVarNames[op.Node.Id];

    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);

    var rtName = op.AtHead ? "maxon_chain_insert_first" : "maxon_chain_insert_last";
    block.AddOp(new StdCallRuntimeOp(rtName, [chainPtr, nodePtr], null));
  }

  /// <summary>
  /// Lowers MaxonChainReinsertRelativeOp: inserts node relative to target.
  /// </summary>
  private static void LowerChainReinsertRelative(
    MaxonChainReinsertRelativeOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var chainVarName = structVarNames[op.Chain.Id];
    var nodeVarName = structVarNames[op.Node.Id];

    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var targetVarName = structVarNames[op.Target.Id];
    var targetPtr = (StdI64)EmitLoad(block, targetVarName, varTypes);

    var rtName = op.After ? "maxon_chain_insert_after" : "maxon_chain_insert_before";
    block.AddOp(new StdCallRuntimeOp(rtName, [chainPtr, targetPtr, nodePtr], null));
  }

  /// <summary>
  /// Lowers MaxonChainDetachOp: unlinks node from chain and moves it to the current scope.
  /// </summary>
  private static void LowerChainDetach(
    MaxonChainDetachOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var chainVarName = structVarNames[op.Chain.Id];
    var nodeVarName = structVarNames[op.Node.Id];

    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    // Unlink node from chain's linked list.
    // The node data stays as a child allocation of the chain — this is correct because
    // the ChainNode wrapper holds a borrowed pointer to the data. The node data remains
    // valid as long as the chain exists, and gets freed when the chain is freed
    // (via mm_free_entry's recursive child cleanup).
    block.AddOp(new StdCallRuntimeOp("maxon_chain_unlink", [chainPtr, nodePtr], null));
  }

  /// <summary>
  /// Lowers MaxonChainRemoveOp: unlinks node, extracts value, frees node, returns value.
  /// For struct values, the extracted value is reparented to the current scope.
  /// </summary>
  private static void LowerChainRemove(
    MaxonChainRemoveOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps) {

    var chainVarName = structVarNames[op.Chain.Id];
    var nodeVarName = structVarNames[op.Node.Id];

    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    // Unlink node from chain
    block.AddOp(new StdCallRuntimeOp("maxon_chain_unlink", [chainPtr, nodePtr], null));

    // Load value from node before freeing it
    var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var valueLoad = new StdLoadIndirectOp(nodePtrReload, NodeValueOffset, MlirType.I64);
    block.AddOp(valueLoad);
    var extractedValue = (StdI64)valueLoad.Result;

    // Save the extracted value. Use CallReturn flag so assignment skips incref
    // (transfer: the node held an incref'd reference, caller inherits it).
    var valueTempName = temps.CreateTemp("chain_nav", op.Result.Id, op.ValueKind, OwnershipFlags.CallReturn);
    EmitStore(block, extractedValue, valueTempName, varTypes);

    // Free the node. The value's refcount is unchanged — caller inherits the node's reference.
    var nodePtrForFree = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    List<StdValue> freeArgs = [nodePtrForFree];
    if (Compiler.MmTrace) freeArgs.Add(EmitNullPtr(block));
    block.AddOp(new StdCallRuntimeOp("mm_free", freeArgs, null));

    // Zero the node variable so scope-end cleanup skips it (node memory is already freed)
    var zeroNode = new StdConstI64Op(0);
    block.AddOp(zeroNode);
    EmitStore(block, zeroNode.Result, nodeVarName, varTypes);

    // Register the extracted value
    if (IsChainHeapValueKind(op.ValueKind, typeDefs)) {
      structVarNames[op.Result.Id] = valueTempName;
      structValueTypes[op.Result.Id] = op.ValueKind;
    } else {
      // Primitive value: only register in valueMap, NOT in structVarNames.
      // Registering in structVarNames would cause downstream assign ops
      // to treat the primitive as a heap pointer and emit incref.
      var valReload = EmitLoad(block, valueTempName, varTypes);
      valueMap[op.Result] = valReload;
    }
  }

  /// <summary>
  /// Lowers MaxonChainCountOp: loads the count field from the chain data.
  /// </summary>
  private static void LowerChainCount(
    MaxonChainCountOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var chainVarName = structVarNames[op.Chain.Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var countLoad = new StdLoadIndirectOp(chainPtr, ChainCountOffset, MlirType.I64);
    block.AddOp(countLoad);
    valueMap[op.Result] = countLoad.Result;
  }

  /// <summary>
  /// Lowers MaxonChainNodeValueOp: loads the value stored in a chain node.
  /// For struct types, returns a borrowed reference (node owns the value).
  /// </summary>
  private static void LowerChainNodeValue(
    MaxonChainNodeValueOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps) {

    var nodeVarName = structVarNames[op.Node.Id];
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var valueLoad = new StdLoadIndirectOp(nodePtr, NodeValueOffset, MlirType.I64);
    block.AddOp(valueLoad);

    if (IsChainHeapValueKind(op.ValueKind, typeDefs)) {
      // Heap value: return a borrowed pointer. The assignment incref (from
      // MaxonAssignOp) provides the caller's reference. mm_free_entry rescues
      // children with rc > 0, so the value survives container destruction.
      var tempName = temps.CreateTemp("chain_val", op.Result.Id, op.ValueKind, OwnershipFlags.Borrowed);
      EmitStore(block, (StdI64)valueLoad.Result, tempName, varTypes);
      structVarNames[op.Result.Id] = tempName;
      structValueTypes[op.Result.Id] = op.ValueKind;
    } else {
      // Primitive value
      valueMap[op.Result] = valueLoad.Result;
    }
  }

  /// <summary>
  /// Lowers MaxonChainNodeSetValueOp: replaces the value in a chain node.
  /// For struct types, detaches the old value to current scope and decrefs it,
  /// then reparents the new value under the node.
  /// </summary>
  private static void LowerChainNodeSetValue(
    MaxonChainNodeSetValueOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, TypeAliasInfo>? typeAliasSources = null) {

    var nodeVarName = structVarNames[op.Node.Id];
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    if (IsChainHeapValueKind(op.ValueKind, typeDefs)) {
      // Load old value pointer from node
      var oldValueLoad = new StdLoadIndirectOp(nodePtr, NodeValueOffset, MlirType.I64);
      block.AddOp(oldValueLoad);
      var oldValue = (StdI64)oldValueLoad.Result;

      // Destruct old value with field cleanup — node no longer holds a reference (may be null)
      EmitDestructValueIfNonnull(block, oldValue, op.ValueKind,
        typeDefs, typeAliasSources, scopeName: _currentFuncName);

      // Store new value and incref — node now holds a reference
      var valueSrcName = structVarNames[op.Value.Id];
      var newValuePtr = (StdI64)EmitLoad(block, valueSrcName, varTypes);
      var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);
      block.AddOp(new StdStoreIndirectOp(newValuePtr, nodePtrReload, NodeValueOffset, MlirType.I64));
      EmitIncrefValue(block, newValuePtr, scopeName: _currentFuncName);
    } else {
      // Primitive: just store new value
      var newValue = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(newValue, nodePtr, NodeValueOffset, MlirType.I64));
    }
  }

  /// <summary>
  /// Lowers MaxonChainClearOp: calls runtime to unlink and free all nodes.
  /// Uses maxon_chain_clear_managed for heap-value chains (decrefs node values),
  /// and maxon_chain_clear for primitive chains (no decref needed).
  /// </summary>
  private static void LowerChainClear(
    MaxonChainClearOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<string, MlirType> typeDefs) {

    var chainVarName = structVarNames[op.Chain.Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var clearFunc = IsChainHeapValueKind(op.ValueKind, typeDefs) ? "maxon_chain_clear_managed" : "maxon_chain_clear";
    block.AddOp(new StdCallRuntimeOp(clearFunc, [chainPtr], null));

    // Reset cursor to null after clearing
    var chainPtrReload = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtrReload, ChainCursorOffset, MlirType.I64));
  }

  /// <summary>
  /// Lowers MaxonChainCursorResetOp: sets the cursor field to 0 (null).
  /// </summary>
  private static void LowerChainCursorReset(
    MaxonChainCursorResetOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var chainVarName = structVarNames[op.Chain.Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainCursorOffset, MlirType.I64));
  }

  /// <summary>
  /// Lowers MaxonChainCursorValueOp: loads the value from the node currently pointed
  /// to by the chain's cursor field. The cursor must be non-null.
  /// </summary>
  private static void LowerChainCursorValue(
    MaxonChainCursorValueOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps) {

    var chainVarName = structVarNames[op.Chain.Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);

    // Load cursor (node pointer) from chain
    var cursorLoad = new StdLoadIndirectOp(chainPtr, ChainCursorOffset, MlirType.I64);
    block.AddOp(cursorLoad);
    var cursorPtr = (StdI64)cursorLoad.Result;

    // Load value from the cursor node
    var valueLoad = new StdLoadIndirectOp(cursorPtr, NodeValueOffset, MlirType.I64);
    block.AddOp(valueLoad);

    if (IsChainHeapValueKind(op.ValueKind, typeDefs)) {
      var tempName = temps.CreateTemp("chain_cursor_val", op.Result.Id, op.ValueKind, OwnershipFlags.Borrowed);
      EmitStore(block, (StdI64)valueLoad.Result, tempName, varTypes);
      structVarNames[op.Result.Id] = tempName;
      structValueTypes[op.Result.Id] = op.ValueKind;
    } else {
      valueMap[op.Result] = valueLoad.Result;
    }
  }

  /// <summary>
  /// Intercepts synthetic chain navigation calls (__chain_head, __chain_tail,
  /// __chain_node_next, __chain_node_prev, __chain_cursor_start, __chain_cursor_advance)
  /// during lowering.
  /// These load a pointer from the chain/node and set an error flag if null.
  /// Returns true if the callee was handled.
  /// </summary>
  private static bool TryLowerChainNavigation(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    bool isTryCall,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {

    // Handle cursor operations that read from/write to the chain's cursor field
    if (callee == "__chain_cursor_start") {
      return LowerChainCursorStart(args, isTryCall, block, valueMap, varTypes,
        structVarNames, errorFlagValue);
    }
    if (callee == "__chain_cursor_advance") {
      return LowerChainCursorAdvance(args, isTryCall, block, valueMap, varTypes,
        structVarNames, errorFlagValue);
    }

    int fieldOffset;
    bool isNodeNav; // true for node_next/node_prev, false for chain head/tail

    switch (callee) {
      case "__chain_head":
        fieldOffset = ChainHeadOffset;
        isNodeNav = false;
        break;
      case "__chain_tail":
        fieldOffset = ChainTailOffset;
        isNodeNav = false;
        break;
      case "__chain_node_next":
        fieldOffset = NodeNextOffset;
        isNodeNav = true;
        break;
      case "__chain_node_prev":
        fieldOffset = NodePrevOffset;
        isNodeNav = true;
        break;
      default:
        return false;
    }

    // Load the pointer from the appropriate struct
    var srcVarName = structVarNames[args[0].Id];
    var srcPtr = (StdI64)EmitLoad(block, srcVarName, varTypes);
    var ptrLoad = new StdLoadIndirectOp(srcPtr, fieldOffset, MlirType.I64);
    block.AddOp(ptrLoad);
    var loadedPtr = (StdI64)ptrLoad.Result;

    // ChainError.empty (ordinal 0) -> flag 1, ChainError.endOfChain (ordinal 1) -> flag 2.
    var errorOrdinal = isNodeNav ? 2 : 1;
    EmitNullCheckErrorFlag(block, loadedPtr, errorOrdinal, isTryCall, valueMap, varTypes, errorFlagValue);

    // Register result as a ChainNode (even if null — the error path handles that)
    if (result != null) {
      var chainNodeType = result is MaxonStruct ms ? ms.TypeName : "__ChainNode";
      var tempName = temps.CreateTemp("chain_nav", result.Id, chainNodeType, OwnershipFlags.CallReturn);
      EmitStore(block, loadedPtr, tempName, varTypes);
      // Incref the ChainNode so it survives until scope exit decref.
      // Null-guarded: on the error path the node pointer is null.
      var nodeForIncref = (StdI64)EmitLoad(block, tempName, varTypes);
      EmitIncrefValueIfNonnull(block, nodeForIncref, scopeName: _currentFuncName);
      structVarNames[result.Id] = tempName;
      structValueTypes[result.Id] = chainNodeType;
    }

    return true;
  }

  /// <summary>
  /// Lowers __chain_cursor_start: sets cursor to chain.head and sets error flag.
  /// No refcounting — cursor is a borrowed pointer; the chain owns the nodes.
  /// </summary>
  private static bool LowerChainCursorStart(
    List<MaxonValue> args,
    bool isTryCall,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    MaxonValue? errorFlagValue) {

    var chainVarName = structVarNames[args[0].Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);

    // Load head pointer
    var headLoad = new StdLoadIndirectOp(chainPtr, ChainHeadOffset, MlirType.I64);
    block.AddOp(headLoad);
    var headPtr = (StdI64)headLoad.Result;

    // Store head as cursor
    var chainPtrReload = (StdI64)EmitLoad(block, chainVarName, varTypes);
    block.AddOp(new StdStoreIndirectOp(headPtr, chainPtrReload, ChainCursorOffset, MlirType.I64));

    EmitNullCheckErrorFlag(block, headPtr, 1, isTryCall, valueMap, varTypes, errorFlagValue);

    return true;
  }

  /// <summary>
  /// Lowers __chain_cursor_advance: loads cursor->next, stores it as the new cursor.
  /// Sets error flag if the new cursor is null (at end of chain).
  /// No refcounting — cursor is a borrowed pointer; the chain owns the nodes.
  /// </summary>
  private static bool LowerChainCursorAdvance(
    List<MaxonValue> args,
    bool isTryCall,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    MaxonValue? errorFlagValue) {

    var chainVarName = structVarNames[args[0].Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);

    // Load current cursor
    var cursorLoad = new StdLoadIndirectOp(chainPtr, ChainCursorOffset, MlirType.I64);
    block.AddOp(cursorLoad);
    var cursorPtr = (StdI64)cursorLoad.Result;

    // Load cursor->next
    var nextLoad = new StdLoadIndirectOp(cursorPtr, NodeNextOffset, MlirType.I64);
    block.AddOp(nextLoad);
    var nextPtr = (StdI64)nextLoad.Result;

    // Store next as new cursor
    var chainPtrReload = (StdI64)EmitLoad(block, chainVarName, varTypes);
    block.AddOp(new StdStoreIndirectOp(nextPtr, chainPtrReload, ChainCursorOffset, MlirType.I64));

    EmitNullCheckErrorFlag(block, nextPtr, 2, isTryCall, valueMap, varTypes, errorFlagValue);

    return true;
  }

  /// <summary>
  /// Emits null-check + error flag for chain operations.
  /// Sets __error_flag to errorOrdinal if ptr is null, 0 otherwise.
  /// </summary>
  private static void EmitNullCheckErrorFlag(
    MlirBlock<StandardOp> block,
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
}
