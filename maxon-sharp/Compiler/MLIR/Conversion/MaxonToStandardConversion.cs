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
			var valueMap = new Dictionary<MaxonValue, StdValue>();
			var varTypes = new Dictionary<string, string>();

			foreach (var block in func.Body.Blocks) {
				var newBlock = newFunc.Body.AddBlock(block.Name);
				foreach (var op in block.Operations) {
					switch (op) {
						case MaxonConstantOp constOp: {
								switch (constOp.ValueKind) {
									case MaxonValueKind.Integer: {
											var newOp = new StdConstI64Op(constOp.IntValue);
											newBlock.AddOp(newOp);
											valueMap[constOp.Result] = newOp.Result;
											break;
										}
									case MaxonValueKind.Float: {
											var newOp = new StdConstF64Op(constOp.FloatValue);
											newBlock.AddOp(newOp);
											valueMap[constOp.Result] = newOp.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported MaxonConstantOp kind: {constOp.ValueKind}");
								}
								break;
							}
						case MaxonVarDeclOp varDecl: {
								var mappedValue = valueMap[varDecl.InitValue];
								switch (mappedValue) {
									case StdI64 i64: {
											newBlock.AddOp(new StdStoreI64Op(i64, varDecl.VarName));
											varTypes[varDecl.VarName] = "i64";
											break;
										}
									case StdF64 f64: {
											newBlock.AddOp(new StdStoreF64Op(f64, varDecl.VarName));
											varTypes[varDecl.VarName] = "f64";
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported value type for var decl: {mappedValue.GetType().Name}");
								}
								break;
							}
						case MaxonVarLoadOp varLoad: {
								var varTypeName = varTypes[varLoad.VarName];
								switch (varTypeName) {
									case "i64": {
											var loadOp = new StdLoadI64Op(varLoad.VarName);
											newBlock.AddOp(loadOp);
											valueMap[varLoad.Result] = loadOp.Result;
											break;
										}
									case "f64": {
											var loadOp = new StdLoadF64Op(varLoad.VarName);
											newBlock.AddOp(loadOp);
											valueMap[varLoad.Result] = loadOp.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported var type: {varTypeName}");
								}
								break;
							}
						case MaxonAddOp addOp: {
								switch (addOp.ValueKind) {
									case MaxonValueKind.Integer: {
											var lhs = (StdI64)valueMap[addOp.Lhs];
											var rhs = (StdI64)valueMap[addOp.Rhs];
											var newAdd = new StdAddI64Op(lhs, rhs);
											newBlock.AddOp(newAdd);
											valueMap[addOp.Result] = newAdd.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported MaxonAddOp kind: {addOp.ValueKind}");
								}
								break;
							}
						case MaxonSubOp subOp: {
								switch (subOp.ValueKind) {
									case MaxonValueKind.Integer: {
											var lhs = (StdI64)valueMap[subOp.Lhs];
											var rhs = (StdI64)valueMap[subOp.Rhs];
											var newSub = new StdSubI64Op(lhs, rhs);
											newBlock.AddOp(newSub);
											valueMap[subOp.Result] = newSub.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported MaxonSubOp kind: {subOp.ValueKind}");
								}
								break;
							}
						case MaxonCmpOp cmpOp: {
								switch (cmpOp.ValueKind) {
									case MaxonValueKind.Float: {
											var lhs = (StdF64)valueMap[cmpOp.Lhs];
											var rhs = (StdF64)valueMap[cmpOp.Rhs];
											var newCmp = new StdCmpF64Op(cmpOp.Predicate, lhs, rhs);
											newBlock.AddOp(newCmp);
											valueMap[cmpOp.Result] = newCmp.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported MaxonCmpOp kind: {cmpOp.ValueKind}");
								}
								break;
							}
						case MaxonCondBrOp condBr: {
								var cond = (StdBool)valueMap[condBr.Condition];
								newBlock.AddOp(new StdCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
								break;
							}
						case MaxonReturnOp retOp: {
								StdValue? newRetVal = null;
								switch (retOp.ReturnExpr) {
									case MaxonExpr.Value v:
										newRetVal = valueMap[v.MaxonValue];
										break;
									case MaxonExpr.VarLoad vl:
										newRetVal = valueMap[vl.LoadOp.Result];
										break;
									case MaxonExpr.Call c: {
											var newArgs = c.CallOp.Args.Select(a => valueMap[a]).ToList();
											var callee = module.Functions.FirstOrDefault(f => f.Name == c.CallOp.Callee);
											StdValue? callResult = null;
											if (callee?.ReturnType != null && callee.ReturnType != MlirType.Void) {
												callResult = callee.ReturnType == MlirType.F64
													? new StdF64(MlirContext.Current.NextId())
													: new StdI64(MlirContext.Current.NextId());
											}
											var funcCall = new StdCallOp(c.CallOp.Callee, newArgs, callResult);
											newBlock.AddOp(funcCall);
											newRetVal = funcCall.Result;
											break;
										}
								}
								newBlock.AddOp(new StdReturnOp(newRetVal));
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
