using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Detects value-containment cycles in type definitions. A cycle exists when a type
/// transitively contains itself through inline (non-pointer) fields, which would require
/// infinite storage. Heap-allocated types (structs, associated-value enums) are stored as
/// pointers, so self-references through them are valid (e.g., linked lists, trees).
///
/// Currently, all struct and associated-value enum types in Maxon are heap-allocated,
/// so direct field references never form value-containment cycles. This pass serves as
/// a safety net for future value-type semantics and catches cycles through generic
/// container element types where elements might be stored inline.
/// </summary>
public static class TypeCycleCheckPass {
  public static void Run(MlirModule<MaxonOp> module) {
    // Build adjacency list: typeName -> set of type names reachable through value containment
    var graph = BuildTypeGraph(module);

    // BFS from each type to detect if it can reach itself
    foreach (var typeName in graph.Keys) {
      var cycle = FindShortestCycle(graph, typeName);
      if (cycle != null) {
        var path = string.Join(" -> ", cycle);
        var typeDef = module.TypeDefs.GetValueOrDefault(typeName);
        throw new CompileError(
          ErrorCode.MlirTypeCycle,
          $"type '{typeName}' contains a reference cycle (via {path}); recursive type references are not allowed",
          typeDef?.SourceLine,
          typeDef?.SourceColumn) {
          FilePath = typeDef?.SourceFilePath
        };
      }
    }
  }

  /// <summary>
  /// Builds a type reachability graph based on value containment. Edges represent
  /// "type A inline-contains type B" relationships. Heap-allocated types (structs,
  /// associated-value enums) are stored as pointers and do NOT create containment edges,
  /// since pointer indirection breaks the infinite-size cycle.
  ///
  /// Edges are added for:
  /// - Struct fields whose type is NOT heap-allocated (primitives, simple enums, ranged types)
  /// - Union case associated values whose type is NOT heap-allocated
  /// - Generic container element types that might be stored inline
  /// </summary>
  private static Dictionary<string, HashSet<string>> BuildTypeGraph(MlirModule<MaxonOp> module) {
    var graph = new Dictionary<string, HashSet<string>>();

    foreach (var (typeName, typeDef) in module.TypeDefs) {
      // Only struct and union types can participate in containment cycles
      if (typeDef is not (MlirStructType or MlirUnionType)) continue;
      // Skip types with unresolved type parameters (generic templates)
      if (typeDef is MlirStructType st && st.Fields.Any(f => f.Type is MlirTypeParameterType)) continue;
      if (typeDef is MlirUnionType ut && ut.Cases.Any(c =>
            c.AssociatedValues != null && c.AssociatedValues.Any(av => av.Type is MlirTypeParameterType))) continue;

      var edges = new HashSet<string>();

      if (typeDef is MlirStructType structType) {
        foreach (var field in structType.Fields) {
          // Only add edge if the field type is NOT heap-allocated (stored inline, not as pointer)
          AddValueContainmentEdge(edges, field.Type, module);
        }
      }

      if (typeDef is MlirUnionType unionType) {
        foreach (var enumCase in unionType.Cases) {
          if (enumCase.AssociatedValues == null) continue;
          foreach (var (_, avType) in enumCase.AssociatedValues) {
            AddValueContainmentEdge(edges, avType, module);
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
  /// Adds an edge only if the target type would be stored inline (by value), not as a
  /// heap pointer. Heap-allocated types (structs, associated-value enums) are stored as
  /// pointers and don't create value-containment edges.
  /// </summary>
  private static void AddValueContainmentEdge(HashSet<string> edges, MlirType type, MlirModule<MaxonOp> module) {
    // Heap-allocated types are stored as pointers -- no value containment
    if (type.IsHeapAllocated) return;

    string? targetName = type switch {
      MlirStructType st => st.Name,
      MlirUnionType ut => ut.Name,
      _ => null
    };
    if (targetName != null && module.TypeDefs.ContainsKey(targetName)) {
      edges.Add(targetName);
    }
  }

  /// <summary>
  /// Uses BFS to find the shortest cycle from startType back to itself.
  /// Returns the cycle path as a list of type names, or null if no cycle exists.
  /// </summary>
  private static List<string>? FindShortestCycle(Dictionary<string, HashSet<string>> graph, string startType) {
    if (!graph.TryGetValue(startType, out var startEdges)) return null;

    // BFS from each direct neighbor back to startType
    var queue = new Queue<(string Current, List<string> Path)>();
    foreach (var neighbor in startEdges) {
      if (neighbor == startType) {
        // Direct self-reference
        return [startType, startType];
      }
      queue.Enqueue((neighbor, [startType, neighbor]));
    }

    var visited = new HashSet<string>();
    while (queue.Count > 0) {
      var (current, path) = queue.Dequeue();
      if (!visited.Add(current)) continue;

      if (!graph.TryGetValue(current, out var edges)) continue;
      foreach (var next in edges) {
        if (next == startType) {
          // Found cycle back to start
          path.Add(startType);
          return path;
        }
        if (!visited.Contains(next)) {
          queue.Enqueue((next, [.. path, next]));
        }
      }
    }

    return null;
  }
}
