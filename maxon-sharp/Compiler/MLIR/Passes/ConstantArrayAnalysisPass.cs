using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Identifies array literals with all-constant elements and tags them for .rdata placement.
/// This is pure analysis on Maxon ops that runs before MaxonToStandardConversion.
/// Results are stored in module.ConstantArrayLiterals for consumption by the conversion.
/// </summary>
public static class ConstantArrayAnalysisPass {
	public static void Run(MlirModule<MaxonOp> module) {
		foreach (var func in module.Functions) {
			AnalyzeFunction(func, module);
		}
	}

	private static void AnalyzeFunction(MlirFunction<MaxonOp> func, MlirModule<MaxonOp> module) {
		foreach (var block in func.Body.Blocks) {
			// Collect MaxonLiteralOp results and MaxonStructLiteralOp results
			var literalValues = new Dictionary<int, long>();
			var structLiterals = new Dictionary<int, MaxonStructLiteralOp>();
			foreach (var op in block.Operations) {
				if (op is MaxonLiteralOp lit && lit.ValueKind == MaxonValueKind.Integer)
					literalValues[lit.Result.Id] = lit.IntValue;
				if (op is MaxonStructLiteralOp slit)
					structLiterals[slit.Result.Id] = slit;
			}

			// Find array/vector assigns with all-constant elements (both let and var)
			foreach (var op in block.Operations) {
				if (op is not MaxonAssignOp { ValueKind: MaxonValueKind.Struct } assignOp) continue;
				if (!structLiterals.TryGetValue(assignOp.Value.Id, out var arrayStructLit)) continue;
				// Accept any struct with ArrayLiteralTag (Array, Vector aliases, etc.)
				if (arrayStructLit.ArrayLiteralTag == null) continue;

				var tag = arrayStructLit.ArrayLiteralTag;
				int count = arrayStructLit.ArrayLiteralCount;
				// Collect element values from the element assign ops
				var elementValues = new long[count];
				bool allConstant = true;
				foreach (var elemOp in block.Operations) {
					if (elemOp is not MaxonAssignOp elemAssign) continue;
					if (!elemAssign.VarName.StartsWith($"{tag}.")) continue;
					var indexStr = elemAssign.VarName[($"{tag}.".Length)..];
					if (!int.TryParse(indexStr, out var idx)) continue;
					if (!literalValues.TryGetValue(elemAssign.Value.Id, out var val)) {
						allConstant = false;
						break;
					}
					elementValues[idx] = val;
				}
				if (allConstant) {
					// Include function name in rdata label to avoid conflicts
					var rdataLabel = $"__const_array_{func.Name}_{assignOp.VarName}";
					module.ConstantArrayLiterals[arrayStructLit.Result.Id] =
					  new ConstantArrayLiteralInfo(rdataLabel, elementValues, assignOp.IsMutable);
				}
			}
		}
	}
}
