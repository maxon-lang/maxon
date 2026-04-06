using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Separates iterator state from collection structs so that nested for-in loops
/// over the same collection don't corrupt each other's iteration position.
///
/// Before this pass, createIterator() is void and mutates the collection in place.
/// After this pass, createIterator() returns a separate __TypeName_Iter struct that
/// holds the iterator state fields and a __source reference back to the collection.
///
/// Runs before monomorphization so that iterator types participate in generic
/// specialization alongside their source collection types.
/// </summary>
public static class IteratorSeparationPass {
  // Per-iterable-type metadata discovered in Step 1
  private record IterableInfo(
    string FullTypeName,           // e.g. "stdlib.Array"
    string ShortTypeName,          // e.g. "Array"
    string Namespace,              // e.g. "stdlib"
    MlirStructType OriginalType,
    List<IterFieldInfo> IterFields,
    bool IsViewType                // true when ALL fields are iterator state (ByteView, etc.)
  );

  private record IterFieldInfo(
    string Name,
    MlirStructField FieldDef,
    MaxonValue? InitialValue,      // the literal value assigned in createIterator
    MaxonOp? InitialValueOp        // the op that produces InitialValue (a MaxonLiteralOp)
  );

  public static void Run(MlirModule<MaxonOp> module) {
    // Step 1: Detect iterator state fields from createIterator() methods
    var iterableInfos = DetectIteratorStateFields(module);
    if (iterableInfos.Count == 0) return;

    // Step 2: Create internal iterator struct types
    var iterTypeMap = CreateIteratorTypes(module, iterableInfos);

    // Step 2.5: Register type aliases for iterator types so monomorphization specializes them
    RegisterIteratorTypeAliases(module, iterableInfos, iterTypeMap);

    // Step 3: Clone next() methods for each iterator type
    CloneNextMethods(module, iterableInfos, iterTypeMap);

    // Step 4: Rewrite createIterator() to return the iterator struct
    RewriteCreateIteratorMethods(module, iterableInfos, iterTypeMap);

    // Step 5: Rewrite for-in loop patterns in all functions
    RewriteForInPatterns(module, iterableInfos, iterTypeMap);

    foreach (var (k, v) in iterableInfos)
      Logger.Debug(LogCategory.Mlir, $"IteratorSeparationPass: iterable '{k}' -> iter fields: [{string.Join(", ", v.IterFields.Select(f => f.Name))}]");
    Logger.Debug(LogCategory.Mlir, $"IteratorSeparationPass: processed {iterableInfos.Count} iterable types");
  }

  // ============================================================================
  // Step 1: Detect iterator state fields
  // ============================================================================

  private static Dictionary<string, IterableInfo> DetectIteratorStateFields(MlirModule<MaxonOp> module) {
    var result = new Dictionary<string, IterableInfo>();

    foreach (var func in module.Functions) {
      if (!func.Name.EndsWith(".createIterator")) continue;
      if (func.ReturnType != null && func.ReturnType != MlirType.Void) continue;
      if (func.Body.Blocks.Count == 0) continue;

      // Extract type name from function name: "stdlib.Array.createIterator" -> fullType="stdlib.Array", short="Array"
      var lastDot = func.Name.LastIndexOf('.');
      if (lastDot < 0) continue;
      var fullTypeName = func.Name[..lastDot];
      var shortTypeName = fullTypeName;
      var ns = "";
      var nsDot = fullTypeName.LastIndexOf('.');
      if (nsDot >= 0) {
        ns = fullTypeName[..nsDot];
        shortTypeName = fullTypeName[(nsDot + 1)..];
      }

      // Look up the struct type definition
      if (!module.TypeDefs.TryGetValue(shortTypeName, out var typeDef)) continue;
      if (typeDef is not MlirStructType structType) continue;

      // Scan createIterator body for MaxonFieldAssignOp on self fields
      var iterFields = new List<IterFieldInfo>();
      // Also detect calls to source.createIterator() for EnumeratedIterator-like types
      bool callsSourceCreateIterator = false;
      // Scan createIterator body to find iterator state fields

      // The parser destructures struct fields into local variables at method entry.
      // So `iterIndex = 0` inside createIterator is emitted as:
      //   MaxonFieldAccessOp .iterIndex self  (destructuring)
      //   MaxonLiteralOp 0
      //   MaxonAssignOp iterIndex = 0         (local var assignment, not field assign)
      // We detect iterator state by finding MaxonAssignOp where the var name
      // matches a field name and the value is a literal.
      var fieldNames = new HashSet<string>(structType.Fields.Select(f => f.Name));
      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var op = block.Operations[i];
          // Check for local variable assignments that match field names
          if (op is MaxonAssignOp assign && !assign.IsDeclaration && fieldNames.Contains(assign.VarName)) {
            var fieldDef = structType.GetField(assign.VarName);
            if (fieldDef != null) {
              MaxonValue? initVal = null;
              MaxonOp? initOp = null;
              for (int j = i - 1; j >= 0; j--) {
                if (block.Operations[j] is MaxonLiteralOp lit && lit.Result.Id == assign.Value.Id) {
                  initVal = lit.Result;
                  initOp = lit;
                  break;
                }
              }
              iterFields.Add(new IterFieldInfo(assign.VarName, fieldDef, initVal, initOp));
            }
          }
          // Also check MaxonFieldAssignOp for completeness
          if (op is MaxonFieldAssignOp fieldAssign) {
            var fieldDef = structType.GetField(fieldAssign.FieldName);
            if (fieldDef != null && !iterFields.Any(f => f.Name == fieldAssign.FieldName)) {
              MaxonValue? initVal = null;
              MaxonOp? initOp = null;
              for (int j = i - 1; j >= 0; j--) {
                if (block.Operations[j] is MaxonLiteralOp lit && lit.Result.Id == fieldAssign.NewValue.Id) {
                  initVal = lit.Result;
                  initOp = lit;
                  break;
                }
              }
              iterFields.Add(new IterFieldInfo(fieldAssign.FieldName, fieldDef, initVal, initOp));
            }
          }
          if (op is MaxonCallOp call && call.Callee.EndsWith(".createIterator")) {
            callsSourceCreateIterator = true;
          }
        }
      }

