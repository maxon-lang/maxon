using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Detects linear comparison chains (from enum/integer match lowering) and replaces
/// them with StdSwitchOp for jump-table code generation. Only fires when the chain
/// has 4+ cases with dense, zero-based ordinals.
/// </summary>
public static class JumpTableFormationPass {
  private const int Threshold = 4;

  public static void Run(IrModule<StandardOp> module) {
    foreach (var func in module.Functions) {
      TransformFunction(func);
    }
  }

  private static void TransformFunction(IrFunction<StandardOp> func) {
    var blocks = func.Body.Blocks;
    var blockMap = new Dictionary<string, IrBlock<StandardOp>>();
    foreach (var block in blocks) {
      blockMap[block.Name] = block;
    }

    // Collect all branch targets so we can verify cmp blocks are only targeted by the chain
    var branchTargets = new Dictionary<string, List<string>>(); // target -> list of source block names
    foreach (var block in blocks) {
      foreach (var target in GetBranchTargets(block)) {
        if (!branchTargets.TryGetValue(target, out var sources)) {
          sources = [];
          branchTargets[target] = sources;
        }
        sources.Add(block.Name);
      }
    }

    // Find entry blocks that branch to a comparison chain
    var blocksToRemove = new HashSet<string>();

    for (int bi = 0; bi < blocks.Count; bi++) {
      var entryBlock = blocks[bi];
      var ops = entryBlock.Operations;
      if (ops.Count == 0) continue;
      var lastOp = ops[^1];

      if (lastOp is not StdBrOp br) continue;
      if (!br.Target.Contains(".cmp")) continue;
      if (!blockMap.TryGetValue(br.Target, out var firstCmpBlock)) continue;

      // Try to detect the comparison chain
      var chain = DetectComparisonChain(firstCmpBlock, blockMap);
      if (chain == null) continue;
      if (chain.Cases.Count < Threshold) continue;

      // Verify ordinals are dense 0..N-1
      var ordinals = chain.Cases.Select(c => c.Ordinal).Order().ToList();
      if (ordinals[0] != 0 || ordinals[^1] != ordinals.Count - 1) continue;
      // Check for duplicates
      if (ordinals.Distinct().Count() != ordinals.Count) continue;

      // Verify cmp blocks are only reachable from the chain (not from any other block)
      bool cmpBlocksClean = true;
      foreach (var cmpBlockName in chain.CmpBlockNames) {
        if (branchTargets.TryGetValue(cmpBlockName, out var sources)) {
          // A cmp block should only be targeted from either the entry block or the previous cmp block in the chain
          foreach (var src in sources) {
            if (src != entryBlock.Name && !chain.CmpBlockNames.Contains(src)) {
              cmpBlocksClean = false;
              break;
            }
          }
        }
        if (!cmpBlocksClean) break;
      }
      if (!cmpBlocksClean) continue;

      // Build the case targets array indexed by ordinal
      var maxOrdinal = chain.Cases.Max(c => c.Ordinal);
      var caseTargets = new string[maxOrdinal + 1];
      for (int i = 0; i <= maxOrdinal; i++) {
        caseTargets[i] = chain.DefaultTarget;
      }
      foreach (var (ordinal, caseBody) in chain.Cases) {
        caseTargets[ordinal] = caseBody;
      }

      // Transform: replace entry block's StdBrOp with StdLoadI64Op + StdSwitchOp
      ops.RemoveAt(ops.Count - 1); // remove StdBrOp
      var loadOp = new StdLoadI64Op(chain.ScrutVar);
      ops.Add(loadOp);
      ops.Add(new StdSwitchOp(loadOp.Result, caseTargets, chain.DefaultTarget));

      // Mark cmp blocks for removal
      foreach (var name in chain.CmpBlockNames) {
        blocksToRemove.Add(name);
      }
    }

    // Remove dead cmp blocks
    if (blocksToRemove.Count > 0) {
      blocks.RemoveAll(b => blocksToRemove.Contains(b.Name));
    }
  }

  private record ComparisonChain(
    string ScrutVar,
    List<(long Ordinal, string CaseBodyLabel)> Cases,
    string DefaultTarget,
    HashSet<string> CmpBlockNames
  );

  private static ComparisonChain? DetectComparisonChain(
      IrBlock<StandardOp> firstCmpBlock,
      Dictionary<string, IrBlock<StandardOp>> blockMap) {
    string? scrutVar = null;
    var cases = new List<(long Ordinal, string CaseBodyLabel)>();
    var cmpBlockNames = new HashSet<string>();

    var currentBlock = firstCmpBlock;
    while (currentBlock != null) {
      var ops = currentBlock.Operations;

      if (ops.Count < 4) return null;

      // Optimization passes may insert or reorder ops, so match by SSA data flow
      // rather than relying on fixed positions within the block.
      if (ops[^1] is not StdCondBrOp condBr) return null;

      // Find the cmp that produces the condition
      StdCmpI64Op? cmpOp = null;
      for (int i = ops.Count - 2; i >= 0; i--) {
        if (ops[i] is StdCmpI64Op c && c.Result.Id == condBr.Condition.Id) {
          cmpOp = c;
          break;
        }
      }
      if (cmpOp == null) return null;
      if (cmpOp.Predicate != "eq") return null;

      // Find the constant that is the RHS of the comparison
      StdConstI64Op? constOp = null;
      for (int i = ops.Count - 2; i >= 0; i--) {
        if (ops[i] is StdConstI64Op c && c.Result.Id == cmpOp.Rhs.Id) {
          constOp = c;
          break;
        }
      }
      if (constOp == null) return null;

      // Find the load that is the LHS of the comparison
      StdLoadI64Op? loadOp = null;
      for (int i = ops.Count - 2; i >= 0; i--) {
        if (ops[i] is StdLoadI64Op l && l.Result.Id == cmpOp.Lhs.Id) {
          loadOp = l;
          break;
        }
      }
      if (loadOp == null) return null;

      // Check that all cmp blocks use the same scrutinee variable
      if (scrutVar == null) {
        scrutVar = loadOp.VarName;
      } else if (loadOp.VarName != scrutVar) {
        return null;
      }

      cmpBlockNames.Add(currentBlock.Name);
      cases.Add((constOp.Value, condBr.ThenBlock));

      // Follow the else branch to the next cmp block or to the default/merge
      var elseTarget = condBr.ElseBlock;
      if (blockMap.TryGetValue(elseTarget, out var nextBlock) && elseTarget.Contains(".cmp")) {
        currentBlock = nextBlock;
      } else {
        // This is the end of the chain — elseTarget is the default/merge block
        return new ComparisonChain(scrutVar, cases, elseTarget, cmpBlockNames);
      }
    }

    return null;
  }

  private static IEnumerable<string> GetBranchTargets(IrBlock<StandardOp> block) {
    foreach (var op in block.Operations) {
      switch (op) {
        case StdBrOp br:
          yield return br.Target;
          break;
        case StdCondBrOp condBr:
          yield return condBr.ThenBlock;
          yield return condBr.ElseBlock;
          break;
        case StdSwitchOp switchOp:
          yield return switchOp.DefaultTarget;
          foreach (var target in switchOp.CaseTargets)
            yield return target;
          break;
      }
    }
  }
}
