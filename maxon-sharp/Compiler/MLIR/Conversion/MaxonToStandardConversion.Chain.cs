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

  /// Whether a type name refers to a Chain-family type (Chain itself or a concrete alias like EChain).
  private static bool IsChainType(string typeName, Dictionary<string, MlirType>? typeDefs = null) {
    if (typeName == "__Chain") return true;
    if (typeDefs != null && typeDefs.TryGetValue(typeName, out var resolved)
        && resolved is MlirStructType st && st.Name == typeName
        && st.Fields.Count == 4
        && st.Fields[0].Name == "head" && st.Fields[1].Name == "tail"
        && st.Fields[2].Name == "count" && st.Fields[3].Name == "cursor")
      return true;
    return false;
  }

  /// Whether a type name refers to a ChainNode-family type (ChainNode itself or a concrete alias).
  private static bool IsChainNodeType(string typeName, Dictionary<string, MlirType>? typeDefs = null) {
    if (typeName == "__ChainNode") return true;
    if (typeDefs != null && typeDefs.TryGetValue(typeName, out var resolved)
        && resolved is MlirStructType st && st.Name == typeName
        && st.Fields.Count == 4
        && st.Fields[0].Name == "next" && st.Fields[1].Name == "prev"
        && st.Fields[2].Name == "chain" && st.Fields[3].Name == "value")
      return true;
    return false;
  }

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
    Dictionary<int, string> structValueTypes) {

    var chainPtr = EmitAlloc(block, ChainDataSize, "__Chain");

    // Zero-initialize head, tail, count, cursor
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainHeadOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainTailOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainCountOffset, MlirType.I64));
    block.AddOp(new StdStoreIndirectOp(zero.Result, chainPtr, ChainCursorOffset, MlirType.I64));

    var tempName = $"__chain_{op.Result.Id}";
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
    Dictionary<string, MlirType> typeDefs) {

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
      // Struct/heap value: load pointer and reparent under node
      var valueHeapPtr = (StdI64)EmitLoad(block, valueSrcName, varTypes);
      block.AddOp(new StdStoreIndirectOp(valueHeapPtr, nodePtr, NodeValueOffset, MlirType.I64));
      // Reparent value under the node so it moves with the chain
      var moveMode = new StdConstI64Op(1);
      block.AddOp(moveMode);
      block.AddOp(new StdCallRuntimeOp("mm_move", [valueHeapPtr, nodePtr, moveMode.Result], null));
      if (Compiler.MmTrace)
        block.AddOp(new StdCallRuntimeOp("mm_trace_move", [valueHeapPtr], null));
    } else {
      // Primitive value: store directly
      var valueStd = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(valueStd, nodePtr, NodeValueOffset, MlirType.I64));
    }

    // Save nodePtr before runtime call (which may clobber registers)
    var nodeTempName = $"__chain_node_{op.Result.Id}";
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
    Dictionary<string, MlirType> typeDefs) {

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
      var valueHeapPtr = (StdI64)EmitLoad(block, valueSrcName, varTypes);
      block.AddOp(new StdStoreIndirectOp(valueHeapPtr, nodePtr, NodeValueOffset, MlirType.I64));
      var moveMode = new StdConstI64Op(1);
      block.AddOp(moveMode);
      block.AddOp(new StdCallRuntimeOp("mm_move", [valueHeapPtr, nodePtr, moveMode.Result], null));
      if (Compiler.MmTrace)
        block.AddOp(new StdCallRuntimeOp("mm_trace_move", [valueHeapPtr], null));
    } else {
      var valueStd = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(valueStd, nodePtr, NodeValueOffset, MlirType.I64));
    }

    // Save nodePtr before runtime calls
    var nodeTempName = $"__chain_node_{op.Result.Id}";
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

    // Reparent node under the new chain (force mode)
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var moveMode = new StdConstI64Op(1);
    block.AddOp(moveMode);
    block.AddOp(new StdCallRuntimeOp("mm_move", [nodePtr, chainPtr, moveMode.Result], null));
    if (Compiler.MmTrace)
      block.AddOp(new StdCallRuntimeOp("mm_trace_move", [nodePtr], null));

    // Reload for runtime call
    var chainPtrReload = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    var rtName = op.AtHead ? "maxon_chain_insert_first" : "maxon_chain_insert_last";
    block.AddOp(new StdCallRuntimeOp(rtName, [chainPtrReload, nodePtrReload], null));
  }

  /// <summary>
  /// Lowers MaxonChainReinsertRelativeOp: reparents node and inserts relative to target.
  /// </summary>
  private static void LowerChainReinsertRelative(
    MaxonChainReinsertRelativeOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var chainVarName = structVarNames[op.Chain.Id];
    var nodeVarName = structVarNames[op.Node.Id];

    // Reparent node under the new chain (force mode)
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var moveMode = new StdConstI64Op(1);
    block.AddOp(moveMode);
    block.AddOp(new StdCallRuntimeOp("mm_move", [nodePtr, chainPtr, moveMode.Result], null));
    if (Compiler.MmTrace)
      block.AddOp(new StdCallRuntimeOp("mm_trace_move", [nodePtr], null));

    // Reload for runtime call
    var chainPtrReload = (StdI64)EmitLoad(block, chainVarName, varTypes);
    var targetVarName = structVarNames[op.Target.Id];
    var targetPtr = (StdI64)EmitLoad(block, targetVarName, varTypes);
    var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    var rtName = op.After ? "maxon_chain_insert_after" : "maxon_chain_insert_before";
    block.AddOp(new StdCallRuntimeOp(rtName, [chainPtrReload, targetPtr, nodePtrReload], null));
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
    Dictionary<string, MlirType> typeDefs) {

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

    // Save the extracted value
    var valueTempName = $"__chain_removed_{op.Result.Id}";
    EmitStore(block, extractedValue, valueTempName, varTypes);

    // Resolve the current scope for reparenting allocations.
    var currentScopeInfo = _scopeAnalysisStack?.Count > 0 ? _scopeAnalysisStack[^1] : null;

    if (IsChainHeapValueKind(op.ValueKind, typeDefs) && currentScopeInfo != null) {
      // Struct value: force-detach from node's child list and move to current scope.
      // Must use mode=2 (force to scope) because the value is parent-owned (child
      // of the chain node), and mode=0 is a no-op for parent-owned allocations.
      var scopePtr = (StdI64)EmitLoad(block, currentScopeInfo.ScopeVar, varTypes);
      var valueReload = (StdI64)EmitLoad(block, valueTempName, varTypes);
      var moveMode = new StdConstI64Op(2);
      block.AddOp(moveMode);
      block.AddOp(new StdCallRuntimeOp("mm_move", [valueReload, scopePtr, moveMode.Result], null));
      if (Compiler.MmTrace)
        block.AddOp(new StdCallRuntimeOp("mm_trace_move", [valueReload], null));
    }

    // Release the node: decref instead of force-free so that any outstanding
    // navigation references keep the node alive until their scope exit.
    var nodePtrForDecref = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    block.AddOp(new StdCallRuntimeOp("mm_decref", [nodePtrForDecref], null));
    if (Compiler.MmTrace)
      block.AddOp(new StdCallRuntimeOp("mm_trace_decref", [nodePtrForDecref], null));

    // Register the extracted value
    if (IsChainHeapValueKind(op.ValueKind, typeDefs)) {
      structVarNames[op.Result.Id] = valueTempName;
      structValueTypes[op.Result.Id] = op.ValueKind;
    } else {
      // Primitive value: only register in valueMap, NOT in structVarNames.
      // Registering in structVarNames would cause downstream assign/move ops
      // to treat the primitive as a heap pointer (incref, mm_move).
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
    Dictionary<string, MlirType> typeDefs) {

    var nodeVarName = structVarNames[op.Node.Id];
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);
    var valueLoad = new StdLoadIndirectOp(nodePtr, NodeValueOffset, MlirType.I64);
    block.AddOp(valueLoad);

    if (IsChainHeapValueKind(op.ValueKind, typeDefs)) {
      // Heap value: return a borrowed pointer. The assignment incref (from
      // MaxonAssignOp) provides the caller's reference. mm_free_entry rescues
      // children with rc > 0, so the value survives container destruction.
      var tempName = $"__chain_val_{op.Result.Id}";
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
    Dictionary<string, MlirType> typeDefs) {

    var nodeVarName = structVarNames[op.Node.Id];
    var nodePtr = (StdI64)EmitLoad(block, nodeVarName, varTypes);

    if (IsChainHeapValueKind(op.ValueKind, typeDefs)) {
      // Load old value pointer from node
      var oldValueLoad = new StdLoadIndirectOp(nodePtr, NodeValueOffset, MlirType.I64);
      block.AddOp(oldValueLoad);
      var oldValue = (StdI64)oldValueLoad.Result;

      // Detach old value from node to current scope via mm_move mode=2.
      // Scope exit will free it (rc=0) or decref it (rc>0 from node.value()).
      var currentScopeInfo = _scopeAnalysisStack?.Count > 0 ? _scopeAnalysisStack[^1] : null;
      if (currentScopeInfo != null) {
        var scopePtr = (StdI64)EmitLoad(block, currentScopeInfo.ScopeVar, varTypes);
        var detachMode = new StdConstI64Op(2);
        block.AddOp(detachMode);
        block.AddOp(new StdCallRuntimeOp("mm_move", [oldValue, scopePtr, detachMode.Result], null));
        if (Compiler.MmTrace)
          block.AddOp(new StdCallRuntimeOp("mm_trace_move", [oldValue], null));
      }

      // Store new value and reparent under node
      var valueSrcName = structVarNames[op.Value.Id];
      var newValuePtr = (StdI64)EmitLoad(block, valueSrcName, varTypes);
      var nodePtrReload = (StdI64)EmitLoad(block, nodeVarName, varTypes);
      block.AddOp(new StdStoreIndirectOp(newValuePtr, nodePtrReload, NodeValueOffset, MlirType.I64));
      var moveMode = new StdConstI64Op(1);
      block.AddOp(moveMode);
      block.AddOp(new StdCallRuntimeOp("mm_move", [newValuePtr, nodePtrReload, moveMode.Result], null));
      if (Compiler.MmTrace)
        block.AddOp(new StdCallRuntimeOp("mm_trace_move", [newValuePtr], null));
    } else {
      // Primitive: just store new value
      var newValue = (StdI64)valueMap[op.Value];
      block.AddOp(new StdStoreIndirectOp(newValue, nodePtr, NodeValueOffset, MlirType.I64));
    }
  }

  /// <summary>
  /// Lowers MaxonChainClearOp: calls runtime to unlink and free all nodes.
  /// The runtime function walks the chain and frees each node.
  /// </summary>
  private static void LowerChainClear(
    MaxonChainClearOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var chainVarName = structVarNames[op.Chain.Id];
    var chainPtr = (StdI64)EmitLoad(block, chainVarName, varTypes);
    block.AddOp(new StdCallRuntimeOp("maxon_chain_clear", [chainPtr], null));

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
    Dictionary<string, MlirType> typeDefs) {

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
      var tempName = $"__chain_cursor_val_{op.Result.Id}";
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
    MaxonValue? errorFlagValue) {

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
      var tempName = $"__chain_nav_{result.Id}";
      EmitStore(block, loadedPtr, tempName, varTypes);
      // Incref the ChainNode so it survives until scope exit decref.
      // mm_incref is a no-op for NULL, so safe even on the error path.
      var nodeForIncref = (StdI64)EmitLoad(block, tempName, varTypes);
      block.AddOp(new StdCallRuntimeOp("mm_incref", [nodeForIncref], null));
      if (Compiler.MmTrace)
        block.AddOp(new StdCallRuntimeOp("mm_trace_incref", [nodeForIncref], null));
      structVarNames[result.Id] = tempName;
      structValueTypes[result.Id] = result is MaxonStruct ms ? ms.TypeName : "__ChainNode";
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