      // Also scan next() for field assignments not already detected in createIterator().
      // This catches fields like iterCursor that are only assigned during iteration, not init.
      var nextFuncName = $"{fullTypeName}.next";
      var nextFunc = module.Functions.FirstOrDefault(f => f.Name == nextFuncName);
      if (nextFunc != null) {
        var existingIterFieldNames = new HashSet<string>(iterFields.Select(f => f.Name));
        foreach (var block in nextFunc.Body.Blocks) {
          for (int i = 0; i < block.Operations.Count; i++) {
            var op = block.Operations[i];
            if (op is MaxonAssignOp assign && !assign.IsDeclaration
                && fieldNames.Contains(assign.VarName) && !existingIterFieldNames.Contains(assign.VarName)) {
              var fieldDef = structType.GetField(assign.VarName);
              if (fieldDef != null) {
                iterFields.Add(new IterFieldInfo(assign.VarName, fieldDef, null, null));
                existingIterFieldNames.Add(assign.VarName);
              }
            }
            if (op is MaxonFieldAssignOp fieldAssign
                && fieldNames.Contains(fieldAssign.FieldName) && !existingIterFieldNames.Contains(fieldAssign.FieldName)) {
              var fieldDef = structType.GetField(fieldAssign.FieldName);
              if (fieldDef != null) {
                iterFields.Add(new IterFieldInfo(fieldAssign.FieldName, fieldDef, null, null));
                existingIterFieldNames.Add(fieldAssign.FieldName);
              }
            }
          }
        }
      }

      if (iterFields.Count == 0 && !callsSourceCreateIterator) continue;

      // Only Array, Vector, and List have simple iterator state fields.
      // Map, Set, String use multiple or non-trivial iterator state fields that
      // require more sophisticated cloning logic.
      var supportedSimpleIterTypes = new HashSet<string> { "Array", "Vector", "List" };
      if (!supportedSimpleIterTypes.Contains(shortTypeName)) continue;

      // Determine if this is a "view type" where ALL fields are iterator state
      var nonIterFields = structType.Fields.Where(f =>
        !iterFields.Any(itr => itr.Name == f.Name)).ToList();
      bool isViewType = nonIterFields.Count == 0;

