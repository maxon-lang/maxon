using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Detects recursive type reference cycles in type definitions. Maxon's ownership model
/// requires acyclic type graphs to guarantee deterministic destruction — a type cannot
/// transitively contain itself, even through heap-allocated (pointer) indirection.
/// </summary>
public static class TypeCycleCheckPass {
  public static void Run(MlirModule<MaxonOp> module) {
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
  /// Builds a type reachability graph. Edges represent "type A references type B"
  /// relationships through struct fields or enum case associated values.
  /// Each edge carries a label like "fieldName: TargetType" for error messages.
  /// </summary>
  private static Dictionary<string, List<(string Label, string Target)>> BuildTypeGraph(MlirModule<MaxonOp> module) {
    var graph = new Dictionary<string, List<(string Label, string Target)>>();

    foreach (var (typeName, typeDef) in module.TypeDefs) {
      // Only struct and enum types can participate in type reference cycles
      if (typeDef is not (MlirStructType or MlirEnumType)) continue;
      // Skip types with unresolved type parameters (generic templates)
      if (typeDef is MlirStructType st && st.Fields.Any(f => f.Type is MlirTypeParameterType)) continue;
      if (typeDef is MlirEnumType ut && ut.Cases.Any(c =>
            c.AssociatedValues != null && c.AssociatedValues.Any(av => av.Type is MlirTypeParameterType))) continue;

      var edges = new List<(string Label, string Target)>();

      if (typeDef is MlirStructType structType) {
        foreach (var field in structType.Fields) {
          AddTypeReferenceEdge(edges, field.Name, field.Type, module);
        }
        // Monomorphized container types reference their element type through TypeParams
        // (e.g. FolderArray has TypeParams["Element"] = Folder), creating ownership edges
        foreach (var (_, paramType) in structType.TypeParams) {
          var paramTargetName = paramType switch {
            MlirStructType s => s.Name,
            MlirEnumType u => u.Name,
            _ => (string?)null
          };
          if (paramTargetName != null && module.TypeDefs.ContainsKey(paramTargetName)) {
            edges.Add((paramTargetName, paramTargetName));
          }
        }
      }

      if (typeDef is MlirEnumType enumType) {
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
  private static void AddTypeReferenceEdge(List<(string Label, string Target)> edges, string fieldName, MlirType type, MlirModule<MaxonOp> module) {
    string? targetName = type switch {
      MlirStructType s => s.Name,
      MlirEnumType u => u.Name,
      _ => null
    };
    if (targetName != null && module.TypeDefs.ContainsKey(targetName)) {
      var typeDisplay = FormatTypeDisplay(type);
      edges.Add(($"{fieldName}: {typeDisplay}", targetName));
    }
  }

  private static string FormatTypeDisplay(MlirType type) {
    return type switch {
      MlirStructType s => s.Name,
      MlirEnumType u => u.Name,
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
