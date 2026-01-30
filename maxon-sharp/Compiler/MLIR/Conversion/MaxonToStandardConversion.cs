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
						case MaxonLiteralOp litOp: {
								switch (litOp.ValueKind) {
									case MaxonValueKind.Integer: {
											var newOp = new StdConstI64Op(litOp.IntValue);
											newBlock.AddOp(newOp);
											valueMap[litOp.Result] = newOp.Result;
											break;
										}
									case MaxonValueKind.Float: {
											var newOp = new StdConstF64Op(litOp.FloatValue);
											newBlock.AddOp(newOp);
											valueMap[litOp.Result] = newOp.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported MaxonLiteralOp kind: {litOp.ValueKind}");
								}
								break;
							}
						case MaxonAssignOp assignOp: {
								var mappedValue = valueMap[assignOp.Value];
								switch (mappedValue) {
									case StdI64 i64: {
											newBlock.AddOp(new StdStoreI64Op(i64, assignOp.VarName));
											varTypes[assignOp.VarName] = "i64";
											break;
										}
									case StdF64 f64: {
											newBlock.AddOp(new StdStoreF64Op(f64, assignOp.VarName));
											varTypes[assignOp.VarName] = "f64";
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported value type for assign: {mappedValue.GetType().Name}");
								}
								break;
							}
						case MaxonVarRefOp varRef: {
								var varTypeName = varTypes[varRef.VarName];
								switch (varTypeName) {
									case "i64": {
											var loadOp = new StdLoadI64Op(varRef.VarName);
											newBlock.AddOp(loadOp);
											valueMap[varRef.Result] = loadOp.Result;
											break;
										}
									case "f64": {
											var loadOp = new StdLoadF64Op(varRef.VarName);
											newBlock.AddOp(loadOp);
											valueMap[varRef.Result] = loadOp.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported var type: {varTypeName}");
								}
								break;
							}
						case MaxonBinOp binOp: {
								ConvertBinOp(binOp, newBlock, valueMap);
								break;
							}
						case MaxonCondBrOp condBr: {
								var cond = (StdBool)valueMap[condBr.Condition];
								newBlock.AddOp(new StdCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
								break;
							}
						case MaxonCallOp callOp: {
								var newArgs = callOp.Args.Select(a => valueMap[a]).ToList();
								StdValue? callResult = null;
								if (callOp.ResultKind != null) {
									callResult = callOp.ResultKind switch {
										MaxonValueKind.Float => new StdF64(MlirContext.Current.NextId()),
										MaxonValueKind.Integer => new StdI64(MlirContext.Current.NextId()),
										MaxonValueKind.Bool => new StdBool(MlirContext.Current.NextId()),
										_ => throw new InvalidOperationException($"Unsupported call result kind: {callOp.ResultKind}")
									};
								}
								var funcCall = new StdCallOp(callOp.Callee, newArgs, callResult);
								newBlock.AddOp(funcCall);
								if (callOp.Result != null && funcCall.Result != null) {
									valueMap[callOp.Result] = funcCall.Result;
								}
								break;
							}
						case MaxonReturnOp retOp: {
								StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
								newBlock.AddOp(new StdReturnOp(newRetVal));
								break;
							}
						default:
							throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
					}
				}
			}
			result.AddFunction(newFunc);
		}
		return result;
	}

	private static void ConvertBinOp(MaxonBinOp binOp, MlirBlock<StandardOp> block, Dictionary<MaxonValue, StdValue> valueMap) {
		switch (binOp.Operator) {
			case MaxonBinOperator.Add: {
					switch (binOp.OperandKind) {
						case MaxonValueKind.Integer: {
								var lhs = (StdI64)valueMap[binOp.Lhs];
								var rhs = (StdI64)valueMap[binOp.Rhs];
								var newAdd = new StdAddI64Op(lhs, rhs);
								block.AddOp(newAdd);
								valueMap[binOp.Result] = newAdd.Result;
								break;
							}
						default:
							throw new InvalidOperationException($"Unsupported binop add kind: {binOp.OperandKind}");
					}
					break;
				}
			case MaxonBinOperator.Sub: {
					switch (binOp.OperandKind) {
						case MaxonValueKind.Integer: {
								var lhs = (StdI64)valueMap[binOp.Lhs];
								var rhs = (StdI64)valueMap[binOp.Rhs];
								var newSub = new StdSubI64Op(lhs, rhs);
								block.AddOp(newSub);
								valueMap[binOp.Result] = newSub.Result;
								break;
							}
						default:
							throw new InvalidOperationException($"Unsupported binop sub kind: {binOp.OperandKind}");
					}
					break;
				}
			case MaxonBinOperator.Eq: {
					switch (binOp.OperandKind) {
						case MaxonValueKind.Float: {
								var lhs = (StdF64)valueMap[binOp.Lhs];
								var rhs = (StdF64)valueMap[binOp.Rhs];
								var newCmp = new StdCmpF64Op("eq", lhs, rhs);
								block.AddOp(newCmp);
								valueMap[binOp.Result] = newCmp.Result;
								break;
							}
						default:
							throw new InvalidOperationException($"Unsupported binop eq kind: {binOp.OperandKind}");
					}
					break;
				}
			default:
				throw new InvalidOperationException($"Unsupported binop operator: {binOp.Operator}");
		}
	}
}