      result[fullTypeName] = new IterableInfo(
        fullTypeName, shortTypeName, ns, structType, iterFields, isViewType);
    }

    return result;
  }

  // ============================================================================
  // Step 2: Create internal iterator struct types
  // ============================================================================

  private static Dictionary<string, MlirStructType> CreateIteratorTypes(
      MlirModule<MaxonOp> module, Dictionary<string, IterableInfo> iterableInfos) {
    var iterTypeMap = new Dictionary<string, MlirStructType>(); // fullTypeName -> iter struct type

    foreach (var (fullTypeName, info) in iterableInfos) {
      if (info.IsViewType) {
        // View types: the iterator IS the original type — createIterator returns a copy
        // We don't need a separate iterator struct, just reuse the original type
        iterTypeMap[fullTypeName] = info.OriginalType;
        continue;
      }

      var iterTypeName = $"__{info.ShortTypeName}_Iter";

      // Build fields: __source (reference to collection) + iterator state fields
      var fields = new List<MlirStructField> {
        // __source field: reference to the original collection type
        new("__source", info.OriginalType,
        isExported: false, isMutable: false)
      };

      // Copy iterator state fields from the original type
      foreach (var iterField in info.IterFields) {
        fields.Add(new MlirStructField(iterField.Name, iterField.FieldDef.Type,
          isExported: false, isMutable: true, iterField.FieldDef.DefaultValue));
      }

      var iterStructType = new MlirStructType(iterTypeName, fields,
        associatedTypeNames: [.. info.OriginalType.AssociatedTypeNames],
        typeParams: new Dictionary<string, MlirType>(info.OriginalType.TypeParams));

      // Register in module
      module.TypeDefs[iterTypeName] = iterStructType;
      iterTypeMap[fullTypeName] = iterStructType;

      Logger.Debug(LogCategory.Mlir,
        $"IteratorSeparationPass: created {iterTypeName} with {fields.Count} fields " +
        $"(1 __source + {info.IterFields.Count} iter state)");
    }

    return iterTypeMap;
  }

  // ============================================================================
  // Step 2.5: Register type aliases for iterator types
  // ============================================================================

  /// <summary>
  /// For each existing type alias that points to a collection type we created an iterator for,
  /// register a parallel alias for the iterator type. This lets monomorphization specialize
  /// the iterator's next() method alongside the collection methods.
  ///
  /// Example: if IntArray is an alias for Array with Element=Int, we register
  /// __IntArray_Iter as an alias for __Array_Iter with Element=Int.
  /// </summary>
  private static void RegisterIteratorTypeAliases(
      MlirModule<MaxonOp> module,
      Dictionary<string, IterableInfo> iterableInfos,
      Dictionary<string, MlirStructType> iterTypeMap) {

    // Build a reverse map: source type short name -> full type name in iterableInfos
    var shortNameToFull = new Dictionary<string, string>();
    foreach (var (fullName, info) in iterableInfos) {
      shortNameToFull[info.ShortTypeName] = fullName;
    }

    // Scan existing type aliases for ones that reference our iterable types
    foreach (var (aliasName, aliasInfo) in module.TypeAliasSources.ToList()) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      // Skip aliases that still have unresolved type parameters
      if (aliasInfo.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;

      // Check if the source type name corresponds to one of our iterable types
      if (!shortNameToFull.TryGetValue(aliasInfo.SourceTypeName, out var fullTypeName)) continue;
      if (!iterTypeMap.TryGetValue(fullTypeName, out var iterType)) continue;

      var info = iterableInfos[fullTypeName];
      if (info.IsViewType) continue; // View types don't get separate iterator types

      // Create the iterator alias name: __AliasName_Iter
      var iterAliasName = $"__{aliasName}_Iter";

      // Skip if already registered
      if (module.TypeAliasSources.ContainsKey(iterAliasName)) continue;

      // Register the alias: __IntArray_Iter -> __Array_Iter with same type params
      module.TypeAliasSources[iterAliasName] = new TypeAliasInfo(
        iterType.Name, new Dictionary<string, MlirType>(aliasInfo.TypeParams),
        aliasInfo.IsExported, aliasInfo.IsStdlib, aliasInfo.SourceFilePath);

      // Create a concrete TypeDef for this iterator alias.
      // The __source field gets the concrete collection type, other fields stay as-is.
      if (module.TypeDefs.TryGetValue(aliasName, out var concreteCollectionType)
          && concreteCollectionType is MlirStructType concreteCollStruct) {
        var concreteFields = new List<MlirStructField> {
          new("__source", concreteCollStruct,
          isExported: false, isMutable: false)
        };
        // Copy iterator state fields (resolve type parameters to concrete types)
        foreach (var iterField in info.IterFields) {
          var fieldType = iterField.FieldDef.Type;
          // Resolve type parameters from the alias's type params
          if (fieldType is MlirTypeParameterType tp
              && aliasInfo.TypeParams.TryGetValue(tp.ParameterName, out var resolvedType)) {
            fieldType = resolvedType;
          }
          concreteFields.Add(new MlirStructField(iterField.Name, fieldType,
            isExported: false, isMutable: true, iterField.FieldDef.DefaultValue));
        }
        var concreteIterType = new MlirStructType(iterAliasName, concreteFields);
        module.TypeDefs[iterAliasName] = concreteIterType;
      }

      Logger.Debug(LogCategory.Mlir,
        $"IteratorSeparationPass: registered alias {iterAliasName} -> {iterType.Name}");
    }
  }

  // ============================================================================
  // Step 3: Clone next() methods for iterator types
  // ============================================================================

  private static void CloneNextMethods(
      MlirModule<MaxonOp> module,
      Dictionary<string, IterableInfo> iterableInfos,
      Dictionary<string, MlirStructType> iterTypeMap) {

    foreach (var (fullTypeName, info) in iterableInfos) {
      if (info.IsViewType) continue; // View types use the original next() directly

      var iterType = iterTypeMap[fullTypeName];
      var iterTypeName = iterType.Name;
      var originalNextName = $"{fullTypeName}.next";
      var iterNextName = $"{iterTypeName}.next";

      // Find the original next() function
      var originalNext = module.Functions.FirstOrDefault(f => f.Name == originalNextName);
      if (originalNext == null) continue;
      if (originalNext.Body.Blocks.Count == 0) continue;

      // Collect the set of iterator state field names for quick lookup
      var iterFieldNames = new HashSet<string>(info.IterFields.Select(f => f.Name));

      // Build a new function with the iterator type as self parameter
      var newParamTypes = new List<MlirType> { iterType };
      for (int i = 1; i < originalNext.ParamTypes.Count; i++)
        newParamTypes.Add(originalNext.ParamTypes[i]);

      var clonedFunc = new MlirFunction<MaxonOp>(
        iterNextName,
        [.. originalNext.ParamNames],
        newParamTypes,
        originalNext.ReturnType,
        originalNext.ThrowsType) {
        IsStdlib = originalNext.IsStdlib,
        IsExported = originalNext.IsExported,
        SourceFilePath = originalNext.SourceFilePath,
        SourceLine = originalNext.SourceLine,
        SourceColumn = originalNext.SourceColumn,
        ExtensionWhereConstraints = originalNext.ExtensionWhereConstraints
      };

      // Clone each block, rewriting ops as needed
      // We need a value map to remap SSA values from old to new
      var valueMap = new Dictionary<int, MaxonValue>();

      foreach (var srcBlock in originalNext.Body.Blocks) {
        var newBlock = clonedFunc.Body.AddBlock(srcBlock.Name);

        foreach (var op in srcBlock.Operations) {
          CloneOpForIterator(op, newBlock, valueMap, info, iterType, iterFieldNames);
        }
      }

      module.AddFunction(clonedFunc);
      Logger.Debug(LogCategory.Mlir, $"IteratorSeparationPass: cloned {iterNextName}");
    }
  }

  /// <summary>
  /// Clones a single op from the original next() into the iterator's next(),
  /// redirecting field accesses as needed.
  /// </summary>
  private static void CloneOpForIterator(
      MaxonOp op,
      MlirBlock<MaxonOp> targetBlock,
      Dictionary<int, MaxonValue> valueMap,
      IterableInfo info,
      MlirStructType iterType,
      HashSet<string> iterFieldNames) {

    MaxonValue Remap(MaxonValue v) => valueMap.TryGetValue(v.Id, out var mapped) ? mapped : v;
    List<MaxonValue> RemapAll(List<MaxonValue> vs) => [.. vs.Select(Remap)];

    switch (op) {
      case MaxonStructParamOp paramOp when paramOp.Name == "self": {
        // Change self param from original type to iterator type
        var newOp = new MaxonStructParamOp(paramOp.Index, paramOp.Name, iterType.Name);
        valueMap[paramOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonFieldAccessOp fieldAccess
        when fieldAccess.TypeName == info.ShortTypeName
          || fieldAccess.TypeName == info.FullTypeName: {
        if (iterFieldNames.Contains(fieldAccess.FieldName)) {
          // Iterator state field: access directly on self (the iterator struct)
          var newOp = new MaxonFieldAccessOp(
            Remap(fieldAccess.StructValue), iterType.Name, fieldAccess.FieldName,
            fieldAccess.ResultKind, fieldAccess.ResultStructTypeName);
          valueMap[fieldAccess.Result.Id] = newOp.Result;
          targetBlock.AddOp(newOp);
        } else {
          // Collection field: access through self.__source
          var sourceAccess = new MaxonFieldAccessOp(
            Remap(fieldAccess.StructValue), iterType.Name, "__source",
            MaxonValueKind.Struct, info.ShortTypeName);
          targetBlock.AddOp(sourceAccess);

          var newOp = new MaxonFieldAccessOp(
            sourceAccess.Result, info.ShortTypeName, fieldAccess.FieldName,
            fieldAccess.ResultKind, fieldAccess.ResultStructTypeName);
          valueMap[fieldAccess.Result.Id] = newOp.Result;
          targetBlock.AddOp(newOp);
        }
        break;
      }

      case MaxonFieldAssignOp fieldAssign
        when (fieldAssign.TypeName == info.ShortTypeName
          || fieldAssign.TypeName == info.FullTypeName)
          && iterFieldNames.Contains(fieldAssign.FieldName): {
        // Iterator state field assignment: assign on iterator struct
        var newOp = new MaxonFieldAssignOp(
          Remap(fieldAssign.StructValue), iterType.Name, fieldAssign.FieldName,
          Remap(fieldAssign.NewValue));
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonFieldAssignOp fieldAssign
        when fieldAssign.TypeName == info.ShortTypeName
          || fieldAssign.TypeName == info.FullTypeName: {
        // Collection field assignment: through __source
        var sourceAccess = new MaxonFieldAccessOp(
          Remap(fieldAssign.StructValue), iterType.Name, "__source",
          MaxonValueKind.Struct, info.ShortTypeName);
        targetBlock.AddOp(sourceAccess);

        var newOp = new MaxonFieldAssignOp(
          sourceAccess.Result, info.ShortTypeName, fieldAssign.FieldName,
          Remap(fieldAssign.NewValue));
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonStructVarRefOp varRef when varRef.StructTypeName == info.ShortTypeName
          || varRef.StructTypeName == info.FullTypeName: {
        // If this loads self, it should be the iterator type now
        // We can't always know if it's self, so check the var name
        if (varRef.VarName == "self") {
          var newOp = new MaxonStructVarRefOp(varRef.VarName, iterType.Name);
          valueMap[varRef.Result.Id] = newOp.Result;
          targetBlock.AddOp(newOp);
        } else {
          // Some other variable of the original type — keep as-is
          var newOp = new MaxonStructVarRefOp(varRef.VarName, varRef.StructTypeName);
          valueMap[varRef.Result.Id] = newOp.Result;
          targetBlock.AddOp(newOp);
        }
        break;
      }

      // --- Generic op cloning with value remapping ---

      case MaxonLiteralOp lit: {
        MaxonLiteralOp newOp;
        if (lit.ValueKind == MaxonValueKind.Integer)
          newOp = new MaxonLiteralOp(lit.IntValue);
        else if (lit.ValueKind == MaxonValueKind.Float)
          newOp = new MaxonLiteralOp(lit.FloatValue);
        else if (lit.ValueKind == MaxonValueKind.Float32)
          newOp = new MaxonLiteralOp(lit.FloatValue, MaxonValueKind.Float32);
        else if (lit.ValueKind == MaxonValueKind.Bool)
          newOp = new MaxonLiteralOp(lit.BoolValue);
        else
          newOp = new MaxonLiteralOp(lit.IntValue);
        valueMap[lit.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonAssignOp assign: {
        var newOp = new MaxonAssignOp(assign.VarName, Remap(assign.Value),
          assign.IsDeclaration, assign.IsMutable, assign.ValueKind);
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonVarRefOp varRef: {
        var newOp = new MaxonVarRefOp(varRef.VarName, varRef.ValueKind);
        valueMap[varRef.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonTryCallOp tryCall: {
        var newOp = new MaxonTryCallOp(tryCall.Callee, RemapAll(tryCall.Args),
          tryCall.ResultKind, tryCall.ResultStructTypeName);
        if (tryCall.Result != null && newOp.Result != null)
          valueMap[tryCall.Result.Id] = newOp.Result;
        valueMap[tryCall.ErrorFlag.Id] = newOp.ErrorFlag;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonCallOp call: {
        var newOp = new MaxonCallOp(call.Callee, RemapAll(call.Args),
          call.ResultKind, call.ResultStructTypeName);
        if (call.Result != null && newOp.Result != null)
          valueMap[call.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonBinOp binOp: {
        var newOp = new MaxonBinOp(binOp.Operator, Remap(binOp.Lhs), Remap(binOp.Rhs),
          binOp.OperandKind, binOp.OptimalType);
        valueMap[binOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonCondBrOp condBr: {
        var newOp = new MaxonCondBrOp(Remap(condBr.Condition), condBr.ThenBlock, condBr.ElseBlock);
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonBrOp br: {
        targetBlock.AddOp(new MaxonBrOp(br.Target));
        break;
      }

      case MaxonReturnOp ret: {
        var newOp = new MaxonReturnOp(ret.Value != null ? Remap(ret.Value) : null, ret.IsErrorPropagation);
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonThrowOp throwOp: {
        var newOp = new MaxonThrowOp(Remap(throwOp.ErrorValue), throwOp.ErrorTypeName);
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonScopeEndOp scopeEnd: {
        var newOp = new MaxonScopeEndOp(scopeEnd.VarsToClean, scopeEnd.KeepVars);
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonEnumLiteralOp enumLit: {
        var newOp = new MaxonEnumLiteralOp(enumLit.EnumTypeName, enumLit.CaseName, enumLit.IntValue);
        valueMap[enumLit.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonEnumConstructOp enumConstruct: {
        var newOp = new MaxonEnumConstructOp(enumConstruct.EnumTypeName, enumConstruct.CaseName,
          enumConstruct.TagValue, RemapAll(enumConstruct.Args));
        valueMap[enumConstruct.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedMemGetOp mmGet: {
        var newOp = new MaxonManagedMemGetOp(Remap(mmGet.ManagedStruct), Remap(mmGet.Index), mmGet.ResultKind) {
          IsStructElement = mmGet.IsStructElement,
          StructElementTypeName = mmGet.StructElementTypeName,
          TypeParamName = mmGet.TypeParamName
        };
        valueMap[mmGet.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedMemSetOp mmSet: {
        var newOp = new MaxonManagedMemSetOp(Remap(mmSet.ManagedStruct), Remap(mmSet.Index),
          Remap(mmSet.Value), mmSet.ElementKind) {
          IsStructElement = mmSet.IsStructElement,
          TypeParamName = mmSet.TypeParamName
        };
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonTruncOp trunc: {
        var newOp = new MaxonTruncOp(Remap(trunc.Input));
        valueMap[trunc.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonCastOp cast: {
        var newOp = new MaxonCastOp(Remap(cast.Input), cast.TargetKind, cast.SourceOptimalType);
        valueMap[cast.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonStructLiteralOp structLit: {
        var newFieldValues = structLit.FieldValues
          .Select(fv => (fv.FieldName, Remap(fv.Value))).ToList();
        var newOp = new MaxonStructLiteralOp(structLit.TypeName, newFieldValues);
        valueMap[structLit.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonParamOp paramOp: {
        var newOp = new MaxonParamOp(paramOp.Index, paramOp.Name, paramOp.ValueKind);
        valueMap[paramOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonStructParamOp structParam: {
        // Generic case (non-self params)
        var newOp = new MaxonStructParamOp(structParam.Index, structParam.Name, structParam.StructTypeName);
        valueMap[structParam.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonFieldAccessOp fieldAccess: {
        // Generic field access on a type we don't need to redirect
        var newOp = new MaxonFieldAccessOp(Remap(fieldAccess.StructValue), fieldAccess.TypeName,
          fieldAccess.FieldName, fieldAccess.ResultKind, fieldAccess.ResultStructTypeName);
        valueMap[fieldAccess.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonFieldAssignOp fieldAssign: {
        // Generic field assignment on a type we don't need to redirect
        var newOp = new MaxonFieldAssignOp(Remap(fieldAssign.StructValue), fieldAssign.TypeName,
          fieldAssign.FieldName, Remap(fieldAssign.NewValue));
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonStructVarRefOp varRef: {
        // Generic struct var ref we don't need to redirect
        var newOp = new MaxonStructVarRefOp(varRef.VarName, varRef.StructTypeName);
        valueMap[varRef.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonGlobalLoadOp globalLoad: {
        var newOp = new MaxonGlobalLoadOp(globalLoad.GlobalName, globalLoad.ValueKind,
          globalLoad.EnumTypeName, globalLoad.StructTypeName);
        valueMap[globalLoad.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonIntToFloatOp itf: {
        var newOp = new MaxonIntToFloatOp(Remap(itf.Input));
        valueMap[itf.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedMemSetLengthOp setLen: {
        var newOp = new MaxonManagedMemSetLengthOp(Remap(setLen.ManagedStruct), Remap(setLen.NewLength));
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonEnumTagOp tag: {
        var newOp = new MaxonEnumTagOp(Remap(tag.EnumValue), tag.EnumTypeName);
        valueMap[tag.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonEnumPayloadOp payload: {
        var newOp = new MaxonEnumPayloadOp(Remap(payload.EnumValue), payload.EnumTypeName,
          payload.PayloadIndex, payload.ResultKind, payload.ResultStructTypeName);
        valueMap[payload.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonEnumVarRefOp enumRef: {
        var newOp = new MaxonEnumVarRefOp(enumRef.VarName, enumRef.EnumTypeName, enumRef.BackingKind);
        valueMap[enumRef.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonIndirectCallOp indirectCall: {
        var newOp = new MaxonIndirectCallOp(Remap(indirectCall.Callee), indirectCall.CalleeType,
          RemapAll(indirectCall.Args), indirectCall.ResultKind, indirectCall.ResultStructTypeName);
        if (indirectCall.Result != null && newOp.Result != null)
          valueMap[indirectCall.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedMemClearOp: {
        targetBlock.AddOp(op);
        break;
      }

      case MaxonManagedListCountOp countOp: {
        var newOp = new MaxonManagedListCountOp(Remap(countOp.ManagedList));
        valueMap[countOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedListNodeValueOp nodeValOp: {
        var newOp = new MaxonManagedListNodeValueOp(Remap(nodeValOp.Node), nodeValOp.ValueKind, nodeValOp.ResultKind);
        valueMap[nodeValOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedListHeadPtrOp headPtrOp: {
        var newOp = new MaxonManagedListHeadPtrOp(Remap(headPtrOp.ManagedList));
        valueMap[headPtrOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedListNodePtrNextOp nodePtrNextOp: {
        var newOp = new MaxonManagedListNodePtrNextOp(Remap(nodePtrNextOp.CursorPtr));
        valueMap[nodePtrNextOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      case MaxonManagedListNodePtrValueOp nodePtrValueOp: {
        var newOp = new MaxonManagedListNodePtrValueOp(Remap(nodePtrValueOp.CursorPtr), nodePtrValueOp.ValueKind, nodePtrValueOp.ResultKind);
        valueMap[nodePtrValueOp.Result.Id] = newOp.Result;
        targetBlock.AddOp(newOp);
        break;
      }

      default:
        throw new InvalidOperationException(
          $"IteratorSeparationPass: unhandled op type '{op.GetType().Name}' in next() clone");
    }
  }

  // ============================================================================
  // Step 4: Rewrite createIterator() to return iterator struct
  // ============================================================================

  private static void RewriteCreateIteratorMethods(
      MlirModule<MaxonOp> module,
      Dictionary<string, IterableInfo> iterableInfos,
      Dictionary<string, MlirStructType> iterTypeMap) {

    // Instead of replacing the existing createIterator (which would break stdlib callers
    // that expect void return), we add a new __createIter function that returns the
    // iterator struct. The original createIterator remains unchanged for backward compat.
    foreach (var (fullTypeName, info) in iterableInfos) {
      var existingName = $"{fullTypeName}.createIterator";
      var existingFunc = module.Functions.FirstOrDefault(f => f.Name == existingName);
      if (existingFunc == null || existingFunc.Body.Blocks.Count == 0) continue;

      var iterType = iterTypeMap[fullTypeName];
      var newName = $"{fullTypeName}.__createIter";

      // Don't add if already exists
      if (module.Functions.Any(f => f.Name == newName)) continue;

      MlirFunction<MaxonOp> newFunc;
      if (info.IsViewType) {
        newFunc = BuildViewTypeCreateIterator(existingFunc, info, iterType);
      } else {
        newFunc = BuildNormalCreateIterator(existingFunc, info, iterType);
      }
      newFunc.Name = newName;

      module.AddFunction(newFunc);
    }
  }

  private static MlirFunction<MaxonOp> BuildNormalCreateIterator(
      MlirFunction<MaxonOp> original,
      IterableInfo info,
      MlirStructType iterType) {

    var newFunc = new MlirFunction<MaxonOp>(
      original.Name,
      [.. original.ParamNames],
      [.. original.ParamTypes],
      iterType, // return type is now the iterator struct
      original.ThrowsType) {
      IsStdlib = original.IsStdlib,
      IsExported = original.IsExported,
      SourceFilePath = original.SourceFilePath,
      SourceLine = original.SourceLine,
      SourceColumn = original.SourceColumn,
      ExtensionWhereConstraints = original.ExtensionWhereConstraints
    };

    var block = newFunc.Body.AddBlock("entry");

    // self parameter
    var selfParam = new MaxonStructParamOp(0, "self", info.ShortTypeName);
    block.AddOp(selfParam);

    // Build field values for the iterator struct literal
    var fieldValues = new List<(string FieldName, MaxonValue Value)> {
      // __source = self
      ("__source", selfParam.Result)
    };

    // Iterator state fields with their initial values
    foreach (var iterField in info.IterFields) {
      MaxonValue initValue;
      if (iterField.InitialValue != null && iterField.InitialValueOp is MaxonLiteralOp srcLit) {
        var newLit = CloneLiteralOp(srcLit);
        block.AddOp(newLit);
        initValue = newLit.Result;
      } else {
        var defaultLit = new MaxonLiteralOp(0L);
        block.AddOp(defaultLit);
        initValue = defaultLit.Result;
      }
      fieldValues.Add((iterField.Name, initValue));
    }

    // Create struct literal
    var structLit = new MaxonStructLiteralOp(iterType.Name, fieldValues);
    block.AddOp(structLit);

    // Assign to retval and return
    var retvalName = $"__retval_{MlirContext.Current.NextId()}";
    block.AddOp(new MaxonAssignOp(retvalName, structLit.Result, true, false, MaxonValueKind.Struct));
    block.AddOp(new MaxonScopeEndOp([retvalName], keepVars: [retvalName]));
    block.AddOp(new MaxonReturnOp(structLit.Result));

    return newFunc;
  }

  private static MlirFunction<MaxonOp> BuildViewTypeCreateIterator(
      MlirFunction<MaxonOp> original,
      IterableInfo info,
      MlirStructType viewType) {

    var newFunc = new MlirFunction<MaxonOp>(
      original.Name,
      [.. original.ParamNames],
      [.. original.ParamTypes],
      viewType, // return type is the view type itself
      original.ThrowsType) {
      IsStdlib = original.IsStdlib,
      IsExported = original.IsExported,
      SourceFilePath = original.SourceFilePath,
      SourceLine = original.SourceLine,
      SourceColumn = original.SourceColumn,
      ExtensionWhereConstraints = original.ExtensionWhereConstraints
    };

    var block = newFunc.Body.AddBlock("entry");

    var selfParam = new MaxonStructParamOp(0, "self", info.ShortTypeName);
    block.AddOp(selfParam);

    var fieldValues = new List<(string FieldName, MaxonValue Value)>();
    var iterFieldNames = new HashSet<string>(info.IterFields.Select(f => f.Name));

    foreach (var field in viewType.Fields) {
      if (iterFieldNames.Contains(field.Name)) {
        var iterField = info.IterFields.First(f => f.Name == field.Name);
        if (iterField.InitialValueOp is MaxonLiteralOp srcLit) {
          var newLit = CloneLiteralOp(srcLit);
          block.AddOp(newLit);
          fieldValues.Add((field.Name, newLit.Result));
        } else {
          var defaultLit = new MaxonLiteralOp(0L);
          block.AddOp(defaultLit);
          fieldValues.Add((field.Name, defaultLit.Result));
        }
      } else {
        // Non-iterator field: copy from self
        var fieldKind = field.Type.ToValueKind();
        string? fieldStructTypeName = null;
        if (field.Type is MlirStructType fst) fieldStructTypeName = fst.Name;
        else if (field.Type is MlirEnumType fut) fieldStructTypeName = fut.Name;

        var access = new MaxonFieldAccessOp(selfParam.Result, info.ShortTypeName, field.Name,
          fieldKind, fieldStructTypeName);
        block.AddOp(access);
        fieldValues.Add((field.Name, access.Result));
      }
    }

    var structLit = new MaxonStructLiteralOp(viewType.Name, fieldValues);
    block.AddOp(structLit);

    var retvalName = $"__retval_{MlirContext.Current.NextId()}";
    block.AddOp(new MaxonAssignOp(retvalName, structLit.Result, true, false, MaxonValueKind.Struct));
    block.AddOp(new MaxonScopeEndOp([retvalName], keepVars: [retvalName]));
    block.AddOp(new MaxonReturnOp(structLit.Result));

    return newFunc;
  }

  private static MaxonLiteralOp CloneLiteralOp(MaxonLiteralOp srcLit) {
    if (srcLit.ValueKind == MaxonValueKind.Integer)
      return new MaxonLiteralOp(srcLit.IntValue);
    if (srcLit.ValueKind == MaxonValueKind.Float)
      return new MaxonLiteralOp(srcLit.FloatValue);
    if (srcLit.ValueKind == MaxonValueKind.Float32)
      return new MaxonLiteralOp(srcLit.FloatValue, MaxonValueKind.Float32);
    if (srcLit.ValueKind == MaxonValueKind.Bool)
      return new MaxonLiteralOp(srcLit.BoolValue);
    return new MaxonLiteralOp(srcLit.IntValue);
  }

  // ============================================================================
  // Step 5: Rewrite for-in loop patterns
  // ============================================================================

  private static void RewriteForInPatterns(
      MlirModule<MaxonOp> module,
      Dictionary<string, IterableInfo> iterableInfos,
      Dictionary<string, MlirStructType> iterTypeMap) {

    // Build a lookup from createIterator callee name -> info
    var createIterCalleeToInfo = new Dictionary<string, (IterableInfo Info, MlirStructType IterType)>();
    foreach (var (fullTypeName, info) in iterableInfos) {
      var calleeName = $"{fullTypeName}.createIterator";
      createIterCalleeToInfo[calleeName] = (info, iterTypeMap[fullTypeName]);
    }

    // Build a lookup from next callee name -> info
    var nextCalleeToInfo = new Dictionary<string, (IterableInfo Info, MlirStructType IterType)>();
    foreach (var (fullTypeName, info) in iterableInfos) {
      var calleeName = $"{fullTypeName}.next";
      nextCalleeToInfo[calleeName] = (info, iterTypeMap[fullTypeName]);
    }

    // First pass: identify which functions need rewriting.
    // Only rewrite non-stdlib functions. Stdlib functions are shared across compilations
    // and have complex monomorphization patterns; they continue to use the old void
    // createIterator pattern. Their for-in loops still work because the source collection
    // aliasing pattern (separate struct instances per for-in) prevents nested conflicts
    // within a single generic method. The real problem is user code that nests loops
    // over the same concrete collection instance.
    for (int funcIdx = 0; funcIdx < module.Functions.Count; funcIdx++) {
      var func = module.Functions[funcIdx];
      if (func.IsStdlib) continue; // Don't rewrite stdlib functions
      if (!FunctionNeedsForInRewrite(func, createIterCalleeToInfo)) continue;

      // This function has for-in patterns that need rewriting.
      // Build a completely new function to avoid mutating shared objects.
      var newFunc = RebuildFunctionWithForInRewrite(func, createIterCalleeToInfo, nextCalleeToInfo, module);
      module.Functions[funcIdx] = newFunc;
    }
  }

  /// <summary>
  /// Checks if a function contains any for-in patterns that need rewriting.
  /// </summary>
  private static bool FunctionNeedsForInRewrite(
      MlirFunction<MaxonOp> func,
      Dictionary<string, (IterableInfo Info, MlirStructType IterType)> createIterCalleeToInfo) {
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonCallOp call && createIterCalleeToInfo.ContainsKey(call.Callee))
          return true;
      }
    }
    return false;
  }

  /// <summary>
  /// Creates a new function that is a copy of the original with for-in patterns rewritten.
  /// Never modifies the original function's blocks or ops.
  /// </summary>
  /// <summary>
  /// Given a collection type name (e.g. "IntArray") and the generic iterator type name
  /// (e.g. "__Array_Iter"), derive the concrete iterator type name (e.g. "__IntArray_Iter").
  /// Falls back to the generic iterator type name if no concrete alias exists.
  /// </summary>
  private static string DeriveConcreteIterTypeName(
      string collectionTypeName, string genericIterTypeName, MlirModule<MaxonOp> module) {
    var concreteIterName = $"__{collectionTypeName}_Iter";
    // Check if this alias was registered in TypeAliasSources
    if (module.TypeAliasSources.ContainsKey(concreteIterName))
      return concreteIterName;
    // For non-aliased types (e.g. the generic type itself), use the generic name
    return genericIterTypeName;
  }

  private static MlirFunction<MaxonOp> RebuildFunctionWithForInRewrite(
      MlirFunction<MaxonOp> original,
      Dictionary<string, (IterableInfo Info, MlirStructType IterType)> createIterCalleeToInfo,
      Dictionary<string, (IterableInfo Info, MlirStructType IterType)> nextCalleeToInfo,
      MlirModule<MaxonOp> module) {

    var newFunc = new MlirFunction<MaxonOp>(
      original.Name,
      [.. original.ParamNames],
      [.. original.ParamTypes],
      original.ReturnType,
      original.ThrowsType) {
      IsStdlib = original.IsStdlib,
      IsExported = original.IsExported,
      SourceFilePath = original.SourceFilePath,
      SourceLine = original.SourceLine,
      SourceColumn = original.SourceColumn,
      ExtensionWhereConstraints = original.ExtensionWhereConstraints,
      IsPure = original.IsPure,
      ReturnsSelf = original.ReturnsSelf
    };

    // Track which __for_iter_N variables have been rewritten to use an iterator type
    // ConcreteIterTypeName is the resolved name (e.g. __IntArray_Iter for IntArray collections)
    var iterVarTypes = new Dictionary<string, (IterableInfo Info, MlirStructType IterType, string ConcreteIterTypeName)>();
    // Track value remapping (old SSA id -> new value) for rewrites within a block
    var valueMap = new Dictionary<int, MaxonValue>();

    foreach (var srcBlock in original.Body.Blocks) {
      var newBlock = newFunc.Body.AddBlock(srcBlock.Name);
      var ops = srcBlock.Operations;
      valueMap.Clear();
      int i = 0;

      while (i < ops.Count) {
        // Pattern: MaxonAssignOp declaring __for_iter_N = iterable,
        //          followed by MaxonCallOp to *.createIterator with __for_iter_N
        if (ops[i] is MaxonAssignOp assign
            && assign.VarName.StartsWith("__for_iter_")
            && assign.IsDeclaration
            && i + 1 < ops.Count
            && ops[i + 1] is MaxonCallOp createIterCall
            && createIterCalleeToInfo.TryGetValue(createIterCall.Callee, out var iterInfo)) {

          var (info, iterType) = iterInfo;

          // Derive concrete iterator type name from the collection variable's type
          string concreteIterTypeName = iterType.Name;
          if (assign.Value is MaxonStruct collStruct) {
            concreteIterTypeName = DeriveConcreteIterTypeName(collStruct.TypeName, iterType.Name, module);
          }
          // concreteIterTypeName is now resolved (e.g. __IntArray_Iter for IntArray)

          // Emit: call __createIter (the new function that returns an iterator struct)
          // Derive the __createIter name from the original createIterator callee:
          // e.g. "stdlib.Array.createIterator" -> "stdlib.Array.__createIter"
          var createIterCallee = createIterCall.Callee.Replace(".createIterator", ".__createIter");
          var newCreateIterCall = new MaxonCallOp(
            createIterCallee,
            [assign.Value],
            MaxonValueKind.Struct,
            concreteIterTypeName);
          newBlock.AddOp(newCreateIterCall);

          // Emit: assign __for_iter_N = createIterator result (now iterator type)
          var newAssign = new MaxonAssignOp(
            assign.VarName, newCreateIterCall.Result!,
            isDeclaration: true, isMutable: true, MaxonValueKind.Struct);
          newBlock.AddOp(newAssign);

          // Track this iter var for later rewrites
          iterVarTypes[assign.VarName] = (info, iterType, concreteIterTypeName);

          i += 2; // skip original assign + createIterator call
          continue;
        }

        // Pattern: MaxonStructVarRefOp loading __for_iter_N with original type
        if (ops[i] is MaxonStructVarRefOp structVarRef
            && iterVarTypes.TryGetValue(structVarRef.VarName, out var varInfo)) {
          var newRef = new MaxonStructVarRefOp(structVarRef.VarName, varInfo.ConcreteIterTypeName);
          valueMap[structVarRef.Result.Id] = newRef.Result;
          newBlock.AddOp(newRef);
          i++;
          continue;
        }

        // Pattern: MaxonTryCallOp to *.next with a rewritten __for_iter variable
        if (ops[i] is MaxonTryCallOp tryCall
            && nextCalleeToInfo.ContainsKey(tryCall.Callee)
            && iterVarTypes.Count > 0) {
          // Only rewrite if the first arg maps to a rewritten iter var
          var remappedArgs = RemapArgs(tryCall.Args, valueMap);
          var firstArg = remappedArgs.Count > 0 ? remappedArgs[0] : null;

          bool shouldRewrite = false;
          string? concreteIterNextName = null;

          if (firstArg is MaxonStruct argStruct) {
            foreach (var (Info, IterType, ConcreteIterTypeName) in iterVarTypes.Values) {
              if (ConcreteIterTypeName == argStruct.TypeName) {
                concreteIterNextName = $"{ConcreteIterTypeName}.next";
                shouldRewrite = true;
                break;
              }
            }
          }

          if (shouldRewrite && concreteIterNextName != null) {
            var newTryCall = new MaxonTryCallOp(
              concreteIterNextName, remappedArgs,
              tryCall.ResultKind, tryCall.ResultStructTypeName);
            if (tryCall.Result != null && newTryCall.Result != null)
              valueMap[tryCall.Result.Id] = newTryCall.Result;
            valueMap[tryCall.ErrorFlag.Id] = newTryCall.ErrorFlag;
            newBlock.AddOp(newTryCall);
            i++;
            continue;
          }
        }

        // No pattern matched — copy op with value remapping
        CopyOpWithValueMap(ops[i], newBlock, valueMap, iterVarTypes);
        i++;
      }
    }

    return newFunc;
  }

  /// <summary>
  /// Remap values in an args list using the value map.
  /// </summary>
  private static List<MaxonValue> RemapArgs(List<MaxonValue> args, Dictionary<int, MaxonValue> valueMap) {
    var result = new List<MaxonValue>(args.Count);
    foreach (var arg in args) {
      result.Add(valueMap.TryGetValue(arg.Id, out var mapped) ? mapped : arg);
    }
    return result;
  }

  /// <summary>
  /// Copy an op to a new block, remapping any values that have been replaced.
  /// For ops that don't have remapped values, they are added directly (shallow copy is fine
  /// since we're building a new function and the original function is not modified).
  /// </summary>
  private static void CopyOpWithValueMap(MaxonOp op, MlirBlock<MaxonOp> block, Dictionary<int, MaxonValue> valueMap,
      Dictionary<string, (IterableInfo Info, MlirStructType IterType, string ConcreteIterTypeName)> iterVarTypes) {
    // Update MaxonScopeEndOp metadata for __for_iter_N variables that changed type
    if (op is MaxonScopeEndOp scopeEnd && iterVarTypes.Count > 0 && scopeEnd.VarMetadata != null) {
      bool needsUpdate = false;
      foreach (var v in scopeEnd.VarsToClean) {
        if (iterVarTypes.ContainsKey(v)) { needsUpdate = true; break; }
      }
      if (needsUpdate) {
        var newMetadata = new Dictionary<string, (OwnershipFlags Flags, string? StructTypeName)>();
        foreach (var (k, v) in scopeEnd.VarMetadata) {
          if (iterVarTypes.TryGetValue(k, out var iterInfo)) {
            newMetadata[k] = (v.Flags, iterInfo.ConcreteIterTypeName);
          } else {
            newMetadata[k] = v;
          }
        }
        var newScopeEnd = new MaxonScopeEndOp(scopeEnd.VarsToClean, scopeEnd.KeepVars) {
          VarMetadata = newMetadata
        };
        block.AddOp(newScopeEnd);
        return;
      }
    }

    if (valueMap.Count == 0) {
      // No remapping needed — just add the original op
      block.AddOp(op);
      return;
    }

    // For ops that reference remapped values, we need to create new ops
    // Most ops in for-in context that need remapping are handled in the main loop.
    // Here we handle the remaining ops that reference remapped error flags or results.
    switch (op) {
      case MaxonAssignOp assign when valueMap.TryGetValue(assign.Value.Id, out var newVal): {
        block.AddOp(new MaxonAssignOp(assign.VarName, newVal, assign.IsDeclaration, assign.IsMutable, assign.ValueKind));
        return;
      }
      case MaxonBinOp binOp: {
        var newLhs = valueMap.TryGetValue(binOp.Lhs.Id, out var ml) ? ml : binOp.Lhs;
        var newRhs = valueMap.TryGetValue(binOp.Rhs.Id, out var mr) ? mr : binOp.Rhs;
        if (newLhs != binOp.Lhs || newRhs != binOp.Rhs) {
          var newOp = new MaxonBinOp(binOp.Operator, newLhs, newRhs, binOp.OperandKind, binOp.OptimalType);
          valueMap[binOp.Result.Id] = newOp.Result;
          block.AddOp(newOp);
          return;
        }
        break;
      }
      case MaxonCondBrOp condBr when valueMap.TryGetValue(condBr.Condition.Id, out var newCond): {
        block.AddOp(new MaxonCondBrOp(newCond, condBr.ThenBlock, condBr.ElseBlock));
        return;
      }
      case MaxonFieldAccessOp fieldAccess when valueMap.TryGetValue(fieldAccess.StructValue.Id, out var newStruct): {
        var newOp = new MaxonFieldAccessOp(newStruct, fieldAccess.TypeName, fieldAccess.FieldName,
          fieldAccess.ResultKind, fieldAccess.ResultStructTypeName);
        valueMap[fieldAccess.Result.Id] = newOp.Result;
        block.AddOp(newOp);
        return;
      }
      case MaxonTryCallOp tryCall: {
        var remapped = RemapArgs(tryCall.Args, valueMap);
        if (!remapped.SequenceEqual(tryCall.Args)) {
          var newOp = new MaxonTryCallOp(tryCall.Callee, remapped, tryCall.ResultKind, tryCall.ResultStructTypeName);
          if (tryCall.Result != null && newOp.Result != null)
            valueMap[tryCall.Result.Id] = newOp.Result;
          valueMap[tryCall.ErrorFlag.Id] = newOp.ErrorFlag;
          block.AddOp(newOp);
          return;
        }
        break;
      }
      case MaxonCallOp call: {
        var remapped = RemapArgs(call.Args, valueMap);
        if (!remapped.SequenceEqual(call.Args)) {
          var newOp = new MaxonCallOp(call.Callee, remapped, call.ResultKind, call.ResultStructTypeName);
          if (call.Result != null && newOp.Result != null)
            valueMap[call.Result.Id] = newOp.Result;
          block.AddOp(newOp);
          return;
        }
        break;
      }
    }

    // No remapping needed for this op
    block.AddOp(op);
  }
}
