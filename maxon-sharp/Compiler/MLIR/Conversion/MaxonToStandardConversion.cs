using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class MaxonToStandardConversion {
	public static void Run(MlirModule module) {
		foreach (var func in module.Functions) {
			var valueMap = new Dictionary<MlirValue, MlirValue>();
			foreach (var block in func.Body.Blocks) {
				var newOps = new List<MlirOperation>();
				foreach (var op in block.Operations) {
					switch (op) {
						case MaxonConstantOp constOp: {
							var newOp = new ArithConstantOp(constOp.Value, constOp.ResultType);
							newOps.Add(newOp);
							valueMap[constOp.Result] = newOp.Result;
							break;
						}
						case MaxonFloatConstantOp floatOp: {
							var newOp = new ArithFloatConstantOp(floatOp.Value, floatOp.ResultType);
							newOps.Add(newOp);
							valueMap[floatOp.Result] = newOp.Result;
							break;
						}
						case MaxonVarDeclOp varDecl: {
							var mappedValue = valueMap.GetValueOrDefault(varDecl.InitValue, varDecl.InitValue);
							newOps.Add(new MemRefAllocaOp(varDecl.VarName, mappedValue.Type));
							newOps.Add(new MemRefStoreOp(mappedValue, varDecl.VarName));
							break;
						}
						case MaxonVarLoadOp varLoad: {
							var loadOp = new MemRefLoadOp(varLoad.VarName, varLoad.Result.Type);
							newOps.Add(loadOp);
							valueMap[varLoad.Result] = loadOp.Result;
							break;
						}
						case MaxonCmpFOp cmpOp: {
							var lhs = valueMap.GetValueOrDefault(cmpOp.Operands[0], cmpOp.Operands[0]);
							var rhs = valueMap.GetValueOrDefault(cmpOp.Operands[1], cmpOp.Operands[1]);
							var newCmp = new ArithCmpFOp(cmpOp.Predicate, lhs, rhs);
							newOps.Add(newCmp);
							valueMap[cmpOp.Result] = newCmp.Result;
							break;
						}
						case MaxonCondBrOp condBr: {
							var cond = valueMap.GetValueOrDefault(condBr.Condition, condBr.Condition);
							newOps.Add(new CfCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
							break;
						}
						case MaxonReturnOp retOp: {
							MlirValue? newRetVal = null;
							switch (retOp.ReturnExpr) {
								case MaxonExpr.Value v:
									newRetVal = valueMap.GetValueOrDefault(v.MlirValue, v.MlirValue);
									break;
								case MaxonExpr.VarLoad vl:
									newRetVal = valueMap.GetValueOrDefault(vl.LoadOp.Result, vl.LoadOp.Result);
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
