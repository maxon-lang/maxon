using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Detects recursive type reference cycles in type definitions. Maxon's ownership model
/// requires acyclic type graphs to guarantee deterministic destruction — a type cannot
/// transitively contain itself, even through heap-allocated (pointer) indirection.
/// </summary>
public static class TypeCycleCheckPass {
  public static void Run(IrModule<MaxonOp> module) {
    // Build adjacency list: typeName -> list of (edgeLabel, targetTypeName)
    var graph = BuildTypeGraph(module);

    // BFS from each user-defined type to detect if it can reach itself
    foreach (var typeName in graph.Keys) {
      // Only report cycles for user-defined types (skip stdlib/internal types)
      var typeDef = module.TypeDefs.GetValueOrDefault(typeName);
      if (typeDef?.SourceFilePath == null) continue;
      var cycle = FindShortestCycle(graph, typeName);
      if (cycle != null) {
        var path = string.Join(" \u2192 ", cycle);
        throw new CompileError(
          ErrorCode.IrTypeCycle,
          $"type '{typeName}' contains a reference cycle (via {path}); recursive type references are not allowed",
          typeDef?.SourceLine,
          typeDef?.SourceColumn) {
          FilePath = typeDef?.SourceFilePath
        };
      }
    }
  }

  /// <summary>
  /// Builds a type reachability graph. Edges represent "type A references type B"
  /// relationships through struct fields or enum case associated values.
  /// Each edge carries a label like "fieldName: TargetType" for error messages.
  /// </summary>
  private static Dictionary<string, List<(string Label, string Target)>> BuildTypeGraph(IrModule<MaxonOp> module) {
    var graph = new Dictionary<string, List<(string Label, string Target)>>();

    foreach (var (typeName, typeDef) in module.TypeDefs) {
      // Only struct and enum types can participate in type reference cycles
      if (typeDef is not (IrStructType or IrEnumType)) continue;
      // Skip types with unresolved type parameters (generic templates)
      if (typeDef is IrStructType st && st.Fields.Any(f => f.Type is IrTypeParameterType)) continue;
      if (typeDef is IrEnumType ut && ut.Cases.Any(c =>
            c.AssociatedValues != null && c.AssociatedValues.Any(av => av.Type is IrTypeParameterType))) continue;

      var edges = new List<(string Label, string Target)>();

      if (typeDef is IrStructType structType) {
        foreach (var field in structType.Fields) {
          AddTypeReferenceEdge(edges, field.Name, field.Type, module);
        }
        // A monomorphized container owns its elements in its BUFFER, not in any declared field, so
        // the element binding in TypeParams is a real ownership edge (`FolderArray` has
        // TypeParams["Element"] = Folder, and that is what makes `Folder -> children: FolderArray ->
        // Folder` a cycle).
        //
        // ONLY the bindings the type actually STORES. TypeParams also carries the bindings of every
        // interface the type conforms to, and those are not storage: `String implements Iterable with
        // (Character, StringIterator)` leaves String with TypeParams [Element=Character,
        // Iter=StringIterator] while a String contains neither and its destructor touches neither.
        // Counting those made `String -> StringIterator -> s: String` a "cycle" the moment
        // StringIterator came to hold its String (Stage 4b-1b), for an object graph that is plainly
        // acyclic — a String references no iterator, ever.
        //
        // What separates them is whether a FIELD parameterizes on the binding. `FolderArray` stores
        // `managed as __ManagedMemory with Folder`, so Folder is genuinely inside it; `String` stores
        // `managed as __ManagedMemory with Byte`, and neither Character nor StringIterator appears in
        // any field of it at any depth. That question — "do I actually hold one?" — IS the ownership
        // question the pass is asking, so it is the right one to answer here.
        var storedTypeNames = StoredTypeParamNames(structType);
        foreach (var (_, paramType) in structType.TypeParams) {
          var paramTargetName = TypeReferenceName(paramType);
          if (paramTargetName != null
              && storedTypeNames.Contains(paramTargetName)
              && module.TypeDefs.ContainsKey(paramTargetName)) {
            edges.Add((paramTargetName, paramTargetName));
          }
        }
      }

      if (typeDef is IrEnumType enumType) {
        foreach (var enumCase in enumType.Cases) {
          if (enumCase.AssociatedValues == null) continue;
          foreach (var (avName, avType) in enumCase.AssociatedValues) {
            var fieldPath = $"{enumCase.Name}.{avName}";
            AddTypeReferenceEdge(edges, fieldPath, avType, module);
          }
        }
      }

      if (edges.Count > 0) {
        graph[typeName] = edges;
      }
    }

    return graph;
  }

  /// <summary>
  /// Adds an edge for any user-defined type reference. Maxon does not allow recursive
  /// type references — even though structs are heap-allocated, the ownership model
  /// requires acyclic type graphs to guarantee deterministic destruction.
  /// </summary>
  private static void AddTypeReferenceEdge(List<(string Label, string Target)> edges, string fieldName, IrType type, IrModule<MaxonOp> module) {
    var targetName = TypeReferenceName(type);
    if (targetName != null && module.TypeDefs.ContainsKey(targetName)) {
      var typeDisplay = FormatTypeDisplay(type);
      edges.Add(($"{fieldName}: {typeDisplay}", targetName));
    }
  }

  /// The named type a reference resolves to, or null for primitives and unresolved type parameters.
  private static string? TypeReferenceName(IrType type) => type switch {
    IrStructType s => s.Name,
    IrEnumType u => u.Name,
    _ => null
  };

  /// Every type this struct's own FIELDS parameterize on — i.e. what it actually holds. A field
  /// declared `__ManagedMemory with Folder` puts Folder in here; a conformance binding never does,
  /// because a conformance is not a field.
  private static HashSet<string> StoredTypeParamNames(IrStructType structType) {
    var stored = new HashSet<string>();
    foreach (var field in structType.Fields) {
      if (field.Type is not IrStructType fieldStruct) continue;
      foreach (var (_, paramType) in fieldStruct.TypeParams) {
        var name = TypeReferenceName(paramType);
        if (name != null) stored.Add(name);
      }
    }
    return stored;
  }

  private static string FormatTypeDisplay(IrType type) {
    return type switch {
      IrStructType s => s.Name,
      IrEnumType u => u.Name,
      _ => type.ToString() ?? "?"
    };
  }

  /// <summary>
  /// Uses BFS to find the shortest cycle from startType back to itself.
  /// Returns the cycle path segments (e.g. ["Node", "next: Node"]) or null if no cycle.
  /// </summary>
  private static List<string>? FindShortestCycle(Dictionary<string, List<(string Label, string Target)>> graph, string startType) {
    if (!graph.TryGetValue(startType, out var startEdges)) return null;

    // BFS from each direct neighbor back to startType
    var queue = new Queue<(string Current, List<string> Path)>();
    foreach (var (label, target) in startEdges) {
      if (target == startType) {
        // Direct self-reference
        return [startType, label];
      }
      queue.Enqueue((target, [startType, label]));
    }

    var visited = new HashSet<string>();
    while (queue.Count > 0) {
      var (current, path) = queue.Dequeue();
      if (!visited.Add(current)) continue;

      if (!graph.TryGetValue(current, out var edges)) continue;
      foreach (var (label, target) in edges) {
        if (target == startType) {
          // Found cycle back to start
          path.Add(label);
          return path;
        }
        if (!visited.Contains(target)) {
          queue.Enqueue((target, [.. path, label]));
        }
      }
    }

    return null;
  }
}
