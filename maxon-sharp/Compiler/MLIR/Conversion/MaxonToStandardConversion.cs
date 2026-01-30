using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class MaxonToStandardConversion {
	public static void Run(MlirModule module) {
		foreach (var func in module.Functions) {
			foreach (var block in func.Body.Blocks) {
				var newOps = new List<MlirOperation>();
				var valueMap = new Dictionary<MlirValue, MlirValue>();
				foreach (var op in block.Operations) {
					switch (op) {
						case MaxonConstantOp constOp: {
							var newOp = new ArithConstantOp(constOp.Value, constOp.ResultType);
							newOps.Add(newOp);
							valueMap[constOp.Result] = newOp.Result;
							break;
						}
						case MaxonReturnOp retOp: {
							MlirValue? newRetVal = null;
							switch (retOp.ReturnExpr) {
								case MaxonExpr.Value v:
									newRetVal = valueMap.GetValueOrDefault(v.MlirValue, v.MlirValue);
									break;
								case MaxonExpr.Call c: {
									var newArgs = c.CallOp.Operands.Select(v => valueMap.GetValueOrDefault(v, v)).ToList();
									var callee = module.Functions.FirstOrDefault(f => f.Name == c.CallOp.Callee);
									var resultType = callee?.ReturnType;
									var funcCall = new FuncCallOp(c.CallOp.Callee, newArgs, resultType);
									newOps.Add(funcCall);
									newRetVal = funcCall.Result;
									break;
								}
							}
							newOps.Add(new FuncReturnOp(newRetVal));
							break;
						}
						case MaxonCallOp:
							// Handled via MaxonReturnOp.ReturnExpr
							break;
						default:
							newOps.Add(op);
							break;
					}
				}
				block.Operations.Clear();
				block.Operations.AddRange(newOps);
			}
		}
	}
}
