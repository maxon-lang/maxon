using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class MaxonToStandardConversion {
	public static MlirModule<StandardOp> Run(MlirModule<MaxonOp> module) {
		var result = new MlirModule<StandardOp>();
		result.RdataEntries.AddRange(module.RdataEntries);
		result.Globals.AddRange(module.Globals);

		foreach (var func in module.Functions) {
			var newFunc = new MlirFunction<StandardOp>(func.Name, func.ParamTypes, func.ReturnType);
			var valueMap = new Dictionary<MlirValue, MlirValue>();

			foreach (var block in func.Body.Blocks) {
				var newBlock = newFunc.Body.AddBlock(block.Name);
				foreach (var op in block.Operations) {
					switch (op) {
						case MaxonConstantOp constOp: {
								var newOp = new StandardArithConstantOp(constOp.Value, constOp.ResultType);
								newBlock.AddOp(newOp);
								valueMap[constOp.Result] = newOp.Result;
								break;
							}
						case MaxonFloatConstantOp floatOp: {
								var newOp = new StandardArithFloatConstantOp(floatOp.Value, floatOp.ResultType);
								newBlock.AddOp(newOp);
								valueMap[floatOp.Result] = newOp.Result;
								break;
							}
						case MaxonVarDeclOp varDecl: {
								var mappedValue = valueMap.GetValueOrDefault(varDecl.InitValue, varDecl.InitValue);
								var allocaOp = new StandardMemRefAllocaOp(varDecl.VarName, mappedValue.Type);
								newBlock.AddOp(allocaOp);
								var storeOp = new StandardMemRefStoreOp(mappedValue, varDecl.VarName);
								newBlock.AddOp(storeOp);
								break;
							}
						case MaxonVarLoadOp varLoad: {
								var loadOp = new StandardMemRefLoadOp(varLoad.VarName, varLoad.Result.Type);
								newBlock.AddOp(loadOp);
								valueMap[varLoad.Result] = loadOp.Result;
								break;
							}
						case MaxonAddIOp addOp: {
								var lhs = valueMap.GetValueOrDefault(addOp.Operands[0], addOp.Operands[0]);
								var rhs = valueMap.GetValueOrDefault(addOp.Operands[1], addOp.Operands[1]);
								var newAdd = new StandardArithAddIOp(lhs, rhs);
								newBlock.AddOp(newAdd);
								valueMap[addOp.Result] = newAdd.Result;
								break;
							}
						case MaxonSubIOp subOp: {
								var lhs = valueMap.GetValueOrDefault(subOp.Operands[0], subOp.Operands[0]);
								var rhs = valueMap.GetValueOrDefault(subOp.Operands[1], subOp.Operands[1]);
								var newSub = new StandardArithSubIOp(lhs, rhs);
								newBlock.AddOp(newSub);
								valueMap[subOp.Result] = newSub.Result;
								break;
							}
						case MaxonCmpFOp cmpOp: {
								var lhs = valueMap.GetValueOrDefault(cmpOp.Operands[0], cmpOp.Operands[0]);
								var rhs = valueMap.GetValueOrDefault(cmpOp.Operands[1], cmpOp.Operands[1]);
								var newCmp = new StandardArithCmpFOp(cmpOp.Predicate, lhs, rhs);
								newBlock.AddOp(newCmp);
								valueMap[cmpOp.Result] = newCmp.Result;
								break;
							}
						case MaxonCondBrOp condBr: {
								var cond = valueMap.GetValueOrDefault(condBr.Condition, condBr.Condition);
								newBlock.AddOp(new StandardCfCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
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
											var funcCall = new StandardFuncCallOp(c.CallOp.Callee, newArgs, resultType);
											newBlock.AddOp(funcCall);
											newRetVal = funcCall.Result;
											break;
										}
								}
								newBlock.AddOp(new StandardFuncReturnOp(newRetVal));
								break;
							}
						case MaxonCallOp:
							break;
						default:
							throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
					}
				}
			}
			result.AddFunction(newFunc);
		}
		return result;
	}
}
